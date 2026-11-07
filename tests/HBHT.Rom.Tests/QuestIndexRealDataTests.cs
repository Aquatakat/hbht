using System.Globalization;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Dialogue;
using HBHT.Rom.Engine;
using HBHT.Rom.Maps;
using HBHT.Rom.Quests;

namespace HBHT.Rom.Tests;

/// <summary>What the quest index makes of NA 1.0's imported plugins, and facts of the ROM's data that it rests on.</summary>
public sealed class QuestIndexRealDataTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    /// <summary>
    /// Each journal entry is listed by its own flag, the one the ROM's journal table names, which the original's journal triggers
    /// (derived flags 5556 on, left out) counted. Entry 32's is derived, 5017; the rest are plain.
    /// </summary>
    [Fact]
    public void EachJournalEntryIsListedByItsOwnFlag()
    {
        var flags = Data.Set.All<FlagRecord>(RecordTypes.Flag).OrderBy(f => Number(f.Id)).ToList();
        var journal = Data.Set.All<JournalRecord>(RecordTypes.Journal).OrderBy(j => Number(j.Id)).ToList();
        var db = GameDb.Read(TestRoms.Get(RomVersionId.Na10));

        Assert.Equal(EngineFlags.JournalTriggers, flags.Count);
        for (var i = 0; i < journal.Count; i++)
        {
            var listing = Assert.Single(flags, f => f.Journal == journal[i].Id);
            Assert.Equal(db.Journal[i].Flag, Number(listing.Id));
            Assert.Equal(Number(listing.Id) == 5017, listing.Formula is not null);
            Assert.Contains(new FlagUse(listing.Id, journal[i].Id, FlagAccess.Read, FlagRole.JournalListed), Data.Index.UsesOf(listing.Id));
        }
    }

    [Fact]
    public void UsesCoverTheData()
    {
        var counts = Data.Set.All<FlagRecord>(RecordTypes.Flag)
            .SelectMany(f => Data.Index.UsesOf(f.Id)).GroupBy(u => u.Role).ToDictionary(g => g.Key, g => g.Count());

        // The flag script 1901 (archive 1019) sets at node 88 in a second effect, which 1.0 never runs, isn't written.
        Assert.Equal(719, counts[FlagRole.ScriptOp]);
        Assert.Equal(290, counts[FlagRole.ScriptDone]);
        Assert.Equal(2, counts[FlagRole.ScriptBattle]);
        // Without the 71 journal triggers' inputs.
        Assert.Equal(1637, counts[FlagRole.DerivedInput]);
        Assert.Equal(257, counts[FlagRole.TripwireSets]);
        Assert.Equal(22, counts[FlagRole.SecretFound]);
        Assert.Equal(1212, counts[FlagRole.PartyBeaten]);
        Assert.Equal(11, counts[FlagRole.BossFlag]);

        // 81 scripts aren't started by any map object or script, so may be unused. Two of them belong to
        // monster parties that are silent. Code alone starts the help menu (1355).
        var unstarted = Data.Set.All<ScriptRecord>(RecordTypes.Script).Where(s => Data.Index.StartersOf(s.Id).Count == 0).ToList();
        Assert.Equal(81, unstarted.Count);
        Assert.Contains(RecordId.Hex(RecordTypes.Script, EngineFlags.HelpScript), unstarted.Select(s => s.Id));
    }

    /// <summary>
    /// The 49 flags the checkpoints set that a first pass found no data setter for. Engine code sets all
    /// but eight, which nothing in the data or the retail code names (see docs/formats/quests.md).
    /// The index finds the eight, secret doors in Ehud's underground, and the flags' labels name the engine's.
    /// </summary>
    [Fact]
    public void CheckpointFlagsHaveSetters()
    {
        int[] checkpointOnly =
        [
            3, 154, 456, 458, 533, 566, 686, 689, 692, 697, .. Enumerable.Range(788, 18), 955, 956, 957, 958,
            960, 961, 962, 963, 3404, 3426, 3428, 3432, 3611, 3612, .. Enumerable.Range(3733, 7),
        ];
        var flags = Data.Set.All<FlagRecord>(RecordTypes.Flag).ToDictionary(f => Number(f.Id));

        Assert.Equal(49, checkpointOnly.Length);
        foreach (var flag in checkpointOnly)
        {
            var setters = Data.Index.UsesOf(Flag(flag)).Where(u => u.Access != FlagAccess.Read).ToList();
            Assert.True(setters.Count > 0 || flags[flag].Label is not null, $"Flag {flag} has no setter and no label");
        }
        foreach (var secret in new[] { 955, 956, 957, 958, 960, 961, 962, 963 })
        {
            Assert.Null(flags[secret].Label);
            Assert.Equal([FlagRole.SecretFound], Data.Index.UsesOf(Flag(secret)).Where(u => u.Access != FlagAccess.Read).Select(u => u.Role));
        }
    }

    /// <summary>
    /// Of the maps, only Map 2 (in a container) and Map 7 (in a Talewok shop) can be got. The Skull
    /// Key and the Map to the Goblin Lair have no source either.
    /// </summary>
    [Fact]
    public void KeyItemSourcesArePinned()
    {
        var unsourced = Data.Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.KeyItem is not null)
            .Where(i => !Data.Index.UsesOf(i.KeyItem!.Held).Any(u => u.Role == FlagRole.ItemGiven)).Select(i => i.Name).ToList();

        Assert.Equal(
        [
            "Map 1", "Map 4", "Map 5", "Map 6", "Map 8", "Map 9", "Map 10", "Map 11", "Map 12", "Map 13", "Map 14",
            "Map 15", "Map 16", "Map to Goblin Lair", "Map 17", "Map 18", "Skull Key",
        ], unsourced);
        Assert.Equal([RecordId.Decimal(RecordTypes.Shop, 17)],
            Data.Index.UsesOf(Flag(3411)).Where(u => u.Role == FlagRole.ItemGiven).Select(u => u.By));
    }

    /// <summary>Trigger objects set every flag the minimaps read.</summary>
    [Fact]
    public void MinimapFlagsHaveSetters()
    {
        var minimaps = Minimaps.Read(TestRoms.Get(RomVersionId.Na10));
        var flags = new[] { minimaps.Ehud, minimaps.RoogEntrance, minimaps.Barrows }.SelectMany(l => l).Select(m => (int)m.Flag).ToList();

        Assert.Equal([.. Enumerable.Range(4222, 16), .. Enumerable.Range(4286, 9)], flags.Order());
        Assert.All(flags, f => Assert.Contains(Data.Index.UsesOf(Flag(f)), u => u.Role == FlagRole.TripwireSets));
    }

    /// <summary>Winning a battle with a boss sets its flag (<c>Combat.SetKillFlags</c>): Kitarak's 154 and Pochanargat's 3204.</summary>
    [Fact]
    public void BossFlagsAreSetByTheirEntities()
    {
        var bosses = Data.Set.All<EntityRecord>(RecordTypes.Entity).Where(e => e.Combat?.BossFlag is not null).ToList();
        Assert.Equal(11, bosses.Count);
        foreach (var (name, flag) in new[] { ("Kitarak", 154), ("Pochanargat", 3204) })
        {
            var entity = bosses.Single(b => b.Combat!.BossFlag == Flag(flag));
            Assert.Equal(name, entity.Name);
            Assert.Equal([new FlagUse(Flag(flag), entity.Id, FlagAccess.Set, FlagRole.BossFlag)],
                Data.Index.UsesOf(Flag(flag)).Where(u => u.Role == FlagRole.BossFlag));
            Assert.Contains(new QuestEdge(entity.Id, Flag(flag), FlagRole.BossFlag, FlagAccess.Set), Data.Index.Upstream(Flag(flag)));
        }
    }

    /// <summary>A dialogue object's flag B is always its script's done flag, or 0. The game doesn't read it, so the import drops it.</summary>
    [Fact]
    public void DialogueObjectsCopyTheirScriptsDoneFlag()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var dialogues = Data.RomObjects.Values.SelectMany(o => o).Where(o => o.Data is DialogueData).ToList();

        Assert.Equal(364, dialogues.Count);
        Assert.Equal(91, dialogues.Count(o => o.FlagB == 0));
        Assert.All(dialogues.Where(o => o.FlagB != 0),
            o => Assert.Equal(Borg13Script.Parse(archive.Read(((DialogueData)o.Data!).Script)).DoneFlag, o.FlagB));
    }
}
