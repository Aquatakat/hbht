using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>What a combatant does with A (<c>+0x25</c>).</summary>
public enum CombatAction : byte
{
    /// <summary>Nothing: it has no weapon, and can't attack.</summary>
    None = 0,

    /// <summary>A melee attack, at a target within reach.</summary>
    Melee = 1,

    /// <summary>A ranged attack, at a target 3 or more cells away and in range, with no opponent within reach.</summary>
    Ranged = 2,

    /// <summary>A flask thrown or a potion drunk (session 9).</summary>
    Flask = 3,

    /// <summary>A ranged weapon that strikes in melee too: melee within reach, else ranged, out to its range.</summary>
    MeleeOrRanged = 4,
}

public sealed partial class Combatant
{
    /// <summary>What A does (<c>+0x25</c>), which its weapon sets (<see cref="SetDefaultAction"/>).</summary>
    public CombatAction Action { get; internal set; }

    /// <summary>
    /// Whether it aims freely rather than at a target in reach (<c>+0x8A</c>, <c>Combatant.UpdateDrawnFlag</c>, <c>0x80066A2C</c>):
    /// a bow or a thrown weapon that flies (<see cref="CombatRecord.Missiles"/>), or a flask. Its A goes to state 18, and its
    /// attack flies to the target.
    /// </summary>
    public bool Drawn { get; internal set; }

    /// <summary>Whether it's healing, by Lay On Hands or Heal With Herbs (flags 8 and 9), which session 9 sets.</summary>
    public bool Healing => (Flags & (CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs)) != 0;

    /// <summary>
    /// The damage an attack on it has done and is yet to show (<c>+0x29</c>, a byte): the action stores it, and the hit takes it
    /// as the attacker's swing ends (<c>Combat.ApplyHit</c>).
    /// </summary>
    public int Damage { get; internal set; }

    /// <summary>The healing done to it and yet to show (<c>+0x2A</c>), session 9's.</summary>
    public int Healed { get; internal set; }

    /// <summary>
    /// Whether its body has gone from the field (<c>Combat.FreeBody</c>, <c>0x80021C6C</c>, which empties its place in
    /// <c>Combat.Bodies</c>): dead, once its fall has played, or fled. The combatant stays in its slot until a reserve takes it.
    /// </summary>
    public bool Freed { get; internal set; }

    /// <summary>
    /// The target mark's pulse on its body (<c>CombatTargetMarks.Draw</c>, <c>0x80089830</c>), 0–1, while it's selected in the
    /// target set the state shows; null when it isn't. Its light goes toward this grey, once a second (<c>Body.SetTint</c>).
    /// </summary>
    public float? Mark { get; internal set; }

    /// <summary>Ticks it has been marked, for <see cref="Mark"/>'s pulse.</summary>
    internal int MarkTicks { get; set; }

    /// <summary>
    /// The ticks left of a fled body's fade (<c>Body.FadeOut</c>, <c>0x80014AD4</c>, its <c>+0x708</c>): it's gone from the battle,
    /// but its body is drawn fading where it stood until this reaches 0.
    /// </summary>
    public int Fading { get; internal set; }

    /// <summary>
    /// Whether its body has played its fall to the end and holds its last frame (the body's <c>+0x77C</c>): its animation no
    /// longer steps.
    /// </summary>
    internal bool Fallen { get; set; }

    /// <summary>
    /// What A does with its weapon (<c>Combatant.SetDefaultAction</c>, <c>0x800672D4</c>): nothing without one; melee with no range;
    /// with a range, ranged, or melee or ranged for a weapon that may strike in melee too (<c>Weapon.AllowsMelee</c>,
    /// <c>0x8007B730</c>: a Breath or a Spit, or one of <see cref="CombatRecord.CloseRange"/>). Whether it aims freely too.
    /// </summary>
    internal void SetDefaultAction(CombatRecord? rules)
    {
        var item = Sheet.Equipment.Weapon;
        Drawn = false;
        if (item?.Item.Weapon is not { } weapon)
        {
            Action = CombatAction.None;
            return;
        }
        if (weapon.Range == 0)
            Action = CombatAction.Melee;
        else if (weapon.Class is WeaponClass.Breath or WeaponClass.Spit || rules?.CloseRange?.Contains(weapon.Id) == true)
            Action = CombatAction.MeleeOrRanged;
        else
            Action = CombatAction.Ranged;
        Drawn = rules?.Missiles?.Any(m => m.Weapon == weapon.Id) == true;
    }
}
