using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using HBHT.Rom.Import;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Tests;

/// <summary>
/// The Roog Cave's mushroom (docs/hardcoded-content.md#corrections-to-the-roms-maps): where the ROM puts it, in the neck of D01's
/// passage, Alaron wedges against it walking either way and often running north to south; the importer moves it, the user's
/// choice, and he passes it every way he moves.
/// </summary>
public sealed partial class GameSessionTests
{
    /// <summary>The way through the neck from the south, in D01's coordinates, west of the mushroom wherever it is.</summary>
    private static readonly Vector2[] RoogNeck =
        [new(12.4f, 20.0f), new(11.2f, 20.5f), new(10.85f, 21.5f), new(10.5f, 22.4f), new(10.0f, 23.0f), new(9.3f, 23.9f), new(9.0f, 24.6f), new(9.0f, 25.6f)];

    [Fact]
    public void TheRoogCavesMushroomIsMovedWithItsCollisionBox()
    {
        var moved = MapCorrections.RoogCaveMushroom;
        var (chunk, rom) = RoogD01();
        var map = RecordId.Hex(RecordTypes.Map, chunk.Collision);
        var mushroom = (SceneObject)Data.Set.Get<MapRecord>(map).Objects.Single(o => o.Index == moved.Object);
        Assert.Equal(RecordId.Hex(RecordTypes.Scene, moved.Scene), mushroom.Models[0].Model);
        Assert.Equal(rom.Objects[moved.Object].Position + moved.By, mushroom.Position);

        // Its 8 triangles move with it and no others do.
        var collision = CollisionMesh.For(Data.Set, map);
        Assert.Equal(rom.Triangles.Count, collision.Count);
        for (var t = 0; t < collision.Count; t++)
        {
            var by = t >= moved.FirstTriangle && t < moved.FirstTriangle + moved.Triangles ? moved.By : Vector3.Zero;
            var expected = rom.Triangles[t];
            Assert.Equal((rom.Vertices[expected.A] + by, rom.Vertices[expected.B] + by, rom.Vertices[expected.C] + by),
                (collision[t].A, collision[t].B, collision[t].C));
        }
    }

    [Fact]
    public void AlaronWalksRunsAndSneaksPastTheRoogCavesMushroomBothWaysWhereTheROMsWedgesHim()
    {
        var (chunk, rom) = RoogD01();
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, (int)MapSet.RoogCave));
        var entry = set.Chunks.Single(c => c.Name == "D01");
        CollisionWorld World(IEnumerable<CollisionTriangle> triangles)
        {
            var world = new CollisionWorld();
            world.Add(new ChunkKey(set.Id, entry.Row, entry.Column), triangles, set.Origin(entry.Row, entry.Column));
            return world;
        }
        var imported = World(CollisionMesh.For(Data.Set, entry.Map));
        var original = World(CollisionMesh.Read(CollisionGltf.Export([(chunk, rom)], placeInSet: false)));

        var gaits = new (string Name, MoveInput Input)[]
        {
            ("run", new MoveInput(new Vector2(0, 1))), ("walk", new MoveInput(new Vector2(0, 0.6f))), ("sneak", new MoveInput(new Vector2(0, 1), Sneak: true)),
        };
        var stuck = new List<string>();
        var romStuck = new List<string>();
        foreach (var (name, input) in gaits)
        foreach (var northward in new[] { true, false })
        foreach (var side in new[] { -0.2f, -0.1f, 0f, 0.1f, 0.2f })
        {
            var way = northward ? RoogNeck : [.. RoogNeck.Reverse()];
            var what = $"{name} {(northward ? "north" : "south")} from {side}";
            if (ThroughRoogNeck(imported, set.Origin(entry.Row, entry.Column), way, side, input) is { } at)
                stuck.Add($"{what}: held at {at}");
            if (ThroughRoogNeck(original, set.Origin(entry.Row, entry.Column), way, side, input) is not null)
                romStuck.Add(what);
        }
        Assert.Empty(stuck);
        // Where the ROM has it, walking and sneaking wedge him beside it every way, and running south from most starts.
        foreach (var gait in new[] { "walk north", "walk south", "sneak north", "sneak south" })
            Assert.Equal(5, romStuck.Count(s => s.StartsWith(gait, StringComparison.Ordinal)));
        Assert.InRange(romStuck.Count(s => s.StartsWith("run south", StringComparison.Ordinal)), 3, 5);
    }

    /// <summary>
    /// Alaron, with his run animation, set down at the way's start moved <paramref name="side"/> across it, steering for each of its
    /// points in turn: null once he's through, or where he's held after 15 seconds.
    /// </summary>
    private Vector3? ThroughRoogNeck(CollisionWorld world, Vector3 origin, Vector2[] way, float side, MoveInput input)
    {
        var toLocal = new Vector2(origin.X, origin.Z);
        var heading = Vector2.Normalize(way[1] - way[0]);
        var start = way[0] + new Vector2(-heading.Y, heading.X) * side;
        var player = new PlayerMotion(origin + new Vector3(start.X, PlayerMotion.Radius + 0.01f, start.Y), heading);
        var animation = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
        for (var i = 0; i < 10; i++)
            player.Step(world, new MoveInput(Vector2.Zero), heading, animation);
        var next = 1;
        for (var i = 0; i < 900; i++)
        {
            var at = new Vector2(player.Sphere.Position.X, player.Sphere.Position.Z) - toLocal;
            if (Vector2.Distance(at, way[^1]) < 0.5f)
                return null;
            while (next < way.Length - 1 && Vector2.Distance(at, way[next]) < 0.5f)
                next++;
            // Up on the stick, the camera looking at the next point.
            player.Step(world, input, Vector2.Normalize(way[next] - at), animation);
            Assert.False(player.FellOut);
        }
        return player.Sphere.Position - origin;
    }

    /// <summary>The Roog Cave's D01 as the ROM has it: its chunk and its Borg9, parsed.</summary>
    private static (MapChunk Chunk, Borg9Map Map) RoogD01()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var chunk = MapChunks.Read(rom).Single(c => c.Set == MapSet.RoogCave && c.Name == "D01");
        return (chunk, Borg9Map.Parse(Borg.BorgArchive.Open(rom).Read(chunk.Collision)));
    }
}
