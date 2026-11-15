using HBHT.Core.Ui;
using Godot;

namespace HBHT.Views;

/// <summary>
/// The mouse in the menus (ADR 0040): a left click takes what's under it, which a view does by putting the cursor there and
/// pressing A through its host, so the click does just what A would, sounds and all; the wheel scrolls the view under it.
/// </summary>
public static class UiPointer
{
    /// <summary>
    /// The host's press, which sends a press from the pointer to whatever takes the controls' presses: the runtime's session,
    /// or the editor's window panel. Null while nothing would take one.
    /// </summary>
    public static Action<MenuAction>? Host { get; set; }

    /// <summary>
    /// The host's way to show a member a click on a small portrait picked, so far round the party from the one shown, in one
    /// change: the runtime's session (<c>GameSession.ShowMember</c>), or the editor's window.
    /// </summary>
    public static Action<int>? MemberHost { get; set; }

    /// <summary>
    /// Whether the mouse is being used, so the menus show what only it needs, such as the arrows that step a list by a click:
    /// from the mouse's moving or a button of it until a controller's button or stick is. The keyboard leaves it, since it's
    /// played beside the mouse.
    /// </summary>
    public static bool InUse { get; set; }

    /// <summary>Keeps <see cref="InUse"/> by an event, whatever takes it.</summary>
    public static void Track(InputEvent e)
    {
        if (e is InputEventMouseMotion or InputEventMouseButton)
            InUse = true;
        else if (e is InputEventJoypadButton { Pressed: true } or InputEventJoypadMotion { AxisValue: > 0.5f or < -0.5f })
            InUse = false;
    }

    /// <summary>Whether an event is the left button going down.</summary>
    public static bool Clicked(InputEvent e) => e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left };

    /// <summary>The wheel's turn in an event, a row up (−1) or down (1), or 0 if it isn't one.</summary>
    public static int Wheel(InputEvent e) => e switch
    {
        InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp } => -1,
        InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown } => 1,
        _ => 0,
    };

    /// <summary>Presses a button through the host, as a key bound to it would.</summary>
    public static void Press(MenuAction action) => Host?.Invoke(action);

    /// <summary>Shows the member so far round the party from the one shown, through the host.</summary>
    public static void ShowMember(int steps) => MemberHost?.Invoke(steps);

    /// <summary>
    /// Which way a click on a point of a pause tab's frame changes tab by where it is, if it does: on the tab beside it, beyond
    /// the frame's sides on a wider screen, from the scroll's top to its foot.
    /// </summary>
    public static int? TabBeside(Vector2 at) => at.Y < 30 || at.Y >= 210 ? null : at.X < 0 ? -1 : at.X >= 320 ? 1 : null;

    /// <summary>Presses A through the host: what a click on a row or a choice comes to once the cursor is on it.</summary>
    public static void Accept() => Press(MenuAction.Accept);

    /// <summary>
    /// Which of a column of laid-out choices is under a point: each reaches down to the next one's top, the last to the bottom of
    /// its bounds, all within their bounds' sides.
    /// </summary>
    public static int? BlockAt(IReadOnlyList<TextBlock> blocks, Vector2 at)
    {
        for (var i = 0; i < blocks.Count; i++)
        {
            var block = blocks[i];
            var bottom = i + 1 < blocks.Count ? blocks[i + 1].Y : Math.Max(block.Bounds.Bottom, block.Y + 1);
            if (at.Y >= block.Y && at.Y < bottom && at.X >= block.Bounds.Left && at.X < block.Bounds.Right)
                return i;
        }
        return null;
    }
}
