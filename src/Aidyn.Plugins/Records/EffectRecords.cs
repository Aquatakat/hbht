using System.Text.Json.Serialization;

namespace Aidyn.Plugins.Records;

/// <summary>
/// How big an effect is at a cast's level: <c>flat + perLevel × level</c>, truncated (ADR 0020). The level is the cast's
/// effective level: the lower of the spell's level and the caster's Wizard, an item's own level, or a potion's, 0.
/// </summary>
public sealed record Magnitude(int Flat = 0, float PerLevel = 0)
{
    public int At(int level) => Flat + (int)(PerLevel * level);

    public static Magnitude Fixed(int amount) => new(amount);

    public static Magnitude Per(float perLevel) => new(0, perLevel);
}

/// <summary>Dice: <c>Count</c>d<c>Sides</c>, plus <c>Flat</c>.</summary>
public sealed record Dice(int Count, int Sides = 6, int Flat = 0);

/// <summary>Whom an effect is for, when a spell can be cast at either side (Wind's two faces).</summary>
public enum EffectFor
{
    /// <summary>Whoever the spell takes.</summary>
    Any,
    /// <summary>A target on the caster's side, or the caster.</summary>
    Allies,
    /// <summary>A target on the other side.</summary>
    Opponents,
}

/// <summary>
/// How long a spell's or potion's effects last, one timer for all of them, as the original keeps one a spell (<c>EffectRecord</c>,
/// <c>+0x08</c>). A record with none leaves nothing on its target: its effects happen once. Timers run in the game clock's ticks:
/// a combat round takes off 600 (<see cref="MagicRecord.RoundTicks"/>), trek the clock's.
/// </summary>
/// <param name="PerLevel">Ticks for each level of the cast: 1800 for a spell.</param>
/// <param name="Flat">Ticks whatever the level: 648,000 for a potion, 3 hours of game time.</param>
/// <param name="Battle">It lasts until the battle ends, never running down: Wraith Touch.</param>
public sealed record EffectDuration(int PerLevel = 0, uint Flat = 0, bool Battle = false)
{
    public uint At(int level) => Battle ? uint.MaxValue : Flat + (uint)(PerLevel * level);
}

/// <summary>
/// One effect of a spell, a potion or an enchantment (ADR 0020): a type the core implements, with its parameters. A record lists
/// them in <c>effects</c>, each written with its <c>type</c>. Any spell, potion or flask may carry any type, which lands the same
/// way whatever carries it (ADR 0021). A type no build knows loads as a plain <see cref="Effect"/>, which does nothing and is
/// reported.
/// </summary>
/// <param name="For">Whom it's for, when the spell can take either side.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(StatEffect), "stat")]
[JsonDerivedType(typeof(SkillEffect), "skill")]
[JsonDerivedType(typeof(ProtectionEffect), "protection")]
[JsonDerivedType(typeof(WardEffect), "ward")]
[JsonDerivedType(typeof(DamageEffect), "damage")]
[JsonDerivedType(typeof(DamagePerRoundEffect), "damagePerRound")]
[JsonDerivedType(typeof(PoisonEffect), "poison")]
[JsonDerivedType(typeof(HealPerRoundEffect), "healPerRound")]
[JsonDerivedType(typeof(HealEffect), "heal")]
[JsonDerivedType(typeof(ImmobiliseEffect), "immobilise")]
[JsonDerivedType(typeof(CharmEffect), "charm")]
[JsonDerivedType(typeof(FieldRadiusEffect), "fieldRadius")]
[JsonDerivedType(typeof(MoraleEffect), "morale")]
[JsonDerivedType(typeof(StaminaEffect), "stamina")]
[JsonDerivedType(typeof(WeatherEffect), "weather")]
[JsonDerivedType(typeof(CureEffect), "cure")]
[JsonDerivedType(typeof(DispelEffect), "dispel")]
[JsonDerivedType(typeof(PartOfDayEffect), "partOfDay")]
[JsonDerivedType(typeof(CheatDeathEffect), "cheatDeath")]
[JsonDerivedType(typeof(ReflectEffect), "reflect")]
[JsonDerivedType(typeof(DrainStatEffect), "drainStat")]
[JsonDerivedType(typeof(BanishEffect), "banish")]
[JsonDerivedType(typeof(FleeEffect), "flee")]
[JsonDerivedType(typeof(TeleportEffect), "teleport")]
[JsonDerivedType(typeof(SenseAuraEffect), "senseAura")]
[JsonDerivedType(typeof(RevealPhaseEffect), "revealPhase")]
[JsonDerivedType(typeof(BlastEffect), "blast")]
[JsonDerivedType(typeof(CorrodeEffect), "corrode")]
[JsonDerivedType(typeof(LuaEffect), "lua")]
public record Effect
{
    [JsonPropertyOrder(1)]
    public EffectFor For { get; init; }
}

/// <summary>
/// Changes a stat's base and modified values while it lasts (<c>Entity.AddStat</c>, <c>CharStats::AddModdedMagic</c>), so
/// Endurance's changes the maximum hit points too. It can't take Endurance, or the Level while Endurance is 0, below 1, nor
/// change a dead character's. The ten stat spells (2 a level) and the Strength and Dexterity potions (10).
/// </summary>
/// <param name="Stat">One of <c>aidyn:stat/0</c>–<c>6</c>.</param>
public sealed record StatEffect(RecordId Stat, Magnitude Amount) : Effect;

/// <summary>Changes a skill's modified level while it lasts. The Stealth spell (6 a level) and the Clarity, Charisma and Stealth potions (3).</summary>
/// <param name="Skill">One of <c>aidyn:skill/0</c>–<c>11</c>.</param>
public sealed record SkillEffect(RecordId Skill, Magnitude Amount) : Effect;

/// <summary>
/// Changes protection (the sheet's, <c>entity+0x14</c>'s <c>+2</c>) while it lasts. The shields (2 a level), the Defence potion (15)
/// and the Acid flask's corrosion, which <see cref="CorrodeEffect"/> sizes.
/// </summary>
/// <param name="Leaves">
/// What stays when it ends. The original's Spirit and Starlight Shield take off only a level of the two they put on
/// (<c>0x800707EC</c>), so each cast leaves a level of protection for good; the user kept it.
/// </param>
public sealed record ProtectionEffect(Magnitude Amount, Magnitude? Leaves = null) : Effect;

/// <summary>Adds to the target's resist roll against one school's spells while it lasts (<c>Combatant.ResistBonusFromEffects</c>). The four vs. spells, 6 a level.</summary>
public sealed record WardEffect(MagicSchool School, Magnitude Amount) : Effect;

/// <summary>
/// Damage, once: (the cast's margin + <see cref="Dice"/>) d6, times the target's resistances to the spell
/// (<c>Combatant.SpellDamage</c>). The spell's school and aspect always meet resistances; these elements add to them.
/// </summary>
/// <param name="Dice">The dice over the margin: the damage byte (Lightning's 2), or a level each (Fireball's).</param>
public sealed record DamageEffect(Magnitude Dice, IReadOnlyList<Element> Elements) : Effect;

/// <summary>Damage each round while it lasts, no more than the target's hit points (<c>Entity.AgeEffects</c>). Immolation and Acid Bolt 1d6, Web of Starlight 2d6.</summary>
public sealed record DamagePerRoundEffect(Dice Dice) : Effect;

/// <summary>Takes Endurance each round while it lasts, up to what it has, and not while hit points are below it. Poison, half a level.</summary>
public sealed record PoisonEffect(Magnitude Amount) : Effect;

/// <summary>
/// Heals the cast's margin each round while it lasts, and each time trek ages it; by day only if <see cref="DayOnly"/>.
/// Photosynthesis, and the Ring of Healing worn.
/// </summary>
public sealed record HealPerRoundEffect(bool DayOnly = true) : Effect;

/// <summary>Heals a share of the maximum hit points once, drawn whole from <c>Low</c>–<c>High</c> percent (<c>Entity::HealByPotion</c>). The Healing and Curing potions.</summary>
public sealed record HealEffect(int Low, int High) : Effect;

/// <summary>
/// The target can't act while it lasts, and counts toward the most enemies disabled at once (<see cref="MagicRecord.MostDisabled"/>).
/// Wall of Bones, Frozen Doom, Web of Starlight.
/// </summary>
/// <param name="Escape">
/// Each round the target escapes, ending the effect, when a d100 is under this times its Strength (Web of Starlight's 2), or
/// never with 0.
/// </param>
/// <param name="Immune">Entities it never holds: the Shadow.</param>
public sealed record ImmobiliseEffect(int Escape = 0, IReadOnlyList<RecordId>? Immune = null) : Effect;

/// <summary>The target fights for the other side while it lasts, and counts toward <see cref="MagicRecord.MostDisabled"/>. Charming and the three Controls.</summary>
public sealed record CharmEffect : Effect;

/// <summary>Changes the field's radius while it lasts, at least 1 and at most 13 (<c>Combatant.UpdateRadius</c>). Haste (a level) and Stellar Gravity (less a level).</summary>
public sealed record FieldRadiusEffect(Magnitude Amount) : Effect;

/// <summary>Changes an enemy's morale while it lasts, by no more than it has. Aura of Death and Solar Wrath (less a level).</summary>
public sealed record MoraleEffect(Magnitude Amount) : Effect;

/// <summary>
/// Moves Stamina, once. A loss takes no more than the target has; a gain restores up to its base, or with
/// <see cref="ToBase"/> all the way. Tap Stamina's two halves (2 a level), the Stamina potion.
/// </summary>
/// <param name="ToCaster">It's for the caster, not the target: Tap Stamina's gain. The caster gets the spell's record too.</param>
/// <param name="RaisesMaximum">
/// It raises the base and modified Stamina, uncapped, and nothing takes it back: Wind on an ally, as the original does
/// (<c>Entity.AddStaminaEffect</c>, <c>0x80072158</c>); the user kept it.
/// </param>
public sealed record StaminaEffect(Magnitude Amount, bool ToBase = false, bool ToCaster = false, bool RaisesMaximum = false) : Effect;

/// <summary>Sets combat's weather class as it ends (<c>World+4</c>): Wind on an ally, 1.</summary>
public sealed record WeatherEffect(int AtEnd) : Effect;

/// <summary>
/// Ends the target's effects of the named spells, once. Remove Poison and the Antidote (Poison), Restore (the six weakening spells)
/// and the Stamina potion (Exhaustion).
/// </summary>
/// <param name="BeatsMargin">Only those cast with a lower margin than this cast's: Remove Poison.</param>
public sealed record CureEffect(IReadOnlyList<RecordId> Spells, bool BeatsMargin = false) : Effect;

/// <summary>
/// Ends every effect of one school on the target, once, if the cast's margin times their number reaches the sum of their margins
/// (<c>Entity.Dispel</c>). Unschooled effects can't be dispelled. The four dispels.
/// </summary>
public sealed record DispelEffect(MagicSchool School) : Effect;

/// <summary>Holds the battle at night or by day while it lasts, ending the other's effects on everyone (<c>Entity.SetLight</c>). Darkness and Light.</summary>
public sealed record PartOfDayEffect(bool Night) : Effect;

/// <summary>When a hit would kill the target, it heals twice the cast's margin instead, and the effect ends. Cheat Death.</summary>
public sealed record CheatDeathEffect : Effect;

/// <summary>Adds to the target's counter, which a resist roll spends to turn a spell away (<c>Combatant</c> <c>+0x28</c>). Mirror, 3 a level.</summary>
public sealed record ReflectEffect(Magnitude Amount) : Effect;

/// <summary>Takes one of <see cref="Stats"/>, drawn, by <see cref="Dice"/> from its base and modified values while it lasts. Wraith Touch: Intelligence, Willpower, Dexterity or Strength by 2d6.</summary>
public sealed record DrainStatEffect(IReadOnlyList<RecordId> Stats, Dice Dice) : Effect;

/// <summary>Damage of (the cast's margin + the target's hit points) d6, once: Banishing.</summary>
public sealed record BanishEffect : Effect;

/// <summary>The target leaves the battle as one who flees, if the battle allows fleeing. Escape.</summary>
public sealed record FleeEffect : Effect;

/// <summary>The caster moves to the placed point, if no one stands there. Teleportation.</summary>
public sealed record TeleportEffect : Effect;

/// <summary>Shows the target's details, more at a higher level. Sense Aura.</summary>
public sealed record SenseAuraEffect : Effect;

/// <summary>Shows the moon's or the sun's phase on the HUD. Detect Moon Phase and Detect Sun Phase.</summary>
public sealed record RevealPhaseEffect(bool Sun) : Effect;

/// <summary>
/// A blast, once (<c>Combatant.FlaskDamage</c>, <c>FlaskExplosion</c>): the dice, then each combatant takes them over its squared
/// distance from where it bursts, from hit points, or from Stamina with <see cref="Stamina"/>. A flask's bursts where it lands, with
/// the dice its throw rolled; a spell's or a drunk potion's about its target, rolled as it lands. The four flasks.
/// </summary>
public sealed record BlastEffect(Dice Dice, bool Stamina = false) : Effect;

/// <summary>
/// A corrosion, bursting with any blast: a radius drawn, and each combatant within it takes a protection effect of the radius over
/// its squared distance, until the battle ends, instead of the blast's share. The Acid flask.
/// </summary>
public sealed record CorrodeEffect(Dice Radius) : Effect;

/// <summary>Reserved for Phase 5's scripts (ADR 0008): a handler called with the cast. It does nothing until then, and loading reports it.</summary>
public sealed record LuaEffect(string Script, string Handler) : Effect;

/// <summary>Who a spell may be cast at, beyond its side and requirement: Banishing and Control Elem the elementals, Charming no Chaos.</summary>
/// <param name="Entities">Only these.</param>
/// <param name="NotCategories">None of these categories.</param>
public sealed record TargetFilter(IReadOnlyList<RecordId>? Entities = null, IReadOnlyList<EntityCategory>? NotCategories = null);

/// <summary>Who resists a spell (<c>Combatant.IsResistable</c>).</summary>
public enum Resisted
{
    /// <summary>No one: a buff, a dispel, Teleportation.</summary>
    Never,
    /// <summary>Every target: a spell at enemies, or a placed area such as Fireball.</summary>
    Always,
    /// <summary>A target on the other side: Wind.</summary>
    Opponents,
}

/// <summary>
/// Casting's numbers, which combat's rules take (<c>aidyn:magic/0</c>, ADR 0020): the cast's chance, the resist roll, a round's
/// ageing, and the rest.
/// </summary>
/// <param name="Chance">The chance to cast (<c>Combatant.CastChance</c>, <c>0x80063CA0</c>).</param>
/// <param name="Resist">The resist roll (<c>Combatant.ResistRoll</c>, <c>0x80064298</c>).</param>
/// <param name="RoundTicks">What a combat round takes off a timer (<c>EffectRecord.Age</c>).</param>
/// <param name="MostDisabled">
/// The most enemies charmed or held at once while reserves are still to come: a cast by the party's side past it fails
/// (<c>Combatant.CheckDisableLimit</c>, <c>0x80063A00</c>).
/// </param>
/// <param name="Improvement">
/// A cast may raise a low spell's level: a d100 at or under (5 − level)², 3 from level 4, halved on a failure, times this
/// (<c>Combatant.ResolveCastRoll</c>).
/// </param>
/// <param name="MaxLevel">A spell's highest level.</param>
/// <param name="KeptAfterBattle">Potions whose effects outlast a battle (<c>Entity.ClearEffects</c>): Stealth.</param>
/// <param name="SpellEffects">A character's room for spells' effects (<c>entity+0x34</c>, 15).</param>
/// <param name="PotionEffects">A character's room for potions' effects (<c>entity+0x38</c>, 7).</param>
/// <param name="Refused">
/// The actor a refused or failed cast plays at its caster, whatever the spell (<c>CombatSpellFx.AddVisual</c>'s role 3,
/// <c>0x8008B8D8</c>).
/// </param>
/// <param name="Resisted">The actor a resisted cast plays at its target (role 4, <c>0x8008B8E0</c>).</param>
public sealed record MagicRecord(
    RecordId Id,
    CastChance Chance,
    ResistRoll Resist,
    int RoundTicks,
    int MostDisabled,
    float Improvement,
    int MaxLevel,
    IReadOnlyList<RecordId> KeptAfterBattle,
    int SpellEffects = 15,
    int PotionEffects = 7,
    RecordId? Refused = null,
    RecordId? Resisted = null);

/// <summary>
/// What a spell or potion shows in a battle (<c>combatspellvisuals.cpp</c>, the role tables of <c>CombatSpellFx.AddVisual</c>):
/// actors that play their cast (request 5) once at the caster's and the target's feet, and a scene that shows over the
/// target's head in turn with the battle's others while the effect lasts. Any may be missing. See docs/systems/combat.md.
/// </summary>
/// <param name="Caster">A spell's actor at its caster (role 0, <c>CombatSpellFx.CasterActor</c>, <c>0x8008CB40</c>).</param>
/// <param name="Target">
/// A spell's actor at its target (role 1, <c>CombatSpellFx.TargetActor</c>, <c>0x8008CCE0</c>), or a potion's at the one who
/// drinks it or a flask's at each one its blast hurts (role 2, <c>CombatSpellFx.PotionActor</c>, <c>0x8008D038</c>).
/// </param>
/// <param name="TargetAtHead">
/// The target's actor stands at the height of the target's entity rather than at its feet (<c>CombatSpellFx.TargetAtHead</c>,
/// <c>0x8008CCA8</c>): the charms, holds and drains.
/// </param>
/// <param name="Aura">
/// The scene shown at the target's locator 3 while the effect lasts (roles 5 and 6, <c>CombatSpellFx.AuraScene</c>,
/// <c>0x8008CE78</c>, and <c>PotionAura</c>, <c>0x8008D0FC</c>).
/// </param>
/// <param name="CasterMemory">
/// The memory the caster's actor asks for, in bytes: the original skips an actor when the heap's largest free block is
/// smaller. The port loads every actor.
/// </param>
/// <param name="TargetMemory">The memory the target's actor asks for.</param>
public sealed record MagicVisuals(
    RecordId? Caster = null,
    RecordId? Target = null,
    bool TargetAtHead = false,
    RecordId? Aura = null,
    int CasterMemory = 0,
    int TargetMemory = 0);

/// <summary>
/// The chance to cast: (Intelligence × <c>Intelligence</c> + the cast's level × <c>Level</c> + the character's level ×
/// <c>CharacterLevel</c>) × a factor, truncated, at least <c>Minimum</c>. A Lunar caster's factor is by the moon's phase,
/// anyone else's by the part of the day.
/// </summary>
public sealed record CastChance(int Intelligence, int Level, int CharacterLevel, int Minimum,
    IReadOnlyList<float> ByPartOfDay, IReadOnlyList<float> ByMoon);

/// <summary>
/// The resist roll's target: Willpower × <c>Willpower</c> + Endurance × <c>Endurance</c> + level × <c>Level</c>, plus wards; a
/// Solar or Lunar spell's is times <c>SameAspect</c> or <c>OtherAspect</c> by the target's aspect against the caster's.
/// </summary>
public sealed record ResistRoll(int Willpower, int Endurance, int Level, float SameAspect, float OtherAspect);
