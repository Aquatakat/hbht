using System.Collections.Concurrent;
using System.Numerics;
using System.Text.RegularExpressions;
using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Physics;
using Aidyn.Core.Movement;
using Aidyn.Core.Rng;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// What map objects do in trek: the collision triangles that run them, how a chunk's objects load, and
/// what using containers, doors, secret doors, sources and triggers does, against NA 1.0's data and a new game.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MapObjectUseTests(Na10Import fixture)
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<CollisionTriangle>> Collisions = new();

    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");

    private readonly Dictionary<(RecordId Set, int Row, int Column), CollisionWorld> _worlds = [];

    private Na10Import.Loaded Data => fixture.Data;

    /// <summary>A new game with everything using map objects needs, and a host that writes down what it was asked.</summary>
    private sealed class Play
    {
        public required GameState Game { get; init; }
        public required MenuText Text { get; init; }
        public required RecordingHost Host { get; init; }
        public required MapObjectUse Use { get; init; }

        public bool Flag(RecordId flag) => Game.Flags.Get(flag);
    }

    private sealed class RecordingHost : IMapObjectHost
    {
        public List<TeleporterObject> Teleports { get; } = [];
        public List<(ItemPile Loot, int Gold)> Loots { get; } = [];
        public List<DialogueObject> Scripts { get; } = [];
        public List<(SceneObject Actor, int Request)> Animations { get; } = [];
        public List<SceneObject> Changes { get; } = [];
        public List<Aidyn.Core.Audio.OneShot> Sounds { get; } = [];

        public bool Idle => Teleports.Count + Loots.Count + Scripts.Count + Animations.Count + Changes.Count + Sounds.Count == 0;

        public void Teleport(LoadedMap from, TeleporterObject teleporter) => Teleports.Add(teleporter);

        public void OpenLoot(ItemPile loot, int gold) => Loots.Add((loot, gold));

        public void StartScript(LoadedMap map, DialogueObject dialogue) => Scripts.Add(dialogue);

        public void Animate(LoadedMap map, SceneObject actor, int request) => Animations.Add((actor, request));

        public void Changed(LoadedMap map, SceneObject scene) => Changes.Add(scene);

        public void Sound(Aidyn.Core.Audio.OneShot sound) => Sounds.Add(sound);
    }

    private Play NewGame()
    {
        var game = GameState.NewGame(Data.Set);
        var text = MenuText.From(Data.Set);
        var host = new RecordingHost();
        return new Play { Game = game, Text = text, Host = host, Use = new MapObjectUse(game, text, host) };
    }

    /// <summary>The chunk as it loads, with a generator of its own so what's used afterwards rolls the same.</summary>
    private static LoadedMap Load(Play play, MapRecord map) =>
        LoadedMap.Activate(map, map.Id, play.Game.Flags, play.Game.Memory, play.Game.Party, new Lcg());

    private IReadOnlyList<CollisionTriangle> Collision(MapRecord map) =>
        Collisions.GetOrAdd(Data.Set.FindAsset(map.Id, "glb")!, path => CollisionMesh.Read(File.ReadAllBytes(path)));

    /// <summary>The map set and chunk a map is placed in.</summary>
    private (MapSetRecord Set, ChunkEntry Chunk) Placed(MapRecord map) =>
        Data.MapSets.SelectMany(s => s.Chunks.Select(c => (Set: s, Chunk: c))).First(p => p.Chunk.Map == map.Id);

    private MapRecord I08 => Data.Set.Get<MapRecord>(Data.Chunk(new ChunkRef(Overworld, 9, 8))!.Map);

    private static bool HoldsSomething(ContainerObject c) => c.Gold != 0 || c.Loot is not null || c.Item is not null;

    private static SceneObject Scene(LoadedMap map, ContainerObject container) => map.Linked<SceneObject>(container)!;

    private static bool IsChest(ContainerObject c) => c.Kind < ContainerKind.Herb;

    // What A reaches.

    /// <summary>The player's collision radius, which a container's reach adds.</summary>
    private const float PlayerRadius = 0.5f;

    [Fact]
    public void AContainersReachIsItsOwnNotItsRadius()
    {
        var containers = Data.Maps.SelectMany(m => m.Objects).OfType<ContainerObject>().ToList();

        // chestSize: 2 for most, where the radius is 10.
        Assert.InRange(containers.Count(c => c.Reach == 2 && c.Radius == 10), 200, int.MaxValue);
        Assert.All(containers, c => Assert.InRange(c.Reach, 1, 5));
        Assert.All(Data.Maps, m => Assert.NotNull(m.Grid));
    }

    [Fact]
    public void TheStartChestIsReachedWithinItsReachFacingAnyWayAndNotBeyond()
    {
        var play = NewGame();
        var map = Load(play, I08);
        var start = new Vector3(16, 0, 62);
        var chest = map.Objects.OfType<ContainerObject>().Where(c => c.Loot == RecordId.Parse("aidyn:loot/0x0839"))
            .MinBy(c => Vector2.Distance(new Vector2(c.Position.X, c.Position.Z), new Vector2(start.X, start.Z)))!;
        Assert.Equal(2, chest.Reach);

        var reachedNear = 0;
        for (var i = 0; i < 16; i++)
        {
            var angle = MathF.Tau * i / 16;
            var way = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            // Facing away from the chest.
            var away = new Vector2(way.X, way.Z);
            var near = chest.Position + way * (chest.Reach + PlayerRadius - 0.1f);
            var far = chest.Position + way * (chest.Reach + PlayerRadius + 0.5f);
            if (I08.Grid!.At(near).Contains(chest.Index))
            {
                Assert.Same(chest, play.Use.Reach(map, near, away, PlayerRadius));
                reachedNear++;
            }
            Assert.NotSame(chest, play.Use.Reach(map, far, away, PlayerRadius));
        }
        Assert.InRange(reachedNear, 8, 16);
    }

    /// <summary>Whether a point is on a map's grid, as <c>getZonePositionShorts</c> finds its cell, truncating.</summary>
    private static bool OnGrid(ObjectGrid grid, Vector3 point)
    {
        var x = (int)((point.X - grid.Origin.X) / grid.CellSize.X);
        var z = (int)((point.Z - grid.Origin.Y) / grid.CellSize.Y);
        return x >= 0 && z >= 0 && x < grid.Columns && z < grid.Rows;
    }

    /// <summary>Points in a disc about an object, across its reach.</summary>
    private static IEnumerable<Vector3> Disc(Vector3 centre, float reach)
    {
        for (var dx = -reach; dx <= reach; dx += reach / 10)
        {
            for (var dz = -reach; dz <= reach; dz += reach / 10)
            {
                if (dx * dx + dz * dz <= reach * reach)
                    yield return centre + new Vector3(dx, 0, dz);
            }
        }
    }

    [Fact]
    public void OnTheGridEveryCellAnObjectsReachCoversListsIt()
    {
        var missed = new List<string>();
        foreach (var map in Data.Maps)
        {
            foreach (var o in map.Objects.Where(o => !o.Tangible && o is ContainerObject or TeleporterObject or DialogueObject or TriggerObject))
            {
                var reach = o is ContainerObject c ? c.Reach + PlayerRadius : o.Radius;
                if (Disc(o.Position, reach).Any(p => OnGrid(map.Grid!, p) && !map.Grid!.At(p).Contains(o.Index)))
                    missed.Add($"{MapRecord.VoxelId(map.Id, o.Index)}");
            }
        }
        // But one dialogue that's off its map's grid altogether, which the first cell doesn't list, so A never
        // reaches it, as in the original; only a trigger triangle naming it could start it.
        Assert.Equal(["aidyn:voxel/0x185C/0"], missed);
    }

    [Fact]
    public void OffTheGridAWeighsOnlyTheFirstCellsObjects()
    {
        // A door whose radius reaches past its map's grid: within its radius but off the grid, the first
        // cell's list counts, which doesn't hold it.
        var play = NewGame();
        bool Works(MapObject o) => o.Enabled is not { } gate || play.Flag(gate.Flag) != gate.Invert;
        foreach (var record in Data.Maps)
        {
            var grid = record.Grid!;
            foreach (var door in record.Objects.OfType<TeleporterObject>().Where(t => !t.Tangible && Works(t) && !grid.Cells[0].Contains(t.Index)))
            {
                var points = Disc(door.Position, door.Radius * 0.9f).ToList();
                var off = points.FirstOrDefault(p => !OnGrid(grid, p));
                var on = points.FirstOrDefault(p => OnGrid(grid, p));
                if (off == default || on == default)
                    continue;
                var map = Load(play, record);
                if (!map.IsActive(door.Index))
                    continue;
                Assert.NotSame(door, play.Use.Reach(map, off, Vector2.UnitX, PlayerRadius));
                Assert.Contains(points.Where(p => OnGrid(grid, p)), p => play.Use.Reach(map, p, Vector2.UnitX, PlayerRadius) == door);
                return;
            }
        }
        Assert.Fail("No door reaches past its grid.");
    }

    // Trigger triangles and toggles.

    [Fact]
    public void EveryTriggerTriangleNamesACameraOrADialogueInItsOwnMap()
    {
        int cameras = 0, dialogues = 0, others = 0, triggers = 0;
        var failures = new List<string>();
        foreach (var map in Data.Maps)
        {
            Assert.NotNull(Data.Set.FindAsset(map.Id, "glb"));
            foreach (var t in Collision(map).Where(t => t.Role == CollisionRole.Trigger))
            {
                triggers++;
                switch (map.Objects.FirstOrDefault(o => o.Index == t.Target))
                {
                    case CameraObject: cameras++; break;
                    case DialogueObject: dialogues++; break;
                    case var other: others++; failures.Add($"{map.Id}: a triangle names {t.Target}, {other?.GetType().Name ?? "nothing"}"); break;
                }
            }
        }
        Assert.Empty(failures);
        Assert.Equal((1849, 1532, 317, 0), (triggers, cameras, dialogues, others));
    }

    [Fact]
    public void EveryToggleTriangleHasItsChangeCollisionTriggerInItsOwnMapAndTheOtherWayRound()
    {
        var failures = new List<string>();
        var toggles = 0;
        foreach (var map in Data.Maps)
        {
            var ids = Collision(map).Where(t => t.Role == CollisionRole.Toggle).Select(t => t.Target).ToHashSet();
            toggles += Collision(map).Count(t => t.Role == CollisionRole.Toggle);
            var triggered = map.Objects.OfType<TriggerObject>().Where(t => t.Kind == TriggerKind.ChangeCollision)
                .Select(t => t.Toggle!.Value).ToHashSet();
            foreach (var id in ids.Except(triggered))
                failures.Add($"{map.Id}: toggle {id} has no trigger");
            foreach (var id in triggered.Except(ids))
                failures.Add($"{map.Id}: a trigger changes toggle {id}, which has no triangles");
        }
        Assert.Empty(failures);
        Assert.Equal(62, toggles);
    }

    // Loading a chunk.

    [Fact]
    public void AChunkLoadsItsChestsAndSacksClosedAndThereWhileTheyHoldSomething()
    {
        var play = NewGame();
        var map = I08;
        var loaded = Load(play, map);
        var held = map.Objects.OfType<ContainerObject>().Where(c => IsChest(c) && HoldsSomething(c) && c.Pak != PakVariant.Without).ToList();
        Assert.Equal(7, held.Count);
        foreach (var c in held)
        {
            Assert.True(loaded.IsActive(c.Index), $"{c.Index} is active");
            Assert.NotNull(loaded.Container(c.Index));
            Assert.True(loaded.IsActive(Scene(loaded, c).Index), $"{c.Index}'s model is there");
            Assert.Equal(ContainerLook.Closed, loaded.Look(Scene(loaded, c).Index));
        }
    }

    [Fact]
    public void AChestOpenedByItsFlagLoadsEmptyAndShowsItsOpenedModel()
    {
        var play = NewGame();
        var map = I08;
        var chest = map.Objects.OfType<ContainerObject>().First(c => IsChest(c) && c.Opened is not null && c.Gold > 0);
        Assert.True(Load(play, map).IsActive(chest.Index));

        play.Game.Flags.Set(chest.Opened!.Value, true);
        var loaded = Load(play, map);
        Assert.False(loaded.IsActive(chest.Index));
        Assert.Equal(ContainerLook.Opened, loaded.Look(Scene(loaded, chest).Index));
        Assert.True(loaded.IsActive(Scene(loaded, chest).Index));
        Assert.Equal(0, loaded.Container(chest.Index)!.Gold);
    }

    [Fact]
    public void AChestWithNoOpenedFlagLoadsEmptyWhileItIsRemembered()
    {
        var (_, map, chest) = Data.Of<ContainerObject>().First(c => IsChest(c.Object) && c.Object.Opened is null && HoldsSomething(c.Object));
        var play = NewGame();
        var loaded = Load(play, map);
        Assert.True(loaded.IsActive(chest.Index));
        Assert.Equal(ContainerLook.Closed, loaded.Look(Scene(loaded, chest).Index));

        play.Game.Memory.Remember(new Memory(MapRecord.VoxelId(map.Id, chest.Index), MemoryKind.Opened), 0);
        loaded = Load(play, map);
        Assert.False(loaded.IsActive(chest.Index));
        Assert.Equal(ContainerLook.Opened, loaded.Look(Scene(loaded, chest).Index));
        Assert.Equal(0, loaded.Container(chest.Index)!.Gold);
    }

    [Fact]
    public void ADoorsLockIsGoneAsItsChunkLoadsOnceItsLockFlagIsSet()
    {
        var flags = NewGame().Game.Flags;
        var doors = Data.Of<TeleporterObject>()
            .Where(t => t.Object.LockLevel > 0 && t.Object.LockFlag is { } flag && flags.TryHandle(flag, out var h) && !flags.IsDerived(h)).ToList();
        Assert.NotEmpty(doors);
        var checkedDoors = 0;
        foreach (var (_, map, door) in doors)
        {
            var play = NewGame();
            var before = Load(play, map).Teleporter(door.Index)!;
            // Some doors start unlocked: the flag is already set, or the door isn't there.
            if (before.LockLevel == 0)
                continue;
            Assert.Equal(door.LockLevel, before.LockLevel);
            play.Game.Flags.Set(door.LockFlag!.Value, true);
            var loaded = Load(play, map);
            Assert.Equal(0, loaded.Teleporter(door.Index)!.LockLevel);
            Assert.Equal(door.TrapLevel, loaded.Teleporter(door.Index)!.TrapLevel);
            checkedDoors++;
        }
        Assert.True(checkedDoors > 0);
    }

    // Opening a chest.

    [Fact]
    public void OpeningAnUntrappedUnlockedChestOpensItAndGivesItsGoldOrOpensTheLootWindow()
    {
        var map = I08;
        var chests = map.Objects.OfType<ContainerObject>()
            .Where(c => IsChest(c) && c.TrapLevel == 0 && c.LockLevel == 0 && c.Opened is not null && HoldsSomething(c)).ToList();
        Assert.Equal(7, chests.Count);
        var gold = 0;
        var windows = 0;
        foreach (var chest in chests)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            var scene = Scene(loaded, chest);
            var before = play.Game.Party.Gold;

            play.Use.Use(loaded, chest);

            Assert.True(play.Flag(chest.Opened!.Value));
            if (chest.Used is { } used)
                Assert.Equal(!used.Clear, play.Flag(used.Flag));
            Assert.False(loaded.IsActive(chest.Index));
            Assert.Equal(ContainerLook.Opened, loaded.Look(scene.Index));
            Assert.Equal([scene], play.Host.Changes);
            // It has an opened flag, so nothing is remembered.
            Assert.Empty(play.Game.Memory.Entries);

            if (play.Use.Popup is { } popup)
            {
                gold++;
                Assert.Equal(TrekPopupKind.Message, popup.Kind);
                var match = Regex.Match(popup.Text, @"^You just picked up (\d+) gold!$");
                Assert.True(match.Success, popup.Text);
                var picked = uint.Parse(match.Groups[1].Value);
                Assert.Equal(before + picked, play.Game.Party.Gold);
                Assert.Empty(play.Host.Loots);
                if (chest.Loot is null)
                    Assert.Equal((uint)chest.Gold, picked);
                play.Use.Accept();
                Assert.Null(play.Use.Popup);
            }
            else
            {
                windows++;
                var (loot, given) = Assert.Single(play.Host.Loots);
                Assert.NotEmpty(loot.Stacks);
                Assert.Equal(before + (uint)given, play.Game.Party.Gold);
                Assert.NotNull(chest.Loot);
            }

            // Using it again finds it opened.
            var seen = play.Host.Loots.Count;
            play.Use.Use(loaded, chest);
            Assert.Null(play.Use.Popup);
            Assert.Equal(seen, play.Host.Loots.Count);
        }
        Assert.Equal(chests.Count, gold + windows);
        Assert.True(gold > 0);
    }

    [Fact]
    public void OpeningAChestThatAlwaysHoldsAnItemOpensTheLootWindowWithIt()
    {
        var (_, map, chest) = Data.Of<ContainerObject>().First(c => IsChest(c.Object) && c.Object.Item is not null
            && c.Object.TrapLevel == 0 && c.Object.LockLevel == 0 && c.Object.Opened is not null);
        var play = NewGame();
        var loaded = Load(play, map);
        var before = play.Game.Party.Gold;
        play.Use.Use(loaded, chest);

        var (loot, gold) = Assert.Single(play.Host.Loots);
        Assert.Contains(loot.Stacks, s => s.Item.Id == chest.Item);
        Assert.Equal(before + (uint)gold, play.Game.Party.Gold);
        Assert.Null(play.Use.Popup);
    }

    [Fact]
    public void OpeningAChestWithNoOpenedFlagRemembersItAsEmptied()
    {
        var (_, map, chest) = Data.Of<ContainerObject>().First(c => IsChest(c.Object) && c.Object.Opened is null && HoldsSomething(c.Object)
            && c.Object.TrapLevel == 0 && c.Object.LockLevel == 0);
        var play = NewGame();
        var loaded = Load(play, map);
        play.Use.Use(loaded, chest);

        Assert.False(loaded.IsActive(chest.Index));
        Assert.True(loaded.Remembered(play.Game.Memory, chest));
        Assert.Equal(ContainerLook.Opened, loaded.Look(Scene(loaded, chest).Index));
    }

    // Traps.

    private static string Danger(int level) => level < 3 ? "slightly" : level < 6 ? "moderately" : "very";

    private List<(MapRecord Map, ContainerObject Container)> Trapped() =>
        [.. Data.Of<ContainerObject>().Where(c => c.Object.TrapLevel > 0).Select(c => (c.Map, c.Object))];

    [Fact]
    public void EveryTrappedContainerAsksWhetherToDisarmItAndExitingChangesNothing()
    {
        var trapped = Trapped();
        Assert.Equal(85, trapped.Count);
        Assert.All(trapped, t => Assert.True(IsChest(t.Container)));
        foreach (var (map, chest) in trapped)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            var container = loaded.Container(chest.Index)!;
            var gold = container.Gold;
            var before = play.Game.Party.Gold;

            play.Use.Use(loaded, chest);
            var popup = play.Use.Popup!;
            Assert.Equal(TrekPopupKind.Choice, popup.Kind);
            Assert.Equal($"There's a {Danger(chest.TrapLevel)} dangerous trap!", popup.Text);
            Assert.Equal(["Disarm", "Exit"], popup.Choices.Rows);
            Assert.Equal(0, popup.Choices.Cursor);

            // Exit.
            play.Use.MoveCursor(1);
            Assert.Equal(1, popup.Choices.Cursor);
            play.Use.Accept();
            AssertUntouched();

            // B leaves it as well.
            play.Use.Use(loaded, chest);
            Assert.Equal(TrekPopupKind.Choice, play.Use.Popup!.Kind);
            play.Use.Back();
            AssertUntouched();

            void AssertUntouched()
            {
                Assert.Null(play.Use.Popup);
                Assert.True(loaded.IsActive(chest.Index));
                Assert.Equal(chest.TrapLevel, container.TrapLevel);
                Assert.Equal(gold, container.Gold);
                Assert.Equal(before, play.Game.Party.Gold);
                Assert.Equal(ContainerLook.Closed, loaded.Look(Scene(loaded, chest).Index));
                Assert.False(chest.Opened is { } opened && play.Flag(opened));
                Assert.Empty(play.Game.Memory.Entries);
                Assert.True(play.Host.Idle);
            }
        }
    }

    /// <summary>Alaron has no Mechanic, so his Disarm always fails, which makes the outcome the same whatever the dice.</summary>
    [Fact]
    public void ADisarmAlaronCannotDoSetsOffTheTrapAndDestroysTheContainer()
    {
        // Alaron starts without the Mechanic skill, so no one can disarm anything.
        Assert.Equal(0, NewGame().Game.Party.Members.Single().Skills.Modified(Skill.Mechanic));

        var comeBack = new List<string>();
        foreach (var (map, chest) in Trapped())
        {
            var play = NewGame();
            var alaron = play.Game.Party.Members.Single();
            var loaded = Load(play, map);
            var scene = Scene(loaded, chest);
            var endurance = alaron.Stats.Modified(Stat.Endurance);

            play.Use.Use(loaded, chest);
            play.Use.Accept();

            var popup = play.Use.Popup!;
            Assert.Equal(TrekPopupKind.Damage, popup.Kind);
            var lines = popup.Text.Split('\n');
            Assert.Equal(2, lines.Length);
            Assert.Equal("The trap exploded!", lines[0]);
            var match = Regex.Match(lines[1], @"^Alaron took (\d+) damage!$");
            Assert.True(match.Success, lines[1]);
            var damage = int.Parse(match.Groups[1].Value);
            Assert.InRange(damage, 1, 4 * chest.TrapLevel);
            Assert.Equal(damage < endurance ? endurance - damage : 1, alaron.Stats.Modified(Stat.Endurance));

            Assert.True(chest.Exploded is not { } exploded || play.Flag(exploded));
            Assert.False(loaded.IsActive(chest.Index));
            Assert.Equal(ContainerLook.Destroyed, loaded.Look(scene.Index));
            Assert.Contains(scene, play.Host.Changes);
            Assert.Empty(play.Host.Loots);
            Assert.Equal(0, loaded.Container(chest.Index)!.Gold);
            Assert.Null(loaded.Container(chest.Index)!.Loot);
            Assert.Equal(1u, play.Game.Party.Gold);

            // A container's damage is the end of it.
            play.Use.Accept();
            Assert.Null(play.Use.Popup);
            Assert.Empty(play.Host.Teleports);

            // Loaded again, it's still destroyed if its exploded flag says so; the opened flag, when it's the same one, is checked first.
            // The original remembers nothing else, so a chest with no exploded flag is whole again.
            var again = Load(play, map);
            if (chest.Exploded is null)
            {
                comeBack.Add(MapRecord.VoxelId(map.Id, chest.Index).ToString());
                Assert.True(again.IsActive(chest.Index));
                Assert.Equal(ContainerLook.Closed, again.Look(scene.Index));
                continue;
            }
            Assert.False(again.IsActive(chest.Index));
            Assert.Equal(chest.Opened == chest.Exploded ? ContainerLook.Opened : ContainerLook.Destroyed, again.Look(scene.Index));
        }
        Assert.Equal(["aidyn:voxel/0x0F88/40", "aidyn:voxel/0x10CC/37", "aidyn:voxel/0x1119/16"], comeBack);
    }

    // Locks.

    private static string Beyond(int level) => level < 3 ? "a little" : level < 6 ? "definitely" : "far";

    [Fact]
    public void ALockedContainerAlaronCannotPickShowsHowFarBeyondHimItIsAndStaysLocked()
    {
        var locked = Data.Of<ContainerObject>().Where(c => c.Object.LockLevel > 0).ToList();
        Assert.Equal(117, locked.Count);
        var untrapped = locked.Where(c => c.Object.TrapLevel == 0).ToList();
        Assert.Equal(56, untrapped.Count);
        foreach (var (_, map, chest) in untrapped)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            Assert.Equal(0, play.Game.Party.Members.Single().Skills.Modified(Skill.Thief));

            play.Use.Use(loaded, chest);

            var popup = play.Use.Popup!;
            Assert.Equal(TrekPopupKind.Message, popup.Kind);
            Assert.Equal($"The lock is {Beyond(chest.LockLevel)} beyond the ability of Alaron to pick.", popup.Text);
            play.Use.Accept();
            Assert.Null(play.Use.Popup);
            Assert.Equal(chest.LockLevel, loaded.Container(chest.Index)!.LockLevel);
            Assert.True(loaded.IsActive(chest.Index));
            Assert.False(chest.Opened is { } opened && play.Flag(opened));
            Assert.True(play.Host.Idle);
        }
    }

    [Fact]
    public void ALockedDoorAlaronCannotPickShowsHowFarBeyondHimItIsAndStaysLocked()
    {
        var doors = Data.Of<TeleporterObject>().Where(t => t.Object.LockLevel > 0 && t.Object.TrapLevel == 0 && t.Object.SecretFlag is null).ToList();
        Assert.NotEmpty(doors);
        var shown = 0;
        foreach (var (_, map, door) in doors)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            var level = loaded.Teleporter(door.Index)!.LockLevel;

            play.Use.Use(loaded, door);

            if (level == 0)
            {
                // Its lock flag is already set in a new game.
                Assert.Equal([door], play.Host.Teleports);
                Assert.Null(play.Use.Popup);
                continue;
            }
            shown++;
            var popup = play.Use.Popup!;
            Assert.Equal($"The lock is {Beyond(level)} beyond the ability of Alaron to pick.", popup.Text);
            Assert.Empty(play.Host.Teleports);
            play.Use.Accept();
            Assert.Null(play.Use.Popup);
            Assert.Equal(level, loaded.Teleporter(door.Index)!.LockLevel);
            Assert.Empty(play.Host.Teleports);
        }
        Assert.True(shown > 0);
    }

    [Fact]
    public void ATrappedDoorsFailedDisarmGoesOnThroughItsLock()
    {
        var doors = Data.Of<TeleporterObject>().Where(t => t.Object.TrapLevel > 0).ToList();
        Assert.Equal(4, doors.Count);
        foreach (var (_, map, door) in doors)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            var teleporter = loaded.Teleporter(door.Index)!;
            var trap = teleporter.TrapLevel;
            var locked = teleporter.LockLevel;
            Assert.Equal(door.TrapLevel, trap);

            play.Use.Use(loaded, door);
            Assert.Equal($"There's a {Danger(trap)} dangerous trap!", play.Use.Popup!.Text);
            play.Use.Accept();
            Assert.Equal(TrekPopupKind.Damage, play.Use.Popup!.Kind);
            Assert.StartsWith("The trap exploded!", play.Use.Popup.Text);
            Assert.Empty(play.Host.Teleports);

            // Closing it sets the disarm flag and uses the door again.
            play.Use.Accept();
            if (door.DisarmFlag is { } disarmed)
                Assert.True(play.Flag(disarmed));
            Assert.Equal(0, teleporter.TrapLevel);
            if (locked != 0)
            {
                Assert.Equal($"The lock is {Beyond(locked)} beyond the ability of Alaron to pick.", play.Use.Popup!.Text);
                Assert.Empty(play.Host.Teleports);
            }
            else
            {
                Assert.Null(play.Use.Popup);
                Assert.Equal([door], play.Host.Teleports);
            }
        }
    }

    // Secret doors.

    [Fact]
    public void ASecretDoorIsFoundWhenUsedAndThenTakesThePlayerThrough()
    {
        var secrets = Data.Of<TeleporterObject>().Where(t => t.Object.SecretFlag is not null).ToList();
        Assert.Equal(11, secrets.Count);
        Assert.Equal(["EhudUnder"], secrets.Select(s => Placed(s.Map).Set.Label).Distinct());
        Assert.Equal(["C11", "E03", "E11", "I09"], secrets.Select(s => Placed(s.Map).Chunk.Name).Distinct().Order());
        // None of them has a lock, a trap or a level of skill to find.
        Assert.All(secrets, s => Assert.Equal((0, 0, 0), (s.Object.LockLevel, s.Object.TrapLevel, s.Object.SecretLevel)));

        foreach (var (_, map, door) in secrets)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            Assert.False(play.Flag(door.SecretFlag!.Value));
            Assert.Equal(0, loaded.Teleporter(door.Index)!.SecretLevel);

            play.Use.Use(loaded, door);

            Assert.Equal(TrekPopupKind.Message, play.Use.Popup!.Kind);
            Assert.Equal("You've just discovered a secret door!", play.Use.Popup.Text);
            Assert.True(play.Flag(door.SecretFlag!.Value));
            Assert.Empty(play.Host.Teleports);

            play.Use.Accept();
            Assert.Null(play.Use.Popup);
            Assert.Equal([door], play.Host.Teleports);

            // Found, it goes straight through.
            play.Use.Use(loaded, door);
            Assert.Null(play.Use.Popup);
            Assert.Equal([door, door], play.Host.Teleports);
        }
    }

    // Sources.

    /// <summary>76 of the 85 sources are there in a new game, and none of them is there still once its used flag is written.</summary>
    [Fact]
    public void UsingASourceOfHerbsSpicesOrGemsGivesAPileOfItsReagentAndUsesItUp()
    {
        var sources = Data.Of<ContainerObject>().Where(c => c.Object.Kind is >= ContainerKind.Herb and <= ContainerKind.Gem).ToList();
        Assert.Equal(85, sources.Count);
        var used = 0;
        var staying = new List<string>();
        foreach (var (id, map, source) in sources)
        {
            var play = NewGame();
            var loaded = Load(play, map);
            if (!loaded.IsActive(source.Index))
                continue;
            used++;
            var rules = play.Game.Items.Rules;
            var reagent = source.Kind switch { ContainerKind.Herb => rules.Herb, ContainerKind.Spice => rules.Spice, _ => rules.Gemstone };

            play.Use.Use(loaded, source);

            var (loot, gold) = Assert.Single(play.Host.Loots);
            Assert.Equal(0, gold);
            var stack = Assert.Single(loot.Stacks);
            Assert.Equal(reagent, stack.Item.Id);
            Assert.InRange(stack.Quantity, 1, Math.Max(1, source.Gold));
            if (source.Used is { } write)
                Assert.Equal(!write.Clear, play.Flag(write.Flag));
            Assert.Equal(1u, play.Game.Party.Gold);
            Assert.Null(play.Use.Popup);
            if (loaded.IsActive(source.Index))
                staying.Add(id.ToString());
        }
        Assert.Equal(76, used);
        Assert.Empty(staying);
    }

    // ChangeCollision triggers.

    private IEnumerable<(MapRecord Map, TriggerObject Trigger)> ChangeCollisionTriggers() =>
        Data.Of<TriggerObject>().Where(t => t.Object.Kind == TriggerKind.ChangeCollision).Select(t => (t.Map, t.Object));

    /// <summary>Four of the nine are in the Barrows, which the others' maps work the same as.</summary>
    [Fact]
    public void AChangeCollisionTriggerMakesItsTogglesPassableAndNoOthers()
    {
        var triggers = ChangeCollisionTriggers().ToList();
        Assert.Equal(9, triggers.Count);
        Assert.Equal(4, triggers.Count(t => Placed(t.Map).Set.Label == "Barrows"));
        foreach (var (map, trigger) in triggers)
        {
            var (set, chunk) = Placed(map);
            var origin = set.Origin(chunk.Row, chunk.Column);
            var triangles = Collision(map).Where(t => !IsSliver(t)).ToList();
            var keep = trigger.KeepFlags!.Value;
            var add = trigger.AddFlags!.Value;
            var toggled = triangles.Where(t => t.Role == CollisionRole.Toggle && t.Target == trigger.Toggle).ToList();
            Assert.NotEmpty(toggled);
            Assert.Contains(toggled, t => !t.Flags.HasFlag(CollisionFlags.Passable));
            // The trigger's flags are 0xFFFF and 1: it only makes the way passable.
            Assert.Equal((0xFFFF, (int)CollisionFlags.Passable), (keep, add));

            var play = NewGame();
            var loaded = Load(play, map);
            var world = new CollisionWorld();
            world.Add(loaded.Key, triangles, origin);
            play.Use.World = world;
            var otherIds = triangles.Where(t => t.Role == CollisionRole.Toggle && t.Target != trigger.Toggle).Select(t => t.Target).Distinct().ToList();
            int NotPassable(int id) => triangles.Count(t => t.Role == CollisionRole.Toggle && t.Target == id && !t.Flags.HasFlag(CollisionFlags.Passable));

            play.Use.Trigger(loaded, trigger);

            // What's left to make passable: nothing of this toggle's, and all of the others'.
            Assert.Equal(0, world.ChangeToggles(loaded.Key, trigger.Toggle!.Value, keep, add));
            foreach (var other in otherIds)
                Assert.Equal(NotPassable(other), world.ChangeToggles(loaded.Key, other, keep, add));

            // A body can pass through each of the toggle's triangles, where it was blocked before.
            foreach (var t in toggled.Where(t => !t.Flags.HasFlag(CollisionFlags.Passable)))
            {
                var alone = NewGame();
                var only = new CollisionWorld();
                only.Add(loaded.Key, [t], origin);
                alone.Use.World = only;
                Assert.True(Blocks(only, t, origin), $"{map.Id} toggle {trigger.Toggle} blocks before");
                alone.Use.Trigger(loaded, trigger);
                Assert.False(Blocks(only, t, origin), $"{map.Id} toggle {trigger.Toggle} lets through after");
            }
        }
    }

    private static bool IsSliver(CollisionTriangle t) => Vector3.Cross(t.B - t.A, t.C - t.A).Length() < 1e-6f;

    /// <summary>
    /// Whether a sphere pressed into the middle of the triangle's front touches it. It starts 0.35 off and moves 0.05 in, which
    /// is where the physics' test of the centre against the triangle looks straight at its middle, whatever way it faces.
    /// </summary>
    private static bool Blocks(CollisionWorld world, CollisionTriangle t, Vector3 origin)
    {
        var normal = Vector3.Normalize(Vector3.Cross(t.B - t.A, t.C - t.A));
        var centre = (t.A + t.B + t.C) / 3 + origin;
        var sphere = new CollisionSphere { Radius = 0.5f, Position = centre + normal * 0.35f, Velocity = -normal * 0.05f };
        SpherePhysics.Step(world, sphere);
        return sphere.Touching;
    }

    // Arrivals.

    /// <summary>A chunk's collision and its neighbours', as the runtime holds them around a player.</summary>
    private CollisionWorld Around(MapSetRecord set, ChunkEntry chunk)
    {
        if (_worlds.TryGetValue((set.Id, chunk.Row, chunk.Column), out var world))
            return world;
        world = new CollisionWorld();
        foreach (var near in set.Chunks.Where(c => Math.Abs(c.Row - chunk.Row) <= 1 && Math.Abs(c.Column - chunk.Column) <= 1))
            world.Add(near.Map, Collision(Data.Set.Get<MapRecord>(near.Map)), set.Origin(near.Row, near.Column));
        return _worlds[(set.Id, chunk.Row, chunk.Column)] = world;
    }

    [Fact]
    public void EveryTeleporterLeadsToAChunkArrivingAtItsReferencePointOrItsOwnPosition()
    {
        var teleporters = Data.Of<TeleporterObject>().ToList();
        Assert.Equal(894, teleporters.Count);
        var withoutPoint = new List<string>();
        var withoutCamera = new List<string>();
        foreach (var (id, _, t) in teleporters)
        {
            var arrival = TeleportArrival.For(Data.Set, t);
            Assert.NotNull(arrival);
            Assert.Equal(t.Destination.MapSet, arrival.Set.Id);
            Assert.Equal((t.Destination.Row, t.Destination.Column), (arrival.Chunk.Row, arrival.Chunk.Column));

            if (t.Arrival is { } point)
            {
                var reference = (ReferencePointObject)Data.Objects[point];
                Assert.Equal(reference.Position, arrival.Position);
                var flat = reference.Facing is { } f ? new Vector2(f.X, f.Z) : Vector2.Zero;
                if (flat.Length() > 1e-6f)
                    Assert.Equal(Vector2.Normalize(flat), arrival.Heading);
                else
                    Assert.Null(arrival.Heading);
            }
            else
            {
                withoutPoint.Add($"{id} {t.Name}");
                Assert.Equal(t.Position, arrival.Position);
                Assert.Null(arrival.Heading);
            }

            if (t.Camera is { } camera)
                Assert.Equal(((ReferencePointObject)Data.Objects[camera]).Position, arrival.Camera);
            else
                withoutCamera.Add($"{id} {t.Name}");
        }
        Assert.Equal(["aidyn:voxel/0x1871/0 patioL2"], withoutPoint);
        // 197 teleporters name a camera point, and the rest leave the camera where it is.
        Assert.Equal(697, withoutCamera.Count);
    }

    [Fact]
    public void TheOneTeleporterWhosePointIsMissingArrivesAtItsOwnPosition()
    {
        var (_, _, patio) = Data.Of<TeleporterObject>().Single(t => t.Object.Name == "patioL2" && Placed(t.Map) is { Set.Label: "JundarInteriors", Chunk.Name: "K07" });
        Assert.Null(patio.Arrival);
        var arrival = TeleportArrival.For(Data.Set, patio)!;
        Assert.Equal(patio.Position, arrival.Position);
    }

    /// <summary>Settling is the original's sweep for ground under the arrival point, in the destination chunk and its eight neighbours.</summary>
    [Fact]
    public void EveryArrivalSettlesOntoGroundExceptSixThatMissItsSweep()
    {
        var floating = new List<string>();
        var total = 0;
        foreach (var (id, _, t) in Data.Of<TeleporterObject>())
        {
            var arrival = TeleportArrival.For(Data.Set, t)!;
            var world = Around(arrival.Set, arrival.Chunk);
            var point = arrival.Set.Origin(arrival.Chunk.Row, arrival.Chunk.Column) + arrival.Position;
            total++;
            var (settled, grounded) = Body.SetDown(world, point, 0.5f, null, near: point.Y - 0.5f);
            if (!grounded)
                floating.Add($"{id} {t.Name}");
            else
                Assert.True(new Vector2(settled.X - point.X, settled.Z - point.Z).Length() <= 0.5f + 1e-3f);
        }
        Assert.Equal(894, total);
        // Four have a thin floor triangle under them that the sphere's front point misses, since the
        // original tests that point, a radius off the arrival's; with a smaller radius they settle. The other
        // two, into the Ehud's tower chunks Q03 and E09, have no ground at all under them.
        Assert.Equal(
        [
            "aidyn:voxel/0x0F01/14 entry",
            "aidyn:voxel/0x0F01/16 jumpBdown",
            "aidyn:voxel/0x12AC/3 fromErromon",
            "aidyn:voxel/0x1530/1 SageExit",
            "aidyn:voxel/0x16EA/1 doorfromF",
            "aidyn:voxel/0x1956/1 doorfromDE",
        ], floating);
    }
}
