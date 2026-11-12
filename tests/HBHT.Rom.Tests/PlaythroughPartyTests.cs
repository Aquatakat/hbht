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
