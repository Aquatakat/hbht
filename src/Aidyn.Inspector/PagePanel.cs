using System.Globalization;
using Aidyn.Game;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A scrolling page of headings, lines, grids and links, which a record's preview rebuilds as what it
/// shows changes: a character sheet, an item, a shop, a loot table or a recipe.
/// </summary>
public partial class PagePanel : ScrollContainer
{
    protected static readonly Color Dim = new(0.65f, 0.65f, 0.65f);
    protected static readonly Color Refused = new(0.85f, 0.55f, 0.45f);

    private readonly Dictionary<RecordId, Texture2D?> _icons = [];

    protected PagePanel(PluginCatalog catalog, Action<RecordId> open)
    {
        Catalog = catalog;
        Open = open;
        HorizontalScrollMode = ScrollMode.Disabled;
        Body.AddThemeConstantOverride("separation", 6);
        AddChild(Body);
    }

    protected PluginCatalog Catalog { get; }

    protected PluginSet Plugins => Catalog.Plugins;

    /// <summary>Opens a record, as a link does.</summary>
    protected Action<RecordId> Open { get; }

    protected VBoxContainer Body { get; } = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };

    protected void ClearBody()
    {
        foreach (var child in Body.GetChildren())
        {
            Body.RemoveChild(child);
            child.QueueFree();
        }
    }

    protected void Heading(string text)
    {
        Body.AddChild(new Control { CustomMinimumSize = new Vector2(0, 6) });
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 16);
        Body.AddChild(label);
    }

    protected Label Line(string text, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = color ?? Colors.White };
        Body.AddChild(label);
        return label;
    }

    /// <summary>A grid with a column per heading, and a row of the headings unless they're all blank.</summary>
    protected static GridContainer Grid(string[] headings)
    {
        var grid = new GridContainer { Columns = headings.Length };
        grid.AddThemeConstantOverride("h_separation", 16);
        if (headings.All(h => h.Length == 0))
            return grid;
        foreach (var heading in headings)
            grid.AddChild(new Label { Text = heading, Modulate = Dim });
        return grid;
    }

    protected static void Row(GridContainer grid, params string[] cells)
    {
        foreach (var cell in cells)
            grid.AddChild(new Label { Text = cell });
    }

    protected Button LinkButton(RecordId id, string text)
    {
        var button = new Button { Text = text, Flat = true, Alignment = HorizontalAlignment.Left, TooltipText = $"Open {id}." };
        button.Pressed += () => Open(id);
        return button;
    }

    /// <summary>A link to an item, with its icon.</summary>
    protected Control ItemLink(RecordId id, RecordId? icon, string text, int scale = 1)
    {
        var row = new HBoxContainer();
        if (icon is { } i && Icon(i) is { } texture)
        {
            row.AddChild(new TextureRect
            {
                Texture = texture,
                TextureFilter = TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                CustomMinimumSize = texture.GetSize() * scale,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
            });
        }
        row.AddChild(LinkButton(id, text));
        return row;
    }

    /// <summary>An image or texture record's PNG, read once per page.</summary>
    protected Texture2D? Icon(RecordId id)
    {
        if (_icons.TryGetValue(id, out var texture))
            return texture;
        texture = Catalog.Find(id)?.Asset("png") is { } png ? Png.Texture(png.Path) : null;
        return _icons[id] = texture;
    }

    /// <summary>A record's <c>name</c> field, or its key if it has none.</summary>
    protected string NameOf(RecordId id) => (string?)Plugins.GetJson(id)?["name"] ?? id.Key;

    protected static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);

    protected static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
