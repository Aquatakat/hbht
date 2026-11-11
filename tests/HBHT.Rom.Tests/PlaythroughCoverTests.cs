using HBHT.Core;
using HBHT.Core.Flags;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's flag coverage (docs/systems/playthrough.md, "Flag coverage") on NA 1.0's data: the ledger seeing what the game
/// sets, the loss check on the planner's states, and a cover job from a new game over the first chests, played through the queue.
/// </summary>
public sealed class PlaythroughCoverTests(Na10Import fixture, ITestOutputHelper output) : IDisposable
{
    private Na10Import.Loaded Data => fixture.Data;

    private static readonly Lock Shared = new();
    private static (PluginSet Set, QuestModel Model, WorldGraph Graph)? _built;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hbht-playthrough-cover-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
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

    [Fact]
    public void TheLedgerSeesWhatANewGamesOpeningSetsAndKeepsItAcrossALoadedSave()
    {
        // The host begins the new game as the title does, which sets a few flags before anyone can watch (788 and 2322 among them).
        var host = PlaythroughHost.NewGame(Data.Set);
        var flags = host.Game.Flags;
        var begun = Enumerable.Range(0, flags.Count).Select(flags.Get).ToArray();
        var ledger = host.Ledger = new FlagLedger(flags.Count);
        Assert.Same(ledger, flags.Ledger);
        Assert.Equal(HostRun.Done, host.RunUntilControl(60 * 60 * 5));

        // Every flag set since then was seen to turn on: the opening's done flags among them.
        var set = Enumerable.Range(2, flags.Count - 2).Where(h => flags.Get(h) && !begun[h]).ToList();
        Assert.Contains(3, set);
        Assert.All(set, h => Assert.True(ledger.Seen(h), $"flag {flags.Id(h)} is set and wasn't seen"));
        output.WriteLine($"{set.Count} set since the new game, {ledger.Count} seen; seen and clear again: " +
            string.Join(", ", ledger.All().Where(h => !flags.Get(h)).Select(h => flags.Id(h).Key)));

        // The game saved and loaded where the player stands, as the run does when he's stuck: the ledger goes on with the loaded game,
        // which marks nothing as it loads, and sees a flag a map object sets as it's set and then cleared.
        var count = ledger.Count;
        host.Restart(GameState.Load(Data.Set, SaveFile.Parse(SaveFile.Serialize(host.Game.Save()), Data.Set)));
        Assert.Same(ledger, host.Game.Flags.Ledger);
        Assert.Equal(count, ledger.Count);
        var chest = Ids.Flag(1902);
        Assert.False(host.Game.Flags.Get(chest));
        host.Game.Flags.TrySetAndProcess(chest, true);
        host.Game.Flags.TrySetAndProcess(chest, false);
        Assert.True(ledger.Seen(host.Game.Flags.Handle(chest)));
    }

    private static PlayerAction Way(QuestModel model, RecordId place, Func<PlayerAction, bool> which) =>
        model.Actions.Single(a => a.Place == place && which(a));

    [Fact]
    public void TheLossCheckSaysTellingTheMaleDryadArdraSentYouLosesTheirFightAndAChestLosesNothing()
    {
        var (model, _) = Built;
        model.Reach = Everywhere.Instance;
        // In the dryads' wood (218), from a new game whose opening has played.
        var game = GameState.NewGame(Data.Set);
        game.Flags.Set(Ids.Flag(3), true);
        game.Flags.Set(Ids.Flag(218), true);
        var state = PlanState.From(model, game);
        Assert.False(state.Opening);
        var relaxation = new Relaxation(model, Enumerable.Range(0, model.Actions.Count));
        var result = relaxation.Run(state);
        var live = Enumerable.Range(2, model.Flags.Count - 2)
            .Where(h => model.Flags.Kind(h) is FlagKind.Plain or FlagKind.Derived && result.Reached(model.FlagFact(h, true)) && !LossCheck.SetFor(state, h))
            .ToList();
        int Handle(int flag) => model.Flags.Handle(Ids.Flag(flag));

        // The male dryad (G12/7, 0x03F9) offends only while 226 is clear: "Ardra sent me" (choice 45) sets it, so the fight that sets
        // 3663 can't come after it.
        var male = Ids.MapObject(0, "G12", 7);
        var ardra = Way(model, male, a => a.Choices.SequenceEqual([45]));
        Assert.True(state.CanDo(ardra), $"{state.Blocker(ardra)}");
        Assert.Contains(new SetFlag(Ids.Flag(226), true), ardra.Effects);
        Assert.Equal([Handle(3663)], LossCheck.Lost(relaxation, state, ardra, live));

        // Offending them (choice 10, the song failed) loses nothing the relaxation can tell: their scene at G12/5 makes peace again once
        // the Gorgon's task is done (its way for 229 clears it and sets 226).
        var offend = Way(model, male, a => a.Choices.SequenceEqual([10]));
        Assert.Contains(new SetFlag(Ids.Flag(229), true), offend.Effects);
        Assert.Empty(LossCheck.Lost(relaxation, state, offend, live));

        // A plain chest by where a new game begins loses nothing.
        var chest = Way(model, Ids.MapObject(0, "I08", 111), a => a.Kind == ActionKind.Open);
        Assert.True(state.CanDo(chest));
        Assert.Empty(LossCheck.Lost(relaxation, state, chest, live));
    }

    [Fact]
    [Trait("Category", "Slow")]
    public void ACoverJobFromANewGameCoversTheChestsAboutWhereItBeginsNearestFirst()
    {
        var (model, graph) = Built;
        // Seven chests about Overworld I08, where a new game puts the player, by their opened flags.
        int[] chests = [1902, 1896, 3302, 1900, 3300, 1894, 1898];
        var queue = new CoverQueue(_root);
        queue.Init(new CoverRoot([.. chests.Select(c => Ids.Flag(c).ToString())], null, 10, 3, DateTimeOffset.UtcNow));
        var job = queue.Claim();
        Assert.NotNull(job);
        var worker = new CoverWorker(queue, model, graph) { Progress = output.WriteLine };
        var result = worker.Work(job);
        output.WriteLine(File.ReadAllText(Path.ChangeExtension(queue.ReportOf(job.Id), ".txt")));

        Assert.All(result.Targets, t => Assert.True(t.State == TargetState.Covered, $"{t.Flag}: {t.State}, {t.Why}"));
        Assert.Empty(result.Made);
        Assert.Equal(JobStatus.Done, queue.Find(job.Id));
        Assert.Null(queue.Claim());
        // The ledger has each, with the save from before the step that set it, which loads.
        var ledger = queue.Ledger();
        foreach (var c in chests)
        {
            var (by, seen) = ledger[Ids.Flag(c).ToString()];
            Assert.Equal("0", by);
            Assert.NotNull(seen.Save);
            Assert.Empty(GameState.Load(Data.Set, SaveFile.Read(queue.Resolve(seen.Save!), Data.Set)).Problems);
        }
        // One step a chest, or near it: nothing else was wanted.
        Assert.InRange(result.Steps, chests.Length, chests.Length + 3);
    }
}
