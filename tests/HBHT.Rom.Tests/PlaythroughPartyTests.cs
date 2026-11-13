using HBHT.Core;
using HBHT.Core.Playthrough;
using HBHT.Plugins;

namespace HBHT.Rom.Tests;

/// <summary>
/// Party jobs (docs/systems/playthrough.md, "Party jobs") on NA 1.0's data, places all taken as reachable: the targets each member's
/// joining leads to, and the party goal planned with the party's cap, through a conversation that asks who leaves.
/// </summary>
public sealed class PlaythroughPartyTests(Na10Import fixture, ITestOutputHelper output)
{
    private static readonly Lock Building = new();
    private static (PluginSet Set, QuestModel Model)? _built;

    private static readonly RecordId Abrecan = Ids.Entity(0x023E);
    private static readonly RecordId Becan = Ids.Entity(0x0242);
    private static readonly RecordId Brenna = Ids.Entity(0x0243);
    private static readonly RecordId Godric = Ids.Entity(0x0248);
    private static readonly RecordId Rheda = Ids.Entity(0x024C);
    private static readonly RecordId Sholeh = Ids.Entity(0x024D);

    private PluginSet Plugins => fixture.Data.Set;

    /// <summary>The model, built once for the class (some seconds).</summary>
    private QuestModel Model
    {
        get
        {
            lock (Building)
            {
                if (_built is not { } built || built.Set != Plugins)
                    _built = built = (Plugins, QuestModel.Build(Plugins));
                return built.Model;
            }
        }
    }

    private PlanState NewGame() => PlanState.From(Model, GameState.NewGame(Plugins));

    [Fact]
    public void FromANewGameEachMembersOwnTargetsAreLostIfTheyNeverJoin()
    {
        var start = NewGame();
        var targets = CoverGoal.StaticTargets(Model, start).Select(Model.Flags.Handle).ToList();
        var needs = PartyNeeds.Needs(Model, start, targets);
        var present = PartyNeeds.Present(Model, start, targets);
        foreach (var (member, lost) in needs.OrderBy(n => n.Key.ToString(), StringComparer.Ordinal))
            output.WriteLine($"{member.Key}: {lost.Count} lost if they never join, {present.GetValueOrDefault(member)?.Count ?? 0} need them in the party then");
        Assert.All(needs.Keys, m => Assert.Contains(m, Model.Members.Keys));

        bool Needs(RecordId member, int flag) => needs.TryGetValue(member, out var lost) && lost.Contains(Model.Flags.Handle(Ids.Flag(flag)));
        // Becan's scene at Oriana's (0x039F) and what follows it; Godric's at Erromon; Brenna's own.
        Assert.True(Needs(Becan, 528) && Needs(Becan, 553) && Needs(Becan, 5156));
        Assert.True(Needs(Godric, 73) && Needs(Godric, 5431));
        Assert.True(Needs(Brenna, 600) && Needs(Brenna, 1515));
        Assert.False(Needs(Becan, 73));
        // A setter that needs Becan in the party as it's done.
        Assert.Contains(Model.Flags.Handle(Ids.Flag(5410)), present[Becan]);
        var all = PartyNeeds.ByTarget(needs);
        Assert.InRange(all.Count, 100, 300);
        // Dougal's left flag needs him to leave, which needs him in the party first: lost with him gone for good and with him kept for
        // good alike, so it isn't counted as needing him in.
        var dougal = Ids.Entity(0x0247);
        var left = Model.Flags.Handle(Model.Members[dougal].Left!.Value);
        Assert.Contains(left, PartyNeeds.Away(Model, start, targets)[dougal]);
        Assert.DoesNotContain(left, needs.GetValueOrDefault(dougal) ?? []);
    }

    [Fact]
    public void APartyGoalWithThePartyFullIsPlannedThroughAConversationThatAsksWhoLeaves()
    {
        var planner = new QuestPlanner(Model);
        var start = NewGame();
        var three = planner.Plan(start, new PartyGoal([Abrecan, Brenna, Rheda]));
        Assert.True(three.Found, three.Why);
        var full = Play(start, three.Steps);
        Assert.Equal(4, full.Party.Count);
        output.WriteLine($"the party filled in {three.Steps.Count} steps");

        // A join whose script asks for a slot can't be had from a full party.
        Assert.False(full.Holds(new PartySizeIn(1, 3)));
        var sholeh = Model.Actions.Where(a => a.Effects.Contains(new Join(Sholeh))).ToList();
        Assert.NotEmpty(sholeh);
        Assert.All(sholeh, a => Assert.False(full.CanDo(a)));

        // Godric from the full party, Brenna kept: some step asks someone else to leave, and the party never holds more than four.
        var keep = PartyJobs.Leaving(Model, [Brenna]);
        Assert.All(keep, a => Assert.Contains(new Leave(Brenna), Model.Actions[a].Effects));
        var plan = planner.Plan(full, new PartyGoal([Godric]), banned: keep);
        Assert.True(plan.Found, plan.Why);
        output.WriteLine(string.Join(Environment.NewLine, plan.Steps.Select(s => s.Describe())));
        var leaving = plan.Steps.SelectMany(s => s.Effects.OfType<Leave>()).ToList();
        Assert.NotEmpty(leaving);
        Assert.DoesNotContain(new Leave(Brenna), leaving);
        var end = Play(full, plan.Steps);
        Assert.Contains(Godric, end.Party);
        Assert.Contains(Brenna, end.Party);
    }

    [Fact]
    public void TargetsThatNeedSomeoneKeptOutOfThePartyAreFoundBeforeTheyJoinAndAfter()
    {
        var arturo = Ids.Entity(0x0240);
        var start = NewGame();
        int[] flags = [29, 32, 73, 244, 262, 1001, 5245];
        var targets = flags.Select(f => Model.Flags.Handle(Ids.Flag(f))).ToList();
        Dictionary<int, List<RecordId>> Out(PlanState state) =>
            PartyNeeds.ByTarget(PartyNeeds.Out(Model, state, targets)).ToDictionary(p => int.Parse(Model.Flags.Id(p.Key).Key), p => p.Value);

        // With Alaron alone: Rheda's talk in Port Saiid needs her out of the party then (5245 wants her joined flag 91 clear), and Batrin's
        // task at the bridge needs Arturo out (5247 wants 247 clear). Once either joins for good, the relaxation can't have them leave.
        var alone = Out(start);
        Assert.Equal([Rheda], alone[262]);
        Assert.Equal([Rheda], alone[5245]);
        Assert.Equal([arturo], alone[244]);
        // No one of Abrecan, Godric and Rheda alone closes the castle's conversations, but any two do (5004): two of them are kept out.
        Assert.Equal(2, alone[29].Count);
        Assert.All(alone[29], m => Assert.Contains(m, new[] { Abrecan, Godric, Rheda }));

        // With Abrecan in, a second of the three closes them (5004): Godric and Rheda are kept out. Godric's agreeing to join (73) needs
        // him out too, though its own conversation brings him in: the job keeps him out but for the steps that set it.
        var abrecan = Out(start.Joined(Abrecan));
        Assert.Equal([Godric, Rheda], abrecan[29]);
        Assert.Equal([Godric, Rheda], abrecan[32]);
        Assert.Contains(Godric, abrecan[73]);
        var joining = PartyJobs.Joining(Model, [Godric], [Ids.Flag(73)]);
        Assert.NotEmpty(joining);
        Assert.All(Model.Actions.Where(a => a.Effects.OfType<SetFlag>().Any(s => s.Flag == Ids.Flag(73) && s.Value)), a => Assert.DoesNotContain(a.Index, joining));
        Assert.All(joining, a => Assert.Contains(Model.Actions[a].Effects, e => e is Join { Entity: var m } && m == Godric));

        // With Rheda in, she must leave first.
        Assert.Equal([Rheda], Out(start.Joined(Rheda))[262]);

        // A party goal of members out is planned too: the party without Rheda is met where she's out, and planned where she's in.
        var planner = new QuestPlanner(Model);
        Assert.True(planner.Achieved(start, new PartyGoal([], [Rheda]), []));
        Assert.Equal([Model.PersonFact(Rheda, false)!.Value], planner.GoalFacts(start, new PartyGoal([], [Rheda])));
        Assert.Equal(new PartyGoal([Becan], [Rheda]), Goal.Parse("party:0x0242,-0x024C"));
    }

    [Fact]
    public void AStoryTriggerAStepOpensIsPlayedByItsWayThatSetsTheMostTargets()
    {
        // In the dryads' wood (218) with the Amaranth (222), from a new game whose opening has played: the queen's conversation (0x03FB at
        // Overworld G12/16) by the way whose Troubadour check wins her trust (231) opens her scene at G12/4 (0x03FC), which plays at once.
        var game = GameState.NewGame(Plugins);
        foreach (var flag in new[] { 3, 218, 222 })
            game.Flags.Set(Ids.Flag(flag), true);
        var reach = Model.Reach;
        try
        {
            Model.Reach = Everywhere.Instance;
            var state = PlanState.From(Model, game);
            var queen = Model.Actions.First(a => a.Place == Ids.MapObject(0, "G12", 16) && a.Effects.Contains(new SetFlag(Ids.Flag(231), true)) && state.CanDo(a));
            var triggers = new StoryTriggers(Model);
            var amulet = Ids.Flag(234);
            var opened = triggers.Opened(state, queen, (_, p) => p.Effects.Contains(new SetFlag(amulet, true)) ? 1 : 0).ToList();
            var (trigger, play) = Assert.Single(opened);
            Assert.Equal(Ids.MapObject(0, "G12", 4), trigger.Id);
            Assert.Equal([26, 62, 83], play.Choices);
            // Its ways all worth the same, it's left to the host's default; one that loses what the other keeps is passed over.
            Assert.Empty(triggers.Opened(state, queen, (_, _) => 0));
            var (_, keeps) = Assert.Single(triggers.Opened(state, queen, (_, p) => p.Effects.Contains(new SetFlag(amulet, true)) ? 0 : -1000));
            Assert.Equal([26, 62, 83], keeps.Choices);
        }
        finally
        {
            Model.Reach = reach;
        }
    }

    /// <summary>Plays a plan's steps over the model's states, each one doable where it comes and the party never past its cap.</summary>
    private static PlanState Play(PlanState state, IEnumerable<PlayerAction> steps)
    {
        foreach (var step in steps)
        {
            Assert.True(state.CanDo(step), $"{step.Describe()} can't be done where it comes: {state.Blocker(step)}");
            state = state.Apply(step);
            Assert.InRange(state.Party.Count, 1, HBHT.Core.Characters.Party.MaxSize);
        }
        return state;
    }
}
