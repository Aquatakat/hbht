using HBHT.Core.Ui;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Views;

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

    /// <summary>The member's list's cursor: it pulses on the trainer's row, and is plain when the member hasn't it.</summary>
    private Rgba MemberCursor(TrainingMenu training) => training.Followed ? Menu.HighlightNow : PartyMenu.TextColor;

    /// <summary>
    /// Training's parchment, title and price lines, over the member's list, which the Party tab's list layer draws as it slides
    /// (<see cref="DrawLists"/>), and under the trainer's list (<see cref="DrawTrainer"/>).
    /// </summary>
    private void DrawTraining()
    {
        if (Menu.Shown is null || OwnTab || Window is not TrainingMenu training)
            return;
        _canvas = _trainingLayer;
        _trainingLayer.DrawSetTransform(_origin);
        _ui.Parchment(_canvas, ClassicParchment.X, ClassicParchment.Y);
        var title = _ui.Size(training.Title);
        _ui.Image(_canvas, training.Title, PileTitleCentre - title.X / 2, TrainTitleFoot - title.Y);
        var y = ClassicGoldLine.Y;
        foreach (var line in training.PriceLines)
        {
            _ui.Text(_canvas, _ui.Slim, line, ClassicGoldLine.X, y, PartyMenu.TextColor);
            // The lines were measured in the main font as they were built, and drawn in the slim one.
            y += _ui.Main.LineHeight;
        }

        // The trainer's list is on its own layer, cut to its bounds as it slides (DrawTrainer); the help over it is on the
        // overlay (DrawOverlay), and C-down's details on theirs (DrawDetails).
    }

    /// <summary>
    /// The trainer's list over the parchment, cut to its bounds, or as the stick changes it, the list it left sliding out and the
    /// other sliding in after it, both the same way, a list's width over <see cref="PartyMenu.ListFrames"/> frames
    /// (<c>TrainingMenu.ChangeList</c>, <c>0x800323D4</c>): to the right, the new list from x −120 and the old to 160.
    /// </summary>
    private void DrawTrainer()
    {
        if (Menu.Shown is null || Window is not TrainingMenu training)
            return;
        _canvas = _trainerLayer;
        if (training.Slide is not { } slide)
        {
            TrainerRows(training, training.OnSpells, 0, _ui.Slim);
            return;
        }
        var by = slide.Direction * PartyMenu.ListDistance;
        TrainerRows(training, slide.From, MenuTween.Value(0, by, training.ListChange), _ui.Slim);
        TrainerRows(training, training.OnSpells, MenuTween.Value(-by, 0, training.ListChange), _ui.Slim);
    }

    /// <summary>The trainer's skills or spells, drawn as the member's are, <paramref name="shift"/> across.</summary>
    private void TrainerRows(TrainingMenu training, bool spells, int shift, UiFont font)
    {
        var layout = TrainingMenu.Layout.Shifted(shift);
        if (spells)
            SpellRows(training.Spells, layout, font, Menu.HighlightNow);
        else
            SkillRows(training.Skills, layout, font, Menu.HighlightNow);
    }
}
