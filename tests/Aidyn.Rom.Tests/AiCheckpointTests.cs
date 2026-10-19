using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Combat;
using Aidyn.Core.Movement;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

using static Aidyn.Rom.Tests.CombatCheckpoints;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The enemy AI (M3 session 6) against the original's own results: the user's Combat checkpoints 08 to 11 laid over the battle the port
/// sets up from 01 (<see cref="CombatCheckpoints"/>), and the port's AI run where 1.0's own <c>EnemyAI.Step</c>, its path finder, its
/// morale and its reserves' flight were run on the checkpoints' RAM in session 5's MIPS interpreter (the session's research,
/// <c>m3-s6-research.md</c> and <c>m3-s6-vectors.json</c>). Checkpoints 09, 10 and 11 are each an enemy's turn under way (a newcomer's
/// walk, a rat's bite at Brenna, a rat's flight), wound back to the moment its turn began. The tests skip without the ROM and the
/// checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class AiCheckpointTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    private Built At(int checkpoint) => CombatCheckpoints.At(Set, checkpoint);

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void TheOverlayHoldsTheCheckpointsAiRecordsAndLeader(int checkpoint)
    {
        // At() checks every record's style, the leader's slot and everything else it doesn't copy.
        var built = At(checkpoint);
        var rats = built.Battle.Combatants.Where(c => c.Hostile).ToList();
        Assert.All(rats, c => Assert.Equal((0, AiKind.Melee, 5), (c.Ai!.Style, c.Ai.Kind, c.Ai.Morale)));
        Assert.All(built.Battle.Combatants.Where(c => !c.Hostile), c => Assert.Null(c.Ai));
        Assert.Equal(4, built.Battle.LeaderSlot);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // A turn wound back and played again

    /// <summary>
    /// A checkpoint's enemy put back as its turn began (as the research's <c>rewind.py</c>): its place and its body's at where its turn
    /// started, its body's walk forgotten, its AI's target and path cleared, and the battle in state 8 on its turn.
    /// </summary>
    private Built Rewound(int checkpoint, int slot)
    {
        var built = At(checkpoint);
        var (battle, c) = (built.Battle, built[slot]);
        battle.Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Cell = c.TurnStart;
        c.Body.Sphere.Position = c.Body.Sphere.Position with { X = battle.Origin.X + c.TurnStart.X, Z = battle.Origin.Z + c.TurnStart.Y };
        c.ReadFrom = c.Body.Sphere.Position;
        battle.Grid.Occupy(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Body.ClearPath();
        c.Ai!.ClearTarget();
        battle.Current = c;
        Poke(battle, nameof(Battle.State), CombatState.AI);
        Poke(battle, nameof(Battle.NextState), CombatState.AI);
        return built;
    }

    /// <summary>Where a body is running to, and what it has queued after, in the arena chunk's cells.</summary>
    private static (Vector2 Goal, Vector2[] Queued) Walk(Battle battle, Body body)
    {
        var origin = new Vector2(battle.Origin.X, battle.Origin.Z);
        Assert.True(body.Moving);
        return (body.Goal - origin, [.. body.Queued.Select(q => q.Goal - origin)]);
    }

    [Fact]
    public void Checkpoint09sNewcomerTargetsSholehAndRunsItsFirstLegAsTheOriginalDid()
    {
        var built = Rewound(9, 6);
        var (battle, rat) = (built.Battle, built[6]);
        var ai = rat.Ai!;
        var combat = built.Game.Random.Combat.Seed;

        battle.StepAI(rat);

        Assert.Equal((CombatState.Moving, CombatState.AI, 240), (battle.State, battle.NextState, battle.Stuck));
        Assert.Equal((3, AiKind.Melee, AiKind.Melee), (ai.Target, ai.Kind, ai.Wanted));
        Assert.Equal((2, 2), (ai.PathPlace, ai.PathLength));
        Assert.Equal([(15, 46), (58, 46)], battle.PathOf(ai));
        // The leg is cut where the line leaves its field of 6 about (12, 43): it stands at (18, 46) at once.
        Assert.Equal(new Vector2(18, 46), rat.Cell);
        Assert.Equal(new Vector2(12, 43), rat.TurnStart);
        Assert.Equal((2, new Vector2(1, 0)), (rat.Direction, rat.Facing));
        Assert.Equal(0x3A, (int)rat.Flags);
        Assert.Equal((AiFlags.Plays, 0), (ai.Flags, ai.FlightLeft));
        var (goal, queued) = Walk(battle, rat.Body);
        Assert.Equal(new Vector2(15, 46), goal);
        Assert.Equal([new Vector2(18, 46)], queued);
        Assert.Equal(1f, rat.Body.GoalRadius);
        // One draw on the sort's stream; none on the battle's.
        Assert.Equal(2868466484u, built.Game.Random.Sort.Seed);
        Assert.Equal(combat, built.Game.Random.Combat.Seed);
    }

    [Fact]
    public void Checkpoint10sRatHeadsForBrennaAndTakesOneStepAsItsEngaged()
    {
        var built = Rewound(10, 4);
        var (battle, rat) = (built.Battle, built[4]);
        var ai = rat.Ai!;
        Assert.Equal(1, rat.Radius);

        battle.StepAI(rat);

        Assert.Equal((CombatState.Moving, CombatState.AI, 240), (battle.State, battle.NextState, battle.Stuck));
        Assert.Equal((2, 1, 1), (ai.Target, ai.PathPlace, ai.PathLength));
        Assert.Equal([(65, 50)], battle.PathOf(ai));
        Assert.Equal(new Vector2(65, 51), rat.Cell);
        Assert.Equal((0, new Vector2(0, -1)), (rat.Direction, rat.Facing));
        Assert.Equal(0xB2, (int)rat.Flags);
        var (goal, queued) = Walk(battle, rat.Body);
        Assert.Equal(new Vector2(65, 51), goal);
        Assert.Empty(queued);
        Assert.Equal(2180890343u, built.Game.Random.Sort.Seed);
    }

    [Fact]
    public void Checkpoint10sRatRunsItsStepTurnsAndBitesBrennaWhoAvoidsIt()
    {
        var built = Rewound(10, 4);
        var (battle, rat, brenna) = (built.Battle, built[4], built[2]);
        var zone = Zone(built);
        for (var i = 0; i < 120 && battle.Fade.Fading; i++)
            battle.Fade.Step();
        battle.StepAI(rat);
        built.Game.Random.Combat.Seed = 1;

        // State 5 until the body has run its step, then 6 (its turn the way it walked), 6 again (to Brenna), then the bite.
        var states = new List<CombatState>();
        for (var i = 0; i < 600 && battle.State != CombatState.Action; i++)
        {
            battle.Tick(zone, false, _ => null);
            if (states.Count == 0 || states[^1] != battle.State)
                states.Add(battle.State);
        }
        output.WriteLine($"states: {string.Join(", ", states)}");
        Assert.Equal(CombatState.Action, battle.State);
        Assert.Equal([CombatState.Facing, CombatState.Action], states.Distinct().Where(s => s != CombatState.Moving));
        Assert.Equal((2, 2), (battle.Target, rat.LastTarget));
        Assert.Equal("Giant Rat attacks Brenna", battle.Lines.Shown);
        Assert.Equal("Brenna avoids the attack", battle.Lines.Queued);
        Assert.Equal(0, brenna.Damage);
        // The bite's chance is the least, 5, and the roll 24 misses: two draws, the roll and the skill's.
        var two = new Lcg(1);
        two.Next();
        two.Next();
        Assert.Equal(two.Seed, built.Game.Random.Combat.Seed);
    }

    [Fact]
    public void Checkpoint11sFleeingRatRunsForTheNearestFleePoint()
    {
        var built = Rewound(11, 6);
        var (battle, rat) = (built.Battle, built[6]);
        var ai = rat.Ai!;
        Assert.Equal((AiFlags.Plays | AiFlags.Fleeing, 4), (ai.Flags, ai.FlightLeft));
        var sort = built.Game.Random.Sort.Seed;

        battle.StepAI(rat);

        Assert.Equal((CombatState.Moving, CombatState.AI, 240), (battle.State, battle.NextState, battle.Stuck));
        Assert.Equal((-1, 2, 2), (ai.Target, ai.PathPlace, ai.PathLength));
        Assert.Equal([(14, 42), (7, 42)], battle.PathOf(ai));
        Assert.Equal(new Vector2(12, 42), rat.Cell);
        Assert.Equal(rat.TurnStart, battle.FleeOrigin);
        Assert.Equal((6, new Vector2(-1, 0)), (rat.Direction, rat.Facing));
        Assert.Equal(0x3A, (int)rat.Flags);
        Assert.Equal((AiFlags.Plays | AiFlags.Fleeing, 4), (ai.Flags, ai.FlightLeft));
        Assert.Equal("Giant Rat flees", battle.Lines.Shown);
        var (goal, queued) = Walk(battle, rat.Body);
        Assert.Equal(new Vector2(14, 42), goal);
        Assert.Equal([new Vector2(12, 42)], queued);
        Assert.Equal(sort, built.Game.Random.Sort.Seed);
    }

    [Fact]
    public void AFleeingRatThatGetsNoWhereLeavesTheField()
    {
        var built = Rewound(11, 6);
        var (battle, rat) = (built.Battle, built[6]);
        battle.StepAI(rat);
        // Its body stays where its turn began: the leg's end finds it not a unit from there, and it leaves.
        rat.Body.ClearPath();
        var active = battle.ActiveCounts;
        for (var i = 0; i < 10 && battle.State != CombatState.Rest; i++)
            battle.Tick(Zone(built), false, _ => null);
        Assert.Equal(CombatState.Rest, battle.State);
        Assert.True(rat.Gone);
        Assert.True(rat.Freed);
        Assert.Equal(Battle.FadeTicks, rat.Fading, 2f);
        Assert.Equal((active.All - 1, active.Party, active.Enemies - 1), battle.ActiveCounts);
        Assert.True(battle.Gui.Portraits.Single(p => p.Slot == 6).Fled);
    }

    [Theory]
    [InlineData(4, 2, new[] { 65, 55, 65, 50 }, 65, 50, 0)]
    [InlineData(5, 0, new[] { 60, 56 }, 60, 56, 7)]
    public void Checkpoint08sRatsChooseAndWalkAsTheOriginalsDid(int slot, int target, int[] path, int x, int z, int direction)
    {
        var built = At(8);
        var (battle, rat) = (built.Battle, built[slot]);
        Assert.Equal(0x3C6EF35Fu, built.Game.Random.Sort.Seed);
        battle.Current = rat;
        Poke(battle, nameof(Battle.State), CombatState.AI);

        battle.StepAI(rat);

        Assert.Equal((CombatState.Moving, target), (battle.State, rat.Ai!.Target));
        Assert.Equal(path.Chunk(2).Select(p => (p[0], p[1])), battle.PathOf(rat.Ai));
        Assert.Equal((new Vector2(x, z), direction), (rat.Cell, rat.Direction));
        Assert.Equal(0x47502932u, built.Game.Random.Sort.Seed);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Morale and fleeing

    /// <summary>
    /// For each checkpoint and enemy slot: its morale as the leader's (<c>Morale(c, 0)</c>), the leader's morale then, its own
    /// (<c>Morale(c, 1)</c>), whether it's checked, and <c>CheckFlee(c, 1)</c> for each seed: the AI's flags and flight after, the stream
    /// after and the leader's morale.
    /// </summary>
    private static readonly (int Checkpoint, int Slot, int AsLeader, int LeaderAfter, int Own, bool Checked, (uint Seed, int Flags, int Flight, uint After, int Leader)[] Flees)[] Morales =
    [
        (8, 4, 4, 4, 4, true, [(0x00000001u, 9, 4, 0x5E8885DBu, 4), (0x00000002u, 9, 4, 0x75C0E284u, 4), (0x00000003u, 9, 4, 0x8CF93F2Du, 4), (0x00000004u, 9, 4, 0xA4319BD6u, 4), (0x00000005u, 9, 4, 0xBB69F87Fu, 4), (0x00000006u, 9, 4, 0xD2A25528u, 4), (0x00000007u, 9, 4, 0xE9DAB1D1u, 4), (0x00000008u, 8, 0, 0x01130E7Au, 4), (0x00000009u, 8, 0, 0x184B6B23u, 4), (0x0000000Au, 8, 0, 0x2F83C7CCu, 4), (0x00012345u, 9, 4, 0x9AD53DBFu, 4), (0xDEADBEEFu, 9, 4, 0xF5E918F9u, 4)]),
        (8, 5, 4, 4, 4, true, [(0x00000001u, 9, 4, 0x5E8885DBu, 4), (0x00000002u, 9, 4, 0x75C0E284u, 4), (0x00000003u, 9, 4, 0x8CF93F2Du, 4), (0x00000004u, 9, 4, 0xA4319BD6u, 4), (0x00000005u, 9, 4, 0xBB69F87Fu, 4), (0x00000006u, 9, 4, 0xD2A25528u, 4), (0x00000007u, 9, 4, 0xE9DAB1D1u, 4), (0x00000008u, 8, 0, 0x01130E7Au, 4), (0x00000009u, 8, 0, 0x184B6B23u, 4), (0x0000000Au, 8, 0, 0x2F83C7CCu, 4), (0x00012345u, 9, 4, 0x9AD53DBFu, 4), (0xDEADBEEFu, 9, 4, 0xF5E918F9u, 4)]),
        (8, 6, 3, 3, 3, true, [(0x00000001u, 9, 4, 0x5E8885DBu, 4), (0x00000002u, 9, 4, 0x75C0E284u, 4), (0x00000003u, 9, 4, 0x8CF93F2Du, 4), (0x00000004u, 9, 4, 0xA4319BD6u, 4), (0x00000005u, 9, 4, 0xBB69F87Fu, 4), (0x00000006u, 9, 4, 0xD2A25528u, 4), (0x00000007u, 9, 4, 0xE9DAB1D1u, 4), (0x00000008u, 8, 0, 0x01130E7Au, 4), (0x00000009u, 8, 0, 0x184B6B23u, 4), (0x0000000Au, 9, 4, 0x2F83C7CCu, 4), (0x00012345u, 9, 4, 0x9AD53DBFu, 4), (0xDEADBEEFu, 9, 4, 0xF5E918F9u, 4)]),
        (8, 7, 3, 3, 3, true, [(0x00000001u, 9, 4, 0x5E8885DBu, 4), (0x00000002u, 9, 4, 0x75C0E284u, 4), (0x00000003u, 9, 4, 0x8CF93F2Du, 4), (0x00000004u, 9, 4, 0xA4319BD6u, 4), (0x00000005u, 9, 4, 0xBB69F87Fu, 4), (0x00000006u, 9, 4, 0xD2A25528u, 4), (0x00000007u, 9, 4, 0xE9DAB1D1u, 4), (0x00000008u, 8, 0, 0x01130E7Au, 4), (0x00000009u, 8, 0, 0x184B6B23u, 4), (0x0000000Au, 9, 4, 0x2F83C7CCu, 4), (0x00012345u, 9, 4, 0x9AD53DBFu, 4), (0xDEADBEEFu, 9, 4, 0xF5E918F9u, 4)]),
        (9, 4, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (9, 5, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (9, 6, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (9, 7, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (10, 4, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (10, 5, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (10, 6, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (10, 7, 4, 4, 4, false, [(0x00000001u, 8, 0, 0x00000001u, 4), (0x00000002u, 8, 0, 0x00000002u, 4), (0x00000003u, 8, 0, 0x00000003u, 4), (0x00000004u, 8, 0, 0x00000004u, 4), (0x00000005u, 8, 0, 0x00000005u, 4), (0x00000006u, 8, 0, 0x00000006u, 4), (0x00000007u, 8, 0, 0x00000007u, 4), (0x00000008u, 8, 0, 0x00000008u, 4), (0x00000009u, 8, 0, 0x00000009u, 4), (0x0000000Au, 8, 0, 0x0000000Au, 4), (0x00012345u, 8, 0, 0x00012345u, 4), (0xDEADBEEFu, 8, 0, 0xDEADBEEFu, 4)]),
        (11, 4, 2, 2, 2, true, [(0x00000001u, 8, 0, 0x5E8885DBu, 2), (0x00000002u, 8, 0, 0x75C0E284u, 2), (0x00000003u, 8, 0, 0x8CF93F2Du, 2), (0x00000004u, 8, 0, 0xA4319BD6u, 2), (0x00000005u, 8, 0, 0xBB69F87Fu, 2), (0x00000006u, 8, 0, 0xD2A25528u, 2), (0x00000007u, 8, 0, 0xE9DAB1D1u, 2), (0x00000008u, 8, 0, 0x01130E7Au, 2), (0x00000009u, 8, 0, 0x184B6B23u, 2), (0x0000000Au, 8, 0, 0x2F83C7CCu, 2), (0x00012345u, 8, 0, 0x9AD53DBFu, 2), (0xDEADBEEFu, 8, 0, 0xF5E918F9u, 2)]),
        (11, 5, 2, 2, 2, true, [(0x00000001u, 9, 4, 0x5E8885DBu, 2), (0x00000002u, 9, 4, 0x75C0E284u, 2), (0x00000003u, 9, 4, 0x8CF93F2Du, 2), (0x00000004u, 9, 4, 0xA4319BD6u, 2), (0x00000005u, 9, 4, 0xBB69F87Fu, 2), (0x00000006u, 9, 4, 0xD2A25528u, 2), (0x00000007u, 9, 4, 0xE9DAB1D1u, 2), (0x00000008u, 9, 4, 0x01130E7Au, 2), (0x00000009u, 9, 4, 0x184B6B23u, 2), (0x0000000Au, 9, 4, 0x2F83C7CCu, 2), (0x00012345u, 9, 4, 0x9AD53DBFu, 2), (0xDEADBEEFu, 9, 4, 0xF5E918F9u, 2)]),
        (11, 6, 3, 3, 3, false, [(0x00000001u, 9, 4, 0x00000001u, 2), (0x00000002u, 9, 4, 0x00000002u, 2), (0x00000003u, 9, 4, 0x00000003u, 2), (0x00000004u, 9, 4, 0x00000004u, 2), (0x00000005u, 9, 4, 0x00000005u, 2), (0x00000006u, 9, 4, 0x00000006u, 2), (0x00000007u, 9, 4, 0x00000007u, 2), (0x00000008u, 9, 4, 0x00000008u, 2), (0x00000009u, 9, 4, 0x00000009u, 2), (0x0000000Au, 9, 4, 0x0000000Au, 2), (0x00012345u, 9, 4, 0x00012345u, 2), (0xDEADBEEFu, 9, 4, 0xDEADBEEFu, 2)]),
        (11, 7, 2, 2, 2, true, [(0x00000001u, 9, 4, 0x5E8885DBu, 2), (0x00000002u, 9, 4, 0x75C0E284u, 2), (0x00000003u, 9, 4, 0x8CF93F2Du, 2), (0x00000004u, 9, 4, 0xA4319BD6u, 2), (0x00000005u, 9, 4, 0xBB69F87Fu, 2), (0x00000006u, 9, 4, 0xD2A25528u, 2), (0x00000007u, 9, 4, 0xE9DAB1D1u, 2), (0x00000008u, 9, 4, 0x01130E7Au, 2), (0x00000009u, 9, 4, 0x184B6B23u, 2), (0x0000000Au, 9, 4, 0x2F83C7CCu, 2), (0x00012345u, 9, 4, 0x9AD53DBFu, 2), (0xDEADBEEFu, 9, 4, 0xF5E918F9u, 2)]),
    ];

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    public void MoraleAndTheCheckToFleeAreTheOriginals(int checkpoint)
    {
        var built = At(checkpoint);
        var battle = built.Battle;
        foreach (var (_, slot, asLeader, leaderAfter, own, checks, flees) in Morales.Where(m => m.Checkpoint == checkpoint))
        {
            var c = built[slot];
            var ai = c.Ai!;
            var (flags, flight, leader) = (ai.Flags, ai.FlightLeft, battle.LeaderMorale);
            Assert.Equal(asLeader, battle.Morale(c, own: false));
            Assert.Equal(leaderAfter, battle.LeaderMorale);
            Assert.Equal(own, battle.Morale(c, own: true));
            (ai.Flags, battle.LeaderMorale) = (flags, leader);
            Assert.Equal(checks, battle.MayCheckMorale(c));
            foreach (var (seed, after, steps, stream, leaderMorale) in flees)
            {
                (ai.Flags, ai.FlightLeft, battle.LeaderMorale) = (flags, flight, leader);
                built.Game.Random.Combat.Seed = seed;
                battle.CheckFlee(c, asked: true);
                Assert.Equal((after, steps, stream, leaderMorale), ((int)ai.Flags, ai.FlightLeft, built.Game.Random.Combat.Seed, battle.LeaderMorale));
            }
            (ai.Flags, ai.FlightLeft, battle.LeaderMorale) = (flags, flight, leader);
        }
    }

    /// <summary>For a checkpoint, an entity and a seed: whether a reserve flees as it arrives, and the stream after.</summary>
    private static readonly (int Checkpoint, int Entity, uint Seed, bool Flees, uint After)[] Reserves =
    [
        (8, 0x026E, 0x00000001u, false, 0x00000001u),
        (8, 0x026E, 0x00000002u, false, 0x00000002u),
        (8, 0x026E, 0x00000003u, false, 0x00000003u),
        (8, 0x026E, 0x00000004u, false, 0x00000004u),
        (8, 0x026E, 0x00000005u, false, 0x00000005u),
        (8, 0x026E, 0x00000006u, false, 0x00000006u),
        (8, 0x0208, 0x00000001u, false, 0x00000001u),
        (8, 0x0208, 0x00000002u, false, 0x00000002u),
        (8, 0x0208, 0x00000003u, false, 0x00000003u),
        (8, 0x0208, 0x00000004u, false, 0x00000004u),
        (8, 0x0208, 0x00000005u, false, 0x00000005u),
        (8, 0x0208, 0x00000006u, false, 0x00000006u),
        (8, 0x020F, 0x00000001u, false, 0x00000001u),
        (8, 0x020F, 0x00000002u, false, 0x00000002u),
        (8, 0x020F, 0x00000003u, false, 0x00000003u),
        (8, 0x020F, 0x00000004u, false, 0x00000004u),
        (8, 0x020F, 0x00000005u, false, 0x00000005u),
        (8, 0x020F, 0x00000006u, false, 0x00000006u),
        (8, 0x0215, 0x00000001u, false, 0x00000001u),
        (8, 0x0215, 0x00000002u, false, 0x00000002u),
        (8, 0x0215, 0x00000003u, false, 0x00000003u),
        (8, 0x0215, 0x00000004u, false, 0x00000004u),
        (8, 0x0215, 0x00000005u, false, 0x00000005u),
        (8, 0x0215, 0x00000006u, false, 0x00000006u),
        (9, 0x026E, 0x00000001u, true, 0x5E8885DBu),
        (9, 0x026E, 0x00000002u, true, 0x75C0E284u),
        (9, 0x026E, 0x00000003u, true, 0x8CF93F2Du),
        (9, 0x026E, 0x00000004u, true, 0xA4319BD6u),
        (9, 0x026E, 0x00000005u, true, 0xBB69F87Fu),
        (9, 0x026E, 0x00000006u, true, 0xD2A25528u),
        (9, 0x0208, 0x00000001u, false, 0x5E8885DBu),
        (9, 0x0208, 0x00000002u, false, 0x75C0E284u),
        (9, 0x0208, 0x00000003u, false, 0x8CF93F2Du),
        (9, 0x0208, 0x00000004u, false, 0xA4319BD6u),
        (9, 0x0208, 0x00000005u, true, 0xBB69F87Fu),
        (9, 0x0208, 0x00000006u, true, 0xD2A25528u),
        (9, 0x020F, 0x00000001u, true, 0x5E8885DBu),
        (9, 0x020F, 0x00000002u, true, 0x75C0E284u),
        (9, 0x020F, 0x00000003u, true, 0x8CF93F2Du),
        (9, 0x020F, 0x00000004u, true, 0xA4319BD6u),
        (9, 0x020F, 0x00000005u, true, 0xBB69F87Fu),
        (9, 0x020F, 0x00000006u, true, 0xD2A25528u),
        (9, 0x0215, 0x00000001u, false, 0x5E8885DBu),
        (9, 0x0215, 0x00000002u, false, 0x75C0E284u),
        (9, 0x0215, 0x00000003u, false, 0x8CF93F2Du),
        (9, 0x0215, 0x00000004u, false, 0xA4319BD6u),
        (9, 0x0215, 0x00000005u, false, 0xBB69F87Fu),
        (9, 0x0215, 0x00000006u, false, 0xD2A25528u),
        (11, 0x026E, 0x00000001u, false, 0x00000001u),
        (11, 0x026E, 0x00000002u, false, 0x00000002u),
        (11, 0x026E, 0x00000003u, false, 0x00000003u),
        (11, 0x026E, 0x00000004u, false, 0x00000004u),
        (11, 0x026E, 0x00000005u, false, 0x00000005u),
        (11, 0x026E, 0x00000006u, false, 0x00000006u),
        (11, 0x0208, 0x00000001u, false, 0x00000001u),
        (11, 0x0208, 0x00000002u, false, 0x00000002u),
        (11, 0x0208, 0x00000003u, false, 0x00000003u),
        (11, 0x0208, 0x00000004u, false, 0x00000004u),
        (11, 0x0208, 0x00000005u, false, 0x00000005u),
        (11, 0x0208, 0x00000006u, false, 0x00000006u),
        (11, 0x020F, 0x00000001u, false, 0x00000001u),
        (11, 0x020F, 0x00000002u, false, 0x00000002u),
        (11, 0x020F, 0x00000003u, false, 0x00000003u),
        (11, 0x020F, 0x00000004u, false, 0x00000004u),
        (11, 0x020F, 0x00000005u, false, 0x00000005u),
        (11, 0x020F, 0x00000006u, false, 0x00000006u),
        (11, 0x0215, 0x00000001u, false, 0x00000001u),
        (11, 0x0215, 0x00000002u, false, 0x00000002u),
        (11, 0x0215, 0x00000003u, false, 0x00000003u),
        (11, 0x0215, 0x00000004u, false, 0x00000004u),
        (11, 0x0215, 0x00000005u, false, 0x00000005u),
        (11, 0x0215, 0x00000006u, false, 0x00000006u),
    ];

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(11)]
    public void AReserveFleesAsItArrivesAsTheOriginalsDid(int checkpoint)
    {
        var built = At(checkpoint);
        foreach (var (_, entity, seed, flees, after) in Reserves.Where(r => r.Checkpoint == checkpoint))
        {
            built.Game.Random.Combat.Seed = seed;
            var record = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, entity));
            Assert.Equal((flees, after), (built.Battle.ReserveFleesOnArrival(record), built.Game.Random.Combat.Seed));
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Paths over checkpoint 08's grid

    /// <summary>A start, a goal and a footprint, and the original's path: its length (the diagonal, 129, for none) and corners.</summary>
    private static readonly (int Sx, int Sz, int Gx, int Gz, int Footprint, int Length, bool Failed, (int X, int Z)[] Path)[] Paths =
    [
        (56, 14, 62, 3, 1, 129, true, []),
        (12, 45, 30, 2, 1, 129, true, []),
        (2, 44, 82, 79, 2, 129, true, []),
        (78, 59, 71, 69, 1, 129, true, []),
        (1, 64, 62, 31, 1, 129, true, []),
        (69, 59, 8, 76, 1, 129, true, []),
        (65, 74, 70, 64, 2, 129, true, []),
        (61, 45, 34, 31, 1, 2, false, [(47, 31), (34, 31)]),
        (58, 67, 2, 65, 1, 129, true, []),
        (0, 42, 3, 51, 2, 129, true, []),
        (21, 6, 9, 75, 2, 129, true, []),
        (20, 9, 71, 27, 1, 129, true, []),
        (3, 44, 0, 53, 2, 129, true, []),
        (58, 5, 53, 35, 1, 129, true, []),
        (22, 34, 19, 38, 1, 2, false, [(19, 37), (19, 38)]),
        (59, 5, 71, 4, 2, 129, true, []),
        (43, 74, 59, 60, 1, 129, true, []),
        (60, 76, 21, 56, 2, 129, true, []),
        (37, 35, 38, 36, 1, 1, false, [(38, 36)]),
        (67, 13, 62, 48, 2, 129, true, []),
        (82, 24, 44, 13, 1, 129, true, []),
        (74, 34, 72, 36, 1, 1, false, [(72, 36)]),
        (10, 50, 27, 6, 2, 129, true, []),
        (4, 28, 64, 11, 2, 129, true, []),
        (39, 58, 35, 48, 2, 2, false, [(35, 54), (35, 48)]),
        (25, 12, 50, 24, 2, 129, true, []),
        (64, 30, 46, 26, 2, 2, false, [(60, 26), (46, 26)]),
        (22, 74, 11, 73, 1, 129, true, []),
        (63, 70, 60, 42, 1, 129, true, []),
        (16, 6, 79, 21, 1, 129, true, []),
        (69, 8, 57, 17, 1, 129, true, []),
        (81, 11, 18, 27, 1, 129, true, []),
        (55, 50, 41, 26, 2, 2, false, [(41, 36), (41, 26)]),
        (35, 68, 36, 66, 1, 2, false, [(36, 67), (36, 66)]),
        (68, 11, 33, 35, 2, 129, true, []),
        (51, 72, 35, 67, 1, 129, true, []),
        (48, 35, 58, 41, 2, 2, false, [(54, 41), (58, 41)]),
        (6, 44, 56, 29, 2, 2, false, [(21, 29), (56, 29)]),
        (21, 48, 39, 0, 1, 129, true, []),
        (61, 50, 70, 53, 1, 129, true, []),
        (51, 80, 61, 20, 2, 129, true, []),
        (75, 47, 83, 76, 2, 129, true, []),
        (26, 21, 28, 30, 2, 129, true, []),
        (84, 3, 59, 84, 1, 129, true, []),
        (3, 46, 60, 8, 1, 129, true, []),
        (75, 42, 84, 39, 2, 129, true, []),
        (74, 42, 72, 37, 1, 129, true, []),
        (65, 95, 35, 9, 1, 129, true, []),
        (43, 65, 45, 61, 1, 2, false, [(45, 63), (45, 61)]),
        (67, 75, 74, 70, 1, 129, true, []),
        (31, 32, 47, 17, 1, 2, false, [(46, 17), (47, 17)]),
        (50, 4, 59, 0, 2, 129, true, []),
        (66, 45, 55, 65, 1, 129, true, []),
        (39, 33, 62, 74, 1, 129, true, []),
        (44, 64, 32, 73, 2, 129, true, []),
        (72, 29, 20, 61, 2, 129, true, []),
        (17, 68, 79, 79, 1, 129, true, []),
        (18, 68, 11, 72, 2, 129, true, []),
        (41, 16, 71, 72, 2, 129, true, []),
        (5, 47, 8, 47, 2, 129, true, []),
    ];

    [Fact]
    public void ThePathFinderFindsAndFailsAsTheOriginalsDoes()
    {
        var built = At(8);
        var grid = built.Battle.Grid;
        Assert.Equal(129, grid.Diagonal);
        var cells = Snapshot(grid);
        var bodies = built.Battle.Combatants.Where(c => !c.Sheet.IsDead && !c.Gone)
            .Select(c => (ArenaGrid.CellOf(c.Cell.X) & 0xFF, ArenaGrid.CellOf(c.Cell.Y) & 0xFF, c.Footprint)).ToList();
        var buffer = new (int X, int Z)[144];
        foreach (var (sx, sz, gx, gz, footprint, length, failed, path) in Paths)
        {
            Restore(grid, cells);
            var found = grid.FindPath(sx, sz, gx, gz, footprint, buffer, bodies);
            Assert.Equal(length, found);
            Assert.Equal(failed, found == grid.Diagonal);
            if (!failed)
                Assert.Equal(path, buffer[..found]);
        }
        output.WriteLine($"{Paths.Count(p => !p.Failed)} of {Paths.Length} found");
    }

    /// <summary>A line's start and end and the footprint; whether it's clear and the cell it stops at.</summary>
    private static readonly (int X0, int Z0, int Tx, int Tz, int Footprint, bool Clear, int X, int Z)[] Walks =
    [
        (5, 77, 0, 79, 1, false, 4, 77),
        (79, 25, 82, 33, 2, false, 79, 26),
        (75, 63, 68, 55, 2, false, 74, 62),
        (10, 37, 15, 39, 1, true, 15, 39),
        (62, 86, 61, 78, 2, false, 62, 85),
        (75, 47, 80, 44, 1, false, 76, 46),
        (81, 2, 79, 0, 1, false, 80, 1),
        (64, 16, 64, 9, 2, false, 64, 15),
        (46, 76, 49, 77, 2, false, 47, 76),
        (52, 47, 51, 48, 1, true, 51, 48),
        (40, 74, 40, 69, 1, false, 40, 73),
        (27, 68, 30, 65, 2, true, 30, 65),
        (37, 46, 37, 43, 2, false, 37, 43),
        (21, 0, 26, 0, 1, false, 22, 0),
        (56, 34, 53, 37, 2, true, 53, 37),
        (67, 62, 74, 69, 2, false, 68, 63),
        (47, 79, 42, 87, 2, false, 46, 80),
        (74, 16, 69, 23, 2, false, 73, 17),
        (12, 69, 8, 69, 1, false, 11, 69),
        (9, 42, 13, 43, 2, true, 13, 43),
        (51, 70, 49, 77, 2, false, 51, 71),
        (40, 56, 48, 51, 2, true, 48, 51),
        (10, 9, 8, 6, 2, false, 9, 8),
        (22, 48, 22, 46, 2, false, 22, 46),
        (3, 20, 5, 15, 1, false, 3, 19),
        (7, 86, 7, 91, 2, false, 7, 87),
        (43, 39, 48, 36, 2, true, 48, 36),
        (50, 1, 51, 2, 2, false, 51, 2),
        (78, 31, 74, 29, 2, false, 77, 30),
        (77, 56, 82, 64, 2, false, 78, 57),
        (66, 34, 71, 38, 2, true, 71, 38),
        (73, 63, 75, 62, 2, false, 74, 62),
        (20, 20, 25, 17, 1, false, 21, 19),
        (29, 73, 32, 71, 2, false, 30, 72),
        (56, 3, 60, 0, 1, false, 57, 2),
        (9, 31, 13, 38, 2, false, 10, 32),
        (30, 88, 31, 96, 2, false, 30, 89),
        (66, 66, 72, 60, 2, false, 67, 65),
        (3, 64, 0, 60, 1, false, 2, 63),
        (48, 54, 45, 62, 1, true, 45, 62),
        (38, 12, 45, 14, 1, false, 39, 12),
        (34, 55, 41, 55, 2, false, 41, 55),
        (21, 8, 27, 7, 2, false, 22, 8),
        (31, 96, 25, 96, 1, false, 30, 96),
        (80, 71, 80, 67, 2, false, 80, 70),
        (27, 25, 35, 21, 1, true, 31, 23),
        (43, 92, 48, 84, 2, false, 44, 91),
        (24, 82, 23, 74, 1, false, 24, 81),
        (46, 56, 45, 48, 2, true, 45, 50),
        (54, 63, 59, 65, 1, false, 55, 63),
        (75, 73, 82, 74, 2, false, 76, 73),
        (75, 2, 67, 3, 1, false, 74, 2),
        (57, 73, 56, 77, 2, false, 57, 74),
        (26, 5, 31, 9, 1, false, 27, 6),
        (52, 22, 49, 16, 2, true, 49, 17),
        (21, 95, 20, 89, 2, false, 21, 94),
        (4, 16, 4, 24, 1, false, 4, 17),
        (44, 65, 37, 65, 1, false, 37, 65),
        (5, 52, 12, 44, 2, false, 6, 51),
        (23, 29, 30, 22, 2, true, 30, 22),
        (31, 57, 37, 54, 2, true, 37, 54),
        (20, 53, 21, 61, 1, true, 20, 56),
        (37, 83, 39, 81, 1, false, 38, 82),
        (4, 83, 5, 90, 1, false, 4, 84),
        (54, 20, 59, 22, 2, false, 55, 20),
        (12, 10, 17, 12, 1, false, 13, 10),
        (84, 52, 84, 60, 1, false, 84, 53),
        (51, 13, 59, 15, 1, false, 52, 13),
        (83, 66, 78, 70, 1, false, 82, 67),
        (40, 89, 48, 85, 1, false, 41, 88),
        (82, 82, 84, 89, 2, false, 82, 83),
        (9, 8, 15, 0, 2, false, 10, 7),
        (4, 75, 2, 70, 2, false, 4, 74),
        (70, 22, 63, 22, 2, false, 69, 22),
        (39, 74, 32, 76, 2, false, 38, 74),
        (27, 11, 35, 10, 1, false, 28, 11),
        (38, 2, 32, 0, 1, false, 37, 2),
        (38, 71, 41, 76, 1, false, 39, 72),
        (50, 58, 44, 56, 1, true, 44, 56),
        (44, 82, 46, 85, 2, false, 45, 83),
    ];

    [Fact]
    public void AStraightWalkStopsWhereTheOriginalsDoes()
    {
        var built = At(8);
        foreach (var (x0, z0, tx, tz, footprint, clear, x, z) in Walks)
            Assert.Equal((clear, x, z), built.Battle.WalkIsClear(x0, z0, tx, tz, footprint));
    }

    private static byte[] Snapshot(ArenaGrid grid)
    {
        var cells = new byte[ArenaGrid.Side * ArenaGrid.Side];
        for (var x = 0; x < ArenaGrid.Side; x++)
        {
            for (var z = 0; z < ArenaGrid.Side; z++)
                cells[x * ArenaGrid.Side + z] = grid[x, z];
        }
        return cells;
    }

    private static void Restore(ArenaGrid grid, byte[] cells)
    {
        for (var x = 0; x < ArenaGrid.Side; x++)
        {
            for (var z = 0; z < ArenaGrid.Side; z++)
                grid.Set(x, z, cells[x * ArenaGrid.Side + z]);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Styles and weapons

    [Fact]
    public void EveryFightersStyleAndWeaponIsTheOriginals()
    {
        var game = GameState.NewGame(Set);
        var rules = Set.Singleton<CombatRecord>(RecordTypes.Combat);
        var counts = new SortedDictionary<int, int>();
        var styles = new Dictionary<RecordId, (int Style, RecordId? Weapon, RecordId? Spare)>();
        foreach (var entity in Set.All<EntityRecord>(RecordTypes.Entity))
        {
            if (rules.Bystanders.Contains(entity.Id) || game.Recruit(entity.Id) is not { } sheet)
                continue;
            var spare = Battle.ArmFromList(sheet, game.Items);
            var c = new Combatant(4, sheet, entity.Body ?? new EntityBody(entity.Id, 1, 1, 1), new Body(Vector3.Zero, 1), CombatantFlags.Hostile);
            var ai = EnemyAI.Init(c, spare, w => w.Range == 0 || w.Class is WeaponClass.Breath or WeaponClass.Spit || rules.CloseRange!.Contains(w.Id),
                rules.Bosses!.Contains(entity.Id));
            counts[ai.Style] = counts.GetValueOrDefault(ai.Style) + 1;
            styles[entity.Id] = (ai.Style, sheet.Equipment.Weapon?.Id, spare?.Id);
        }
        output.WriteLine(string.Join(", ", counts.Select(c => $"style {c.Key}: {c.Value}")));
        Assert.Equal(new SortedDictionary<int, int> { [0] = 32, [1] = 7, [2] = 7, [3] = 12, [4] = 3, [5] = 6, [6] = 28, [7] = 2, [8] = 4, [9] = 7, [10] = 6 }, counts);
        Assert.Equal((0, W(0x0702), null), styles[E(0x026E)]);
        Assert.Equal((0, W(0x072B), null), styles[E(0x0215)]);
        Assert.Equal((2, W(0x071A), W(0x0725)), styles[E(0x022E)]);
        Assert.Equal((3, W(0x0718), W(0x072B)), styles[E(0x0208)]);

        static RecordId E(int id) => RecordId.Hex(RecordTypes.Entity, id);
        static RecordId? W(int id) => RecordId.Hex(RecordTypes.Weapon, id);
    }
}
