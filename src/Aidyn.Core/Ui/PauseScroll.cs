namespace Aidyn.Core.Ui;

/// <summary>Where the pause menu's opening and closing is.</summary>
public enum PauseScrollPhase
{
    /// <summary>The world, frozen, darkening; the menu isn't built yet.</summary>
    Darkening,
    /// <summary>The scroll unrolling, the tabs showing between its rollers; no input yet.</summary>
    Unrolling,
    /// <summary>Open, taking input.</summary>
    Open,
    /// <summary>The scroll rolling up over the tabs.</summary>
    Rolling,
    /// <summary>Rolled up, the world brightening again.</summary>
    Brightening,
    /// <summary>Gone: trek goes on.</summary>
    Closed,
}

/// <summary>What the menu gives the scroll's actor this frame (<see cref="PauseScroll.TakeCue"/>).</summary>
/// <param name="Request">The request to step it toward. The original's actors take one for a step and then go back to 0.</param>
/// <param name="Join">
/// Above 0, the frame of the request's state to enter it at instead of stepping, where the port runs one of the
/// scroll's animations on into another (<see cref="Actors.ActorStateMachine.Join"/>).
/// </param>
public readonly record struct ScrollCue(int Request, int Join = 0);

/// <summary>
/// The pause menu's opening and closing, frame by frame: the world darkening behind it, the 3D scroll unrolling and
/// rolling up, and the clip that shows the tabs only between its rollers (1.0's <c>PauseMenu.DarkenStep</c>
/// <c>0x8004847c</c>, <c>OpenStep</c> <c>0x8002f1d0</c>, <c>CloseStep</c> <c>0x8002f4d4</c> and <c>SlideOut</c>
/// <c>0x8002e9d0</c>). The scroll is actor <c>0x0E6B</c>, which the view plays: it gives the actor
/// <see cref="TakeCue"/> while <see cref="StepsScroll"/>, and tells this its frame.
/// </summary>
/// <remarks>
/// <para>
/// The clip is in the 320-pixel frame's columns. The original cuts each tab's image to it, in x only, and draws
/// nothing of the tabs outside it. On the console the TV's overscan hides the frame's outer 16 columns, and the
/// original's clips stop short of the edges, behind it: Party's opens to 17 and 303. The port shows those columns, so
/// a side within a column of <see cref="Overscan"/> of the frame's edge counts as at it. The view also cuts the tabs
/// at the scroll's rollers, where it draws them, so a side at the frame's edge reaches the roller on that side, past
/// the frame on a wider screen.
/// </para>
/// <para>
/// The port's own, for screens wider and taller than the console's frame, where what the original's scroll does
/// beyond it shows. The user asked for these (Oct 13, 2026):
/// </para>
/// <list type="bullet">
/// <item>The Party tab's scroll runs on into the System and World tabs' opening, so its rollers go on out to their
/// ends, rather than jumping there as the original's does once it's past the frame. Its opening's last 9 frames are
/// their opening's first 9, so it joins theirs at frame 10. Closing from Party, their closing brings the rollers in to
/// the frame's edges, and then the Party tab's closing runs from its first frame, which is their closing's 31st.</item>
/// <item>The Party tab's scroll fades in as it rises into the frame and out as it drops out of it; the System and
/// World tabs fade out with their scroll as it rolls past the frame's edge, instead of the rolled-up scroll hanging
/// there.</item>
/// <item>Barter and training, which can't change tab, hold the scroll where the Party tab's opening ends, its rollers
/// at the frame's edges.</item>
/// </list>
/// </remarks>
public sealed class PauseScroll
{
    /// <summary>The tint the world darkens to: half (<c>PauseMenu.BackdropTint</c>).</summary>
    public const byte Dark = 0x80;

    /// <summary>How far the tint moves a frame, darkening and brightening.</summary>
    public const int TintStep = 10;

    /// <summary>The scroll's x in its scene for each tab, System, Party and World (<c>PauseMenu.SectionScrollX</c>, <c>0x800de9c8</c>).</summary>
    public static readonly float[] TabX = [27f, 0f, -26f];

    /// <summary>The scroll's camera: its height over the scene, looking straight down with −Z up the screen.</summary>
    public const float CameraHeight = 27f;

    // How each tab's clip opens, from the scroll's frame: until Start it's (Left, Right), and by End Left has
    // gone to 0 and Right to RightEnd, in a straight line that carries on until the animation ends.
    private readonly record struct Opening(int Request, int Start, int End, int Left, int Right, int RightEnd);

    // How each tab's clip closes: from the whole frame at Start, Left goes to LeftEnd and Right to RightEnd by End.
    // From FadeFrom the tabs and the scroll fade out, gone by the animation's end.
    private readonly record struct Closing(int Request, int Start, int End, int LeftEnd, int RightEnd, int FadeFrom);

    private const int System = 0, Party = 1;

    private static readonly Opening[] Openings =
    [
        new(22, 8, 36, 320, 320, 320),  // System: from the right.
        new(10, 36, 55, 160, 160, 320), // Party: from the middle out.
        new(22, 6, 35, 0, -5, 320),     // World: from the left.
    ];

    private static readonly Closing[] Closings =
    [
        new(11, 1, 31, 320, 320, 29), // System: to the right, fading once its roller reaches the frame's edge.
        new(19, 1, 14, 160, 160, 41), // Party: to the middle, fading as it drops out of the frame.
        new(11, 2, 32, 0, 0, 29),     // World: to the left.
    ];

    /// <summary>The frame of the System and World tabs' opening that the Party tab's runs on into: its last 9 frames are their first 9.</summary>
    private const int PartyRunsOnAt = 10;

    /// <summary>The last frame of the System and World tabs' closing before the Party tab's: their frames 31 to 39 are its 1 to 9.</summary>
    private const int PartyClosingFrom = 30;

    /// <summary>The System and World tabs' closing is their opening backwards, two frames longer: its frame i shows about the opening's 40 − i.</summary>
    private const int Reversed = 40;

    /// <summary>The frames the Party tab's scroll fades in over as it rises into the frame.</summary>
    private const int RiseFrames = 12;

    /// <summary>The frame's width, which the clip is in.</summary>
    private const int FrameWidth = 320;

    /// <summary>The columns at each side of the frame that the TV's overscan hides: the captures are black over them.</summary>
    public const int Overscan = 16;

    /// <summary>A side of the clip this near the frame's edge counts as at it: the overscan, and the column more the Party tab's clip stops short of it.</summary>
    private const int ClipOverscan = Overscan + 1;

    private readonly bool _brightens, _holds;
    private ScrollCue _cue;
    /// <summary>Whether the Party tab's scroll is running on into the other tabs' opening.</summary>
    private bool _runningOn;
    /// <summary>Closing from Party: the other tabs' closing, before the Party tab's own.</summary>
    private bool _rollingIn;
    /// <summary>The frame the scroll shows while running on.</summary>
    private int _shown;

    /// <param name="tab">The tab it opens on: 0 System, 1 Party, 2 World.</param>
    /// <param name="brightens">
    /// Whether the world brightens again after rolling up. The loot window and shops snap back (Game.Mode 7).
    /// </param>
    /// <param name="holds">
    /// Whether the scroll holds where the Party tab's opening ends, for barter and training, which can't change tab.
    /// </param>
    public PauseScroll(int tab, bool brightens = true, bool holds = false)
    {
        Tab = tab;
        _brightens = brightens;
        _holds = holds;
    }

    public PauseScrollPhase Phase { get; private set; }

    /// <summary>The tab the scroll opened on or rolls up from.</summary>
    public int Tab { get; set; }

    /// <summary>The world's brightness behind the menu, 255 to <see cref="Dark"/>, as each channel's multiplier.</summary>
    public byte Tint { get; private set; } = 255;

    /// <summary>Whether the tabs and the scroll are built and drawn: once the world has darkened, until the scroll has rolled up.</summary>
    public bool ShowsMenu => Phase is PauseScrollPhase.Unrolling or PauseScrollPhase.Open or PauseScrollPhase.Rolling;

    /// <summary>Whether the scroll's actor steps this frame. It holds still once rolled up, and in barter and training once open.</summary>
    public bool StepsScroll => Phase is PauseScrollPhase.Unrolling or PauseScrollPhase.Rolling || Phase == PauseScrollPhase.Open && !_holds;

    /// <summary>How opaque the tabs and the scroll are, 0 to 1.</summary>
    public float Alpha { get; private set; } = 1;

    /// <summary>
    /// The columns of the frame the tabs show between, or null for all of them. A side at the frame's edge, 0 or 320,
    /// reaches the roller on that side.
    /// </summary>
    public (int Left, int Right)? Clip { get; private set; } = (0, 0);

    /// <summary>What to give the scroll's actor this frame: a new request once, then 0, as the original's actors reset theirs after each step.</summary>
    public ScrollCue TakeCue()
    {
        var cue = _cue;
        _cue = default;
        return cue;
    }

    /// <summary>
    /// One frame. <paramref name="frame"/> is the scroll's clock, from 1, before this frame's step, and
    /// <paramref name="length"/> the length of the state it's in. The scroll starts each opening afresh, in its
    /// first state on its first frame, as the original builds it once the world has darkened.
    /// </summary>
    public void Step(int frame, int length)
    {
        switch (Phase)
        {
            case PauseScrollPhase.Darkening:
                if (Tint > Dark)
                {
                    Tint = (byte)(Tint <= Dark + TintStep ? Dark : Tint - TintStep);
                    return;
                }
                Phase = PauseScrollPhase.Unrolling;
                _cue = new(Openings[Tab].Request);
                Clip = OpenClip(Openings[Tab], 1);
                Alpha = Rise(1);
                return;
            case PauseScrollPhase.Unrolling:
                Clip = OpenClip(Openings[Tab], frame);
                Alpha = Rise(frame);
                if (frame <= Openings[Tab].Start || frame < length)
                    return;
                Phase = PauseScrollPhase.Open;
                if (Tab == Party && !_holds)
                {
                    _cue = new(Openings[System].Request, PartyRunsOnAt);
                    _runningOn = true;
                    _shown = PartyRunsOnAt;
                }
                return;
            case PauseScrollPhase.Open:
                Clip = null;
                Alpha = 1;
                if (_runningOn)
                {
                    _runningOn = frame < length;
                    _shown = frame + 1;
                }
                return;
            case PauseScrollPhase.Rolling:
                if (_rollingIn)
                {
                    Clip = null;
                    if (frame < PartyClosingFrom)
                        return;
                    _rollingIn = false;
                    _cue = new(Closings[Party].Request, 1);
                    return;
                }
                var closing = Closings[Tab];
                Clip = CloseClip(closing, frame);
                Alpha = frame <= closing.FadeFrom ? 1 : Math.Max(0, (float)(length - frame) / (length - closing.FadeFrom));
                if (frame <= closing.Start || frame < length)
                    return;
                // Rolled up: it brightens from this frame on.
                Phase = PauseScrollPhase.Brightening;
                Alpha = 0;
                if (!_brightens)
                    Tint = 255;
                goto case PauseScrollPhase.Brightening;
            case PauseScrollPhase.Brightening:
                if (Tint == 255)
                {
                    Phase = PauseScrollPhase.Closed;
                    return;
                }
                Tint = (byte)Math.Min(Tint + TintStep, 255);
                return;
        }
    }

    /// <summary>
    /// Starts rolling up from the tab showing (<c>PauseMenu.SlideOut</c>). The view steps the scroll once at once, with
    /// <see cref="TakeCue"/>, as the original does, so its next frame is the closing state's first.
    /// </summary>
    public void Close(int tab)
    {
        if (Phase is PauseScrollPhase.Rolling or PauseScrollPhase.Brightening or PauseScrollPhase.Closed)
            return;
        Tab = tab;
        Tint = Dark;
        Phase = PauseScrollPhase.Rolling;
        Clip = null;
        Alpha = 1;
        if (tab == Party && !_holds)
        {
            // The other tabs' closing first, from where their opening has got to if it's still running on.
            _rollingIn = true;
            _cue = _runningOn ? new(Closings[System].Request, Reversed - _shown) : new(Closings[System].Request);
            _runningOn = false;
        }
        else
        {
            _cue = new(Closings[tab].Request);
        }
    }

    /// <summary>The Party tab's scroll fades in as it rises into the frame.</summary>
    private float Rise(int frame) => Tab == Party ? Math.Min(1, (float)frame / RiseFrames) : 1;

    private static (int, int)? OpenClip(Opening o, int frame)
    {
        if (frame <= o.Start)
            return Overscanned(o.Left, o.Right);
        var span = (float)(o.End - o.Start);
        var by = frame - o.Start;
        return Overscanned(o.Left + (int)(-o.Left / span * by), o.Right + (int)((o.RightEnd - o.Right) / span * by));
    }

    private static (int, int)? CloseClip(Closing c, int frame)
    {
        if (frame <= c.Start)
            return null;
        var span = (float)(c.End - c.Start);
        var by = frame - c.Start;
        return Overscanned((int)(c.LeftEnd / span * by), FrameWidth + (int)((c.RightEnd - FrameWidth) / span * by));
    }

    /// <summary>The original's clip, with a side in the overscan moved to the frame's edge; null if both are.</summary>
    private static (int, int)? Overscanned(int left, int right)
    {
        // An edge that has crossed the other, closing, keeps its place.
        if (left <= ClipOverscan && left <= right)
            left = 0;
        if (right >= FrameWidth - ClipOverscan && right >= left)
            right = FrameWidth;
        return (left, right) == (0, FrameWidth) ? null : (left, right);
    }
}
