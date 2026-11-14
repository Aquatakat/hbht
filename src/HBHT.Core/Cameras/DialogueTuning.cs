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
    public IReadOnlyList<float> AcrossYaws { get; init; } = [0, 20, -20, 40, -40];

    /// <summary>Over the shoulder: behind the listener's eyes by this much, out to the side by this, and up by this.</summary>
public float ShoulderBack { get; init; } = 1.3f;
public float ShoulderSide { get; init; } = 0.75f;
public float ShoulderRise { get; init; } = 0.15f;

    /// <summary>A single: this far from the subject's eyes, turned this far toward the camera's side from the line to the listener.</summary>
public float SingleDistance { get; init; } = 2.8f;
public float SingleAngle { get; init; } = 35;
public float SingleRise { get; init; } = 0.1f;

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

    /// <summary>Past the listener, a long run turns between the single and over the shoulder every this many lines.</summary>
    public int RunCycle { get; init; } = 4;

    // Fitting.

    /// <summary>How far each side of the line is looked along for room as the conversation starts, and how much more room the other side needs to be taken.</summary>
public float SideReach { get; init; } = 6;
public float SideMargin { get; init; } = 1.5f;

    /// <summary>The sphere the camera's way from the focus is swept with.</summary>
    public float EyeRadius { get; init; } = 0.2f;

    public Lens Lens => new(FieldOfView, 4f / 3, Margin) { Floor = Floor };
}
