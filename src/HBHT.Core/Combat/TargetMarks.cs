namespace HBHT.Core.Combat;

/// <summary>
/// What the two target sets say of one combatant (<c>Combat.Current</c>'s <c>+0x4E64</c> and <c>+0x4F0C</c>, their 14
/// candidate flags at <c>+0x00</c> and selected flags at <c>+0x48</c>): set A holds what the current combatant can attack,
/// set B what its selected spell can be cast at. The battle screen tints the portraits by them, and the radar pulses set
/// B's selected. Session 5's targeting keeps them (<c>TargetSet.Rebuild</c>, <c>0x80069264</c>); until then they're
/// written by tests and the runtime's debug key.
/// </summary>
[Flags]
public enum TargetMarks : byte
{
    None = 0,

    /// <summary>Set A's candidate: the current combatant could attack it.</summary>
    AttackCandidate = 1 << 0,

    /// <summary>Set A's selected: the attack's target.</summary>
    AttackSelected = 1 << 1,

    /// <summary>Set B's candidate: the current combatant's selected spell could be cast at it.</summary>
    SpellCandidate = 1 << 2,

    /// <summary>Set B's selected: one of the spell's targets.</summary>
    SpellSelected = 1 << 3,
}
