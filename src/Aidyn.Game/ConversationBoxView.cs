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
    /// <summary>Alaron's entity, whose portrait shows over choices alone (<c>IDEntInd(Alaron)</c>).</summary>
    public static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);

    // Engine code's literals: the portrait's border (BORG8_PortraitHighlight), the gradient over the
    // box, and the portrait of a speaker without one (BORG8_PortraitNPCMale1). The decomp names the
    // gradient BORG8_GradientC, 0x1C2, but the machine code loads 0x1C3, which fades upward.
    private static readonly RecordId BorderImage = RecordId.Hex(RecordTypes.Image, 0x00DF);
    private static readonly RecordId GradientImage = RecordId.Hex(RecordTypes.Image, 0x01C3);
    private static readonly RecordId FallbackPortrait = RecordId.Hex(RecordTypes.Image, 0x0125);

    private readonly PluginSet _plugins;
    private readonly TextLayout _text;
    private readonly Dictionary<RecordId, Texture2D?> _images = [];
    private readonly Texture2D? _sheet, _border, _gradient;

    public ConversationBoxView(PluginSet plugins)
    {
        _plugins = plugins;
        Name = "Conversation";
        MouseFilter = MouseFilterEnum.Ignore;
        Size = new Vector2(UiScreen.Width, UiScreen.Height);
        var font = plugins.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0));
        _text = new TextLayout(font);
        _sheet = Image(font.Image);
        _border = Image(BorderImage);
        _gradient = Image(GradientImage);
        var border = _border?.GetSize() ?? new Vector2(52, 52);
        Box = new DialogueBox(_text, (int)border.X, (int)border.Y, Alaron);
    }

    public DialogueBox Box { get; }

    /// <summary>The name the game writes where a line says <c>{player}</c>: the first party member's.</summary>
    public string PlayerName { get; set; } = "Alaron";

    /// <summary>Shows a page, with its name placeholders filled in.</summary>
    public void Show(ScriptPage page)
    {
        Box.Show(new ScriptPage(Named(page.Line), [.. page.Choices.Select(Named)]));
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
        DrawRect(new Rect2(0, layout.Top, UiScreen.Width, layout.Bottom - layout.Top), Colors.Black);
        if (_gradient is not null)
            DrawTextureRect(_gradient, new Rect2(0, layout.Top - DialogueBox.GradientHeight, UiScreen.Width, DialogueBox.GradientHeight), false, Colors.Black);
        if (_border is not null)
            DrawTexture(_border, new Vector2(layout.BorderX, layout.BorderY));
        if (Image(PortraitOf(layout.Speaker)) is { } portrait)
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
                new Rect2(glyph.SourceX, glyph.SourceY, glyph.Width, glyph.Height), Color8(glyph.Color));
        }
    }

    /// <summary>A speaker's portrait, or the code's fallback for one without.</summary>
    private RecordId PortraitOf(RecordId? speaker) =>
        speaker is { } id && _plugins.TryGet<DbRecord>(id)?.Portrait is { } portrait ? portrait : FallbackPortrait;

    private ScriptLine Named(ScriptLine line) => line with { Text = line.Text.Replace("{player}", PlayerName, StringComparison.Ordinal) };

    private static Color Color8(Rgba c) => Color.Color8(c.R, c.G, c.B, c.A);

    private Texture2D? Image(RecordId image)
    {
        if (_images.TryGetValue(image, out var texture))
            return texture;
        if (_plugins.FindAsset(image, "png") is { } png)
        {
            var data = new Image();
            if (data.LoadPngFromBuffer(File.ReadAllBytes(png)) == Error.Ok)
                texture = ImageTexture.CreateFromImage(data);
        }
        else
        {
            GD.PushWarning($"No asset for {image}.");
        }
        return _images[image] = texture;
    }
}
