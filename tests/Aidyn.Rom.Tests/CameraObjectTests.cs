using System.Collections.Concurrent;
using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Cameras;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>Camera objects: what NA 1.0's data holds, which chunks use the interiors' camera, and what touching and treading them does.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CameraObjectTests(Na10Import fixture)
{
    private static readonly ConcurrentDictionary<string, IReadOnlyList<CollisionTriangle>> Collisions = new();

    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");
    private static readonly RecordId GwerniaInteriors = RecordId.Parse("aidyn:mapset/5");

    private const CameraFlags Moves = CameraFlags.Cut | CameraFlags.Pan | CameraFlags.Sine | CameraFlags.EaseIn | CameraFlags.EaseOut;

    private Na10Import.Loaded Data => fixture.Data;

    /// <summary>Does nothing with what map objects ask of it: these tests only watch the camera.</summary>
    private sealed class IdleHost : IMapObjectHost
    {
        public void Teleport(LoadedMap from, TeleporterObject teleporter) { }

        public void OpenLoot(ItemPile loot, int gold) { }

        public void StartScript(LoadedMap map, DialogueObject dialogue) { }

        public void Animate(LoadedMap map, SceneObject actor, int request) { }

        public void Changed(LoadedMap map, SceneObject scene) { }

        public void Sound(Aidyn.Core.Audio.OneShot sound) { }
    }

    private IReadOnlyList<CollisionTriangle> Collision(MapRecord map) =>
        Collisions.GetOrAdd(Data.Set.FindAsset(map.Id, "glb")!, path => CollisionMesh.Read(File.ReadAllBytes(path)));

    private IEnumerable<(MapRecord Map, CameraObject Camera)> Cameras() =>
        Data.Maps.SelectMany(m => m.Objects.OfType<CameraObject>().Select(c => (Map: m, Camera: c)));

    /// <summary>Which cameras trigger triangles name, by map and index, and how many triangles name them.</summary>
    private Dictionary<(RecordId Map, int Index), int> NamedByTriangles()
    {
        var named = new Dictionary<(RecordId, int), int>();
        foreach (var map in Data.Maps)
        {
            foreach (var triangle in Collision(map).Where(t => t.Role == CollisionRole.Trigger && map.Objects.Any(o => o.Index == t.Target && o is CameraObject)))
                named[(map.Id, triangle.Target)] = named.GetValueOrDefault((map.Id, triangle.Target)) + 1;
        }
        return named;
    }

    private static bool ListedByAGridCell(MapRecord map, CameraObject camera) => map.Grid is { } grid && grid.Cells.Any(c => c.Contains(camera.Index));

    private MapRecord Chunk(RecordId set, int row, int column) =>
        Data.Set.Get<MapRecord>(Data.Chunk(new ChunkRef(set, row, column))!.Map);

    private ReferencePointObject RefPoint(MapRecord map, CameraObject camera) => (ReferencePointObject)map.Objects.Single(o => o.Index == MapRecord.IndexOf(camera.RefPoint!.Value));

    private sealed class Play
    {
        public required GameState Game { get; init; }
        public required MapObjectUse Use { get; init; }
        public required FollowCamera Camera { get; init; }
        public required LoadedMap Map { get; init; }
        public required Vector3 Origin { get; init; }
    }

    /// <summary>A new game with a chunk loaded at <paramref name="origin"/>, and a camera for its camera objects to move.</summary>
    private Play Start(MapRecord map, Vector3 origin)
    {
        var game = GameState.NewGame(Data.Set);
        var use = new MapObjectUse(game, MenuText.From(Data.Set), new IdleHost());
        var camera = new FollowCamera(origin + new Vector3(10, 1, 10), Vector2.UnitX);
        use.Camera = camera;
        var loaded = LoadedMap.Activate(map, map.Id, game.Flags, game.Memory, game.Party, new Lcg(), origin);
        return new Play { Game = game, Use = use, Camera = camera, Map = loaded, Origin = origin };
    }

    [Fact]
    public void ThereAre244CameraObjectsSplitBetweenThoseAGridListsAndThoseTriangleNames()
    {
        var all = Cameras().ToList();
        Assert.Equal(244, all.Count);
        var named = NamedByTriangles();

        var listed = all.Where(c => ListedByAGridCell(c.Map, c.Camera)).ToList();
        var byTriangle = all.Where(c => named.ContainsKey((c.Map.Id, c.Camera.Index))).ToList();
        Assert.Equal(182, listed.Count);
        Assert.Equal(62, byTriangle.Count);

        // No camera is both, and none is neither.
        Assert.DoesNotContain(listed, c => named.ContainsKey((c.Map.Id, c.Camera.Index)));
        Assert.DoesNotContain(byTriangle, c => ListedByAGridCell(c.Map, c.Camera));

        Assert.All(listed, c => Assert.True(c.Camera.Radius < 200, $"{MapRecord.VoxelId(c.Map.Id, c.Camera.Index)}: {c.Camera.Radius}"));
        Assert.All(byTriangle, c => Assert.Equal(200, c.Camera.Radius));
        Assert.Equal(1532, named.Values.Sum());
    }

    [Fact]
    public void EveryCameraThatMovesTheAimOrPositionHasAMoveTypeAndAReferencePointOfItsOwnMap()
    {
        var moving = Cameras().Where(c => (c.Camera.Flags & (CameraFlags.Aim | CameraFlags.Position)) != 0).ToList();
        var still = Cameras().Where(c => (c.Camera.Flags & (CameraFlags.Aim | CameraFlags.Position)) == 0).ToList();
        Assert.Equal(244 - 28, moving.Count);
        Assert.Equal(28, still.Count);

        foreach (var (map, camera) in moving)
        {
            var id = MapRecord.VoxelId(map.Id, camera.Index);
            Assert.True((camera.Flags & Moves) != 0, $"{id} has no move type: {camera.Flags}");
            Assert.NotNull(camera.RefPoint);
            Assert.Equal(map.Id, MapRecord.MapOf(camera.RefPoint!.Value));
            Assert.IsType<ReferencePointObject>(map.Objects.Single(o => o.Index == MapRecord.IndexOf(camera.RefPoint.Value)));
        }

        // The rest only set how the outdoor camera follows.
        Assert.All(still, c =>
        {
            Assert.Equal(CameraFlags.Distance | CameraFlags.Pitch, c.Camera.Flags);
            Assert.Null(c.Camera.RefPoint);
        });
    }

    [Fact]
    public void NoCameraMovesBothTheAimAndThePosition()
    {
        Assert.All(Cameras(), c => Assert.NotEqual(CameraFlags.Aim | CameraFlags.Position, c.Camera.Flags & (CameraFlags.Aim | CameraFlags.Position)));
    }

    [Fact]
    public void TheGwerniaInteriorsOfTerrainSixteenOrEightUseTheInteriorsCameraAndTheOverworldNeverDoes()
    {
        var interiors = Data.MapSets.Single(s => s.Id == GwerniaInteriors).Chunks.Where(c => c.Terrain is 16 or 8).ToList();
        Assert.NotEmpty(interiors);
        Assert.All(interiors, c => Assert.Equal(CameraMode.Interior, FollowCamera.ModeFor(Data.Set, c)));

        var overworld = Data.MapSets.Single(s => s.Id == Overworld).Chunks;
        Assert.NotEmpty(overworld);
        Assert.All(overworld, c => Assert.Equal(CameraMode.Outdoor, FollowCamera.ModeFor(Data.Set, c)));
    }

    [Fact]
    public void ATerrainsModeIsInteriorExactlyForTerrainsSixEightNineElevenAndThirteenToSixteen()
    {
        int[] interior = [6, 8, 9, 11, 13, 14, 15, 16];
        var terrains = Data.Set.All<TerrainRecord>(RecordTypes.Terrain).ToList();
        Assert.NotEmpty(terrains);
        foreach (var terrain in terrains)
        {
            var number = int.Parse(terrain.Id.Key, System.Globalization.CultureInfo.InvariantCulture);
            var chunk = new ChunkEntry(1, 1, null, null, Data.Maps[0].Id, number);
            var expected = interior.Contains(number) ? CameraMode.Interior : CameraMode.Outdoor;
            Assert.True(expected == FollowCamera.ModeFor(Data.Set, chunk), $"terrain {number}");
        }
        // Every one of them has a terrain record.
        Assert.All(interior, t => Assert.Contains(terrains, r => r.Id.Key == t.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void HouseAsFirstCameraCutsToItsReferencePointWhereverTheChunkIsPlaced()
    {
        var map = Chunk(GwerniaInteriors, 1, 3);
        Assert.Equal(RecordId.Parse("aidyn:map/0x15BC"), map.Id);
        var camera = (CameraObject)map.Objects.Single(o => o.Index == 0);
        Assert.Equal(CameraFlags.Cut | CameraFlags.Track | CameraFlags.Position, camera.Flags);
        Assert.Equal(8, camera.Radius);
        Assert.Equal(3.3f, camera.Position.X, 0.1f);
        Assert.Equal(2.6f, camera.Position.Y, 0.1f);
        Assert.Equal(3.3f, camera.Position.Z, 0.1f);
        var point = RefPoint(map, camera);
        Assert.Equal(10, point.Index);
        Assert.Equal("M07_HosACam1", point.Name);
        Assert.Equal(4.28f, point.Position.X, 0.01f);
        Assert.Equal(3.15f, point.Position.Y, 0.01f);
        Assert.Equal(0.61f, point.Position.Z, 0.01f);
        var arrival = (ReferencePointObject)map.Objects.Single(o => o.Index == 9);
        Assert.Equal("Ahouse", arrival.Name);

        var origin = new Vector3(100, 0, 200);
        var play = Start(map, origin);
        var at = arrival.Position + new Vector3(0, 0.5f, 0);
        Assert.Contains(0, map.Grid!.At(at));

        play.Use.Touch(play.Map, at);

        var expected = origin + point.Position;
        Assert.Equal(expected.X, play.Camera.Position.X, 1e-3f);
        Assert.Equal(expected.Y, play.Camera.Position.Y, 1e-3f);
        Assert.Equal(expected.Z, play.Camera.Position.Z, 1e-3f);
        Assert.True(play.Camera.Held);
        Assert.Same(camera, play.Use.CurrentCamera);

        // It tracks the player, in the world, from then on.
        var world = origin + at;
        Assert.Equal(world.X, play.Camera.Aim.X, 1e-3f);
        Assert.Equal(world.Y + 1.05f, play.Camera.Aim.Y, 1e-3f);
    }

    /// <summary>Touching a camera's chunk at a point outside its radius leaves the camera be.</summary>
    [Fact]
    public void HouseAsCameraLeavesTheCameraBeOutsideItsRadius()
    {
        var map = Chunk(GwerniaInteriors, 1, 3);
        var play = Start(map, new Vector3(100, 0, 200));
        var before = play.Camera.Position;
        play.Use.Touch(play.Map, new Vector3(60, 0.5f, 60));
        Assert.Equal(before, play.Camera.Position);
        Assert.False(play.Camera.Held);
        Assert.Null(play.Use.CurrentCamera);
    }

    [Fact]
    public void TheInnsTriangleCamerasHoldTheCameraEaseToTheirPointAndLetGoASecondAfterTheLastTread()
    {
        var map = Chunk(GwerniaInteriors, 1, 7);
        Assert.Equal(RecordId.Parse("aidyn:map/0x15E2"), map.Id);
        var cameras = map.Objects.OfType<CameraObject>().ToList();
        Assert.Equal([0, 1, 2, 3], cameras.Select(c => c.Index));
        Assert.All(cameras, c =>
        {
            Assert.Equal(CameraFlags.Sine | CameraFlags.Track | CameraFlags.Position, c.Flags);
            Assert.Equal(60, c.Duration);
        });
        var point = RefPoint(map, cameras[0]);
        Assert.Equal(40, point.Index);
        Assert.Equal("M06_InnCam1", point.Name);
        Assert.Equal(16.7f, point.Position.X, 0.1f);
        Assert.Equal(3.0f, point.Position.Y, 0.1f);
        Assert.Equal(11.0f, point.Position.Z, 0.1f);

        var origin = new Vector3(300, 0, 100);
        var play = Start(map, origin);
        var world = new CollisionWorld();
        var inside = new Vector3(8, 0.5f, 8);
        var start = play.Camera.Position;
        // One tick as the runtime does it: the player moves, the trigger triangle is trodden, the camera objects step, the camera.
        bool Tick(bool tread)
        {
            play.Game.PlayTime++;
            if (tread)
                play.Use.Tread(play.Map, 0, inside);
            play.Use.StepCameras(origin + inside);
            var held = play.Camera.Held;
            play.Camera.Step(world, origin + inside, Vector2.UnitX);
            return held;
        }

        for (var k = 1; k <= 60; k++)
        {
            Assert.True(Tick(tread: true), $"tick {k}");
            var share = (1 - MathF.Cos(MathF.PI * k / 60)) / 2;
            var expected = start + (origin + point.Position - start) * share;
            Assert.Equal(expected.X, play.Camera.Position.X, 1e-2f);
            Assert.Equal(expected.Y, play.Camera.Position.Y, 1e-2f);
            Assert.Equal(expected.Z, play.Camera.Position.Z, 1e-2f);
            Assert.Same(cameras[0], play.Use.CurrentCamera);
        }
        Assert.Equal((origin + point.Position).X, play.Camera.Position.X, 1e-3f);

        // Still on the triangle, it keeps holding; leaving it, 60 ticks in all from the last tread (counting its own).
        for (var i = 0; i < 20; i++)
            Assert.True(Tick(tread: true));
        var after = 0;
        while (Tick(tread: false))
            Assert.InRange(++after, 1, 100);
        Assert.Equal(59, after);
        Assert.False(play.Camera.Held);
        // The follow camera has the camera again that tick, so it has moved off the point.
        Assert.NotEqual(origin + point.Position, play.Camera.Position);

        // CurrentCamera counts this tick and the last, so it's let go a tick after the camera is.
        Tick(tread: false);
        Assert.Null(play.Use.CurrentCamera);
    }

    [Fact]
    public void ClearingCamerasLetsGoOfATrianglesCameraAtOnce()
    {
        var map = Chunk(GwerniaInteriors, 1, 7);
        var play = Start(map, new Vector3(300, 0, 100));
        var inside = new Vector3(8, 0.5f, 8);
        play.Game.PlayTime++;
        play.Use.Tread(play.Map, 0, inside);
        play.Use.StepCameras(play.Origin + inside);
        Assert.True(play.Camera.Held);

        play.Use.ClearCameras();
        play.Camera.Step(new CollisionWorld(), play.Origin + inside, Vector2.UnitX);
        Assert.False(play.Camera.Held);
        play.Game.PlayTime++;
        play.Use.StepCameras(play.Origin + inside);
        Assert.False(play.Camera.Held);
    }

    [Fact]
    public void TheOverworldsA15FirstCameraEasesInToItsPointOverThirtyTicks()
    {
        var map = Chunk(Overworld, 1, 15);
        Assert.Equal(RecordId.Parse("aidyn:map/0x0EAA"), map.Id);
        var camera = (CameraObject)map.Objects.Single(o => o.Index == 0);
        Assert.Equal(CameraFlags.EaseIn | CameraFlags.Track | CameraFlags.Position, camera.Flags);
        Assert.Equal(0x8 | 0x2000 | 0x8000, (int)camera.Flags);
        Assert.Equal(30, camera.Duration);
    }
}
