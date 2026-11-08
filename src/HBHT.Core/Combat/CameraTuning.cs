using System.Globalization;
using HBHT.Core.Cameras;

namespace HBHT.Core.Combat;

/// <summary>
/// Every number the enhanced battle camera works by (<see cref="EnhancedCamera"/>), each settable by name from the command line
/// (<c>--battle-camera enhanced,Name=value,…</c>, <see cref="Parse"/>). Angles are in degrees, distances in units, accelerations a second
/// squared, times in ticks.
/// </summary>
public sealed record CameraTuning
{
    public static CameraTuning Default { get; } = new();

    // ---- The lens

    /// <summary>The vertical field of view a shot is judged by: the runtime camera's.</summary>
    public float FieldOfView { get; init; } = 45;

    /// <summary>The narrowest screen judged for, its width over its height.</summary>
    public float Aspect { get; init; } = 4f / 3;

    /// <summary>The share of the half-width and half-height a subject must sit within.</summary>
    public float FrameMargin { get; init; } = 0.85f;

    // ---- Motion

    /// <summary>The cap on the aim's acceleration, and its deceleration (units a second squared). Speed isn't capped.</summary>
    public float AimAcceleration { get; init; } = 30;

    /// <summary>The cap on how fast the view's turn and pitch speed up and slow down (degrees a second squared).</summary>
    public float TurnAcceleration { get; init; } = 240;

    /// <summary>The cap on how fast its distance from the aim speeds up and slows down (units a second squared).</summary>
    public float ZoomAcceleration { get; init; } = 40;

    /// <summary>How quickly the last of a move closes, a second's share: a gentle landing rather than a wobble round the goal.</summary>
    public float Settle { get; init; } = 4;

    /// <summary>How far a goal may move in a tick and be followed at its own speed; further, it jumped.</summary>
    public float GoalJump { get; init; } = 1;

    /// <summary>
    /// The caps times this while the player has the camera in their turn, so the view follows their own hand closely and still swoops,
    /// quicker, from a wide shot.
    /// </summary>
    public float HandAcceleration { get; init; } = 4;

    /// <summary>A goal whose aim is further than this from the camera's is cut to rather than moved to. Rare by design.</summary>
    public float CutDistance { get; init; } = 80;

    /// <summary>How far above the ground the eye is kept.</summary>
    public float FloorClearance { get; init; } = 1;

    // ---- Sight lines

    /// <summary>The sphere a sight line is swept with against the arena's collision.</summary>
    public float SightRadius { get; init; } = 0.1f;

    /// <summary>A body across a sight line hides what's behind it, taken as this times its radius; 0 for bodies hiding nothing.</summary>
    public float BodyOcclusion { get; init; } = 1.2f;

    /// <summary>Taken off a shot's score, times a subject's weight, for each subject it holds but can't see.</summary>
    public float OcclusionPenalty { get; init; } = 2;

    // ---- Events: the original's shots, kept clear

    /// <summary>Ticks between looks at whether the shot's subjects are hidden, and at a better framing.</summary>
    public int DecideEvery { get; init; } = 15;

    /// <summary>Turns from the original's shot tried when it hides a subject, degrees.</summary>
    public float[] EventYaws { get; init; } = [25, -25, 50, -50, 80, -80, 120, -120, 180];

    /// <summary>Raises of its pitch tried with each, degrees.</summary>
    public float[] EventPitches { get; init; } = [0, 15, 30];

    /// <summary>Times its distance tried with each.</summary>
    public float[] EventScales { get; init; } = [1, 1.3f];

    /// <summary>Looks in a row the original's shot must be clear before the camera goes back to it.</summary>
    public int ClearChecks { get; init; } = 3;

    // ---- The player's targets

    /// <summary>Half the cone, about the way the character points, a body must be in to be a target.</summary>
    public float ConeAngle { get; init; } = 40;

    /// <summary>Half the cone the target held stays in, wider, so the stick's wobble doesn't drop it.</summary>
    public float ConeHold { get; init; } = 60;

    /// <summary>A party member's weight as a target, beside an enemy's 1.</summary>
    public float PartyWeight { get; init; } = 0.75f;

    /// <summary>With a melee weapon, how fast a target's worth falls with its distance: nearer is likelier.</summary>
    public float NearScale { get; init; } = 6;

    /// <summary>With a ranged weapon or a staff, the distance at which a target is half as likely as the farthest.</summary>
    public float FarScale { get; init; } = 8;

    /// <summary>How much likelier another body must be than the target held to take its place.</summary>
    public float TargetMargin { get; init; } = 0.3f;

    /// <summary>Ticks in a row another body must be that much likelier.</summary>
    public int TargetHold { get; init; } = 20;

    /// <summary>The weapon skill counted as a staff, a weapon whose wielder aims far (<c>aidyn:weaponskill/5</c>).</summary>
    public int StaffSkill { get; init; } = 5;

    // ---- Framing the character and the target

    /// <summary>Pitches tried, degrees.</summary>
    public float[] Pitches { get; init; } = [25, 40, 55, 70];

    /// <summary>Turns tried from the way the camera looks now, degrees.</summary>
    public float[] Yaws { get; init; } = [0, 20, -20, 45, -45, 80, -80, 120, -120, 180];

    /// <summary>The pitch liked best, degrees.</summary>
    public float PreferredPitch { get; init; } = 40;

    /// <summary>The nearest and farthest a framed shot goes from its aim.</summary>
    public float MinDistance { get; init; } = 7;

    /// <inheritdoc cref="MinDistance"/>
    public float MaxDistance { get; init; } = 60;

    /// <summary>Taken off for each radian a framing turns the view from where it is.</summary>
    public float TurnCost { get; init; } = 0.6f;

    /// <summary>Taken off for each radian of pitch from the one liked best.</summary>
    public float PitchCost { get; init; } = 0.3f;

    /// <summary>Taken off for each unit of distance.</summary>
    public float DistanceCost { get; init; } = 0.01f;

    /// <summary>Added, times the cosine of the angle, for an eye on the character's side, away from the target.</summary>
    public float BehindBonus { get; init; } = 0.5f;

    /// <summary>How much better another framing must score to replace the one held.</summary>
    public float SwitchMargin { get; init; } = 0.3f;

    // ---- Aiming

    /// <summary>The pitch a ranged aim looks down from, degrees.</summary>
    public float AimPitch { get; init; } = 55;

    /// <summary>The pitch an area spell's placement looks down from, degrees: near enough straight down, with the screen's up still the stick's.</summary>
    public float TopDownPitch { get; init; } = 80;

    /// <summary>The farthest an aiming shot goes from its aim; past it the camera follows the reticle near the edge.</summary>
    public float MaxAimDistance { get; init; } = 70;

    /// <summary>
    /// How near the screen's edge, as a share of its half-width and half-height, the reticle goes before the camera follows it. At least
    /// <see cref="FrameMargin"/>, so a reticle within the reach held never moves it.
    /// </summary>
    public float EdgeMargin { get; init; } = 0.9f;

    /// <summary>Points round a reach the shot holds.</summary>
    public int ReachPoints { get; init; } = 16;

    /// <summary>The caps on the camera's motion.</summary>
    public MotionCaps Caps => new(AimAcceleration, TurnAcceleration, ZoomAcceleration, Settle);

    /// <summary>The lens shots are judged by.</summary>
    public Lens Lens => new(FieldOfView, Aspect, FrameMargin);

    /// <summary>
    /// These numbers with some set by name, as <c>AimAcceleration=20,ConeAngle=30</c>: any above, its name's case ignored. The lists
    /// (<see cref="Pitches"/>, <see cref="Yaws"/> and the event's) take values joined with <c>;</c>.
    /// </summary>
    public CameraTuning Parse(string settings)
    {
        var tuning = this with { };
        foreach (var part in settings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"'{part}' isn't Name=value.");
            var name = part[..eq].Trim();
            var value = part[(eq + 1)..].Trim();
            var property = typeof(CameraTuning).GetProperties().FirstOrDefault(p => p.CanWrite && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"The camera has no setting '{name}'.");
            object parsed = property.PropertyType == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float[]) ? value.Split(';').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()
                : throw new FormatException($"'{name}' can't be set.");
            property.SetValue(tuning, parsed);
        }
        return tuning;
    }

    /// <summary>Every setting that differs from the defaults, as <see cref="Parse"/> reads them.</summary>
    public override string ToString()
    {
        var changed = new List<string>();
        foreach (var p in typeof(CameraTuning).GetProperties())
        {
            if (!p.CanWrite)
                continue;
            var (mine, theirs) = (Text(p.GetValue(this)), Text(p.GetValue(Default)));
            if (mine != theirs)
                changed.Add($"{p.Name}={mine}");
        }
        return changed.Count == 0 ? "defaults" : string.Join(',', changed);

        static string Text(object? value) => value is float[] list
            ? string.Join(';', list.Select(v => v.ToString(CultureInfo.InvariantCulture)))
            : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
    }
}
