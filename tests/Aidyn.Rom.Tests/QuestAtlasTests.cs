using Aidyn.Rom.Db;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Tests;

public class QuestAtlasTests
{
    private static readonly Lazy<QuestAtlas> Na10 = new(() => QuestAtlas.Build(TestRoms.Get(RomVersionId.Na10)));

    /// <summary>Each journal entry has a derived flag that turns on with the entry's own flag.</summary>
    [Fact]
    public void JournalTriggersFollowTheirEntries()
    {
        var atlas = Na10.Value;

        Assert.Equal(atlas.Funnel.Flags.Count, EngineFlags.JournalTriggers + atlas.Journal.Count);
        for (var i = 0; i < atlas.Journal.Count; i++)
        {
            var trigger = EngineFlags.JournalTriggers + i;
            var d = atlas.Funnel.Flags[trigger];
            Assert.Equal((FlagKind.Count, FlagOp.Greater, (ushort)0), (d.Kind, d.Op, d.Operand));
            Assert.Equal([atlas.Journal[i].Flag], QuestAtlas.Inputs(atlas.Funnel, trigger));
        }
    }

    [Fact]
    public void ReferencesCoverTheData()
    {
        var atlas = Na10.Value;
        var counts = atlas.References.GroupBy(r => r.Role).ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(471, counts[FlagRole.DialogueEffect]);
        Assert.Equal(249, counts[FlagRole.DialogueCheck]);
        Assert.Equal(290, counts[FlagRole.ScriptDone]);
        Assert.Equal(1708, counts[FlagRole.NetworkInput]);
        Assert.Equal(258, counts[FlagRole.TriggerSets]);
        Assert.Equal(612 * 2, counts[FlagRole.PartyBeaten]);

        // 79 scripts aren't started by any map object or script. Code starts some, such as
        // the help menu (1355) and the death scenes; the rest may be unused.
        var unstarted = atlas.Scripts.Values.Where(s => s.StartedBy.Count == 0).Select(s => s.Borg).ToList();
        Assert.Equal(79, unstarted.Count);
        Assert.Contains(1355, unstarted);
    }

    /// <summary>
    /// <c>setEventFlag</c> only writes plain flags from 2 up, so these writes do nothing: a node's
    /// second effect, derived flags named as done or beaten flags, and two odd triggers.
    /// </summary>
    [Fact]
    public void IgnoredWritesArePinned()
    {
        var ignored = Na10.Value.References.Where(r => r.Ignored).Select(r => ((int)r.Flag, r.Role)).ToList();

        Assert.Equal(
        [
            (3691, FlagRole.DialogueEffect), (5250, FlagRole.ScriptBattleWon), (5264, FlagRole.PartyTalkedTo),
            (5495, FlagRole.TriggerSets), (5495, FlagRole.ObjectUsed), (5227, FlagRole.PartyBeaten),
            (5244, FlagRole.PartyTalkedTo), (0, FlagRole.TriggerSets), (5257, FlagRole.PartyBeaten),
            (5256, FlagRole.PartyBeaten), (5260, FlagRole.PartyBeaten), (5259, FlagRole.PartyBeaten),
        ], ignored);
    }

    /// <summary>A dialogue object's flag B is always its script's done flag, or 0. The game doesn't read it.</summary>
    [Fact]
    public void DialogueObjectsCopyTheirScriptsDoneFlag()
    {
        var atlas = Na10.Value;
        var dialogues = atlas.Objects.Where(o => o.Object.Data is DialogueData).ToList();

        Assert.Equal(364, dialogues.Count);
        Assert.Equal(91, dialogues.Count(o => o.Object.FlagB == 0));
        Assert.All(dialogues.Where(o => o.Object.FlagB != 0),
            o => Assert.Equal(atlas.Scripts[((DialogueData)o.Object.Data!).Script].Script.DoneFlag, o.Object.FlagB));
    }
}
