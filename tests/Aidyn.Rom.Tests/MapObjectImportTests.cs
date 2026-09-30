using System.Globalization;
using System.Text.Json;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Import;
using RomFlagRole = Aidyn.Rom.Quests.FlagRole;

namespace Aidyn.Rom.Tests;

/// <summary>Imports NA 1.0 once for <see cref="MapObjectImportTests"/>, when the first test needs it.</summary>
public sealed class MapObjectFixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aidyn-mapobjects-" + Guid.NewGuid().ToString("N"));
    private readonly Lazy<Loaded> _loaded;

    public MapObjectFixture() => _loaded = new(Load);

    public Loaded Data => _loaded.Value;

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private Loaded Load()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var dir = Path.Combine(_root, BasePlugin.Folder);
        BaseImporter.Import(rom, dir);
        var set = PluginSet.Load([dir]);
        var maps = set.All<MapRecord>(RecordTypes.Map).ToList();
        var chunks = Maps.MapChunks.Read(rom);
        var archive = Borg.BorgArchive.Open(rom);
        return new Loaded
        {
            Dir = dir,
            Set = set,
            Maps = maps,
            MapSets = [.. set.All<MapSetRecord>(RecordTypes.MapSet)],
            Objects = maps.SelectMany(m => m.Objects.Select(o => (Id: MapRecord.VoxelId(m.Id, o.Index), Object: o)))
                .ToDictionary(o => o.Id, o => o.Object),
            Index = QuestIndex.Build(set),
            Atlas = Quests.QuestAtlas.Build(rom),
            Chunks = chunks,
            RomObjects = chunks.Select(c => c.Collision).Distinct().Order()
                .ToDictionary(b => b, b => Maps.Borg9Voxels.Read(archive.Read(b))),
        };
    }

    public sealed class Loaded
    {
        public required string Dir { get; init; }
        public required PluginSet Set { get; init; }
        public required IReadOnlyList<MapRecord> Maps { get; init; }
        public required IReadOnlyList<MapSetRecord> MapSets { get; init; }

        /// <summary>Every map object by its voxel ID.</summary>
        public required IReadOnlyDictionary<RecordId, MapObject> Objects { get; init; }

        public required QuestIndex Index { get; init; }
        public required Quests.QuestAtlas Atlas { get; init; }
        public required IReadOnlyList<Maps.MapChunk> Chunks { get; init; }

        /// <summary>The ROM's map objects of each Borg9 the chunk list names.</summary>
        public required IReadOnlyDictionary<int, IReadOnlyList<Maps.VoxelObject>> RomObjects { get; init; }

        public MapRecord Map(string id) => Maps.Single(m => m.Id == RecordId.Parse(id));

        /// <summary>Every object of a type, with its voxel ID and map.</summary>
        public IEnumerable<(RecordId Id, MapRecord Map, T Object)> Of<T>() where T : MapObject =>
            Maps.SelectMany(m => m.Objects.OfType<T>().Select(o => (MapRecord.VoxelId(m.Id, o.Index), m, o)));

        /// <summary>The map set's chunk the reference names, or null.</summary>
        public ChunkEntry? Chunk(ChunkRef chunk) => MapSets.Single(s => s.Id == chunk.MapSet).Chunks
            .SingleOrDefault(c => c.Row == chunk.Row && c.Column == chunk.Column);
    }
}

public sealed class MapObjectImportTests(MapObjectFixture fixture) : IClassFixture<MapObjectFixture>
{
    private MapObjectFixture.Loaded Data => fixture.Data;

    private static int Borg(RecordId map) => Convert.ToInt32(map.Key[2..], 16);

    private static int Number(RecordId flag) => int.Parse(flag.Key, CultureInfo.InvariantCulture);

    private static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    private static RecordId Script(int borg) => RecordId.Hex(RecordTypes.Dialogue, borg);

    private static RecordId Voxel(int borg9, int index) => MapRecord.VoxelId(RecordId.Hex(RecordTypes.Map, borg9), index);

    [Fact]
    public void EveryMapObjectOfEveryTypeIsImportedInIndexOrder()
    {
        var maps = Data.Maps;
        var counts = maps.SelectMany(m => m.Objects).GroupBy(o => o.GetType().Name).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int>
        {
            [nameof(ReferencePointObject)] = 8792,
            [nameof(SceneObject)] = 8317,
            [nameof(WanderNodeObject)] = 2052,
            [nameof(MonsterPartyObject)] = 1079,
            [nameof(SoundObject)] = 1038,
            [nameof(TeleporterObject)] = 894,
            [nameof(LightObject)] = 777,
            [nameof(ContainerObject)] = 370,
            [nameof(DialogueObject)] = 364,
            [nameof(TriggerObject)] = 302,
            [nameof(CameraObject)] = 244,
            [nameof(SavePointObject)] = 3,
        }, counts);
        Assert.Equal(24232, maps.Sum(m => m.Objects.Count));

        // Code objects are left out, so indices can skip, but never repeat or go backwards.
        foreach (var map in maps)
            Assert.Equal(map.Objects.Select(o => o.Index).Order().Distinct(), map.Objects.Select(o => o.Index));

        // Every object of a chunk's Borg9 that isn't a code object is there, with the ROM's own index.
        foreach (var (borg, romObjects) in Data.RomObjects)
        {
            var expected = romObjects.Where(o => o.Type != Maps.VoxelType.Code).Select(o => o.Index);
            Assert.Equal(expected, Data.Map($"aidyn:map/0x{borg:X4}").Objects.Select(o => o.Index));
        }
    }

    [Fact]
    public void LinksStayInTheirMapAndLeadContainersAndAnimatedActorsToTheirScenes()
    {
        var objects = Data.Objects;
        var failures = new List<string>();
        foreach (var (map, o) in Data.Maps.SelectMany(m => m.Objects.Select(o => (m, o))))
        {
            if (o.Link is { } link && (MapRecord.MapOf(link) != map.Id || !objects.ContainsKey(link)))
                failures.Add($"{MapRecord.VoxelId(map.Id, o.Index)} links to {link}");
        }
        Assert.Empty(failures);

        // GetVoxelFromObjectLink follows links until it reaches the type it wants, up to 51 steps.
        MapObject? Follow(RecordId voxel, Func<MapObject, bool> wanted)
        {
            var current = objects[voxel];
            for (var step = 0; step < 51 && current.Link is { } next; step++)
            {
                current = objects[next];
                if (wanted(current))
                    return current;
            }
            return wanted(current) ? current : null;
        }

        foreach (var (id, _, _) in Data.Of<ContainerObject>())
        {
            if (Follow(id, o => o is SceneObject) is null)
                failures.Add($"{id}: a container reaches no scene");
        }
        Assert.Empty(failures);

        var animated = Data.Of<TriggerObject>().Where(t => t.Object.Kind == TriggerKind.Animate).ToList();
        Assert.NotEmpty(animated);
        foreach (var (id, _, _) in animated)
        {
            if (Follow(id, o => o is SceneObject) is not SceneObject scene)
                failures.Add($"{id}: an Animate trigger reaches no scene");
            else if (scene.Models[0].Model.Type != RecordTypes.Actor)
                failures.Add($"{id}: an Animate trigger reaches {scene.Index}, whose first model is {scene.Models[0].Model}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void WanderNodesPartiesAndCamerasNameObjectsOfTheRightTypeInTheirMap()
    {
        var objects = Data.Objects;
        var failures = new List<string>();
        foreach (var (id, map, node) in Data.Of<WanderNodeObject>())
        {
            foreach (var next in node.Next)
            {
                if (MapRecord.MapOf(next) != map.Id || objects[next] is not WanderNodeObject)
                    failures.Add($"{id}: next {next} isn't a wander node in its map");
            }
        }
        foreach (var (id, map, party) in Data.Of<MonsterPartyObject>())
        {
            if (party.Home is not { } home || MapRecord.MapOf(home) != map.Id || objects[home] is not WanderNodeObject { Flags: var flags })
                failures.Add($"{id}: home {party.Home} isn't a wander node in its map");
            // The original crashes if a party's home node doesn't have Move.
            else if (!flags.HasFlag(WanderFlags.Move))
                failures.Add($"{id}: home {home} has {flags}, not Move");
        }
        foreach (var (id, map, camera) in Data.Of<CameraObject>().Where(c => c.Object.RefPoint is not null))
        {
            var point = camera.RefPoint!.Value;
            if (MapRecord.MapOf(point) != map.Id || objects[point] is not ReferencePointObject)
                failures.Add($"{id}: reference point {point} isn't in its map");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void TeleportersArriveAtReferencePointsOfTheirDestinationChunk()
    {
        var failures = new List<string>();
        foreach (var (id, _, teleporter) in Data.Of<TeleporterObject>())
        {
            var chunk = Data.Chunk(teleporter.Destination);
            if (chunk is null)
                failures.Add($"{id}: no chunk {teleporter.Destination}");
            foreach (var point in new[] { teleporter.Arrival, teleporter.Camera }.OfType<RecordId>())
            {
                if (chunk is null || MapRecord.MapOf(point) != chunk.Map || Data.Objects[point] is not ReferencePointObject)
                    failures.Add($"{id}: {point} isn't a reference point of {teleporter.Destination}'s map");
            }
        }
        foreach (var (id, _, dialogue) in Data.Of<DialogueObject>().Where(d => d.Object.Stage is not null))
        {
            if (Data.Chunk(dialogue.Stage!) is null)
                failures.Add($"{id}: no chunk {dialogue.Stage}");
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void ScriptsEntitiesLootAndItemsNamedByMapObjectsAreRecords()
    {
        var set = Data.Set;
        var failures = new List<string>();
        void Has(RecordId id, RecordId? named, string what)
        {
            if (named is { } record && !set.Contains(record))
                failures.Add($"{id}: {what} {record} isn't a record");
        }

        foreach (var (id, _, dialogue) in Data.Of<DialogueObject>())
        {
            Has(id, dialogue.Script, "script");
            if (dialogue.Script.Type != RecordTypes.Dialogue)
                failures.Add($"{id}: script {dialogue.Script} isn't a dialogue");
        }
        foreach (var (id, _, party) in Data.Of<MonsterPartyObject>())
        {
            Has(id, party.Dialogue, "dialogue");
            if (party.Dialogue is { Type: not RecordTypes.Dialogue })
                failures.Add($"{id}: dialogue {party.Dialogue} isn't a dialogue");
            Has(id, party.Entity, "entity");
            Has(id, party.Loot, "loot");
            foreach (var enemy in party.Enemies)
                Has(id, enemy.Entity, "enemy");
        }
        foreach (var (id, _, container) in Data.Of<ContainerObject>())
        {
            Has(id, container.Loot, "loot");
            Has(id, container.Item, "item");
        }
        Assert.Empty(failures);
        // A container's loot is always a table, even the two whose data names a potion.
        Assert.All(Data.Of<ContainerObject>(), c => Assert.True(c.Object.Loot is null or { Type: RecordTypes.Loot }, $"{c.Id}: {c.Object.Loot}"));
        Assert.Contains(Data.Of<ContainerObject>(), c => c.Object.Item is { Type: RecordTypes.Item } item && item.Key.StartsWith("0x10", StringComparison.Ordinal));

        // Something of each is there to be checked.
        Assert.Contains(Data.Of<MonsterPartyObject>(), p => p.Object.Loot is not null && p.Object.Dialogue is not null);
        Assert.Contains(Data.Of<ContainerObject>(), c => c.Object.Loot is not null);
        Assert.Contains(Data.Of<ContainerObject>(), c => c.Object.Item is not null);
    }

    [Fact]
    public void TheMissingReferencePointsAreExactlyTheKnownOnes()
    {
        // patioL2 (Borg9 6257, object 0) names a point its destination doesn't have, and five teleporters into
        // Jundar's interiors A01 name camera points it doesn't have.
        (int Borg9, int Index) patio = (6257, 0);
        HashSet<(int, int)> cameras = [(5278, 0), (5300, 1), (5412, 1), (5432, 0), (6257, 0)];
        var failures = new List<string>();
        var teleporters = 0;
        foreach (var (borg, romObjects) in Data.RomObjects)
        {
            var map = Data.Map($"aidyn:map/0x{borg:X4}");
            foreach (var rom in romObjects.Where(o => o.Data is Maps.TeleporterData))
            {
                teleporters++;
                var data = (Maps.TeleporterData)rom.Data!;
                var o = (TeleporterObject)map.Objects.Single(x => x.Index == rom.Index);
                var where = $"{Voxel(borg, rom.Index)}";

                var ownPosition = data.RefPoint == Maps.TeleporterData.OwnPosition;
                var lost = (borg, rom.Index) == patio;
                if ((o.Arrival is null) != (ownPosition || lost))
                    failures.Add($"{where}: arrival {o.Arrival}, ROM point 0x{data.RefPoint:X}");
                if (lost && ownPosition)
                    failures.Add($"{where}: the missing arrival is the teleporter's own position");

                var noCamera = data.CameraRefPoint == 0;
                var lostCamera = cameras.Contains((borg, rom.Index));
                if ((o.Camera is null) != (noCamera || lostCamera))
                    failures.Add($"{where}: camera {o.Camera}, ROM point {data.CameraRefPoint}");
                if (lostCamera && noCamera)
                    failures.Add($"{where}: the missing camera point is 0");

                // A point that's there is the first with the ROM's ID.
                foreach (var (point, id) in new[] { (o.Arrival, data.RefPoint), (o.Camera, data.CameraRefPoint) })
                {
                    if (point is not { } found)
                        continue;
                    var destination = Data.Map(Data.Chunk(o.Destination)!.Map.ToString());
                    var first = destination.Objects.OfType<ReferencePointObject>().First(r => r.Id == id);
                    if (found != MapRecord.VoxelId(destination.Id, first.Index))
                        failures.Add($"{where}: {found} isn't the first point with ID {id}");
                }

                if ((o.SecretFlag is null) != (data.SecretFlag is 0 or 1))
                    failures.Add($"{where}: secret flag {o.SecretFlag}, ROM flag {data.SecretFlag}");
                else if (o.SecretFlag is { } secret && Number(secret) != data.SecretFlag)
                    failures.Add($"{where}: secret flag {secret}, ROM flag {data.SecretFlag}");
            }
        }
        Assert.Empty(failures);
        Assert.Equal(894, teleporters);
    }

    [Fact]
    public void GatesAndFlagsAreOnlyOnTheTypesThatReadThem()
    {
        var failures = new List<string>();
        foreach (var (map, o) in Data.Maps.SelectMany(m => m.Objects.Select(o => (m, o))))
        {
            var where = MapRecord.VoxelId(map.Id, o.Index);
            var kind = (o as ContainerObject)?.Kind;
            var crate = kind is ContainerKind.Chest or ContainerKind.Sack or ContainerKind.Crate or ContainerKind.Barrel;

            // Parties, dialogues and save points are always there, and so are the plain containers until they're opened.
            var alwaysThere = o is MonsterPartyObject or DialogueObject or SavePointObject || crate;
            if ((o.Visible is null) != alwaysThere)
                failures.Add($"{where}: a {o.GetType().Name} {kind} has visible gate {o.Visible}");

            var works = o is TeleporterObject or TriggerObject or DialogueObject or MonsterPartyObject || (kind is not null && !crate);
            if ((o.Enabled is not null) != works)
                failures.Add($"{where}: a {o.GetType().Name} {kind} has enabled gate {o.Enabled}");

            if (o.Used is not null && o is not (ContainerObject or TriggerObject or MonsterPartyObject))
                failures.Add($"{where}: a {o.GetType().Name} writes {o.Used}");
            if (o is MonsterPartyObject && o.Used is { Clear: true })
                failures.Add($"{where}: a party clears {o.Used.Flag}");

            if (o.Tangible && o is not (TeleporterObject or TriggerObject or DialogueObject))
                failures.Add($"{where}: a {o.GetType().Name} is tangible");
        }
        Assert.Empty(failures);

        // Each gate is used somewhere.
        Assert.Contains(Data.Maps.SelectMany(m => m.Objects), o => o.Used is { Clear: true });
        Assert.Contains(Data.Maps.SelectMany(m => m.Objects), o => o.Enabled is { Invert: true });
        Assert.Contains(Data.Maps.SelectMany(m => m.Objects), o => o.Tangible);
    }

    [Fact]
    public void TheMapRecordsWriteBackAsTheFileTheyCameFrom()
    {
        var written = File.ReadAllBytes(Path.Combine(Data.Dir, "records", "maps.json"));
        Assert.Equal(written, JsonSerializer.SerializeToUtf8Bytes(Data.Maps.ToList(), PluginJson.Options));
    }

    [Fact]
    public void MapObjectsUseFlagsAsTheRomsQuestAtlasSays()
    {
        var included = Data.RomObjects.Keys.ToHashSet();
        RomFlagRole[] notIndexed = [RomFlagRole.ItemGiven, RomFlagRole.ItemTaken, RomFlagRole.MemberAdded, RomFlagRole.MemberRemoved, RomFlagRole.BossBeaten];

        // The flag uses of map objects, with no more than the flag's number, as either side has them.
        var rom = Data.Atlas.References
            .Where(r => r.Source is Quests.ObjectSource s && included.Contains(s.Borg9) && r.Flag > 1 && !notIndexed.Contains(r.Role))
            .Select(r => (Voxel: Voxel(((Quests.ObjectSource)r.Source).Borg9, ((Quests.ObjectSource)r.Source).Index),
                Flag: (int)r.Flag, Access: r.Access.ToString(), Role: r.Role.ToString()))
            .ToHashSet();
        var plugin = Data.Objects.Keys
            .SelectMany(v => Data.Index.UsesBy(v).Select(u => (Voxel: u.By, Flag: Number(u.Flag), Access: u.Access.ToString(), Role: u.Role.ToString())))
            .ToHashSet();
        Assert.True(rom.Count > 5000, $"{rom.Count} uses");
        Assert.Empty(rom.Except(plugin).Select(u => $"ROM only: {u}"));
        Assert.Empty(plugin.Except(rom).Select(u => $"plugin only: {u}"));

        // What starts each script.
        var romStarters = Data.Atlas.Scripts.SelectMany(s => s.Value.StartedBy.Select(by => (Script: Script(s.Key), By: by)))
            .Select(s => s.By switch
            {
                Quests.ObjectSource o when included.Contains(o.Borg9) => new ScriptStarter(s.Script, Voxel(o.Borg9, o.Index)),
                Quests.ScriptSource script => new ScriptStarter(s.Script, Script(script.Script), script.Node),
                _ => null,
            })
            .OfType<ScriptStarter>().ToHashSet();
        var pluginStarters = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue).SelectMany(s => Data.Index.StartersOf(s.Id)).ToHashSet();
        Assert.Contains(romStarters, s => s.At is null);
        Assert.Contains(romStarters, s => s.At is not null);
        Assert.Empty(romStarters.Except(pluginStarters).Select(s => $"ROM only: {s}"));
        Assert.Empty(pluginStarters.Except(romStarters).Select(s => $"plugin only: {s}"));

        // Each way round, the starters agree with what the plugin's objects say they start.
        Assert.All(pluginStarters.Where(s => s.At is null), s => Assert.Contains(s, Data.Index.StartedBy(s.By)));
    }

    [Fact]
    public void TheEhudUndergroundSecretDoorsAreTeleportersInItsMaps()
    {
        var ehud = Data.MapSets.Single(s => s.Label == "EhudUnder");
        var maps = ehud.Chunks.Select(c => c.Map).ToHashSet();
        foreach (var n in new[] { 955, 956, 957, 958, 960, 961, 962, 963 })
        {
            var found = Data.Index.UsesOf(Flag(n)).Where(u => u is { Role: FlagRole.SecretFound, Access: FlagAccess.Set }).ToList();
            var door = Assert.Single(found);
            Assert.IsType<TeleporterObject>(Data.Objects[door.By]);
            Assert.Contains(MapRecord.MapOf(door.By), maps);
        }
    }

    [Fact]
    public void ACheckedChestAndDoorHaveTheFieldsThePlayerSees()
    {
        // A chest with a lock, a trap and gold, which links to its model.
        var chest = Assert.IsType<ContainerObject>(Assert.Single(Data.Map("aidyn:map/0x0EE0").Objects, o => o.Index == 10));
        Assert.Equal(
            (ContainerKind.Chest, Flag(3378), Flag(3379), 2, 1, RecordId.Parse("aidyn:loot/0x0803"), 1000),
            (chest.Kind, chest.Opened!.Value, chest.Exploded!.Value, chest.LockLevel, chest.TrapLevel, chest.Loot!.Value, chest.Gold));
        Assert.Equal(Voxel(0x0EE0, 11), chest.Link);
        Assert.IsType<SceneObject>(Data.Objects[chest.Link!.Value]);
        Assert.False(string.IsNullOrEmpty(Data.Set.Get<DbRecord>(chest.Loot.Value).Name));

        var door = Assert.IsType<TeleporterObject>(Assert.Single(Data.Map("aidyn:map/0x0EAA").Objects, o => o.Index == 4));
        Assert.Equal("Front_enter", door.Name);
        Assert.Equal(new ChunkRef(RecordId.Parse("aidyn:mapset/3"), 1, 3), door.Destination);
        Assert.Equal(Voxel(0x11F6, 26), door.Arrival);
        Assert.Equal(
            (4, Flag(5221), Flag(976), 4, Flag(975), Flag(975)),
            (door.LockLevel, door.LockFlag!.Value, door.PickedFlag!.Value, door.TrapLevel, door.TrapFlag!.Value, door.DisarmFlag!.Value));
    }
}
