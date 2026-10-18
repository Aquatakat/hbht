using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// Spells, potions and the magic record as the importer writes them (src/Aidyn.Rom/Import/MagicImporter.cs, whose real-data tests
/// check them against the ROM), for the rules the core's tests build by hand.
/// </summary>
internal static class MagicData
{
    private static RecordId Stat(Aidyn.Core.Characters.Stat stat) => StatId.Record(stat);

    private static RecordId SkillOf(Skill skill) => new SkillId((int)skill).Record;

    public static readonly EffectDuration Cast = new(PerLevel: 1800);

    public static readonly MagicRecord Record = new(RecordId.Decimal(RecordTypes.Magic, 0),
        new CastChance(4, 6, 1, 5, [0.75f, 1.25f, 1.25f, 0.75f, 0.75f], [0.5f, 0.75f, 1.25f, 1.5f]),
        new ResistRoll(3, 1, 1, 0.75f, 1.25f), RoundTicks: 600, MostDisabled: 2, Improvement: 0.1f, MaxLevel: 10,
        KeptAfterBattle: [Potions.Id(Items.Potion.Stealth)]);

    public static SpellRecord Spell(int id, string name, MagicSchool school, IReadOnlyList<Effect> effects, EffectDuration? duration = null,
        SpellTargetType type = SpellTargetType.Party, SpellTargetCount count = SpellTargetCount.ByLevel, Resisted resisted = Resisted.Never,
        int stamina = 6, Reagent reagent = Reagent.Spice, int? ai = null, int position = 0, SpellSpecial special = SpellSpecial.None) =>
        new(RecordId.Hex(RecordTypes.Spell, id), name, school, 0, stamina, count, type, 1, special, 3, reagent, 100, 0,
            Effects: effects, Duration: duration, Resisted: resisted, AiValue: ai, Position: position);

    public static readonly SpellRecord Endurance = Spell(0x0310, "Endurance", MagicSchool.Naming,
        [new StatEffect(Stat(Aidyn.Core.Characters.Stat.Endurance), Magnitude.Per(2))], Cast, ai: 52, position: 19);

    public static readonly SpellRecord Weakness = Spell(0x0313, "Weakness", MagicSchool.Naming,
        [new StatEffect(Stat(Aidyn.Core.Characters.Stat.Endurance), Magnitude.Per(-2))], Cast, SpellTargetType.Enemy, resisted: Resisted.Always, ai: 32);

    public static readonly SpellRecord Exhaustion = Spell(0x0318, "Exhaustion", MagicSchool.Necromancy,
        [new StatEffect(Stat(Aidyn.Core.Characters.Stat.Stamina), Magnitude.Per(-2))], Cast, SpellTargetType.Enemy, resisted: Resisted.Always, ai: 34);

    public static readonly SpellRecord Clumsiness = Spell(0x032A, "Clumsiness", MagicSchool.Star,
        [new StatEffect(Stat(Aidyn.Core.Characters.Stat.Dexterity), Magnitude.Per(-2))], Cast, SpellTargetType.Enemy, resisted: Resisted.Always, ai: 35);

    public static readonly SpellRecord Poison = Spell(0x0332, "Poison", MagicSchool.None, [new PoisonEffect(Magnitude.Per(0.5f))], Cast,
        SpellTargetType.Enemy, SpellTargetCount.One, Resisted.Always, ai: 21, special: SpellSpecial.Heavy);

    public static readonly SpellRecord RemovePoison = Spell(0x0338, "Remove Poison", MagicSchool.Elemental,
        [new CureEffect([Poison.Id], BeatsMargin: true)], count: SpellTargetCount.One, ai: 83);

    public static readonly SpellRecord CheatDeath = Spell(0x033F, "Cheat Death", MagicSchool.Necromancy, [new CheatDeathEffect()], Cast,
        count: SpellTargetCount.One, ai: 82);

    public static readonly SpellRecord SpiritShield = Spell(0x031C, "Spirit Shield", MagicSchool.Necromancy,
        [new ProtectionEffect(Magnitude.Per(2), Magnitude.Per(1))], Cast, count: SpellTargetCount.All, ai: 61);

    public static readonly SpellRecord AirShield = Spell(0x0300, "Air Shield", MagicSchool.Elemental,
        [new ProtectionEffect(Magnitude.Per(2))], Cast, count: SpellTargetCount.All, ai: 61);

    public static readonly SpellRecord Haste = Spell(0x0317, "Haste", MagicSchool.Necromancy, [new FieldRadiusEffect(Magnitude.Per(1))], Cast,
        count: SpellTargetCount.All, ai: 73);

    public static readonly SpellRecord StellarGravity = Spell(0x032F, "Stellar Gravity", MagicSchool.Star,
        [new FieldRadiusEffect(Magnitude.Per(-1))], Cast, SpellTargetType.Enemy, SpellTargetCount.One, Resisted.Always, ai: 41);

    public static readonly SpellRecord WebOfStarlight = Spell(0x0330, "Web Of Starlight", MagicSchool.Star,
        [new ImmobiliseEffect(2), new DamagePerRoundEffect(new Dice(2))], Cast, SpellTargetType.Enemy, SpellTargetCount.One, Resisted.Always,
        ai: 43);

    public static readonly SpellRecord WraithTouch = Spell(0x0333, "Wraith Touch", MagicSchool.Necromancy,
        [new DrainStatEffect([Stat(Aidyn.Core.Characters.Stat.Intelligence), Stat(Aidyn.Core.Characters.Stat.Willpower), Stat(Aidyn.Core.Characters.Stat.Dexterity),
            Stat(Aidyn.Core.Characters.Stat.Strength)], new Dice(2))], new EffectDuration(Battle: true), SpellTargetType.Enemy, SpellTargetCount.One,
        Resisted.Always, ai: 36);

    public static readonly SpellRecord DispelNaming = Spell(0x0322, "Dispel Naming", MagicSchool.None, [new DispelEffect(MagicSchool.Naming)],
        type: SpellTargetType.Area, count: SpellTargetCount.All, ai: 81);

    public static readonly SpellRecord Photosynthesis = Spell(0x0339, "Photosynthesis", MagicSchool.Star, [new HealPerRoundEffect()], Cast,
        ai: 83);

    public static readonly SpellRecord VsNaming = Spell(0x031E, "vs. Naming", MagicSchool.None,
        [new WardEffect(MagicSchool.Naming, Magnitude.Per(6))], Cast, ai: 71);

    public static readonly SpellRecord Weakening2 = Spell(0x0302, "Debilitation", MagicSchool.Elemental,
        [new StatEffect(Stat(Aidyn.Core.Characters.Stat.Strength), Magnitude.Per(-2))], Cast, SpellTargetType.Enemy, resisted: Resisted.Always, ai: 31);

    public static readonly SpellRecord[] Spells =
    [
        Endurance, Weakness, Exhaustion, Clumsiness, Poison, RemovePoison, CheatDeath, SpiritShield, AirShield, Haste, StellarGravity,
        WebOfStarlight, WraithTouch, DispelNaming, Photosynthesis, VsNaming, Weakening2,
    ];

    private static readonly EffectDuration PotionTime = new(Flat: 648000);

    /// <summary>A potion's effects and duration, as the importer writes them.</summary>
    public static (IReadOnlyList<Effect>? Effects, EffectDuration? Duration) Potion(Items.Potion potion) => potion switch
    {
        Items.Potion.FireFlask => ([new BlastEffect(new Dice(4))], null),
        Items.Potion.InfernoFlask => ([new BlastEffect(new Dice(8))], null),
        Items.Potion.SleepGas => ([new BlastEffect(new Dice(0, 6, 15), Stamina: true)], null),
        Items.Potion.AcidFlask => ([new BlastEffect(new Dice(2)), new CorrodeEffect(new Dice(2))], null),
        Items.Potion.Healing => ([new HealEffect(15, 25)], null),
        Items.Potion.Curing => ([new HealEffect(40, 60)], null),
        Items.Potion.Stamina => ([new CureEffect([Exhaustion.Id]), new StaminaEffect(Magnitude.Fixed(0), ToBase: true)], null),
        Items.Potion.Antidote => ([new CureEffect([Poison.Id])], null),
        Items.Potion.Restore => ([new CureEffect([WraithTouch.Id, Weakening2.Id, Weakness.Id, Exhaustion.Id,
            RecordId.Hex(RecordTypes.Spell, 0x0336), Clumsiness.Id])], null),
        Items.Potion.Strength => ([new StatEffect(Stat(Aidyn.Core.Characters.Stat.Strength), Magnitude.Fixed(10))], PotionTime),
        Items.Potion.Dexterity => ([new StatEffect(Stat(Aidyn.Core.Characters.Stat.Dexterity), Magnitude.Fixed(10))], PotionTime),
        Items.Potion.Clarity => ([new SkillEffect(SkillOf(Skill.Loremaster), Magnitude.Fixed(3))], PotionTime),
        Items.Potion.Charisma => ([new SkillEffect(SkillOf(Skill.Diplomat), Magnitude.Fixed(3))], PotionTime),
        Items.Potion.Defence => ([new ProtectionEffect(Magnitude.Fixed(15))], PotionTime),
        Items.Potion.Stealth => ([new SkillEffect(SkillOf(Skill.Stealth), Magnitude.Fixed(3))], PotionTime),
        _ => (null, null),
    };

    public static readonly MagicRules Rules = new(Record, Spells, Enum.GetValues<Items.Potion>().Select(PotionData.Of));
}
