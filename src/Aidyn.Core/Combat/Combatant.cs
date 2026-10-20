using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Movement;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>A combatant's flags (<c>+0x2C</c>, a u16).</summary>
[Flags]
public enum CombatantFlags : ushort
{
    None = 0,

    /// <summary>It has acted this turn.</summary>
    Acted = 1 << 0,

    /// <summary>It can act: alive, in play and not held.</summary>
    Active = 1 << 1,

    /// <summary>It's performing, as the Troubadour.</summary>
    Performing = 1 << 2,

    /// <summary>It has moved this turn.</summary>
    Moved = 1 << 3,

    /// <summary>It's on the enemies' side.</summary>
    Hostile = 1 << 4,

    /// <summary>It came from the enemy list.</summary>
    FromEnemyList = 1 << 5,

    /// <summary>It's gone, dead or fled.</summary>
    Gone = 1 << 6,

    /// <summary>An opponent is within reach, which holds its field to 1.</summary>
    Engaged = 1 << 7,

    /// <summary>It used Lay On Hands.</summary>
    LayOnHands = 1 << 8,

    /// <summary>It used Heal With Herbs.</summary>
    HealWithHerbs = 1 << 9,

    /// <summary>A reserve that has just joined the battle.</summary>
    Newcomer = 1 << 10,
}

/// <summary>
/// One of a battle's combatants (<c>Combat.Current</c>'s <c>+0x52D0</c>, a record of 0x8C bytes, docs/systems/combat.md):
/// a party member or an enemy on the field, with its character sheet, where it stands in the arena's cells, which way it
/// faces, its field's radius and its flags, and its body in the arena (<c>Combat.Bodies</c>).
/// </summary>
public sealed partial class Combatant
{
    internal Combatant(int slot, Character sheet, EntityBody look, Body body, CombatantFlags flags)
    {
        Slot = slot;
        Sheet = sheet;
        Look = look;
        Body = body;
        Flags = flags;
    }

    /// <summary>Its slot, 0–13, which is its body's in <c>Combat.Bodies</c> (<c>+0x20</c>).</summary>
    public int Slot { get; }

    /// <summary>
    /// Its character sheet (<c>+0x40</c>): a party member's own, or a sheet its entity's record starts for an enemy, which
    /// goes with the battle.
    /// </summary>
    public Character Sheet { get; }

    /// <summary>Its entity.</summary>
    public RecordId Entity => Sheet.Entity;

    /// <summary>Its entity's actor, radius, scale and height.</summary>
    public EntityBody Look { get; }

    /// <summary>Its body in the arena.</summary>
    public Body Body { get; }

    /// <summary>What its footsteps and voice keep between ticks.</summary>
    public Audio.BodyAudio Audio { get; } = new();

    /// <summary>Its footsteps and voice: its entity's, as combatants have theirs (<c>GetIdIndex</c>).</summary>
    public BodySounds? Sounds { get; init; }

    /// <summary>Its flags (<c>+0x2C</c>).</summary>
    public CombatantFlags Flags { get; internal set; }

    /// <summary>Whether it's on the enemies' side (flag 4): an enemy, but not Sholeh fighting with the party.</summary>
    public bool Hostile => Flags.HasFlag(CombatantFlags.Hostile);

    /// <summary>Whether it's on the party's side: a member, or Sholeh fighting with them (its slot under the party's size).</summary>
    public bool Party { get; internal init; }

    /// <summary>
    /// Whether the AI plays its turns (<c>+0x44</c>, the AI record): every enemy, and Sholeh on the party's side. The record
    /// itself is session 6's.
    /// </summary>
    public bool Controlled { get; internal set; }

    /// <summary>Whether it can act (flag 1).</summary>
    public bool Active => Flags.HasFlag(CombatantFlags.Active);

    /// <summary>
    /// Where it stands in the arena chunk's coordinates, a cell a unit (<c>+0x00</c>, <c>+0x04</c>): its body's X and Z as
    /// they were last read (<see cref="SyncFromBody"/>). The cell it stands in is <see cref="ArenaGrid.CellOf"/> of each.
    /// </summary>
    public Vector2 Cell { get; internal set; }

    /// <summary>Where it stood as its turn began (<c>+0x08</c>, <c>+0x0C</c>).</summary>
    public Vector2 TurnStart { get; internal set; }

    /// <summary>Which way it faces, 0–7 from north round to north-west (<c>+0x14</c>).</summary>
    public int Direction { get; internal set; }

    /// <summary>The way its body faces across the ground, as last read (<c>+0x18</c>, <c>+0x1C</c>).</summary>
    public Vector2 Facing { get; internal set; }

    /// <summary>
    /// How many times it has moved this round, to 3 (<c>+0x22</c>); the next sets <see cref="CombatantFlags.Moved"/>
    /// (<see cref="UpdateFromBody"/>). A round's end clears both.
    /// </summary>
    public int Moves { get; internal set; }

    /// <summary>Whether it's gone, dead or fled (flag 6).</summary>
    public bool Gone => Flags.HasFlag(CombatantFlags.Gone);

    /// <summary>Whether it's performing, as the Troubadour (flag 2).</summary>
    public bool Performing => Flags.HasFlag(CombatantFlags.Performing);

    /// <summary>Whether an opponent was within reach as its radius was last worked out (flag 7).</summary>
    public bool Engaged => Flags.HasFlag(CombatantFlags.Engaged);

    /// <summary>Its field's radius in cells (<c>+0x21</c>), as <see cref="UpdateRadius"/> last worked it out.</summary>
    public int Radius { get; internal set; }

    /// <summary>How many cells across it stands on the grid (<c>+0x23</c>): the party 1, rats 2.</summary>
    public int Footprint { get; internal set; }

    /// <summary>The last combatant it targeted, or −1 (<c>+0x10</c>).</summary>
    public int LastTarget { get; internal set; } = -1;

    /// <summary>
    /// Whether the AI has it fleeing (<c>EnemyAI.IsFleeing</c>, <c>0x8005B9AC</c>: the hostile side and its AI record's
    /// flag 0), which turns its portrait's frame orange.
    /// </summary>
    public bool Fleeing => Ai?.IsFleeing == true;

    /// <summary>Its AI record (<c>+0x44</c>): every enemy's, and Sholeh's from the enemy list. Null for one the player plays.</summary>
    public EnemyAI? Ai { get; internal set; }

    /// <summary>
    /// Mirror's counter (<c>+0x28</c>): a resist roll spends it to turn a spell away (<c>Combatant.ResistRoll</c>). Mirror's ending
    /// doesn't take it back.
    /// </summary>
    public int Mirror { get; internal set; }

    /// <summary>
    /// Its morale (the AI record's <c>+6</c>), which Aura of Death and Solar Wrath lower; null without an AI record.
    /// </summary>
    public int? Morale
    {
        get => Ai?.Morale;
        set
        {
            if (Ai is { } ai && value is { } morale)
                ai.Morale = (sbyte)morale;
        }
    }

    /// <summary>A morale check is due (the AI record's flag 1, <c>+9</c>'s 2): a weakness hit, or a spell that shook it.</summary>
    public bool MoraleCheckDue
    {
        get => Ai?.Flags.HasFlag(AiFlags.CheckDue) == true;
        set
        {
            if (Ai is { } ai)
                ai.Flags = value ? ai.Flags | AiFlags.CheckDue : ai.Flags & ~AiFlags.CheckDue;
        }
    }

    /// <summary>
    /// Whether an effect holds it (<c>Combatant.IsImmobilised</c>, <c>0x80062250</c>): Wall of Bones, Frozen Doom, Web of Starlight.
    /// A held target's Dexterity and shield don't count against an attack.
    /// </summary>
    public bool IsImmobilised => Sheet.Effects.Any(e => e.Effects.Any(x => x is Plugins.Records.ImmobiliseEffect));

    /// <summary>
    /// Its field's radius from its Dexterity's band, <paramref name="radius"/>, with its effects (<c>Combatant.UpdateRadius</c>,
    /// <c>0x8005F29C</c>), each in turn by slot: Haste adds its level, and Stellar Gravity takes its level off, leaving 1 if that's
    /// as much or more. <see cref="UpdateRadius"/> then caps it at 13 and holds it to 1 while engaged or performing.
    /// </summary>
    public int RadiusWithEffects(int radius)
    {
        foreach (var effect in Sheet.Effects)
        {
            foreach (var change in effect.Effects.OfType<Plugins.Records.FieldRadiusEffect>())
            {
                var amount = change.Amount.At(effect.Power);
                radius = (byte)(amount >= 0 ? radius + amount : -amount < radius ? radius + amount : 1);
            }
        }
        return radius;
    }

    /// <summary>The battle its effects reach beyond it, for those that end as it dies: Darkness and Light.</summary>
    /// <summary>The battle, for the effects that end as the combatant dies (<see cref="Characters.Character.TakeDamage"/>); set as it is made.</summary>
    internal Magic.IMagicHost? MagicHost { get; init; }

    public override string ToString() => $"{Slot}: {Sheet.Name}";
}
