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

        // 80 scripts aren't started by any map object, script or engine code, so may be unused. Two
        // of them belong to monster parties that are silent. Code alone starts the help menu (1355).
        var unstarted = atlas.Scripts.Values.Where(s => s.StartedBy.Count == 0).Select(s => s.Borg).ToList();
        Assert.Equal(80, unstarted.Count);
        Assert.DoesNotContain(EngineFlags.HelpScript, unstarted);
    }

    /// <summary>
    /// <c>setEventFlag</c> only writes plain flags from 2 up, so these writes do nothing: a node's
    /// second effect, derived flags named as done, beaten or fought flags, and two odd triggers.
    /// </summary>
    [Fact]
    public void IgnoredWritesArePinned()
    {
        var ignored = Na10.Value.References.Where(r => r.Ignored).Select(r => ((int)r.Flag, r.Role)).ToList();

        Assert.Equal(
        [
            (3691, FlagRole.DialogueEffect), (5250, FlagRole.ScriptBattle), (5264, FlagRole.PartyTalkedTo),
            (5264, FlagRole.PartyFought), (5495, FlagRole.TriggerSets), (5495, FlagRole.ObjectUsed),
            (5227, FlagRole.PartyBeaten), (0, FlagRole.TriggerSets),
            (5257, FlagRole.PartyBeaten), (5256, FlagRole.PartyBeaten), (5260, FlagRole.PartyBeaten),
            (5259, FlagRole.PartyBeaten),
        ], ignored);
    }

    /// <summary>
    /// The 49 flags the checkpoints set that 1E found no data setter for. Engine code sets all but
    /// eight, which nothing in the data or the retail code names (see docs/formats/quests.md).
    /// </summary>
    [Fact]
    public void CheckpointFlagsHaveSetters()
    {
        var atlas = Na10.Value;
        var set = atlas.References.Where(r => r.Access != FlagAccess.Read && !r.Ignored).Select(r => (int)r.Flag).ToHashSet();
        int[] checkpointOnly =
        [
            3, 154, 456, 458, 533, 566, 686, 689, 692, 697, .. Enumerable.Range(788, 18), 955, 956, 957, 958,
            960, 961, 962, 963, 3404, 3426, 3428, 3432, 3611, 3612, .. Enumerable.Range(3733, 7),
        ];

        Assert.Equal(49, checkpointOnly.Length);
        // The last eight, 955-958 and 960-963, are secret doors in Ehud's underground.
        Assert.All(checkpointOnly, f => Assert.Contains(f, set));
    }

    /// <summary>
    /// Of the maps, only Map 2 (in a container) and Map 7 (in a Talewok shop) can be got. The Skull
    /// Key and the Map to the Goblin Lair have no source either.
    /// </summary>
    [Fact]
    public void KeyItemSourcesArePinned()
    {
        var atlas = Na10.Value;
        var rom = TestRoms.Get(RomVersionId.Na10);
        var given = atlas.References.Where(r => r.Role == FlagRole.ItemGiven && !r.Ignored).ToLookup(r => r.Flag);
        var unsourced = EngineTables.Read(rom).KeyItems.Where(k => !given[k.Held].Any()).Select(k => atlas.Names[k.Item]).ToList();

        Assert.Equal(
        [
            "Map 1", "Map 4", "Map 5", "Map 6", "Map 8", "Map 9", "Map 10", "Map 11", "Map 12", "Map 13", "Map 14",
            "Map 15", "Map 16", "Map to Goblin Lair", "Map 17", "Map 18", "Skull Key",
        ], unsourced);
        Assert.Equal([new ShopSource(17, GameDb.Read(rom).Shops[17].Shopkeeper)], given[3411].Select(r => r.Source));
    }

    /// <summary>Every boss flag has a battle in the data that can set it.</summary>
    [Fact]
    public void BossesHaveBattles()
    {
        var beaten = Na10.Value.References.Where(r => r.Role == FlagRole.BossBeaten).Select(r => r.Flag).ToHashSet();

        Assert.All(EngineFlags.BossFlags.Values, f => Assert.Contains(f, beaten));
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
