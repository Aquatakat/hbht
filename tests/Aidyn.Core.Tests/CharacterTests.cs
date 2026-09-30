using Aidyn.Core.Characters;
using CharacterParty = Aidyn.Core.Characters.Party;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>Rules and characters with the original's numbers, for the character tests.</summary>
internal static class Characters
{
    public static readonly RecordId Oriana = RecordId.Hex(RecordTypes.Entity, 0x0236);

    private static readonly int[] Levels =
    [
        0, 500, 2500, 7000, 15000, 27500, 45500, 70000, 102000, 142500, 192500, 253000, 325000, 409500, 507500, 620000, 748000,
        892500, 1054500, 1235000, 1435000, 1655500, 1897500, 2162000, 2450000, 2762500, 3100500, 3465000, 3857000, 4277500,
        4727500, 5208000, 5720000, 6264500, 6842500, 7455000, 8103000, 8787500, 9509500, 10270000, 11070000,
    ];

    public static readonly CharacterRules Rules = new(
        [.. new (int BaseCap, int Cap, int? Cost)[] { (30, 40, 12), (30, 40, 12), (30, 40, 24), (40, 50, 12), (30, 40, 12), (90, 120, 4), (40, 40, null) }
            .Select((s, i) => new StatRecord(RecordId.Decimal(RecordTypes.Stat, i), ((Stat)i).ToString(), null, s.BaseCap, s.Cap, s.Cost))],
        [.. new[] { 1500, 500, 1000, 750, 500, 750, 1000, 500, 1000, 1000, 1000, 1500, 0, 0, 0, 400, 600, 400, 0, 0, 600, 400, 0, 500 }
            .Select((cost, i) => new SkillRecord(new SkillId(i).Record, new SkillId(i).ToString(), null, cost, 10, 15))],
        new ProgressionRecord(RecordId.Decimal(RecordTypes.Progression, 0), Levels, 1.5f, new TrainingPrices(200, 40, 0.8f, [Oriana], 10)));

    public static readonly SpellRecord Fireball = Spell(0x0305, MagicSchool.Elemental, wizard: 3, cost: 200);

    public static SpellRecord Spell(int id, MagicSchool school, int wizard, int cost) => new(RecordId.Hex(RecordTypes.Spell, id),
        $"Spell {id:X4}", school, 0, 5, SpellTargetCount.One, SpellTargetType.Enemy, wizard, SpellSpecial.None, 3, Reagent.Spice, cost, 0);

    /// <summary>Alaron as the entity DB starts him.</summary>
    public static EntityRecord Alaron(int level = 1, KnownSpell[]? spells = null) => new(
        RecordId.Hex(RecordTypes.Entity, 0x023F), "Alaron", null, EntityCategory.Party, Aspect.Solar, MagicSchool.Chaos,
        TrueName: false, Heavy: true, level, new Attributes(18, 12, 15, 15, 15, 30),
        new SkillLevels(0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 3, 1), new WeaponSkillLevels(Hafted: 1, Missile: 3, Pole: 0, Sword: 3, Thrown: 0),
        ShieldSkill: 3, spells ?? [], [null, null, null], null, null, 0, 2, 0, [], 0, 0, 0, 0, RecordId.Decimal(RecordTypes.Flag, 590));

    public static EntityRecord Trainer(RecordId id, SkillLevels skills, KnownSpell[]? spells = null) => Alaron() with
    {
        Id = id, Name = "Trainer", Skills = skills, WeaponSkills = new WeaponSkillLevels(Sword: 6), ShieldSkill = null, Spells = spells ?? [],
        TrueNameFlag = null,
    };

    public static Character Create(EntityRecord entity) => Character.Create(entity, Rules, _ => Fireball);

    /// <summary>A stream whose d100 rolls come out as <paramref name="rolls"/>.</summary>
    public static IRandom D100(params int[] rolls) => new Scripted([.. rolls.Select(r => (uint)(((ulong)(r - 1) * 0x1_0000_0000 + 99) / 100))]);

    private sealed class Scripted(uint[] values) : IRandom
    {
        private int _next;

        public uint Next() => values[_next++];
    }
}

public class CharacterTests
{
    private static Character Alaron(int level = 1) => Characters.Create(Characters.Alaron(level));

    [Fact]
    public void AlaronStartsWithHisRecordsSheetAndHisLevelsExperience()
    {
        var alaron = Alaron(level: 3);

        Assert.Equal(18, alaron.Stats.Base(Stat.Intelligence));
        Assert.Equal(3, alaron.Level);
        Assert.Equal(7000u, alaron.TotalExp);
        Assert.Equal(0u, alaron.SpendableExp);
        Assert.Equal(3, alaron.Skills.Base(Skill.Warrior));
        Assert.False(alaron.Skills.CanLearn(WeaponClass.Bite));
        Assert.Equal(0, alaron.Skills.Modified(WeaponClass.Bite));
        Assert.Equal(3, alaron.Skills.Base(SkillId.Shield));
        Assert.False(alaron.TrueName);
    }

    [Fact]
    public void AlaronHasHisTrueNameOnceTheRoogCinematicsFlagIsSet()
    {
        var alaron = Character.Create(Characters.Alaron(), Characters.Rules, _ => Characters.Fireball, flag => flag.Key == "590");

        Assert.True(alaron.TrueName);
    }

    [Fact]
    public void HitPointsAreEnduranceStaminaAndLevel()
    {
        var alaron = Alaron(level: 5);

        Assert.Equal(15 + 30 + 5, alaron.MaxHp);
        Assert.Equal(alaron.MaxHp, alaron.Hp);
    }

    [Fact]
    public void BaseValuesReadAsTheirCapsButCountTheirExcessWhenModified()
    {
        // Some monsters start above the base caps, such as 120 Stamina.
        var ogre = Characters.Create(Characters.Alaron() with { Attributes = new Attributes(10, 10, 10, 45, 10, 120) });

        Assert.Equal(90, ogre.Stats.Base(Stat.Stamina));
        Assert.Equal(120, ogre.Stats.Modified(Stat.Stamina));
        Assert.Equal(40, ogre.Stats.Base(Stat.Endurance));
        Assert.Equal(45, ogre.Stats.Modified(Stat.Endurance));
        Assert.Equal(40 + 90 + 1, ogre.MaxHp);
        Assert.Equal(ogre.MaxHp, ogre.Hp);
    }

    [Fact]
    public void DamageTakesTheLevelsPointsThenStaminaThenEndurance()
    {
        var alaron = Alaron(level: 5);

        alaron.TakeDamage(3);
        Assert.Equal((2, 30, 15), (alaron.Stats.Modified(Stat.Level), alaron.Stats.Modified(Stat.Stamina), alaron.Stats.Modified(Stat.Endurance)));

        alaron.TakeDamage(12);
        Assert.Equal((0, 20, 15), (alaron.Stats.Modified(Stat.Level), alaron.Stats.Modified(Stat.Stamina), alaron.Stats.Modified(Stat.Endurance)));

        alaron.TakeDamage(25);
        Assert.Equal((0, 0, 10), (alaron.Stats.Modified(Stat.Level), alaron.Stats.Modified(Stat.Stamina), alaron.Stats.Modified(Stat.Endurance)));
        Assert.Equal(10, alaron.Hp);
        Assert.True(alaron.Stats.IsHurt(Stat.Level));

        alaron.TakeDamage(50);
        Assert.True(alaron.IsDead);
        Assert.Equal(0, alaron.Stats.Current(Stat.Endurance));
    }

    [Fact]
    public void SkillsSpendStaminaThenEnduranceButNeverTheLevel()
    {
        var alaron = Alaron(level: 5);

        alaron.DrainStamina(32);

        Assert.Equal((5, 0, 13), (alaron.Stats.Modified(Stat.Level), alaron.Stats.Modified(Stat.Stamina), alaron.Stats.Modified(Stat.Endurance)));
    }

    [Fact]
    public void HealingRestoresEnduranceThenStaminaThenTheLevel()
    {
        var alaron = Alaron(level: 5);
        alaron.TakeDamage(5 + 30 + 10);

        alaron.Heal(12);
        Assert.Equal((0, 2, 15), (alaron.Stats.Modified(Stat.Level), alaron.Stats.Modified(Stat.Stamina), alaron.Stats.Modified(Stat.Endurance)));

        alaron.Heal(100);
        Assert.Equal(alaron.MaxHp, alaron.Hp);
    }

    [Fact]
    public void RestoringStaminaToEightyPercentTruncates()
    {
        var alaron = Characters.Create(Characters.Alaron() with { Attributes = new Attributes(18, 12, 15, 15, 15, 33) });
        alaron.DrainStamina(33);

        alaron.RestoreStamina(toEightyPercent: true);
        Assert.Equal(26, alaron.Stats.Modified(Stat.Stamina));

        alaron.RestoreStamina(toEightyPercent: true);
        Assert.Equal(26, alaron.Stats.Modified(Stat.Stamina));
        alaron.RestoreStamina();
        Assert.Equal(33, alaron.Stats.Modified(Stat.Stamina));
    }

    [Fact]
    public void CampingHealsAFractionOfTheMaximumUpToWhatsMissing()
    {
        var alaron = Alaron(level: 5);
        alaron.TakeDamage(40);

        alaron.CampHeal(0.5f);
        Assert.Equal(10 + 25, alaron.Hp);

        alaron.CampHeal(0.5f);
        Assert.Equal(alaron.MaxHp, alaron.Hp);
    }

    [Fact]
    public void ExperienceIsAwardedTimesOneAndAHalfAndTruncated()
    {
        var alaron = Alaron();

        alaron.AddExp(333);

        Assert.Equal(499u, alaron.SpendableExp);
        Assert.Equal(500u + 499, alaron.TotalExp);
        Assert.Equal(1, alaron.Level);
    }

    [Fact]
    public void ALevelRaisesTheLevelStatAndItsHitPoints()
    {
        var alaron = Alaron();
        alaron.TakeDamage(1);

        alaron.AddExp(5000);

        Assert.Equal(3, alaron.Level);
        Assert.Equal(500u + 7500, alaron.TotalExp);
        Assert.Equal(15000u, alaron.NextLevelExp);
        // The one point of damage stays: the level adds to the current value as well.
        Assert.Equal(2, alaron.Stats.Modified(Stat.Level));
    }

    [Fact]
    public void LevelsStopAtFortyWhereTheTableEnds()
    {
        var rules = Characters.Rules;

        Assert.Equal(0, rules.LevelFor(0));
        Assert.Equal(0, rules.LevelFor(499));
        Assert.Equal(1, rules.LevelFor(500));
        Assert.Equal(39, rules.LevelFor(11_069_999));
        Assert.Equal(40, rules.LevelFor(11_070_000));
        Assert.Equal(40, rules.LevelFor(uint.MaxValue));
        Assert.Null(rules.NextLevelExp(11_070_000));

        var alaron = Alaron();
        alaron.AddExp(10_000_000);
        Assert.Equal(40, alaron.Level);
    }

    [Fact]
    public void RaisingAStatStopsAtItsBaseCap()
    {
        var stats = Alaron().Stats;

        stats.Train(Stat.Intelligence, 20);

        Assert.Equal(30, stats.Trained(Stat.Intelligence));
        Assert.True(stats.IsCapped(Stat.Intelligence));
    }

    [Fact]
    public void LoweringAStatBelowZeroRaisesItToTheCapInstead()
    {
        // CharStats::AddBase tests the cap unsigned.
        var stats = Alaron().Stats;

        stats.Train(Stat.Intelligence, -20);

        Assert.Equal(30, stats.Trained(Stat.Intelligence));
    }

    [Fact]
    public void ModifiedValuesReadAsZeroBelowZeroAndAtMostTheirCap()
    {
        var stats = Alaron().Stats;

        stats.ChangeModified(Stat.Strength, -20);
        Assert.Equal(-5, stats.RawModified(Stat.Strength));
        Assert.Equal(0, stats.Modified(Stat.Strength));

        stats.ChangeModified(Stat.Strength, 50);
        Assert.Equal(40, stats.Modified(Stat.Strength));
    }

    [Fact]
    public void ASkillsModifiedLevelGainsWhatItsCappedBaseGains()
    {
        var skills = Characters.Create(Characters.Alaron() with { Skills = new SkillLevels(Thief: 13, Stealth: 8) }).Skills;
        skills.Modify(Skill.Stealth, 5);

        skills.Train(Skill.Stealth, 5);

        Assert.Equal(10, skills.RawBase(Skill.Stealth));
        Assert.Equal(15, skills.RawModified(Skill.Stealth));
        Assert.Equal(15, skills.Modified(Skill.Stealth));
        // Above the cap already: it reads as 10, and training can't raise it.
        Assert.Equal(10, skills.Base(Skill.Thief));
        Assert.True(skills.IsCapped(Skill.Thief));
    }

    [Fact]
    public void ASkillThatCantBeLearntStaysSoAndGearDoesntChangeIt()
    {
        var skills = Alaron().Skills;

        skills.Train(WeaponClass.Claw, 1);
        skills.Modify(WeaponClass.Claw, 3);

        Assert.Equal(CharacterSkills.CantLearn, skills.Base(WeaponClass.Claw));
        Assert.Equal(0, skills.Modified(WeaponClass.Claw));
    }

    [Fact]
    public void ArmourCanTakeASkillBelowZero()
    {
        var skills = Alaron().Skills;

        skills.Modify(Skill.Stealth, -3);

        Assert.Equal(-2, skills.RawModified(Skill.Stealth));
        Assert.Equal(0, skills.Modified(Skill.Stealth));
    }
}

public class TrainingTests
{
    private static readonly RecordId Keeper = RecordId.Hex(RecordTypes.Entity, 0x02D8);

    private static Trainer Teacher(SkillLevels skills, RecordId? id = null, KnownSpell[]? spells = null) =>
        Trainer.From(Characters.Trainer(id ?? Keeper, skills, spells), Characters.Rules, _ => Characters.Fireball);

    private static Character Student(uint exp = 100_000)
    {
        var alaron = Characters.Create(Characters.Alaron());
        alaron.SpendableExp = exp;
        return alaron;
    }

    [Fact]
    public void AfterABattleASkillCostsFullExperienceAndNoGold()
    {
        var alaron = Student();
        var gold = 0u;

        var quote = Training.TrainSkill(alaron, Skill.Warrior, null, ref gold);

        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 16 * 1000, 0), quote);
        Assert.Equal(4, alaron.Skills.Base(Skill.Warrior));
        Assert.Equal(100_000u - 16_000, alaron.SpendableExp);
    }

    [Fact]
    public void AfterABattleOnlySkillsAboveLevelZeroAreOffered()
    {
        var offered = Training.SkillsOffered(Student(), null).ToList();

        Assert.Equal(new SkillId[] { Skill.Stealth, Skill.Troubadour, Skill.Warrior, Skill.Wizard, WeaponClass.Hafted, WeaponClass.Missile,
            WeaponClass.Sword, SkillId.Shield }, offered);
    }

    [Fact]
    public void ATrainerTakesEightyPercentOfTheExperienceAndGold()
    {
        var alaron = Student();
        var gold = 1000u;

        var quote = Training.TrainSkill(alaron, Skill.Warrior, Teacher(new SkillLevels(Warrior: 8)), ref gold);

        // 16,000 × 0.8 in single precision is 12,800.
        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 12_800, 4 * 40), quote);
        Assert.Equal(1000u - 160, gold);
    }

    [Fact]
    public void ATrainerChargesTwoHundredGoldForTheFirstLevel()
    {
        var alaron = Student();
        var gold = 1000u;

        var quote = Training.TrainSkill(alaron, Skill.Healer, Teacher(new SkillLevels(Healer: 4)), ref gold);

        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 800, 200), quote);
        Assert.Equal(1, alaron.Skills.Base(Skill.Healer));
    }

    [Fact]
    public void OrianaTeachesForNothing()
    {
        var gold = 0u;

        var quote = Training.TrainSkill(Student(), Skill.Healer, Teacher(new SkillLevels(Healer: 4), Characters.Oriana), ref gold);

        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 800, 0), quote);
    }

    [Fact]
    public void ATrainerTeachesOnlyUpToTheirOwnLevel()
    {
        var gold = 1000u;

        var quote = Training.TrainSkill(Student(), Skill.Warrior, Teacher(new SkillLevels(Warrior: 3)), ref gold);

        Assert.Equal(TrainingRefusal.TrainerKnowsNoMore, quote.Refusal);
    }

    [Fact]
    public void ASkillThatCantBeLearntIsRefusedFirst()
    {
        var gold = 1000u;
        var student = Characters.Create(Characters.Alaron() with { Skills = new SkillLevels() });

        var quote = Training.TrainSkill(student, Skill.Healer, Teacher(new SkillLevels(Healer: 4)), ref gold);

        Assert.Equal(TrainingRefusal.CantLearn, quote.Refusal);
    }

    [Fact]
    public void ATrainerNeedsBothTheExperienceAndTheGold()
    {
        var gold = 199u;

        var quote = Training.TrainSkill(Student(), Skill.Healer, Teacher(new SkillLevels(Healer: 4)), ref gold);

        Assert.Equal(new TrainingQuote(TrainingRefusal.CantAfford, 800, 200), quote);
        Assert.Equal(199u, gold);
    }

    [Fact]
    public void AttributesCostTheirMultiplierTimesTheNextValueSquared()
    {
        var alaron = Student();

        var quote = Training.TrainStat(alaron, Stat.Dexterity);

        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 16 * 16 * 24, 0), quote);
        Assert.Equal(16, alaron.Stats.Trained(Stat.Dexterity));
        Assert.Equal(TrainingRefusal.CantLearn, Training.QuoteStat(alaron, Stat.Level).Refusal);
        Assert.Equal(TrainingRefusal.CantAfford, Training.QuoteStat(Student(exp: 100), Stat.Stamina).Refusal);
    }

    [Fact]
    public void ANewSpellComesFromATrainerOfTheRightSchoolToAGoodEnoughWizard()
    {
        var niesen = Characters.Create(Characters.Alaron() with { School = MagicSchool.Elemental, Skills = new SkillLevels(Wizard: 3) });
        niesen.SpendableExp = 1000;
        var gold = 1000u;
        var fireball = Characters.Fireball;
        var trainer = Teacher(new SkillLevels(Wizard: 5), spells: [new KnownSpell(fireball.Id, 5)]);

        Assert.Equal(TrainingRefusal.CantLearn, Training.QuoteSpell(niesen, fireball, null, gold).Refusal);
        var quote = Training.TrainSpell(niesen, fireball, trainer, ref gold, out var learnt);

        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 160, 200), quote);
        Assert.True(learnt);
        Assert.Equal(1, niesen.FindSpell(fireball.Id)!.Level);

        var star = Characters.Spell(0x0330, MagicSchool.Star, wizard: 1, cost: 100);
        Assert.Equal(TrainingRefusal.WrongSchool, Training.QuoteSpell(niesen, star, trainer, gold).Refusal);
        var hard = Characters.Spell(0x0331, MagicSchool.None, wizard: 4, cost: 100);
        Assert.Equal(TrainingRefusal.WizardTooLow, Training.QuoteSpell(niesen, hard, trainer, gold).Refusal);
    }

    [Fact]
    public void ASpellStopsAtLevelTenAndATrainersLevel()
    {
        var alaron = Character.Create(Characters.Alaron(spells: [new KnownSpell(Characters.Fireball.Id, 4)]), Characters.Rules,
            _ => Characters.Fireball);
        alaron.SpendableExp = 1_000_000;
        var gold = 0u;

        Assert.Equal(TrainingRefusal.TrainerKnowsNoMore,
            Training.QuoteSpell(alaron, Characters.Fireball, Teacher(new SkillLevels(), spells: [new KnownSpell(Characters.Fireball.Id, 4)]), gold).Refusal);
        var quote = Training.TrainSpell(alaron, Characters.Fireball, null, ref gold, out var learnt);
        Assert.Equal(new TrainingQuote(TrainingRefusal.None, 25 * 200, 0), quote);
        Assert.False(learnt);

        for (var i = 0; i < 10; i++)
            Training.TrainSpell(alaron, Characters.Fireball, null, ref gold, out _);
        Assert.Equal(10, alaron.FindSpell(Characters.Fireball.Id)!.Level);
        Assert.Equal(TrainingRefusal.AtMost, Training.QuoteSpell(alaron, Characters.Fireball, null, gold).Refusal);
    }
}

public class PartyTests
{
    private static CharacterParty Party(params EntityRecord[] members)
    {
        var party = new CharacterParty();
        foreach (var member in members)
            Assert.True(party.Add(Characters.Create(member)));
        return party;
    }

    private static EntityRecord Member(int id, SkillLevels skills, Attributes? attributes = null) => Characters.Alaron() with
    {
        Id = RecordId.Hex(RecordTypes.Entity, id), Skills = skills, Attributes = attributes ?? new Attributes(18, 12, 15, 15, 15, 30),
    };

    [Fact]
    public void ThePartyHoldsFourAndPacksItsSlotsWhenOneLeaves()
    {
        var party = Party(Member(1, new()), Member(2, new()), Member(3, new()), Member(4, new()));

        Assert.False(party.Add(Characters.Create(Member(5, new()))));
        Assert.True(party.Remove(RecordId.Hex(RecordTypes.Entity, 2)));

        Assert.Equal([1, 3, 4], party.Slots.Take(3).Select(m => int.Parse(m!.Entity.Key[2..], System.Globalization.NumberStyles.HexNumber)));
        Assert.Null(party.Slots[3]);
    }

    [Fact]
    public void TheMostSkilledIsTheLaterOnATieAndCanBeAtZero()
    {
        var party = Party(Member(1, new(Healer: 3)), Member(2, new(Healer: 3)), Member(3, new(Healer: 1)));

        Assert.Equal(RecordId.Hex(RecordTypes.Entity, 2), party.MostSkilled(Skill.Healer)!.Entity);
        Assert.Equal(RecordId.Hex(RecordTypes.Entity, 3), party.MostSkilled(Skill.Thief)!.Entity);
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(25, 1)]
    [InlineData(26, 2)]
    [InlineData(61, 3)]
    [InlineData(111, 7)]
    [InlineData(265, 20)]
    [InlineData(266, 21)]
    [InlineData(1000, 21)]
    public void WhatARollLeavesIsASkillCheckStep(int value, int step) => Assert.Equal(step, CharacterParty.SkillCheckStep((short)value));

    [Fact]
    public void AnAlchemistCheckIsD100LessTheBestAlchemistsBonus()
    {
        // Intelligence 18 × 3 + Alchemist 5 × 6 = 84; a roll of 100 leaves 16, a step of 1.
        var party = Party(Member(1, new(Alchemist: 5)), Member(2, new(Alchemist: 2)));

        Assert.Equal(1, party.SkillCheck(Skill.Alchemist, Characters.D100(100)));
        // A member who can't learn it still counts, at 0; only an empty party rolls nothing.
        Assert.Equal(3, Party(Member(1, new())).SkillCheck(Skill.Alchemist, Characters.D100(100)));
        Assert.Equal(0, new CharacterParty().SkillCheck(Skill.Alchemist, Characters.D100()));
    }

    [Fact]
    public void ALoremasterCheckPoolsTheLivingPartysLevels()
    {
        // Best Intelligence 30 × 3 + (4 + 6) × 6 = 150; a roll of 10 leaves 140, a step of 9.
        var party = Party(Member(1, new(Loremaster: 4)), Member(2, new(Loremaster: 6), new Attributes(30, 10, 10, 10, 10, 10)));

        Assert.Equal(9, party.SkillCheck(Skill.Loremaster, Characters.D100(10)));
        // A roll at or over the bonus gives 0.
        Assert.Equal(0, Party(Member(1, new(Loremaster: 0), new Attributes(1, 1, 1, 1, 1, 1))).SkillCheck(Skill.Loremaster, Characters.D100(3)));
    }

    [Fact]
    public void ADiplomatCheckRollsAgainWhenTheFirstPasses()
    {
        // 18 × 3 + 2 × 10 + 50 = 124.
        var party = Party(Member(1, new(Diplomat: 2)));

        Assert.Equal(2, party.SkillCheck(Skill.Diplomat, Characters.D100(100)));
        // 124 − 3 = 121 is step 7, so it rolls again: 124 − 50 = 74, step 4.
        Assert.Equal(4, party.SkillCheck(Skill.Diplomat, Characters.D100(3, 50)));
    }

    [Fact]
    public void TheStealthCheckIsAStub() => Assert.Equal(0, Party(Member(1, new(Stealth: 10))).SkillCheck(Skill.Stealth, Characters.D100()));

    [Fact]
    public void StatQuestionsReadModifiedValues()
    {
        var party = Party(Member(1, new()), Member(2, new(), new Attributes(30, 5, 15, 15, 15, 30)));
        party.Slots[1]!.Stats.ChangeModified(Stat.Intelligence, 5);

        Assert.Equal(35, party.BestStat(Stat.Intelligence));
        Assert.Equal(5, party.WorstStat(Stat.Willpower));
        Assert.Equal(35, party.MemberStat(RecordId.Hex(RecordTypes.Entity, 2), Stat.Intelligence));
        Assert.Equal(0, party.MemberStat(RecordId.Hex(RecordTypes.Entity, 9), Stat.Intelligence));
    }

    [Fact]
    public void TheAverageLevelCountsDamage()
    {
        var party = Party(Member(1, new()) with { Level = 5 }, Member(2, new()) with { Level = 8 });
        party.Slots[1]!.TakeDamage(4);

        Assert.Equal((5 + 4) / 2, party.AverageLevel);
    }
}
