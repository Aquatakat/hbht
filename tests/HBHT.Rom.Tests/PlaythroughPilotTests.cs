using System.Diagnostics;
using System.Numerics;
using HBHT.Core;
using HBHT.Core.Combat;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's pilot (docs/systems/playthrough.md, layer 4) on NA 1.0's data, through a real <see cref="PlaythroughHost"/>
/// session: from a new game played to trek, it walks to an npc and talks, goes through a door to open a chest, takes a wanted choice,
/// and plays the story on from where the opening leaves the player to the Goblin Ambush and Oriana's hut. The walk graphs come from the
/// cache the world graph's tests keep under the temp folder.
/// </summary>
public sealed class PlaythroughPilotTests(Na10Import fixture, ITestOutputHelper output)
{
    private Na10Import.Loaded Data => fixture.Data;

    private static WorldGraph? _graph;
    private static readonly Lock GraphLock = new();

    private WorldGraph Graph
    {
        get
        {
            lock (GraphLock)
                return _graph ??= new WorldGraph(Data.Set, Path.Combine(Path.GetTempPath(), "hbht-playthrough-cache"));
        }
    }

    /// <summary>A new game played to trek at Overworld I08, where nothing more comes to the player.</summary>
    private static PlaythroughHost NewGame(PluginSet plugins)
    {
        var host = PlaythroughHost.NewGame(plugins);
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        return host;
    }

    private StepOutcome Run(PlaythroughHost host, PilotStep step, long most, PilotOptions? options = null)
    {
        var wall = Stopwatch.StartNew();
        var outcome = new Pilot(Graph, options).Run(host, step, most);
        output.WriteLine($"{outcome} in {wall.Elapsed.TotalSeconds:0.0} s");
        foreach (var t in outcome.Transitions)
            output.WriteLine($"  {t}{(t.Transition is Transition.StartCutScene s ? $" {s.Scene.Runner.Script.Id}" : "")}");
        foreach (var b in outcome.Battles)
            output.WriteLine($"  battle {b}");
        foreach (var p in outcome.Pages.Where(p => p.Choice is not null))
            output.WriteLine($"  choice {p}");
        foreach (var m in outcome.Misses)
            output.WriteLine($"  missed {m}");
        foreach (var w in host.Warnings)
            output.WriteLine($"  {w}");
        output.WriteLine($"  unexpected: {outcome.Unexpected}");
        return outcome;
    }

    /// <summary>The flag talking to an npc sets, as the quest index has it.</summary>
    private RecordId TalkedTo(RecordId npc) =>
        Data.Index.UsesBy(npc).Single(u => u.Role == FlagRole.PartyTalkedTo && u.Access == FlagAccess.Set).Flag;

    /// <summary>
    /// A new game played to trek, then the same game begun again <paramref name="back"/> units of walking short of the end of the first
    /// leg of the route to <paramref name="target"/>, facing along it. Everything out of where the opening leaves the player goes through
    /// the story (Edur's cut-scene, the ambush, Oriana's hut, whose door is locked), so a walk anywhere else begins nearer.
    /// </summary>
    private PlaythroughHost Short(RecordId target, float back)
    {
        var played = NewGame(Data.Set);
        var leg = Graph.Route(played.Game, target).Route!.Legs[0];
        var w = leg.Waypoints;
        var (i, walked) = (w.Count - 1, 0f);
        for (; i > 0 && walked < back; i--)
            walked += Vector3.Distance(w[i], w[i - 1]);
        var set = Data.Set.Get<MapSetRecord>(leg.MapSet);
        var at = w[i];
        var chunk = Data.Set.ChunksIn(leg.MapSet).Single(c => c.Row == (int)MathF.Floor(at.X / set.CellSize) && c.Column == (int)MathF.Floor(at.Z / set.CellSize));
        var on = w[Math.Min(i + 3, w.Count - 1)] - at;
        var heading = Vector2.Normalize(new Vector2(on.X, on.Z));
        output.WriteLine($"begun at {chunk.Name} {at}, {walked:0} units of walking short of {(leg.Teleporter is { } t ? t : target)}");
        return PlaythroughHost.From(played.Game, start: new GameStart(set, chunk.Name, at - set.Origin(chunk) + Vector3.UnitY, heading, SetDown: true),
            opening: false);
    }

    // A Male Commoner in Gwernia (script 1201), the nearest npc to the new game's start that talks without a cut-scene's script object
    // about it: those nearer (Edur, Zurene, Alaron's) are the story's, set off by walking near.
    private static readonly RecordId Commoner = Ids.MapObject(0, "M07", 31);

    [Fact]
    public void FromANewGameThePilotWalksToAnNpcInTheOverworldAndTalksToIt()
    {
        var host = Short(Commoner, 120);
        var flag = TalkedTo(Commoner);
        Assert.False(host.Game.Flags.IsSet(flag));

        var outcome = Run(host, new PilotStep(Commoner, Expect: [ExpectedEffect.FlagSet(flag)]), 20_000);

        Assert.True(outcome.Done, outcome.Reason);
        Assert.True(outcome.Arrived && outcome.Used);
        Assert.Empty(outcome.Misses);
        Assert.Contains(flag, outcome.Changes.FlagsSet);
        Assert.DoesNotContain(flag, outcome.Unexpected.FlagsSet);
        var script = Data.Set.Get<ScriptRecord>(Assert.IsType<NpcObject>(Data.Objects[Commoner]).Script);
        Assert.Contains(outcome.Pages, p => p.Script == script.Id && p.Source == PageSource.Conversation);
        Assert.True(host.PlayerHasControl);
    }

    // A chest in the Gwernia interiors (map set 5), through a door of the Overworld.
    private static readonly RecordId InteriorChest = Ids.MapObject(5, "C03", 9);

    [Fact]
    public void ThePilotGoesThroughADoorIntoAnInteriorAndOpensAChestTakingItsLoot()
    {
        var host = Short(InteriorChest, 60);
        var chest = Assert.IsType<ContainerObject>(Data.Objects[InteriorChest]);
        var opened = chest.Opened!.Value;

        var outcome = Run(host, new PilotStep(InteriorChest, Expect: [ExpectedEffect.FlagSet(opened)]), 20_000);

        Assert.True(outcome.Done, outcome.Reason);
        Assert.Empty(outcome.Misses);
        Assert.True(host.Game.Flags.IsSet(opened));
        Assert.Equal(RecordId.Decimal(RecordTypes.MapSet, 5), outcome.Where!.Value.MapSet);
        // Through the door: the teleport took the controls, and the pilot asked for the route again inside.
        Assert.True(outcome.Replans >= 1);
        // Its loot is taken, through the loot window, and the window left.
        Assert.True(outcome.Changes.Gold > 0 || outcome.Changes.Items.Any(i => i.After > i.Before), outcome.Changes.ToString());
        Assert.Contains(outcome.Transitions, t => t.Transition is Transition.OpenMenu or Transition.OpenLoot);
        Assert.DoesNotContain(host.Warnings, w => w.Text.Contains("pause menu", StringComparison.Ordinal));
    }

    /// <summary>Takes the first choice, and records every page with choices as it came up: its script, its node and the nodes it offered.</summary>
    private sealed class Probe : IConversationPolicy
    {
        public List<(RecordId Script, int Node, int[] Offered)> Seen { get; } = [];

        public int ReadTicks => 0;

        public int Choose(ChoiceContext page)
        {
            Seen.Add((page.Script.Id, page.Page.Line.Node, [.. page.Page.Choices.Select(c => c.Node)]));
            return 0;
        }
    }

    // Yeraza in the Overworld's C08 (script 1476), whose conversation comes to a page of three choices at node 7.
    private static readonly RecordId Yeraza = Ids.MapObject(0, "C08", 31);

    /// <summary>A new game's state, with the player set down a little way from Yeraza, facing her.</summary>
    private PlaythroughHost NearYeraza()
    {
        var chunk = Data.Set.Get<ChunkRecord>(ChunkRecord.ChunkOf(Yeraza));
        var npc = Data.Objects[Yeraza];
        var start = new GameStart(Data.Set.Get<MapSetRecord>(chunk.MapSet), chunk.Name, npc.Position + new Vector3(4, 2, 0), new Vector2(-1, 0), SetDown: true);
        return PlaythroughHost.From(GameState.NewGame(Data.Set), start: start, opening: false);
    }

    [Fact]
    public void AWantedChoiceThatIsntTheFirstIsTakenAndRecorded()
    {
        // First with the first choice everywhere, to see the pages with choices the conversation comes to.
        var probe = new Probe();
        var host = NearYeraza();
        host.Conversations = probe;
        var first = Run(host, new PilotStep(Yeraza), 6000);
        Assert.True(first.Done, first.Reason);
        var (script, node, offered) = probe.Seen.First(s => s.Offered.Length > 1);

        // Then wanting its second, the same way.
        var want = new ChoiceWanted(script, node, offered[1]);
        var unseen = new ChoiceWanted(script, 9999, 1);
        host = NearYeraza();
        var outcome = Run(host, new PilotStep(Yeraza, Choices: [want, unseen]), 6000);
        Assert.True(outcome.Done, outcome.Reason);
        Assert.Contains(want, outcome.ChoicesTaken);
        Assert.Empty(outcome.ChoicesMissed);
        Assert.Equal([unseen], outcome.ChoicesNotReached);
        var taken = outcome.Pages.First(p => p.Script == script && p.Node == node && p.Choice is not null);
        Assert.Equal((1, offered[1]), (taken.Choice!.Value, taken.ChoiceNode!.Value));
        // The host's default is back.
        Assert.IsType<FirstChoice>(host.Conversations);
    }

    [Fact]
    public void AWantedChoiceThePageDoesntOfferIsRecordedAndTheDefaultTaken()
    {
        var probe = new Probe();
        var host = NearYeraza();
        host.Conversations = probe;
        Assert.True(Run(host, new PilotStep(Yeraza), 6000).Done);
        var (script, node, offered) = probe.Seen.First(s => s.Offered.Length > 1);

        var want = new ChoiceWanted(script, node, -5);
        var outcome = Run(NearYeraza(), new PilotStep(Yeraza, Choices: [want]), 6000);
        Assert.True(outcome.Done, outcome.Reason);
        var miss = Assert.Single(outcome.ChoicesMissed);
        Assert.Equal(want, miss.Wanted);
        Assert.Equal(offered, miss.Offered);
        Assert.Empty(outcome.ChoicesTaken);
        Assert.Equal(offered[0], outcome.Pages.First(p => p.Script == script && p.Node == node && p.Choice is not null).ChoiceNode);
    }

    [Fact]
    public void AStepToAnObjectNothingReachesFailsWithTheWorldGraphsReason()
    {
        var host = NearYeraza();
        // Enabled while flag 5552 is set, which a new game hasn't.
        var outcome = Run(host, new PilotStep(Ids.MapObject(0, "A22", 13)), 600);
        Assert.Equal(StepFailure.NoRoute, outcome.Failure);
        Assert.Equal(RefusalKind.ObjectGated, outcome.Refusal!.Kind);
        Assert.False(outcome.Arrived || outcome.Used);
        Assert.True(outcome.Changes.IsEmpty);
    }

    // The story's next step after the opening.

    private static readonly RecordId Ambush = Ids.Script(0x0365);

    /// <summary>Whether a map object is there and enabled in a game, by its own gates.</summary>
    private static bool Open(GameState game, RecordId id, MapObject o) => WorldGraph.ObjectGates(id, o).All(g => !g.Closed(game.Flags));

    /// <summary>The nearest of some map objects to where the player stands, on foot, by the world graph's route.</summary>
    private RecordId Nearest(PlaythroughHost host, IEnumerable<RecordId> objects) =>
        objects.Select(id => (Id: id, Route: Graph.Route(host.Game, PlaceOf(host), id).Route)).Where(r => r.Route is not null)
            .OrderBy(r => r.Route!.Length).First().Id;

    private static Place PlaceOf(PlaythroughHost host) => new(host.Session.Trek.Chunks.MapSet.Id, host.Session.Trek.Player.Feet);

    /// <summary>
    /// What leads the story on from where the opening leaves the player, read from the data: every map object that starts the Goblin
    /// Ambush (script 0x0365) is shut in a new game by its enabled flag, a mask of plain flags; the ones it wants set and that are clear
    /// are set by scripts, and the map objects that start those scripts and are open now are the way on, the nearest first.
    /// </summary>
    private (RecordId Leads, RecordId Flag, RecordId Script) StoryStep(PlaythroughHost host)
    {
        var game = host.Game;
        var starters = Data.Index.StartersOf(Ambush).Select(s => s.By).ToList();
        Assert.All(starters, s => Assert.False(Open(game, s, Data.Objects[s])));
        var enabled = starters.Select(s => Data.Objects[s].Enabled!).Distinct().ToList();
        var gate = Assert.Single(enabled);
        var formula = Data.Set.Get<FlagRecord>(gate.Flag).Formula!;
        Assert.Equal(FlagOperation.Mask, formula.Op);
        // The inputs the mask wants set (its operand's bits, the first input lowest) and are clear.
        var wanted = formula.Inputs.Where((f, i) => (formula.Operand >> i & 1) == 1 && !game.Flags.IsSet(f)).ToList();
        var flag = Assert.Single(wanted);
        var setters = Data.Index.UsesOf(flag).Where(u => u.Access == FlagAccess.Set && u.By.Type == RecordTypes.Script).Select(u => u.By).Distinct().ToList();
        var script = Assert.Single(setters);
        var leads = Nearest(host, Data.Index.StartersOf(script).Select(s => s.By).Where(s => Open(game, s, Data.Objects[s])));
        return (leads, flag, script);
    }

    [Fact]
    public void FromANewGameThePilotPlaysTheStoryOnToTheGoblinAmbushAndOrianasHut()
    {
        var host = NewGame(Data.Set);

        // Edur's cut-scene (0x0391) sets 45, which opens the ambush's map objects (5466, 45 set and 11 clear).
        var (leads, flag, script) = StoryStep(host);
        output.WriteLine($"{leads} starts {script}, which sets {flag}");
        Assert.Equal((Ids.Script(0x0391), Ids.Flag(45)), (script, flag));
        var edur = Run(host, new PilotStep(leads, Expect: [ExpectedEffect.FlagSet(flag)]), 20_000);
        Assert.True(edur.Done, edur.Reason);
        Assert.Empty(edur.Misses);
        Assert.Contains(edur.Transitions, t => t.Transition is Transition.StartCutScene s && s.Scene.Runner.Script.Id == script);

        // Now the ambush's map objects are open: the nearest is walked to and set off.
        var ambushers = Data.Index.StartersOf(Ambush).Select(s => s.By).Where(s => Open(host.Game, s, Data.Objects[s])).ToList();
        Assert.NotEmpty(ambushers);
        var at = Nearest(host, ambushers);
        output.WriteLine($"the ambush from {at}");
        var ambush = Run(host, new PilotStep(at, Expect: [ExpectedEffect.FlagSet(Ids.Flag(11))]), 60_000);
        Assert.True(ambush.Done, ambush.Reason);
        Assert.Empty(ambush.Misses);

        // On the way, Alaron's own cut-scene (0x0364), whose three map objects close the way from Edur's to the ambush, so the pilot can't
        // keep clear of it; then the ambush's, the battle spared, trek built at Oriana's hut, which walks into her two cut-scenes, then
        // trek in the hut's map set. A wanderer's battle on the way, won outright, is its own.
        var scenes = ambush.Transitions.Select(t => t.Transition).OfType<Transition.StartCutScene>().Select(s => s.Scene.Runner.Script.Id).ToList();
        Assert.Equal([Ids.Script(0x0364), Ambush, Ids.Script(0x0392), Ids.Script(0x0393)], scenes);
        var fought = Assert.Single(ambush.Battles, b => b.Setup.Source == BattleSource.Script);
        Assert.Equal(BattleOutcome.Spared, fought.Outcome);
        Assert.All(ambush.Battles.Where(b => b != fought), b => Assert.Equal((BattleSource.Wanderer, BattleOutcome.Won), (b.Setup.Source, b.Outcome)));
        var all = ambush.Transitions.Select(t => t.Transition).ToList();
        int IndexOf(Func<Transition, bool> what) => all.FindIndex(t => what(t));
        var cue = IndexOf(t => t is Transition.StartCutScene s && s.Scene.Runner.Script.Id == Ambush);
        var began = IndexOf(t => t is Transition.StartedBattle b && b.Battle.Setup == fought.Setup);
        var ended = all.FindIndex(began, t => t is Transition.EndBattle);
        var hut = IndexOf(t => t is Transition.StartCutScene s && s.Scene.Runner.Script.Id == Ids.Script(0x0392));
        Assert.True(cue < began && began < ended && ended < hut, string.Join(", ", all.Select(t => t.GetType().Name)));
        // Trek is built at the hut as the battle ends, its first tick walking into her cut-scene with no trek between.
        Assert.Equal([typeof(Transition.EndBattle), typeof(Transition.ResumeTrek), typeof(Transition.StartCutScene)],
            all[ended..hut].Append(all[hut]).Select(t => t.GetType()));
        Assert.IsType<Transition.ResumeTrek>(all[^1]);
        Assert.Equal(RecordId.Decimal(RecordTypes.MapSet, 6), host.Session.Trek.Chunks.MapSet.Id);
        Assert.True(host.PlayerHasControl);
        Assert.Empty(host.Warnings);
    }
}
