using HBHT.Core;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// The template eleven cut-scenes share for a recruit who finds the party full: they say there's no room, then ask each member in
/// turn, and a member who would leave offers it in a menu: the first choice runs RemoveMember and goes back to the join node, the
/// second stays. Here Keelin's (<c>aidyn:dialogue/0x0382</c>), played through the runner over the 1.0 checkpoints' parties.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class FullPartyScriptTests(Na10Import fixture)
{
    private static readonly RecordId KeelinsScript = RecordId.Hex(RecordTypes.Dialogue, 0x0382);

    private const int WillYouJoinMe = 41, NoRoom = 46, KeelinJoins = 44, KeelinWalksOff = 134;

    // The menu nodes: Keelin's "You do remember me" and Sholeh's and Rheda's offers to leave.
    private const int Remember = 27, Sholeh = 76, Rheda = 86;

    private PluginSet Set => fixture.Data.Set;

    /// <summary>A 1.0 checkpoint as a game, found by its label.</summary>
    private GameState Game(string label)
    {
        var name = Directory.Exists(TestRoms.Folder)
            ? Directory.EnumerateFiles(TestRoms.Folder).Select(Path.GetFileName).OfType<string>()
                .FirstOrDefault(n => n.StartsWith("Aidyn Chronicles", StringComparison.Ordinal) && n.Contains(" - ") && n[^4..^1] == ".bs"
                    && CheckpointSave.Label(n) == label)
            : null;
        if (name is null)
            Assert.Skip($"No 1.0 checkpoint \"{label}\" in {TestRoms.Folder}.");
        var problems = new List<string>();
        var save = CheckpointSave.Convert(AresCheckpoint.Read(Path.Combine(TestRoms.Folder, name)), Set, label, problems);
        Assert.Empty(problems);
        var game = GameState.Load(Set, save);
        Assert.Empty(game.Problems);
        return game;
    }

    /// <summary>
    /// Plays the script from its first node, pressing on through lines and waits, and choosing in each menu the option
    /// <paramref name="choose"/> says, or the second (stay) if it doesn't say.
    /// </summary>
    private ScriptRunner Play(GameState game, Func<int, int?> choose, out List<int> menus)
    {
        var runner = game.RunScript(Set.Get<DialogueRecord>(KeelinsScript), new CueLog());
        var seen = new List<int>();
        runner.Start();
        for (var steps = 0; runner.Ending is null; steps++)
        {
            Assert.True(steps < 500, "The script doesn't end.");
            if (runner.Status == ScriptStatus.Waiting)
            {
                if (runner.Wait == ScriptWait.Timer)
                    runner.Step(100_000);
                else
                    runner.Resume();
            }
            else if (runner.Page is { Choices.Count: > 0 } page)
            {
                seen.Add(page.Line.Node);
                runner.Choose(choose(page.Line.Node) ?? 1);
            }
            else
                runner.Continue();
        }
        menus = seen;
        Assert.Null(runner.Ending!.Fault);
        return runner;
    }

    private static bool IsSet(GameState game, int flag) => game.Flags.Get(Flag(flag));

    [Fact]
    public void ScriptsStartAtTheNodeKeelinAsksWillYouJoinMeAndAFullPartyTakesTheNoRoomLine()
    {
        var script = Set.Get<DialogueRecord>(KeelinsScript);
        Assert.Equal(DialogueMode.Cutscene, script.Mode);
        Assert.Equal(("Will you join me?", DialogueNodeKind.Case), (script.Nodes[WillYouJoinMe].Text, script.Nodes[WillYouJoinMe].Kind));
        Assert.Equal(DialogueCommand.AddMember, script.Nodes[KeelinJoins].Ops!.Single().Command);
        Assert.StartsWith("I'm afraid there's no room", script.Nodes[NoRoom].Text);
        Assert.Equal(DialogueCommand.CheckPartySize, script.Nodes[WillYouJoinMe].Ops![0].Command);
    }

    [Fact]
    public void SholehLeavesWhenAskedAndKeelinTakesHerPlaceWhileBrennaStays()
    {
        var game = Game("Before Rooughah");
        var party = game.Party.Members.Select(m => m.Entity).ToList();
        Assert.Equal([GameState.Alaron, Entity(0x024C), Entity(0x0243), Entity(0x024D)], party);
        Assert.True(IsSet(game, 523));
        Assert.False(IsSet(game, 694));
        Assert.True(IsSet(game, 458));
        Assert.False(IsSet(game, 265));

        // Keelin asks for a plan and whether he'll join her: "What are your plans now?", then Sholeh offers to leave.
        var runner = Play(game, node => node == Sholeh ? 0 : null, out var menus);

        Assert.Equal([Remember, Sholeh], menus);
        Assert.True(runner.Visited(NoRoom));
        Assert.True(runner.Visited(78));
        Assert.True(runner.Visited(KeelinJoins));
        // The members before Sholeh in the list who aren't dismissable: Brenna says she'll not go, and stays.
        Assert.True(runner.Visited(48));
        Assert.True(runner.Visited(49));
        Assert.Equal(ScriptEndKind.Ended, runner.Ending!.Kind);
        // A cut-scene doesn't send a partner away.
        Assert.False(runner.Ending.PartnerJoined);

        Assert.False(IsSet(game, 523));
        Assert.True(IsSet(game, 694));
        Assert.True(IsSet(game, 265));
        Assert.False(IsSet(game, 688));
        Assert.Equal([GameState.Alaron, Entity(0x024C), Entity(0x0243), Entity(0x0249)], game.Party.Members.Select(m => m.Entity));
        Assert.True(IsSet(game, 92));
        // Sholeh isn't kept; the three the console kept are.
        Assert.Equal(3, game.Party.Kept.Count);
        Assert.DoesNotContain(Entity(0x024D), game.Party.Kept.Keys);
    }

    [Fact]
    public void WithSholehStayingRhedaWhoMayNowLeaveIsOfferedItAndKeelinTakesHerPlace()
    {
        var game = Game("Before Rooughah");

        var runner = Play(game, node => node == Rheda ? 0 : null, out var menus);

        // Sholeh is asked first and stays; the flag for beating Shamsuk (458) lets Rheda be asked.
        Assert.Equal([Remember, Sholeh, Rheda], menus);
        Assert.True(runner.Visited(88));
        Assert.True(runner.Visited(KeelinJoins));
        Assert.True(IsSet(game, 523));
        Assert.True(IsSet(game, 687));
        Assert.False(IsSet(game, 91));
        Assert.True(IsSet(game, 265));
        Assert.Equal([GameState.Alaron, Entity(0x0243), Entity(0x024D), Entity(0x0249)], game.Party.Members.Select(m => m.Entity));
    }

    [Fact]
    public void NoOneLeavesIfEveryoneAskedStaysAndKeelinWalksOffSighing()
    {
        var game = Game("Before Rooughah");
        var party = game.Party.Members.Select(m => m.Entity).ToList();

        var runner = Play(game, _ => null, out var menus);

        Assert.Equal([Remember, Sholeh, Rheda], menus);
        Assert.True(runner.Visited(KeelinWalksOff));
        Assert.False(runner.Visited(KeelinJoins));
        Assert.Equal(party, game.Party.Members.Select(m => m.Entity));
        Assert.False(IsSet(game, 265));
        Assert.True(IsSet(game, 523));
        Assert.True(IsSet(game, 91));
        Assert.True(IsSet(game, 92));
    }

    /// <summary>Before Shamsuk's Tower, Niesen is in the party and 458 is clear: Rheda and Niesen won't leave, so no one does.</summary>
    [Fact]
    public void RhedaWontLeaveBeforeShamsukIsBeatenAndNiesenNeverDoesSoNoRoomIsMadeForKeelin()
    {
        var game = Game("Before Shamsuk's Tower");
        Assert.Equal([GameState.Alaron, Entity(0x024C), Entity(0x0243), Entity(0x024A)], game.Party.Members.Select(m => m.Entity));
        Assert.False(IsSet(game, 458));

        var runner = Play(game, _ => null, out var menus);

        // Only the first menu is a choice: "What are your plans now?" is taken by the second option, and the rest aren't offered.
        Assert.Single(menus);
        Assert.True(runner.Visited(NoRoom));
        Assert.True(runner.Visited(53));
        Assert.True(runner.Visited(56));
        Assert.True(runner.Visited(KeelinWalksOff));
        Assert.False(runner.Visited(KeelinJoins));
        Assert.Equal([GameState.Alaron, Entity(0x024C), Entity(0x0243), Entity(0x024A)], game.Party.Members.Select(m => m.Entity));
        Assert.True(IsSet(game, 91));
        Assert.True(IsSet(game, 412));
        Assert.False(IsSet(game, 265));
    }

    [Fact]
    public void WithRoomKeelinJoinsAtOnceAndTheNoRoomLineIsSkipped()
    {
        var game = Game("Before Rooughah");
        game.RemoveMember(Entity(0x024D));
        Assert.Equal(3, game.Party.Count);

        var runner = Play(game, _ => null, out var menus);

        Assert.Equal([Remember], menus);
        Assert.False(runner.Visited(NoRoom));
        Assert.True(runner.Visited(42));
        Assert.True(runner.Visited(KeelinJoins));
        Assert.Equal(4, game.Party.Count);
        Assert.True(game.Party.Contains(Entity(0x0249)));
        Assert.True(IsSet(game, 265));
        Assert.False(IsSet(game, 688));
    }
}
