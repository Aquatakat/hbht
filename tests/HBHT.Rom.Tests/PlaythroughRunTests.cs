using HBHT.Core;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's run (docs/systems/playthrough.md, layer 6) on NA 1.0's data: from a new game to the first member, through the
/// Goblin Ambush and out of Oriana's locked hut with the world graph behind the planner's places; the member goal coming back when a
/// member is gone; and the report saying when a step's expected flag doesn't come. The walk graphs come from the cache the world
/// graph's tests keep under the temp folder.
/// </summary>
public sealed class PlaythroughRunTests(Na10Import fixture, ITestOutputHelper output) : IDisposable
{
    private Na10Import.Loaded Data => fixture.Data;

    private static readonly Lock Shared = new();
    private static (PluginSet Set, QuestModel Model, WorldGraph Graph)? _built;

    private readonly string _saves = Path.Combine(Path.GetTempPath(), "hbht-playthrough-run-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_saves))
            Directory.Delete(_saves, recursive: true);
    }

    /// <summary>The model and the world graph, built once for the class.</summary>
    private (QuestModel Model, WorldGraph Graph) Built
    {
        get
        {
            lock (Shared)
            {
                if (_built is not { } built || built.Set != Data.Set)
                    _built = built = (Data.Set, QuestModel.Build(Data.Set), new WorldGraph(Data.Set, Path.Combine(Path.GetTempPath(), "hbht-playthrough-cache")));
                return (built.Model, built.Graph);
            }
        }
    }

    private Playthrough Run(PlaythroughHost host, Goal goal, RunOptions? options = null)
    {
        var (model, graph) = Built;
        return new Playthrough(host, model, graph, goal, (options ?? RunOptions.Default) with { Saves = _saves }) { Progress = output.WriteLine };
    }

    private void Print(RunReport report) => output.WriteLine(report.ToText());

    [Fact]
    public void FromInsideOrianasHutThePlacesOutsideNeedItsDoorsFlagWhichThePlannerTakesAsAPrecondition()
    {
        // The player stood in the hut (6/E03), as the Goblin Ambush leaves him, in a new game's state.
        var (model, graph) = Built;
        var game = GameState.NewGame(Data.Set);
        var hut = Data.Set.Get<ChunkRecord>(RecordId.Parse("aidyn:chunk/6/E03"));
        var set = Data.Set.Get<MapSetRecord>(hut.MapSet);
        var reach = new WorldReach(graph, game, new Place(hut.MapSet, set.Origin(hut) + new System.Numerics.Vector3(4.6f, 0, 5.6f)));
        var castle = Ids.MapObject(4, "I01", 6);
        Assert.Equal([new FlagIs(Ids.Flag(50), true)], reach.Gates(castle));
        Assert.Equal(GateKind.Locked, Assert.Single(reach.Blockers(castle)).Kind);
        Assert.False(reach.OffFoot(castle));
        // Oriana herself is in the hut: no gate on the way.
        Assert.Empty(reach.Gates(Ids.MapObject(6, "E03", 6))!);

        model.Reach = reach;
        try
        {
            var state = PlanState.From(model, game);
            Assert.False(reach.CanReach(castle, state));
            Assert.True(reach.CanReach(castle, state.With(Ids.Flag(50), true)));
            // The castle's actions need flag 50 as a fact, so the relaxation and the backward chain go through what sets it.
            var talk = model.Actions.First(a => a.Place == castle);
            Assert.Contains(model.FlagFact(model.Flags.Handle(Ids.Flag(50)), true), model.Needs(talk.Index));
        }
        finally
        {
            model.Reach = Everywhere.Instance;
        }
    }

    [Fact]
    public void InRoundsNoGateIsOpenedFromBeyondIt()
    {
        // From a new game, the Cradawgh's scene at Overworld A22/6 sets 320, which unlocks the door 11/A01/8 on the shortest way to
        // A22. Found in rounds, that door stays a wall, and A22's way goes round by D14's collision toggle, which Arturo's scene opens.
        var (model, graph) = Built;
        var game = GameState.NewGame(Data.Set);
        try
        {
            var reach = WorldReach.Layered(model, graph, game, PlanState.From(model, game), graph.PlaceOf(game), output.WriteLine);
            var cradawgh = Ids.MapObject(0, "A22", 6);
            var gates = reach.Gates(cradawgh);
            Assert.NotNull(gates);
            Assert.DoesNotContain(new FlagIs(Ids.Flag(320), true), gates);
            Assert.Contains(reach.Blockers(cradawgh), g => g.Object == Ids.MapObject(0, "D14", 0));
            // The south-east island is beyond the Jundar gate.
            Assert.Contains(new FlagIs(Ids.Flag(480), true), reach.Gates(Ids.MapObject(0, "O25", 135))!);
        }
        finally
        {
            model.Reach = Everywhere.Instance;
        }
    }

    [Fact]
    public void FromANewGameTheMemberGoalPlaysThroughTheAmbushAndOutOfOrianasHutUntilSomeoneJoins()
    {
        var host = PlaythroughHost.NewGame(Data.Set);
        var run = Run(host, new MemberGoal(), new RunOptions(MostTicks: 200_000, MostFailures: 4));
        var report = run.Run();
        Print(report);

        Assert.True(report.Summary!.Reached, report.Summary.Why);
        var joined = Assert.Single(report.Milestones, m => m.Kind == MilestoneKind.Joined);
        Assert.Equal(2, joined.Party.Count);

        // The story on the way: Edur's cut-scene, Alaron's, the Goblin Ambush and its battle, and Oriana's two cut-scenes in her hut.
        var scenes = host.Transitions.Select(t => t.Transition).OfType<Transition.StartCutScene>().Select(s => s.Scene.Runner.Script.Id).ToList();
        foreach (var script in new[] { 0x0391, 0x0364, 0x0365, 0x0392, 0x0393 })
            Assert.Contains(Ids.Script(script), scenes);

        // In the hut, whose door (6/E03/2) is locked behind flag 50, the planner chained back through it: the next step talked to Oriana,
        // whose conversation (0x0394) sets 50, and once it was set the step after left the hut.
        var hut = Ids.MapObject(6, "E03", 2);
        var door = Assert.IsType<TeleporterObject>(Data.Objects[hut]);
        Assert.Equal(Ids.Flag(50), door.LockFlag);
        var oriana = run.Played.FindIndex(s => s.Action.Place == Ids.MapObject(6, "E03", 6) && s.Action.Script == Ids.Script(0x0394));
        Assert.True(oriana > 0, "No step talked to Oriana.");
        Assert.True(run.Played[oriana].Done, run.Played[oriana].Summary);
        Assert.Contains(new SetFlag(Ids.Flag(50), true), run.Played[oriana].Action.Effects);
        // The step before it was the one the story left in the hut, whose way out the door then shut.
        Assert.Contains(run.Played[..oriana], s => s.Outcome?.Where?.MapSet == RecordId.Decimal(RecordTypes.MapSet, 6));
        Assert.True(host.Game.Flags.IsSet(Ids.Flag(50)));
        Assert.Contains(run.Played[(oriana + 1)..], s => s.Done && s.Outcome?.Transitions.Count > 0);

        // A save at each milestone, each of which loads.
        var saved = report.Milestones.Where(m => m.Save is not null).ToList();
        Assert.Contains(saved, m => m.Kind == MilestoneKind.Joined);
        Assert.Contains(saved, m => m.Kind == MilestoneKind.Goal);
        Assert.Contains(saved, m => m.Kind == MilestoneKind.MapSet);
        foreach (var milestone in saved)
        {
            Assert.StartsWith(Path.Combine(_saves, $"{int.Parse(Path.GetFileName(milestone.Save!)[..2]):00}-"), milestone.Save);
            var game = GameState.Load(Data.Set, SaveFile.Read(milestone.Save!, Data.Set));
            Assert.Empty(game.Problems);
            Assert.False(game.StartsAsNewGame);
        }
        var last = GameState.Load(Data.Set, SaveFile.Read(saved[^1].Save!, Data.Set));
        Assert.Equal(2, last.Party.Count);
    }

    /// <summary>The game at a story checkpoint, made into a save and loaded as the runtime loads one.</summary>
    private GameState AtCheckpoint(string label)
    {
        var name = TestRoms.Checkpoints().Select(row => row.Data).FirstOrDefault(n => n.Length > 0 && n[..^4].EndsWith($" - {label}", StringComparison.Ordinal))
            ?? "";
        var game = GameState.Load(Data.Set, CheckpointSave.Convert(TestRoms.Checkpoint(name), Data.Set, label));
        Assert.Empty(game.Problems);
        return game;
    }

    [Fact]
    public void AMemberGoneBringsTheMemberGoalBack()
    {
        // Before Rooughah the party is full: the run's goal comes first.
        var game = AtCheckpoint("Before Rooughah");
        Assert.False(game.Party.HasEmptySlot());
        var options = new RunOptions(MostTicks: 1, StepTicks: 30_000, PlanBudget: 300, WriteSaves: false);
        var full = Run(PlaythroughHost.From(game), new EndingGoal(), options);
        var before = full.Run();
        Print(before);
        Assert.All(full.Played, s => Assert.IsNotType<MemberGoal>(s.Goal));
        Assert.DoesNotContain(before.Entries, e => e.Expected == "a member joins");

        // A companion dies, as a battle leaves them: their died flag set, gone from the party, which has a slot again.
        game = AtCheckpoint("Before Rooughah");
        var dead = game.Party.Members.Last(m => m.Entity != GameState.Alaron);
        game.Flags.Set(Data.Set.Get<EntityRecord>(dead.Entity).Member!.Died, true);
        game.Party.Strip(dead);
        game.Party.Remove(dead.Entity);
        Assert.True(game.Party.HasEmptySlot());
        var gone = Run(PlaythroughHost.From(game), new EndingGoal(), options);
        var after = gone.Run();
        Print(after);
        // The member goal came first: a step towards someone joining, or, with none to be had, the goal resting with why.
        Assert.True(gone.Played.FirstOrDefault()?.Goal is MemberGoal || after.Entries.Any(e => e.Expected == "a member joins" && e.Kind == EntryKind.Unreachable),
            string.Join(Environment.NewLine, after.Entries));
        // The dead member isn't among those it plans for.
        Assert.DoesNotContain(gone.Played, s => s.Action.Effects.Contains(new Join(dead.Entity)));
    }

    [Fact]
    public void AtAShopTheBestGainsAreBoughtByTheShopsPresses()
    {
        // Before Rooughah, in Terminor, at the fighters' shop (STTE02, 8/A07/2, which sells the Helm of Defense): its keeper's way to the
        // shop is talked through, and with Shopping set the barter window buys from the stock what gains the party most over what it has, with the
        // party's gold, by its presses; then the shop is left.
        var (model, graph) = Built;
        var host = PlaythroughHost.From(AtCheckpoint("Before Rooughah, in Terminor"));
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        var outfitting = new Outfitting { GoldReserve = 0 };
        host.Menus = new Levelling(outfitting);
        host.Game.Party.Gold = 100_000;
        var keeper = Ids.MapObject(8, "A07", 2);
        var state = PlanState.From(model, host.Game);
        var action = model.Actions.First(a => a.Place == keeper && a.Kind == ActionKind.Talk && a.Way?.End == Core.Scripts.ScriptEndKind.Shop && state.CanDo(a));
        var step = StepTranslation.Translate(action, model, host.Game, graph).Step!;
        outfitting.Shopping = true;
        var outcome = new Pilot(graph).Run(host, step with { Expect = [] }, 30_000);
        foreach (var b in outfitting.Bought)
            output.WriteLine(b);
        Assert.True(outcome.Done, outcome.Reason);
        Assert.Contains(outcome.Transitions, t => t.Transition is Transition.OpenShop);
        Assert.NotEmpty(outfitting.Bought);
        Assert.True(host.Game.Party.Gold < 100_000);
        Assert.False(outfitting.Shopping);
        Assert.True(host.PlayerHasControl);
    }

    // Yeraza in the Overworld's C08 (script 1476).
    private static readonly RecordId Yeraza = Ids.MapObject(0, "C08", 31);

    [Fact]
    public void AStepWhoseExpectedFlagDoesntComeIsReportedWithWhatHappenedAndTheSaveBeforeIt()
    {
        // A new game with the player set down a little way from Yeraza.
        var chunk = Data.Set.Get<ChunkRecord>(ChunkRecord.ChunkOf(Yeraza));
        var npc = Assert.IsType<NpcObject>(Data.Objects[Yeraza]);
        var start = new GameStart(Data.Set.Get<MapSetRecord>(chunk.MapSet), chunk.Name, npc.Position + new System.Numerics.Vector3(4, 2, 0),
            new System.Numerics.Vector2(-1, 0), SetDown: true);
        var host = PlaythroughHost.From(GameState.NewGame(Data.Set), start: start, opening: false);
        Assert.Equal(HostRun.Done, host.RunUntilControl(600));
        var run = Run(host, new FlagGoal(Ids.Flag(84)));

        // A hand-made action: talking to her sets her talked-to flag, as it does, and flag 84, which nothing sets.
        var talked = npc.Used!.Flag;
        var action = new PlayerAction(0, ActionKind.Talk, Yeraza, npc.Script, [], [], [],
            [new SetFlag(talked, true), new SetFlag(Ids.Flag(84), true)]);
        var step = run.Play(action, run.Goal);
        Print(run.Report);

        Assert.False(step.Done);
        Assert.True(step.Outcome!.Used);
        Assert.Equal([ExpectedEffect.FlagSet(Ids.Flag(84))], step.Outcome.Misses);
        var entry = Assert.Single(run.Report.Entries, e => e.Kind == EntryKind.StepMissed);
        Assert.Equal(1, entry.Step);
        Assert.Contains("aidyn:flag/84 set", entry.Expected);
        Assert.DoesNotContain(talked.ToString() + " set", entry.Expected);
        Assert.Contains($"set {talked.Key}", entry.Happened);
        Assert.Equal(chunk.MapSet.Key, entry.Where!.MapSet);
        Assert.True(entry.Tick > 0);
        // The save before the step, which plays it again.
        Assert.NotNull(entry.Save);
        var before = GameState.Load(Data.Set, SaveFile.Read(entry.Save!, Data.Set));
        Assert.Empty(before.Problems);
        Assert.False(before.Flags.IsSet(talked));
        Assert.Contains("talk to aidyn:mapobject/0/C08/31", run.Report.ToText());
        Assert.Contains("\"StepMissed\"", run.Report.ToJson());
    }
}
