using Aidyn.Core.Characters;
using Aidyn.Core.Ui;
using Aidyn.Plugins;

namespace Aidyn.Inspector;

/// <summary>
/// The pause menu's training state at a trainer, drawn by the runtime's view against the editor's game:
/// what's trained stays in the game, to save for the runtime. A trainer's page shows it.
/// </summary>
public partial class TrainingWindowPanel : MenuWindowPanel
{
    private readonly TrainingMenu _training;

    public TrainingWindowPanel(PluginSet plugins, EditorGame game, Trainer trainer)
        : this(plugins, game, TrainingMenu.For(plugins, game.Game.Party, trainer, game.Game.Flags, game.Game.Random.World))
    {
    }

    private TrainingWindowPanel(PluginSet plugins, EditorGame game, TrainingMenu training) : base(plugins, training)
    {
        _training = training;
        Describe($"The training window against {(game.IsNewGame ? "a new game" : game.Name)}: what's trained stays in the game. The stick's up "
            + "and down move the trainer's cursor, which the member's list follows; its left and right change both between skills and "
            + "spells; C-left and C-right change the member. A asks to train the row, B leaves.");
        AddButton("◀ ▶", "The stick left or right: skills or spells.", _training.Switch);
        AddButton("▲", "The stick up.", () => _training.MoveCursor(-1));
        AddButton("▼", "The stick down.", () => _training.MoveCursor(1));
        AddButton("C-left", "The member before.", () => _training.ChangeMember(-1));
        AddButton("C-right", "The next member.", () => _training.ChangeMember(1));
        AddButton("A", "Ask to train the trainer's row, choose Yes or No, or close a message.", _training.Accept);
        AddButton("B", "Close a message, or leave.", Back);
        AddDetails("Show what C-down shows for the trainer's row, as it does while held.", closesMessage: true);
        Presses(("a", _training.Accept), ("b", Back), ("up", () => _training.MoveCursor(-1)), ("down", () => _training.MoveCursor(1)),
            ("left", _training.Switch), ("right", _training.Switch), ("cleft", () => _training.ChangeMember(-1)),
            ("cright", () => _training.ChangeMember(1)), ("details", ToggleDetails));
        Finish("with room, the trainer's list goes right of the member's.", closes: true);
    }

    private void Back()
    {
        if (!_training.Back())
            Leave("The window closed. In the game, the menu closes and trek goes on.");
    }
}
