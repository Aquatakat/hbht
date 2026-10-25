using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>What A does on a script's page (<see cref="DialogueBox.Accept"/>, <see cref="DialogueBox.Advance"/>).</summary>
public class DialogueAcceptTests
{
    private static readonly RecordId Player = RecordId.Hex(RecordTypes.Entity, 0x023F);
    private static readonly RecordId Trahern = RecordId.Hex(RecordTypes.Entity, 0x0239);
    private static readonly RecordId Rumours = RecordId.Hex(RecordTypes.Dialogue, 0x0325);

    private sealed class World : IScriptWorld
    {
        public bool InParty(RecordId entity) => false;
        public bool HasItem(RecordId item) => false;
        public int SkillCheck(int skill, IRandom random) => 0;
        public int BestStat(int stat) => 0;
        public int WorstStat(int stat) => 0;
        public int MemberStat(RecordId? member, int stat) => 0;
        public int PartySize => 1;
        public string? PlayerName => "Alaron";
        public uint Gold { get; set; }
        public void AddItem(RecordId item) { }
        public bool TakeItem(RecordId item) => false;
        public bool AddMember(RecordId entity) => false;
        public void RemoveMember(RecordId entity) { }
        public bool AddExp(RecordId member, int amount) => false;
        public void CampHeal() { }
        public bool TryRecall(Memory memory, out int value)
        {
            value = 0;
            return false;
        }
        public void Remember(Memory memory, int value) { }
    }

    private sealed class Host : IScriptHost
    {
        public void Cue(ScriptCue cue) { }
    }

    private static DialogueNode Line(int index, string text, int? link = null, params DialogueOp[] ops) =>
        new(index, DialogueNodeKind.Line, text, ops.Length == 0 ? null : ops, link is { } l ? [l] : null);

    private static DialogueNode Menu(int index, string text, int[] links, params DialogueOp[] ops) =>
        new(index, DialogueNodeKind.Menu, text, ops.Length == 0 ? null : ops, links);

    private static DialogueOp Op(DialogueCommand command, int? value = null, RecordId? target = null) => new(command, value, target);

    private static readonly DialogueOp End = Op(DialogueCommand.EndDialogue);

    private static DialogueRecord Script(params DialogueNode[] nodes) =>
        new(RecordId.Hex(RecordTypes.Dialogue, 0x0400), 1234, DialogueMode.Conversation, nodes);

    /// <summary>A menu from Trahern whose choices are his too, so its line shows alone first, then a choice that goes on and one that ends.</summary>
    private static readonly DialogueRecord Alone = Script(
        Menu(0, "Well?", [1, 2], Op(DialogueCommand.SetSpeaker, target: Trahern)),
        Line(1, "Yes.", 3),
        Line(2, "No.", null, End),
        Line(3, "Then go.", null, End));

    /// <summary>A menu whose choices are Alaron's, so its line and choices show together.</summary>
    private static readonly DialogueRecord Together = Script(
        Menu(0, "Well?", [1, 2], Op(DialogueCommand.SetSpeaker, target: Trahern)),
        Line(1, "Yes.", 3, Op(DialogueCommand.SetSpeaker, target: Player)),
        Line(2, "No.", null, Op(DialogueCommand.SetSpeaker, target: Player), End),
        Line(3, "Then go.", null, End));

    private static readonly DialogueRecord Plain = Script(
        Line(0, "Hello.", 1),
        Line(1, "Goodbye.", null, End));

    private static readonly DialogueRecord Gossip = Script(
        Line(0, "", 1, Op(DialogueCommand.RandomLine, target: Rumours)),
        Line(1, "That's all I know.", null, End));

    private static readonly DialogueRecord RumourScript = new(Rumours, 1, DialogueMode.Conversation, [Line(0, "A rumour.", null, End)]);

    private static (ScriptRunner Runner, DialogueBox Box) Open(DialogueRecord script)
    {
        var flags = new FlagNetwork(Enumerable.Range(0, 10).Select(i => new FlagRecord(RecordId.Decimal(RecordTypes.Flag, i), Initial: false)),
            RecordId.Decimal(RecordTypes.Flag, 9));
        var scripts = new[] { script, RumourScript }.ToDictionary(s => s.Id);
        var runner = new ScriptRunner(script, flags, new World(), new Host(), new Lcg(), id => scripts.GetValueOrDefault(id));
        runner.Start();
        var box = new DialogueBox(new TextLayout(TextLayoutTests.Font()), 52, 52, Player);
        box.Show(runner.Page!);
        return (runner, box);
    }

    private static string Text(ScriptRunner runner) => runner.Page!.Line.Text;

    [Fact]
    public void AMovesOnFromALine()
    {
        var (runner, box) = Open(Plain);

        Assert.False(DialogueBox.PicksChoice(runner));
        Assert.Equal(DialogueAccept.Continued, box.Accept(runner));
        Assert.Equal("Goodbye.", Text(runner));
    }

    [Fact]
    public void ATakesTheChoiceTheCursorIsOn()
    {
        var (runner, box) = Open(Together);
        Assert.True(DialogueBox.PicksChoice(runner));
        Assert.False(box.ShowsLineBeforeChoices);

        box.MoveCursor(-1);
        Assert.Equal(DialogueAccept.Chose, box.Accept(runner));
        Assert.Equal("Then go.", Text(runner));

        (runner, box) = Open(Together);
        box.MoveCursor(1);
        Assert.Equal(DialogueAccept.Chose, box.Accept(runner));
        Assert.Equal(ScriptStatus.Ended, runner.Status);
    }

    [Fact]
    public void ABringsUpTheChoicesBehindALineThatShowedAloneFirst()
    {
        var (runner, box) = Open(Alone);
        Assert.True(box.ShowsLineBeforeChoices);

        Assert.Equal(DialogueAccept.ShowedChoices, box.Accept(runner));

        // The script hasn't moved, and the choices are up.
        Assert.Equal("Well?", Text(runner));
        Assert.False(box.ShowsLineBeforeChoices);
        Assert.Equal(2, box.Layout!.Choices.Count);

        Assert.Equal(DialogueAccept.Chose, box.Accept(runner));
        Assert.Equal("Then go.", Text(runner));
    }

    [Fact]
    public void APressWrapsWhateverMovesTheScript()
    {
        var pressed = 0;

        void Press(Action advance)
        {
            pressed++;
            advance();
        }

        var (runner, box) = Open(Alone);
        box.Accept(runner, Press);
        box.MoveCursor(1);
        box.Accept(runner, Press);
        Assert.Equal(2, pressed);
        Assert.Equal(ScriptStatus.Ended, runner.Status);

        (runner, box) = Open(Plain);
        box.Accept(runner, Press);
        Assert.Equal(3, pressed);
    }

    [Fact]
    public void AdvanceTakesTheChoiceItIsGivenOrMovesOn()
    {
        var (runner, _) = Open(Together);
        Assert.Equal(DialogueAccept.Chose, DialogueBox.Advance(runner, 1));
        Assert.Equal(ScriptStatus.Ended, runner.Status);

        (runner, _) = Open(Plain);
        Assert.Equal(DialogueAccept.Continued, DialogueBox.Advance(runner, 5));
        Assert.Equal("Goodbye.", Text(runner));
    }

    [Fact]
    public void ALineWaitingForAMovesOnWhateverTheCursorIsOn()
    {
        var (runner, box) = Open(Gossip);
        Assert.Equal(ScriptWait.Player, runner.Wait);

        Assert.False(DialogueBox.PicksChoice(runner));
        Assert.Equal(DialogueAccept.Continued, box.Accept(runner));
        Assert.Equal("That's all I know.", Text(runner));
    }
}
