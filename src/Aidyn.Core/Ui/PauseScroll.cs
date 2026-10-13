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

/// <summary>
/// The pause menu's opening and closing, frame by frame: the world darkening behind it, the 3D scroll unrolling and
/// rolling up, and the clip that shows the tabs only between its rollers (1.0's <c>PauseMenu.DarkenStep</c>
/// <c>0x8004847c</c>, <c>OpenStep</c> <c>0x8002f1d0</c>, <c>CloseStep</c> <c>0x8002f4d4</c> and <c>SlideOut</c>
/// <c>0x8002e9d0</c>). The scroll is actor <c>0x0E6B</c>, which the view plays: it gives the actor
/// <see cref="TakeRequest"/> and steps it while <see cref="StepsScroll"/>, and tells this its frame.
/// </summary>
/// <remarks>
/// The clip is in the 320-pixel frame's columns. The original cuts each tab's image to it, in x only, and draws
/// nothing of the tabs outside it. On the console the TV's overscan hides the frame's outer 16 columns, and the
/// original's clips stop short of the edges, behind it: Party's opens to 17 and 303. The port shows those columns, so
/// a side within a column of <see cref="Overscan"/> of the frame's edge counts as at it, and the tabs cover them rather than the
/// darkened world showing there. A side at the frame's edge reaches the screen's, so a screen wider than the frame
/// shows the tabs beside the one opening, as it does once the menu is open.
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
    private readonly record struct Closing(int Request, int Start, int End, int LeftEnd, int RightEnd);

    private static readonly Opening[] Openings =
    [
        new(22, 8, 36, 320, 320, 320),  // System: from the right.
        new(10, 36, 55, 160, 160, 320), // Party: from the middle out.
        new(22, 6, 35, 0, -5, 320),     // World: from the left.
    ];

    private static readonly Closing[] Closings =
    [
        new(11, 1, 31, 320, 320), // System: to the right.
        new(19, 1, 14, 160, 160), // Party: to the middle.
        new(11, 2, 32, 0, 0),     // World: to the left.
    ];

    /// <summary>The frame's width, which the clip is in.</summary>
    private const int FrameWidth = 320;

    /// <summary>The columns at each side of the frame that the TV's overscan hides: the captures are black over them.</summary>
    public const int Overscan = 16;

    /// <summary>A side of the clip this near the frame's edge counts as at it: the overscan, and the column more the Party tab's clip stops short of it.</summary>
    private const int ClipOverscan = Overscan + 1;

    /// <summary>
    /// Where the scroll ends: the columns of the System and World tabs' frames that their rollers are centred on once
    /// open, measured from the port's draw. The tabs' parchment is cut there, at either end of the scroll, so it never
    /// shows past a roller, which the TV's overscan hides on the console. The user asked for this (Oct 12, 2026).
    /// </summary>
    public const int SystemEnd = 37, WorldEnd = 289;

    private readonly bool _brightens;
    private int _request;

    /// <param name="tab">The tab it opens on: 0 System, 1 Party, 2 World.</param>
    /// <param name="brightens">
    /// Whether the world brightens again after rolling up. The loot window and shops snap back (Game.Mode 7).
    /// </param>
    public PauseScroll(int tab, bool brightens = true)
    {
        Tab = tab;
        _brightens = brightens;
    }

    public PauseScrollPhase Phase { get; private set; }

    /// <summary>The tab the scroll opened on or rolls up from.</summary>
    public int Tab { get; set; }

    /// <summary>The world's brightness behind the menu, 255 to <see cref="Dark"/>, as each channel's multiplier.</summary>
    public byte Tint { get; private set; } = 255;

    /// <summary>Whether the tabs are built and drawn: once the world has darkened.</summary>
    public bool ShowsMenu => Phase is PauseScrollPhase.Unrolling or PauseScrollPhase.Open or PauseScrollPhase.Rolling;

    /// <summary>Whether the scroll's actor steps this frame. It holds still once rolled up.</summary>
    public bool StepsScroll => Phase is PauseScrollPhase.Unrolling or PauseScrollPhase.Open or PauseScrollPhase.Rolling;

    /// <summary>
    /// The columns of the frame the tabs show between, or null for all of them. A side at the frame's edge, 0 or 320,
    /// reaches the screen's edge.
    /// </summary>
    public (int Left, int Right)? Clip { get; private set; } = (0, 0);

    /// <summary>The request to give the scroll's actor this frame: a new one once, then 0, as the original's actors reset theirs after each step.</summary>
    public int TakeRequest()
    {
        var request = _request;
        _request = 0;
        return request;
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
                _request = Openings[Tab].Request;
                Clip = OpenClip(Openings[Tab], 1);
                return;
            case PauseScrollPhase.Unrolling:
                Clip = OpenClip(Openings[Tab], frame);
                if (frame > Openings[Tab].Start && frame >= length)
                    Phase = PauseScrollPhase.Open;
                return;
            case PauseScrollPhase.Open:
                Clip = null;
                return;
            case PauseScrollPhase.Rolling:
                Clip = CloseClip(Closings[Tab], frame);
                if (frame <= Closings[Tab].Start || frame < length)
                    return;
                // Rolled up: it brightens from this frame on.
                Phase = PauseScrollPhase.Brightening;
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
    /// <see cref="TakeRequest"/>, as the original does, so its next frame is the closing state's first.
    /// </summary>
    public void Close(int tab)
    {
        if (Phase is PauseScrollPhase.Rolling or PauseScrollPhase.Brightening or PauseScrollPhase.Closed)
            return;
        Tab = tab;
        Tint = Dark;
        Phase = PauseScrollPhase.Rolling;
        _request = Closings[tab].Request;
        Clip = null;
    }

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
