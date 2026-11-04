using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Graphics;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Import;

/// <summary>
/// Every chunk (ADR 0035): a record per cell, with the map set's entry for it (<see cref="WorldImporter.BuildCells"/>) and its
/// Borg9's map objects, grid and drawn neighbours, a file each under <c>records/chunks/&lt;set&gt;/</c> (ADR 0036); and each Borg9's
/// collision, an asset the chunks copied from it share.
/// </summary>
internal static class MapImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Chunks and collision");
        var chunks = ctx.Chunks;
        var maps = chunks.Select(c => c.Collision).Distinct().Order().ToDictionary(i => i, i => Borg9Map.Parse(ctx.Archive.Read(i)));
        MapCorrections.Apply(chunks, maps, ctx.Options);
        var cells = WorldImporter.BuildCells(ctx);
        var records = BuildChunks(ctx.Archive, chunks, cells, maps.ToDictionary(m => m.Key, m => m.Value.Objects), [.. ctx.Db.Loot.Select(l => l.Id)],
            maps.ToDictionary(m => m.Key, m => Grid(m.Value)), maps.ToDictionary(m => m.Key, m => m.Value.DrawnNeighbours),
            CombatImporter.BattleRules(ctx.Engine), ctx.Scripts);
        foreach (var chunk in records)
            ctx.WriteRecords($"chunks/{chunk.Id.Key}.json", chunk);
        foreach (var (index, map) in maps)
        {
            var chunk = chunks.First(c => c.Collision == index);
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Collision, index), "glb", CollisionGltf.Export([(chunk, map)], placeInSet: false));
        }
    }

    /// <summary>The ID of the chunk in a map set's cell (<see cref="ChunkRecord.IdOf(int, int, int)"/>).</summary>
    internal static RecordId ChunkId(MapSet set, int row, int column) => ChunkRecord.IdOf((int)set, row, column);

    /// <summary>The ID of one of the original's chunks.</summary>
    internal static RecordId ChunkId(MapChunk chunk) => ChunkId(chunk.Set, chunk.Row, chunk.Column);

    /// <summary>
    /// Every cell's chunk with its Borg9's objects, but the code objects. A Borg9 shared between cells is copied into each, so each
    /// object has one ID (<c>aidyn:mapobject/&lt;set&gt;/&lt;name&gt;/&lt;index&gt;</c>). References to other objects become their IDs:
    /// links and wander nodes in the same chunk, a camera's reference point in its own, and a teleporter's arrival and camera in its
    /// destination, found by ID as the original does. A reference point that isn't there is left out.
    /// </summary>
    /// <param name="chunks">The original's chunks, which teleporters and script objects name.</param>
    /// <param name="cells">Every cell's chunk without its objects, with the Borg9 it takes them from (<see cref="WorldImporter.BuildCells"/>).</param>
    /// <param name="lootTables">ChestDB's IDs, in order.</param>
    /// <param name="grids">Each Borg9's grid of the objects its cells list, if it's known.</param>
    /// <param name="neighbours">Each Borg9's neighbours drawn whatever the view (<see cref="Borg9Map.DrawnNeighbours"/>), if known.</param>
    /// <param name="rules">The battles' own rules by their encounter flag (<see cref="CombatImporter.BattleRules"/>), if known.</param>
    /// <param name="scripts">The scripts, by which an npc's battle and its stage are found, if known.</param>
    internal static List<ChunkRecord> BuildChunks(BorgArchive archive, IReadOnlyList<MapChunk> chunks,
        IReadOnlyList<(ChunkRecord Chunk, int Borg)> cells,
        IReadOnlyDictionary<int, IReadOnlyList<VoxelObject>> maps, IReadOnlyList<ItemId> lootTables,
        IReadOnlyDictionary<int, ObjectGrid>? grids = null, IReadOnlyDictionary<int, byte>? neighbours = null,
        IReadOnlyDictionary<int, BattleRules>? rules = null, IReadOnlyList<ScriptRecord>? scripts = null)
    {
        var types = archive.Listings.ToDictionary(l => l.Index, l => l.Type);
        var chunkMaps = chunks.ToDictionary(c => (c.Set, c.Row, c.Column), c => c.Collision);
        var empty = new Dictionary<int, bool>();
        bool Empty(int borg)
        {
            if (types.GetValueOrDefault(borg) != BorgType.Scene)
                return false;
            if (!empty.TryGetValue(borg, out var none))
            {
                var scene = Borg5Scene.Parse(archive.Read(borg));
                var models = scene.Models.Select(m => Borg2Geometry.Parse(archive.Read(m))).ToList();
                empty[borg] = none = !scene.Draws.Any(d => SceneGltf.IsDrawn(models[d.Model], d.DisplayList));
            }
            return none;
        }
        var scriptsById = (scripts ?? []).ToDictionary(s => s.Id);
        var context = new Context(types, chunkMaps, maps, lootTables, Empty, rules ?? new Dictionary<int, BattleRules>(), scriptsById);
        var built = cells.Select(c => c.Chunk with
        {
            Objects = BuildObjects(c.Chunk.Id, c.Borg, maps[c.Borg], context),
            Grid = grids?.GetValueOrDefault(c.Borg),
            DrawnNeighbours = neighbours?.GetValueOrDefault(c.Borg) is > 0 and var bits ? (Neighbours)bits : null,
        }).ToList();
        return scripts is null ? built : [.. built.Select(c => Staged(c, built, scriptsById))];
    }

    /// <summary>What building a chunk's objects reads.</summary>
    private sealed record Context(
        IReadOnlyDictionary<int, BorgType> Types,
        IReadOnlyDictionary<(MapSet, int, int), int> ChunkMaps,
        IReadOnlyDictionary<int, IReadOnlyList<VoxelObject>> Maps,
        IReadOnlyList<ItemId> LootTables,
        Func<int, bool> Empty,
        IReadOnlyDictionary<int, BattleRules> Rules,
        IReadOnlyDictionary<RecordId, ScriptRecord> Scripts);

    /// <summary>
    /// <c>BORG5_OrianaLight</c>, the billboard of Oriana's path lights. <c>RenderVoxelScenes</c> (<c>0x8000c67c</c> in 1.0)
    /// puts out a scene object whose second model is this scene once only its first is drawn, comparing the index
    /// as a literal (<c>0x8000cda4</c>), which the importer carries as <see cref="SceneObject.GoesOut"/>.
    /// </summary>
    internal const int OrianaLight = 0x374A;

    /// <summary>
    /// A Borg9's grid as its cells list objects, without the code objects and those only there without the Expansion Pak, which
    /// the records leave out.
    /// </summary>
    private static ObjectGrid Grid(Borg9Map map)
    {
        var code = map.Objects.Where(o => o.Type == VoxelType.Code || o.Flags.HasFlag(VoxelFlags.NoExpansionPak)).Select(o => o.Index).ToHashSet();
        return new ObjectGrid(new System.Numerics.Vector2(map.Origin.X, map.Origin.Z), map.CellSize, map.Columns, map.Rows,
            [.. map.Cells.Select(c => (IReadOnlyList<int>)[.. c.Objects.Where(i => !code.Contains(i))])]);
    }

    /// <summary>Whether a script starts a battle: an effect of one of its nodes is <see cref="ScriptOp.BattleStart"/>.</summary>
    internal static bool Battles(ScriptRecord script) => script.Nodes.Any(n => n.Effect is ScriptOp.BattleStart);

    private static List<MapObject> BuildObjects(RecordId chunkId, int index, IReadOnlyList<VoxelObject> objects, Context ctx)
    {
        var types = ctx.Types;
        RecordId Own(int i, VoxelType type, string what) =>
            i < objects.Count && objects[i].Type == type ? ChunkRecord.ObjectId(chunkId, i)
            : throw new InvalidDataException($"Borg9 {index}: {what} {i} isn't a {type}.");
        RecordId Borg(BorgType type, int borg, string recordType) =>
            types.GetValueOrDefault(borg) == type ? RecordId.Hex(recordType, borg)
            : throw new InvalidDataException($"Borg9 {index}: Borg {borg} isn't a {type}.");
        RecordId Db(ItemId item) => DbRecordIds.Required(item, $"Borg9 {index}");
        RecordId? OptionalDb(ItemId item) => item.IsNone ? null : Db(item);
        // The game finds a loot table by GetIDIndex, which for a potion's ID is its low byte, so
        // a potion in a loot field names the table at that position. Two containers do this.
        RecordId? Loot(ItemId loot) => loot.Type switch
        {
            DbType.None => null,
            DbType.Loot => Db(loot),
            DbType.Potion when loot.Index < ctx.LootTables.Count => Db(ctx.LootTables[loot.Index]),
            _ => throw new InvalidDataException($"Borg9 {index}: loot {loot} isn't a loot table."),
        };
        RecordId? OptionalFlag(int flag) => flag == 0 ? null : DbRecordIds.Flag(flag);
        BattleRules? Rules(int flag) => flag != 0 && ctx.Rules.TryGetValue(flag, out var rules) ? rules : null;
        int ChunkMap((MapSet Set, int Row, int Column) c) => ctx.ChunkMaps.TryGetValue(c, out var m) ? m
            : throw new InvalidDataException($"Borg9 {index}: no chunk {c.Set} {c.Row},{c.Column}.");
        RecordId Chunk((MapSet Set, int Row, int Column) c)
        {
            ChunkMap(c);
            return ChunkId(c.Set, c.Row, c.Column);
        }
        // The first reference point with the ID in the chunk, as FindReferncePoint finds it. A few are missing.
        RecordId? RefPoint((MapSet Set, int Row, int Column) c, int refPoint) =>
            ctx.Maps[ChunkMap(c)].FirstOrDefault(o => o.Data is ReferencePointData r && r.Id == refPoint) is { } found
                ? ChunkRecord.ObjectId(Chunk(c), found.Index) : null;
        RecordId? OwnRefPoint(int refPoint) =>
            objects.FirstOrDefault(o => o.Data is ReferencePointData r && r.Id == refPoint) is { } found ? ChunkRecord.ObjectId(chunkId, found.Index) : null;

        SceneModel Model(SceneData scene, int i) => new(
            scene.SceneFlags.HasFlag(RomSceneObjectFlags.Actor) && types.GetValueOrDefault(scene.Models[i].Borg) == BorgType.Actor
                ? RecordId.Hex(RecordTypes.Actor, scene.Models[i].Borg) : RecordId.Hex(RecordTypes.Scene, scene.Models[i].Borg),
            scene.Models[i].DrawDistance) { Empty = ctx.Empty(scene.Models[i].Borg) };

        var records = new List<MapObject>();
        foreach (var o in objects)
        {
            // The port always plays as with the Expansion Pak, so the four objects only there without it (a sound in 0x0E96 and
            // three scenes in 0x11AF, which nothing links to) are left out.
            if (o.Type == VoxelType.Code || o.Flags.HasFlag(VoxelFlags.NoExpansionPak))
                continue;
            var visible = Gate(o.Visible);
            var enabled = Gate(o.Enabled);
            var used = o.FlagB == 0 ? null : new FlagWrite(DbRecordIds.Flag(o.FlagB), o.Flags.HasFlag(VoxelFlags.ClearFlagBOnUse));
            MapObject record = o.Data switch
            {
                SceneData scene => new SceneObject(o.Index, o.Position, o.Radius, visible,
                    [.. Enumerable.Range(0, Math.Min(scene.Lods, scene.Models.Count)).Select(i => Model(scene, i))],
                    scene.Rotation, scene.Scale, scene.Tint,
                    (SceneObjectFlags)((int)scene.SceneFlags & ~(int)(RomSceneObjectFlags.Actor | RomSceneObjectFlags.Unknown8)))
                {
                    GoesOut = scene.Models.Count > 1 && scene.Models[1].Borg == OrianaLight,
                    // A container's model: one level of detail, then what it shows opened and destroyed.
                    Opened = scene.Models.Count > scene.Lods ? Model(scene, scene.Lods) : null,
                    Destroyed = scene.Models.Count > scene.Lods + 1 ? Model(scene, scene.Lods + 1) : null,
                },
                // Herb, spice, gem and custom containers are gated as tripwires are.
                ContainerData c => c.Kind > (int)ContainerKind.Custom
                    ? throw new InvalidDataException($"Borg9 {index}: container {o.Index} is kind {c.Kind}.")
                    : new ContainerObject(o.Index, o.Position, o.Radius, c.Kind >= (int)ContainerKind.Herb ? visible : null,
                        (ContainerKind)c.Kind, OptionalFlag(c.OpenFlag), OptionalFlag(c.ExplodeFlag), c.LockLevel, c.TrapLevel,
                        Loot(c.Loot), c.Gold, OptionalDb(c.Item), c.Reach)
                    {
                        Enabled = c.Kind >= (int)ContainerKind.Herb ? enabled : null,
                        Used = used,
                    },
                LightData l => new LightObject(o.Index, o.Position, o.Radius, visible, l.From, l.To,
                    l.Kind, l.Speed, o.Flags.HasFlag(VoxelFlags.Occludable), o.Flags.HasFlag(VoxelFlags.FacesCamera)),
                AudioData a => new SoundObject(o.Index, o.Position, o.Radius, visible, Borg(BorgType.Sequence, a.Sequence, RecordTypes.Sequence),
                    a.Volume, a.MinWait, a.MaxWait, a.AudioFlags),
                WanderNodeData w => new WanderNodeObject(o.Index, o.Position, o.Radius, visible, w.Radius, w.Scatter, w.Chance, w.Wait,
                    w.WanderFlags, [.. w.Next.Select(n => Own(n, VoxelType.WanderNode, "wander node"))]),
                // Without a script it fights: winning sets its flag B, which is the battle's encounter flag.
                MonsterPartyData m when m.Dialogue == 0 => new EncounterObject(o.Index, o.Position, o.Radius, null, Db(m.Entity),
                    Own(m.WanderNode, VoxelType.WanderNode, "home node"), m.PartyFlags,
                    [.. m.Enemies.Select(e => new PartyEnemy(Db(e.Enemy), e.Min, e.Max))], OptionalDb(m.Loot), m.MinSize, m.MaxSize)
                {
                    Enabled = enabled,
                    // Beating it always sets the flag.
                    Used = used is null ? null : used with { Clear = false },
                    BattleRules = Rules(o.FlagB),
                },
                MonsterPartyData m => Npc(o, m, enabled, used),
                ReferencePointData r => new ReferencePointObject(o.Index, o.Position, o.Radius, visible, r.Id, r.Name,
                    r.Facing == System.Numerics.Vector3.Zero ? null : r.Facing),
                TeleporterData t => Teleporter(o, t, visible, enabled),
                CameraData c => new CameraObject(o.Index, o.Position, o.Radius, visible,
                    c.RefPoint == 0 ? null : OwnRefPoint(c.RefPoint), c.CameraFlags, Ticks: c.Values.Z, Pitch: c.Values.X, Distance: c.Values.Y),
                DialogueData d => new ScriptObject(o.Index, o.Position, o.Radius, null,
                    Borg(BorgType.Dialogue, d.Script, RecordTypes.Script), d.Map.Row == 0 ? null : Chunk(d.Map), d.RefPoint, d.Name)
                {
                    Enabled = enabled,
                    Tangible = o.Flags.HasFlag(VoxelFlags.Tangible),
                },
                TriggerData t => new TripwireObject(o.Index, o.Position, o.Radius, visible, t.Kind switch
                {
                    TriggerKind.SetFlag => new SetFlagAction(DbRecordIds.Flag(t.A)),
                    TriggerKind.Animate => new AnimateAction(Requests.Id(t.A)),
                    TriggerKind.ChangeCollision => new ChangeCollisionAction(t.A, t.B, t.C),
                    // Kinds 0, 3 and 5 do nothing in 1.0, and no object has one.
                    _ => throw new InvalidDataException($"Borg9 {index}: trigger {o.Index} is kind {t.Kind}, which does nothing."),
                }) with
                {
                    Enabled = enabled,
                    Used = used,
                    Tangible = o.Flags.HasFlag(VoxelFlags.Tangible),
                },
                null when o.Type == VoxelType.SavePoint => new SavePointObject(o.Index, o.Position, o.Radius, null),
                _ => throw new InvalidDataException($"Borg9 {index}: object {o.Index} is a {o.Type} with no data."),
            };
            // Both link slots are always the same, and the game only follows the first.
            var link = o.Links[0];
            if (link >= objects.Count || link >= 0 && objects[link].Type == VoxelType.Code || o.Links[1] != link)
                throw new InvalidDataException($"Borg9 {index}: object {o.Index} links to {o.Links[0]} and {o.Links[1]}.");
            records.Add(record with
            {
                Reachable = o.Flags.HasFlag(VoxelFlags.Reachable),
                Link = link < 0 ? null : ChunkRecord.ObjectId(chunkId, link),
            });
        }
        return records;

        // A talker. Its enemies, loot and size are what its script's battle fights (the original fights the party, not the script's
        // list), so they're left out where the script starts no battle and the list names only the talker, a stand-in.
        NpcObject Npc(VoxelObject o, MonsterPartyData m, FlagGate? enabled, FlagWrite? used)
        {
            var script = Borg(BorgType.Dialogue, m.Dialogue, RecordTypes.Script);
            var entity = Db(m.Entity);
            var enemies = m.Enemies.Select(e => new PartyEnemy(Db(e.Enemy), e.Min, e.Max)).ToList();
            var fights = !ctx.Scripts.TryGetValue(script, out var record) || Battles(record);
            var kept = fights || enemies.Any(e => e.Entity != entity);
            return new NpcObject(o.Index, o.Position, o.Radius, null, entity, Own(m.WanderNode, VoxelType.WanderNode, "home node"),
                m.PartyFlags, script)
            {
                Enabled = enabled,
                // Talking to it always sets the flag.
                Used = used is null ? null : used with { Clear = false },
                Enemies = kept ? enemies : null,
                MinSize = kept ? m.MinSize : 0,
                MaxSize = kept ? m.MaxSize : 0,
                Loot = OptionalDb(m.Loot),
                BattleFlag = OptionalFlag(o.FlagC),
                BattleRules = Rules(o.FlagC),
            };
        }

        // A teleporter: its destination is its arrival's chunk, so it names one only where it has no arrival.
        TeleporterObject Teleporter(VoxelObject o, TeleporterData t, FlagGate? visible, FlagGate? enabled)
        {
            var arrival = t.RefPoint == TeleporterData.OwnPosition ? null : RefPoint(t.Destination, t.RefPoint);
            return new TeleporterObject(o.Index, o.Position, o.Radius, visible, arrival is null ? Chunk(t.Destination) : null,
                arrival, t.CameraRefPoint == 0 ? null : RefPoint(t.Destination, t.CameraRefPoint),
                t.LockLevel, OptionalFlag(t.LockFlag), OptionalFlag(t.UnlockFlag), t.TrapLevel, OptionalFlag(t.TrapFlag),
                OptionalFlag(t.DisarmFlag), t.SecretFlag is 0 or 1 ? null : DbRecordIds.Flag(t.SecretFlag), t.SecretLevel, t.Name)
            {
                Enabled = enabled,
                Tangible = o.Flags.HasFlag(VoxelFlags.Tangible),
            };
        }
    }

    /// <summary>The marks a script names: its camera moves' onto marks, and its actors' spawns, facings and walks.</summary>
    internal static IEnumerable<int> MarksNamed(ScriptRecord script) =>
        script.Nodes.SelectMany(n => (n.Setup ?? []).Append(n.Effect).OfType<ScriptOp>()).Select(op => op switch
        {
            ScriptOp.CameraMove { Mark: { } mark } => mark,
            ScriptOp.ActorSpawn a => a.Mark,
            ScriptOp.ActorFace a => a.Mark,
            ScriptOp.ActorWalkTo a => a.Mark,
            ScriptOp.ActorRunTo a => a.Mark,
            _ => (int?)null,
        }).OfType<int>().Distinct();

    /// <summary>
    /// The chunk with each npc's <see cref="NpcObject.Stage"/>: the port's fix for a talker's cut-scene that names a mark marks 0 doesn't
    /// find in its chunk, which 1.0 starts from there and crashes on. Where a script object in the npc's chunk plays the same script and
    /// its marks find every mark in its stage, the first such is the npc's stage.
    /// </summary>
    private static ChunkRecord Staged(ChunkRecord chunk, IReadOnlyList<ChunkRecord> all, IReadOnlyDictionary<RecordId, ScriptRecord> scripts)
    {
        if (!chunk.Objects.OfType<NpcObject>().Any())
            return chunk;
        var byId = all.ToDictionary(c => c.Id);
        bool Finds(ChunkRecord? stage, int marks, IEnumerable<int> named)
        {
            var points = stage?.Objects.OfType<ReferencePointObject>().Select(p => p.Mark).ToHashSet() ?? [];
            return named.All(m => points.Contains(marks + m));
        }
        var objects = chunk.Objects.Select(o =>
        {
            // A silent npc starts nothing when it's used, so it needs no stage.
            if (o is not NpcObject npc || (npc.Flags & PartyFlags.Silent) != 0 || !scripts.TryGetValue(npc.Script, out var script)
                || script.Mode != ScriptMode.Cutscene)
                return o;
            var named = MarksNamed(script).ToList();
            if (Finds(chunk, 0, named))
                return o;
            var stage = chunk.Objects.OfType<ScriptObject>().Where(s => s.Plays == npc.Script && s.Stage is not null)
                .FirstOrDefault(s => Finds(byId.GetValueOrDefault(s.Stage!.Value), s.Marks, named));
            return stage is null ? o : npc with { Stage = chunk.ObjectId(stage.Index) };
        }).ToList();
        return chunk with { Objects = objects };
    }

    /// <summary>A gate, or none where it's flag 0 inverted, which always holds (flag 0 always reads clear).</summary>
    private static FlagGate? Gate(RomFlagGate gate) =>
        gate is { Flag: 0, Inverted: true } ? null : new(DbRecordIds.Flag(gate.Flag), gate.Inverted);
}
