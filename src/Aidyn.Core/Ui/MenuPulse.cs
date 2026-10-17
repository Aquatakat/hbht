using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// A cursor's pulse (<c>WidgetFastScrollMenu::Tick</c>, <c>WidgetScrollMenu</c>): a step a frame from 0 up
/// to <see cref="Frames"/> − 1 and back, and the colour that far from one colour toward another, by whole
/// steps of a 20th of the way, rounded toward zero. The menus' widgets step at 60 frames a second, so by
/// default a step is a tick: 38 ticks a cycle, 1.6 cycles a second, as the user timed the original's
/// (Oct 16, 2026).
/// </summary>
/// <param name="ticksPerStep">The ticks a step takes: a tick by default, at the menus' 60, or a 30 fps frame in trek.</param>
public sealed class MenuPulse(int ticksPerStep = Cadence.MenuFrameTicks)
{
    public const int Frames = 20;

    /// <summary>A scroll menu's own colours, which popups' choices pulse between (<c>WidgetScrollMenu</c>'s defaults).</summary>
    public static readonly Rgba ScrollFrom = new(100, 100, 100, 100), ScrollTo = new(255, 255, 255, 255);

    private int _ticks, _step, _sign = 1;

    /// <summary>Starts again from the first colour, as a new popup's does.</summary>
    public void Reset() => (_ticks, _step, _sign) = (0, 0, 1);

    /// <summary>Steps by a 60 Hz tick: a step each frame.</summary>
    public void Step()
    {
        if (++_ticks < ticksPerStep)
            return;
        _ticks = 0;
        _step += _sign;
        if (_step == 0 || _step >= Frames - 1)
            _sign = -_sign;
    }

    /// <summary>The colour now, from <paramref name="from"/> toward <paramref name="to"/>.</summary>
    public Rgba Now(Rgba from, Rgba to) => new(Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B), Blend(from.A, to.A));

    private byte Blend(byte from, byte to) => (byte)(from + (to - from) / Frames * _step);
}
