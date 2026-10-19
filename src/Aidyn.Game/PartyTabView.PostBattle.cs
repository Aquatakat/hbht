using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The post-battle window (<see cref="PostBattleMenu"/>, pause state 5).
/// </summary>
/// <remarks>
/// As the original's (<c>DollMenu::InitMenu</c> with <c>PartyMenu.ListsCreate</c>, read in checkpoint 13): no silhouette and no arrow
/// under the knotwork; the header, and the member's spells, skills and stats where the Party tab has its list, sliding as the
/// tab's do (<see cref="DrawLists"/>); over the silhouette the details' parchment at (0, 58), Train's title centred on x 90 with
/// its foot at y 88, "Each member earned n XP." at (20, 92) in the main font wrapping at x 150, under it the level lines in the
/// slim font wrapping at x 155, and at (20, 186) and 11 below, the cost lines in the main font, all in the rows' red.
/// </remarks>
public partial class PartyTabView
{
    /// <summary>Train's title's foot on the post-battle window, 2 below the trainer's.</summary>
    private const int PostBattleTitleFoot = 88;

    private void DrawPostBattle(PostBattleMenu window)
    {
        _ui.Parchment(_canvas, ClassicParchment.X, ClassicParchment.Y);
        var title = _ui.Size(window.Title);
        _ui.Image(_canvas, window.Title, PileTitleCentre - title.X / 2, PostBattleTitleFoot - title.Y);

        var (x, y, wrap) = PostBattleMenu.EarnedPlace;
        var main = _ui.Main;
        var earned = new ScreenRect(x, y, x + wrap, FrameHeight);
        _ui.Text(_canvas, main, window.Earned, x, y, earned, PartyMenu.TextColor);
        var top = y + main.Layout.Height(window.Earned, x, x + wrap) + PostBattleMenu.LevelPlace.Gap;
        var (levelX, levelWrap, _) = PostBattleMenu.LevelPlace;
        foreach (var line in window.Levelled)
        {
            _ui.Text(_canvas, _ui.Slim, line, levelX, top, new ScreenRect(levelX, top, levelX + levelWrap, FrameHeight), PartyMenu.TextColor);
            top += _ui.Slim.Layout.Height(line, levelX, levelX + levelWrap);
        }

        var (costX, costY, spacing) = PostBattleMenu.CostPlace;
        foreach (var line in window.CostLines)
        {
            _ui.Text(_canvas, main, line, costX, costY, PartyMenu.TextColor);
            costY += spacing;
        }
    }
}
