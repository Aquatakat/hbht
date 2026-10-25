using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Graphics;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Import;

/// <summary>
/// The minimaps, and every chunk's map objects and collision.
/// </summary>
internal static class MapImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Minimaps");
        ctx.WriteRecords("minimaps.json", MinimapImporter.Build(Minimaps.Read(ctx.Rom)));

        ctx.Report("Maps and collision");
        var chunks = ctx.Chunks;
        var maps = chunks.Select(c => c.Collision).Distinct().Order().ToDictionary(i => i, i => Borg9Map.Parse(ctx.Archive.Read(i)));
        MapCorrections.Apply(chunks, maps);
        // A file per map set, since each map is in one, such as records/maps/Overworld.json.
        var mapRecords = BuildMaps(ctx.Archive, chunks, maps.ToDictionary(m => m.Key, m => m.Value.Objects), [.. ctx.Db.Loot.Select(l => l.Id)],
            maps.ToDictionary(m => m.Key, m => Grid(m.Value)), maps.ToDictionary(m => m.Key, m => m.Value.DrawnNeighbours));
        var setOf = chunks.ToLookup(c => RecordId.Hex(RecordTypes.Map, c.Collision), c => c.Set);
        foreach (var set in mapRecords.GroupBy(m => setOf[m.Id].Distinct().Single()))
            ctx.WriteRecords(Path.Combine("maps", set.Key + ".json"), set.ToList());
        foreach (var (index, map) in maps)
        {
            var chunk = chunks.First(c => c.Collision == index);
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Map, index), "glb", CollisionGltf.Export([(chunk, map)], placeInSet: false));
        }
    }

    /// <summary>
    /// Every map's objects, but the code objects. References to other objects become voxel IDs:
    /// links and wander nodes in the same map, a camera's reference point in its own, and a
    /// teleporter's arrival and camera in its destination, found by ID as the original does. A
    /// reference point that isn't there is left out.
    /// </summary>
    /// <param name="lootTables">ChestDB's IDs, in order.</param>
    /// <param name="grids">Each map's grid of the objects its cells list, if it's known.</param>
    /// <param name="neighbours">Each map's neighbours drawn whatever the view (<see cref="Borg9Map.DrawnNeighbours"/>), if known.</param>
    internal static List<MapRecord> BuildMaps(BorgArchive archive, IReadOnlyList<MapChunk> chunks,
        IReadOnlyDictionary<int, IReadOnlyList<VoxelObject>> maps, IReadOnlyList<ItemId> lootTables,
        IReadOnlyDictionary<int, ObjectGrid>? grids = null, IReadOnlyDictionary<int, byte>? neighbours = null)
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
        return [.. maps.OrderBy(m => m.Key).Select(m => BuildMap(m.Key, m.Value, types, chunkMaps, maps, lootTables, Empty) with
        {
            Grid = grids?.GetValueOrDefault(m.Key),
            DrawnNeighbours = neighbours?.GetValueOrDefault(m.Key) is > 0 and var bits ? (Neighbours)bits : null,
        })];
    }

    /// <summary>
    /// <c>BORG5_OrianaLight</c>, the billboard of Oriana's path lights. <c>RenderVoxelScenes</c> (<c>0x8000c67c</c> in 1.0)
    /// puts out a scene object whose second model is this scene once only its first is drawn, comparing the index
    /// as a literal (<c>0x8000cda4</c>), which the importer carries as <see cref="SceneObject.GoesOut"/>.
    /// </summary>
    internal const int OrianaLight = 0x374A;

    /// <summary>A Borg9's grid as its cells list objects, without the code objects, which the records leave out.</summary>
    private static ObjectGrid Grid(Borg9Map map)
    {
        var code = map.Objects.Where(o => o.Type == VoxelType.Code).Select(o => o.Index).ToHashSet();
        return new ObjectGrid(new System.Numerics.Vector2(map.Origin.X, map.Origin.Z), map.CellSize, map.Columns, map.Rows,
            [.. map.Cells.Select(c => (IReadOnlyList<int>)[.. c.Objects.Where(i => !code.Contains(i))])]);
    }

    private static MapRecord BuildMap(int index, IReadOnlyList<VoxelObject> objects, IReadOnlyDictionary<int, BorgType> types,
        IReadOnlyDictionary<(MapSet, int, int), int> chunkMaps, IReadOnlyDictionary<int, IReadOnlyList<VoxelObject>> maps,
        IReadOnlyList<ItemId> lootTables, Func<int, bool> empty)
    {
        var id = RecordId.Hex(RecordTypes.Map, index);
        RecordId Voxel(int map, int i) => MapRecord.VoxelId(RecordId.Hex(RecordTypes.Map, map), i);
        RecordId Own(int i, VoxelType type, string what) =>
            i < objects.Count && objects[i].Type == type ? Voxel(index, i)
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
            DbType.Potion when loot.Index < lootTables.Count => Db(lootTables[loot.Index]),
            _ => throw new InvalidDataException($"Borg9 {index}: loot {loot} isn't a loot table."),
        };
        RecordId? OptionalFlag(int flag) => flag == 0 ? null : DbRecordIds.Flag(flag);
        int ChunkMap((MapSet Set, int Row, int Column) c) => chunkMaps.TryGetValue(c, out var m) ? m
            : throw new InvalidDataException($"Borg9 {index}: no chunk {c.Set} {c.Row},{c.Column}.");
        ChunkRef Chunk((MapSet Set, int Row, int Column) c)
        {
            ChunkMap(c);
            return new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, (int)c.Set), c.Row, c.Column);
        }
        // The first reference point with the ID, as FindReferncePoint finds it. A few are missing.
        RecordId? RefPoint(int map, int refPoint) =>
            maps[map].FirstOrDefault(o => o.Data is ReferencePointData r && r.Id == refPoint) is { } found ? Voxel(map, found.Index) : null;

        var records = new List<MapObject>();
        foreach (var o in objects)
        {
            if (o.Type == VoxelType.Code)
                continue;
            var pak = o.Flags.HasFlag(VoxelFlags.ExpansionPak) ? PakVariant.With
                : o.Flags.HasFlag(VoxelFlags.NoExpansionPak) ? PakVariant.Without
                : PakVariant.Any;
            var visible = Gate(o.Visible);
            var enabled = Gate(o.Enabled);
            var used = o.FlagB == 0 ? null : new FlagWrite(DbRecordIds.Flag(o.FlagB), o.Flags.HasFlag(VoxelFlags.ClearFlagBOnUse));
            MapObject record = o.Data switch
            {
                SceneData scene => new SceneObject(o.Index, o.Position, o.Radius, pak, visible,
                    [
                        .. scene.Models.Select(m => new SceneModel(
                            scene.SceneFlags.HasFlag(RomSceneObjectFlags.Actor) && types.GetValueOrDefault(m.Borg) == BorgType.Actor
                                ? RecordId.Hex(RecordTypes.Actor, m.Borg) : RecordId.Hex(RecordTypes.Scene, m.Borg),
                            m.DrawDistance) { Empty = empty(m.Borg) }),
                    ],
                    scene.Lods, scene.Rotation, scene.Scale, scene.Tint,
                    (SceneObjectFlags)((int)scene.SceneFlags & ~(int)RomSceneObjectFlags.Actor))
                {
                    GoesOut = scene.Models.Count > 1 && scene.Models[1].Borg == OrianaLight,
                },
                // Herb, spice, gem and custom containers are gated as triggers are.
                ContainerData c => c.Kind > (int)ContainerKind.Custom
                    ? throw new InvalidDataException($"Borg9 {index}: container {o.Index} is kind {c.Kind}.")
                    : new ContainerObject(o.Index, o.Position, o.Radius, pak, c.Kind >= (int)ContainerKind.Herb ? visible : null,
                        (ContainerKind)c.Kind, OptionalFlag(c.OpenFlag), OptionalFlag(c.ExplodeFlag), c.LockLevel, c.TrapLevel,
                        Loot(c.Loot), c.Gold, OptionalDb(c.Item), c.Reach)
                    {
                        Enabled = c.Kind >= (int)ContainerKind.Herb ? enabled : null,
                        Used = used,
                    },
                LightData l => new LightObject(o.Index, o.Position, o.Radius, pak, visible, l.From, l.To,
                    l.Kind, l.Speed, o.Flags.HasFlag(VoxelFlags.Occludable), o.Flags.HasFlag(VoxelFlags.FacesCamera)),
                AudioData a => new SoundObject(o.Index, o.Position, o.Radius, pak, visible, Borg(BorgType.Sequence, a.Sequence, RecordTypes.Sequence),
                    a.Volume, a.MinWait, a.MaxWait, a.AudioFlags),
                WanderNodeData w => new WanderNodeObject(o.Index, o.Position, o.Radius, pak, visible, w.Radius, w.Scatter, w.Chance, w.Wait,
                    w.WanderFlags, [.. w.Next.Select(n => Own(n, VoxelType.WanderNode, "wander node"))]),
                MonsterPartyData m => new MonsterPartyObject(o.Index, o.Position, o.Radius, pak, null,
                    [.. m.Enemies.Select(e => new PartyEnemy(Db(e.Enemy), e.Min, e.Max))], Db(m.Entity), OptionalDb(m.Loot),
                    m.MinSize, m.MaxSize, Own(m.WanderNode, VoxelType.WanderNode, "home node"), m.PartyFlags,
                    m.Dialogue == 0 ? null : Borg(BorgType.Dialogue, m.Dialogue, RecordTypes.Dialogue),
                    m.Dialogue == 0 ? null : OptionalFlag(o.FlagC))
                {
                    Enabled = enabled,
                    // Talking to it or beating it always sets the flag.
                    Used = used is null ? null : used with { Clear = false },
                },
                ReferencePointData r => new ReferencePointObject(o.Index, o.Position, o.Radius, pak, visible, r.Id, r.Name,
                    r.Facing == System.Numerics.Vector3.Zero ? null : r.Facing),
                TeleporterData t => new TeleporterObject(o.Index, o.Position, o.Radius, pak, visible, Chunk(t.Destination),
                    t.RefPoint == TeleporterData.OwnPosition ? null : RefPoint(ChunkMap(t.Destination), t.RefPoint),
                    t.CameraRefPoint == 0 ? null : RefPoint(ChunkMap(t.Destination), t.CameraRefPoint),
                    t.LockLevel, OptionalFlag(t.LockFlag), OptionalFlag(t.UnlockFlag), t.TrapLevel, OptionalFlag(t.TrapFlag),
                    OptionalFlag(t.DisarmFlag), t.SecretFlag is 0 or 1 ? null : DbRecordIds.Flag(t.SecretFlag), t.SecretLevel, t.Name)
                {
                    Enabled = enabled,
                    Tangible = o.Flags.HasFlag(VoxelFlags.Tangible),
                },
                CameraData c => new CameraObject(o.Index, o.Position, o.Radius, pak, visible,
                    c.RefPoint == 0 ? null : RefPoint(index, c.RefPoint), c.CameraFlags, c.Values),
                DialogueData d => new DialogueObject(o.Index, o.Position, o.Radius, pak, null,
                    Borg(BorgType.Dialogue, d.Script, RecordTypes.Dialogue), d.Map.Row == 0 ? null : Chunk(d.Map), d.RefPoint, d.Name)
                {
                    Enabled = enabled,
                    Tangible = o.Flags.HasFlag(VoxelFlags.Tangible),
                },
                TriggerData t => t.Kind switch
                {
                    TriggerKind.SetFlag => new TriggerObject(o.Index, o.Position, o.Radius, pak, visible, TriggerKind.SetFlag, Flag: DbRecordIds.Flag(t.A)),
                    TriggerKind.Animate => new TriggerObject(o.Index, o.Position, o.Radius, pak, visible, TriggerKind.Animate, Request: t.A),
                    TriggerKind.ChangeCollision => new TriggerObject(o.Index, o.Position, o.Radius, pak, visible, TriggerKind.ChangeCollision,
                        Toggle: t.A, KeepFlags: t.B, AddFlags: t.C),
                    _ => new TriggerObject(o.Index, o.Position, o.Radius, pak, visible, t.Kind),
                } with
                {
                    Enabled = enabled,
                    Used = used,
                    Tangible = o.Flags.HasFlag(VoxelFlags.Tangible),
                },
                null when o.Type == VoxelType.SavePoint => new SavePointObject(o.Index, o.Position, o.Radius, pak, null),
                _ => throw new InvalidDataException($"Borg9 {index}: object {o.Index} is a {o.Type} with no data."),
            };
            // Both link slots are always the same, and the game only follows the first.
            var link = o.Links[0];
            if (link >= objects.Count || link >= 0 && objects[link].Type == VoxelType.Code || o.Links[1] != link)
                throw new InvalidDataException($"Borg9 {index}: object {o.Index} links to {o.Links[0]} and {o.Links[1]}.");
            records.Add(record with
            {
                Reachable = o.Flags.HasFlag(VoxelFlags.Reachable),
                Link = link < 0 ? null : Voxel(index, link),
            });
        }
        return new MapRecord(id, records);
    }

    private static FlagGate Gate(RomFlagGate gate) => new(DbRecordIds.Flag(gate.Flag), gate.Inverted);
}
