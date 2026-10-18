using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using CharacterParty = Aidyn.Core.Characters.Party;

namespace Aidyn.Core.Tests;

/// <summary>Effects on a character (ADR 0020, docs/systems/magic.md): adding, ageing, ending, and what each type does.</summary>
public sealed class MagicTests
{
    private static Character Alaron() => Characters.Create(Characters.Alaron());

    private static ActiveEffect Cast(Character on, SpellRecord spell, int level, int margin = 4, Aidyn.Core.Rng.IRandom? random = null) =>
        on.AddEffect(spell.Id, [.. spell.Effects!.Where(EffectRules.Lasting)], level, spell.School, margin, spell.Duration!.At(level), null,
            null, random)!;

    [Fact]
    public void AStatSpellRaisesBaseAndModifiedAndLastsThreeRoundsALevel()
    {
        var alaron = Alaron();
        var endurance = alaron.Stats.Base(Stat.Endurance);
        var effect = Cast(alaron, MagicData.Endurance, 4);
        Assert.Equal(7200u, effect.Timer);
        Assert.Equal(endurance + 8, alaron.Stats.Base(Stat.Endurance));
        Assert.Equal(endurance + 8, alaron.Stats.Modified(Stat.Endurance));
        Assert.Equal(endurance, alaron.Stats.Trained(Stat.Endurance));

        // 600 a round (EffectRecord.Age): 12 rounds to 0, removed on the 13th.
        for (var round = 1; round <= 12; round++)
        {
            alaron.AgeRound(null, null, PartOfDay.Midday, new Lcg());
            Assert.Equal(7200u - 600u * (uint)round, effect.Timer);
        }
        Assert.Single(alaron.Effects);
        alaron.AgeRound(null, null, PartOfDay.Midday, new Lcg());
        Assert.Empty(alaron.Effects);
        Assert.Equal(endurance, alaron.Stats.Base(Stat.Endurance));
    }

    [Fact]
    public void ASpellAlreadyOnDoesntGoOnAgainOrRefresh()
    {
        var alaron = Alaron();
        var first = Cast(alaron, MagicData.Endurance, 4);
        alaron.AgeRound(null, null, PartOfDay.Midday, new Lcg());
        Assert.Null(alaron.AddEffect(MagicData.Endurance.Id, MagicData.Endurance.Effects!, 6, MagicSchool.Naming, 4, 9999, null, null, null));
        Assert.Equal(6600u, Assert.Single(alaron.Effects).Timer);
        Assert.Same(first, alaron.Effects[0]);
    }

    [Fact]
    public void SpiritShieldLeavesALevelOfProtectionAndAirShieldNone()
    {
        var alaron = Alaron();
        var protection = alaron.Protection;
        alaron.RemoveEffect(Cast(alaron, MagicData.AirShield, 4));
        Assert.Equal(protection, alaron.Protection);

        var spirit = Cast(alaron, MagicData.SpiritShield, 4);
        Assert.Equal(protection + 8, alaron.Protection);
        alaron.RemoveEffect(spirit);
        Assert.Equal(protection + 4, alaron.Protection);
    }

    [Fact]
    public void CheatDeathHealsTwiceItsMarginAndEnds()
    {
        var alaron = Alaron();
        Cast(alaron, MagicData.CheatDeath, 6, margin: 5);
        Assert.True(alaron.HasCheatDeath);

        Assert.True(alaron.TakeDamage(alaron.Hp + 10));
        Assert.Equal(10, alaron.Hp);
        Assert.False(alaron.HasCheatDeath);
    }

    [Fact]
    public void DyingOutsideABattleLosesThePotionsButNotTheSpells()
    {
        var alaron = Alaron();
        Cast(alaron, MagicData.Haste, 3);
        alaron.ApplyPotionEffect(Potion.Clarity, 0, 1000);

        Assert.False(alaron.TakeDamage(alaron.Hp));
        Assert.True(alaron.IsDead);
        Assert.Empty(alaron.PotionEffects);
        Assert.Single(alaron.Effects);
    }

    [Fact]
    public void ADeadCharactersBuffIsntTakenBack()
    {
        // Entity.SubStat does nothing to the dead, so dying under Endurance keeps its base raised, as the original's does.
        var alaron = Alaron();
        var endurance = alaron.Stats.Base(Stat.Endurance);
        var effect = Cast(alaron, MagicData.Endurance, 2);
        alaron.TakeDamage(alaron.Hp);
        alaron.RemoveEffect(effect);
        Assert.Equal(endurance + 4, alaron.Stats.Base(Stat.Endurance));
    }

    [Fact]
    public void PoisonTakesHalfALevelOfEnduranceARound()
    {
        var alaron = Alaron();
        var endurance = alaron.Stats.Modified(Stat.Endurance);
        Cast(alaron, MagicData.Poison, 5);
        var aged = alaron.AgeRound(null, null, PartOfDay.Midday, new Lcg());
        Assert.Equal(2, aged.Damage);
        Assert.Equal(endurance - 2, alaron.Stats.Modified(Stat.Endurance));
        Assert.Equal(endurance, alaron.Stats.Base(Stat.Endurance));
    }

    [Fact]
    public void PhotosynthesisHealsItsMarginByDayOnly()
    {
        var alaron = Alaron();
        alaron.TakeDamage(20);
        Cast(alaron, MagicData.Photosynthesis, 3, margin: 4);
        var hp = alaron.Hp;
        Assert.Equal(0, alaron.AgeRound(null, null, PartOfDay.Night, new Lcg()).Healed);
        Assert.Equal(hp, alaron.Hp);
        Assert.Equal(4, alaron.AgeRound(null, null, PartOfDay.Afternoon, new Lcg()).Healed);
        Assert.Equal(hp + 4, alaron.Hp);
    }

    [Fact]
    public void AWebIsEscapedByAD100UnderTwiceStrengthAndOtherwiseHurts()
    {
        var alaron = Alaron();
        var strength = alaron.Stats.Modified(Stat.Strength);
        Cast(alaron, MagicData.WebOfStarlight, 3);
        // A roll at twice Strength holds, and 2d6 follow.
        var held = alaron.AgeRound(null, null, PartOfDay.Midday,
            new ScriptedRandom(RandomFor.D(100, 2 * strength), RandomFor.D(6, 3), RandomFor.D(6, 4)));
        Assert.Equal(7, held.Damage);
        Assert.Single(alaron.Effects);
        alaron.AgeRound(null, null, PartOfDay.Midday, new ScriptedRandom(RandomFor.D(100, 2 * strength - 1)));
        Assert.Empty(alaron.Effects);
    }

    [Fact]
    public void WraithTouchDrainsADrawnStatUntilItEnds()
    {
        var alaron = Alaron();
        var dexterity = alaron.Stats.Base(Stat.Dexterity);
        // The third of Intelligence, Willpower, Dexterity and Strength, then 2d6 = 7.
        var effect = Cast(alaron, MagicData.WraithTouch, 8, random: new ScriptedRandom(RandomFor.D(4, 3), RandomFor.D(6, 3), RandomFor.D(6, 4)));
        Assert.Equal(dexterity - 7, alaron.Stats.Base(Stat.Dexterity));
        Assert.Equal(ActiveEffect.Never, effect.Timer);
        Assert.Equal((int)Stat.Dexterity, effect.Margin);
        alaron.EndBattleEffects();
        Assert.Equal(dexterity, alaron.Stats.Base(Stat.Dexterity));
    }

    [Fact]
    public void ABattlesEndClearsSpellsAndPutsTheWornEnchantmentBack()
    {
        var amulet = ItemData.NewItem(5, ItemKind.Amulet) with { Bonuses = ItemData.Enchants(MagicData.Haste.Id) };
        var alaron = Alaron();
        alaron.Equipment.EquipGear(ItemData.Inst(amulet));
        var worn = Assert.Single(alaron.Effects);
        Assert.True(worn.Worn);
        Assert.Equal(ActiveEffect.Never, worn.Timer);

        Cast(alaron, MagicData.Endurance, 2);
        alaron.RemoveAllEffects(null);
        alaron.EndBattleEffects();
        Assert.Equal(MagicData.Haste.Id, Assert.Single(alaron.Effects).Source);

        alaron.Equipment.RemoveGear(0);
        Assert.Empty(alaron.Effects);
    }

    [Fact]
    public void HasteAndStellarGravityChangeTheRadiusInTurn()
    {
        var alaron = Alaron();
        Cast(alaron, MagicData.StellarGravity, 5);
        Cast(alaron, MagicData.Haste, 3);
        var combatant = BattleCombatant(alaron);
        // Gravity first, by slot: 4 less 5 leaves 1, then Haste's 3.
        Assert.Equal(4, combatant.RadiusWithEffects(4));
        Assert.Equal(6, combatant.RadiusWithEffects(8));
    }

    [Fact]
    public void TheChanceToCastIsCheckpoint08s()
    {
        // Rheda in checkpoint 07: Intelligence 22, Endurance at 4, Level 19, Solar, at midday: (88 + 24 + 19) × 1.25.
        var rheda = Characters.Create(Characters.Alaron() with { Level = 19, Attributes = new Attributes(22, 12, 15, 16, 15, 68) });
        Assert.Equal(163, Casting.Chance(rheda, 4, MagicData.Record.Chance, PartOfDay.Midday, 0, 0, hostile: false));
        Assert.Equal(98, Casting.Chance(rheda, 4, MagicData.Record.Chance, PartOfDay.Night, 0, 0, hostile: false));
        Assert.Equal(4, CharacterParty.SkillCheckStep(163 - 100));
    }

    [Fact]
    public void AWardAddsItsMagnitudeAgainstItsSchoolOnly()
    {
        var alaron = Alaron();
        Cast(alaron, MagicData.VsNaming, 2);
        Assert.Equal(12, Casting.Ward(alaron, MagicSchool.Naming));
        Assert.Equal(0, Casting.Ward(alaron, MagicSchool.Star));
    }

    [Fact]
    public void ResistancesScaleSpellDamageButZeroStaysZero()
    {
        Assert.Equal(10, Casting.Damage(10, null));
        Assert.Equal(1, Casting.Damage(1, 0.5f));
        Assert.Equal(0, Casting.Damage(10, 0));
        Assert.Equal(15, Casting.Damage(10, 1.5f));
    }

    [Theory]
    [InlineData(SpellTargetCount.One, 7, 1)]
    [InlineData(SpellTargetCount.ByLevel, 4, 2)]
    [InlineData(SpellTargetCount.ByLevel, 5, 3)]
    [InlineData(SpellTargetCount.All, 1, 9)]
    public void ASpellTakesOneAllOrHalfItsLevel(SpellTargetCount count, int level, int expected) =>
        Assert.Equal(expected, Casting.TargetCount(MagicData.Endurance with { TargetCount = count }, level, all: 9));

    [Fact]
    public void TheCostIsTheSpellsLessALevelAtLeastOne()
    {
        Assert.Equal(2, Casting.Cost(MagicData.Endurance, 4));
        Assert.Equal(1, Casting.Cost(MagicData.Endurance, 10));
    }

    // A combatant for rules that read only its sheet.
    private static Combat.Combatant BattleCombatant(Character sheet) =>
        new(0, sheet, new EntityBody(RecordId.Hex(RecordTypes.Actor, 1), 0.5f, 1, 1), new Movement.Body(System.Numerics.Vector3.Zero, 0.7f),
            Combat.CombatantFlags.Active);
}

/// <summary>The Antidote and Restore in trek, which wait on poison and weakening (items.md), and the Stamina potion's Exhaustion.</summary>
public sealed class CuringPotionTests
{
    private static (CharacterParty Party, Character Member) Setup(params Potion[] carried)
    {
        var member = PotionData.Member("Brenna");
        var party = TradeData.PartyOf(PotionData.Catalog(), null, member);
        foreach (var potion in carried)
            party.Inventory.Add(Potions.Id(potion));
        return (party, member);
    }

    private static Item Item(CharacterParty party, Potion potion) => party.Inventory.Catalog[Potions.Id(potion)];

    private static void Cast(Character on, SpellRecord spell, int level) =>
        on.AddEffect(spell.Id, spell.Effects!, level, spell.School, 3, spell.Duration!.At(level), null, null, null);

    [Fact]
    public void TheAntidoteWaitsForPoisonAndEndsIt()
    {
        var (party, member) = Setup(Potion.Antidote);
        Assert.Equal(DrinkResult.NotNow, Potions.Drink(party, member, Item(party, Potion.Antidote), new ScriptedRandom()));
        Cast(member, MagicData.Poison, 4);
        Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, Potion.Antidote), new ScriptedRandom()));
        Assert.Empty(member.Effects);
        Assert.False(party.Inventory.Has(Potions.Id(Potion.Antidote)));
    }

    [Fact]
    public void RestoreEndsTheWeakeningSpellsAndUndoesThem()
    {
        var (party, member) = Setup(Potion.Restore);
        var endurance = member.Stats.Base(Stat.Endurance);
        Cast(member, MagicData.Endurance, 2);
        Assert.Equal(DrinkResult.NotNow, Potions.Drink(party, member, Item(party, Potion.Restore), new ScriptedRandom()));
        Cast(member, MagicData.Weakness, 3);
        Assert.Equal(endurance + 4 - 6, member.Stats.Base(Stat.Endurance));
        Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, Potion.Restore), new ScriptedRandom()));
        Assert.Equal(MagicData.Endurance.Id, Assert.Single(member.Effects).Source);
        Assert.Equal(endurance + 4, member.Stats.Base(Stat.Endurance));
    }

    [Fact]
    public void TheStaminaPotionEndsExhaustionThenRestoresStamina()
    {
        var (party, member) = Setup(Potion.Stamina);
        Cast(member, MagicData.Exhaustion, 2);
        Assert.Equal(DrinkResult.Drunk, Potions.Drink(party, member, Item(party, Potion.Stamina), new ScriptedRandom()));
        Assert.Empty(member.Effects);
        Assert.Equal(member.Stats.Base(Stat.Stamina), member.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void FlasksAndPotionsWithoutEffectsCantBeDrunkInTrek()
    {
        var (party, member) = Setup(Potion.FireFlask, Potion.Aspect);
        Assert.Equal(DrinkResult.NotInTrek, Potions.Drink(party, member, Item(party, Potion.FireFlask), new ScriptedRandom()));
        Assert.Equal(DrinkResult.NotInTrek, Potions.Drink(party, member, Item(party, Potion.Aspect), new ScriptedRandom()));
    }
}
