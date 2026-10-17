using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The menu's barter state (<see cref="BarterMenu"/>): the loot window, or a shop's.
/// </summary>
/// <remarks>
/// As the original's (<c>WidgetDollMenu</c> in its barter state, <c>WidgetBarter</c>): no
/// silhouette and no arrow under the knotwork; the header and the party's inventory list where the Party
/// tab has them, and over the silhouette the details' parchment at (0, 58) with the pile on it. Its title
/// is centred on x 90 with its foot at y 85, the list from (20, 88) to (157, 166) in the slim font, and
/// under it in red, at (30, 169), the gold the loot held, or in a shop, the active row's price. A shop's
/// pile is titled Buy and the party's list Sell, and its stock has no counts.
/// </remarks>
public partial class PartyTabView
{
    /// <summary>The pile's title's centre and foot.</summary>
    private const int PileTitleCentre = 90, PileTitleFoot = 0x55;

    /// <summary>Where the gold the loot held, or a shop's price, is written.</summary>
    private static readonly Vector2I ClassicGoldLine = new(0x1E, 0xA9);

    /// <summary>The help's text on its parchment, and how far it wraps (<c>WidgetMenuPrompt</c>, <c>WidgetMenuPopup</c>).</summary>
    private static readonly Vector2I HelpText = new(23, 70);

    private const int HelpRight = 149;

    private ScreenRect _pileBounds;

    /// <summary>The barter state, or null for the Party tab. Setting it shows its party's menu.</summary>
    public BarterMenu? Barter
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

    /// <summary>The pile's or the party's list's row under a point, if any and no popup is up.</summary>
    private (bool Pile, int Row)? BarterRowAt(Vector2 point)
    {
        var barter = Barter!;
        if (barter.HasPopup)
            return null;
        var at = point - (Vector2)_origin;
        foreach (var (bounds, list, pile) in new[] { (_pileBounds, barter.Pile, true), (_listBounds, Menu.Inventory, false) })
        {
            if (at.X < bounds.Left || at.X >= bounds.Right || at.Y < bounds.Top || at.Y >= bounds.Bottom)
                continue;
            var row = list.First + (int)(at.Y - bounds.Top) / 13;
            return row < list.Rows.Count ? (pile, row) : null;
        }
        return null;
    }

    /// <summary>The colours of the party's list's cursor and the pile's: the active one pulses, the other is plain.</summary>
    private (Rgba Party, Rgba Pile) Cursors(BarterMenu barter) =>
        barter.OnPile ? (PartyMenu.TextColor, Menu.HighlightNow) : (Menu.HighlightNow, PartyMenu.TextColor);

    private void DrawBarter(BarterMenu barter)
    {
        var (party, pile) = Cursors(barter);
        _ui.Image(_canvas, MenuImages.PartyTab, 0, 0);
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        List(ClassicList, _ui.Slim, party);

        _ui.Parchment(_canvas, ClassicParchment.X, ClassicParchment.Y);
        var title = _ui.Size(barter.Title);
        _ui.Image(_canvas, barter.Title, PileTitleCentre - title.X / 2, PileTitleFoot - title.Y);
        _pileBounds = BarterMenu.ClassicPile;
        Items(barter.Pile, _pileBounds, _ui.Slim, pile);
        _ui.Text(_canvas, _ui.Slim, barter.PileLine, ClassicGoldLine.X, ClassicGoldLine.Y, PartyMenu.TextColor);

        // C-down's details cover the pile, as WidgetItemDetail covers WidgetBarter, on their own layer (DrawDetails).
        if (barter.Help is { } help)
            Help(help);
    }

    /// <summary>
    /// The help on the first potion (<c>WidgetMenuPrompt</c>): the parchment, and the text in the rows'
    /// red, in the main font, 23 in and 12 down, wrapping at 149.
    /// </summary>
    private void Help(string text)
    {
        _ui.Parchment(_canvas, ClassicParchment.X, ClassicParchment.Y);
        _ui.Text(_canvas, _ui.Main, text, HelpText.X, HelpText.Y, new ScreenRect(HelpText.X, HelpText.Y, HelpRight, (int)Size.Y), PartyMenu.TextColor);
    }

    /// <summary>The popups over the barter state, centred on the screen.</summary>
    private void BarterPopups(BarterMenu barter)
    {
        var screen = (Vector2I)Size;
        if (barter.Confirm is { } confirm)
            Confirm(confirm, barter.PopupHighlight);
        if (barter.Choice is { } choice)
            _ui.Choice(_canvas, screen, choice.Text, choice.Width, choice.Gap, choice.Color, choice.ChoiceColor, choice.Choices, barter.PopupHighlight);
        if (barter.Message is { Box: { } box } boxed)
            _ui.MessageBox(_canvas, screen, boxed.Text, box.Width, box.Height, MenuMessage.White);
        else if (barter.Message is { } message)
            Message(message);
    }

    /// <summary>
    /// The drop's question (<c>DollBarterConfirm</c>): the question wrapped across 160 and its options 8 under
    /// it, together centred on the screen, on a feathered panel 4 beyond them above and below.
    /// </summary>
    private void Confirm(BarterConfirm confirm, Rgba cursor)
    {
        var font = _ui.Main;
        var screen = (Vector2I)Size;
        var left = (screen.X - BarterConfirm.Width) / 2;
        var right = left + BarterConfirm.Width;
        var textHeight = font.Layout.Height(confirm.Text, left, right);
        var rows = confirm.Options.Rows;
        var height = textHeight + rows.Count * font.LineHeight + BarterConfirm.Gap;
        var top = (screen.Y - height) / 2;
        _ui.Images.DrawPanel(_canvas, _ui.Images.Around(new ScreenRect(left, top - 4, right, top + height + 4)));
        _ui.Text(_canvas, font, confirm.Text, left, top, new ScreenRect(left, top, right, top + textHeight), BarterConfirm.TextColor);
        var optionsTop = top + textHeight + BarterConfirm.Gap;
        for (var i = 0; i < rows.Count; i++)
        {
            _ui.Text(_canvas, font, rows[i].Text, left, optionsTop + i * font.LineHeight, new ScreenRect(left, optionsTop, right, top + height),
                i == confirm.Options.Cursor ? cursor : BarterConfirm.OptionColor);
        }
    }
}
