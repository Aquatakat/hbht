using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// The post-battle window (<see cref="PostBattleMenu"/>, pause state 5).
/// </summary>
/// <remarks>
/// As the original's (<c>DollMenu::InitMenu</c> with <c>PartyMenu.ListsCreate</c>, read in checkpoint 13): no silhouette and no arrow
/// under the knotwork; the header, and the member's spells, skills and stats where the Party tab has its list, sliding as the
/// tab's do (<see cref="DrawLists"/>); over the silhouette the details' parchment at (0, 58), Train's title centred on x 90 with
/// its foot at y 88, "Each member earned n XP." at (20, 92) wrapping at x 150, under it the level lines wrapping at x 155, and at
/// (20, 186) and 11 below, the cost lines, all in the slim font and the rows' red: the lists' widget draws everything in it in the
/// slim font (<c>PartyMenu.ListsDraw</c>, <c>0x8003140C</c>). The level lines go 4 under line 491 as the main font wraps it, as it
/// was measured while the main font was set (<see cref="PostBattleMenu.LevelPlace"/>).
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
        var slim = _ui.Slim;
        var earned = new ScreenRect(x, y, x + wrap, FrameHeight);
        _ui.Text(_canvas, slim, window.Earned, x, y, earned, PartyMenu.TextColor);
        var top = y + _ui.Main.Layout.Height(window.Earned, x, x + wrap) + PostBattleMenu.LevelPlace.Gap;
        var (levelX, levelWrap, _) = PostBattleMenu.LevelPlace;
        foreach (var line in window.Levelled)
        {
            _ui.Text(_canvas, slim, line, levelX, top, new ScreenRect(levelX, top, levelX + levelWrap, FrameHeight), PartyMenu.TextColor);
            top += slim.Layout.Height(line, levelX, levelX + levelWrap);
        }

        var (costX, costY, spacing) = PostBattleMenu.CostPlace;
        foreach (var line in window.CostLines)
        {
            _ui.Text(_canvas, slim, line, costX, costY, PartyMenu.TextColor);
            costY += spacing;
        }
    }
}
