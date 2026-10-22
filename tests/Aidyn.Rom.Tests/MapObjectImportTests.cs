using System.Globalization;
using System.Text.Json;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Import;

namespace Aidyn.Rom.Tests;

[Collection(ImportedPluginCollection.Name)]
public sealed class MapObjectImportTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private static int Borg(RecordId map) => Convert.ToInt32(map.Key[2..], 16);

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

        // GetVoxelObjectFromLink follows links until it reaches the type it wants, up to 51 steps.
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
    public void TheMapRecordsWriteBackAsTheFilesTheyCameFrom()
    {
        // A file per map set, holding the maps its chunks name, in ID order.
        var files = Directory.GetFiles(Path.Combine(Data.Dir, "records", "maps")).Select(Path.GetFileNameWithoutExtension).Order().ToList();
        Assert.Equal(Data.MapSets.Where(s => s.Chunks.Count > 0).Select(s => s.Label).Order(), files);
        foreach (var set in Data.MapSets.Where(s => s.Chunks.Count > 0))
        {
            var maps = set.Chunks.Select(c => c.Map).ToHashSet();
            var written = File.ReadAllBytes(Path.Combine(Data.Dir, "records", "maps", set.Label + ".json"));
            Assert.Equal(written, JsonSerializer.SerializeToUtf8Bytes(Data.Maps.Where(m => maps.Contains(m.Id)).ToList(), PluginJson.Options));
        }
    }

    [Fact]
    public void MapObjectsUseFlagsAndStartScripts()
    {
        // The flag uses of map objects.
        var uses = Data.Objects.Keys.SelectMany(v => Data.Index.UsesBy(v)).ToList();
        Assert.Equal(5283, uses.Count);

        // What starts each script: a map object, or another script's RandomLine.
        var starters = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue).SelectMany(s => Data.Index.StartersOf(s.Id)).ToHashSet();
        Assert.Contains(starters, s => s.At is null);
        Assert.Contains(starters, s => s.At is not null);

        // Each way round, the starters agree with what the plugin's objects say they start.
        Assert.All(starters.Where(s => s.At is null), s => Assert.Contains(s, Data.Index.StartedBy(s.By)));
    }

    [Fact]
    public void ScriptsAndShopsGiveAndTakeKeyItems()
    {
        // Every write of a key item's flags by a script or a shop, second effects of a node included.
        var plugin = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue).Select(s => s.Id)
            .Concat(Data.Set.All<ShopRecord>(RecordTypes.Shop).Select(s => s.Id))
            .SelectMany(id => Data.Index.UsesBy(id))
            .Where(u => u.Role is FlagRole.ItemGiven or FlagRole.ItemTaken)
            .Select(u => (u.By, u.At, Flag: Number(u.Flag), Access: u.Access.ToString(), Role: u.Role.ToString()))
            .ToHashSet();
        Assert.Contains(plugin, u => u is { Role: "ItemGiven", By.Type: RecordTypes.Dialogue });
        Assert.Contains(plugin, u => u is { Role: "ItemTaken", By.Type: RecordTypes.Dialogue });
        Assert.Contains(plugin, u => u is { Role: "ItemGiven", By.Type: RecordTypes.Shop });

        // Taking an item clears its held flag and never its region flag, and giving sets both.
        var items = Data.Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.KeyItem is not null).ToDictionary(i => i.KeyItem!.Held);
        var regions = items.Values.Select(i => i.KeyItem!.Region).OfType<RecordId>().ToHashSet();
        Assert.All(plugin.Where(u => u.Role == "ItemTaken"), u => Assert.Equal(("Clear", true), (u.Access, items.ContainsKey(Flag(u.Flag)))));
        Assert.All(plugin.Where(u => u.Role == "ItemGiven"), u => Assert.Equal("Set", u.Access));
        Assert.Contains(plugin, u => u.Role == "ItemGiven" && regions.Contains(Flag(u.Flag)));
    }

    [Fact]
    public void KeyItemsAreGivenByTheKindsOfRecordTheGameGivesThemFrom()
    {
        var items = Data.Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.KeyItem is not null).ToList();
        Assert.Equal(31, items.Count);
        Assert.Equal(18, items.Count(i => i.KeyItem!.Region is not null));

        // Each key item lists its held flag and its region flag, and each flag lists the item.
        foreach (var item in items)
        {
            var key = item.KeyItem!;
            Assert.Contains(new FlagUse(key.Held, item.Id, FlagAccess.Set, FlagRole.KeyItem), Data.Index.UsesOf(key.Held));
            if (key.Region is { } region)
                Assert.Contains(new FlagUse(region, item.Id, FlagAccess.Set, FlagRole.KeyItem), Data.Index.UsesOf(region));
            Assert.Equal(key.Region is null ? 1 : 2, Data.Index.UsesBy(item.Id).Count);
            Assert.All(Data.Index.UsesBy(item.Id), u => Assert.Equal(FlagRole.KeyItem, u.Role));
        }

        // What gives them: scripts, shops, containers and a monster party that's fought.
        var given = Data.Set.All<DialogueRecord>(RecordTypes.Dialogue).Select(s => s.Id)
            .Concat(Data.Set.All<ShopRecord>(RecordTypes.Shop).Select(s => s.Id))
            .Concat(Data.Objects.Keys)
            .SelectMany(id => Data.Index.UsesBy(id))
            .Where(u => u.Role == FlagRole.ItemGiven)
            .ToList();
        Assert.Equal(
            new Dictionary<string, int> { [RecordTypes.Dialogue] = 8, [RecordTypes.Shop] = 2, [RecordTypes.Voxel] = 8 },
            given.GroupBy(u => u.By.Type).ToDictionary(g => g.Key, g => g.Count()));
        var voxels = given.Where(u => u.By.Type == RecordTypes.Voxel).GroupBy(u => Data.Objects[u.By].GetType().Name)
            .ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(new Dictionary<string, int> { [nameof(ContainerObject)] = 7, [nameof(MonsterPartyObject)] = 1 }, voxels);
        Assert.All(given, u => Assert.Contains(items, i => i.KeyItem!.Held == u.Flag || i.KeyItem.Region == u.Flag));

        // Map 7 is sold in shop 17, and its held and region flags both come with it.
        var map7 = items.Single(i => i.Name == "Map 7");
        Assert.Equal(Flag(3411), map7.KeyItem!.Held);
        Assert.Equal(Flag(793), map7.KeyItem.Region);
        var shop = Data.Set.Get<ShopRecord>(RecordId.Decimal(RecordTypes.Shop, 17));
        Assert.Contains(shop.Stock, s => s.Item == map7.Id);
        Assert.Equal(
            [new FlagUse(Flag(793), shop.Id, FlagAccess.Set, FlagRole.ItemGiven), new FlagUse(Flag(3411), shop.Id, FlagAccess.Set, FlagRole.ItemGiven)],
            Data.Index.UsesBy(shop.Id));
    }

    [Fact]
    public void TheLighthouseKeeperGivesTheLighthouseKeyAtTwoNodes()
    {
        // The Lighthouse Key has a held flag and no region flag.
        var key = Data.Set.Get<ItemRecord>(RecordId.Parse("aidyn:item/0x1205"));
        Assert.Equal("Lighthouse Key", key.Name);
        Assert.Equal(new KeyItemFlags(Flag(3430)), key.KeyItem);

        // The keeper's conversation (Borg13 890) adds it in two nodes, and nothing takes it.
        var keeper = Script(890);
        var held = Data.Index.UsesOf(Flag(3430));
        Assert.Equal(
            [
                new FlagUse(Flag(3430), keeper, FlagAccess.Set, FlagRole.ItemGiven, 15),
                new FlagUse(Flag(3430), keeper, FlagAccess.Set, FlagRole.ItemGiven, 37),
                new FlagUse(Flag(3430), key.Id, FlagAccess.Set, FlagRole.KeyItem),
            ],
            held.Where(u => u.Role is FlagRole.ItemGiven or FlagRole.ItemTaken or FlagRole.KeyItem));
        var nodes = Data.Set.Get<DialogueRecord>(keeper).Nodes;
        Assert.All([15, 37], n => Assert.Contains(nodes[n].Ops!, op => op.Command == DialogueCommand.AddItem && op.Target == key.Id));

        // The conversation's own flag 285, which it checks first and sets, is a different flag from the key's.
        Assert.Contains(new FlagUse(Flag(285), keeper, FlagAccess.Set, FlagRole.ScriptOp, 28), Data.Index.UsesOf(Flag(285)));
        Assert.DoesNotContain(Data.Index.UsesOf(Flag(285)), u => u.Role is FlagRole.ItemGiven or FlagRole.KeyItem);

        // The item is upstream of its flag, and its giver is too.
        Assert.Contains(new QuestEdge(key.Id, Flag(3430), FlagRole.KeyItem, FlagAccess.Set), Data.Index.Upstream(Flag(3430)));
        Assert.Contains(new QuestEdge(keeper, Flag(3430), FlagRole.ItemGiven, FlagAccess.Set), Data.Index.Downstream(keeper));
    }

    [Fact]
    public void ATakenKeyItemClearsOnlyItsHeldFlag()
    {
        // Cradawgh's Body is given by one conversation (0x042D, node 52) and taken by another (0x043F, node 19).
        var body = Data.Set.Get<ItemRecord>(RecordId.Parse("aidyn:item/0x0119"));
        Assert.Equal(new KeyItemFlags(Flag(3417), Flag(799)), body.KeyItem);
        var taker = RecordId.Parse("aidyn:dialogue/0x043F");
        Assert.Equal([new FlagUse(Flag(3417), taker, FlagAccess.Clear, FlagRole.ItemTaken, 19)], Data.Index.UsesBy(taker).Where(u => u.Role == FlagRole.ItemTaken));
        Assert.DoesNotContain(Data.Index.UsesOf(Flag(799)), u => u.Role == FlagRole.ItemTaken);
        Assert.Contains(new FlagUse(Flag(799), RecordId.Parse("aidyn:dialogue/0x042D"), FlagAccess.Set, FlagRole.ItemGiven, 52), Data.Index.UsesOf(Flag(799)));
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

    [Fact]
    public void OrianasPathLightsAreTheSceneObjectsThatGoOut()
    {
        // RenderVoxelScenes puts out an object whose second model is BORG5_OrianaLight (0x374A).
        var lights = Data.Of<SceneObject>().Where(o => o.Object.GoesOut).ToList();
        var chunkOf = Data.MapSets.Single(s => s.Label == "Overworld").Chunks.DistinctBy(c => c.Map).ToDictionary(c => c.Map, c => c.Name);
        Assert.Equal(new Dictionary<string, int> { ["I08"] = 15, ["J08"] = 7, ["K08"] = 1 },
            lights.CountBy(l => chunkOf[l.Map.Id]).ToDictionary());
        Assert.All(lights, l =>
        {
            Assert.Equal([RecordId.Hex(RecordTypes.Scene, 0x1910), RecordId.Hex(RecordTypes.Scene, 0x374A)], l.Object.Models.Select(m => m.Model));
            Assert.Equal((2, 5f, 24f), (l.Object.Lods, l.Object.Models[0].DrawDistance, l.Object.Models[1].DrawDistance));
        });
        Assert.Equal(lights.Count, Data.Of<SceneObject>().Count(o => o.Object.Models.Any(m => m.Model == RecordId.Hex(RecordTypes.Scene, 0x374A))));
    }

    [Fact]
    public void OnlyThePathLightsFirstLevelDrawsNothing()
    {
        // 0x1910 is a grey quad whose Borg2 (0x190F) is hidden, so the game skips its only draw.
        var empty = Data.Of<SceneObject>().SelectMany(o => o.Object.Models).Where(m => m.Empty).Select(m => m.Model).Distinct();
        Assert.Equal([RecordId.Hex(RecordTypes.Scene, 0x1910)], empty);
        Assert.All(Data.Of<SceneObject>().Where(o => o.Object.GoesOut), l => Assert.Equal([true, false], l.Object.Models.Select(m => m.Empty)));
    }

    [Fact]
    public void APathLightInI08GoesOutAsThePlayerReachesItAndStaysOut()
    {
        var game = Core.GameState.NewGame(Data.Set);
        game.Flags.Set(Flag(50), true);
        var record = Data.Map("aidyn:map/0x0FDB");
        Core.MapObjects.LoadedMap Load() => Core.MapObjects.LoadedMap.Activate(record, Worlds.Chunk(), game.Flags, game.Memory, game.Party, new Core.Rng.Lcg());
        var map = Load();
        var light = Assert.IsType<SceneObject>(map[104]);
        Assert.True(map.IsActive(104));

        // The player 2 south of it, the camera behind him, looking north.
        var at = light.Position + new System.Numerics.Vector3(0, 0.5f, 2);
        var view = new Core.Scenes.SceneView(at, new(at.X, at.Z + 5), new(0, -1), 5, Core.Scenes.ViewCone.Original);
        Assert.Equal([104], map.PutOut(view, game.Memory, record.Id).Select(o => o.Index));
        Assert.False(Load().IsActive(104));
        Assert.True(Load().IsActive(103));
    }

    [Fact]
    public void AtHighestDetailAPathLightFadesOutAsThePlayerNearsItAndATreeShowsItsNearestLevel()
    {
        var map = Data.Map("aidyn:map/0x0FDB");
        var light = Assert.IsType<SceneObject>(map.Objects.Single(o => o.Index == 104));
        var highest = new Core.Scenes.DrawDistance(NearestDetail: true);

        // The player south of it, the camera 5 behind him, looking north at it.
        byte[] At(SceneObject o, float distance, Core.Scenes.DrawDistance range)
        {
            var at = o.Position + new System.Numerics.Vector3(0, 0, distance);
            var view = new Core.Scenes.SceneView(at, new(at.X, at.Z + 5), new(0, -1), 5, Core.Scenes.ViewCone.Original);
            var alphas = new byte[o.Lods];
            Core.Scenes.SceneObjectFade.Alphas(o, System.Numerics.Vector3.Zero, view, alphas, range);
            return alphas;
        }

        // The billboard, the second level, shows as the original's does, and fades out as the empty first fades in.
        foreach (var d in new[] { 20f, 10f, 4.5f, 3.5f, 2.5f })
            Assert.Equal(At(light, d, Core.Scenes.DrawDistance.Original)[1], At(light, d, highest)[1]);
        Assert.Equal([0, 255], At(light, 10, highest));
        Assert.Equal([0, 127], At(light, 3.5f, highest));
        Assert.Equal([0, 0], At(light, 2.5f, highest));

        // A tree near the lights: past its first level's distance the original draws its second, Highest still its first.
        var tree = map.Objects.OfType<SceneObject>().First(o => o.Lods == 2 && o.Models.Take(2).All(m => !m.Empty) && !o.GoesOut);
        var past = tree.Models[0].DrawDistance + 1;
        Assert.Equal([0, 255], At(tree, past, Core.Scenes.DrawDistance.Original));
        Assert.Equal([255, 0], At(tree, past, highest));
        Assert.Equal([255, 0], At(tree, 1, highest));
    }

    [Fact]
    public void TwentyOneOverworldMapsDrawNeighboursWhateverTheView()
    {
        var chunks = Data.MapSets.SelectMany(s => s.Chunks.Select(c => (Set: s.Label, Chunk: c))).ToList();
        var forcing = chunks.Where(c => Data.Map(c.Chunk.Map.ToString()).DrawnNeighbours is not null).ToList();
        Assert.Equal(21, forcing.Count);
        Assert.All(forcing, c => Assert.Equal("Overworld", c.Set));
        Assert.Equal("A15 B08 B15 G14 G19 H14 H19 I14 I17 I18 I19 J15 J16 J17 J19 K19 K20 L20 M20 M21 M22",
            string.Join(' ', forcing.Select(c => c.Chunk.Name).Order()));
        // I18 draws its four corners; I19 its north-east, south-east, south, west and east: 0xEA.
        Assert.Equal(Neighbours.NorthWest | Neighbours.NorthEast | Neighbours.SouthWest | Neighbours.SouthEast,
            Data.Map("aidyn:map/0x0FFE").DrawnNeighbours);
        Assert.Equal((Neighbours)0xEA, Data.Map("aidyn:map/0x1004").DrawnNeighbours);
    }
}
