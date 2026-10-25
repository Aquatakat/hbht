using Aidyn.Core;
using Aidyn.Core.Controls;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Game;

public partial class Game
{
    /// <summary>Mouse drag in pixels to stick units at Mouse Sensitivity 1x: about 0.3° of orbit, and 0.02 of height, a pixel.</summary>
    private static readonly Vector2 DragToStick = new(0.2f, 0.4f);

    /// <summary>The bindings' version the input actions were last made from (<see cref="ControlBindings.Version"/>).</summary>
    private int? _controlsApplied;

    /// <summary>
    /// The input actions: the debug keys', which aren't the player's to bind, and the player's controls', made from the bindings
    /// (<see cref="InputBindings.Apply"/>) now and again whenever they change.
    /// </summary>
    private void AddInputs()
    {
        Add("fly_up", Key.Space);
        Add("fly_down", Key.C);
        Add("toggle_fly", Key.F);
        Add("fast_forward", null, JoyButton.LeftStick);
        if (InputMap.ActionGetEvents("fast_forward").Count < 2)
            InputMap.ActionAddEvent("fast_forward", new InputEventJoypadButton { ButtonIndex = JoyButton.RightStick });
        Add("toggle_overlay", Key.F3);
        Add("toggle_pace", Key.F5);
        Add("toggle_sky_placement", Key.F6);
        Add("cycle_ui_lines", Key.F7);
        Add("quick_save", Key.F8);
        Add("toggle_radar_anchor", Key.F9);
        Add("battle_rout", Key.F10);
        Add("battle_collapse", Key.F11);
        ApplyControls();
        _settings.Changed += ApplyControls;

        static void Add(string action, Key? key, JoyButton? button = null)
        {
            if (InputMap.HasAction(action))
                return;
            InputMap.AddAction(action, 0.1f);
            if (key is { } k)
                InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = k });
            if (button is { } b)
                InputMap.ActionAddEvent(action, new InputEventJoypadButton { ButtonIndex = b });
        }
    }

    /// <summary>Makes the controls' actions again if the bindings changed.</summary>
    private void ApplyControls()
    {
        var bindings = _settings.Controls;
        if (_controlsApplied == bindings.Version)
            return;
        InputBindings.Apply(bindings);
        _controlsApplied = bindings.Version;
    }

    /// <summary>
    /// The controls as the original reads them: the stick walks under 80% tilt and runs above, and R with the stick orbits the
    /// camera, holding him still. On a PC, Shift walks and Ctrl sneaks, and the camera turns by the sources Camera takes alone, by
    /// Camera Left and Right, and, while Camera Hold is held, by the sources Move and Camera share (instead of moving him) and the
    /// mouse; with the mouse on Camera, the mouse always turns it (ADR 0026).
    /// </summary>
    private PlayerInput ReadInput()
    {
        var hold = Input.IsActionPressed(GameControls.CameraHold);
        var own = Vector(InputBindings.Move);
        var shared = Vector(InputBindings.Steer);
        var move = hold ? own : (own + shared).LimitLength(1);
        var stick = new System.Numerics.Vector2(move.X, -move.Y);
        if (Input.IsActionPressed(GameControls.Walk) && stick.Length() > 0.5f)
            stick = System.Numerics.Vector2.Normalize(stick) * 0.5f;

        var orbit = false;
        var orbitStick = System.Numerics.Vector2.Zero;
        // What turns the camera while what moves him still does: Camera's own sources and its turning keys.
        var look = Vector(InputBindings.Look) + new Vector2(Input.GetAxis(GameControls.CameraLeft, GameControls.CameraRight), 0);
        if (look != Vector2.Zero)
        {
            orbit = true;
            orbitStick += new System.Numerics.Vector2(look.X, -look.Y);
        }
        // Camera Hold, as the N64's R: what moves him and Camera shares steers instead.
        if (hold)
        {
            orbit = true;
            orbitStick += new System.Numerics.Vector2(shared.X, -shared.Y);
        }
        var drag = _camera!.TakeDrag();
        if (_camera.MouseSteers)
        {
            orbit = true;
            var scale = DragToStick * _settings.MouseSensitivity;
            orbitStick += new System.Numerics.Vector2(drag.X * scale.X, drag.Y * scale.Y);
        }
        // He stands still only while what would move him steers, as with R; a source of Move's alone still walks him.
        var free = !(hold && shared != Vector2.Zero) || own != Vector2.Zero;
        return new PlayerInput(stick, Input.IsActionPressed(GameControls.Sneak), orbit, orbitStick, Free: free);

        // Up, down, left and right as Godot's vector: up is negative y.
        static Vector2 Vector(string[] actions) => Input.GetVector(actions[2], actions[3], actions[0], actions[1]);
    }

    /// <summary>
    /// With the mouse on Camera, the mouse is captured in trek and a battle, where it turns the camera, and let go in the menus,
    /// a conversation, a cut-scene and the free camera, and while the window isn't in front.
    /// </summary>
    private void CaptureMouse()
    {
        var look = InputBindings.MouseLook(_settings.Controls) && _session is { Mode: GameMode.Trek or GameMode.Combat }
            && _camera is { Flying: false } && GetWindow().HasFocus();
        var mode = look ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != mode)
            Input.MouseMode = mode;
        _camera?.MouseLook = look;
    }

    /// <summary>
    /// While the options' Controls page waits for an input to bind (<see cref="OptionsMenu.Prompt"/>), it takes every event: one
    /// of its device binds, any other is passed over, and nothing reaches the menus, so the key being bound can't also cancel or
    /// move anything. Whether it took the event.
    /// </summary>
    private bool BindPrompted(GameSession session, InputEvent e)
    {
        if (session.Mode != GameMode.Menu || session.Menu.System is not { Page: SystemPage.Options, Options: { Prompt: not null } options })
            return false;
        if (InputBindings.Of(e) is { } binding)
            options.Bind(binding);
        _menu!.Redraw();
        GetViewport().SetInputAsHandled();
        return true;
    }
}
