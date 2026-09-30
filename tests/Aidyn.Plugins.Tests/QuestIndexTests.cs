namespace Aidyn.Plugins.Tests;

public sealed class QuestIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "aidyn-quests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static RecordId Id(string text) => RecordId.Parse(text);

    private static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    /// <summary>
    /// Script 0x0100 checks flag 10 twice and sets and clears flag 20; it ends by setting 30, and
    /// winning its battle sets 31. Script 0x0101 sets flag 20 and checks 40. Flag 50 is derived
    /// from 10 and 20 and adds journal entry 2, which lists flag 60. Map 0x0200 has objects gated
    /// on flag 10, on flag 20 inverted, and on flags 0 and 1. The engine writes 70 and 71, and
    /// reads 72.
    /// </summary>
    private QuestIndex Index()
    {
        var dir = Path.Combine(_root, "aidyn");
        Directory.CreateDirectory(Path.Combine(dir, "records"));
        File.WriteAllText(Path.Combine(dir, "plugin.json"), """{ "id": "aidyn", "name": "aidyn", "version": "1.0.0" }""");
        File.WriteAllText(Path.Combine(dir, "records", "quests.json"), """
            [
              {
                "id": "aidyn:dialogue/0x0100", "number": 1, "mode": "Cutscene",
                "doneFlag": "aidyn:flag/30", "battleFlag": "aidyn:flag/31",
                "nodes": [
                  { "index": 0, "kind": "If", "text": "The Lighthouse is dark.",
                    "ops": [ { "command": "CheckFlag", "target": "aidyn:flag/10" }, { "command": "SetSpeaker", "target": "aidyn:entity/0x0001" } ] },
                  { "index": 1, "kind": "Line", "text": "Take the key.",
                    "ops": [ { "command": "CheckFlag", "target": "aidyn:flag/10" }, { "command": "SetFlag", "target": "aidyn:flag/20" }, { "command": "ClearFlag", "target": "aidyn:flag/20" } ] },
                  { "index": 2, "kind": "Line", "text": "" },
                ],
              },
              {
                "id": "aidyn:dialogue/0x0101", "number": 2, "mode": "Conversation",
                "nodes": [
                  { "index": 0, "kind": "Line", "text": "the LIGHTHOUSE keeper",
                    "ops": [ { "command": "SetFlag", "target": "aidyn:flag/20" }, { "command": "CheckFlag", "target": "aidyn:flag/40" }, { "command": "AddGold", "value": 5 } ] },
                ],
              },
              { "id": "aidyn:flag/10" },
              { "id": "aidyn:flag/20" },
              { "id": "aidyn:flag/50", "journal": "aidyn:journal/2",
                "formula": { "kind": "Logic", "op": "And", "operand": 0, "inputs": [ "aidyn:flag/10", "aidyn:flag/20" ] } },
              { "id": "aidyn:journal/2", "flag": "aidyn:flag/60", "text": "Find the key." },
              {
                "id": "aidyn:map/0x0200",
                "objects": [
                  { "type": "chest", "index": 0, "position": [0, 0, 0], "radius": 1, "pak": "Any", "visible": { "flag": "aidyn:flag/10" } },
                  { "type": "chest", "index": 1, "position": [0, 0, 0], "radius": 1, "pak": "Any", "visible": { "flag": "aidyn:flag/20", "invert": true } },
                  { "type": "chest", "index": 2, "position": [0, 0, 0], "radius": 1, "pak": "Any", "visible": { "flag": "aidyn:flag/0" } },
                  { "type": "chest", "index": 3, "position": [0, 0, 0], "radius": 1, "pak": "Any", "visible": { "flag": "aidyn:flag/1", "invert": true } },
                ],
              },
              {
                "id": "aidyn:stateflags/0",
                "moonPhase": [ "aidyn:flag/70", "aidyn:flag/71" ], "timeOfDay": [], "dayOfSeason": [], "season": [],
                "dayOfWeek": [], "week": [], "rain": [], "terrain": [],
                "newJournalEntry": "aidyn:flag/73", "randomSkillChecks": "aidyn:flag/72",
              },
            ]
            """);
        return QuestIndex.Build(PluginSet.Load([dir]));
    }

    private static readonly RecordId ScriptA = Id("aidyn:dialogue/0x0100");
    private static readonly RecordId ScriptB = Id("aidyn:dialogue/0x0101");
    private static readonly RecordId State = Id("aidyn:stateflags/0");

    [Fact]
    public void ScriptOpsAreUsesAtTheirNodes()
    {
        var index = Index();

        Assert.Equal(
            [
                new FlagUse(Flag(10), ScriptA, FlagAccess.Read, FlagRole.ScriptOp, 0),
                new FlagUse(Flag(10), ScriptA, FlagAccess.Read, FlagRole.ScriptOp, 1),
                new FlagUse(Flag(10), Id("aidyn:flag/50"), FlagAccess.Read, FlagRole.DerivedInput),
                new FlagUse(Flag(10), Id("aidyn:voxel/0x0200/0"), FlagAccess.Read, FlagRole.ObjectVisible),
            ],
            index.UsesOf(Flag(10)));
        Assert.Equal(
            [
                new FlagUse(Flag(20), ScriptA, FlagAccess.Set, FlagRole.ScriptOp, 1),
                new FlagUse(Flag(20), ScriptA, FlagAccess.Clear, FlagRole.ScriptOp, 1),
                new FlagUse(Flag(20), ScriptB, FlagAccess.Set, FlagRole.ScriptOp, 0),
                new FlagUse(Flag(20), Id("aidyn:flag/50"), FlagAccess.Read, FlagRole.DerivedInput),
                new FlagUse(Flag(20), Id("aidyn:voxel/0x0200/1"), FlagAccess.Read, FlagRole.ObjectVisible, Inverted: true),
            ],
            index.UsesOf(Flag(20)));
    }

    [Fact]
    public void DoneAndBattleFlagsAreSetByTheirScript()
    {
        var index = Index();

        Assert.Equal([new FlagUse(Flag(30), ScriptA, FlagAccess.Set, FlagRole.ScriptDone)], index.UsesOf(Flag(30)));
        Assert.Equal([new FlagUse(Flag(31), ScriptA, FlagAccess.Set, FlagRole.ScriptBattle)], index.UsesOf(Flag(31)));
    }

    [Fact]
    public void JournalEntriesReadTheFlagThatAddsThemAndTheFlagThatListsThem()
    {
        var index = Index();
        var entry = Id("aidyn:journal/2");

        Assert.Equal([new FlagUse(Flag(50), entry, FlagAccess.Read, FlagRole.JournalAdds)], index.UsesOf(Flag(50)));
        Assert.Equal([new FlagUse(Flag(60), entry, FlagAccess.Read, FlagRole.JournalListed)], index.UsesOf(Flag(60)));
        Assert.Equal(
            [new FlagUse(Flag(50), entry, FlagAccess.Read, FlagRole.JournalAdds), new FlagUse(Flag(60), entry, FlagAccess.Read, FlagRole.JournalListed)],
            index.UsesBy(entry));
    }

    [Fact]
    public void TheEngineSetsItsStateFlagsAndReadsItsOption()
    {
        var index = Index();

        Assert.Equal(
            [
                new FlagUse(Flag(70), State, FlagAccess.Set, FlagRole.Engine),
                new FlagUse(Flag(71), State, FlagAccess.Set, FlagRole.Engine),
                new FlagUse(Flag(72), State, FlagAccess.Read, FlagRole.Engine),
                new FlagUse(Flag(73), State, FlagAccess.Set, FlagRole.Engine),
            ],
            index.UsesBy(State));
        Assert.Equal([new QuestEdge(State, Flag(70), FlagRole.Engine, FlagAccess.Set)], index.Upstream(Flag(70)));
        Assert.Equal([new QuestEdge(Flag(72), State, FlagRole.Engine, FlagAccess.Read)], index.Downstream(Flag(72)));
    }

    [Fact]
    public void GatesOnTheConstantFlagsAreLeftOut()
    {
        var index = Index();

        Assert.Empty(index.UsesOf(Flag(0)));
        Assert.Empty(index.UsesOf(Flag(1)));
        Assert.Empty(index.UsesBy(Id("aidyn:voxel/0x0200/2")));
        Assert.Empty(index.UsesBy(Id("aidyn:voxel/0x0200/3")));
    }

    [Fact]
    public void UsesByListsAScriptsFlagsInNodeOrder()
    {
        var index = Index();

        Assert.Equal(
            [
                new FlagUse(Flag(10), ScriptA, FlagAccess.Read, FlagRole.ScriptOp, 0),
                new FlagUse(Flag(10), ScriptA, FlagAccess.Read, FlagRole.ScriptOp, 1),
                new FlagUse(Flag(20), ScriptA, FlagAccess.Set, FlagRole.ScriptOp, 1),
                new FlagUse(Flag(20), ScriptA, FlagAccess.Clear, FlagRole.ScriptOp, 1),
                new FlagUse(Flag(30), ScriptA, FlagAccess.Set, FlagRole.ScriptDone),
                new FlagUse(Flag(31), ScriptA, FlagAccess.Set, FlagRole.ScriptBattle),
            ],
            index.UsesBy(ScriptA));
        Assert.Empty(index.UsesBy(Id("aidyn:dialogue/0x0999")));
    }

    [Fact]
    public void EdgesPointTheWayThingsHappen()
    {
        var index = Index();

        // A flag's setters come before it, and its derived flag's inputs.
        Assert.Equal(
            [
                new QuestEdge(ScriptA, Flag(20), FlagRole.ScriptOp, FlagAccess.Set),
                new QuestEdge(ScriptA, Flag(20), FlagRole.ScriptOp, FlagAccess.Clear),
                new QuestEdge(ScriptB, Flag(20), FlagRole.ScriptOp, FlagAccess.Set),
            ],
            index.Upstream(Flag(20)));
        Assert.Equal(
            [
                new QuestEdge(Flag(10), Flag(50), FlagRole.DerivedInput, FlagAccess.Read),
                new QuestEdge(Flag(20), Flag(50), FlagRole.DerivedInput, FlagAccess.Read),
            ],
            index.Upstream(Flag(50)));

        // Its readers come after it.
        Assert.Equal(
            [
                new QuestEdge(Flag(20), Flag(50), FlagRole.DerivedInput, FlagAccess.Read),
                new QuestEdge(Flag(20), Id("aidyn:voxel/0x0200/1"), FlagRole.ObjectVisible, FlagAccess.Read),
            ],
            index.Downstream(Flag(20)));
        Assert.Equal([new QuestEdge(Flag(50), Id("aidyn:journal/2"), FlagRole.JournalAdds, FlagAccess.Read)], index.Downstream(Flag(50)));
        Assert.Equal([new QuestEdge(Flag(60), Id("aidyn:journal/2"), FlagRole.JournalListed, FlagAccess.Read)], index.Downstream(Flag(60)));

        // A script is downstream of the flags it checks and upstream of the flags it changes.
        Assert.Equal([new QuestEdge(Flag(10), ScriptA, FlagRole.ScriptOp, FlagAccess.Read)], index.Upstream(ScriptA));
        Assert.Equal(
            [
                new QuestEdge(ScriptA, Flag(20), FlagRole.ScriptOp, FlagAccess.Set),
                new QuestEdge(ScriptA, Flag(20), FlagRole.ScriptOp, FlagAccess.Clear),
                new QuestEdge(ScriptA, Flag(30), FlagRole.ScriptDone, FlagAccess.Set),
                new QuestEdge(ScriptA, Flag(31), FlagRole.ScriptBattle, FlagAccess.Set),
            ],
            index.Downstream(ScriptA));
        Assert.Equal([new QuestEdge(Flag(10), Id("aidyn:voxel/0x0200/0"), FlagRole.ObjectVisible, FlagAccess.Read)], index.Upstream(Id("aidyn:voxel/0x0200/0")));
    }

    [Fact]
    public void RepeatedUsesCollapseToOneEdgeButDifferentAccessesDoNot()
    {
        var index = Index();

        // Script A checks flag 10 in two nodes.
        Assert.Equal(2, index.UsesBy(ScriptA).Count(u => u.Flag == Flag(10)));
        Assert.Single(index.Upstream(ScriptA));
        // It both sets and clears flag 20.
        Assert.Equal(2, index.Downstream(ScriptA).Count(e => e.To == Flag(20)));
        Assert.Empty(index.Upstream(Flag(0)));
        Assert.Empty(index.Downstream(Id("aidyn:flag/999")));
    }

    [Fact]
    public void FindTextIgnoresCaseAndKeepsScriptThenNodeOrder()
    {
        var index = Index();

        Assert.Equal([(ScriptA, 0), (ScriptB, 0)], index.FindText("lighthouse"));
        Assert.Equal([(ScriptA, 1)], index.FindText("KEY"));
        Assert.Empty(index.FindText("dragon"));
        Assert.Empty(index.FindText(""));
    }
}
