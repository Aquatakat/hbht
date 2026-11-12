using HBHT.Core;
using HBHT.Core.Flags;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's actions for what the original's code does rather than a map object (docs/systems/playthrough.md, "What the
/// planner doesn't model"), on NA 1.0's data: a camp scene played by camping in its chunk, a container's trap let go off, the help menu
/// read from the System tab, a key item bought in a shop, and the clock waited for, by a cover run at its end and by the pilot for an npc
/// only the night brings out.
/// </summary>
public sealed class PlaythroughEngineActionsTests(Na10Import fixture, ITestOutputHelper output) : IDisposable
{
    private Na10Import.Loaded Data => fixture.Data;

    private static readonly Lock Shared = new();
    private static (PluginSet Set, QuestModel Model, WorldGraph Graph)? _built;

    private readonly string _saves = Path.Combine(Path.GetTempPath(), "hbht-playthrough-engine-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_saves))
            Directory.Delete(_saves, recursive: true);
    }

    /// <summary>The model and the world graph, built once for the class, the graphs from the cache the world graph's tests keep.</summary>
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
        return new Playthrough(host, model.ForRun(), graph, goal, (options ?? RunOptions.Default) with { Saves = _saves }) { Progress = output.WriteLine };
    }

    /// <summary>A new game with the player set down a little way from a map object, its opening not played.</summary>
    private PlaythroughHost Near(RecordId mapObject, Action<GameState>? setUp = null, float off = 4)
    {
        var chunk = Data.Set.Get<ChunkRecord>(ChunkRecord.ChunkOf(mapObject));
        var o = chunk.Object(ChunkRecord.IndexOf(mapObject))!;
        var start = new GameStart(Data.Set.Get<MapSetRecord>(chunk.MapSet), chunk.Name, o.Position + new System.Numerics.Vector3(off, 2, 0),
            new System.Numerics.Vector2(-1, 0), SetDown: true);
        var game = GameState.NewGame(Data.Set);
        setUp?.Invoke(game);
        var host = PlaythroughHost.From(game, start: start, opening: false);
        Assert.Equal(HostRun.Done, host.RunUntilControl(600));
        return host;
    }

    [Fact]
    public void TheTargetsTakeInTheCampScenesTheTrapsTheHelpAndTheClock()
    {
        var (model, _) = Built;
        var game = GameState.NewGame(Data.Set);
        var state = PlanState.From(model, game);
        var targets = CoverGoal.StaticTargets(model, state, game).ToHashSet();
        // The camp south of the goblin camp (Overworld B07/0, 0x03CC) plays as the party camps in its chunk: its scene's flag (5056, over
        // the check flag 4240) and its done flag (146) are targets, and so is the check flag, which the camp sets and clears again.
        Assert.Contains(Ids.Flag(5056), targets);
        Assert.Contains(Ids.Flag(146), targets);
        Assert.Contains(Ids.Flag(4240), targets);
        var camp = model.Actions.Single(a => a.Place == Ids.MapObject(0, "B07", 0));
        Assert.Equal(ActionKind.Camp, camp.Kind);
        Assert.Equal(UseKind.Camp, StepTranslation.UseOf(camp.Kind));
        Assert.Contains(new FlagIs(Ids.Flag(145), true), camp.Presence);
        Assert.Contains(new FlagIs(Ids.Flag(154), false), camp.Presence);
        Assert.Equal([new SetFlag(Ids.Flag(4240), true), new SetFlag(Ids.Flag(146), true), new SetFlag(Ids.Flag(4240), false)], camp.Effects);
        Assert.Contains(camp.Requires, r => r is CodeGate { Check.Kind: CodeCheckKind.Ambush });

        // A trapped chest's exploded flag: Overworld I12/41's (a trap of 2), which a trap going off sets.
        var chest = Assert.IsType<ContainerObject>(Data.Objects[Ids.MapObject(0, "I12", 41)]);
        Assert.Contains(chest.Exploded!.Value, targets);
        var spring = model.Actions.Single(a => a.Place == Ids.MapObject(0, "I12", 41) && a.Kind == ActionKind.Spring);
        Assert.Equal([new SetFlag(chest.Exploded!.Value, true)], spring.Effects);
        // Alaron alone has no Mechanic, so the trap always goes off for him; one he always disarms would never go off.
        Assert.Null(CodeGates.Meets(spring.Requires.OfType<CodeGate>().Single(), game));

        // The help menu's done flag (3759), which only the System tab's Help sets.
        Assert.Contains(Ids.Flag(3759), targets);
        Assert.Single(model.Actions, a => a.Kind == ActionKind.Help);

        // The clock's: night (5283), which a new game's noon doesn't hold, and the Burial Chamber's door, which night opens (5022); with
        // no game given, the planner's either-way engine flags make neither a target.
        Assert.Contains(Ids.Flag(5283), targets);
        Assert.Contains(Ids.Flag(5022), targets);
        Assert.DoesNotContain(Ids.Flag(5284), targets);
        Assert.DoesNotContain(Ids.Flag(5283), CoverGoal.StaticTargets(model, state));
        Assert.Equal(Core.Calendar.GameClock.Day, model.Flags.Horizon(model.Flags.Handle(Ids.Flag(5283))));
        Assert.Equal(4 * Core.Calendar.GameClock.Week, model.Flags.Horizon(model.Flags.Handle(Ids.Flag(5276))));
        Assert.Equal(0u, model.Flags.Horizon(model.Flags.Handle(Ids.Flag(2312))));
    }

    [Fact]
    public void CampingInTheChunkOfACampScenePlaysIt()
    {
        // Told to fight the goblins (145), Kitarak alive: the camp at Overworld B07 plays 0x03CC as the party camps there. Alaron alone,
        // with fewer than two of Abrecan, Godric and Rheda, is never ambushed.
        var (model, _) = Built;
        var place = Ids.MapObject(0, "B07", 0);
        var host = Near(place, g => g.Flags.Set(Ids.Flag(145), true), off: 20);
        var run = Run(host, new FlagGoal(Ids.Flag(146)));
        var camp = model.Actions.Single(a => a.Place == place);
        var step = run.Play(camp, run.Goal);
        output.WriteLine(run.Report.ToText());

        Assert.True(step.Done, step.Summary);
        Assert.True(host.Game.Flags.IsSet(Ids.Flag(146)));
        Assert.False(host.Game.Flags.IsSet(Ids.Flag(4240)));
        Assert.Equal(1, run.Resting.Camps);
        Assert.True(host.PlayerHasControl);
    }

    [Fact]
    public void ATrapLetGoOffDestroysItsChestAndSetsItsExplodedFlag()
    {
        var (model, _) = Built;
        var place = Ids.MapObject(0, "I12", 41);
        var chest = Assert.IsType<ContainerObject>(Data.Objects[place]);
        var host = Near(place, off: 1.5f);
        var run = Run(host, new FlagGoal(chest.Exploded!.Value));
        var spring = model.Actions.Single(a => a.Place == place && a.Kind == ActionKind.Spring);
        var step = run.Play(spring, run.Goal);
        output.WriteLine(run.Report.ToText());

        Assert.True(step.Done, step.Summary);
        Assert.True(host.Game.Flags.IsSet(chest.Exploded!.Value));
        Assert.False(host.Game.Flags.IsSet(chest.Opened!.Value));
        // Destroyed, it can't be opened, nor go off again.
        var state = PlanState.From(model, host.Game);
        Assert.All(model.Actions.Where(a => a.Place == place), a => Assert.False(state.CanDo(a)));
    }

    [Fact]
    public void TheHelpMenuIsReadFromTheSystemTab()
    {
        var host = PlaythroughHost.NewGame(Data.Set);
        Assert.Equal(HostRun.Done, host.RunUntilControl(60 * 60 * 5));
        var run = Run(host, new FlagGoal(Ids.Flag(3759)));
        var help = run.Model.Actions.Single(a => a.Kind == ActionKind.Help);
        Assert.False(host.Game.Flags.IsSet(Ids.Flag(3759)));
        var step = run.Play(help, run.Goal);
        output.WriteLine($"{step.Summary}; pages {host.Pages.Count}");

        Assert.True(step.Done, step.Summary);
        Assert.True(host.Game.Flags.IsSet(Ids.Flag(3759)));
        Assert.Contains(host.Pages, p => p.Script == Core.Ui.SystemMenu.HelpScript);
        Assert.True(host.PlayerHasControl);
    }

    [Fact]
    public void AStepsKeyItemIsBoughtFirstWhateverItGains()
    {
        // Map 7 (0x010A), which only Talewok's shop sells (JundarInteriors I05/2, 0x03EC): a party with less gold than its price doesn't
        // meet the buying step's gate, and one with more leaves it to the shop.
        var (model, graph) = Built;
        var map = RecordId.Hex(RecordTypes.Item, 0x010A);
        var gate = model.Actions.First(a => a.Buys == map).Requires.OfType<CodeGate>().Single(g => g.Check?.Kind == CodeCheckKind.Buy);
        var poor = GameState.NewGame(Data.Set);
        poor.Party.Gold = 0;
        Assert.False(CodeGates.Meets(gate, poor));
        poor.Party.Gold = 50_000;
        Assert.Null(CodeGates.Meets(gate, poor));

        // Before Rooughah, in Terminor, at the fighters' shop (STTE02, 8/A07/2): the item a step is to buy comes first, though it gains
        // the party least of the stock; then what's better.
        var host = PlaythroughHost.From(AtCheckpoint("Before Rooughah, in Terminor"));
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        var outfitting = new Outfitting();
        host.Menus = new Levelling(outfitting);
        host.Game.Party.Gold = 50_000;
        var keeper = Ids.MapObject(8, "A07", 2);
        var npc = Assert.IsType<NpcObject>(Data.Objects[keeper]);
        var catalog = host.Game.Party.Inventory.Catalog;
        RecordId? wanted = Core.Items.Shop.For(Data.Set.All<ShopRecord>(RecordTypes.Shop), npc.Entity)!.Stock.Select(s => s.Item)
            .Where(i => !host.Game.Party.HasItem(i) && catalog[i].Price <= 1_000).MinBy(i => outfitting.Gain(host.Game.Party, catalog[i]));
        Assert.NotNull(wanted);
        var state = PlanState.From(model, host.Game);
        var shop = model.Actions.First(a => a.Place == keeper && a.Kind == ActionKind.Talk && a.Way?.End == Core.Scripts.ScriptEndKind.Shop && state.CanDo(a));
        var step = StepTranslation.Translate(shop, model, host.Game, graph).Step!;
        (outfitting.Shopping, outfitting.Buy) = (true, wanted);
        var outcome = new Pilot(graph).Run(host, step with { Expect = [] }, 30_000);
        foreach (var b in outfitting.Bought)
            output.WriteLine(b);
        Assert.True(outcome.Done, outcome.Reason);
        Assert.True(host.Game.Party.HasItem(wanted.Value));
        Assert.Contains($" {catalog[wanted.Value].Name} for ", outfitting.Bought[0]);
    }

    private GameState AtCheckpoint(string label)
    {
        var name = TestRoms.Checkpoints().Select(row => row.Data).FirstOrDefault(n => n.Length > 0 && n[..^4].EndsWith($" - {label}", StringComparison.Ordinal))
            ?? "";
        var game = GameState.Load(Data.Set, Rig.CheckpointSave.Convert(TestRoms.Checkpoint(name), Data.Set, label));
        Assert.Empty(game.Problems);
        return game;
    }

    [Fact]
    public void ACoverRunWithOnlyTheClockLeftWaitsForIt()
    {
        // After the opening it's a little past noon: night (5283) and its first bit of the part of the day (2303) come in about nine
        // hours, which a cover run waits for where the player stands once nothing else is live.
        var host = PlaythroughHost.NewGame(Data.Set);
        Assert.Equal(HostRun.Done, host.RunUntilControl(60 * 60 * 5));
        Assert.False(host.Game.Flags.Get(Ids.Flag(5283)));
        var start = host.Clock.Ticks;
        var run = Run(host, new CoverGoal([Ids.Flag(5283), Ids.Flag(2303)]), RunOptions.Default with { WriteSaves = false });
        run.Run();
        var cover = run.Coverage!;
        output.WriteLine(cover.ToString());
        Assert.All(cover.Targets, t => Assert.True(t.State == TargetState.Covered, $"{t.Flag}: {t.State}, {t.Why}"));
        Assert.Empty(run.Played);
        Assert.InRange(host.Clock.Ticks - start, 1, Core.Calendar.GameClock.Day / Core.Calendar.GameClock.DaySpeed);
    }

    [Fact]
    public void ThePilotWaitsForTheNightToBringAnNpcOut()
    {
        // Overworld H13/18 is there only at night (5283). From a new game's noon set down by it, the step to talk to it waits where he stands
        // an hour at a time; once night comes, trek's world is built again, as after a backdrop, so the npc is there, and he talks to it.
        var (model, _) = Built;
        var npc = Ids.MapObject(0, "H13", 18);
        var host = Near(npc, off: 6);
        Assert.False(host.Game.Flags.Get(Ids.Flag(5283)));
        var run = Run(host, new FlagGoal(Ids.Flag(5283)));
        var talk = model.Actions.First(a => a.Place == npc && a.Kind == ActionKind.Talk);
        var step = run.Play(talk, run.Goal);
        output.WriteLine(run.Report.ToText());

        Assert.True(step.Outcome!.Used, step.Summary);
        Assert.True(host.Game.Flags.Get(Ids.Flag(5283)));
    }
}
