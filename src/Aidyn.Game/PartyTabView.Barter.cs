using Aidyn.Core.Ui;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The menu's barter state (<see cref="BarterMenu"/>): the loot window, or a shop's.
/// </summary>
/// <remarks>
/// <para>
/// <b>Classic</b>, as the original's (<c>WidgetDollMenu</c> in its barter state, <c>WidgetBarter</c>): no
/// silhouette and no arrow under the knotwork; the header and the party's inventory list where the Party
/// tab has them, and over the silhouette the details' parchment at (0, 58) with the pile on it. Its title
/// is centred on x 90 with its foot at y 85, the list from (20, 88) to (157, 166) in the slim font, and
/// under it in red, at (30, 169), the gold the loot held, or in a shop, the active row's price. A shop's
/// pile is titled Buy and the party's list Sell, and its stock has no counts.
/// </para>
/// <para>
/// <b>Wide</b>: the Party tab's wide sheet, silhouette and all, with the pile on the right-hand parchment,
/// where the classic layout's popups go, and the active row's details under it. So the pile is to the
/// right of the party's list, and the stick's right reaches it (<see cref="PileOnRight"/>).
/// </para>
/// </remarks>
public partial class PartyTabView
{
    /// <summary>The pile's title's centre and foot in the classic layout.</summary>
    private const int PileTitleCentre = 90, PileTitleFoot = 0x55;

    /// <summary>Where the gold the loot held, or a shop's price, is written in the classic layout.</summary>
    private static readonly Vector2I ClassicGoldLine = new(0x1E, 0xA9);

    /// <summary>The help's text on its parchment, and how far it wraps (<c>WidgetMenuPrompt</c>, <c>WidgetMenuPopup</c>).</summary>
    private static readonly Vector2I HelpText = new(23, 70);

    private const int HelpRight = 149;

    /// <summary>The fewest rows the wide pile shows.</summary>
    private const int WidePileRows = 6;

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

    /// <summary>Whether the pile is right of the party's list, as the wide layout has it, so the stick's right reaches it.</summary>
    public bool PileOnRight => Wide;

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

    private void DrawBarterClassic(BarterMenu barter)
    {
        var (party, pile) = Cursors(barter);
        _ui.Image(this, MenuImages.PartyTab, 0, 0);
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        List(ClassicList, _ui.Slim, Columns.Classic, party);

        _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
        var title = _ui.Size(barter.Title);
        _ui.Image(this, barter.Title, PileTitleCentre - title.X / 2, PileTitleFoot - title.Y);
        _pileBounds = BarterMenu.ClassicPile;
        Items(barter.Pile, _pileBounds, _ui.Slim, Columns.Classic, pile);
        _ui.Text(this, _ui.Slim, barter.PileLine, ClassicGoldLine.X, ClassicGoldLine.Y, PartyMenu.TextColor);

        // C-down's details cover the pile while it's held, as WidgetItemDetail covers WidgetBarter.
        if (DetailsOpen && barter.Details() is { } details)
        {
            _ui.Parchment(this, ClassicParchment.X, ClassicParchment.Y);
            Details(details, ClassicDetails, _ui.Slim, classic: true);
        }
        if (barter.Help is { } help)
            Help(help, ClassicParchment);
    }

    /// <summary>
    /// The wide layout's barter state: the Party tab's sheet and list, and on the right-hand parchment the
    /// pile under its title, the gold, and the active row's details below.
    /// </summary>
    private void DrawBarterWide(BarterMenu barter)
    {
        var (party, pile) = Cursors(barter);
        var size = new Vector2I((int)Size.X, (int)Size.Y);
        var bottom = size.Y - BottomEdge.Size.Y;
        Sheet(size, bottom);
        _ui.Feathered(this, MenuImages.PartyTab, TopPart, 0, 0, (0, 0, Feather, Feather));
        _ui.Feathered(this, MenuImages.PartyTab, SilhouetteColumn, 0, SilhouetteColumn.Position.Y, (0, 0, Feather, Feather));
        Portraits(PortraitPiece.Position);
        Health(HealthPiece.Position, _ui.Main);
        Silhouette(SilhouettePiece.Position);

        // The party's list as the tab has it, without the arrow under it: there are no tabs either side.
        var list = ClassicList;
        var room = size.X - list.Left - WideGap;
        var listWidth = Math.Clamp(room * 11 / 20, 220, 300);
        var rows = (bottom - SliderMargin - list.Top) / 13 * 13;
        Rules(RulesPiece.Position.X, RulesPiece.Position.Y, Math.Max(RulesPiece.Size.X, list.Left + listWidth + 10 - RulesPiece.Position.X));
        Knot(KnotTop.Position.X, KnotTop.Position.Y, rows + 4);
        List(list with { Right = list.Left + listWidth, Bottom = list.Top + rows }, _ui.Main, Columns.Wide, party);

        var top = TopEdge.Size.Y - 2;
        var parchmentLeft = list.Left + listWidth + WideGap;
        var parchment = new Vector2I(size.X - parchmentLeft, bottom - top + 2);
        _ui.Parchment(this, parchmentLeft, top, parchment, mirror: true);
        var area = new ScreenRect(parchmentLeft + parchment.X * 11 / 160, top + parchment.Y * 12 / 160,
            parchmentLeft + parchment.X - DetailsEdge, top + parchment.Y - parchment.Y * 8 / 160);

        // The pile under its title, as tall as two fifths of the parchment or six rows, then the gold.
        var font = _ui.Main;
        var title = _ui.Size(barter.Title);
        _ui.Image(this, barter.Title, (area.Left + area.Right - title.X) / 2, area.Top);
        var pileTop = area.Top + title.Y + 4;
        var pileRows = Math.Max(WidePileRows, (area.Bottom - area.Top) * 2 / 5 / 13);
        _pileBounds = new ScreenRect(area.Left, pileTop, area.Right, pileTop + pileRows * 13);
        Items(barter.Pile, _pileBounds, font, Columns.Wide, pile);
        var goldTop = _pileBounds.Bottom + 4;
        _ui.Text(this, font, barter.PileLine, area.Left, goldTop, PartyMenu.TextColor);

        var detailsTop = goldTop + font.LineHeight + 12;
        if (barter.Details() is { } details && detailsTop < area.Bottom)
            Details(details, area with { Top = detailsTop }, font, classic: false);
        // The help covers the pile, on a parchment of its own at its own size.
        if (barter.Help is { } help)
            Help(help, new Vector2I(parchmentLeft + (parchment.X - 160) / 2, top + 12));
    }

    /// <summary>
    /// The help on the first potion (<c>WidgetMenuPrompt</c>): the parchment, and the text in the rows'
    /// red, in the main font, 23 in and 12 down, wrapping at 149.
    /// </summary>
    private void Help(string text, Vector2I parchment)
    {
        _ui.Parchment(this, parchment.X, parchment.Y);
        var x = parchment.X + HelpText.X - ClassicParchment.X;
        var y = parchment.Y + HelpText.Y - ClassicParchment.Y;
        _ui.Text(this, _ui.Main, text, x, y, new ScreenRect(x, y, parchment.X + HelpRight - ClassicParchment.X, (int)Size.Y), PartyMenu.TextColor);
    }

    /// <summary>The popups over the barter state, centred on the screen.</summary>
    private void BarterPopups(BarterMenu barter)
    {
        var screen = (Vector2I)Size;
        if (barter.Confirm is { } confirm)
            Confirm(confirm, barter.PopupHighlight);
        if (barter.Choice is { } choice)
            _ui.Choice(this, screen, choice.Text, choice.Width, choice.Gap, choice.Color, choice.ChoiceColor, choice.Choices, barter.PopupHighlight);
        if (barter.Message is { Box: { } box } boxed)
            _ui.MessageBox(this, screen, boxed.Text, box.Width, box.Height, MenuMessage.White);
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
        _ui.Images.DrawPanel(this, _ui.Images.Around(new ScreenRect(left, top - 4, right, top + height + 4)));
        _ui.Text(this, font, confirm.Text, left, top, new ScreenRect(left, top, right, top + textHeight), BarterConfirm.TextColor);
        var optionsTop = top + textHeight + BarterConfirm.Gap;
        for (var i = 0; i < rows.Count; i++)
        {
            _ui.Text(this, font, rows[i].Text, left, optionsTop + i * font.LineHeight, new ScreenRect(left, optionsTop, right, top + height),
                i == confirm.Options.Cursor ? cursor : BarterConfirm.OptionColor);
        }
    }
}
