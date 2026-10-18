using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The menu's training state at a trainer (<see cref="TrainingMenu"/>).
/// </summary>
/// <remarks>
/// As the original's (<c>WidgetDollMenu</c> in pause state 4, <c>WidgetMenuSpells</c>): no
/// silhouette and no arrow under the knotwork; the header and the member's skills or spells where the Party
/// tab has its list, and over the silhouette the details' parchment at (0, 58) with the trainer's list on
/// it. Its title is centred on x 90 with its foot at y 86, the list from (20, 88) to (157, 166) in the slim
/// font, as the skills and spells lists draw, and under it in red, from (30, 169) and 11 apart, the XP cost,
/// the XP remaining and the gold cost.
/// </remarks>
public partial class PartyTabView
{
    /// <summary>The trainer's title's foot, a pixel below the pile's.</summary>
    private const int TrainTitleFoot = 86;

    private ScreenRect _trainerBounds;

    /// <summary>The training state, or null. Setting it shows its party's menu.</summary>
    public TrainingMenu? Training
    {
        get;
        set
        {
            field = value;
            if (value is not null)
                Menu = value.Party;
            QueueRedraw();
        }
    }

    /// <summary>The trainer's row under a point, if any and no popup is up.</summary>
    private int? TrainerRowAt(Vector2 point)
    {
        var training = Training!;
        if (training.HasPopup)
            return null;
        var at = point - (Vector2)_origin;
        var b = _trainerBounds;
        if (at.X < b.Left || at.X >= b.Right || at.Y < b.Top || at.Y >= b.Bottom)
            return null;
        var (first, count) = training.OnSpells ? (training.Spells.First, training.Spells.Rows.Count) : (training.Skills.First, training.Skills.Rows.Count);
        var row = first + (int)(at.Y - b.Top) / 13;
        return row < count ? row : null;
    }

    /// <summary>The member's list's cursor: it pulses on the trainer's row, and is plain when the member hasn't it.</summary>
    private Rgba MemberCursor(TrainingMenu training) => training.Followed ? Menu.HighlightNow : PartyMenu.TextColor;

    private void DrawTraining(TrainingMenu training)
    {
        _ui.Image(_canvas, MenuImages.PartyTab, 0, 0);
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        List(ClassicList, _ui.Slim, MemberCursor(training));

        _ui.Parchment(_canvas, ClassicParchment.X, ClassicParchment.Y);
        var title = _ui.Size(training.Title);
        _ui.Image(_canvas, training.Title, PileTitleCentre - title.X / 2, TrainTitleFoot - title.Y);
        _trainerBounds = TrainingMenu.ClassicList;
        TrainerRows(training, _trainerBounds, _ui.Slim);
        var y = ClassicGoldLine.Y;
        foreach (var line in training.PriceLines)
        {
            _ui.Text(_canvas, _ui.Slim, line, ClassicGoldLine.X, y, PartyMenu.TextColor);
            // The lines were measured in the main font as they were built, and drawn in the slim one.
            y += _ui.Main.LineHeight;
        }

        // C-down's details cover the trainer's list on their own layer (DrawDetails).
        if (training.Help is { } help)
            Help(help);
    }

    /// <summary>The trainer's skills or spells, drawn as the member's are.</summary>
    private void TrainerRows(TrainingMenu training, ScreenRect bounds, UiFont font)
    {
        if (training.OnSpells)
            SpellRows(training.Spells, bounds, font, Menu.HighlightNow);
        else
            SkillRows(training.Skills, bounds, font, Menu.HighlightNow);
    }

    /// <summary>The popups over the training state, centred on the screen.</summary>
    private void TrainingPopups(TrainingMenu training)
    {
        var screen = (Vector2I)Size;
        _ui.Overlay(_canvas, screen, training.Overlay);
    }
}
