using Aidyn.Core;
using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a conversation's box (<see cref="DialogueBox"/>) on a <see cref="UiScreen"/>'s canvas, in
/// the game's main font, as <c>DiaMenuSub</c>'s widgets do.
/// </summary>
public partial class ConversationBoxView : Control
{
    // Engine code's literals: the portrait's border (BORG8_PortraitHighlight) and the portrait of a
    // speaker without one (BORG8_PortraitNPCMale1).
    private static readonly RecordId BorderImage = RecordId.Hex(RecordTypes.Image, 0x00DF);
    private static readonly RecordId FallbackPortrait = RecordId.Hex(RecordTypes.Image, 0x0125);

    private readonly PluginSet _plugins;
    private readonly UiImages _images;
    private readonly TextLayout _text;
    private readonly Texture2D? _sheet, _border;

    public ConversationBoxView(PluginSet plugins)
    {
        _plugins = plugins;
        _images = new UiImages(plugins);
        Name = "Conversation";
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
        var font = plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0));
        _text = new TextLayout(font);
        _sheet = _images.Get(font.Image);
        _border = _images.Get(BorderImage);
        var border = _border?.GetSize() ?? new Vector2(52, 52);
        Box = new DialogueBox(_text, (int)border.X, (int)border.Y, GameState.Alaron);
    }

    public DialogueBox Box { get; }

    /// <summary>Shows a page.</summary>
    public void Show(ScriptPage page)
    {
        Box.Show(page);
        Visible = true;
        QueueRedraw();
    }

    /// <summary>Brings up the choices behind a line that showed alone first.</summary>
    public void ShowChoices()
    {
        Box.ShowChoices();
        QueueRedraw();
    }

    /// <summary>Puts the cursor on a choice.</summary>
    public void Point(int choice)
    {
        Box.MoveCursor(choice - Box.Highlight);
        QueueRedraw();
    }

    public override void _Notification(int what)
    {
        // The UI screen's canvas is as wide as the window's shape, so the box follows it.
        if (what == NotificationResized)
        {
            Box.Resize((int)Size.X, (int)Size.Y);
            QueueRedraw();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        Box.Step();
        if (Visible && Box.Layout is { Choices.Count: > 0 })
            QueueRedraw();
    }

    public override void _Draw()
    {
        if (Box.Layout is not { } layout)
            return;
        _images.DrawPanel(this, layout.Panel);
        if (_border is not null)
            DrawTexture(_border, new Vector2(layout.BorderX, layout.BorderY));
        if (_images.Get(PortraitOf(layout.Speaker)) is { } portrait)
            DrawTexture(portrait, new Vector2(layout.PortraitX, layout.PortraitY));

        if (layout.Line is { } line)
            Print(line, DialogueBox.LineColor);
        for (var i = 0; i < layout.Choices.Count; i++)
            Print(layout.Choices[i], i == layout.Highlight ? Box.HighlightNow : DialogueBox.ChoiceColor);
    }

    private void Print(TextBlock block, Rgba color)
    {
        if (_sheet is null)
            return;
        foreach (var glyph in _text.Print(block.Text, block.X, block.Y, block.Bounds, color).Glyphs)
        {
            DrawTextureRectRegion(_sheet, new Rect2(glyph.X, glyph.Y, glyph.Width, glyph.Height),
                new Rect2(glyph.SourceX, glyph.SourceY, glyph.Width, glyph.Height), UiPainter.Color8(glyph.Color));
        }
    }

    /// <summary>A speaker's portrait, or the code's fallback for one without.</summary>
    private RecordId PortraitOf(RecordId? speaker) =>
        speaker is { } id && _plugins.TryGet<DbRecord>(id)?.Portrait is { } portrait ? portrait : FallbackPortrait;
}
