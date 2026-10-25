namespace HBHT.Core.Ui;

/// <summary>
/// The stick's directions held, and when each repeats, as the original's widgets count them (<c>BaseWidget::Control</c>,
/// <c>0x800ad708</c> in 1.0): a direction held counts 30 of the controller's samples, 60 a second, before the cursor moves
/// on by itself, and then 5 between moves. The user timed about 0.7 s and 12 a second. It's stepped once a tick
/// (<see cref="Tick"/>), the menus' frame being a tick (<see cref="Cadence.MenuFrameTicks"/>), so a hold repeats on the
/// same ticks in a test as in the game, whatever the frame rate. Whoever reads the controls says when a direction is
/// pressed and let go (<see cref="Press"/>, <see cref="Release"/>); a keyboard's key is held but doesn't repeat here,
/// as the system repeats it.
/// </summary>
public sealed class StickRepeat
{
    /// <summary>The ticks a direction is held before it first repeats, and between repeats after.</summary>
    public const int DelayTicks = 30, EveryTicks = 5;

    /// <summary>The directions held, and the ticks until each repeats; null for one that never does, a key's.</summary>
    private readonly Dictionary<MenuAction, int?> _held = [];

    /// <summary>A direction is pressed, and held from now: it first repeats after <see cref="DelayTicks"/>.</summary>
    /// <param name="repeats">Whether it repeats here while held: the stick's and the D-pad's do, a key's doesn't.</param>
    public void Press(MenuAction direction, bool repeats = true)
    {
        if (direction.IsDirection())
            _held[direction] = repeats ? DelayTicks : null;
    }

    /// <summary>The direction is let go.</summary>
    public void Release(MenuAction direction) => _held.Remove(direction);

    /// <summary>Whether a direction is held, by the stick, the D-pad or a key.</summary>
    public bool Held(MenuAction direction) => _held.ContainsKey(direction);

    /// <summary>Forgets what's held, so nothing repeats until it's pressed again: as a menu opens or closes, or a mode that has no cursor runs.</summary>
    public void Clear() => _held.Clear();

    /// <summary>One tick: each direction held long enough repeats, calling <paramref name="repeat"/>, and waits <see cref="EveryTicks"/> for the next.</summary>
    public void Tick(Action<MenuAction> repeat)
    {
        foreach (var direction in MenuActions.Directions)
        {
            if (!_held.TryGetValue(direction, out var left) || left is not { } ticks)
                continue;
            if (--ticks <= 0)
            {
                repeat(direction);
                ticks = EveryTicks;
            }
            // What the repeat did may have let everything go.
            if (_held.ContainsKey(direction))
                _held[direction] = ticks;
        }
    }
}
