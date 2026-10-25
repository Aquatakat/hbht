namespace HBHT.Core.Controls;

/// <summary>
/// A set of inputs bound as one, four directions at once, which a direction control takes or doesn't (<see cref="ControlKind.Sources"/>):
/// WASD, the arrows or the mouse on the keyboard, either stick or the D-pad on a controller.
/// </summary>
/// <param name="Id">Its binding, <c>Source:WASD</c>.</param>
/// <param name="Label">Its name, in Title Case.</param>
/// <param name="Directions">The bindings for up, down, left and right, or none for the mouse, which moves rather than presses.</param>
public sealed record InputSource(string Id, ControlDevice Device, string Label, IReadOnlyList<string> Directions)
{
    public const string Wasd = "Source:WASD", Arrows = "Source:Arrows", Mouse = "Source:Mouse", LeftStick = "Source:LeftStick",
        RightStick = "Source:RightStick", DPad = "Source:DPad";

    /// <summary>The mouse's movement, which only the mouse's source takes.</summary>
    public const string MouseMotion = "Mouse:Motion";

    public static readonly IReadOnlyList<InputSource> All =
    [
        new(Wasd, ControlDevice.Keyboard, "WASD", ["Key:W", "Key:S", "Key:A", "Key:D"]),
        new(Arrows, ControlDevice.Keyboard, "Arrows", ["Key:Up", "Key:Down", "Key:Left", "Key:Right"]),
        new(Mouse, ControlDevice.Keyboard, "Mouse", []),
        new(LeftStick, ControlDevice.Controller, "Left Stick", ["Axis:LeftY-", "Axis:LeftY+", "Axis:LeftX-", "Axis:LeftX+"]),
        new(RightStick, ControlDevice.Controller, "Right Stick", ["Axis:RightY-", "Axis:RightY+", "Axis:RightX-", "Axis:RightX+"]),
        new(DPad, ControlDevice.Controller, "D-Pad", ["Joy:DPadUp", "Joy:DPadDown", "Joy:DPadLeft", "Joy:DPadRight"]),
    ];

    public static InputSource? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>The single inputs it covers, which no button in the same situation may share.</summary>
    public IReadOnlyList<string> Inputs => Directions.Count > 0 ? Directions : [MouseMotion];
}

/// <summary>
/// A binding, as <see cref="ControlBindings"/> keeps it and <c>settings.json</c> holds it: <c>Key:W</c> (Godot's physical key's name),
/// <c>Mouse:Left</c>, <c>Joy:A</c> (the Xbox controller's names), <c>Axis:LeftX-</c> (an axis pushed one way), or a source,
/// <c>Source:WASD</c> (<see cref="InputSource"/>). The runtime turns them into Godot's input events, and these name them for the page.
/// </summary>
public static class Binding
{
    /// <summary>The mouse's buttons and wheel, by binding name: what the page calls them.</summary>
    private static readonly Dictionary<string, string> MouseNames = new()
    {
        ["Left"] = "Left Click", ["Right"] = "Right Click", ["Middle"] = "Middle Click", ["X1"] = "Mouse 4", ["X2"] = "Mouse 5",
        ["WheelUp"] = "Wheel Up", ["WheelDown"] = "Wheel Down", ["Motion"] = "Mouse",
    };

    /// <summary>A controller's buttons, by binding name, in the Xbox controller's names.</summary>
    private static readonly Dictionary<string, string> JoyNames = new()
    {
        ["A"] = "A", ["B"] = "B", ["X"] = "X", ["Y"] = "Y", ["LB"] = "LB", ["RB"] = "RB", ["LS"] = "LS", ["RS"] = "RS",
        ["Start"] = "Start", ["View"] = "View", ["Guide"] = "Guide", ["DPadUp"] = "D-Pad Up", ["DPadDown"] = "D-Pad Down",
        ["DPadLeft"] = "D-Pad Left", ["DPadRight"] = "D-Pad Right",
    };

    /// <summary>A controller's axes pushed one way, by binding name: the triggers are LT and RT.</summary>
    private static readonly Dictionary<string, string> AxisNames = new()
    {
        ["LeftX-"] = "Left Stick Left", ["LeftX+"] = "Left Stick Right", ["LeftY-"] = "Left Stick Up", ["LeftY+"] = "Left Stick Down",
        ["RightX-"] = "Right Stick Left", ["RightX+"] = "Right Stick Right", ["RightY-"] = "Right Stick Up", ["RightY+"] = "Right Stick Down",
        ["LeftTrigger+"] = "LT", ["RightTrigger+"] = "RT",
    };

    /// <summary>Keys whose Godot names aren't what's printed on them.</summary>
    private static readonly Dictionary<string, string> KeyNames = new()
    {
        ["Up"] = "Up Arrow", ["Down"] = "Down Arrow", ["Left"] = "Left Arrow", ["Right"] = "Right Arrow",
        ["Pageup"] = "Page Up", ["Pagedown"] = "Page Down", ["Capslock"] = "Caps Lock", ["Numlock"] = "Num Lock", ["Scrolllock"] = "Scroll Lock",
        ["Quoteleft"] = "`", ["Apostrophe"] = "'", ["Minus"] = "-", ["Equal"] = "=", ["Bracketleft"] = "[", ["Bracketright"] = "]",
        ["Backslash"] = "\\", ["Semicolon"] = ";", ["Comma"] = ",", ["Period"] = ".", ["Slash"] = "/", ["KpMultiply"] = "Keypad *",
        ["KpDivide"] = "Keypad /", ["KpSubtract"] = "Keypad -", ["KpAdd"] = "Keypad +", ["KpPeriod"] = "Keypad .", ["KpEnter"] = "Keypad Enter",
    };

    /// <summary>The F-keys the debug keys use, which a control can't take (F3, F5–F11).</summary>
    public static readonly IReadOnlySet<string> Reserved = new HashSet<string>(
        ["Key:F3", "Key:F5", "Key:F6", "Key:F7", "Key:F8", "Key:F9", "Key:F10", "Key:F11"]);

    /// <summary>The device a binding is on, or null if it isn't one.</summary>
    public static ControlDevice? DeviceOf(string binding) => Split(binding) switch
    {
        ("Source", _) => InputSource.Find(binding)?.Device,
        ("Key", { Length: > 0 } key) when key.All(char.IsAsciiLetterOrDigit) => ControlDevice.Keyboard,
        ("Mouse", var button) when MouseNames.ContainsKey(button) => ControlDevice.Keyboard,
        ("Joy", var button) when JoyNames.ContainsKey(button) => ControlDevice.Controller,
        ("Axis", var axis) when AxisNames.ContainsKey(axis) => ControlDevice.Controller,
        _ => null,
    };

    /// <summary>Whether a binding is one of the forms above.</summary>
    public static bool IsValid(string binding) => DeviceOf(binding) is not null;

    /// <summary>Whether a binding is a source rather than a single input.</summary>
    public static bool IsSource(string binding) => binding.StartsWith("Source:", StringComparison.Ordinal);

    /// <summary>What the page calls a binding: "W", "Space", "Left Click", "LB", "D-Pad Up", "Left Stick Left", "WASD".</summary>
    public static string Name(string binding) => Split(binding) switch
    {
        ("Source", _) => InputSource.Find(binding)?.Label ?? binding,
        ("Mouse", var button) => MouseNames.GetValueOrDefault(button, button),
        ("Joy", var button) => JoyNames.GetValueOrDefault(button, button),
        ("Axis", var axis) => AxisNames.GetValueOrDefault(axis, axis),
        ("Key", var key) => KeyName(key),
        _ => binding,
    };

    /// <summary>
    /// What the page shows for a cell: its bindings' names joined, "Both Sticks" for the two sticks, or "None".
    /// </summary>
    public static string Describe(IEnumerable<string> bindings)
    {
        var list = bindings.ToList();
        if (list.Count == 0)
            return "None";
        var names = new List<string>();
        foreach (var binding in list)
        {
            if (binding == InputSource.RightStick && list.Contains(InputSource.LeftStick))
                continue;
            names.Add(binding == InputSource.LeftStick && list.Contains(InputSource.RightStick) ? "Both Sticks" : Name(binding));
        }
        return string.Join(", ", names);
    }

    /// <summary>The single inputs a binding covers: itself, or a source's directions.</summary>
    public static IEnumerable<string> Inputs(string binding) => IsSource(binding) ? InputSource.Find(binding)?.Inputs ?? [] : [binding];

    private static string KeyName(string key)
    {
        if (KeyNames.TryGetValue(key, out var name))
            return name;
        // Key0 to Key9 are the digits; Kp0 to Kp9 the keypad's.
        if (key.Length == 4 && key.StartsWith("Key", StringComparison.Ordinal) && char.IsAsciiDigit(key[3]))
            return key[3..];
        if (key.Length == 3 && key.StartsWith("Kp", StringComparison.Ordinal) && char.IsAsciiDigit(key[2]))
            return "Keypad " + key[2];
        // The rest, as Godot names them, split into words: "Insert", "Home".
        return string.Concat(key.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));
    }

    private static (string Kind, string Name) Split(string binding) =>
        binding.IndexOf(':') is var colon and > 0 ? (binding[..colon], binding[(colon + 1)..]) : ("", binding);
}
