using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The menu's training state at a trainer (<see cref="TrainingMenu"/>).
/// </summary>
/// <remarks>
/// <para>
/// <b>Classic</b>, as the original's (<c>WidgetDollMenu</c> in pause state 4, <c>WidgetMenuSpells</c>): no
/// silhouette and no arrow under the knotwork; the header and the member's skills or spells where the Party
/// tab has its list, and over the silhouette the details' parchment at (0, 58) with the trainer's list on
/// it. Its title is centred on x 90 with its foot at y 86, the list from (20, 88) to (157, 166) in the slim
/// font, as the skills and spells lists draw, and under it in red, from (30, 169) and 11 apart, the XP cost,
/// the XP remaining and the gold cost.
/// </para>
/// <para>
/// <b>Wide</b>: the Party tab's wide sheet, silhouette and all, with the trainer's list on the right-hand
/// parchment, where the loot window's pile goes, the price lines under it and the row's details below.
/// </para>
/// </remarks>
public partial class PartyTabView
{
    /// <summary>The trainer's title's foot in the classic layout, a pixel below the pile's.</summary>
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

    private void DrawTrainingClassic(TrainingMenu training)
    {
        _ui.Image(this, MenuImages.PartyTab, 0, 0);
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        List(ClassicList, _ui.Slim, Columns.Classic, MemberCursor(training));

        _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
        var title = _ui.Size(training.Title);
        _ui.Image(this, training.Title, PileTitleCentre - title.X / 2, TrainTitleFoot - title.Y);
        _trainerBounds = TrainingMenu.ClassicList;
        TrainerRows(training, _trainerBounds, _ui.Slim, Columns.Classic);
        var y = ClassicGoldLine.Y;
        foreach (var line in training.PriceLines)
        {
            _ui.Text(this, _ui.Slim, line, ClassicGoldLine.X, y, PartyMenu.TextColor);
            // The lines were measured in the main font as they were built, and drawn in the slim one.
            y += _ui.Main.LineHeight;
        }

        if (DetailsOpen && training.Details() is { } details)
        {
            _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
            Details(details, ClassicDetails, _ui.Slim, classic: true);
        }
        if (training.Help is { } help)
            Help(help, ClassicParchment);
    }

    /// <summary>
    /// The wide layout's training state: the Party tab's sheet and the member's list, and on the right-hand
    /// parchment the trainer's list under its title, the price lines, and the row's details below.
    /// </summary>
    private void DrawTrainingWide(TrainingMenu training)
    {
        var (area, parchmentLeft, top, parchment) = WideFrame(MemberCursor(training));
        var font = _ui.Main;
        var title = _ui.Size(training.Title);
        _ui.Image(this, training.Title, (area.Left + area.Right - title.X) / 2, area.Top);
        var listTop = area.Top + title.Y + 4;
        var rows = Math.Max(WidePileRows, (area.Bottom - area.Top) * 2 / 5 / 13);
        _trainerBounds = new ScreenRect(area.Left, listTop, area.Right, listTop + rows * 13);
        TrainerRows(training, _trainerBounds, font, Columns.Wide);
        var y = _trainerBounds.Bottom + 4;
        foreach (var line in training.PriceLines)
        {
            _ui.Text(this, font, line, area.Left, y, PartyMenu.TextColor);
            y += font.LineHeight;
        }

        var detailsTop = y + 12;
        if (training.Details() is { } details && detailsTop < area.Bottom)
            Details(details, area with { Top = detailsTop }, font, classic: false);
        if (training.Help is { } help)
            Help(help, new Vector2I(parchmentLeft + (parchment.X - 160) / 2, top + 12));
    }

    /// <summary>The trainer's skills or spells, drawn as the member's are.</summary>
    private void TrainerRows(TrainingMenu training, ScreenRect bounds, UiFont font, Columns columns)
    {
        if (training.OnSpells)
            SpellRows(training.Spells, bounds, font, columns, Menu.HighlightNow);
        else
            SkillRows(training.Skills, bounds, font, columns, Menu.HighlightNow);
    }

    /// <summary>The popups over the training state, centred on the screen.</summary>
    private void TrainingPopups(TrainingMenu training)
    {
        var screen = (Vector2I)Size;
        if (training.Choice is { } choice)
            _ui.Choice(this, screen, choice.Text, choice.Width, choice.Gap, choice.Color, choice.ChoiceColor, choice.Choices, training.PopupHighlight);
        if (training.Message is { } message)
            Message(message);
    }
}
