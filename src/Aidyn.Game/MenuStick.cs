using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Godot's input events as the game's buttons (<see cref="MenuAction"/>): the stick, the D-pad, the keys and the triggers,
/// as the original's widgets read them (<c>Controller::ReadInput</c>, <c>BaseWidget::Control</c>). The stick and the
/// triggers send an event whenever they move, so a direction counts once as it crosses <see cref="Press"/>, and lets go
/// below <see cref="Release"/>. Every event goes through <see cref="Track"/> first, which also tells the session's
/// <see cref="StickRepeat"/> (<see cref="Repeat"/>) of each direction pressed and let go, so the core repeats a hold on
/// its own ticks; a key's hold repeats as the system repeats the key. This translates and nothing more: what a press does
/// is the core's (<see cref="Core.GameSession.Press"/>).
/// </summary>
public sealed class MenuStick
{
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

    /// <summary>The axes' actions past their threshold.</summary>
    private readonly HashSet<string> _held = [];
    /// <summary>The axes' actions the last event pressed.</summary>
    private readonly HashSet<string> _pressed = [];

    /// <summary>The held directions the core repeats (<see cref="Core.GameSession.Stick"/>), told of each pressed and let go.</summary>
    public StickRepeat? Repeat { get; set; }

    /// <summary>
    /// Brings what's held up to date with an event: every action an axis drives, so one that's let go is let go even
    /// when no menu asks, the D-pad's directions, which Godot doesn't repeat, and the keys', which it does.
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
                        Repeat?.Press(Direction(direction));
                    else
                        Repeat?.Release(Direction(direction));
                }
                break;
            case InputEventKey { Echo: false } key:
                foreach (var direction in Directions)
                {
                    if (!key.IsAction(direction))
                        continue;
                    if (key.Pressed)
                        Repeat?.Press(Direction(direction), repeats: false);
                    else
                        Repeat?.Release(Direction(direction));
                }
                break;
        }
    }

    private void Update(InputEventJoypadMotion motion, string action)
    {
        if (!motion.IsAction(action))
            return;
        var past = _held.Contains(action) ? Release : Press;
        if (!motion.IsActionPressed(action) || Math.Abs(motion.AxisValue) < past)
        {
            if (_held.Remove(action) && Directions.Contains(action))
                Repeat?.Release(Direction(action));
        }
        else if (_held.Add(action))
        {
            _pressed.Add(action);
            if (Directions.Contains(action))
                Repeat?.Press(Direction(action));
        }
    }

    /// <summary>Whether an event presses an action: for the axes, as <see cref="Track"/> found it.</summary>
    public bool Pressed(InputEvent e, string action) =>
        e is InputEventJoypadMotion ? _pressed.Contains(action) : e.IsActionPressed(action, allowEcho: true);

    /// <summary>
    /// What an event means to the game: the button or direction it presses, or <see cref="MenuAction.None"/>. The stick and the
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

    /// <summary>The D-pad's action for a stick direction.</summary>
    public static string Dpad(string direction) => direction switch
    {
        "move_forward" => "menu_up",
        "move_back" => "menu_down",
        "move_left" => "menu_left",
        _ => "menu_right",
    };
}
