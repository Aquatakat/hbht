using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Magic;

/// <summary>
/// A spell, potion or flask landing on one target (ADR 0021): whose effects they are, at what level and margin, who sent them and
/// whom they land on.
/// </summary>
/// <param name="Source">The spell or potion, which a lasting record keeps.</param>
/// <param name="School">The spell's school, which a lasting record keeps for a dispel to match; a potion's is none.</param>
/// <param name="Level">The cast's effective level, which the magnitudes are at; an item's own; a potion's 0.</param>
/// <param name="Margin">
/// The cast's margin after the target's resist step, which a dispel and a cure that beats margins weigh; a potion's or flask's
/// <see cref="Potions.Margin"/>.
/// </param>
/// <param name="Caster">Who cast, drank or threw it: Teleportation moves it, and Tap Stamina's gain goes on it.</param>
/// <param name="Target">Whom it lands on.</param>
internal sealed record Landing(RecordId Source, MagicSchool School, int Level, int Margin, EffectTarget Caster, EffectTarget Target, IRandom Random)
{
    /// <summary>The margin a lasting record keeps, which Photosynthesis heals and Cheat Death twice: the cast's, or a potion's 0.</summary>
    public int RecordMargin { get; init; } = Margin;

    /// <summary>The cell a blast or corrosion bursts about: where a flask landed (<c>+0x5444</c>), or with none the target's.</summary>
    public (int X, int Z)? Centre { get; init; }

    /// <summary>A blast's damage, rolled already: a flask's, which its throw rolls (<c>Combatant.FlaskDamage</c>). With none, the blast rolls its dice as it lands.</summary>
    public int? Blast { get; init; }
}

/// <summary>What a landing's effects did, for the battle's lines and numbers.</summary>
internal sealed class LandingResult
{
    /// <summary>
    /// The dice over the margin that damage and Banishing add, which the caller rolls as d6s (<c>ApplySpellEffect</c>'s return); null
    /// with neither (its −1).
    /// </summary>
    public int? Dice { get; set; }

    /// <summary>A Banishing landed, which the battle says.</summary>
    public bool Banished { get; set; }

    /// <summary>The dispels it tried, and those that took something off.</summary>
    public int Dispels { get; set; }

    public int Dispelled { get; set; }

    /// <summary>The hit points its heals gave.</summary>
    public int Healed { get; set; }
}

/// <summary>
/// The one place a spell's, potion's or flask's effects land on a target (ADR 0021), whatever carries them: <c>Combatant.ApplySpellEffect</c>
/// (<c>0x800646E0</c>) for a spell, <c>Combatant.DrinkPotion</c> (<c>0x80064C84</c>) and <c>Entity::UsePotion</c> for a potion,
/// <c>Combatant.FlaskExplosion</c> (<c>0x800652F8</c>) for a flask. What lasts is <see cref="EffectRules"/>'.
/// </summary>
internal static class EffectLanding
{
    /// <summary>
    /// The effects land, in the record's order: each that happens once does now (<see cref="Once"/>), and with no duration so does
    /// each that would last, leaving nothing; a blast and a corrosion burst together about the centre, in a battle; then with a
    /// duration the lasting effects go on the target as one record, Tap Stamina's gain on the caster as another.
    /// </summary>
    /// <param name="effects">Those of the record's effects that are for this target (<see cref="Effect.For"/>).</param>
    public static LandingResult Land(IReadOnlyList<Effect> effects, EffectDuration? duration, Landing landing)
    {
        var result = new LandingResult();
        BlastEffect? blast = null;
        CorrodeEffect? corrode = null;
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case BlastEffect b:
                    blast ??= b;
                    break;
                case CorrodeEffect c:
                    corrode ??= c;
                    break;
                case var lasting when EffectRules.Lasting(lasting):
                    if (duration is null)
                        StartOnce(lasting, landing);
                    break;
                default:
                    Once(effect, landing, result);
                    break;
            }
        }
        if ((blast is not null || corrode is not null) && landing.Target is { Host: { } host, Combatant: { } at })
        {
            var damage = landing.Blast ?? (blast is null ? 0 : Roll(blast.Dice, landing.Random));
            host.Burst(at, landing.Source, damage, blast?.Stamina == true, corrode?.Radius, landing.Centre);
        }
        if (duration is { } d)
        {
            var timer = d.At(landing.Level);
            var lasting = effects.Where(e => EffectRules.Lasting(e) && e is not StaminaEffect { ToCaster: true }).ToList();
            var target = landing.Target;
            if (lasting.Count > 0)
            {
                target.Sheet.AddEffect(landing.Source, lasting, landing.Level, landing.School, landing.RecordMargin, timer, target.Combatant,
                    target.Host, landing.Random);
            }
            if (effects.OfType<StaminaEffect>().Where(s => s.ToCaster).ToList() is [_, ..] toCaster)
            {
                var caster = landing.Caster;
                caster.Sheet.AddEffect(landing.Source, toCaster, landing.Level, landing.School, 0, timer, caster.Combatant, caster.Host, landing.Random);
            }
        }
        return result;
    }

    /// <summary>
    /// An effect that happens once, as its spell, potion or flask lands. Damage and Banishing only add their dice, for the caller to
    /// roll; the battle's own (fleeing, Teleportation, the details popup, the sun and moon) need a battle and do nothing in trek.
    /// </summary>
    public static void Once(Effect effect, Landing landing, LandingResult result)
    {
        var target = landing.Target;
        var sheet = target.Sheet;
        var host = target.Host;
        switch (effect)
        {
            case DamageEffect damage:
                result.Dice = (result.Dice ?? 0) + damage.Dice.At(landing.Level);
                break;
            case BanishEffect:
                // (margin + the target's hit points) d6 (0x800646E0).
                result.Dice = (result.Dice ?? 0) + sheet.Hp;
                result.Banished = true;
                break;
            case DispelEffect dispel:
                result.Dispels++;
                if (Dispel(target, dispel.School, landing.Margin))
                    result.Dispelled++;
                break;
            case CureEffect cure:
                // Entity.CurePoison (0x80071A6C) and Entity.CureWeakness: Remove Poison only what was cast with a lower margin.
                foreach (var cured in sheet.Effects.Where(e => cure.Spells.Contains(e.Source) && (!cure.BeatsMargin || e.Margin < (byte)landing.Margin)).ToList())
                    sheet.RemoveEffect(cured, target.Combatant, host);
                break;
            case HealEffect heal:
                // Entity.HealPercent (0x800715DC): nothing, and no draw, in a battle with the Shadow; Entity::HealByPotion's share of the
                // maximum, worked in single precision and truncated.
                if (host?.HealingBarred != true)
                {
                    var before = sheet.Hp;
                    var share = landing.Random.Range(heal.Low, heal.High) / 100f;
                    sheet.Heal((ushort)(share * sheet.MaxHp));
                    result.Healed += sheet.Hp - before;
                }
                break;
            case FleeEffect when target.Combatant is { } fleeing:
                host?.Flee(fleeing);
                break;
            case TeleportEffect when landing.Caster.Combatant is { } caster:
                host?.Teleport(caster);
                break;
            case SenseAuraEffect when target.Combatant is { } sensed:
                host?.SenseAura(sensed, landing.Level);
                break;
            case RevealPhaseEffect phase:
                host?.RevealPhase(phase.Sun);
                break;
        }
    }

    /// <summary>A blast's dice (<c>Combatant.FlaskDamage</c>, <c>0x80065270</c>): Sleep Gas's none draw nothing.</summary>
    public static int Roll(Dice dice, IRandom random) => (dice.Count > 0 ? random.RollD(dice.Count, dice.Sides) : 0) + dice.Flat;

    // An effect that would last, on a record with no duration: it starts and leaves nothing, as the Stamina potion's does. Tap Stamina's
    // gain goes on the caster.
    private static void StartOnce(Effect effect, Landing landing)
    {
        var on = effect is StaminaEffect { ToCaster: true } ? landing.Caster : landing.Target;
        var record = new ActiveEffect(landing.Source, [effect], landing.Level, landing.School, landing.RecordMargin, 0);
        EffectRules.Start(record, 0, on, landing.Random);
    }

    // Entity.Dispel (0x80071CD8): every effect of the school comes off, if the margin times their number reaches their margins.
    // Unschooled effects can't be dispelled, and a potion's aren't among a character's spells' effects.
    private static bool Dispel(EffectTarget target, MagicSchool school, int margin)
    {
        if (school == MagicSchool.None)
            return false;
        var effects = target.Sheet.Effects.Where(e => e.School == school).ToList();
        if (effects.Count == 0 || (uint)(byte)margin * (uint)effects.Count < (uint)effects.Sum(e => e.Margin))
            return false;
        foreach (var effect in effects)
            target.Sheet.RemoveEffect(effect, target.Combatant, target.Host);
        return true;
    }
}
