using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using HBHT.Rom.Maps;

namespace HBHT.Rom.Tests;

/// <summary>
/// Modern movement where the original's is known to go wrong or be awkward (docs/systems/movement.md#modern-movement): Q25's
/// plain faces stay walls, its sand ramp and the Wizard's School's steeper stairs are climbed, and the Roog Cave's mushroom.
/// </summary>
public sealed partial class GameSessionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(SessionSettings.BriskPace)]
    public void InModernMovementAlaronNeverClimbsQ25sSteepPlainFaces(float pace)
    {
        // The user's climb out of the world, to 8.6 and on, and the face east of it Brisk climbed when only his steps ran faster:
        // running into them for 10 seconds, he slides along their foot and up the sand ramp beside them, never above its top.
        foreach (var (from, toward) in new[] { (new Vector3(91.9f, 2.2f, 45.7f), new Vector2(90.6f, 41.0f)), (new Vector3(95.1f, 3.2f, 48.9f), new Vector2(94.83f, 45.9f)) })
        {
            var (world, origin) = Loaded(0, "Q25", from);
            var way = Vector2.Normalize(toward - new Vector2(from.X, from.Z));
            var player = new PlayerMotion(origin + from, way) { Movement = MovementMode.Modern, Pace = pace };
            var run = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
            for (var i = 0; i < 600; i++)
            {
                player.Step(world, new MoveInput(new Vector2(0, 1)), way, run);
                Assert.True(player.Sphere.Position.Y - origin.Y < 5, $"climbed to {player.Sphere.Position - origin}");
                Assert.False(player.FellOut);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(SessionSettings.BriskPace)]
    public void InModernMovementAlaronRunsUpQ25sSandRampWithoutLeavingIt(float pace)
    {
        // Sand (surface 5) of normal Y 0.48 and 0.79, meant to be climbed, from its foot to its top at about (98.5, 4.4, 44.3)
        // in three seconds, on the ground all the way.
        var (world, origin) = Loaded(0, "Q25", new Vector3(88.3f, -1.2f, 48.0f));
        var way = Vector2.Normalize(new Vector2(98.5f - 88.3f, 44.3f - 48.0f));
        var player = new PlayerMotion(origin + new Vector3(88.3f, -1.2f, 48.0f), way) { Movement = MovementMode.Modern, Pace = pace };
        var run = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
        for (var i = 0; i < 20; i++)
            player.Step(world, new MoveInput(Vector2.Zero), way, run);
        for (var i = 0; i < 180 && Vector2.Distance(Local(player, origin), new Vector2(98.5f, 44.3f)) > 0.5f; i++)
        {
            player.Step(world, new MoveInput(new Vector2(0, 1)), way, run);
            Assert.True(player.Grounded, $"off the ground at {player.Sphere.Position - origin}");
        }
        Assert.InRange(Vector2.Distance(Local(player, origin), new Vector2(98.5f, 44.3f)), 0, 0.5f);
        Assert.InRange(player.Feet.Y - origin.Y, 3.5f, 4.5f);
    }

    [Theory]
    [InlineData(MovementMode.Original, 150)]
    [InlineData(MovementMode.Modern, 90)]
    public void AlaronClimbsTheWizardsSchoolsSteeperStairs(MovementMode movement, int ticks)
    {
        // JundarInteriors A01's first flight, triangles 170 and 171, of normal Y 0.71: 2.6 up over 2.6 toward −X. The original
        // climbs it at half his pace; Modern at all of it.
        var (world, origin) = Loaded(11, "A01", new Vector3(17.5f, 0.6f, 22.2f));
        var way = -Vector2.UnitX;
        var player = new PlayerMotion(origin + new Vector3(17.5f, 0.6f, 22.2f), way) { Movement = movement };
        var run = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
        for (var i = 0; i < 20; i++)
            player.Step(world, new MoveInput(Vector2.Zero), way, run);
        var top = 0f;
        for (var i = 0; i < ticks; i++)
        {
            player.Step(world, new MoveInput(new Vector2(0, 1)), way, run);
            top = MathF.Max(top, player.Feet.Y - origin.Y);
            // Modern on the ground all the way; the original leaves it.
            Assert.True(movement == MovementMode.Original || player.Grounded, $"off the ground at {player.Sphere.Position - origin}");
        }
        Assert.InRange(top, 2.5f, 2.7f);
    }

    [Fact]
    public void InModernMovementAlaronPassesTheMovedRoogCaveMushroomEveryWayAndWedgesWhereTheROMHasIt()
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
        var stuck = new List<string>();
        foreach (var (world, which) in new[] { (imported, "moved"), (original, "the ROM's") })
        foreach (var (name, input) in new[] { ("run", new MoveInput(new Vector2(0, 1))), ("walk", new MoveInput(new Vector2(0, 0.6f))),
                     ("sneak", new MoveInput(new Vector2(0, 1), Sneak: true)) })
        foreach (var northward in new[] { true, false })
        foreach (var side in new[] { -0.2f, -0.1f, 0f, 0.1f, 0.2f })
        {
            var way = northward ? RoogNeck : [.. RoogNeck.Reverse()];
            if (ThroughRoogNeck(world, set.Origin(entry.Row, entry.Column), way, side, input, MovementMode.Modern) is { } at)
                stuck.Add($"{which}: {name} {(northward ? "north" : "south")} from {side}");
        }
        // Moved, as the importer does by default, he passes every way; where the ROM has it, every way wedges him, where the
        // original's running passes, its walls lifting him the higher he squeezes.
        Assert.DoesNotContain(stuck, s => s.StartsWith("moved", StringComparison.Ordinal));
        Assert.Equal(30, stuck.Count);
    }

    [Fact]
    public void TheReachabilityCheckFindsOnlyAPlainFloorClimbingQ25sPlainFaces()
    {
        // From the foot of the sand ramp and of the user's climb, within the 28 × 25 about them: the original's movement and Modern
        // reach the ramp's top at about 4.4 and no higher, while Modern with plain collision for ground walks up the faces beside it
        // and out of the world, above 10.
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var chunk = set.Chunks.Single(c => c.Name == "Q25");
        var origin = set.Origin(chunk.Row, chunk.Column);
        (Vector3, float?)[] starts =
            [(origin + new Vector3(88.3f, -1.2f, 48.0f), null), (origin + new Vector3(91.9f, 2.2f, 45.7f), null), (origin + new Vector3(95.1f, 3.2f, 48.9f), null)];
        var within = (origin + new Vector3(78, -20, 33), origin + new Vector3(106, 30, 58));
        int Top(Reach.Options options) => Reach.Explore(Data.Set, set, starts, options with { Within = within }).Cells.Max(c => c.Y) - (int)origin.Y;

        Assert.Equal(4, Top(new Reach.Options(MovementMode.Original)));
        Assert.Equal(4, Top(new Reach.Options(MovementMode.Modern)));
        Assert.Equal(4, Top(new Reach.Options(MovementMode.Modern, SessionSettings.BriskPace)));
        Assert.True(Top(new Reach.Options(MovementMode.Modern, Rules: ModernRules.Default with { PlainIsWall = false })) > 10);

        var original = Reach.Explore(Data.Set, set, starts, new Reach.Options(MovementMode.Original, Within: within));
        Assert.Empty(original.Beyond(original));
    }

    /// <summary>A map set's collision with the 3 × 3 chunks about a point of a chunk loaded, and the chunk's origin.</summary>
    private (CollisionWorld World, Vector3 Origin) Loaded(int mapSet, string chunkName, Vector3 local)
    {
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, mapSet));
        var chunk = set.Chunks.Single(c => c.Name == chunkName);
        var origin = set.Origin(chunk.Row, chunk.Column);
        var world = new CollisionWorld();
        new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), world, set).Update(origin + local);
        return (world, origin);
    }

    private static Vector2 Local(PlayerMotion player, Vector3 origin) =>
        new(player.Sphere.Position.X - origin.X, player.Sphere.Position.Z - origin.Z);
}
