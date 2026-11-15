using HBHT.Core;
using HBHT.Core.Controls;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using Godot;

namespace HBHT.Game;

public partial class Game
{
    /// <summary>Mouse drag in pixels to stick units at Mouse Sensitivity 1x: about 0.3° of orbit, and 0.02 of height, a pixel.</summary>
    private static readonly Vector2 DragToStick = new(0.2f, 0.4f);

    /// <summary>The mouse's movement in pixels to a free cursor's frames of the stick's step at Mouse Sensitivity 1x: 40 pixels a unit of the arena.</summary>
    private const float AimPerPixel = 1 / 32f;

    /// <summary>Mouse movement across, in pixels at Mouse Sensitivity 1x, that steps an aim's target once.</summary>
    private const float PixelsPerTarget = 80;

    /// <summary>The sideways movement gathered toward the next step of an aim's target.</summary>
    private float _targetDrag;

    /// <summary>The bindings' version the input actions were last made from (<see cref="ControlBindings.Version"/>).</summary>
    private int? _controlsApplied;

    /// <summary>
    /// The input actions: the debug keys', which aren't the player's to bind, and the player's controls', made from the bindings
    /// (<see cref="InputBindings.Apply"/>) now and again whenever they change. A release build has the debug keys' actions with
    /// nothing bound to them (<see cref="Debugging"/>), so the launch options can still press them.
    /// </summary>
    private void AddInputs()
    {
        Add("fly_up", Key.Space);
        Add("fly_down", Key.C);
        Add("toggle_fly", Key.F);
        Add("fast_forward", null, JoyButton.LeftStick);
        if (Debugging && InputMap.ActionGetEvents("fast_forward").Count < 2)
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
            if (!Debugging)
                return;
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

    /// <summary>An input action's name as Godot's, made once: a string handed to Godot is made into one each call, every tick.</summary>
    private static StringName Action(string name)
    {
        if (!ActionNames.TryGetValue(name, out var action))
            ActionNames[name] = action = name;
        return action;
    }

    private static readonly Dictionary<string, StringName> ActionNames = [];

    /// <summary>
    /// The controls as the original reads them: the stick walks under 80% tilt and runs above, and R with the stick orbits the
    /// camera, holding him still. On a PC, Shift walks and Ctrl sneaks, and the camera turns by the sources Camera takes alone, by
    /// Camera Left and Right, and, while Camera Hold is held, by the sources Move and Camera share (instead of moving him) and the
    /// mouse; with the mouse on Camera, the mouse always turns it (ADR 0026).
    /// </summary>
    private PlayerInput ReadInput()
    {
        var hold = Input.IsActionPressed(Action(GameControls.CameraHold));
        var own = Vector(InputBindings.Move);
        var sharedKeys = Vector(InputBindings.Steer);
        var sharedPad = Vector(InputBindings.SteerPad);
        var shared = (sharedKeys + sharedPad).LimitLength(1);
        // Invert Camera Y turns the controller's up and down on the camera, not his walking.
        var padY = _settings.InvertCameraY ? new Vector2(1, -1) : Vector2.One;
        var move = hold ? own : (own + shared).LimitLength(1);
        var stick = new System.Numerics.Vector2(move.X, -move.Y);
        if (Input.IsActionPressed(Action(GameControls.Walk)) && stick.Length() > 0.5f)
            stick = System.Numerics.Vector2.Normalize(stick) * 0.5f;
        _stickTilt = stick.Length();

        var orbit = false;
        var orbitStick = System.Numerics.Vector2.Zero;
        // What turns the camera while what moves him still does: Camera's own sources and its turning keys.
        var look = Vector(InputBindings.Look) + Vector(InputBindings.LookPad) * padY
            + new Vector2(Input.GetAxis(Action(GameControls.CameraLeft), Action(GameControls.CameraRight)), 0);
        if (look != Vector2.Zero)
        {
            orbit = true;
            orbitStick += new System.Numerics.Vector2(look.X, -look.Y);
        }
        // Camera Hold, as the N64's R: what moves him and Camera shares steers instead.
        if (hold)
        {
            orbit = true;
            var steer = (sharedKeys + sharedPad * padY).LimitLength(1);
            orbitStick += new System.Numerics.Vector2(steer.X, -steer.Y);
        }
        var drag = _camera!.TakeDrag();
        // A thrown weapon's or an area spell's free cursor takes the mouse instead of the camera, which the battle holds as it aims.
        var aim = Vector2.Zero;
        if (_camera.MouseSteers && Battle?.State is Core.Combat.CombatState.TargetThrown or Core.Combat.CombatState.PlaceArea
            && _session is { Mode: GameMode.Combat })
        {
            aim = new Vector2(drag.X, -drag.Y) * AimPerPixel * _settings.MouseSensitivity;
        }
        else if (_camera.MouseSteers)
        {
            orbit = true;
            var scale = DragToStick * _settings.MouseSensitivity * (_settings.InvertMouseY ? new Vector2(1, -1) : Vector2.One);
            orbitStick += new System.Numerics.Vector2(drag.X * scale.X, drag.Y * scale.Y);
        }
        // He stands still only while what would move him steers, as with R; a source of Move's alone still walks him.
        var free = !(hold && shared != Vector2.Zero) || own != Vector2.Zero;
        return new PlayerInput(stick, Input.IsActionPressed(Action(GameControls.Sneak)), orbit, orbitStick, Free: free,
            Aim: new System.Numerics.Vector2(aim.X, aim.Y), Held: hold);

        // Up, down, left and right as Godot's vector: up is negative y.
        static Vector2 Vector(string[] actions) => Input.GetVector(Action(actions[2]), Action(actions[3]), Action(actions[0]), Action(actions[1]));
    }

    /// <summary>
    /// With the mouse on Camera, the mouse is captured in trek and a battle, where it turns the camera, and let go in the menus,
    /// a conversation, a cut-scene and the free camera, and while the window isn't in front.
    /// </summary>
    private void CaptureMouse()
    {
        // A battle's held menus let it go, so the pointer chooses in them.
        var look = InputBindings.MouseLook(_settings.Controls) && _session is { Mode: GameMode.Trek or GameMode.Combat }
            && Battle?.Menu is null && _camera is { Flying: false } && GetWindow().HasFocus();
        var mode = look ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
        if (Input.MouseMode != mode)
        {
            Input.MouseMode = mode;
            _mouseModeAt = Time.GetTicksMsec();
        }
        _camera?.MouseLook = look;
    }

    /// <summary>When the mouse was last captured or let go, in <see cref="Time.GetTicksMsec"/>'s milliseconds.</summary>
    private ulong _mouseModeAt;

    /// <summary>How long after the mouse is captured or let go its motion is taken for the warp, not the player's hand.</summary>
    private const ulong MouseModeSettleMsec = 150;

    /// <summary>
    /// Keeps <see cref="Views.UiPointer.InUse"/> by the events in the menus alone, so the arrows only the mouse needs show as they
    /// did when the menu last closed: trek's and a battle's mouse look, and a controller there, leave it. The motion the pointer's
    /// warp sends as the mouse is let go isn't the player's.
    /// </summary>
    private void TrackPointer(InputEvent e)
    {
        if (_session is not { Mode: GameMode.Menu })
            return;
        if (e is InputEventMouseMotion && Time.GetTicksMsec() - _mouseModeAt < MouseModeSettleMsec)
            return;
        Views.UiPointer.Track(e);
    }

    /// <summary>
    /// A press from the pointer (<see cref="Views.UiPointer"/>): a view put the cursor on what was clicked and presses A, which
    /// goes to the session as a key's press would, unless the Controls page's prompt is waiting.
    /// </summary>
    private void PointerPress(MenuAction action)
    {
        if (_session is not { } session || session.Mode is not (GameMode.Menu or GameMode.Title or GameMode.Conversation or GameMode.Popup
            or GameMode.Backdrop or GameMode.CutScene))
            return;
        var system = session.Mode == GameMode.Title ? session.Title!.Menu : session.Menu.System;
        if (system?.Options?.Prompt is not null)
            return;
        if (session.Press(action) != PressOutcome.Ignored)
            ShowPressed(session);
    }

    /// <summary>
    /// The mouse in a conversation, a cut-scene's box, trek's popups and a backdrop's page, which draw on screens of their own
    /// that don't take it (ADR 0040): hovering a choice puts the cursor on it and a click takes it; where there are no choices,
    /// a click anywhere is A, continuing the line or closing the message. A click while choices are up but on none of them does
    /// nothing. Whether it took the event.
    /// </summary>
    private bool TalkPointer(GameSession session, InputEvent e)
    {
        var click = Views.UiPointer.Clicked(e);
        if (e is not InputEventMouse mouse || !click && e is not InputEventMouseMotion)
            return false;
        int? choice = null;
        switch (session.Mode)
        {
            case GameMode.Conversation or GameMode.CutScene when _box is { Visible: true, Box: { Shown: true, Layout: { } layout } } box:
                if (layout.Choices.Count == 0)
                    break;
                choice = Views.UiPointer.BlockAt(layout.Choices, _trekScreen.ToCanvas(mouse.Position));
                if (choice is { } said)
                    box.Point(said);
                else
                    return click;
                break;
            case GameMode.Popup when _popupView?.Popup is { } popup:
                if (popup.Question is not { } question)
                    break;
                choice = _popupView.ChoiceAt(_trekScreen.ToCanvas(mouse.Position));
                if (choice is { } picked)
                    question.Choices.Point(picked);
                else
                    return click;
                break;
            case GameMode.Backdrop when _backdrop?.Page is { Layout: { } page } backdrop:
                if (page.Choices.Count == 0)
                    break;
                var screen = _backdrop.Screen;
                var frame = (screen.CanvasSize - new Vector2I(320, 240)) / 2;
                choice = Views.UiPointer.BlockAt(page.Choices, screen.ToCanvas(mouse.Position) - frame);
                if (choice is { } row)
                    backdrop.MoveCursor(row - backdrop.Highlight);
                else
                    return click;
                break;
            default:
                return false;
        }
        if (click)
            PointerPress(MenuAction.Accept);
        else if (choice is null)
            return false;
        else
            ShowPressed(session);
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>
    /// The mouse in a battle (ADR 0040). A held menu lets the mouse go: hovering a row puts the cursor on it, the wheel scrolls the
    /// list, and a click chooses the row, as letting the menu's button go would. Aiming an attack or a spell (states 9 and 10),
    /// moving the mouse across steps the target as the stick's left and right do, every <see cref="PixelsPerTarget"/> pixels,
    /// and the wheel steps it too; a bow's or thrown weapon's (state 18) steps by the wheel as C-left and C-right do, and its
    /// cursor follows the mouse (<see cref="ReadInput"/>). The buttons are the bindings': the left is Attack, which confirms,
    /// and the right Cancel. Whether it took the event.
    /// </summary>
    private bool CombatPointer(GameSession session, InputEvent e)
    {
        if (session.Mode != GameMode.Combat || Battle is not { } battle || e is not InputEventMouse mouse)
            return false;
        var turn = Views.UiPointer.Wheel(e);
        if (battle.Menu is { Empty: null } menu)
        {
            var frame = _trekScreen.ToCanvas(mouse.Position) - (Vector2)((_trekScreen.CanvasSize - new Vector2I(320, 240)) / 2);
            var row = menu.RowAt(frame.X, frame.Y);
            if (turn != 0)
            {
                menu.ScrollBy(turn);
                return Handled();
            }
            if (row is not { } at)
                return false;
            menu.Point(at);
            if (Views.UiPointer.Clicked(e) && session.Press(menu.Items ? MenuAction.AppraiseRelease : MenuAction.DetailsRelease) != PressOutcome.Ignored)
                ShowPressed(session);
            return Handled();
        }
        if (!InputBindings.MouseLook(_settings.Controls))
            return false;
        var thrown = battle.State == Core.Combat.CombatState.TargetThrown;
        if (battle.State is not (Core.Combat.CombatState.TargetAttack or Core.Combat.CombatState.TargetSpell) && !thrown)
        {
            _targetDrag = 0;
            return false;
        }
        var step = 0;
        if (turn != 0)
            step = turn;
        else if (e is InputEventMouseMotion motion && !thrown)
        {
            _targetDrag += motion.Relative.X * _settings.MouseSensitivity;
            if (Math.Abs(_targetDrag) >= PixelsPerTarget)
            {
                step = Math.Sign(_targetDrag);
                _targetDrag = 0;
            }
        }
        if (step == 0)
            return false;
        // Right is the stick's right, which steps back through the order by angle; C-right likewise for a thrown weapon.
        var action = thrown ? step > 0 ? MenuAction.MemberRight : MenuAction.MemberLeft : step > 0 ? MenuAction.Right : MenuAction.Left;
        if (session.Press(action) != PressOutcome.Ignored)
            ShowPressed(session);
        return Handled();

        bool Handled()
        {
            _battleScreen?.QueueRedraw();
            GetViewport().SetInputAsHandled();
            return true;
        }
    }

    /// <summary>How long after the window comes to the front a mouse button is still the click that brought it there.</summary>
    private const ulong FocusClickMsec = 300;

    /// <summary>When the window last came to the front, in <see cref="Time.GetTicksMsec"/>'s milliseconds.</summary>
    private ulong _focusedAt;

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusIn)
            _focusedAt = Time.GetTicksMsec();
    }

    /// <summary>
    /// Whether a mouse button is passed over: in trek and a battle, the click that brings the window back to the front, which
    /// would otherwise use what he stands by or start an attack, and with mouse look, any click while the mouse isn't captured.
    /// It captures the mouse instead, where mouse look is on.
    /// </summary>
    private bool UncapturedClick(GameSession session, InputEvent e)
    {
        // A battle's held menu lets the mouse go on purpose, for the pointer to choose in it.
        if (e is not InputEventMouseButton || session.Mode is not (GameMode.Trek or GameMode.Combat) || _camera is not { Flying: false }
            || Battle?.Menu is not null)
            return false;
        // Windows can hand the game the click before it says the window is in front, so a click while it isn't counts too.
        var focusing = !GetWindow().HasFocus() || Time.GetTicksMsec() - _focusedAt < FocusClickMsec;
        var look = InputBindings.MouseLook(_settings.Controls);
        if (!focusing && (!look || Input.MouseMode == Input.MouseModeEnum.Captured))
            return false;
        CaptureMouse();
        GetViewport().SetInputAsHandled();
        return true;
    }

    /// <summary>
    /// While the options' Controls page waits for an input to bind (<see cref="OptionsMenu.Prompt"/>), it takes every event: one
    /// of its device binds, any other is passed over, and nothing reaches the menus, so the key being bound can't also cancel or
    /// move anything. Whether it took the event.
    /// </summary>
    private bool BindPrompted(GameSession session, InputEvent e)
    {
        var system = session.Mode switch
        {
            GameMode.Menu => session.Menu.System,
            GameMode.Title => session.Title!.Menu,
            _ => null,
        };
        if (system is not { Page: SystemPage.Options, Options: { Prompt: not null } options })
            return false;
        if (InputBindings.Of(e) is { } binding)
            options.Bind(binding);
        _menu!.Redraw();
        _titleLayer?.Redraw();
        GetViewport().SetInputAsHandled();
        return true;
    }
}
