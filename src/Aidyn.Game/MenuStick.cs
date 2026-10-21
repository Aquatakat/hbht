using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The stick, the D-pad and the triggers in menus, the pause menu's and trek's popups and conversations alike, as the
/// original's widgets read them (<c>Controller::ReadInput</c>, <c>BaseWidget::Control</c>). The stick and the triggers
/// send an event whenever they move, so a direction counts once as it crosses <see cref="Press"/>, and lets go below
/// <see cref="Release"/>. Held, the stick's and the D-pad's directions repeat in <see cref="Step"/>. Keys repeat as the
/// system repeats them. Every event goes through <see cref="Track"/> first.
/// </summary>
public sealed class MenuStick
{
    /// <summary>
    /// How long a direction held waits before the cursor moves on by itself, and how often it does then, in seconds:
    /// the original's widgets count 30 of the controller's samples, 60 a second, then 5 (<c>BaseWidget::Control</c>,
    /// <c>0x800ad708</c> in 1.0). The user timed about 0.7 s and 12 a second.
    /// </summary>
    public const double RepeatDelay = 30.0 / 60, RepeatEvery = 5.0 / 60;

    /// <summary>
    /// How far the stick or a trigger must go to count, and how far back to let go. The original counts a direction
    /// from 56 of the N64 stick's 80 (0.7, <c>Controller::ReadInput</c>), which the user read as 0.58 on a PC stick
    /// through the emulator, as ares scales a stick pushed all the way to about 100. The original has no hysteresis.
    /// </summary>
    public const float Press = 0.58f, Release = 0.5f;

    /// <summary>The stick's directions, which repeat as they're held.</summary>
    public static readonly string[] Directions = ["move_forward", "move_back", "move_left", "move_right"];

    /// <summary>The axes' actions the menus read: the stick's directions, and the triggers that change tab.</summary>
    private static readonly string[] Tracked = [.. Directions, "menu_tab_previous", "menu_tab_next"];

    /// <summary>The axes' actions and the directions held, and how long until each repeats.</summary>
    private readonly Dictionary<string, double> _held = [];
    /// <summary>The axes' actions the last event pressed.</summary>
    private readonly HashSet<string> _pressed = [];

    /// <summary>
    /// Brings what's held up to date with an event: every action an axis drives, so one that's let go is let go even
    /// when no menu asks, and the D-pad's directions, which Godot doesn't repeat.
    /// </summary>
    public void Track(InputEvent e)
    {
        _pressed.Clear();
        switch (e)
        {
            case InputEventJoypadMotion motion:
                foreach (var action in Tracked)
                    Update(motion, action);
                break;
            case InputEventJoypadButton button:
                foreach (var direction in Directions)
                {
                    if (!button.IsAction(Dpad(direction)))
                        continue;
                    if (button.Pressed)
                        _held[direction] = RepeatDelay;
                    else
                        _held.Remove(direction);
                }
                break;
        }
    }

    private void Update(InputEventJoypadMotion motion, string action)
    {
        if (!motion.IsAction(action))
            return;
        var past = _held.ContainsKey(action) ? Release : Press;
        if (!motion.IsActionPressed(action) || Math.Abs(motion.AxisValue) < past)
            _held.Remove(action);
        else if (_held.TryAdd(action, RepeatDelay))
            _pressed.Add(action);
    }

    /// <summary>Whether an event presses an action: for the axes, as <see cref="Track"/> found it.</summary>
    public bool Pressed(InputEvent e, string action) =>
        e is InputEventJoypadMotion ? _pressed.Contains(action) : e.IsActionPressed(action, allowEcho: true);

    /// <summary>
    /// What an event means to a menu: the button or direction it presses, or <see cref="MenuAction.None"/>. The stick and the
    /// triggers count as <see cref="Pressed"/> finds them, and up, down, left and right take the movement keys and the
    /// D-pad alike, a key repeating as the system repeats it. Every event goes through <see cref="Track"/> first, and one
    /// translates the same however often it's asked.
    /// </summary>
    public MenuAction Translate(InputEvent e)
    {
        if (e.IsActionPressed("menu_accept"))
            return MenuAction.Accept;
        if (e.IsActionPressed("menu_back"))
            return MenuAction.Back;
        if (e.IsActionPressed("pause"))
            return MenuAction.Start;
        if (e.IsActionPressed("menu_details"))
            return MenuAction.Details;
        if (e.IsActionReleased("menu_details"))
            return MenuAction.DetailsRelease;
        if (e.IsActionPressed("menu_appraise"))
            return MenuAction.Appraise;
        if (e.IsActionReleased("menu_appraise"))
            return MenuAction.AppraiseRelease;
        if (e.IsActionPressed("menu_member_left"))
            return MenuAction.MemberLeft;
        if (e.IsActionPressed("menu_member_right"))
            return MenuAction.MemberRight;
        if (Pressed(e, "menu_tab_previous"))
            return MenuAction.TabPrevious;
        if (Pressed(e, "menu_tab_next"))
            return MenuAction.TabNext;
        foreach (var direction in Directions)
        {
            if (Pressed(e, direction) || e.IsActionPressed(Dpad(direction), allowEcho: true))
                return Direction(direction);
        }
        return MenuAction.None;
    }

    /// <summary>The menu action for a stick direction, as <see cref="Directions"/> names it.</summary>
    public static MenuAction Direction(string direction) => direction switch
    {
        "move_forward" => MenuAction.Up,
        "move_back" => MenuAction.Down,
        "move_left" => MenuAction.Left,
        _ => MenuAction.Right,
    };

    /// <summary>Which way a menu action moves a cursor along a list: -1 for up, 1 for down, else 0.</summary>
    public static int Vertical(MenuAction action) => action switch { MenuAction.Up => -1, MenuAction.Down => 1, _ => 0 };

    /// <summary>Which way a menu action moves sideways: -1 for left, 1 for right, else 0.</summary>
    public static int Horizontal(MenuAction action) => action switch { MenuAction.Left => -1, MenuAction.Right => 1, _ => 0 };

    /// <summary>Whether a direction is held past the threshold by the stick, or by a key or the D-pad.</summary>
    public static bool Held(string action) => Input.GetActionRawStrength(action) >= Press;

    /// <summary>Forgets what's held, so nothing repeats until it's pressed again.</summary>
    public void Clear() => _held.Clear();

    /// <summary>Moves the held directions' repeats on by <paramref name="delta"/> seconds, calling <paramref name="repeat"/> for each due.</summary>
    public void Step(double delta, Action<string> repeat)
    {
        foreach (var action in Directions)
        {
            if (!_held.TryGetValue(action, out var left))
                continue;
            left -= delta;
            if (left <= 0)
            {
                repeat(action);
                left += RepeatEvery;
            }
            _held[action] = left;
        }
    }

    /// <summary>The D-pad's action for a stick direction.</summary>
    public static string Dpad(string direction) => direction switch
    {
        "move_forward" => "menu_up",
        "move_back" => "menu_down",
        "move_left" => "menu_left",
        _ => "menu_right",
    };
}
