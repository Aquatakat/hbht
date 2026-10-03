using Aidyn.Core.Characters;
using Aidyn.Core.Ui;
using Aidyn.Game;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The pause menu's training state at a trainer, drawn by the runtime's view against the editor's game:
/// what's trained stays in the game, to save for the runtime. A trainer's page shows it.
/// </summary>
public partial class TrainingWindowPanel : VBoxContainer
{
    private readonly UiScreen _screen = new()
    {
        CustomMinimumSize = new Vector2(UiScreen.Width, UiScreen.Height), Backdrop = new Color(0.22f, 0.25f, 0.3f), ForwardsMouse = true,
    };
    private readonly UiScreenOptions _options;
    private readonly PartyTabView _view;
    private readonly Label _state = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly HFlowContainer _bar = new();
    private readonly CheckBox _details = new() { Text = "Details (C-down)", TooltipText = "Show what C-down shows for the trainer's row, as it does while held." };

    public TrainingWindowPanel(PluginSet plugins, EditorGame game, Trainer trainer)
    {
        AddThemeConstantOverride("separation", 6);
        var training = TrainingMenu.For(plugins, game.Game.Party, trainer, game.Game.Flags, game.Game.Random.World);
        _view = new PartyTabView(plugins, training.Party) { Training = training };
        _screen.Canvas.AddChild(_view);

        AddChild(new Label
        {
            Text = $"The training window against {(game.IsNewGame ? "a new game" : game.Name)}: what's trained stays in the game. The stick's up "
                + "and down move the trainer's cursor, which the member's list follows; its left and right change both between skills and "
                + "spells; C-left and C-right change the member. A asks to train the row, B leaves.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
        var bar = _bar;
        bar.AddThemeConstantOverride("h_separation", 6);
        bar.AddChild(Button("◀ ▶", "The stick left or right: skills or spells.", () => Training.Switch()));
        bar.AddChild(Button("▲", "The stick up.", () => Training.MoveCursor(-1)));
        bar.AddChild(Button("▼", "The stick down.", () => Training.MoveCursor(1)));
        bar.AddChild(Button("C-left", "The member before.", () => Training.ChangeMember(-1)));
        bar.AddChild(Button("C-right", "The next member.", () => Training.ChangeMember(1)));
        bar.AddChild(Button("A", "Ask to train the trainer's row, choose Yes or No, or close a message.", Accept));
        bar.AddChild(Button("B", "Close a message, or leave.", Back));
        _details.Toggled += on =>
        {
            if (!on || !Training.CloseMessage())
                _view.DetailsOpen = on;
        };
        bar.AddChild(_details);
        AddChild(bar);
        AddChild(_options = new UiScreenOptions(_screen, "with room, the trainer's list goes right of the member's."));
        AddChild(_screen);
        AddChild(_state);
    }

    private TrainingMenu Training => _view.Training!;

    /// <summary>Sets how the screen is shown (<c>--zoom</c>, <c>--ui-filter</c>, <c>--aspect</c>, <c>--ui-lines</c>).</summary>
    public void SetScreen(int? zoom, UiFilter? filter, bool? wide, int? lines) => _options.Set(zoom, filter, wide, lines);

    /// <summary>
    /// Presses A, B, the stick, C-left and C-right, or C-down, which stays held until pressed again, in turn
    /// (<c>--press</c>), for screenshots.
    /// </summary>
    public void Press(IEnumerable<string> buttons)
    {
        foreach (var button in buttons)
        {
            switch (button.Trim().ToLowerInvariant())
            {
                case "a": Accept(); break;
                case "b": Back(); break;
                case "up": Training.MoveCursor(-1); break;
                case "down": Training.MoveCursor(1); break;
                case "left" or "right": Training.Switch(); break;
                case "cleft": Training.ChangeMember(-1); break;
                case "cright": Training.ChangeMember(1); break;
                case "details": _details.ButtonPressed = !_details.ButtonPressed; break;
                default: GD.PrintErr($"--press: \"{button}\" isn't a, b, up, down, left, right, cleft, cright or details."); break;
            }
        }
        _view.QueueRedraw();
    }

    private void Accept() => Training.Accept();

    // The original's window is gone once it closes; here it stays, to look at, with nothing more to do.
    private void Back()
    {
        if (Training.Back())
            return;
        _state.Text = "The window closed. In the game, the menu closes and trek goes on.";
        foreach (var button in _bar.GetChildren().OfType<BaseButton>())
            button.Disabled = true;
    }

    private Button Button(string text, string tooltip, Action act)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += () =>
        {
            act();
            _view.QueueRedraw();
        };
        return button;
    }
}
