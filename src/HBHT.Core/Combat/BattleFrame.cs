using System.Numerics;
using HBHT.Core.Audio;
using HBHT.Core.Movement;
using HBHT.Core.Ui;
using HBHT.Plugins;

namespace HBHT.Core.Combat;

/// <summary>
/// How a battle's turns are drawn (ADR 0040): the original's, each drawn as the core plays it, or Start Early's, where the core plays
/// the AI's turns ahead and the drawing plays them back from the recording.
/// </summary>
public enum CombatTurns
{
    Original,
    StartEarly,
}

/// <summary>
/// One tick of a battle as it's drawn (ADR 0040): everything the views show, sampled after the battle's tick
/// (<see cref="BattleRecorder.Sample"/>), and the one-offs it cued, the sounds it played and the emitters its actors started in that tick.
/// The views draw a battle from these alone, under both settings (<see cref="BattleDrawing"/>). Live objects (a combatant, a number, a
/// missile, a spell's visual) are only keys, which the views made their parts for as each was cued; what they show is the frame's.
/// </summary>
public sealed class BattleFrame
{
    /// <summary>The battle's tick it was made on, counted from 1 (0 is what <see cref="Battle.Begin"/> cued).</summary>
    public required int Tick { get; init; }

    /// <summary>What the battle's tick returned.</summary>
    public required BattleStep Step { get; init; }

    /// <summary>The battle's state after the tick.</summary>
    public required CombatState State { get; init; }

    /// <summary>Whose turn it was.</summary>
    public required Combatant? Current { get; init; }

    /// <summary>The combatants' bodies on the field, from each one's joining until it's gone (<see cref="Combatant.OnField"/>), in slot order.</summary>
    public required DrawnBody[] Bodies { get; init; }

    /// <summary>An arrow, thrown weapon or flask in flight, or none.</summary>
    public required DrawnMissile? Missile { get; init; }

    /// <summary>The spells' and potions' visuals live, in pool order (<see cref="SpellFx.Visuals"/>).</summary>
    public required DrawnVisual[] Visuals { get; init; }

    /// <summary>The numbers over heads.</summary>
    public required DrawnNumber[] Numbers { get; init; }

    /// <summary>The ring's teardrops (<see cref="Battle.Ring"/>), shared with the frames before while it hasn't moved.</summary>
    public required Vector3[] Ring { get; init; }

    /// <summary>Whether the ring shows: it hides while an action plays (<see cref="Battle.ActionActive"/>).</summary>
    public required bool RingShown { get; init; }

    /// <summary>The marker on the ground while a spell, its area or a flask is aimed (<see cref="Battle.Aim"/>).</summary>
    public required AimMarker? Aim { get; init; }

    /// <summary>The screen's brightness (<see cref="ScreenFade.Brightness"/>).</summary>
    public required float Fade { get; init; }

    /// <summary>Whether the music waited for the fade this tick (<c>Bgm.Update</c>): the fade as the tick began.</summary>
    public required bool MusicHeld { get; init; }

    /// <summary>The sky's brightness a cast dims it to (<see cref="SpellFx.Sky"/>).</summary>
    public required float SkyDim { get; init; }

    /// <summary>Whether the drawn fog thins away: a battle under Wind (<see cref="Battle.FogLifted"/>).</summary>
    public required bool FogLifted { get; init; }

    /// <summary>The combat camera.</summary>
    public required DrawnCamera Camera { get; init; }

    /// <summary>The battle screen.</summary>
    public required CombatGuiFrame Gui { get; init; }

    /// <summary>
    /// A held menu, the battle's own: one is only up in a turn the player plays, which the drawing is never behind, so its cursor is the
    /// live one's.
    /// </summary>
    public required CombatMenu? Menu { get; init; }

    /// <summary>Sense Aura's popup.</summary>
    public required string? Aura { get; init; }

    /// <summary>The one-offs cued in the tick, in order (<see cref="ICombatHost"/>).</summary>
    public required IReadOnlyList<CombatCue> Cues { get; init; }

    /// <summary>The calls the bodies' sounds made in the tick, in order (<see cref="ISoundOutput"/>).</summary>
    public required IReadOnlyList<SoundCall> Sounds { get; init; }

    /// <summary>
    /// The emitters the combatants' and the spells' visuals' actors started in the tick, by the request each started for, in order: the
    /// views start their own from these as they draw it.
    /// </summary>
    public required IReadOnlyList<EmitterStart> Emitters { get; init; }

    /// <summary>Whether this is an AI's walk (state 5), which A only fast-forwards under Start Early.</summary>
    public bool Walking => State == CombatState.Moving;

    /// <summary>
    /// Every arrow, thrown weapon or flask in flight: the battle's one (<see cref="Missile"/>) on a frame it recorded, or under Start Early
    /// as many as the actions drawing at once have in flight (<see cref="BattleDrawing"/>).
    /// </summary>
    public IReadOnlyList<DrawnMissile> Missiles
    {
        get => _missiles ?? (Missile is { } m ? [m] : []);
        init => _missiles = value;
    }

    private readonly IReadOnlyList<DrawnMissile>? _missiles;

    /// <summary>
    /// The bodies whose actors are drawn from a different tick than the one after the last drawn (an idle body meeting its next action
    /// under Start Early), whose views blend from the pose they had (ADR 0040). None on a frame the battle recorded.
    /// </summary>
    public IReadOnlySet<Combatant> Jumped { get; init; } = Empty;

    private static readonly IReadOnlySet<Combatant> Empty = new HashSet<Combatant>();
}

/// <summary>
/// A combatant's body as it's drawn on one tick: where its sphere's centre is, which way it faces, how it moves (which says how the view
/// smooths it), its alpha, tint and fog, its actor's state and clock, and what it holds in each slot.
/// </summary>
public readonly record struct DrawnBody(
    Combatant Combatant,
    Vector3 Centre,
    Vector2 Heading,
    MovementMode Movement,
    byte Alpha,
    TintState Tint,
    bool Unfogged,
    int ActorState,
    int ActorClock,
    HeldModel? Arm,
    HeldModel? Hand,
    HeldModel? Notes,
    int Hp)
{
    /// <summary>Where its feet are.</summary>
    public Vector3 Feet => Centre - new Vector3(0, Combatant.Body.Sphere.Radius, 0);

    /// <summary>What it holds, slot by slot (<see cref="Combatant.Held"/>).</summary>
    public HeldModel? Held(int slot) => slot switch { 0 => Arm, 1 => Hand, 2 => Notes, _ => null };
}

/// <summary>An arrow, thrown weapon or flask in flight as it's drawn: where on its arc, turned how, at what scale.</summary>
public readonly record struct DrawnMissile(MissileFlight Flight, Vector3 Drawn, Matrix4x4 Rotation, float Scale);

/// <summary>A spell's or potion's visual as it's drawn: where, whether a lasting scene shows and at what alpha, and its actor's state and clock.</summary>
/// <param name="Played">Whether an actor's animation has run its length.</param>
/// <param name="Struck">Whether it has played to the share of its cast state where its cast lands, or has nothing to play to it.</param>
public readonly record struct DrawnVisual(SpellFxVisual Visual, Vector3 Position, bool Showing, byte Alpha, int ActorState, int ActorClock,
    bool Played, bool Struck);

/// <summary>A number over a head as it's drawn: how long it has shown, which says where it is and how it looks.</summary>
public readonly record struct DrawnNumber(CombatNumber Number, int Ticks);

/// <summary>The combat camera as it's drawn: its eye, what it looks at, and which way it looks across the ground.</summary>
public readonly record struct DrawnCamera(Vector3 Position, Vector3 Aim, Vector2 LookXZ);

/// <summary>An actor started its emitters for a request: a combatant's or a spell's visual's (<see cref="Actors.ActorMotion.Starts"/>).</summary>
public readonly record struct EmitterStart(object Owner, int Request);

/// <summary>One call the bodies' sounds made (<see cref="ISoundOutput"/>), with the recording's own handle for the sound.</summary>
public abstract record SoundCall
{
    /// <summary>The combatant whose body's keys made it, or null if none was keying (<see cref="AudioKeys.Keying"/>).</summary>
    public Combatant? Owner { get; init; }

    public sealed record Start(SoundHandle Handle, RecordId Sequence, byte Volume, byte Pan, bool Loop, int DurationMs) : SoundCall;

    public sealed record Set(SoundHandle Handle, byte Volume, byte Pan) : SoundCall;

    public sealed record Duration(SoundHandle Handle, int Ms) : SoundCall;

    public sealed record Stop(SoundHandle Handle) : SoundCall;

    public sealed record Play(OneShot Sound) : SoundCall;
}
