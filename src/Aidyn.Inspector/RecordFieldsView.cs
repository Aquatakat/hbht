using System.Globalization;
using System.Text.Json.Nodes;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A record's merged JSON as a tree, with the plugins that set each top-level field. Branches are
/// filled in when first expanded, and long lists come in pages. A field naming a record is a link:
/// double-click it to open that record.
/// </summary>
public partial class RecordFieldsView : Tree
{
    private const int PageSize = 256;
    private static readonly Color LinkColor = new(0.5f, 0.72f, 1f);
    private static readonly Color MissingLinkColor = new(0.5f, 0.72f, 1f, 0.6f);

    private readonly Dictionary<TreeItem, Action<TreeItem>> _pending = [];
    private Func<RecordId, bool> _exists = _ => false;

    public RecordFieldsView()
    {
        Columns = 3;
        HideRoot = true;
        ColumnTitlesVisible = true;
        SetColumnTitle(0, "Field");
        SetColumnTitle(1, "Value");
        SetColumnTitle(2, "Set by");
        SetColumnExpandRatio(0, 2);
        SetColumnExpandRatio(1, 3);
        SetColumnExpand(2, false);
        SetColumnCustomMinimumWidth(2, 140);
        SelectMode = SelectModeEnum.Row;
        ItemCollapsed += OnItemCollapsed;
        ItemActivated += () =>
        {
            if (GetSelected()?.GetMetadata(1).AsString() is { Length: > 0 } text && RecordId.TryParse(text, out var id))
                Opened?.Invoke(id);
        };
    }

    /// <summary>A link was double-clicked.</summary>
    public event Action<RecordId>? Opened;

    /// <param name="setBy">The plugins that set a top-level field, in load order.</param>
    /// <param name="exists">Whether a record or asset is there to open, for how its links are drawn.</param>
    public void Show(JsonObject? record, Func<string, IReadOnlyList<string>> setBy, Func<RecordId, bool> exists)
    {
        Clear();
        _pending.Clear();
        _exists = exists;
        var root = CreateItem();
        if (record is null)
            return;
        foreach (var (field, value) in record)
        {
            // The record's own ID isn't a link.
            var item = field == "id" ? Add(root, field, value?.ToString()) : Add(root, field, value);
            item.SetText(2, string.Join(" → ", setBy(field)));
        }
    }

    private TreeItem Add(TreeItem parent, string name, string? text)
    {
        var item = CreateItem(parent);
        item.SetText(0, name);
        item.SetText(1, text ?? "");
        return item;
    }

    private TreeItem Add(TreeItem parent, string name, JsonNode? node)
    {
        var item = CreateItem(parent);
        item.SetText(0, name);
        item.SetText(1, Value(node));
        item.SetTooltipText(1, item.GetText(1));
        switch (node)
        {
            case JsonObject o when o.Count > 0:
                Defer(item, i =>
                {
                    foreach (var (key, value) in o)
                        Add(i, key, value);
                });
                break;
            case JsonArray a when a.Count > 0 && !IsInline(a):
                Defer(item, i => AddItems(i, a, 0, a.Count));
                break;
            case JsonValue v when v.TryGetValue(out string? text) && RecordId.TryParse(text, out var id):
                item.SetMetadata(1, text);
                item.SetCustomColor(1, _exists(id) ? LinkColor : MissingLinkColor);
                item.SetTooltipText(1, _exists(id) ? "Double-click to open." : "No plugin has this yet. Double-click to see what names it.");
                break;
        }
        return item;
    }

    private void AddItems(TreeItem parent, JsonArray array, int start, int count)
    {
        if (count <= PageSize)
        {
            for (var i = start; i < start + count; i++)
                Add(parent, $"[{i}]", array[i]);
            return;
        }
        var step = PageSize;
        while (count > step * PageSize)
            step *= PageSize;
        for (var s = start; s < start + count; s += step)
        {
            var (first, n) = (s, Math.Min(step, start + count - s));
            var page = CreateItem(parent);
            page.SetText(0, $"[{first}…{first + n - 1}]");
            Defer(page, i => AddItems(i, array, first, n));
        }
    }

    /// <summary>Makes an item expandable, filling it in when first expanded.</summary>
    private void Defer(TreeItem item, Action<TreeItem> fill)
    {
        // A placeholder child makes the item expandable until it's filled in.
        CreateItem(item);
        _pending[item] = fill;
        item.Collapsed = true;
    }

    private void OnItemCollapsed(TreeItem item)
    {
        if (item.Collapsed || !_pending.Remove(item, out var fill))
            return;
        item.GetFirstChild()?.Free();
        fill(item);
    }

    /// <summary>Short lists of numbers, such as vectors and colours, go on one line.</summary>
    private static bool IsInline(JsonArray a) => a.Count <= 4 && a.All(n => n is JsonValue v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number);

    private static string Value(JsonNode? node) => node switch
    {
        null => "null",
        JsonArray a when IsInline(a) => "[" + string.Join(", ", a.Select(n => n!.ToJsonString())) + "]",
        JsonArray a => string.Create(CultureInfo.InvariantCulture, $"{a.Count:N0} items"),
        // A map object reads as its type and index.
        JsonObject o when o["type"] is JsonValue t && o["index"] is JsonValue i => $"{t} {i}",
        JsonObject o => o.Count == 1 ? "1 field" : $"{o.Count} fields",
        JsonValue v when v.TryGetValue(out string? s) => s,
        _ => node.ToJsonString(),
    };
}
