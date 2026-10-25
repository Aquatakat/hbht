using HBHT.Plugins.Records;

namespace HBHT.Core.Cinematics;

/// <summary>
/// Plays a cinematic (game mode 0xE, <c>Cinematic.Tick</c>, <c>0x80053934</c> in 1.0): its shots in order, each its
/// scene's animation through the scene's own camera, with its captions and fades, until the last ends or B or Start
/// skips the rest. The runtime draws what <see cref="Step"/> leaves, and carries out the end.
/// </summary>
/// <remarks>
/// <para>
/// The original steps it by the frame's ticks, uncapped; the port steps it a tick at a time, so it plays at 60 frames a
/// second, the most the original's can. A shot starts with its scene's clock at 1 and steps before it draws, so its
/// first picture is time 2, and it draws while the time it would step to is under the animation's length − 1
/// (<c>0x80053a28</c>), so the last is length − 2. The tick that fails draws nothing and frees the shot, and the next
/// starts the next shot and draws it: a tick between shots with the picture held. After the last shot, or a skip, two
/// more ticks pass undrawn before the end (<c>Cinematic.EndCase</c>, <c>0x80053c20</c>).
/// </para>
/// <para>
/// Captions and fades run on the drawn ticks alone, on clocks from each shot's start (<see cref="CinematicShot"/>'s
/// times are already doubled into ticks). One caption shows at a time, for its length from the tick it falls due
/// (<c>Cinematic.ReadCaption</c>, <c>0x80054a90</c>; <c>DrawCaption</c>, <c>0x80054784</c>), with no fade. A shot without
/// captions leaves the last list's state as it was. Fades (<c>Cinematic.StepFades</c>, <c>0x800541f0</c>) carry their
/// colour across shots.
/// </para>
/// </remarks>
public sealed class CinematicPlayer
{
    private enum State { Playing, Stopping, Ending, Ended }

    private readonly CinematicRecord _cinematic;
    private readonly string _leader;
    private State _state;
    private bool _startPending = true;
    private int _next;

    // The captions (Cinematic.Captions, 0x800e09c0): the list being read, the next caption's place in it, when it's due
    // and for how long, the clock, and the caption showing with the ticks it has left.
    private IReadOnlyList<CinematicCaption> _captions = [];
    private int _nextCaption;
    private int? _due;
    private int _captionClock, _captionLeft;
    private string? _caption;

    // The fades (0x800e09a8 on): the next entry, the shot clock, the overlay's colour, how long it stays, its progress
    // and its rate a tick.
    private int _fadeClock, _fadeNext, _fadeLeft;
    private CinematicShot? _fadeShot;
    private float _fadeProgress, _fadeRate;
    private byte _r, _g, _b;

    /// <param name="leader">The party leader's name, which the captions say in place of Alaron's.</param>
    public CinematicPlayer(CinematicRecord cinematic, string leader)
    {
        _cinematic = cinematic;
        _leader = leader;
    }

    public CinematicRecord Cinematic => _cinematic;

    /// <summary>The shot showing, or null between shots and after the last.</summary>
    public CinematicShot? Shot { get; private set; }

    /// <summary>Which shot it is, from 0.</summary>
    public int ShotIndex => _next - 1;

    /// <summary>Whether this tick started <see cref="Shot"/>, so its scene is loaded.</summary>
    public bool ShotStarted { get; private set; }

    /// <summary>Whether this tick drew a picture; when it didn't, the last one stays.</summary>
    public bool Drew { get; private set; }

    /// <summary>The shot's scene's time, from 1, which its animation is posed at.</summary>
    public int Time { get; private set; }

    /// <summary>The caption the last picture showed, with the leader's name in it, or null.</summary>
    public string? Caption { get; private set; }

    /// <summary>The fade's colour over everything, captions too; its alpha is 0 when nothing fades.</summary>
    public Rgba Fade { get; private set; }

    /// <summary>Whether it has ended, so its done flag is set and the game goes on.</summary>
    public bool Ended => _state == State.Ended;

    /// <summary>Whether B or Start has skipped the rest.</summary>
    public bool Skipped { get; private set; }

    /// <summary>B or Start: the rest is skipped (<c>Cinematic.ReadInput</c>, <c>0x80053d18</c>). Nothing else is taken.</summary>
    public void Skip()
    {
        if (_state != State.Playing)
            return;
        _state = State.Stopping;
        Skipped = true;
    }

    /// <summary>One 60 Hz tick.</summary>
    public void Step()
    {
        ShotStarted = Drew = false;
        switch (_state)
        {
            case State.Playing:
                if (_startPending)
                {
                    _startPending = false;
                    StartShot();
                }
                if (Shot is { } shot)
                {
                    if (Time + 1 < shot.Length - 1)
                        Draw(shot);
                    else
                    {
                        Shot = null;
                        _startPending = true;
                    }
                }
                break;
            case State.Stopping:
                Shot = null;
                _state = State.Ending;
                break;
            case State.Ending:
                _state = State.Ended;
                break;
        }
    }

    /// <summary>The next shot, from its scene's first tick (<c>Cinematic.StartShot</c>, <c>0x80053e4c</c>), or the end.</summary>
    private void StartShot()
    {
        if (_next >= _cinematic.Shots.Count)
        {
            _state = State.Stopping;
            return;
        }
        Shot = _cinematic.Shots[_next++];
        ShotStarted = true;
        Time = 1;
        if (Shot.Captions.Count > 0)
            SelectCaptions(Shot.Captions);
        // Cinematic.StartShotFades (0x800541d4): the clock starts again and the shot's own entries are read.
        _fadeClock = 0;
        _fadeShot = Shot;
        _fadeNext = 0;
    }

    private void Draw(CinematicShot shot)
    {
        Time++;
        Drew = true;
        StepCaptions();
        StepFades(shot);
    }

    // ---- Captions (cinematictext.cpp) -------------------------------------------------------------------------------

    /// <summary>A shot's list, from its first caption, with the clock at 0 (<c>Cinematic.SelectCaptions</c>, <c>0x80054ca0</c>).</summary>
    private void SelectCaptions(IReadOnlyList<CinematicCaption> captions)
    {
        _captions = captions;
        _nextCaption = 0;
        ReadCaption();
        _captionClock = 0;
    }

    /// <summary>When the next caption is due, or none.</summary>
    private void ReadCaption() => _due = _nextCaption < _captions.Count ? _captions[_nextCaption].Start : null;

    private void StepCaptions()
    {
        _captionClock++;
        // Cinematic.ShowDueCaption (0x800549ac): a caption whose time has come replaces the one showing, one a tick.
        if (_due is { } due && due <= _captionClock)
        {
            var caption = _captions[_nextCaption++];
            if (caption.End - caption.Start is > 0 and var length)
            {
                _caption = Named(caption.Text);
                _captionLeft = length;
            }
            ReadCaption();
        }
        // Cinematic.DrawCaption: drawn while it has a tick left, then counted down.
        Caption = _captionLeft >= 1 ? _caption : null;
        _captionLeft = Math.Max(0, _captionLeft - 1);
    }

    /// <summary>The leader's name for Alaron's, and in capitals for his in capitals (<c>Text.ReplaceAll</c>, <c>0x800548f0</c>).</summary>
    private string Named(string text) =>
        text.Replace("Alaron", _leader, StringComparison.Ordinal).Replace("ALARON", _leader.ToUpperInvariant(), StringComparison.Ordinal);

    // ---- Fades (Cinematic.StepFades, 0x800541f0) ---------------------------------------------------------------------

    private void StepFades(CinematicShot shot)
    {
        var ticks = 1;
        var before = _fadeClock;
        _fadeClock += ticks;
        if (_fadeShot == shot && _fadeNext < shot.Fades.Count && shot.Fades[_fadeNext] is var fade &&
            before <= fade.Start && fade.Start <= _fadeClock)
        {
            // The ramp is the length less 4 ticks, and the overlay stays 64 more after its hold; a fade of under 5 in
            // the table, under 10 ticks (the Shadow's lightning), ramps for its length and stops at once.
            var quick = fade.Length < 10;
            var ramp = quick ? fade.Length : fade.Length - 4;
            var stays = quick ? fade.Hold : fade.Hold + 0x40;
            // The tick it starts on takes the ticks since it was due as well as the tick's own.
            ticks = (ticks + _fadeClock - fade.Start) & 0xFFFF;
            _fadeLeft = stays + ramp;
            _fadeRate = (float)(1.0 / ramp);
            switch (fade.Color)
            {
                case CinematicFadeColor.Clear:
                    // From the colour showing back to the picture.
                    _fadeRate = -_fadeRate;
                    _fadeProgress = 1;
                    break;
                case CinematicFadeColor.Black:
                    (_r, _g, _b, _fadeProgress) = (0, 0, 0, 0);
                    break;
                case CinematicFadeColor.White:
                    (_r, _g, _b, _fadeProgress) = (255, 255, 255, 0);
                    break;
                case CinematicFadeColor.Red:
                    (_r, _g, _b, _fadeProgress) = (255, 0, 0, 0);
                    break;
            }
            _fadeNext++;
        }
        if (_fadeLeft == 0)
        {
            Fade = new Rgba(_r, _g, _b, 0);
            return;
        }
        _fadeLeft = Math.Max(0, _fadeLeft - ticks);
        _fadeProgress = Math.Clamp(_fadeProgress + _fadeRate * ticks, 0, 1);
        Fade = new Rgba(_r, _g, _b, ColorBytes.Single(_fadeProgress * 255f));
    }
}
