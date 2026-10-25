using HBHT.Core;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Magic;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// Rheda's Endurance (level 4) on herself and Alaron, in the user's Combat checkpoints 07 (the targets chosen), 08 (Rheda's
/// effect on) and 09 to 12 (Alaron's too, and the rounds running down): the battle is set up as <see cref="BattleCheckpointTests"/>
/// does, from checkpoint 01, with the cast's inputs checked against 07's RAM, then <see cref="Battle.Cast"/> must leave the
/// effect records, the stats, the Stamina, the reagent and the random stream as 08 and 09 hold them. The tests skip without the ROM
/// and the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MagicCheckpointTests(Na10Import fixture)
{
    private const string Before = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Combat 01, before.bs1";

    private const uint RandomState = 0x800D8088;

    /// <summary>The sheets (<c>Entity</c>) of Alaron and Rheda, which stay put from 07 to 12.</summary>
    private const uint AlaronSheet = 0x8035DEA8, RhedaSheet = 0x8035F2F8;

    private static readonly RecordId Endurance = RecordId.Hex(RecordTypes.Spell, 0x0310);

    private PluginSet Set => fixture.Data.Set;

    private static AresCheckpoint Combat(int number)
    {
        var tag = $" - Combat {number:00},";
        var name = Directory.Exists(TestRoms.Folder)
            ? Directory.EnumerateFiles(TestRoms.Folder).Select(Path.GetFileName).OfType<string>()
                .FirstOrDefault(n => n.Contains("(V1.0)", StringComparison.Ordinal) && n.Contains(tag, StringComparison.Ordinal) && n.EndsWith(".bs1", StringComparison.Ordinal))
            : null;
        if (name is null)
            Assert.Skip($"No Combat {number:00} checkpoint in {TestRoms.Folder}.");
        return TestRoms.Checkpoint(name);
    }

    /// <summary>An effect record as the original keeps it (<c>EffectRecord</c>, 0x18 bytes).</summary>
    private sealed record Record(int Kind, int Power, int School, int Margin, uint Harmful, uint Timer)
    {
        public static Record Read(AresCheckpoint c, uint at) =>
            new(c.ReadByte(at), c.ReadByte(at + 1), c.ReadByte(at + 2), c.ReadByte(at + 3), c.ReadWord(at + 4), c.ReadWord(at + 8));
    }

    /// <summary>The records of an entity's effect array (<c>entity+0x34</c>: 15 pointers), by slot, null for an empty one.</summary>
    private static Record?[] Records(AresCheckpoint c, uint sheet)
    {
        var array = c.ReadWord(sheet + 0x34);
        return [.. Enumerable.Range(0, 15).Select(i => c.ReadWord(array + (uint)i * 4) is var p and >= 0x80000000 ? Record.Read(c, p) : null)];
    }

    /// <summary>The checkpoint's party member with this sheet, checked to be who the address is said to be.</summary>
    private static CheckpointCharacter Member(AresCheckpoint c, uint sheet, string name)
    {
        var id = (ushort)(c.ReadByte(sheet) << 8 | c.ReadByte(sheet + 1));
        var member = c.Party.Single(m => m.Id == id);
        Assert.Equal(name, ReferenceEquals(c.Party[0], member) ? "Alaron" : "Rheda");
        return member;
    }

    private sealed record Built(Battle Battle, GameState Game, Combatant Alaron, Combatant Rheda, AresCheckpoint Start, uint Seed);

    /// <summary>Checkpoint 01's battle, with the clock and the combat stream as checkpoint 07 holds them.</summary>
    private Built Build()
    {
        var before = TestRoms.Checkpoint(Before);
        var start = Combat(7);
        var game = GameState.Load(Set, CheckpointSave.Convert(before, Set, "Combat 01, before"));
        Assert.Empty(game.Problems);
        var arena = Arena.For(Set, 3)!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), Set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [.. Enumerable.Repeat(new PartyEnemy(RecordId.Hex(RecordTypes.Entity, 0x026E), 1, 1), 10)],
            Arena: 3, Ambush: true, CanFlee: true, EncounterFlag: null, CollisionByte: 0, AniByte: 2);
        game.Random.Combat.Seed = before.ReadWord(RandomState);
        var battle = Battle.Begin(game, setup, arena, streamer.World);

        // The state the cast reads is checkpoint 07's: its clock, its part of the day (World+2), its combat stream.
        game.Clock.Time = start.Time;
        if (game.Weather is { } weather)
            weather.PartOfDay = (PartOfDay)start.Weather.PartOfDay;
        var seed = start.ReadWord(RandomState);
        game.Random.Combat.Seed = seed;
        return new Built(battle, game, battle.Slots[0]!, battle.Slots[1]!, start, seed);
    }

    [Fact]
    public void TheCastReadsTheCheckpointsSheetsDayAndReagent()
    {
        var built = Build();
        var c = built.Start;
        Assert.Equal("Alaron", built.Alaron.Sheet.Name);
        Assert.Equal("Rheda", built.Rheda.Sheet.Name);

        // Rheda's sheet as 07 holds it, and the facts the chance and the cost are made of.
        var rheda = Member(c, RhedaSheet, "Rheda");
        var alaron = Member(c, AlaronSheet, "Alaron");
        foreach (var (port, original) in new[] { (built.Rheda.Sheet, rheda), (built.Alaron.Sheet, alaron) })
        {
            foreach (var stat in Enum.GetValues<Stat>())
            {
                var i = (int)stat;
                Assert.True(original.Trained[i] == port.Stats.Trained(stat) && original.Current[i] == port.Stats.Current(stat)
                    && original.Base[i] == port.Stats.RawBase(stat) && original.Modified[i] == port.Stats.RawModified(stat),
                    $"{port.Name} {stat}: original {original.Trained[i]}/{original.Current[i]}/{original.Base[i]}/{original.Modified[i]}, "
                    + $"port {port.Stats.Trained(stat)}/{port.Stats.Current(stat)}/{port.Stats.RawBase(stat)}/{port.Stats.RawModified(stat)}");
            }
            Assert.Equal(original.SkillModified[(int)Skill.Wizard], port.Skills.Modified(Skill.Wizard));
        }
        var sheet = built.Rheda.Sheet;
        Assert.Equal(22, sheet.Stats.Modified(Stat.Intelligence));
        Assert.Equal(4, sheet.Skills.Modified(Skill.Wizard));
        Assert.Equal(19, sheet.Stats.Base(Stat.Level));
        Assert.Equal(68, sheet.Stats.Current(Stat.Stamina));
        Assert.Equal((16, 18), (sheet.Stats.Current(Stat.Endurance), built.Alaron.Sheet.Stats.Current(Stat.Endurance)));
        Assert.Equal(Aspect.Solar, sheet.Aspect);

        // The spellbook: Endurance at level 4, as 07 has it.
        var learned = sheet.Spells.Single(s => s.Spell.Id == Endurance);
        Assert.Equal(4, learned.Level);
        Assert.Contains(((ushort)0x0310, 4), rheda.Spells);

        // The part of the day: Midday (World+2 = 1), which the chance's table reads.
        Assert.Equal(1, c.Weather.PartOfDay);
        Assert.Equal(PartOfDay.Midday, built.Battle.PartOfDay);

        // The reagent: the party's Spice stack of 99.
        var spice = built.Game.Rules.Magic!.ReagentItem(learned.Spell.Reagent)!.Value;
        Assert.Equal(99, built.Game.Party.Inventory.Count(spice));
        Assert.Equal(99, c.Inventory.Single(s => TestRoms.ItemId(s.Item.Id) == spice).Quantity);

        // The combat stream is 07's, and neither sheet has an effect yet.
        Assert.Equal(0x5D8D8B5Bu, built.Seed);
        Assert.Equal(built.Seed, built.Game.Random.Combat.Seed);
        Assert.All(Records(c, RhedaSheet).Concat(Records(c, AlaronSheet)), Assert.Null);
        Assert.Empty(built.Rheda.Sheet.Effects);
        Assert.Empty(built.Alaron.Sheet.Effects);
    }

    /// <summary>The cast of checkpoint 08: Rheda's Endurance on herself, then Alaron.</summary>
    private (Built Built, IReadOnlyList<CastOutcome> Outcomes, LearnedSpell Spell, Lcg Expected, int SpiceBefore) Cast()
    {
        var built = Build();
        var learned = built.Rheda.Sheet.Spells.Single(s => s.Spell.Id == Endurance);
        var spice = built.Game.Rules.Magic!.ReagentItem(learned.Spell.Reagent)!.Value;
        var expected = new Lcg(built.Seed);
        var outcomes = built.Battle.Cast(built.Rheda, SpellSource.From(learned), [built.Rheda, built.Alaron]);
        return (built, outcomes, learned, expected, built.Game.Party.Inventory.Count(spice));
    }

    [Fact]
    public void RhedasEnduranceTakesEffectOnBothAtMarginFourAsCheckpoint08Has()
    {
        var (built, outcomes, _, _, _) = Cast();
        var c08 = Combat(8);
        Assert.Equal(2, outcomes.Count);
        Assert.Same(built.Rheda, outcomes[0].Target);
        Assert.Same(built.Alaron, outcomes[1].Target);
        Assert.All(outcomes, o => Assert.Equal((CastResult.Effect, 4), (o.Result, o.Margin)));

        // The record at 08's slot 0 of Rheda's array (0x802BD4C0): kind 0x13 (Endurance's position 19), power 4, school 2,
        // margin 4, not harmful, 7200.
        var original = Records(c08, RhedaSheet)[0]!;
        Assert.Equal(new Record(0x13, 4, 2, 4, 0, 7200), original);
        Assert.Equal(0x802BD4C0u, c08.ReadWord(c08.ReadWord(RhedaSheet + 0x34)));
        Assert.Equal(1, Records(c08, RhedaSheet).Count(r => r is not null));
        Assert.All(Records(c08, AlaronSheet), Assert.Null);

        foreach (var sheet in new[] { built.Rheda.Sheet, built.Alaron.Sheet })
        {
            var effect = Assert.Single(sheet.Effects);
            Assert.Equal(Endurance, effect.Source);
            Assert.Equal(original, new Record(0x13, effect.Power, (int)effect.School, effect.Margin, effect.Harmful ? 1u : 0u, effect.Timer));
            Assert.Equal((MagicSchool.Naming, 4, 4, false, 7200u), (effect.School, effect.Power, effect.Margin, effect.Harmful, effect.Timer));
        }
        Assert.Equal(19, built.Game.Rules.Magic!.Spell(Endurance)!.Position);
    }

    [Fact]
    public void TheStatsStaminaAndSpiceAreCheckpoint08And09s()
    {
        var (built, _, _, _, _) = Cast();
        var c08 = Combat(8);
        var c09 = Combat(9);
        const int endurance = (int)Stat.Endurance, stamina = (int)Stat.Stamina;

        // 08 has Rheda's Endurance raised (16/16/24/24 as trained/current/base/modified) and Alaron's not; 09 both.
        var rheda08 = Member(c08, RhedaSheet, "Rheda");
        var alaron09 = Member(c09, AlaronSheet, "Alaron");
        var rheda09 = Member(c09, RhedaSheet, "Rheda");
        Assert.Equal((16, 16, 24, 24), (rheda08.Trained[endurance], rheda08.Current[endurance], rheda08.Base[endurance], rheda08.Modified[endurance]));
        Assert.Equal((16, 16, 24, 24), (rheda09.Trained[endurance], rheda09.Current[endurance], rheda09.Base[endurance], rheda09.Modified[endurance]));
        Assert.Equal((18, 18, 26, 26), (alaron09.Trained[endurance], alaron09.Current[endurance], alaron09.Base[endurance], alaron09.Modified[endurance]));
        Assert.Equal(18, Member(c08, AlaronSheet, "Alaron").Modified[endurance]);

        foreach (var (port, original) in new[] { (built.Rheda.Sheet, rheda09), (built.Alaron.Sheet, alaron09) })
        {
            Assert.Equal((original.Trained[endurance], original.Current[endurance], original.Base[endurance], original.Modified[endurance]),
                (port.Stats.Trained(Stat.Endurance), port.Stats.Current(Stat.Endurance), port.Stats.RawBase(Stat.Endurance), port.Stats.RawModified(Stat.Endurance)));
            // Every other stat as it was.
            foreach (var stat in Enum.GetValues<Stat>().Where(s => s != Stat.Stamina))
            {
                var i = (int)stat;
                Assert.True((original.Trained[i], original.Current[i], original.Base[i], original.Modified[i])
                    == (port.Stats.Trained(stat), port.Stats.Current(stat), port.Stats.RawBase(stat), port.Stats.RawModified(stat)),
                    $"{port.Name} {stat} differs from checkpoint 09");
            }
        }

        // Stamina 68 -> 66: the cost is max(1, 6 - 4) = 2, paid once for both targets.
        Assert.Equal((66, 66), (rheda08.Current[stamina], rheda08.Modified[stamina]));
        Assert.Equal((rheda08.Current[stamina], rheda08.Modified[stamina]), (built.Rheda.Sheet.Stats.Current(Stat.Stamina), built.Rheda.Sheet.Stats.RawModified(Stat.Stamina)));
        Assert.Equal((alaron09.Current[stamina], alaron09.Modified[stamina]), (built.Alaron.Sheet.Stats.Current(Stat.Stamina), built.Alaron.Sheet.Stats.RawModified(Stat.Stamina)));

        // The Spice stack: 99 -> 98, one for both targets.
        var spice = built.Game.Rules.Magic!.ReagentItem(Endurance_Reagent(built))!.Value;
        var original08 = c08.Inventory.Single(s => TestRoms.ItemId(s.Item.Id) == spice).Quantity;
        Assert.Equal(98, original08);
        Assert.Equal(original08, built.Game.Party.Inventory.Count(spice));
        Assert.Equal(98, c09.Inventory.Single(s => TestRoms.ItemId(s.Item.Id) == spice).Quantity);
    }

    private static Reagent Endurance_Reagent(Built built) => built.Rheda.Sheet.Spells.Single(s => s.Spell.Id == Endurance).Spell.Reagent;

    [Fact]
    public void TheCastDrawsTheD100AndTheImprovementRollFromTheCombatStream()
    {
        var (built, _, learned, expected, _) = Cast();
        // The d100 is 100 (the first draw is 0xFE0848FE), the second the improvement roll, which a level 4 spell fails (p = 3 x 0.1).
        Assert.Equal(0xFE0848FEu, new Lcg(built.Seed).Next());
        expected.Next();
        expected.Next();
        Assert.Equal(expected.Seed, built.Game.Random.Combat.Seed);
        Assert.Equal(4, learned.Level);
    }

    [Theory]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(11, 4)]
    [InlineData(12, 7)]
    public void EachRoundTakesSixHundredOffTheTimerAsTheCheckpointsHaveIt(int checkpoint, int rounds)
    {
        var (built, _, _, _, _) = Cast();
        var c = Combat(checkpoint);
        for (var i = 0; i < rounds; i++)
        {
            built.Alaron.Sheet.AgeRound(built.Alaron, built.Battle, built.Battle.PartOfDay, built.Game.Random.Combat);
            built.Rheda.Sheet.AgeRound(built.Rheda, built.Battle, built.Battle.PartOfDay, built.Game.Random.Combat);
        }
        var expected = (uint)(7200 - 600 * rounds);

        // Alaron's record, found through his entity's +0x34 array in this checkpoint, not at a fixed address.
        var alaron = Assert.Single(Records(c, AlaronSheet), r => r is not null)!;
        Assert.Equal(new Record(0x13, 4, 2, 4, 0, expected), alaron);
        Assert.Equal(alaron.Timer, Assert.Single(built.Alaron.Sheet.Effects).Timer);

        // Rheda's too, where 07 to 12 still hold it.
        var rheda = Assert.Single(Records(c, RhedaSheet), r => r is not null)!;
        Assert.Equal(expected, rheda.Timer);
        Assert.Equal(rheda.Timer, Assert.Single(built.Rheda.Sheet.Effects).Timer);

        // Still raised: the effect lasts 3 x 4 = 12 rounds.
        Assert.Equal(26, built.Alaron.Sheet.Stats.Modified(Stat.Endurance));
        Assert.Equal(26, Member(c, AlaronSheet, "Alaron").Modified[(int)Stat.Endurance]);
    }
}
