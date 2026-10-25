using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Tests;

/// <summary>The spells', potions' and casting's records the importer writes as facts (ADR 0020), checked against the ROM and 1.0's code.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MagicImportTests(Na10Import fixture)
{
    private static RecordId Spell(int id) => RecordId.Hex(RecordTypes.Spell, id);

    private static RecordId Potion(int n) => RecordId.Hex(RecordTypes.Item, 0x1000 + n);

    private static RecordId Stat(int n) => RecordId.Decimal(RecordTypes.Stat, n);

    private static RecordId Skill(int n) => RecordId.Decimal(RecordTypes.Skill, n);

    private IReadOnlyList<SpellRecord> Spells => fixture.Data.Set.All<SpellRecord>(RecordTypes.Spell);

    private SpellRecord Get(int id) => fixture.Data.Set.Get<SpellRecord>(Spell(id));

    private static int Number(SpellRecord spell) => Convert.ToInt32(spell.Id.Key, 16);

    private static readonly int[] Dispels = [0x0321, 0x0322, 0x0323, 0x0324];

    [Fact]
    public void EverySpellHasItsFacts()
    {
        Assert.Equal(59, Spells.Count);
        Assert.All(Spells, s =>
        {
            Assert.NotNull(s.Effects);
            Assert.NotNull(s.AiValue);
        });
        // The ROM's 59 IDs run 0x0300 to 0x033F with five gaps.
        Assert.All(new[] { 0x0307, 0x030B, 0x030E, 0x0325, 0x032C }, id => Assert.DoesNotContain(Spell(id), Spells.Select(s => s.Id)));
        Assert.All(Spells, s => Assert.InRange(Number(s), 0x0300, 0x033F));
    }

    [Fact]
    public void ASpellsPositionIsItsKindInTheCodesSpellList()
    {
        var list = TestRoms.Get(RomVersionId.Na10).Table(CodeTable.SpellList).ToArray();
        Assert.Equal(59, list.Length);
        Assert.All(Spells, s => Assert.Equal(s.Id, Spell(0x300 | list[s.Position!.Value])));
    }

    [Fact]
    public void EnduranceIsAStatSpellOfTwoALevelFor1800TicksALevel()
    {
        var endurance = Get(0x0310);
        var stat = Assert.IsType<StatEffect>(Assert.Single(endurance.Effects!));
        Assert.Equal(Stat(3), stat.Stat);
        Assert.Equal(Magnitude.Per(2), stat.Amount);
        Assert.Equal(new EffectDuration(PerLevel: 1800), endurance.Duration);
        Assert.Equal(52, endurance.AiValue);
        Assert.Equal(Resisted.Never, endurance.Resisted);
        // The checkpoints' cast: level 4, a timer of 7200 and 8 more Endurance.
        Assert.Equal(7200u, endurance.Duration!.At(4));
        Assert.Equal(8, stat.Amount.At(4));
    }

    [Fact]
    public void FireballIsPlacedAndResistedByAll()
    {
        var fireball = Get(0x0305);
        Assert.True(fireball.Placed);
        Assert.Equal(Resisted.Always, fireball.Resisted);
        Assert.Equal(Aspect.Solar, fireball.Aspect);
        var damage = Assert.IsType<DamageEffect>(Assert.Single(fireball.Effects!));
        Assert.Equal(Magnitude.Per(1), damage.Dice);
        Assert.Equal([Element.Fire], damage.Elements);
        Assert.Null(fireball.Duration);
    }

    [Fact]
    public void TeleportationAndTheDispelsArePlacedAndNeverResisted()
    {
        foreach (var id in Dispels.Append(0x033A))
        {
            var spell = Get(id);
            Assert.True(spell.Placed, spell.Name);
            Assert.Equal(Resisted.Never, spell.Resisted);
        }
        Assert.Equal(MagicSchool.Elemental, Assert.IsType<DispelEffect>(Assert.Single(Get(0x0321).Effects!)).School);
        Assert.Equal(MagicSchool.Star, Assert.IsType<DispelEffect>(Assert.Single(Get(0x0324).Effects!)).School);
        Assert.Equal(81, Get(0x0322).AiValue);
        Assert.IsType<TeleportEffect>(Assert.Single(Get(0x033A).Effects!));
        // Only Fireball, Teleportation and the dispels have the placed bit.
        Assert.Equal([0x0305, 0x0321, 0x0322, 0x0323, 0x0324, 0x033A], Spells.Where(s => s.Placed).Select(Number).Order());
    }

    [Fact]
    public void WindIsDamageOnOpponentsAndStaminaAndWeatherOnAllies()
    {
        var wind = Get(0x0309);
        Assert.Equal(Resisted.Opponents, wind.Resisted);
        Assert.Equal(3, wind.Effects!.Count);
        var damage = Assert.IsType<DamageEffect>(wind.Effects[0]);
        Assert.Equal(EffectFor.Opponents, damage.For);
        Assert.Equal([Element.Air], damage.Elements);
        var stamina = Assert.IsType<StaminaEffect>(wind.Effects[1]);
        Assert.Equal(EffectFor.Allies, stamina.For);
        Assert.True(stamina.RaisesMaximum);
        var weather = Assert.IsType<WeatherEffect>(wind.Effects[2]);
        Assert.Equal((EffectFor.Allies, 1), (weather.For, weather.AtEnd));
        Assert.Equal(new EffectDuration(PerLevel: 1800), wind.Duration);
    }

    /// <summary>The rule of <c>Combatant.IsResistable</c> (<c>0x800641B8</c>), from each spell's target type and placed bit.</summary>
    [Fact]
    public void ResistedFollowsTheOriginalsRule()
    {
        foreach (var s in Spells)
        {
            var id = Number(s);
            var expected = Dispels.Contains(id) || id == 0x033A ? Resisted.Never
                : id == 0x0309 ? Resisted.Opponents
                : s.TargetType == SpellTargetType.Enemy || s.Placed ? Resisted.Always
                : Resisted.Never;
            Assert.True(expected == s.Resisted, $"{s.Name}: {s.Resisted}, the rule says {expected}.");
        }
        Assert.All(Spells.Where(s => s.TargetType == SpellTargetType.Enemy), s => Assert.Equal(Resisted.Always, s.Resisted));
    }

    [Fact]
    public void AspectAndPlacedComeFromTheRomsBits()
    {
        Assert.All(Spells, s =>
        {
            Assert.Equal((s.Unk1E & 1) != 0, s.Aspect == Aspect.Lunar);
            Assert.Equal((s.Unk1E & 2) != 0, s.Aspect == Aspect.Solar);
            Assert.Equal((s.Unk1E & 0x10) != 0, s.Placed);
        });
        Assert.Equal(Aspect.Lunar, Get(0x0306).Aspect);
        Assert.Equal(Aspect.Solar, Get(0x033D).Aspect);
        Assert.Equal(Aspect.None, Get(0x030A).Aspect);
    }

    [Fact]
    public void FiltersNameTheEntitiesTheCodeDoes()
    {
        var entities = fixture.Data.Set.All<EntityRecord>(RecordTypes.Entity).ToDictionary(e => e.Id, e => e.Name);
        var elementals = Get(0x0301).Filter!.Entities!;
        Assert.Equal(5, elementals.Count);
        Assert.Equal(new[] { "Air Elemental", "Earth Elemental", "Firelord", "Fire Elemental", "Water Elemental" }.Order(), elementals.Select(e => entities[e]).Order());
        Assert.Equal(Get(0x0301).Filter!.Entities, Get(0x030A).Filter!.Entities);
        Assert.Equal([Entity(0x0221)], Get(0x030D).Filter!.Entities);
        Assert.Equal([Entity(0x0266), Entity(0x028C)], Get(0x0314).Filter!.Entities);
        Assert.Equal([EntityCategory.Chaos], Get(0x030C).Filter!.NotCategories);
        Assert.Equal([Entity(0x0221)], Get(0x031B).NotResistedBy);
        Assert.Single(Spells, s => s.NotResistedBy is not null);
        Assert.Equal(5, Spells.Count(s => s.Filter is not null));
        // The Shadow is immune to what holds.
        var shadow = Assert.IsType<ImmobiliseEffect>(Get(0x031B).Effects![0]).Immune!;
        Assert.Equal("Shadow", entities[Assert.Single(shadow)]);
    }

    [Fact]
    public void TheDurationsAreALevelOf1800TicksOrTheBattle()
    {
        Assert.Equal(new EffectDuration(Battle: true), Get(0x0333).Duration);
        Assert.Equal(uint.MaxValue, Get(0x0333).Duration!.At(3));
        int[] once =
        [
            0x033C, 0x0338, 0x0303, 0x0304, 0x0305, 0x0306, 0x033A, 0x030A, 0x030F, 0x0311, 0x0312, 0x0315, 0x0321, 0x0322, 0x0323, 0x0324,
            0x0327, 0x0328, 0x0331,
        ];
        Assert.Equal(once.Order(), Spells.Where(s => s.Duration is null).Select(Number).Order());
        Assert.All(Spells.Where(s => Number(s) != 0x0333 && s.Duration is not null), s => Assert.Equal(new EffectDuration(PerLevel: 1800), s.Duration));
        Assert.Empty(Get(0x030F).Effects!);
        Assert.Empty(Get(0x0311).Effects!);
    }

    [Fact]
    public void TheMultiEffectSpellsListEachHalf()
    {
        var tap = Get(0x031A).Effects!;
        Assert.Equal(Magnitude.Per(-2), Assert.IsType<StaminaEffect>(tap[0]).Amount);
        var gain = Assert.IsType<StaminaEffect>(tap[1]);
        Assert.True(gain.ToCaster);
        Assert.Equal(Magnitude.Per(2), gain.Amount);

        var web = Get(0x0330).Effects!;
        var held = Assert.IsType<ImmobiliseEffect>(web[0]);
        Assert.Equal(2, held.Escape);
        Assert.Equal(new Dice(2), Assert.IsType<DamagePerRoundEffect>(web[1]).Dice);
        Assert.Single(held.Immune!);

        var wraith = Assert.IsType<DrainStatEffect>(Assert.Single(Get(0x0333).Effects!));
        Assert.Equal([Stat(0), Stat(1), Stat(2), Stat(4)], wraith.Stats);
        Assert.Equal(new Dice(2), wraith.Dice);

        Assert.Equal(Magnitude.Per(1), Assert.IsType<ProtectionEffect>(Assert.Single(Get(0x031C).Effects!)).Leaves);
        Assert.Null(Assert.IsType<ProtectionEffect>(Assert.Single(Get(0x0300).Effects!)).Leaves);
        Assert.Equal(Magnitude.Per(0.5f), Assert.IsType<PoisonEffect>(Assert.Single(Get(0x0332).Effects!)).Amount);
        Assert.Equal(3, Magnitude.Per(0.5f).At(7));
        Assert.Equal(Skill(7), Assert.IsType<SkillEffect>(Assert.Single(Get(0x032E).Effects!)).Skill);
    }

    /// <summary>
    /// The list Restore's potion cures from is code (<c>0x800D34B4</c>, kinds in <c>SpellList</c>'s positions), as are Wraith Touch's four stats
    /// (<c>0x800D34B0</c>).
    /// </summary>
    [Fact]
    public void RestoreAndWraithTouchHoldTheListsInTheCode()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var list = rom.Table(CodeTable.SpellList).ToArray();
        var restore = CodeSegment.At(rom, 0x800D34B4)[..6].ToArray().Select(k => Spell(0x300 | list[k])).ToList();
        var cure = Assert.IsType<CureEffect>(Assert.Single(fixture.Data.Set.Get<ItemRecord>(Potion(8)).Effects!));
        Assert.Equal(restore, cure.Spells);
        Assert.Equal(0xFF, CodeSegment.At(rom, 0x800D34B4)[6]);
        var wraith = CodeSegment.At(rom, 0x800D34B0)[..4].ToArray().Select(s => Stat(s)).ToList();
        Assert.Equal(wraith, ((DrainStatEffect)Get(0x0333).Effects![0]).Stats);
    }

    [Fact]
    public void ThePotionsEffects()
    {
        ItemRecord P(int n) => fixture.Data.Set.Get<ItemRecord>(Potion(n));
        T One<T>(int n) where T : Effect => Assert.IsType<T>(Assert.Single(P(n).Effects!));
        var timed = new EffectDuration(Flat: 648000);

        Assert.Equal(new Dice(4), One<BlastEffect>(0).Dice);
        Assert.Equal(new Dice(8), One<BlastEffect>(1).Dice);
        var gas = One<BlastEffect>(2);
        Assert.Equal((new Dice(0, 6, 15), true), (gas.Dice, gas.Stamina));
        var acid = P(3).Effects!;
        Assert.Equal(new Dice(2), Assert.IsType<BlastEffect>(acid[0]).Dice);
        Assert.Equal(new Dice(2), Assert.IsType<CorrodeEffect>(acid[1]).Radius);
        Assert.Equal((15, 25), (One<HealEffect>(4).Low, One<HealEffect>(4).High));
        Assert.Equal((40, 60), (One<HealEffect>(6).Low, One<HealEffect>(6).High));
        var stamina = P(5).Effects!;
        Assert.Equal([Spell(0x0318)], Assert.IsType<CureEffect>(stamina[0]).Spells);
        Assert.Equal((Magnitude.Fixed(0), true), (((StaminaEffect)stamina[1]).Amount, ((StaminaEffect)stamina[1]).ToBase));
        Assert.Equal([Spell(0x0332)], One<CureEffect>(7).Spells);
        Assert.Equal([Spell(0x0333), Spell(0x0302), Spell(0x0313), Spell(0x0318), Spell(0x0336), Spell(0x032A)], One<CureEffect>(8).Spells);
        Assert.Equal((Stat(4), Magnitude.Fixed(10)), (One<StatEffect>(9).Stat, One<StatEffect>(9).Amount));
        Assert.Equal((Stat(2), Magnitude.Fixed(10)), (One<StatEffect>(10).Stat, One<StatEffect>(10).Amount));
        Assert.Equal((Skill(3), Magnitude.Fixed(3)), (One<SkillEffect>(13).Skill, One<SkillEffect>(13).Amount));
        Assert.Equal((Skill(1), Magnitude.Fixed(3)), (One<SkillEffect>(14).Skill, One<SkillEffect>(14).Amount));
        Assert.Equal(Magnitude.Fixed(15), One<ProtectionEffect>(15).Amount);
        Assert.Equal((Skill(7), Magnitude.Fixed(3)), (One<SkillEffect>(16).Skill, One<SkillEffect>(16).Amount));

        Assert.Null(P(11).Effects);
        Assert.Null(P(12).Effects);
        Assert.All(new[] { 9, 10, 13, 14, 15, 16 }, n => Assert.Equal(timed, P(n).Duration));
        Assert.All(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 11, 12 }, n => Assert.Null(P(n).Duration));
        Assert.Equal(0x9E340u, timed.At(0));
    }

    [Fact]
    public void TheMagicRecordHoldsCastingsNumbers()
    {
        var magic = fixture.Data.Set.Singleton<MagicRecord>();
        Assert.Equal(RecordId.Decimal(RecordTypes.Magic, 0), magic.Id);
        Assert.Equal((4, 6, 1, 5), (magic.Chance.Intelligence, magic.Chance.Level, magic.Chance.CharacterLevel, magic.Chance.Minimum));
        Assert.Equal([0.75f, 1.25f, 1.25f, 0.75f, 0.75f], magic.Chance.ByPartOfDay);
        Assert.Equal([0.5f, 0.75f, 1.25f, 1.5f], magic.Chance.ByMoon);
        Assert.Equal((3, 1, 1, 0.75f, 1.25f), (magic.Resist.Willpower, magic.Resist.Endurance, magic.Resist.Level, magic.Resist.SameAspect, magic.Resist.OtherAspect));
        Assert.Equal((600, 2, 0.1f, 10, 15, 7), (magic.RoundTicks, magic.MostDisabled, magic.Improvement, magic.MaxLevel, magic.SpellEffects, magic.PotionEffects));
        Assert.Equal([Potion(16)], magic.KeptAfterBattle);
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheCastingFactorsReadTheSameInEveryVersion(RomVersionId id)
    {
        var factors = CastingFactors.Read(TestRoms.Get(id));
        Assert.Equal([0.5f, 0.75f, 1.25f, 1.5f], factors.ByMoon);
        Assert.Equal([0.75f, 1.25f, 1.25f, 0.75f, 0.75f], factors.ByPartOfDay);
        Assert.Equal((0.1f, 0.75f, 1.25f), (factors.Improvement, factors.SameAspect, factors.OtherAspect));
    }
}
