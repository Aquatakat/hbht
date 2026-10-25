using HBHT.Core.Controls;
using HBHT.Core.Ui;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Godot's input events as the game's buttons (<see cref="MenuAction"/>), by the controls of the situation the player is in
/// (<see cref="Situation"/>, <see cref="GameControls"/>): the stick, the D-pad, the keys and the triggers, as the original's widgets
/// read them (<c>Controller::ReadInput</c>, <c>BaseWidget::Control</c>). The stick and the triggers send an event whenever they
/// move, so a direction counts once as it crosses <see cref="Press"/>, and lets go below <see cref="Release"/>. Every event goes
/// through <see cref="Track"/> first, which also tells the session's <see cref="StickRepeat"/> (<see cref="Repeat"/>) of each
/// direction of the situation pressed and let go, so the core repeats a hold on its own ticks; a key's hold repeats as the system
/// repeats the key. This translates and nothing more: what a press does is the core's (<see cref="Core.GameSession.Press"/>).
/// </summary>
public sealed class MenuStick
{
    /// <summary>
    /// How far the stick or a trigger must go to count, and how far back to let go. The original counts a direction
    /// from 56 of the N64 stick's 80 (0.7, <c>Controller::ReadInput</c>), which the user read as 0.58 on a PC stick
    /// through the emulator, as ares scales a stick pushed all the way to about 100. The original has no hysteresis.
    /// </summary>
    public const float Press = 0.58f, Release = 0.5f;

    /// <summary>The actions an axis can drive: every button's, as any can be bound to a trigger, and the directions'.</summary>
    private static readonly string[] Tracked = InputBindings.Actions;

    /// <summary>The axes' actions past their threshold.</summary>
    private readonly HashSet<string> _held = [];
    /// <summary>The axes' actions the last event pressed, and let go.</summary>
    private readonly HashSet<string> _pressed = [], _released = [];

    /// <summary>The held directions the core repeats (<see cref="Core.GameSession.Stick"/>), told of each pressed and let go.</summary>
    public StickRepeat? Repeat { get; set; }

    /// <summary>The situation the player is in, whose controls the events are read by (<see cref="Situations.Of"/>).</summary>
    public Situation Situation { get; set; } = Situation.Trek;

    /// <summary>
    /// The situation's direction, as four actions, up, down, left and right: the menus' cursor, which a conversation's choices take
    /// too, or a battle's target; none in trek, where the stick walks.
    /// </summary>
    public string[] Directions => Situation switch
    {
        Situation.Menus or Situation.Talking => InputBindings.Menu,
        _ when Situation.HasFlag(Situation.Combat) => InputBindings.Target,
        _ => [],
    };

    /// <summary>
    /// Brings what's held up to date with an event: every action an axis drives, so one that's let go is let go even when no menu
    /// asks, and the situation's directions on the D-pad, which Godot doesn't repeat, and on the keys, which it does.
    /// </summary>
    public void Track(InputEvent e)
    {
        _pressed.Clear();
        _released.Clear();
        var directions = Directions;
        switch (e)
        {
            case InputEventJoypadMotion motion:
                foreach (var action in Tracked)
                    Update(motion, action, Array.IndexOf(directions, action));
                break;
            case InputEventJoypadButton or InputEventKey { Echo: false }:
                for (var i = 0; i < directions.Length; i++)
                {
                    if (!e.IsAction(directions[i]))
                        continue;
                    if (e.IsPressed())
                        Repeat?.Press(MenuActions.Directions[i], repeats: e is InputEventJoypadButton);
                    else
                        Repeat?.Release(MenuActions.Directions[i]);
                }
                break;
        }
    }

    private void Update(InputEventJoypadMotion motion, string action, int direction)
    {
        if (!motion.IsAction(action))
            return;
        var past = _held.Contains(action) ? Release : Press;
        if (!motion.IsActionPressed(action) || Math.Abs(motion.AxisValue) < past)
        {
            if (!_held.Remove(action))
                return;
            _released.Add(action);
            if (direction >= 0)
                Repeat?.Release(MenuActions.Directions[direction]);
        }
        else if (_held.Add(action))
        {
            _pressed.Add(action);
            if (direction >= 0)
                Repeat?.Press(MenuActions.Directions[direction]);
        }
    }

    /// <summary>Whether an event presses an action, a key's repeats among them: for the axes, as <see cref="Track"/> found it.</summary>
    public bool Pressed(InputEvent e, string action) =>
        e is InputEventJoypadMotion ? _pressed.Contains(action) : e.IsActionPressed(action, allowEcho: true);

    /// <summary>Whether an event presses an action, but not a key's repeat.</summary>
    private bool PressedOnce(InputEvent e, string action) =>
        e is InputEventJoypadMotion ? _pressed.Contains(action) : e.IsActionPressed(action);

    private bool Released(InputEvent e, string action) =>
        e is InputEventJoypadMotion ? _released.Contains(action) : e.IsActionReleased(action);

    /// <summary>
    /// What an event means to the game in the situation: the button or direction it presses, or <see cref="MenuAction.None"/>. A
    /// button counts once, but the tabs, which repeat as a key does; the stick and the triggers count as <see cref="Pressed"/> finds
    /// them, and the situation's directions take their sources alike, a key repeating as the system repeats it. Every event goes
    /// through <see cref="Track"/> first, and one translates the same however often it's asked.
    /// </summary>
    public MenuAction Translate(InputEvent e)
    {
        foreach (var control in GameControls.In(Situation))
        {
            if (control is not { Kind: ControlKind.Button, Press: { } press })
                continue;
            var repeats = press is MenuAction.TabPrevious or MenuAction.TabNext;
            if (repeats ? Pressed(e, control.Id) : PressedOnce(e, control.Id))
                return press;
            if (control.Release is { } release && Released(e, control.Id))
                return release;
        }
        var directions = Directions;
        for (var i = 0; i < directions.Length; i++)
        {
            if (Pressed(e, directions[i]))
                return MenuActions.Directions[i];
        }
        return MenuAction.None;
    }
}
