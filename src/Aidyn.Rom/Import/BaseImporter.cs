using System.Text.Json;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;
using FlagGate = Aidyn.Plugins.Records.FlagGate;
using PluginSceneModel = Aidyn.Plugins.Records.SceneModel;
using PluginSceneObjectFlags = Aidyn.Plugins.Records.SceneObjectFlags;
using RomSceneObjectFlags = Aidyn.Rom.Maps.SceneObjectFlags;

namespace Aidyn.Rom.Import;

/// <summary>
/// Turns a ROM into <c>aidyn.base</c> (ADR 0001): records under <c>records/</c> and converted assets
/// under <c>assets/aidyn/</c>, keyed by record ID. The output is deterministic: the same ROM and
/// <see cref="BasePlugin.ImporterVersion"/> always give the same bytes.
/// </summary>
/// <remarks>
/// This is the slice M1 needs: map sets and their chunks, terrains, scene objects, actors, every
/// scene as glTF, every chunk's collision, and every texture and UI image as PNG for what the
/// engine draws itself.
/// Records for the rest of the content come with the tracks that use them.
/// </remarks>
public static class BaseImporter
{
    /// <summary>
    /// Alaron's actor (<c>BORG7_Alaron</c>) and the head scene the game draws on its locator 3
    /// (<c>some_borg5</c>, <c>Actor::ChangeAppearance</c>). Engine code holds both as literals; see
    /// the hardcoded-content audit.
    /// </summary>
    private const int AlaronActor = 0x25F9, AlaronHead = 0x25AA;

    /// <summary>
    /// Imports <paramref name="rom"/> into <paramref name="directory"/>, replacing what's there. It
    /// writes to a sibling folder first, so a failed import leaves the old one in place.
    /// </summary>
    public static void Import(RomImage rom, string directory, IProgress<string>? progress = null)
    {
        if (rom.Version.ResearchOnly)
            throw new UnsupportedRomException(rom.Sha1, rom.Header);

        directory = Path.GetFullPath(directory);
        var staging = directory + ".importing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var archive = BorgArchive.Open(rom);
        var chunks = MapChunks.Read(rom);

        WriteJson(Path.Combine(staging, PluginManifest.FileName), new PluginManifest(
            RecordId.BaseNamespace, "Aidyn Chronicles: The First Mage", "1.0.0",
            Import: new ImportInfo(BasePlugin.ImporterVersion, rom.Sha1, rom.Version.Name)));

        progress?.Report("Map sets");
        WriteJson(Path.Combine(staging, "records", "mapsets.json"), BuildMapSets(chunks));

        progress?.Report("Terrains");
        var gradients = Skies.ReadGradients(rom);
        WriteJson(Path.Combine(staging, "records", "terrains.json"), Enumerable.Range(0, Skies.TerrainCount).Select(t =>
        {
            var (sky, drawn) = Skies.ForTerrain(t);
            return new TerrainRecord(RecordId.Decimal(RecordTypes.Terrain, t), RecordId.Hex(RecordTypes.Image, gradients[sky]), drawn);
        }).ToList());

        progress?.Report("Flags");
        var atlas = QuestAtlas.Build(rom);
        WriteJson(Path.Combine(staging, "records", "flags.json"), BuildFlags(atlas));
        WriteJson(Path.Combine(staging, "records", "journal.json"), atlas.Journal
            .Select((e, i) => new JournalRecord(RecordId.Decimal(RecordTypes.Journal, i), Flag(e.Flag), e.Text)).ToList());
        WriteJson(Path.Combine(staging, "records", "stateflags.json"), BuildStateFlags());

        progress?.Report("Names");
        foreach (var (file, records) in BuildNames(GameDb.Read(rom)))
            WriteJson(Path.Combine(staging, "records", file), records);

        progress?.Report("Scripts");
        WriteJson(Path.Combine(staging, "records", "dialogue.json"), BuildDialogues(archive, EngineTables.Read(rom)));

        progress?.Report("Sky");
        WriteJson(Path.Combine(staging, "records", "sky.json"), BuildSky(rom, archive));

        progress?.Report("Maps and collision");
        var maps = chunks.Select(c => c.Collision).Distinct().Order().ToList();
        var actors = archive.Listings.Where(l => l.Type == BorgType.Actor).Select(l => l.Index).ToHashSet();
        var mapRecords = new List<MapRecord>();
        foreach (var index in maps)
        {
            var map = Borg9Map.Parse(archive.Read(index));
            mapRecords.Add(BuildMap(index, map, actors));
            var chunk = chunks.First(c => c.Collision == index);
            WriteAsset(staging, RecordId.Hex(RecordTypes.Map, index), "glb", CollisionGltf.Export([(chunk, map)], placeInSet: false));
        }
        WriteJson(Path.Combine(staging, "records", "maps.json"), mapRecords);

        progress?.Report("Actors");
        var actorRecords = new List<ActorRecord>();
        foreach (var index in actors.Order())
        {
            var actor = Borg7Actor.Parse(archive.Read(index));
            var scene = Borg6Animation.Parse(archive.Read(actor.States[0].Animation)).Scene;
            actorRecords.Add(new ActorRecord(RecordId.Hex(RecordTypes.Actor, index), RecordId.Hex(RecordTypes.Scene, scene),
                actor.RootTransform, actor.InitialState,
                [.. actor.States.Select(s => new ActorState(RecordId.Hex(RecordTypes.Animation, s.Animation),
                    [.. s.Branches.Select(b => new ActorBranch(b.Request, b.Chance, b.Interrupts, b.State))]))],
                index == AlaronActor ? new ActorHead(RecordId.Hex(RecordTypes.Scene, AlaronHead), 3) : null));
        }
        WriteJson(Path.Combine(staging, "records", "actors.json"), actorRecords);

        progress?.Report("Scenes");
        var animations = archive.Listings.Where(l => l.Type == BorgType.Animation)
            .Select(l => (l.Index, Animation: Borg6Animation.Parse(archive.Read(l.Index))))
            .ToLookup(a => a.Animation.Scene);
        var scenes = archive.Listings.Where(l => l.Type == BorgType.Scene).Select(l => l.Index).ToList();
        var done = 0;
        Parallel.ForEach(scenes, index =>
        {
            var scene = Borg5Scene.Parse(archive.Read(index));
            WriteAsset(staging, RecordId.Hex(RecordTypes.Scene, index), "glb",
                SceneGltf.Export(scene, archive.Read, index, animations[index].ToList()));
            if (Interlocked.Increment(ref done) % 200 == 0)
                progress?.Report($"Scenes {done}/{scenes.Count}");
        });

        // Scenes carry their own textures. These are for what the engine draws with a texture
        // alone, such as bodies' shadows and particles. Only the full-size level is kept.
        progress?.Report("Textures");
        var textures = archive.Listings.Where(l => l.Type == BorgType.Texture).Select(l => l.Index).ToList();
        Parallel.ForEach(textures, index =>
            WriteAsset(staging, RecordId.Hex(RecordTypes.Texture, index), "png", Png.Encode(Borg1Texture.Parse(archive.Read(index)).Decode())));

        // UI images, and the sky gradients the scenery's colours come from.
        progress?.Report("Images");
        var images = archive.Listings.Where(l => l.Type == BorgType.UiImage).Select(l => l.Index).ToList();
        Parallel.ForEach(images, index =>
            WriteAsset(staging, RecordId.Hex(RecordTypes.Image, index), "png", Png.Encode(Borg8Image.Parse(archive.Read(index)).Decode())));

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        Directory.Move(staging, directory);
    }

    private static List<MapSetRecord> BuildMapSets(IReadOnlyList<MapChunk> chunks) =>
    [
        .. Enum.GetValues<MapSet>().Select(set => new MapSetRecord(
            RecordId.Decimal(RecordTypes.MapSet, (int)set), set.ToString(), Maps.MapSets.CellSize(set),
            [
                .. chunks.Where(c => c.Set == set).Select(c => new ChunkEntry(c.Row, c.Column,
                    c.Scene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.Scene),
                    c.AltScene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.AltScene),
                    RecordId.Hex(RecordTypes.Map, c.Collision), c.Terrain)),
            ])),
    ];

    /// <summary>
    /// The sun, moon, stars, clouds and lens flare, with the Borg8 images engine code draws them with
    /// (<see cref="Skies"/> carries those as facts). Every image must be a UI image in the archive.
    /// </summary>
    private static SkyRecord BuildSky(RomImage rom, BorgArchive archive)
    {
        var uiImages = archive.Listings.Where(l => l.Type == BorgType.UiImage).Select(l => l.Index).ToHashSet();
        RecordId Image(int index) => uiImages.Contains(index)
            ? RecordId.Hex(RecordTypes.Image, index)
            : throw new InvalidDataException($"The sky's image 0x{index:X} isn't a Borg8 in {rom.Version.Name}.");
        static Plugins.Records.Rgba Colour(Maps.Rgba c) => new(c.R, c.G, c.B, c.A);

        var objects = Skies.ReadSkyObjects(rom);
        SkyBody Body(SkyObjectEntry o, int image, float? scale = null) =>
            new(Image(image), scale ?? o.Scale, o.Height, o.Azimuth, Colour(o.Color));
        var moon = Skies.MoonImages;
        return new SkyRecord(
            RecordId.Decimal(RecordTypes.Sky, 0),
            Body(objects.Single(o => o.Kind == SkyObjectKind.Sun), Skies.SunImage),
            Body(objects.Single(o => o.Kind == SkyObjectKind.Moon), moon.Full),
            new MoonImages(Image(moon.New), Image(moon.WaxingCrescent), Image(moon.WaningCrescent),
                Image(moon.FirstQuarter), Image(moon.ThirdQuarter), Image(moon.Full)),
            [.. objects.Where(o => o.Kind == SkyObjectKind.Star).Select(o => Body(o, Skies.StarImage, Skies.StarScale))],
            [.. Skies.CloudImages.Select(Image)],
            [.. Skies.ReadClouds(rom).Select(c => new Cloud(c.Image, c.X, c.Z, c.Alpha, c.Cover))],
            [.. Skies.FlareImages.Select(Image)],
            [.. Skies.ReadLensFlare(rom).Select(f => new FlarePiece(f.Image, f.Distance, f.Scale, Colour(f.Color)))]);
    }

    private static MapRecord BuildMap(int index, Borg9Map map, IReadOnlySet<int> actors)
    {
        var objects = new List<MapObject>();
        foreach (var o in map.Objects)
        {
            if (o is not { Type: VoxelType.Scene, Data: SceneData scene })
                continue;
            var pak = o.Flags.HasFlag(VoxelFlags.ExpansionPak) ? PakVariant.With
                : o.Flags.HasFlag(VoxelFlags.NoExpansionPak) ? PakVariant.Without
                : PakVariant.Any;
            var isActor = scene.SceneFlags.HasFlag(RomSceneObjectFlags.Actor);
            objects.Add(new SceneObject(o.Index, o.Position, o.Radius, pak, Gate(o.Visible),
                [
                    .. scene.Models.Select(m => new PluginSceneModel(
                        RecordId.Hex(isActor && actors.Contains(m.Borg) ? RecordTypes.Actor : RecordTypes.Scene, m.Borg),
                        m.DrawDistance)),
                ],
                scene.Lods, scene.Rotation, scene.Scale, new Plugins.Records.Rgba(scene.Tint.R, scene.Tint.G, scene.Tint.B, scene.Tint.A),
                (PluginSceneObjectFlags)((int)scene.SceneFlags & ~(int)RomSceneObjectFlags.Actor)));
        }
        return new MapRecord(RecordId.Hex(RecordTypes.Map, index), objects);
    }

    private static FlagGate Gate(Maps.FlagGate gate) => new(Flag(gate.Flag), gate.Inverted);

    private static RecordId Flag(int flag) => RecordId.Decimal(RecordTypes.Flag, flag);

    /// <summary>The flags the engine writes its state into (<see cref="EngineFlags"/>).</summary>
    internal static StateFlagsRecord BuildStateFlags() => new(
        RecordId.Decimal(RecordTypes.StateFlags, 0),
        Flags(EngineFlags.MoonPhase), Flags(EngineFlags.TimeOfDay), Flags(EngineFlags.DayOfSeason), Flags(EngineFlags.Season),
        Flags(EngineFlags.DayOfWeek), Flags(EngineFlags.Week), Flags(EngineFlags.Rain), Flags(EngineFlags.Terrain),
        Flag(EngineFlags.NewJournalEntry),
        Flag(EngineFlags.RandomSkillChecks));

    private static RecordId[] Flags(EngineFlagRange range) => [.. Enumerable.Range(range.First, range.Bits).Select(Flag)];

    /// <summary>
    /// Every flag of the <c>GameStateFunnel</c>, with the labels engine code gives them. Derived
    /// flags from <see cref="EngineFlags.JournalTriggers"/> on each add the journal entry of their
    /// position after it (<c>set_journalentry_flag</c>).
    /// </summary>
    internal static List<FlagRecord> BuildFlags(QuestAtlas atlas)
    {
        var funnel = atlas.Funnel;
        var records = new List<FlagRecord>(funnel.Flags.Count);
        for (var i = 0; i < funnel.Flags.Count; i++)
        {
            var flag = funnel.Flags[i];
            var label = atlas.Labels.GetValueOrDefault((ushort)i) ?? i switch { 0 => "Always clear", 1 => "Always set", _ => null };
            if (i < funnel.BitFlagCount)
            {
                if (flag.Kind != Db.FlagKind.Bit)
                    throw new InvalidDataException($"Flag {i} is below the derived flags, but it's {flag.Kind}.");
                records.Add(new FlagRecord(Flag(i), label, flag.InitialValue != 0));
                continue;
            }

            var kind = flag.Kind switch
            {
                Db.FlagKind.Logic => FlagFormulaKind.Logic,
                Db.FlagKind.Value => FlagFormulaKind.Value,
                Db.FlagKind.Count => FlagFormulaKind.Count,
                _ => throw new InvalidDataException($"Derived flag {i} is {flag.Kind}."),
            };
            var inputs = Enumerable.Range(flag.InputStart, flag.InputCount).Select(n => funnel.Inputs[n]).ToList();
            // An unused input still takes its bit, so leaving it out would move the others.
            if (flag.Op == Db.FlagOp.Invalid || inputs.Contains((ushort)0xFFFF) || inputs.Count > 16 || flag.InitialValue != 0)
                throw new InvalidDataException($"Derived flag {i} ({flag}) can't be written as a formula.");
            var formula = new FlagFormula(kind, (FlagOperation)flag.Op, flag.Operand, [.. inputs.Select(n => Flag(n))]);
            var entry = i - EngineFlags.JournalTriggers;
            RecordId? journal = entry < 0 ? null
                : entry < atlas.Journal.Count ? RecordId.Decimal(RecordTypes.Journal, entry)
                : throw new InvalidDataException($"Journal trigger {i} has no journal entry.");
            records.Add(new FlagRecord(Flag(i), label, false, formula, journal));
        }
        return records;
    }

    /// <summary>
    /// The names of the DB records scripts and tools name, by file: entities, dialogue entities,
    /// weapons, armor, spells and items. Records whose ID has no record type are left out.
    /// </summary>
    internal static List<(string File, List<DbRecord> Records)> BuildNames(GameDb db)
    {
        return
        [
            ("entities.json", Names(db.Entities.Select(e => (e.Id, e.Name)))),
            ("dialogentities.json", Names(db.DialogEntities.Select(e => (e.Id, e.Name)))),
            ("weapons.json", Names(db.Weapons.Select(w => (w.Id, w.Name)))),
            ("armor.json", Names(db.Armor.Select(a => (a.Id, a.Name)))),
            ("spells.json", Names(db.Spells.Select(s => (s.Id, s.Name)))),
            ("items.json", Names(db.Gear.Select(g => (g.Id, g.Name)))),
        ];

        static List<DbRecord> Names(IEnumerable<(ItemId Id, string Name)> records) =>
        [
            .. records.Select(r => (Id: DbRecordIds.For(r.Id), r.Name)).Where(r => r.Id is not null)
                .Select(r => new DbRecord(r.Id!.Value, r.Name)).DistinctBy(r => r.Id).OrderBy(r => r.Id.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Every Borg13 as a <see cref="DialogueRecord"/>, in archive order. Text is cleaned
    /// (<see cref="DialogueText"/>), the ops the game never reads are dropped, and operands that name
    /// a DB record, a flag or another script become record IDs. A node a Case links to gets its range.
    /// </summary>
    internal static List<DialogueRecord> BuildDialogues(BorgArchive archive, EngineTables tables)
    {
        var records = new List<DialogueRecord>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Dialogue))
        {
            var index = listing.Index;
            var script = Borg13Script.Parse(archive.Read(index));
            var mode = (DialogueMode)script.StartMode;
            var hasDoneFlag = mode is DialogueMode.Cutscene or DialogueMode.Backdrop or DialogueMode.Help;

            var cased = script.Nodes.Where(n => n.Kind == Borg13NodeKind.Case).SelectMany(n => n.Links).ToHashSet();
            var nodes = script.Nodes.Select(node => new DialogueNode(
                node.Index,
                Enum.Parse<DialogueNodeKind>(node.Kind.ToString()),
                DialogueText.Clean(node.Text),
                NullIfEmpty(node.Ops.Where(op => !UnusedOps.Contains(op.Opcode)).Select(BuildOp).ToList()),
                NullIfEmpty(node.Links),
                cased.Contains(node.Index)
                    ? new DialogueRange(node.Min, node.Max == Borg13Node.NoUpperBound ? null : node.Max)
                    : null)).ToList();

            records.Add(new DialogueRecord(
                RecordId.Hex(RecordTypes.Dialogue, index),
                script.Id,
                mode,
                nodes,
                NullIfEmpty(script.Actors.Select(DbRecordIds.For).OfType<RecordId>().ToList()),
                hasDoneFlag ? Flag(script.DoneFlag) : null,
                mode is DialogueMode.Backdrop or DialogueMode.Help ? script.Backdrop : null,
                tables.ScriptBattleFlags.TryGetValue(index, out var battle) ? Flag(battle) : null,
                tables.CampfireScenes.Contains(index),
                script.BlankNodes));
        }
        return records;
    }

    /// <summary>Ops the game never reads, which the records leave out.</summary>
    private static readonly HashSet<Borg13Opcode> UnusedOps =
        [Borg13Opcode.Unknown1, Borg13Opcode.Unknown2, Borg13Opcode.Backdrop, Borg13Opcode.Unknown55, Borg13Opcode.Unknown59];

    private static IReadOnlyList<T>? NullIfEmpty<T>(IReadOnlyList<T> list) => list.Count == 0 ? null : list;

    private static DialogueOp BuildOp(Borg13Op op)
    {
        var command = (DialogueCommand)op.Opcode;
        switch (op.Opcode)
        {
            case Borg13Opcode.SetSpeaker or Borg13Opcode.CameraCutToActor or Borg13Opcode.CameraOnActor
                or Borg13Opcode.CameraAngleToActor or Borg13Opcode.CameraPanToActor or Borg13Opcode.CameraSineToActor
                or Borg13Opcode.AddEnemy or Borg13Opcode.CheckMember or Borg13Opcode.AddMember or Borg13Opcode.RemoveMember
                or Borg13Opcode.CheckItem or Borg13Opcode.AddItem or Borg13Opcode.TakeItem or Borg13Opcode.ShowItem:
                // An ID with no record, such as the tools' 0xCCCC, keeps its number.
                return DbRecordIds.For(new ItemId(op.Value)) is { } target
                    ? new DialogueOp(command, Target: target)
                    : new DialogueOp(command, op.Value);
            case Borg13Opcode.CheckFlag or Borg13Opcode.SetFlag or Borg13Opcode.ClearFlag:
                return new DialogueOp(command, Target: Flag(op.Value));
            case Borg13Opcode.RandomLine:
                return new DialogueOp(command, Target: RecordId.Hex(RecordTypes.Dialogue, op.Value));
            // The game ignores the operand of these.
            case Borg13Opcode.CameraTrackOn or Borg13Opcode.EndDialogue or Borg13Opcode.FadeOut or Borg13Opcode.Shop
                or Borg13Opcode.Battle or Borg13Opcode.Train or Borg13Opcode.CampHeal or Borg13Opcode.HideItem
                or Borg13Opcode.Unknown53 or Borg13Opcode.Unimplemented or Borg13Opcode.WaitForCamera
                or Borg13Opcode.ResetVisited or Borg13Opcode.CheckPartySize or Borg13Opcode.CheckGold:
                return new DialogueOp(command);
            default:
                return new DialogueOp(command, op.Value);
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(value, PluginJson.Options));
    }

    private static void WriteAsset(string plugin, RecordId id, string extension, byte[] bytes)
    {
        var path = Path.Combine([plugin, "assets", id.Namespace, id.Type, .. id.Key.Split('/')]) + "." + extension;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
