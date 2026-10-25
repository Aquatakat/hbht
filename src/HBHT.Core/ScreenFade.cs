namespace HBHT.Core;

/// <summary>
/// The screen's fade (<c>screenFadeMode</c>, stepped by <c>appState_1</c> at <c>0x8001f330</c> in 1.0):
/// out to black, in from black, or a dip down and back up. Its speeds are per tick. A fade out ends on
/// the third frame at black, so what waits for it starts then.
/// </summary>
public sealed class ScreenFade
{
    /// <summary>The frames a fade out holds at black before it ends.</summary>
    private const int BlackFrames = 3;

    private float _speed;
    /// <summary>A dip's brightness, from −1 to 1, whose size is the screen's (<c>brightness2</c>).</summary>
    private float _dip;
    private int _black;

    public FadeMode Mode { get; private set; }

    /// <summary>0 for black, 1 for full.</summary>
    public float Brightness { get; private set; } = 1;

    /// <summary>Whether a fade is under way: input is dropped, the conversation box hidden, and steered bodies stand.</summary>
    public bool Fading => Mode != FadeMode.None;

    /// <summary>Fades out from where the brightness is, by <paramref name="perTick"/> a tick.</summary>
    public void Out(float perTick) => Start(FadeMode.Out, perTick);

    /// <summary>Fades in, by <paramref name="perTick"/> a tick, from black when <paramref name="fromBlack"/>.</summary>
    public void In(float perTick, bool fromBlack = true)
    {
        if (fromBlack)
            Brightness = 0;
        Start(FadeMode.In, perTick);
    }

    /// <summary>Down to black and back up, by <paramref name="perTick"/> each way (a script's <c>FadeOut</c>, mode 3).</summary>
    public void Dip(float perTick)
    {
        _dip = -1;
        Start(FadeMode.Dip, perTick);
    }

    /// <summary>One tick.</summary>
    public void Step()
    {
        switch (Mode)
        {
            case FadeMode.Out:
                if (Brightness > 0)
                {
                    Brightness = MathF.Max(0, Brightness - _speed);
                }
                // Counted in frames: the third frame at black ends it.
                else if (++_black >= Cadence.Frames(BlackFrames))
                {
                    Mode = FadeMode.None;
                }
                break;
            case FadeMode.In:
                Brightness = MathF.Min(1, Brightness + _speed);
                if (Brightness >= 1)
                    Mode = FadeMode.None;
                break;
            case FadeMode.Dip:
                _dip += _speed;
                if (_dip >= 1)
                {
                    _dip = 1;
                    Mode = FadeMode.None;
                }
                Brightness = MathF.Abs(_dip);
                break;
        }
    }

    private void Start(FadeMode mode, float perTick)
    {
        Mode = mode;
        _speed = perTick;
        _black = 0;
    }
}

public enum FadeMode
{
    None,
    Out,
    In,
    /// <summary>Down and back up.</summary>
    Dip,
}
