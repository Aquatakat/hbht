using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>What an attack came to (<c>Combatant.ResolveMelee</c> and <c>ResolveRanged</c>).</summary>
/// <param name="Chance">The chance it was rolled against.</param>
/// <param name="Roll">The d100.</param>
/// <param name="Adjusted">The roll after the skill turned it (<c>Combatant.AdjustRoll</c>): under the roll, a critical hit.</param>
/// <param name="Damage">The damage, 0 for a miss.</param>
/// <param name="Behind">Whether the attacker struck from behind (<c>Combatant.IsBehind</c>).</param>
public readonly record struct AttackOutcome(int Chance, int Roll, int Adjusted, int Damage, bool Behind)
{
    /// <summary>Whether it hit: the adjusted roll under the chance.</summary>
    public bool Hit => Adjusted < Chance;

    /// <summary>Whether the log calls it a critical hit: a hit whose roll the skill turned. It does nothing more.</summary>
    public bool Critical => Hit && Roll != Adjusted;
}

// Melee and ranged attacks (combatEntity.cpp, 0x800622C4-0x80063548): the chance, the roll, the damage and the log's lines.
// docs/systems/combat.md's "Melee and ranged" has the original's.
public sealed partial class Battle
{
    private const int AvoidsLine = 0x20, AttacksLine = 0x24, LosesLine = 0x30, LosesOneLine = 0x34, CriticalLine = 0x2C, MissesLine = 0x38;

    /// <summary>The least any attack's chance comes to.</summary>
    public const int LeastChance = 5;

    /// <summary>
    /// The Strength tiers (<c>0x800E0C30</c>): a melee attack rolls a d6 more for each tier the attacker's modified Strength is
    /// above (<c>Combatant.StrengthTier</c>, <c>0x80062008</c>).
    /// </summary>
    public static readonly IReadOnlyList<byte> StrengthTiers = [4, 9, 17, 24, 28, 32, 36, 39];

    /// <summary>
    /// The attacker's request by its weapon's animation (<c>Combat.AnimationByWeaponClass</c>, <c>0x800D27E0</c>,
    /// <c>Combatant.AnimationForWeapon</c>, <c>0x8005FEE4</c>), 18 for any other.
    /// </summary>
    public static readonly IReadOnlyList<int> AttackRequests = [16, 17, 13, 12, 15, 14];

    /// <summary>The attack request for a weapon animation not in <see cref="AttackRequests"/>.</summary>
    public const int OtherAttackRequest = 18;

    /// <summary>The request a weapon's animation asks of the attacker's body.</summary>
    public static int AttackRequest(int animation) =>
        animation >= 0 && animation < AttackRequests.Count ? AttackRequests[animation] : OtherAttackRequest;

    /// <summary>How many tiers of <see cref="StrengthTiers"/> a Strength is past (<c>Combatant.StrengthTier</c>), 0–8.</summary>
    public static int StrengthTier(int strength)
    {
        for (var i = 0; i < StrengthTiers.Count; i++)
        {
            if (strength <= StrengthTiers[i])
                return i;
        }
        return StrengthTiers.Count;
    }

    /// <summary>The weapon a combatant attacks with (the sheet's <c>+0x24</c>): a party member's in hand, an enemy's natural one.</summary>
    public static WeaponRecord? WeaponOf(Combatant c) => c.Sheet.Equipment.Weapon?.Item.Weapon;

    /// <summary>
    /// Whether <paramref name="attacker"/> strikes <paramref name="target"/> from behind (<c>Combatant.IsBehind</c>,
    /// <c>0x8005FB54</c>): it faces the target (<c>Combatant.IsAimedAt</c>, <c>0x8005FA44</c>: the point its distance away
    /// along its facing is within half a unit of the target in X and Z, or the two stand at one point), and the target faces away,
    /// within 45° (their facings' dot product, rounded half away from zero to six places, at least 0.7071).
    /// </summary>
    public static bool IsBehind(Combatant attacker, Combatant target)
    {
        if (!IsAimedAt(attacker, target.Cell))
            return false;
        var dot = Vector2.Dot(attacker.Facing, target.Facing) * 1e6f;
        dot = dot >= 0 ? MathF.Floor(dot + 0.5f) : -MathF.Floor(0.5f - dot);
        return dot / 1e6f >= 0.7071f;
    }

    private static bool IsAimedAt(Combatant attacker, Vector2 at)
    {
        var distance = Vector2.Distance(attacker.Cell, at);
        if (distance == 0)
            return true;
        var x = attacker.Facing.X * distance + attacker.Cell.X;
        var z = attacker.Facing.Y * distance + attacker.Cell.Y;
        return x >= at.X - 0.5f && x <= at.X + 0.5f && z >= at.Y - 0.5f && z <= at.Y + 0.5f;
    }

    /// <summary>
    /// A shield's defence (<c>Combatant.ShieldDefence</c>, <c>0x8005F6B0</c>): its defence times the shield skill, plus Warrior,
    /// which counts without a shield too, as a byte.
    /// </summary>
    public static int ShieldDefence(Character c) =>
        (byte)(c.Equipment.ShieldDefense * c.Skills.Modified(SkillId.Shield) + c.Skills.Modified(Skill.Warrior));

    /// <summary>
    /// Protection against an attack (<c>Combatant.ProtectionVsAttack</c>, <c>0x8005F71C</c>, over <c>TotalProtection</c>,
    /// <c>0x8005F570</c>): the sheet's (gear's and the effects', the Defence potion's and Acid's among them), body armor's and a
    /// fifth of Warrior, none if under 1; and the shield's unless struck from behind; as a byte.
    /// </summary>
    public static int Protection(Character c, bool behind)
    {
        var total = c.Protection + (c.Equipment.Armor?.Item.Armor?.Protection ?? 0) + c.Skills.Modified(Skill.Warrior) / 5;
        total = total < 1 ? 0 : total & 0xFF;
        if (!behind && c.Equipment.Shield?.Item.Armor is { } shield)
            total += shield.Protection;
        return total & 0xFF;
    }

    /// <summary>
    /// The aspect on a chance (<c>Combatant.AspectScale</c>): a Lunar attacker's × 1.25 at night and × 0.75 by day, a Solar one's the
    /// other way round (<c>0x800D2990</c>, <c>0x800D2994</c>).
    /// </summary>
    public int AspectScale(Character c, int chance)
    {
        if (c.Aspect == Aspect.None)
            return chance;
        var night = PartOfDay == PartOfDay.Night;
        var favoured = c.Aspect == Aspect.Lunar ? night : !night;
        return (short)(int)(chance * (favoured ? 1.25f : 0.75f));
    }

    /// <summary>
    /// The chance of a melee attack (<c>Combatant.MeleeChance</c>, <c>0x800622C4</c>): the attacker's modified Strength and
    /// Endurance, base Level, its weapon's hit, 6 × its weapon skill and 2 × Warrior, less the target's 2 × Dexterity, base
    /// Level and shield defence (no Dexterity or shield for a target held fast); 20 more from behind, and 10 × Thief; × 1.15
    /// against a target with no Stamina; 5% a half unit the attacker stands higher; × 0.8 with no Stamina of its own, × 0.9 at
    /// night; the aspect and the Troubadour; at least 5. Each step truncates to 16 bits, as the original's.
    /// </summary>
    public int MeleeChance(Combatant a, Combatant t, bool behind)
    {
        var (sheet, target) = (a.Sheet, t.Sheet);
        var weapon = WeaponOf(a)!;
        var s = (short)(sheet.Stats.Modified(Stat.Strength) + sheet.Stats.Modified(Stat.Endurance) + sheet.Stats.Base(Stat.Level));
        s = (short)(s + weapon.Hit + 6 * sheet.Skills.Modified(weapon.Class) + 2 * sheet.Skills.Modified(Skill.Warrior));
        var held = t.IsImmobilised;
        var defence = 2 * (held ? 0 : target.Stats.Modified(Stat.Dexterity)) + target.Stats.Base(Stat.Level) + (held ? 0 : ShieldDefence(target));
        var raw = (short)(s - defence);
        // Combatant.SkillTerm (0x80062ED8).
        int c = (short)((short)(raw + 20 * (behind ? 1 : 0)) + sheet.Skills.Modified(Skill.Thief) * 10);
        if (target.Stats.Modified(Stat.Stamina) == 0)
            c = (short)(int)(c * 1.15f);
        var height = HeightDifference(a, t);
        c = (short)(int)(height * 0.05f * c + c);
        if (sheet.Stats.Modified(Stat.Stamina) == 0)
            c = (short)(int)(c * 0.8f);
        if (PartOfDay == PartOfDay.Night)
            c = (short)(int)(c * 0.9f);
        c = AspectScale(sheet, c);
        c = Casting.TroubadourScale((short)c, TroubadourBonus, a.Hostile);
        return Math.Max(LeastChance, (int)(short)c);
    }

    /// <summary>
    /// The chance of a ranged attack (<c>Combat.RangedChance</c>, <c>0x80062F50</c>): the attacker's modified Intelligence and
    /// Dexterity, base Level, the weapon's hit, 6 × its skill and 2 × Warrior, less a point a cell past 6 away (less 5), the
    /// target's base Level, and unless it's held fast its 2 × Dexterity and shield defence; 20 more from behind; × 1.2 against a
    /// target that hasn't moved or acted, × 0.8 against one that has; × 1.15 against one with no Stamina; the height; × 0.9 with
    /// no Stamina of its own; less 30% of the rain's intensity; × 0.7 at night; the aspect and the Troubadour; at least 5.
    /// </summary>
    public int RangedChance(Combatant a, Combatant t, bool behind)
    {
        var (sheet, target) = (a.Sheet, t.Sheet);
        var weapon = WeaponOf(a)!;
        var distance = (byte)(int)Vector2.Distance(a.Cell, t.Cell);
        var s = (short)(sheet.Stats.Modified(Stat.Intelligence) + sheet.Stats.Modified(Stat.Dexterity) + sheet.Stats.Base(Stat.Level));
        s = (short)(s + weapon.Hit + 6 * sheet.Skills.Modified(weapon.Class) + 2 * sheet.Skills.Modified(Skill.Warrior));
        if (distance > 6)
            s = (short)(s - (distance - 5));
        s = (short)(s - target.Stats.Base(Stat.Level));
        if (!t.IsImmobilised)
            s = (short)((short)(s - 2 * target.Stats.Modified(Stat.Dexterity)) - ShieldDefence(target));
        int c = (short)(s + 20 * (behind ? 1 : 0));
        if (t.Moves == 0)
            c = (short)(int)(c * 1.2f);
        if (target.Stats.Modified(Stat.Stamina) == 0)
            c = (short)(int)(c * 1.15f);
        c = (short)(int)(c * (HeightDifference(a, t) * 0.05f + 1.0f));
        if (sheet.Stats.Modified(Stat.Stamina) == 0)
            c = (short)(int)(c * 0.9f);
        c = (short)(int)(c * (1.0f - Game.Weather.Intensity * 0.3f));
        if (PartOfDay == PartOfDay.Night)
            c = (short)(int)(c * 0.7f);
        if (t.Moves != 0)
            c = (short)(int)(c * 0.8f);
        c = AspectScale(sheet, c);
        c = Casting.TroubadourScale((short)c, TroubadourBonus, a.Hostile);
        return Math.Max(LeastChance, (int)(short)c);
    }

    /// <summary>How much higher the attacker's cell is than the target's, in half units (<see cref="ArenaGrid.HeightDifference"/>).</summary>
    private int HeightDifference(Combatant a, Combatant t) =>
        Grid.HeightDifference(ArenaGrid.CellOf(t.Cell.X), ArenaGrid.CellOf(t.Cell.Y), ArenaGrid.CellOf(a.Cell.X), ArenaGrid.CellOf(a.Cell.Y));

    /// <summary>
    /// A melee attack (<c>Combatant.ResolveMelee</c>, <c>0x800628A4</c>): a d100, which Warrior can turn into a critical
    /// (<see cref="Casting.AdjustRoll"/>); under the chance it hits for <see cref="Damage"/>; either way, a d100 for the weapon
    /// skill's experience; and the log's lines, the attack shown and the result queued. Without a weapon, nothing.
    /// </summary>
    public AttackOutcome ResolveMelee(Combatant a, Combatant t) => Resolve(a, t, ranged: false);

    /// <summary>
    /// A ranged attack (<c>Combatant.ResolveRanged</c>, <c>0x80063324</c>): as <see cref="ResolveMelee"/> with the ranged chance, no
    /// Strength dice or blow from behind in the damage, the experience after the lines, and "misses" for a miss.
    /// </summary>
    public AttackOutcome ResolveRanged(Combatant a, Combatant t) => Resolve(a, t, ranged: true);

    private AttackOutcome Resolve(Combatant a, Combatant t, bool ranged)
    {
        if (WeaponOf(a) is not { } weapon)
            return default;
        var random = Game.Random.Combat;
        var behind = IsBehind(a, t);
        var chance = ranged ? RangedChance(a, t, behind) : MeleeChance(a, t, behind);
        var roll = random.RollD(1, 100);
        var adjusted = Casting.AdjustRoll(roll, chance, a.Sheet.Skills.Modified(Skill.Warrior), random);
        var skill = a.Sheet.Skills.Modified(weapon.Class);
        var experience = skill < 6 ? (byte)((8 - skill) * (8 - skill)) : 5;
        var strike = new AttackOutcome(chance, roll, adjusted, 0, behind);
        if (!strike.Hit)
        {
            GainExperience(a, experience >> 1, weapon.Class);
            LogMiss(a, t, ranged);
            return strike;
        }
        var damage = Damage(a, chance, adjusted, t, ranged ? 0 : 1, ranged ? false : behind);
        if (!ranged)
            GainExperience(a, experience, weapon.Class);
        var line = Line2(strike.Critical ? CriticalLine : AttacksLine, a, t);
        Write(line);
        Lines.Queue(line);
        if (damage == 0)
            LogMiss(a, t, ranged);
        else
            LogHpLoss(t, damage);
        if (ranged)
            GainExperience(a, experience, weapon.Class);
        return strike with { Damage = damage };
    }

    /// <summary>
    /// A hit's damage (<c>Combatant.MeleeDamage</c>, <c>0x80062AE4</c>): the weapon's dice, and a melee hit's a die more for each
    /// Strength tier, in d6s; the margin's step (<see cref="Party.SkillCheckStep"/>) and the attacker's base Level; less the target's
    /// protection; 2 × Thief more from behind; at least 1; through the target's resistances to the weapon's element; and the
    /// weapon's spell as it strikes (<see cref="Strike(Combatant, Combatant)"/>).
    /// </summary>
    private int Damage(Combatant a, int chance, int adjusted, Combatant t, int tiers, bool behind)
    {
        var weapon = WeaponOf(a)!;
        var dice = (sbyte)(weapon.Damage + StrengthTier(a.Sheet.Stats.Modified(Stat.Strength)) * tiers);
        var sum = (ushort)Game.Random.Combat.RollD(dice, 6);
        var step = Party.SkillCheckStep((short)(chance - adjusted));
        var d = (short)(sum + (step + a.Sheet.Stats.Base(Stat.Level) - Protection(t.Sheet, behind)));
        d = (short)(d + a.Sheet.Skills.Modified(Skill.Thief) * 2 * (behind ? 1 : 0));
        d = Math.Max((short)1, d);
        d = (short)ApplyResistance(t, d, weapon.DamageType);
        var proc = Strike(a, t);
        var extra = proc is { Result: CastResult.Damage, Damage: var e } ? e : 0;
        return (short)(extra + d);
    }

    /// <summary>
    /// A weapon's damage through the target's resistances (<c>Combatant.ApplyResistance</c>, <c>0x80062704</c>): every resistance of
    /// its own and its gear's that meets the element (<c>ResistanceApplies</c>, <c>0x80061A30</c>: the element itself, Physical for
    /// Cutting and Smashing too, Magic for all the magical ones), multiplied, the damage truncated and at least 1 unless one is 0
    /// (immune), none below 0. A weakness makes an enemy's morale due a check.
    /// </summary>
    public static int ApplyResistance(Combatant t, int damage, Element element)
    {
        float? product = null;
        foreach (var resistance in t.Sheet.Record.Resistances.Concat(t.Sheet.Equipment.Resistances))
        {
            if (Applies(resistance.Element, element))
                product = (product ?? 1) * resistance.Damage;
        }
        if (product is not { } multiplier)
            return damage;
        var d = (short)(int)(damage * multiplier);
        if (multiplier != 0 && d < 1)
            d = 1;
        // A weakness makes a check due, and costs it morale from then on (the AI record's flags 0x12).
        if (multiplier > 1 && t.Ai is { } ai)
            ai.Flags |= AiFlags.CheckDue | AiFlags.HitLately;
        return d < 0 ? 0 : d;

        static bool Applies(Element resistance, Element element) => resistance == element || resistance switch
        {
            Element.Physical => element is Element.Cutting or Element.Smashing,
            Element.Magic => element is not (Element.None or Element.Physical or Element.Cutting or Element.Smashing or Element.Holy),
            _ => false,
        };
    }

    /// <summary>
    /// A chance to learn from an attack (<c>Combatant.GainSkillExperience</c>, <c>0x80062650</c>): a d100, always; at or under a tenth
    /// of <paramref name="points"/>, the weapon skill's base rises a level (<see cref="CharacterSkills.Train"/>).
    /// </summary>
    private void GainExperience(Combatant a, int points, WeaponClass weapon)
    {
        var roll = Game.Random.Combat.RollD(1, 100);
        if (roll <= ((int)(points * 0.1f) & 0xFF))
            a.Sheet.Skills.Train(weapon, 1);
    }

    // Combatant.LogMiss and LogMissed: the attack shown, and "avoids the attack" or "misses" queued.
    private void LogMiss(Combatant a, Combatant t, bool ranged)
    {
        Write(Line2(AttacksLine, a, t));
        Lines.Queue(ranged ? Line2(MissesLine, a, t) : Line(AvoidsLine, t));
    }

    // Combatant.LogHpLoss: "loses 1 hit point" or "loses %d hit points" queued.
    private void LogHpLoss(Combatant t, int damage) =>
        Lines.Queue(damage < 2 ? Line(LosesOneLine, t) : Line(LosesLine, t).Replace("%d", damage.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

    /// <summary>A line of the combat strings with two combatants' names.</summary>
    private string Line2(int offset, Combatant first, Combatant second)
    {
        var line = CombatText(offset);
        var at = line.IndexOf("%s", StringComparison.Ordinal);
        if (at >= 0)
            line = string.Concat(line.AsSpan(0, at), first.Sheet.Name, line.AsSpan(at + 2));
        at = line.IndexOf("%s", StringComparison.Ordinal);
        if (at >= 0)
            line = string.Concat(line.AsSpan(0, at), second.Sheet.Name, line.AsSpan(at + 2));
        return line;
    }
}
