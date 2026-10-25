using Aidyn.Core.Controls;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The player's bindings (<see cref="ControlBindings"/>) as Godot's input actions, and Godot's input events as bindings for the
/// Controls page's prompt. Each button is an action named for its control; each direction is four actions, its sources'
/// directions, which the runtime reads by name (<see cref="Actions"/>).
/// </summary>
public static class InputBindings
{
    /// <summary>The deadzone every action has, as the runtime's always had.</summary>
    private const float Deadzone = 0.1f;

    /// <summary>How far a stick or trigger must go for the prompt to bind it.</summary>
    private const float AxisToBind = 0.6f;

    /// <summary>Move's sources that aren't Camera's: what moves him, with or without Camera Hold. Up, down, left, right.</summary>
    public static readonly string[] Move = ["move_forward", "move_back", "move_left", "move_right"];

    /// <summary>The sources both Move and Camera take: they move him, and with Camera Hold held steer the camera instead.</summary>
    public static readonly string[] Steer = ["steer_forward", "steer_back", "steer_left", "steer_right"];

    /// <summary>Camera's sources that aren't Move's, but the mouse: they steer the camera always.</summary>
    public static readonly string[] Look = ["look_up", "look_down", "look_left", "look_right"];

    /// <summary>The menus' cursor, which moves a conversation's choices too, and a battle's target and held menus.</summary>
    public static readonly string[] Menu = ["menu_up", "menu_down", "menu_left", "menu_right"],
        Target = ["target_up", "target_down", "target_left", "target_right"];

    /// <summary>Every action the bindings make: the buttons', and the directions'.</summary>
    public static readonly string[] Actions =
    [
        .. GameControls.All.Where(c => c.Kind == ControlKind.Button).Select(c => c.Id), .. Move, .. Steer, .. Look, .. Menu, .. Target,
    ];

    private static readonly Dictionary<string, JoyButton> Buttons = new()
    {
        ["A"] = JoyButton.A, ["B"] = JoyButton.B, ["X"] = JoyButton.X, ["Y"] = JoyButton.Y, ["LB"] = JoyButton.LeftShoulder,
        ["RB"] = JoyButton.RightShoulder, ["LS"] = JoyButton.LeftStick, ["RS"] = JoyButton.RightStick, ["Start"] = JoyButton.Start,
        ["View"] = JoyButton.Back, ["Guide"] = JoyButton.Guide, ["DPadUp"] = JoyButton.DpadUp, ["DPadDown"] = JoyButton.DpadDown,
        ["DPadLeft"] = JoyButton.DpadLeft, ["DPadRight"] = JoyButton.DpadRight,
    };

    private static readonly Dictionary<string, JoyAxis> Axes = new()
    {
        ["LeftX"] = JoyAxis.LeftX, ["LeftY"] = JoyAxis.LeftY, ["RightX"] = JoyAxis.RightX, ["RightY"] = JoyAxis.RightY,
        ["LeftTrigger"] = JoyAxis.TriggerLeft, ["RightTrigger"] = JoyAxis.TriggerRight,
    };

    private static readonly Dictionary<string, MouseButton> MouseButtons = new()
    {
        ["Left"] = MouseButton.Left, ["Right"] = MouseButton.Right, ["Middle"] = MouseButton.Middle, ["X1"] = MouseButton.Xbutton1,
        ["X2"] = MouseButton.Xbutton2, ["WheelUp"] = MouseButton.WheelUp, ["WheelDown"] = MouseButton.WheelDown,
    };

    /// <summary>Whether the mouse steers the camera always, captured in trek and a battle's turn: Camera takes it.</summary>
    public static bool MouseLook(ControlBindings bindings) => bindings.Get(GameControls.Camera).Contains(InputSource.Mouse);

    /// <summary>Makes the actions afresh from the bindings, as the game starts and whenever they change.</summary>
    public static void Apply(ControlBindings bindings)
    {
        foreach (var action in Actions)
        {
            if (!InputMap.HasAction(action))
                InputMap.AddAction(action, Deadzone);
            InputMap.ActionEraseEvents(action);
        }
        foreach (var control in GameControls.All.Where(c => c.Kind == ControlKind.Button))
        {
            foreach (var binding in bindings.Get(control.Id))
                Add(control.Id, binding);
        }
        var moving = bindings.Get(GameControls.Move);
        var looking = bindings.Get(GameControls.Camera);
        AddSources(Move, moving.Except(looking));
        AddSources(Steer, moving.Intersect(looking));
        AddSources(Look, looking.Except(moving));
        AddSources(Menu, bindings.Get(GameControls.MenuCursor));
        AddSources(Target, bindings.Get(GameControls.BattleTarget));
    }

    /// <summary>Adds each source's four directions to four actions, up, down, left and right.</summary>
    private static void AddSources(string[] actions, IEnumerable<string> sources)
    {
        foreach (var source in sources.Select(InputSource.Find).OfType<InputSource>())
        {
            for (var i = 0; i < source.Directions.Count; i++)
                Add(actions[i], source.Directions[i]);
        }
    }

    private static void Add(string action, string binding)
    {
        if (Event(binding) is { } e)
            InputMap.ActionAddEvent(action, e);
    }

    /// <summary>A binding as the event Godot matches an action's against, or null if it isn't one.</summary>
    public static InputEvent? Event(string binding)
    {
        var colon = binding.IndexOf(':');
        if (colon < 0)
            return null;
        var name = binding[(colon + 1)..];
        switch (binding[..colon])
        {
            case "Key" when Enum.TryParse<Key>(name, out var key) && key != Key.None:
                return new InputEventKey { PhysicalKeycode = key };
            case "Mouse" when MouseButtons.TryGetValue(name, out var button):
                return new InputEventMouseButton { ButtonIndex = button };
            case "Joy" when Buttons.TryGetValue(name, out var joy):
                return new InputEventJoypadButton { ButtonIndex = joy };
            case "Axis" when name.Length > 1 && Axes.TryGetValue(name[..^1], out var axis):
                return new InputEventJoypadMotion { Axis = axis, AxisValue = name[^1] == '-' ? -1 : 1 };
            default:
                return null;
        }
    }

    /// <summary>
    /// What the Controls page's prompt binds for an event: a key, a mouse button or the wheel, a controller's button, or a stick or
    /// trigger pushed well over, as it goes down; null for anything else, a key's repeat and a letting go among them.
    /// </summary>
    public static string? Of(InputEvent e) => e switch
    {
        InputEventKey { Pressed: true, Echo: false } key => "Key:" + (key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode),
        InputEventMouseButton { Pressed: true } mouse => MouseButtons.FirstOrDefault(p => p.Value == mouse.ButtonIndex).Key is { } name
            ? "Mouse:" + name
            : null,
        InputEventJoypadButton { Pressed: true } joy => Buttons.FirstOrDefault(p => p.Value == joy.ButtonIndex).Key is { } name
            ? "Joy:" + name
            : null,
        InputEventJoypadMotion motion when Math.Abs(motion.AxisValue) >= AxisToBind
            && Axes.FirstOrDefault(p => p.Value == motion.Axis).Key is { } name
            && (motion.AxisValue > 0 || !name.EndsWith("Trigger", StringComparison.Ordinal))
            => $"Axis:{name}{(motion.AxisValue < 0 ? '-' : '+')}",
        _ => null,
    };
}
