using System.Numerics;
using HBHT.Core.Cameras;

namespace HBHT.Core.Combat;

/// <summary>
/// What Camera: Enhanced (<see cref="EnhancedCamera"/>) was trying to do on its last tick, for the runtime's debug overlay to draw: whom it
/// tried to hold, the shot it moved toward and the one it showed, and what it planned that shot by. It's only read; nothing in the
/// camera depends on it.
/// </summary>
public sealed class CameraIntent
{
    /// <summary>What it tried to hold in its shot, by weight: whose turn it is first, those of lesser weight a pack held where it can be.</summary>
    public List<ShotSubject> Subjects { get; } = [];

    /// <summary>The shot it moved toward, and the one it showed.</summary>
    public Shot Goal { get; set; }

    public Shot Shown { get; set; }

    /// <summary>What it's showing, in words (<see cref="EnhancedCamera.Kind"/>).</summary>
    public string Kind { get; set; } = "";

    /// <summary>The lens subjects are held within.</summary>
    public Lens Lens { get; set; }

    /// <summary>Whose turn it is, and whom they head for in the player's turn.</summary>
    public Combatant? Turn { get; set; }

    public Combatant? Target { get; set; }

    /// <summary>The point the shot follows whoever's turn it is by (<see cref="FollowPoint"/>).</summary>
    public Vector3? Follow { get; set; }

    /// <summary>The battle's vantage, whose side every shot is taken from, and the field.</summary>
    public Vector2? Vantage { get; set; }

    public (Vector2 Min, Vector2 Max)? Field { get; set; }

    /// <summary>Over the shoulder: the yaw straight over it at the target, the framing's, and the one it eases onto.</summary>
    public float? Behind { get; set; }

    public float? Framing { get; set; }

    public float? FramingTo { get; set; }

    /// <summary>Why it last swung onto a new framing in the player's turn, and how many ticks ago.</summary>
    public (string Why, int Ago)? Swung { get; set; }

    /// <summary>An AI's walk: the way its shot's aim rides, where the eye starts and ends, and how far along it is.</summary>
    public List<Vector3> Rail { get; } = [];

    public (Vector3 Start, Vector3 End)? EyeRail { get; set; }

    public float? Along { get; set; }

    /// <summary>
    /// Start Early's director (<see cref="BattleDirector"/>): the beats it held besides, each where its body is drawn, its kind, and how
    /// many ticks off it strikes (0 or less: playing, that many ticks since it struck).
    /// </summary>
    public List<(ShotSubject Subject, BeatKind Kind, int In)> Beats { get; } = [];

    /// <summary>Start Early's director: the bodies walking in the drawing it held besides, where they're drawn.</summary>
    public List<ShotSubject> Walkers { get; } = [];

    /// <summary>Start Early's director: what it leads with, in words; null under Camera: Enhanced.</summary>
    public string? Director { get; set; }

    /// <summary>Start Early's director: why it last moved off the enhanced camera's own shot, and how many ticks ago.</summary>
    public (string Why, int Ago)? Moved { get; set; }
}
