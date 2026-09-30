using System.Globalization;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// The quest atlas for one record (<see cref="QuestIndex"/>): what sets and reads a flag, or which
/// flags a script, map object or journal entry reads and sets, and the graph around it. Edges point
/// the way things happen: from what sets a flag, to the flag, to what reads it.
/// </summary>
public partial class AtlasView : VBoxContainer
{
    private const int NodeLimit = 160;
    private const float NodeWidth = 260, ColumnGap = 110, RowGap = 16;

    private readonly Tree _uses = new()
    {
        Columns = 4,
        HideRoot = true,
        SelectMode = Tree.SelectModeEnum.Row,
        ColumnTitlesVisible = true,
        CustomMinimumSize = new Vector2(0, 200),
    };
    private readonly OptionButton _direction = new() { TooltipText = "Which way to follow the edges" };
    private readonly SpinBox _depth = new() { MinValue = 1, MaxValue = 8, Value = 2, TooltipText = "How many steps to follow" };
    private readonly CheckBox _hideEngine = new() { Text = "Hide engine flags", ButtonPressed = true, TooltipText = "Leave out the flags the engine writes its state into, such as the time of day." };
    private readonly Label _info = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly GraphEdit _graph = new()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill,
        MinimapEnabled = true,
        ShowGrid = false,
        ZoomMin = 0.1f,
    };
    private readonly Dictionary<RecordId, GraphNode> _nodes = [];

    private PluginCatalog? _catalog;
    private QuestIndex? _index;
    private Action<RecordId>? _open;
    private IReadOnlyList<RecordId> _keys = [];
    private HashSet<RecordId> _engine = [];

    public AtlasView()
    {
        AddThemeConstantOverride("separation", 6);
        string[] titles = ["Does", "Record", "Where", "Summary"];
        for (var c = 0; c < titles.Length; c++)
        {
            _uses.SetColumnTitle(c, titles[c]);
            _uses.SetColumnClipContent(c, true);
            _uses.SetColumnExpand(c, c == 3);
        }
        _uses.SetColumnCustomMinimumWidth(0, 190);
        _uses.SetColumnCustomMinimumWidth(1, 190);
        _uses.SetColumnCustomMinimumWidth(2, 90);
        _uses.ItemActivated += () =>
        {
            if (_uses.GetSelected()?.GetMetadata(1).AsString() is { } text && RecordId.TryParse(text, out var id))
                _open?.Invoke(id);
        };
        AddChild(_uses);

        var bar = new HBoxContainer();
        _direction.AddItem("Upstream: what leads to it", 0);
        _direction.AddItem("Downstream: what it leads to", 1);
        _direction.AddItem("Both", 2);
        _direction.Selected = 2;
        _direction.ItemSelected += _ => DrawGraph();
        _depth.ValueChanged += _ => DrawGraph();
        _hideEngine.Toggled += _ => DrawGraph();
        bar.AddChild(_direction);
        bar.AddChild(new Label { Text = "Steps" });
        bar.AddChild(_depth);
        bar.AddChild(_hideEngine);
        bar.AddChild(_info);
        AddChild(bar);
        AddChild(_graph);
        GraphMenu.MoveToBottom(_graph);
    }

    /// <summary>Shows a record's uses and graph. A map is shown through its objects.</summary>
    public void Show(PluginCatalog catalog, QuestIndex index, RecordId id, Action<RecordId> open)
    {
        (_catalog, _index, _open) = (catalog, index, open);
        _engine = [.. catalog.Plugins.All<StateFlagsRecord>(RecordTypes.StateFlags).SelectMany(s => index.UsesBy(s.Id)).Select(u => u.Flag)];
        _keys = id.Type == RecordTypes.Map && catalog.Plugins.TryGet<MapRecord>(id) is { } map
            ? [.. map.Objects.Select(o => RecordId.Parse($"{id.Namespace}:{RecordTypes.Voxel}/{id.Key}/{o.Index}"))]
            : [id];
        ListUses();
        DrawGraph();
    }

    /// <summary>How many uses <see cref="Show"/> listed, for the tab's title.</summary>
    public int Count { get; private set; }

    private void ListUses()
    {
        _uses.Clear();
        var root = _uses.CreateItem();
        Count = 0;
        foreach (var key in _keys)
        {
            // A flag's own uses, then what the record does with other flags.
            foreach (var use in _index!.UsesOf(key))
                Row(root, Does(use, flagFirst: true), use.By, use);
            foreach (var use in _index.UsesBy(key))
                Row(root, Does(use, flagFirst: false), use.Flag, use, key == _keys[0] && _keys.Count == 1 ? null : key);
        }
        if (Count == 0)
            _uses.CreateItem(root).SetText(3, "Nothing here reads or sets a flag.");
    }

    private void Row(TreeItem root, string does, RecordId other, FlagUse use, RecordId? of = null)
    {
        var row = _uses.CreateItem(root);
        row.SetText(0, does);
        row.SetText(1, other.ToString());
        row.SetMetadata(1, other.ToString());
        row.SetTooltipText(1, other.ToString());
        row.SetText(2, use.At is { } node ? $"node {node}" : of is { } key ? key.Key : "");
        var summary = _catalog!.Find(other)?.Summary ?? "";
        row.SetText(3, summary);
        row.SetTooltipText(3, summary);
        Count++;
    }

    /// <summary>What a use does, said from the flag's side or from the user's.</summary>
    private static string Does(FlagUse use, bool flagFirst)
    {
        var verb = use.Access switch
        {
            FlagAccess.Set => flagFirst ? "set by" : "sets",
            FlagAccess.Clear => flagFirst ? "cleared by" : "clears",
            _ => flagFirst ? "read by" : "reads",
        };
        var how = use.Role switch
        {
            FlagRole.ScriptOp => use.Access == FlagAccess.Read ? "a check" : "an op",
            FlagRole.ScriptDone => "the end",
            FlagRole.ScriptBattle => "winning the battle",
            FlagRole.DerivedInput => "a formula",
            FlagRole.JournalAdds => "the entry it adds",
            FlagRole.JournalListed => "the journal's list",
            FlagRole.ObjectVisible => use.Inverted ? "a gate, while clear" : "a gate",
            FlagRole.Engine => "the engine",
            _ => use.Role.ToString(),
        };
        return $"{verb} ({how})";
    }

    // ---- The graph ------------------------------------------------------------------------------

    private void DrawGraph()
    {
        if (_index is null || _catalog is null)
            return;
        _graph.ClearConnections();
        foreach (var node in _nodes.Values)
        {
            _graph.RemoveChild(node);
            node.QueueFree();
        }
        _nodes.Clear();

        // Each record's column is its signed distance: upstream to the left, downstream to the right.
        var column = _keys.ToDictionary(k => k, _ => 0);
        var edges = new HashSet<QuestEdge>();
        var directions = _direction.Selected switch { 0 => new[] { -1 }, 1 => [1], _ => [-1, 1] };
        foreach (var direction in directions)
        {
            var frontier = _keys.ToList();
            for (var step = 1; step <= (int)_depth.Value && frontier.Count > 0 && column.Count < NodeLimit; step++)
            {
                var next = new List<RecordId>();
                foreach (var key in frontier)
                {
                    foreach (var edge in direction < 0 ? _index.Upstream(key) : _index.Downstream(key))
                    {
                        var other = direction < 0 ? edge.From : edge.To;
                        if (_hideEngine.ButtonPressed && (_engine.Contains(other) || other.Type == RecordTypes.StateFlags))
                            continue;
                        if (!column.ContainsKey(other))
                        {
                            if (column.Count >= NodeLimit)
                                continue;
                            column[other] = direction * step;
                            next.Add(other);
                        }
                        edges.Add(edge);
                    }
                }
                frontier = next;
            }
        }

        var tops = new Dictionary<int, float>();
        foreach (var (key, x) in column.OrderBy(c => c.Value))
        {
            var node = GraphNodeFor(key);
            var top = tops.GetValueOrDefault(x);
            node.PositionOffset = new Vector2(x * (NodeWidth + ColumnGap), top);
            tops[x] = top + 110 + RowGap;
            _nodes[key] = node;
            _graph.AddChild(node);
        }
        foreach (var edge in edges)
        {
            if (_nodes.ContainsKey(edge.From) && _nodes.ContainsKey(edge.To))
                _graph.ConnectNode(_nodes[edge.From].Name, 0, _nodes[edge.To].Name, 0);
        }
        _info.Text = $"{_nodes.Count} records{(column.Count >= NodeLimit ? $" (the limit, {NodeLimit})" : "")}";
        Callable.From(Center).CallDeferred();
    }

    private GraphNode GraphNodeFor(RecordId key)
    {
        var focused = _keys.Contains(key);
        var color = key.Type switch
        {
            RecordTypes.Flag => _catalog!.Plugins.TryGet<FlagRecord>(key)?.Formula is null ? new Color(0.88f, 0.69f, 0.29f) : new Color(0.77f, 0.54f, 0.94f),
            RecordTypes.Dialogue => new Color(0.44f, 0.66f, 1f),
            RecordTypes.Voxel => new Color(0.49f, 0.81f, 0.54f),
            RecordTypes.Journal => new Color(0.9f, 0.9f, 0.9f),
            _ => new Color(0.6f, 0.62f, 0.67f),
        };
        var node = new GraphNode
        {
            Name = "r" + _nodes.Count.ToString(CultureInfo.InvariantCulture),
            Title = Title(key),
            Selected = focused,
            TooltipText = key.ToString(),
        };
        var style = new StyleBoxFlat { BgColor = focused ? color : color.Darkened(0.55f), ContentMarginLeft = 8, ContentMarginTop = 4, ContentMarginBottom = 4 };
        node.AddThemeStyleboxOverride("titlebar", style);
        node.AddThemeStyleboxOverride("titlebar_selected", style);
        var open = new Button
        {
            Text = Truncate(Summary(key), 90),
            TooltipText = $"Open {key}",
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(NodeWidth, 0),
        };
        open.Pressed += () => _open?.Invoke(key);
        node.AddChild(open);
        node.SetSlot(0, true, 0, color, true, 0, color);
        return node;
    }

    private string Title(RecordId key) => key.Type switch
    {
        RecordTypes.Flag => $"Flag {key.Key}",
        RecordTypes.Dialogue => $"Script {key.Key}",
        RecordTypes.Voxel => $"Object {key.Key}",
        RecordTypes.Journal => $"Journal {key.Key}",
        _ => key.ToString(),
    };

    private string Summary(RecordId key)
    {
        if (key.Type == RecordTypes.Voxel)
        {
            var map = RecordId.Parse($"{key.Namespace}:{RecordTypes.Map}/{key.Key[..key.Key.IndexOf('/', StringComparison.Ordinal)]}");
            return $"In map {map.Key}: {_catalog!.Find(map)?.Summary}";
        }
        var summary = _catalog!.Find(key)?.Summary;
        return !string.IsNullOrEmpty(summary) ? summary : key.Type == RecordTypes.Flag ? "Plain flag" : key.ToString();
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    /// <summary>Scrolls to the record shown.</summary>
    private void Center()
    {
        if (_keys.Count > 0 && _nodes.TryGetValue(_keys[0], out var node) && _graph.Size.X > 0)
            _graph.ScrollOffset = (node.PositionOffset + node.Size / 2) * _graph.Zoom - _graph.Size / 2;
    }
}
