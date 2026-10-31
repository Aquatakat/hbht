using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
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

    [Fact]
    public void TheReachabilityCheckFindsEveryBattleArenaSpawnReachableFromTheOthersUnderBothMovementsAndNothingBetweenThem()
    {
        // Every place combat makes a body (an arena's 40 spawns) can reach the party's first and the enemies' first on foot, under
        // the original's movement and Modern, so a combatant that walks from one to another is never stranded; nothing is reached
        // by one movement and not the other, at either pace; and every flee point is walked to within 1.5 but M11's flee3, which
        // stands on a rock nothing on the floor climbs, under either.
        var set = Data.Set.Get<MapSetRecord>(Arena.MapSet);
        var placements = Reach.ArenaPlacements(Data.Set).ToList();
        var arenas = placements.Select(p => p.Arena).Distinct().ToList();
        Assert.Equal(15, arenas.Count);
        var problems = new List<string>();
        foreach (var number in arenas)
        {
            var arena = Arena.For(Data.Set, number)!;
            var spawns = placements.Where(p => p.Arena == number && !p.Name.StartsWith("flee", StringComparison.Ordinal)).ToList();
            var flees = placements.Where(p => p.Arena == number && p.Name.StartsWith("flee", StringComparison.Ordinal)).ToList();
            Assert.Equal(40, spawns.Count);

            // Where each is set down.
            var world = new CollisionWorld();
            var streamer = new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), world, set);
            var cells = new List<Reach.Cell>();
            foreach (var spawn in spawns)
            {
                streamer.Update(spawn.At);
                var (ground, grounded) = Reach.SetDownAt(world, spawn.At, null, sideways: true);
                Assert.True(grounded, $"{spawn.Name} in arena {number} finds no ground");
                cells.Add(Reach.Cell.Of(ground - new Vector3(0, PlayerMotion.Radius, 0)));
            }

            foreach (var pace in new[] { 1f, SessionSettings.BriskPace })
            {
                var explored = new Dictionary<MovementMode, Reach>();
                foreach (var movement in new[] { MovementMode.Original, MovementMode.Modern })
                {
                    var options = new Reach.Options(movement, pace);
                    var all = Reach.Explore(Data.Set, set, [.. spawns.Select(p => (p.At, (float?)null))], options);
                    explored[movement] = all;
                    foreach (var flee in flees)
                    {
                        var at = new Vector2(flee.At.X, flee.At.Z);
                        if (!all.Cells.Any(c => Vector2.Distance(new Vector2(c.X + 0.5f, c.Z + 0.5f), at) <= 1.5f))
                            problems.Add($"{movement} at {pace}: arena {number} {flee.Name} is not walked to");
                    }
                    if (pace != 1)
                        continue;
                    foreach (var seed in new[] { spawns.First(p => p.Name.StartsWith("good", StringComparison.Ordinal)), spawns.First(p => p.Name.StartsWith("bad", StringComparison.Ordinal)) })
                    {
                        var from = Reach.Explore(Data.Set, set, [(seed.At, (float?)null)], options);
                        problems.AddRange(spawns.Where((p, i) => !from.Covers(cells[i])).Select(p => $"{movement}: arena {number} {p.Name} is not reached from {seed.Name}"));
                    }
                }
                problems.AddRange(explored[MovementMode.Modern].Beyond(explored[MovementMode.Original]).Select(c => $"{pace}: arena {number} only Modern reaches {c.Top} ({c.Cells.Count} cells)"));
                problems.AddRange(explored[MovementMode.Original].Beyond(explored[MovementMode.Modern]).Select(c => $"{pace}: arena {number} only the original reaches {c.Top} ({c.Cells.Count} cells)"));
            }
        }
        string[] expected = [.. new[] { MovementMode.Original, MovementMode.Modern }.SelectMany(m => new[] { 1f, SessionSettings.BriskPace }.Select(p => $"{m} at {p}: arena 22 flee3 is not walked to"))];
        Assert.Equal(expected.Order(StringComparer.Ordinal), problems.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void InModernMovementLettingGoMidStepStopsAlaronWithinAThirdOfAUnit()
    {
        // Let go at the start of a step of his run, the original carries him on through its 19 ticks, about 1.2 units,
        // before his stop plays in place. The rest of the step plays three times as fast, and carries him no more than 0.3.
        float Overrun(ModernRules rules, out int moving)
        {
            var (player, run) = StandingOnFlat(rules, Vector2.UnitY);
            for (var i = 0; i < 60; i++)
                player.Step(Flat, new MoveInput(Vector2.UnitY), Vector2.UnitY, run);
            var from = player.Sphere.Position;
            moving = 0;
            for (var i = 1; i <= 60; i++)
            {
                var before = player.Sphere.Position;
                player.Step(Flat, new MoveInput(Vector2.Zero), Vector2.UnitY, run);
                if (player.Sphere.Position != before)
                    moving = i;
            }
            return Vector3.Distance(from, player.Sphere.Position);
        }

        Assert.InRange(Overrun(ModernRules.Default, out var ticks), 0.1f, ModernRules.Default.StopReach + 1e-4f);
        Assert.InRange(ticks, 1, 7);
        Assert.True(Overrun(ModernRules.Default with { StopPace = 1, StopReach = float.PositiveInfinity }, out ticks) > 1.1f);
        Assert.InRange(ticks, 15, 20);
    }

    [Fact]
    public void InModernMovementAlaronTurnsBeforeSettingOffFromStanding()
    {
        // The stick pushed 170° from the way he faces: the original turns him 20° a frame as he runs, a curve to the side. He
        // turns in the three ticks his run stands still as it starts, and sets off straight.
        var stick = new Vector2(MathF.Sin(170 * MathF.PI / 180), MathF.Cos(170 * MathF.PI / 180));
        // Looking along +Z, right on the stick is −X.
        var way = stick with { X = -stick.X };
        float Aside(ModernRules rules)
        {
            var (player, run) = StandingOnFlat(rules, Vector2.UnitY);
            var from = player.Sphere.Position;
            for (var i = 0; i < 60; i++)
            {
                var before = player.Sphere.Position;
                player.Step(Flat, new MoveInput(stick), Vector2.UnitY, run);
                if (player.Sphere.Position != before && before == from && rules.SetOffTurn >= MathF.PI / 3)
                    Assert.True(Vector2.Dot(player.Heading, way) > 0.9999f, $"set off facing {player.Heading}");
            }
            var moved = player.Sphere.Position - from;
            return MathF.Abs(moved.X * way.Y - moved.Z * way.X);
        }

        var original = Aside(ModernRules.Default with { SetOffTurn = Cadence.PerTick(Body.TurnPerFrame) });
        Assert.True(original > 0.2f);
        Assert.True(Aside(ModernRules.Default with { SetOffTurn = MathF.PI / 3 }) < 0.01f);
        // The base game's, 20° a tick, the user's: 80° before he moves, a curve half the original's.
        Assert.InRange(Aside(ModernRules.Default), 0.01f, original);
    }

    [Fact]
    public void InModernMovementTheStartOfHisRunMovesAlaronByItsOwnRootEvenAveraged()
    {
        // Averaged over a stride, the start's push-off, twice the run's mean, left him 0.2 behind his feet as the run's loop began.
        var (own, ownRun) = StandingOnFlat(ModernRules.Default, Vector2.UnitY);
        var (averaged, averagedRun) = StandingOnFlat(ModernRules.Default with { Stride = 20 }, Vector2.UnitY);
        for (var i = 0; i < 21; i++)
        {
            own.Step(Flat, new MoveInput(Vector2.UnitY), Vector2.UnitY, ownRun);
            averaged.Step(Flat, new MoveInput(Vector2.UnitY), Vector2.UnitY, averagedRun);
            Assert.Equal(own.Sphere.Position, averaged.Sphere.Position);
        }
        Assert.InRange(own.Sphere.Position.Z, 1.2f, 1.4f);
    }

    [Fact]
    public void AlaronLeadsByHisEntitysStepsUnderModernMovement()
    {
        // The importer gives every entity the user's tuning, and the session hands the leader's to the player.
        var body = Data.Set.Get<EntityRecord>(GameState.Alaron).Body!;
        Assert.Equal((0.3f, 3f, 10, 20f), (body.StopReach, body.StopPace, body.Stride, body.SetOffTurn));
        Assert.All(Data.Set.All<EntityRecord>(RecordTypes.Entity).Select(e => e.Body).OfType<EntityBody>(),
            b => Assert.Equal((0.3f, 3f, 10, 20f), (b.StopReach, b.StopPace, b.Stride, b.SetOffTurn)));
        var (session, _) = StartIn(0, "I08", 50, 50);
        Hold(session, Vector2.Zero);
        Assert.Equal(ModernRules.Default.For(body), session.Trek.Player.Rules);
        Assert.Equal(MathF.PI / 9, session.Trek.Player.Rules.SetOffTurn, 1e-6f);
    }

    private static readonly CollisionWorld Flat = Worlds.Flat();

    /// <summary>Alaron under Modern movement, standing on flat ground, facing <paramref name="heading"/>, with his actor.</summary>
    private (PlayerMotion Player, ActorMotion Run) StandingOnFlat(ModernRules rules, Vector2 heading)
    {
        var player = new PlayerMotion(new Vector3(0, 0.6f, 0), heading) { Movement = MovementMode.Modern, Rules = rules };
        var run = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
        for (var i = 0; i < 20; i++)
            player.Step(Flat, new MoveInput(Vector2.Zero), heading, run);
        return (player, run);
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
