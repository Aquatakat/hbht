namespace HBHT.Core.Combat;

/// <summary>
/// What a battle tells whatever presents it (ADR 0019): every one-off it shows, as typed cues through <see cref="Cue"/>, in the
/// order the battle makes them. The runtime's views build and free their parts by them, and tests and the editor record them. What a
/// view draws each frame (where a body, a number or a missile is, the ring, the log box's text, the aim) is the battle's state, read
/// each frame; how the battle ended is its step's (<see cref="BattleStep"/>).
/// </summary>
public interface ICombatHost
{
    void Cue(CombatCue cue);
}

/// <summary>A one-off a battle shows.</summary>
public abstract record CombatCue;

/// <summary>
/// A combatant's body comes onto the field: each of the first ones as the battle is set up (<c>Combat.CreateBody</c>), and a reserve as it
/// joins (<c>Combat.SpawnReserves</c>). The runtime makes its view.
/// </summary>
public sealed record CombatantJoined(Combatant Combatant) : CombatCue;

/// <summary>
/// A body is asked for a one-off animation (a request): an attack's swing (12–18) or a throw's (15), a cast or a drink (5), and as the
/// blow lands the hit (7), the dodge (8), the fall (9) or an ally's help (10). The core steps the actor, so this is for the record.
/// </summary>
public sealed record CombatantAnimated(Combatant Combatant, int Request) : CombatCue;

/// <summary>A combatant dies (<c>Combatant.Death</c>): as the blow that kills it is dealt, before it lands, or as its effects kill it.</summary>
public sealed record CombatantDied(Combatant Combatant) : CombatCue;

/// <summary>
/// A body has gone from the field and is no longer drawn: a dead one's once it has fallen (<c>Combat.FreeBody</c>), a fled one's once its
/// fade is over. The runtime frees its view.
/// </summary>
public sealed record CombatantGone(Combatant Combatant, bool Fled) : CombatCue;

/// <summary>A number over a head starts showing (<c>CombatStatIndicator.Add</c>): damage, or with <see cref="CombatNumber.Healing"/> healing.</summary>
public sealed record NumberShown(CombatNumber Number) : CombatCue;

/// <summary>A number over a head has gone: its two seconds are over, the pool needed its place, or its body went.</summary>
public sealed record NumberGone(CombatNumber Number) : CombatCue;

/// <summary>An arrow, a thrown weapon or a flask leaves the attacker's hand (<c>Combat.StartCastCamera</c>).</summary>
public sealed record MissileLaunched(MissileFlight Flight) : CombatCue;

/// <summary>It has landed and is gone (<c>Combat.EndCast</c>): back in the hand, and the blow lands.</summary>
public sealed record MissileLanded(MissileFlight Flight) : CombatCue;

/// <summary>A spell's or potion's visual starts (<c>CombatSpellFx.AddVisual</c>): an actor that plays once, or a lasting scene over a head.</summary>
public sealed record SpellVisualStarted(SpellFxVisual Visual) : CombatCue;

/// <summary>A visual has finished and is freed (<c>CombatSpellFx.FreeVisual</c>).</summary>
public sealed record SpellVisualEnded(SpellFxVisual Visual) : CombatCue;

/// <summary>
/// The log box says something else (<see cref="CombatLog"/>, the one writer of its lines): a line shown at once, a queued one flushed, the
/// reinforcements' hold and the line it gives back, or the line it's locked on as the party falls. A line put up again over itself
/// isn't one, as the box doesn't change.
/// </summary>
public sealed record LogLine(string Text) : CombatCue;

/// <summary>
/// The battle pauses for its pause menu (states 14 and 27, once their two frames have passed): with the Party tab and Flee offered from a
/// turn, the System tab alone from any other state.
/// </summary>
public sealed record BattlePaused(bool PartyTab, bool CanFlee) : CombatCue;

/// <summary>The battle has ended, as <see cref="Outcome"/> says, and leaves for whatever follows it.</summary>
public sealed record BattleEnded(BattleOutcome Outcome) : CombatCue;

/// <summary>
/// What a tick of a battle came to (<see cref="Battle.Tick"/>), as the original's <c>Combat.Frame</c> returns the next game mode: it goes
/// on, its pause menu is due, or it has ended. Whoever runs it acts on it.
/// </summary>
public abstract record BattleStep
{
    private BattleStep()
    {
    }

    /// <summary>It goes on (mode 2).</summary>
    public static readonly BattleStep Ran = new Running();

    /// <summary>It goes on.</summary>
    public sealed record Running : BattleStep;

    /// <summary>Its pause menu is due (mode 3): with the Party tab and Flee from a turn, the System tab alone otherwise.</summary>
    public sealed record Paused(bool PartyTab, bool CanFlee) : BattleStep;

    /// <summary>It has left, as <paramref name="Outcome"/> says (modes 1, 6, <c>0xC</c> and <c>0xE</c>).</summary>
    public sealed record Ended(BattleOutcome Outcome) : BattleStep;
}
