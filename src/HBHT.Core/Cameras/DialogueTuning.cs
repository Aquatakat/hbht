using System.Globalization;

namespace HBHT.Core.Cameras;

/// <summary>
/// Every number the enhanced conversation camera goes by (<see cref="DialogueCamera"/>, <see cref="DialoguePlanner"/>), in one
/// place so a launch flag can set them by name later. Distances are in units, angles in degrees, times in ticks unless named.
/// </summary>
    public sealed record DialogueTuning
{
    public static DialogueTuning Default { get; } = new();

    // The lens.

    /// <summary>The runtime's field of view (<c>CameraRig</c>, 45° high): the shots are framed by it, not the original's 75°.</summary>
    public float FieldOfView { get; init; } = 45;

    /// <summary>How much of the half-height below the middle a subject may sit within: the box covers the bottom quarter.</summary>
    public float Floor { get; init; } = 0.45f;

    /// <summary>How much of the half-width and half-height above the middle a subject may sit within.</summary>
    public float Margin { get; init; } = 0.85f;

    /// <summary>How far below a talker's eyes the aim sits, so a face is in the upper part of the frame.</summary>
    public float AimDrop { get; init; } = 0.2f;

    /// <summary>The head held round each talker's eyes in a two-shot or a wide.</summary>
    public float HeadRadius { get; init; } = 0.3f;

    /// <summary>How far below the eyes the chest held in a two-shot is, and its radius: a medium shot is from the waist up.</summary>
    public float ChestDrop { get; init; } = 0.7f;
    public float ChestRadius { get; init; } = 0.35f;

    // The shots.

    /// <summary>A two-shot: across the line, this high, no nearer than this.</summary>
    public float TwoShotPitch { get; init; } = 8;
    public float TwoShotNearest { get; init; } = 3.5f;

    /// <summary>A wide: higher and further, at least this far, and this much further than the two-shot's distance.</summary>
    public float WidePitch { get; init; } = 16;
    public float WideNearest { get; init; } = 6;
    public float WideScale { get; init; } = 1.5f;

    /// <summary>The ways round from straight across the line a two-shot or a wide may be tried, on its side.</summary>
    public float[] AcrossYaws { get; init; } = [0, 20, -20, 40, -40];

    /// <summary>
    /// Over the shoulder, roomy, tried first: behind the listener's eyes by this much, out to the side by this, and up by this, the
    /// listener in a third of the frame and the speaker clear (the user's, in the game at Baldric).
    /// </summary>
    public float ShoulderBack { get; init; } = 2.0f;
    public float ShoulderSide { get; init; } = 0.95f;
    public float ShoulderRise { get; init; } = 0.3f;

    /// <summary>Over the shoulder, tight, tried second where the roomy one doesn't fit: the listener fills more of the frame.</summary>
    public float TightShoulderBack { get; init; } = 1.3f;
    public float TightShoulderSide { get; init; } = 0.75f;
    public float TightShoulderRise { get; init; } = 0.15f;

    /// <summary>
    /// A single: this far from the subject's eyes, turned these ways (tried in order) toward the camera's side from the line to the
    /// listener, and up this much. It must be clean: the listener covering no more than <see cref="SingleSliver"/> of the frame's width.
    /// </summary>
    public float SingleDistance { get; init; } = 2.4f;
    public float[] SingleAngles { get; init; } = [70, 60, 80, 90, 100, 50];
    public float SingleRise { get; init; } = 0.1f;

    /// <summary>How far a single's view may be turned from its subject, away from the listener, as shares of the half-width, tried in order.</summary>
    public float[] SingleLeads { get; init; } = [0, 0.35f, 0.5f, 0.6f];

    /// <summary>The share of the frame's width the listener may cover in a single: a sliver at the edge.</summary>
    public float SingleSliver { get; init; } = 0.05f;

    /// <summary>A shouted line's punch-in: the single's axis, this near. A face stays under a quarter of the frame's height (about 1.7).</summary>
    public float PunchDistance { get; init; } = 2.0f;

    /// <summary>How fast a shot drifts in toward its aim, units a second, and how far at most: slow enough to feel rather than see.</summary>
    public float DriftSpeed { get; init; } = 0.06f;
    public float DriftMost { get; init; } = 0.5f;

    // Choosing.

    /// <summary>A shot lasts at least this long (1.5 seconds): a page that comes sooner keeps it if it shows the speaker, else the two-shot.</summary>
    public int MinShotTicks { get; init; } = 90;

    /// <summary>A run of this many lines or more by one speaker is long: it goes wide at line <see cref="RunWideAt"/> and to the listener at <see cref="RunListenerAt"/>.</summary>
    public int RunLong { get; init; } = 5;
    public int RunWideAt { get; init; } = 3;
    public int RunListenerAt { get; init; } = 5;

    /// <summary>
    /// Past the listener, a long run repeats this cycle, a letter a line: S the speaker's single, O over the listener's shoulder at the
    /// speaker, W the wide, L the listener's single. So the wide and the listener come back every 12 lines. The listener never takes a
    /// run's last line, which goes over the shoulder instead.
    /// </summary>
    public string RunPattern { get; init; } = "SSSSOOOWOOOL";
    // Fitting.

    /// <summary>How far each side of the line is looked along for room as the conversation starts, and how much more room the other side needs to be taken.</summary>
    public float SideReach { get; init; } = 6;
    public float SideMargin { get; init; } = 1.5f;

    /// <summary>The sphere the camera's way from the focus is swept with.</summary>
    public float EyeRadius { get; init; } = 0.2f;

    public Lens Lens => new(FieldOfView, 4f / 3, Margin) { Floor = Floor };

    /// <summary>
    /// These numbers with some set by name, as <c>--dialogue-camera enhanced,SingleDistance=3,RunPattern=SSOOWL</c>: any above, its
    /// name's case ignored; <see cref="AcrossYaws"/> takes values joined with <c>;</c>.
    /// </summary>
    public DialogueTuning Parse(string settings)
    {
        var tuning = this with { };
        foreach (var part in settings.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"'{part}' isn't Name=value.");
            var (name, value) = (part[..eq].Trim(), part[(eq + 1)..].Trim());
            var property = typeof(DialogueTuning).GetProperties().FirstOrDefault(p => p.CanWrite && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                ?? throw new FormatException($"The conversation camera has no setting '{name}'.");
            object parsed = property.PropertyType == typeof(int) ? int.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float) ? float.Parse(value, CultureInfo.InvariantCulture)
                : property.PropertyType == typeof(float[]) ? value.Split(';').Select(v => float.Parse(v, CultureInfo.InvariantCulture)).ToArray()
                : property.PropertyType == typeof(string) ? value
                : throw new FormatException($"'{name}' can't be set.");
            property.SetValue(tuning, parsed);
        }
        return tuning;
    }
}
