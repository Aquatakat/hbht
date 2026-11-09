using System.Numerics;
using HBHT.Core;
using HBHT.Core.Combat;
using HBHT.Core.Playthrough;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's headless host (docs/systems/playthrough.md, layer 3) on NA 1.0's data: a new game played with nobody at the
/// controls, a cut-scene's choice taken and recorded, and a battle won outright through the game's own victory.
/// </summary>
public sealed class PlaythroughHostTests(Na10Import fixture, ITestOutputHelper output)
{
    private Na10Import.Loaded Data => fixture.Data;

    private static RecordId Script(int n) => RecordId.Hex(RecordTypes.Script, n);

    private void Report(PlaythroughHost host)
    {
        output.WriteLine(host.Clock.ToString());
        foreach (var t in host.Transitions)
            output.WriteLine(t.ToString());
        foreach (var n in host.Notes)
            output.WriteLine(n.ToString());
        foreach (var b in host.BattlesFought)
            output.WriteLine(b.ToString());
    }

    [Fact]
    public void ANewGameWithNobodyAtTheControlsPlaysTheOpeningAndItsCutSceneThenStandsInTrek()
    {
        var host = PlaythroughHost.NewGame(Data.Set);
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        Report(host);

        // The opening cinematic, skipped as B skips it, then script 866 from its object in Overworld I08 at once, its 13 pages pressed on,
        // and trek where the new game arrived, the player's to play. No choice, no battle, nothing wrong.
        var opening = Data.Set.Get<CinematicRecord>(RecordId.Decimal(RecordTypes.Cinematic, 0));
        Assert.Equal([typeof(Transition.PlayCinematic), typeof(Transition.EndCinematic), typeof(Transition.StartCutScene),
            typeof(Transition.EndCutScene), typeof(Transition.ResumeTrek)], host.Transitions.Select(t => t.Transition.GetType()));
        Assert.Equal(new Transition.PlayCinematic(opening), host.Transitions[0].Transition);
        Assert.Equal(Script(0x0362), ((Transition.StartCutScene)host.Transitions[2].Transition).Scene.Runner.Script.Id);
        Assert.True(host.Game.Flags.IsSet(opening.DoneFlag));
        Assert.Equal(13, host.Pages.Count);
        Assert.All(host.Pages, p => Assert.Equal((PageSource.CutScene, Script(0x0362), (int?)null), (p.Source, p.Script, p.Choice)));
        Assert.Empty(host.BattlesFought);
        Assert.Empty(host.Notes);
        Assert.Equal((RecordId.Parse("aidyn:chunk/0/I08"), GameMode.Trek), (host.Session.Where().Place!.Value, host.Session.Mode));
        Assert.InRange(host.Clock.Ticks, 900, 1100);

        // Ten minutes more with nobody at the controls: nothing comes to him.
        var transitions = host.Transitions.Count;
        Assert.Equal(HostRun.OutOfTicks, host.RunUntil(_ => false, 36_000));
        Assert.Equal(transitions, host.Transitions.Count);
        Assert.True(host.PlayerHasControl);
        Assert.Empty(host.Notes);
        output.WriteLine(host.Clock.ToString());
    }

    [Fact]
    public void WithCinematicsWatchedTheOpeningPlaysToItsEndAndTheSameFollows()
    {
        var host = PlaythroughHost.NewGame(Data.Set, PlaythroughOptions.Default with { SkipCinematics = false });
        Assert.Equal(HostRun.Done, host.RunUntilControl(30_000));
        var ended = host.Transitions.Single(t => t.Transition is Transition.EndCinematic);
        output.WriteLine($"The opening ended on tick {ended.Tick}.");
        Assert.True(ended.Tick > 600);
        Assert.Equal([typeof(Transition.PlayCinematic), typeof(Transition.EndCinematic), typeof(Transition.StartCutScene),
            typeof(Transition.EndCutScene), typeof(Transition.ResumeTrek)], host.Transitions.Select(t => t.Transition.GetType()));
        Assert.Equal(13, host.Pages.Count);
    }

    /// <summary>Takes a choice by its place among those shown.</summary>
    private sealed class Take(int choice) : IConversationPolicy
    {
        public int ReadTicks => 0;

        public int Choose(ChoiceContext page) => choice;
    }

    /// <summary>A new game played to trek, then script 0x039A cued from its object in I08, as walking into it would, and played out.</summary>
    private PlaythroughHost PlayChoiceScene(IConversationPolicy? policy = null)
    {
        var host = PlaythroughHost.NewGame(Data.Set);
        Assert.Equal(HostRun.Done, host.RunUntilControl(10_000));
        if (policy is not null)
            host.Conversations = policy;
        var id = Script(0x039A);
        var (voxel, _, launcher) = Data.Of<ScriptObject>().Single(d => d.Object.Plays == id && d.Map.Name == "I08");
        host.Session.Cue(Data.Set.Get<ScriptRecord>(id), new Core.Scripts.ScriptStart(launcher.Marks, voxel), launcher.Stage!.Value);
        var from = host.Clock.Ticks;
        Assert.Equal(HostRun.Done, host.RunUntil(h => h.Clock.Ticks > from + 1 && h.PlayerHasControl, 20_000));
        Report(host);
        Assert.Empty(host.Warnings);
        return host;
    }

    [Fact]
    public void ACutScenesChoiceIsTakenAsTheDefaultSaysAndRecorded()
    {
        // Its page at node 12 offers two; the default takes the first, node 13.
        var host = PlayChoiceScene();
        var choice = Assert.Single(host.Choices);
        Assert.Equal((PageSource.CutScene, Script(0x039A), 12, 0, 2, 13), (choice.Source, choice.Script, choice.Node, choice.Choice!.Value, choice.Of,
            choice.ChoiceNode!.Value));
        Assert.Equal(Data.Set.Get<ScriptRecord>(Script(0x039A)).Nodes.Single(n => n.Index == 13).Text, choice.ChoiceText);
    }

    [Fact]
    public void AnotherChoiceIsTakenByMovingTheCursorAsThePlayerDoes()
    {
        // The second at node 12 leads to another page of three, where the second is taken too, and back to node 12, which shows only the
        // first now, as a conversation hides the choices taken: the policy's second is taken as the last there is.
        var host = PlayChoiceScene(new Take(1));
        Assert.Equal([(12, 1, 2), (47, 1, 3), (12, 0, 1)], host.Choices.Select(c => (c.Node, c.Choice!.Value, c.Of)));
        var first = host.Choices.First();
        var links = Data.Set.Get<ScriptRecord>(Script(0x039A)).Nodes.Single(n => n.Index == 12).Links!;
        Assert.Equal(links[1], first.ChoiceNode);
        Assert.Equal(links[0], host.Choices.Last().ChoiceNode);
    }

    /// <summary>A every ten ticks until a conversation has turned a page, then nothing: talking to whoever the player faces.</summary>
    private sealed class TalkOnce : IInputPolicy
    {
        public TickInput Next(PlaythroughHost host) =>
            host.Pages.Count == 0 && host.Clock.Ticks % 10 == 0 ? new TickInput(default, [MenuAction.Accept]) : TickInput.None;
    }

    [Fact]
    public void ABattleWonOutrightSetsTheNpcsBattleFlagAsTheGamesOwnWinDoes()
    {
        // The npc in Overworld B06 (object 5) who fights when talked to: winning its battle sets its battle flag, 3758 (the encounter
        // flag Combat.StateVictory sets). The player stands before it, facing it, and presses A.
        var overworld = RecordId.Decimal(RecordTypes.MapSet, 0);
        var (id, map, npc) = Data.Of<NpcObject>().Single(n => n.Map.MapSet == overworld && n.Map.Name == "B06" && n.Object.Index == 5);
        var flag = npc.BattleFlag!.Value;
        Assert.Equal(Flag(3758), flag);
        var at = npc.Position + new Vector3(0, 2, 1.2f);
        var start = new GameStart(Data.Set.Get<MapSetRecord>(map.MapSet), map.Name, at, new Vector2(0, -1), SetDown: true);
        var host = PlaythroughHost.From(GameState.NewGame(Data.Set), start: start, opening: false);
        host.Input = new TalkOnce();
        var gold = host.Game.Party.Gold;
        var exp = host.Game.Party.Slots[0]!.TotalExp;
        Assert.False(host.Game.Flags.IsSet(flag));

        Assert.Equal(HostRun.Done, host.RunUntil(h => h.BattlesFought.Count > 0 && h.PlayerHasControl, 5000));
        Report(host);
        var fought = Assert.Single(host.BattlesFought);
        Assert.Equal((id, BattleOutcome.Won, true), (fought.Setup.Party!.Value, fought.Outcome, fought.WonOutright));
        Assert.Empty(fought.Deaths);
        Assert.True(host.Game.Flags.IsSet(flag));

        // Through the game's own end: the battle, the post-battle windows, whose loot was taken, and trek where it began.
        Assert.Equal([typeof(Transition.StartedBattle), typeof(Transition.EndBattle), typeof(Transition.PostBattle), typeof(Transition.ResumeTrek)],
            host.Transitions.Select(t => t.Transition.GetType()));
        var spoils = ((Transition.PostBattle)host.Transitions[2].Transition).Spoils;
        Assert.Equal(gold + (uint)spoils.Gold, host.Game.Party.Gold);
        Assert.True(host.Game.Party.Slots[0]!.TotalExp > exp);
        Assert.Empty(spoils.Loot.Stacks);
        Assert.Empty(host.Warnings);
        Assert.Equal(map.Id, host.Session.Where().Place);
    }
}
