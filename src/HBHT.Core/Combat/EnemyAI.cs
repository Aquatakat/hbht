using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

/// <summary>What the AI wants to do with its turn (the AI record's <c>+4</c> and <c>+7</c>).</summary>
public enum AiKind : byte
{
    /// <summary>Fight in melee.</summary>
    Melee = 0,

    /// <summary>Fight at range.</summary>
    Ranged = 1,

    /// <summary>Cast first (session 11's).</summary>
    Caster = 2,
}

/// <summary>The AI record's flags (<c>+9</c>).</summary>
[Flags]
public enum AiFlags : byte
{
    None = 0,

    /// <summary>It's fleeing (<c>EnemyAI.IsFleeing</c>, <c>0x8005B9AC</c>, on the hostile side).</summary>
    Fleeing = 1 << 0,

    /// <summary>A morale check is due: a weakness hit, or a spell that shook it (<c>EnemyAI.MayCheckMorale</c>).</summary>
    CheckDue = 1 << 1,

    /// <summary>Two more morale (<c>EnemyAI.Morale</c>); nothing in the original sets it.</summary>
    Steady = 1 << 2,

    /// <summary>The AI plays its turns: every record has it from the start (<c>TurnOrder.BeginTurn</c> tests it).</summary>
    Plays = 1 << 3,

    /// <summary>A weakness hit it, which costs it 2 morale from then on; never cleared.</summary>
    HitLately = 1 << 4,
}

/// <summary>
/// A combatant's AI record (<c>+0x44</c>, 0x1C bytes, <c>EnemyAI.Init</c>, <c>0x8005BA7C</c>): every enemy's, and Sholeh's when
/// she comes from the enemy list. What it holds, docs/systems/combat.md's "The AI" has; <see cref="Battle"/>'s AI plays it.
/// </summary>
public sealed class EnemyAI
{
    /// <summary>The action slots: the entity's five spells, then its armor's, its shield's and its weapon's (<c>+0x0A</c>).</summary>
    public const int ActionSlots = 8;

    /// <summary>A flight's turns (<c>EnemyAI.StartFleeing</c>): it leaves on the fourth if it hasn't before.</summary>
    public const int FlightTurns = 4;

    /// <summary>Morale at which an enemy never flees.</summary>
    public const int Fearless = 12;

    private EnemyAI(Combatant combatant) => Combatant = combatant;

    /// <summary>Whose record it is (<c>+0x14</c>).</summary>
    public Combatant Combatant { get; }

    /// <summary>
    /// The spare weapon (<c>+0x00</c>): the entity's first weapon, kept when it wields its third; what <c>EnemyAI.SwapWeapon</c>
    /// trades the held one for.
    /// </summary>
    public ItemInstance? Spare { get; internal set; }

    /// <summary>What it fights as (<c>+4</c>): by its weapon and spells at first, then what it last wanted (<see cref="Wanted"/>).</summary>
    public AiKind Kind { get; internal set; }

    /// <summary>What its style wants this turn (<c>+7</c>); unlike <see cref="Kind"/>, a weapon swap is due.</summary>
    public AiKind Wanted { get; internal set; }

    /// <summary>Its target's slot, or −1 (<c>+5</c>).</summary>
    public int Target { get; internal set; } = -1;

    /// <summary>Its morale (<c>+6</c>), the entity's: 12 or more never flees. Spells that shake it lower it.</summary>
    public int Morale { get; internal set; }

    /// <summary>Its attack style, 0–10 (<c>+8</c>, <c>EnemyAI.StyleTable</c>, <c>0x800E0B80</c>).</summary>
    public int Style { get; internal set; }

    /// <summary>Its flags (<c>+9</c>).</summary>
    public AiFlags Flags { get; internal set; } = AiFlags.Plays;

    /// <summary>The spells of its action slots (<c>+0x0A</c>–<c>+0x11</c>), null for an empty one; the casters' styles read them.</summary>
    public RecordId?[] Slots { get; } = new RecordId?[ActionSlots];

    /// <summary>The next corner of its path to walk (<c>+0x12</c>), or −1.</summary>
    public int PathPlace { get; internal set; } = -1;

    /// <summary>Its path's length (<c>+0x13</c>).</summary>
    public int PathLength { get; internal set; }

    /// <summary>A flight's turns left (<c>+0x18</c>).</summary>
    public int FlightLeft { get; internal set; }

    /// <summary>Whether it's fleeing (<c>EnemyAI.IsFleeing</c>, <c>0x8005B9AC</c>): on the hostile side, with the flag.</summary>
    public bool IsFleeing => Combatant.Hostile && Flags.HasFlag(AiFlags.Fleeing);

    /// <summary>Whether any action slot holds a spell.</summary>
    public bool HasSpells => Slots.Any(s => s is not null);

    /// <summary>
    /// The record for a combatant (<c>EnemyAI.Init</c>, <c>0x8005BA7C</c>): the spare weapon, the morale, the action slots from its
    /// spells and its armor's, shield's and weapon's, and the style by its weapon: none, 5; melee, 0 (1 with a base Intelligence of
    /// 15 or more), or with spells 6 (9 for the bosses); ranged, 2 (3 clever), 4 for one that strikes in melee too, or with spells
    /// 7, 8 (10 for the bosses).
    /// </summary>
    /// <param name="spare">The entity's first weapon, when it has a third too.</param>
    /// <param name="closeRange">Whether a weapon strikes in melee too (<c>Weapon.AllowsMelee</c>).</param>
    /// <param name="boss">Whether the entity has the bosses' style (<c>EnemyAI.HasBossStyle</c>, <c>0x8005BA40</c>).</param>
    public static EnemyAI Init(Combatant c, ItemInstance? spare, Func<WeaponRecord, bool> closeRange, bool boss)
    {
        var ai = new EnemyAI(c) { Spare = spare, Morale = (sbyte)c.Sheet.Record.Morale };
        var sheet = c.Sheet;
        for (var i = 0; i < 5; i++)
            ai.Slots[i] = i < sheet.Spells.Count ? sheet.Spells[i].Spell.Id : null;
        ai.Slots[5] = SpellOf(sheet.Equipment.Armor);
        ai.Slots[6] = SpellOf(sheet.Equipment.Shield);
        var clever = sheet.Stats.Base(Stat.Intelligence) >= 15;
        if (sheet.Equipment.Weapon?.Item.Weapon is not { } weapon)
        {
            ai.Style = 5;
            ai.Kind = AiKind.Caster;
        }
        else
        {
            ai.Slots[7] = SpellOf(sheet.Equipment.Weapon);
            var spells = ai.HasSpells;
            if (weapon.Range == 0)
            {
                (ai.Style, ai.Kind) = spells ? (boss ? 9 : 6, AiKind.Caster) : (clever ? 1 : 0, AiKind.Melee);
            }
            else
            {
                var both = closeRange(weapon);
                (ai.Style, ai.Kind) = spells
                    ? (boss ? 10 : both ? 8 : 7, AiKind.Caster)
                    : (both ? 4 : clever ? 3 : 2, AiKind.Ranged);
            }
        }
        ai.Wanted = ai.Kind;
        return ai;

        // An item's spell (its +0x10 list): what it casts when used, not what it casts when it strikes or is worn.
        static RecordId? SpellOf(ItemInstance? item) => item?.Item.Bonuses.Spell?.Spell;
    }

    /// <summary>Forgets its target and its path (<c>EnemyAI.ClearTarget</c>, <c>0x8005B998</c>).</summary>
    public void ClearTarget()
    {
        Target = -1;
        PathPlace = -1;
        PathLength = 0;
    }
}
