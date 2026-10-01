using System.Globalization;
using Aidyn.Core.Characters;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Game;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A script's page: its nodes drawn as a graph, with their ops' records as links, and a player
/// that runs it with the core's <see cref="ScriptRunner"/> against the editor's flags and a party
/// of the core's characters: the loaded checkpoint's, or a new game's. Skill checks roll as the
/// original's do, or can be fixed to explore a branch; items are answered by hand until 2.3.
/// </summary>
public partial class ScriptPanel : VBoxContainer
{
    private const float NodeWidth = 300, ColumnGap = 70, RowGap = 28;
    private const string PlayerName = "Alaron";
    private static readonly Color Dim = new(0.65f, 0.65f, 0.65f);
    private static readonly StyleBoxFlat Plain = new() { BgColor = Colors.Transparent, ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2 };
    private static readonly StyleBoxFlat Current = new()
    {
        BgColor = new Color(0.42f, 0.66f, 1f, 0.2f),
        ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 2, ContentMarginBottom = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
    };
    private static readonly string[] Skills =
        ["Alchemist", "Diplomat", "Healer", "Loremaster", "Mechanic", "Merchant", "Ranger", "Stealth", "Thief", "Troubadour", "Warrior", "Wizard"];

    private readonly PluginCatalog _catalog;
    private readonly FlagState? _flags;
    private readonly DialogueRecord _script;
    private readonly Action<RecordId> _open;
    private readonly GraphEdit _graph = new()
    {
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        SizeFlagsVertical = SizeFlags.ExpandFill,
        MinimapEnabled = true,
        ShowGrid = false,
        ZoomMin = 0.1f,
        RightDisconnects = false,
    };
    // Each node's card, and its section of it.
    private readonly Dictionary<int, GraphNode> _nodes = [];
    private readonly Dictionary<int, PanelContainer> _sections = [];
    private readonly List<List<int>> _cards = [];
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer _page = new();
    private readonly VBoxContainer _answers = new();
    private readonly ItemList _log = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
    private readonly EditorWorld _world;
    private readonly CharacterRules? _rules;
    private readonly Label _party = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly Dictionary<RecordId, CheckBox> _members = [];
    private SpinBox? _gold;
    private readonly UiScreen _screen = new() { CustomMinimumSize = new Vector2(UiScreen.Width, UiScreen.Height), Backdrop = new Color(0.22f, 0.25f, 0.3f) };
    private readonly ConversationBoxView? _conversation;
    private ScriptPage? _shown;
    private OptionButton? _filterChoice, _zoomChoice, _aspectChoice, _linesChoice;
    private int _zoom = 1;
    private bool _wide;
    private ScriptRunner? _runner;
    private bool[]? _before;
    private bool _arranged;

    public ScriptPanel(PluginCatalog catalog, FlagState? flags, DialogueRecord script, Action<RecordId> open)
    {
        (_catalog, _flags, _script, _open) = (catalog, flags, script, open);
        _rules = EditorCharacters.Rules(catalog.Plugins);
        _world = new EditorWorld(Log, NameOf, entity => _rules is null ? null : EditorCharacters.Create(catalog.Plugins, _rules, entity, flags));
        AddThemeConstantOverride("separation", 6);
        // Plugins from before the importer wrote fonts have none to draw with.
        if (catalog.Plugins.Contains(RecordId.Decimal(RecordTypes.Font, 0)))
            _screen.Canvas.AddChild(_conversation = new ConversationBoxView(catalog.Plugins) { PlayerName = PlayerName, Visible = false });

        AddChild(Header());
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(_graph);
        GraphMenu.MoveToBottom(_graph);
        split.AddChild(Player());
        AddChild(split);
        BuildGraph();
    }

    /// <summary>Makes a number of moves as a player might: moves on, takes the first choice, or finishes a wait (<c>--play</c>).</summary>
    public void Play(int moves)
    {
        Restart();
        for (var i = 0; i < moves && _runner is { Status: not ScriptStatus.Ended } runner; i++)
        {
            if (runner.Status == ScriptStatus.Waiting)
                Act(runner.Wait == ScriptWait.Timer ? () => runner.Step(int.MaxValue) : runner.Resume);
            else if (runner.Page is { Choices.Count: > 0 } && runner.Wait != ScriptWait.Player)
                Act(() => runner.Choose(0));
            else
                Act(runner.Continue);
        }
    }

    // ---- The header -----------------------------------------------------------------------------

    private HFlowContainer Header()
    {
        var header = new HFlowContainer();
        header.AddThemeConstantOverride("h_separation", 12);
        var facts = new List<string> { $"{_script.Mode}, number {_script.Number}, {_script.Nodes.Count} nodes" };
        if (_script.Backdrop is { } backdrop)
            facts.Add($"backdrop {backdrop}");
        if (_script.Rest)
            facts.Add("eight hours pass when it ends");
        if (_script.BlankNodes > 0)
            facts.Add("a blank node, which random lines count");
        header.AddChild(new Label { Text = string.Join(", ", facts) + "." });
        if (_script.DoneFlag is { } done)
            header.AddChild(Link(done, "Sets when done: "));
        if (_script.BattleFlag is { } battle)
            header.AddChild(Link(battle, "Winning its battle sets: "));
        foreach (var actor in _script.Actors ?? [])
            header.AddChild(Link(actor, "Actor: "));
        return header;
    }

    private LinkButton Link(RecordId id, string prefix = "")
    {
        var button = new LinkButton { Text = prefix + NameOf(id), TooltipText = $"{id}\n{_catalog.Find(id)?.Summary}", Underline = LinkButton.UnderlineMode.OnHover };
        button.Pressed += () => _open(id);
        return button;
    }

    /// <summary>A record's name or label where it has one, else its key.</summary>
    private string NameOf(RecordId id)
    {
        var summary = _catalog.Find(id)?.Summary;
        return id.Type switch
        {
            RecordTypes.Flag => summary is { Length: > 0 } s ? $"flag {id.Key} ({Truncate(s, 40)})" : $"flag {id.Key}",
            RecordTypes.Dialogue => $"script {id.Key}",
            _ => summary is { Length: > 0 } name ? name : id.Key,
        };
    }

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    // ---- The graph ------------------------------------------------------------------------------

    /// <summary>
    /// Draws the script as cards. A run of lines, each the only way into the next, is one card, read
    /// down; a card ends where the script branches, and another starts where branches join. Each node
    /// is a section of its card, which lights up while the player is at it.
    /// </summary>
    private void BuildGraph()
    {
        foreach (var card in Cards(_script.Nodes))
        {
            var head = _script.Nodes[card[0]];
            var tail = _script.Nodes[card[^1]];
            var graphNode = new GraphNode
            {
                Name = $"n{head.Index}",
                Title = card.Count == 1 ? $"{head.Index} · {head.Kind}" : $"{head.Index}–{tail.Index} · {card.Count} lines",
            };
            var color = KindColor(head.Kind);
            graphNode.AddThemeStyleboxOverride("titlebar", new StyleBoxFlat { BgColor = color.Darkened(0.55f), ContentMarginLeft = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
            graphNode.AddThemeStyleboxOverride("titlebar_selected", new StyleBoxFlat { BgColor = color.Darkened(0.1f), ContentMarginLeft = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
            foreach (var index in card)
            {
                var section = Section(_script.Nodes[index], numbered: card.Count > 1, withLinks: index == tail.Index);
                graphNode.AddChild(section);
                _sections[index] = section;
                _nodes[index] = graphNode;
            }
            // In at the top, out at the bottom, where the card's last node says where it goes.
            graphNode.SetSlot(0, true, 0, color, card.Count == 1, 0, color);
            if (card.Count > 1)
                graphNode.SetSlot(card.Count - 1, false, 0, color, true, 0, color);
            _cards.Add(card);
            _graph.AddChild(graphNode);
        }
        foreach (var card in _cards)
        {
            foreach (var link in _script.Nodes[card[^1]].Links ?? [])
                _graph.ConnectNode(_nodes[card[0]].Name, 0, _nodes[link].Name, 0);
        }
        // Cards are placed by their size, which they have once their text has wrapped.
        _graph.Modulate = Colors.Transparent;
        Callable.From(Arrange).CallDeferred();
        _graph.GuiInput += FollowConnection;
    }

    /// <summary>
    /// Splits the nodes into cards: a line with one link continues into the node it links to, when
    /// that's a line with nothing else leading to it. Each card lists its nodes in reading order.
    /// </summary>
    private static List<List<int>> Cards(IReadOnlyList<DialogueNode> nodes)
    {
        var into = new int[nodes.Count];
        foreach (var link in nodes.SelectMany(n => n.Links ?? []))
            into[link]++;
        var carded = new bool[nodes.Count];
        var cards = new List<List<int>>();
        // Heads first, so a card starts where its run does, then any loop that nothing leads into.
        foreach (var start in nodes.Where(n => !Continued(n.Index)).Concat(nodes).Select(n => n.Index))
        {
            if (carded[start])
                continue;
            var card = new List<int>();
            for (int? index = start; index is { } i && !carded[i]; index = Next(i))
            {
                carded[i] = true;
                card.Add(i);
            }
            cards.Add(card);
        }
        return cards;

        bool Runs(DialogueNode node) => node.Kind == DialogueNodeKind.Line && node.Links is { Count: 1 };
        int? Next(int index) => Runs(nodes[index]) && Continued(nodes[index].Links![0]) ? nodes[index].Links![0] : null;
        bool Continued(int index) => index != 0 && nodes[index].Kind == DialogueNodeKind.Line && into[index] == 1
            && nodes.Any(n => Runs(n) && n.Links![0] == index);
    }

    private PanelContainer Section(DialogueNode node, bool numbered, bool withLinks)
    {
        var section = new PanelContainer { TooltipText = node.Text };
        section.AddThemeStyleboxOverride("panel", Plain);
        var body = new VBoxContainer { CustomMinimumSize = new Vector2(NodeWidth, 0) };
        section.AddChild(body);
        if (numbered)
            body.AddChild(Dimmed(node.Index.ToString(CultureInfo.InvariantCulture)));
        if (node.Text.Length > 0)
            body.AddChild(new Label { Text = Truncate(Display(node.Text), 240), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(NodeWidth, 0) });
        foreach (var op in node.Ops ?? [])
            body.AddChild(OpRow(op));
        if (LinkText(node, withLinks) is { } links)
            body.AddChild(Dimmed(links));
        return section;
    }

    private static Color KindColor(DialogueNodeKind kind) => kind switch
    {
        DialogueNodeKind.If => new Color(0.88f, 0.69f, 0.29f),
        DialogueNodeKind.Case => new Color(0.93f, 0.5f, 0.25f),
        DialogueNodeKind.Menu => new Color(0.42f, 0.66f, 1f),
        _ => new Color(0.6f, 0.62f, 0.67f),
    };

    private Control OpRow(DialogueOp op)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = op.Command.ToString() });
        if (op.Target is { } target)
            row.AddChild(Link(target));
        else if (op.Value is { } value)
            row.AddChild(Dimmed(op.Command == DialogueCommand.CheckSkill && value < Skills.Length ? Skills[value] : value.ToString(CultureInfo.InvariantCulture), wrap: false));
        return row;
    }

    /// <summary>A node's range, if a case selects it, and where it goes, which a card shows under its last node.</summary>
    private static string? LinkText(DialogueNode node, bool withLinks)
    {
        var links = node.Links ?? [];
        var range = node.Selects is { } r ? $"selected by {r.Min}{(r.Max is { } max ? max == r.Min ? "" : $"–{max}" : "+")}. " : "";
        var text = !withLinks ? "" : node.Kind switch
        {
            DialogueNodeKind.If when links.Count == 2 => $"1 → {links[0]}, else → {links[1]}",
            _ when links.Count > 0 => "→ " + string.Join(", ", links),
            DialogueNodeKind.Line => "No link: back to the last menu.",
            _ => "",
        };
        return range.Length + text.Length > 0 ? (range + text).TrimEnd() : null;
    }

    /// <summary>Grey text, wrapped at the card's width unless it's a word beside another.</summary>
    private static Label Dimmed(string text, bool wrap = true)
    {
        var label = wrap ? new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(NodeWidth, 0) } : new Label { Text = text };
        label.AddThemeColorOverride("font_color", Dim);
        return label;
    }

    /// <summary>
    /// Lays the cards out to read across: a card's first unplaced link goes to its right, level with
    /// where it leaves, and each other branch goes below, as high as it fits. Cards nothing reaches
    /// go in a last row. Waits for the cards' text to wrap, since that sets their height.
    /// </summary>
    private async void Arrange()
    {
        var height = -1f;
        for (var frame = 0; frame < 10; frame++)
        {
            // A card grows while its text wraps, but doesn't shrink back by itself.
            foreach (var card in _cards)
                _nodes[card[0]].Size = Vector2.Zero;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var total = _cards.Sum(c => _nodes[c[0]].Size.Y);
            if (total == height && _graph.Size.X > 0)
                break;
            height = total;
        }

        // The tree the layout follows: each card under the first card that links to it, in the
        // order a walk from the start reaches them.
        var heads = _cards.ToDictionary(c => _nodes[c[0]]);
        var children = heads.Keys.ToDictionary(c => c, _ => new List<GraphNode>());
        var reached = new HashSet<GraphNode>();
        Reach(_nodes[0]);
        var offsets = new Dictionary<GraphNode, float>();
        var bottom = Measure(_nodes[0]).Max(c => c.Bottom) + RowGap;
        // Columns are as wide as their widest card, since a long name can widen one.
        var columns = new Dictionary<GraphNode, int>();
        Place(_nodes[0], 0, 0);
        var lefts = new List<float> { 0 };
        foreach (var width in columns.GroupBy(c => c.Value).OrderBy(g => g.Key).Select(g => g.Max(c => c.Key.Size.X)))
            lefts.Add(lefts[^1] + width + ColumnGap);
        foreach (var (card, column) in columns)
            card.PositionOffset = card.PositionOffset with { X = lefts[column] };
        var x = 0f;
        foreach (var card in heads.Keys.Where(n => !reached.Contains(n)))
        {
            card.PositionOffset = new Vector2(x, bottom);
            x += card.Size.X + ColumnGap;
        }
        // The graph only scrolls as far as its cards were a frame ago.
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _graph.Modulate = Colors.White;
        _arranged = true;
        Focus(_runner?.Page?.Line.Node ?? 0);

        void Reach(GraphNode card)
        {
            reached.Add(card);
            foreach (var link in _script.Nodes[heads[card][^1]].Links ?? [])
            {
                if (reached.Add(_nodes[link]))
                {
                    children[card].Add(_nodes[link]);
                    Reach(_nodes[link]);
                }
            }
        }

        // Lays out a card's subtree with the card at the top of column 0, and returns how far down
        // each column's cards reach. The first branch lines its way in up with the card's way out;
        // each other branch goes as high as it can without meeting the branches above it.
        List<(float Top, float Bottom)> Measure(GraphNode card)
        {
            var contour = new List<(float Top, float Bottom)> { (0, card.Size.Y) };
            var first = true;
            foreach (var child in children[card])
            {
                var shape = Measure(child);
                var top = first ? Math.Max(0, card.GetOutputPortPosition(0).Y - child.GetInputPortPosition(0).Y) : 0;
                for (var column = 0; column < shape.Count && column + 1 < contour.Count; column++)
                    top = Math.Max(top, contour[column + 1].Bottom + RowGap - shape[column].Top);
                for (var column = 0; column < shape.Count; column++)
                {
                    var (childTop, childBottom) = (shape[column].Top + top, shape[column].Bottom + top);
                    if (column + 1 < contour.Count)
                        contour[column + 1] = (Math.Min(contour[column + 1].Top, childTop), Math.Max(contour[column + 1].Bottom, childBottom));
                    else
                        contour.Add((childTop, childBottom));
                }
                offsets[child] = top;
                first = false;
            }
            return contour;
        }

        void Place(GraphNode card, int column, float top)
        {
            card.PositionOffset = new Vector2(0, top);
            columns[card] = column;
            foreach (var child in children[card])
                Place(child, column + 1, top + offsets[child]);
        }
    }

    /// <summary>Double-clicking a link scrolls to the card at its far end from the click.</summary>
    private void FollowConnection(InputEvent input)
    {
        if (input is not InputEventMouseButton { ButtonIndex: MouseButton.Left, DoubleClick: true, Pressed: true } click)
            return;
        var connection = _graph.GetClosestConnectionAtPoint(click.Position, 8);
        if (connection.Count == 0)
            return;
        var from = _graph.GetNode<GraphNode>(connection["from_node"].AsString());
        var to = _graph.GetNode<GraphNode>(connection["to_node"].AsString());
        var at = (click.Position + _graph.ScrollOffset) / _graph.Zoom;
        var fromPort = from.PositionOffset + from.GetOutputPortPosition(0);
        var toPort = to.PositionOffset + to.GetInputPortPosition(0);
        var (card, port) = at.DistanceTo(toPort) >= at.DistanceTo(fromPort) ? (to, toPort) : (from, fromPort);
        var middle = new Vector2(card.PositionOffset.X + card.Size.X / 2, port.Y);
        _graph.CreateTween().TweenProperty(_graph, "scroll_offset", middle * _graph.Zoom - _graph.Size / 2, 0.25)
            .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        _graph.AcceptEvent();
    }

    /// <summary>Lights up a node and scrolls the graph to put it in the middle.</summary>
    private void Focus(int index)
    {
        foreach (var (i, section) in _sections)
            section.AddThemeStyleboxOverride("panel", i == index ? Current : Plain);
        foreach (var card in _nodes.Values.Distinct())
            card.Selected = card == _nodes.GetValueOrDefault(index);
        if (!_arranged || !_nodes.TryGetValue(index, out var focused))
            return;
        var zoom = _graph.Zoom;
        var at = _sections[index];
        var middle = focused.PositionOffset + new Vector2(focused.Size.X / 2, at.Position.Y + Math.Min(at.Size.Y, _graph.Size.Y / zoom) / 2);
        _graph.ScrollOffset = middle * zoom - _graph.Size / 2;
    }

    // ---- Playing it -----------------------------------------------------------------------------

    private Control Player()
    {
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) };
        box.AddThemeConstantOverride("separation", 6);
        var bar = new HBoxContainer();
        var play = new Button { Text = "Play", TooltipText = "Run the script from the start with the core's runner, against the flags the editor shows." };
        var skip = new Button { Text = "Skip", TooltipText = "End it, as R + A + Z + B does." };
        play.Pressed += Restart;
        skip.Pressed += () =>
        {
            if (_runner is { } runner)
                Act(runner.Skip);
        };
        bar.AddChild(play);
        bar.AddChild(skip);
        box.AddChild(bar);
        if (_conversation is not null)
        {
            box.AddChild(ScreenOptions());
            box.AddChild(_screen);
        }
        box.AddChild(_status);
        box.AddChild(_page);
        box.AddChild(new HSeparator());
        box.AddChild(Dimmed("What the script checks, answered here:"));
        var reset = new Button { Text = "Reset party", SizeFlagsVertical = SizeFlags.ShrinkCenter, TooltipText = "Back to the loaded checkpoint's party, or a new game's: Alaron alone." };
        reset.Pressed += () =>
        {
            NewParty();
            Log("The party is reset");
        };
        var partyRow = new HBoxContainer();
        _party.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        partyRow.AddChild(_party);
        partyRow.AddChild(reset);
        box.AddChild(partyRow);
        var answers = new ScrollContainer { CustomMinimumSize = new Vector2(0, 120), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        _answers.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        answers.AddChild(_answers);
        box.AddChild(answers);
        box.AddChild(new HSeparator());
        box.AddChild(Dimmed("What it did:"));
        box.AddChild(_log);
        BuildAnswers();
        NewParty();
        _status.Text = _flags is null ? "These plugins have no flag network, so it can't play." : $"Press Play to run it against {_flags.Name}'s flags.";
        play.Disabled = _flags is null;
        return box;
    }

    /// <summary>
    /// Sets how the conversation box's screen is shown (<c>--zoom</c>, <c>--ui-filter</c>,
    /// <c>--aspect</c>, <c>--ui-lines</c>). Options that aren't offered are ignored.
    /// </summary>
    public void SetScreen(int? zoom, UiFilter? filter, bool? wide, int? lines)
    {
        Pick(_zoomChoice, zoom);
        Pick(_filterChoice, (int?)filter);
        Pick(_aspectChoice, wide is { } w ? (w ? 1 : 0) : null);
        Pick(_linesChoice, lines);

        static void Pick(OptionButton? choice, int? id)
        {
            if (choice is not null && id is { } value && choice.GetItemIndex(value) is var i and >= 0)
            {
                choice.Select(i);
                choice.EmitSignal(OptionButton.SignalName.ItemSelected, i);
            }
        }
    }

    /// <summary>How the conversation box's screen is shown: its filter, size, shape and lines, to compare with the console.</summary>
    private HFlowContainer ScreenOptions()
    {
        var row = new HFlowContainer();
        row.AddThemeConstantOverride("h_separation", 6);
        row.AddChild(_filterChoice = Choice("How the UI screen is scaled up to the window.",
            [((int)UiFilter.VideoOut, "As the console outputs it"), ((int)UiFilter.Pixels, "Sharp pixels"), ((int)UiFilter.Smooth, "Smooth")],
            id => _screen.Filter = (UiFilter)id));
        row.AddChild(_zoomChoice = Choice("How large the preview is.", [(1, "1×"), (2, "2×"), (3, "3×")], id =>
        {
            _zoom = id;
            SizeScreen();
        }));
        row.AddChild(_aspectChoice = Choice("The window's shape: on a wider one the box floats at the bottom centre.", [(0, "4:3"), (1, "16:9")], id =>
        {
            _wide = id == 1;
            SizeScreen();
        }));
        row.AddChild(_linesChoice = Choice("How many pixels tall the UI screen is: 240 is the original's, and more draws the UI smaller.",
            [(240, "240 lines"), (360, "360 lines"), (480, "480 lines")], id => _screen.Lines = id));
        return row;

        static OptionButton Choice(string tooltip, (int Id, string Name)[] items, Action<int> apply)
        {
            var choice = new OptionButton { TooltipText = tooltip };
            foreach (var (id, name) in items)
                choice.AddItem(name, id);
            choice.ItemSelected += i => apply(choice.GetItemId((int)i));
            return choice;
        }
    }

    private void SizeScreen() =>
        _screen.CustomMinimumSize = new Vector2(_wide ? MathF.Round(UiScreen.Height * 16 / 9f) : UiScreen.Width, UiScreen.Height) * _zoom;

    /// <summary>The party, and a control for each thing the script checks other than flags.</summary>
    private void BuildAnswers()
    {
        var ops = _script.Nodes.SelectMany(n => n.Ops ?? []).ToList();
        var checks = ops.Where(o => o.Command.IsCondition()).ToList();
        foreach (var member in checks.Where(o => o.Command == DialogueCommand.CheckMember).Select(o => o.Target).OfType<RecordId>().Distinct())
        {
            var box = new CheckBox { Text = $"{NameOf(member)} is in the party" };
            box.Toggled += on =>
            {
                if (!on)
                    _world.Leave(member);
                else if (!_world.Join(member))
                    Log($"{NameOf(member)} can't join: the party is full, or there's no character record");
                ShowParty();
            };
            _members[member] = box;
            _answers.AddChild(box);
        }
        foreach (var item in checks.Where(o => o.Command == DialogueCommand.CheckItem).Select(o => o.Target).OfType<RecordId>().Distinct())
            _answers.AddChild(Answer($"The party has {NameOf(item)}", _world.Items, item));
        foreach (var skill in checks.Where(o => o.Command == DialogueCommand.CheckSkill).Select(o => o.Value ?? 0).Distinct().Order())
            _answers.AddChild(FixedCheck(skill));
        if (checks.Any(o => o.Command == DialogueCommand.CheckGold) || ops.Any(o => o.Command is DialogueCommand.TakeGold))
        {
            var row = new HBoxContainer();
            _gold = new SpinBox { MinValue = 0, MaxValue = 10_000_000, Step = 1 };
            _gold.ValueChanged += v =>
            {
                _world.Gold = (uint)v;
                ShowParty();
            };
            row.AddChild(new Label { Text = "Gold", SizeFlagsHorizontal = SizeFlags.ExpandFill });
            row.AddChild(_gold);
            _answers.AddChild(row);
        }
        if (_answers.GetChildCount() == 0)
            _answers.AddChild(Dimmed("Nothing but flags and the party."));
    }

    /// <summary>A skill check's result: rolled with the party's skills as the original rolls it, or fixed to see a branch.</summary>
    private HBoxContainer FixedCheck(int skill)
    {
        var row = new HBoxContainer();
        var choice = new OptionButton { TooltipText = "Roll it with the party's skills, as the original does, or fix the step it gives (0–21)." };
        choice.AddItem("Rolled", -1);
        for (var step = 0; step <= 21; step++)
            choice.AddItem($"Fixed at {step}", step);
        choice.ItemSelected += i =>
        {
            if (choice.GetItemId((int)i) is var step and >= 0)
                _world.FixedChecks[skill] = step;
            else
                _world.FixedChecks.Remove(skill);
        };
        row.AddChild(new Label { Text = $"{SkillName(skill)} check", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(choice);
        return row;
    }

    private static string SkillName(int skill) => skill >= 0 && skill < Skills.Length ? Skills[skill] : $"Skill {skill}";

    /// <summary>The party as it starts: the loaded checkpoint's, or a new game's.</summary>
    private void NewParty()
    {
        _world.Party = _rules is null ? new Party() : EditorCharacters.NewParty(_catalog.Plugins, _rules, _flags);
        ShowParty();
    }

    /// <summary>Shows the party, its gold and the stats the script checks, after it or the answers change them.</summary>
    private void ShowParty()
    {
        var party = _world.Party;
        var members = party.Members.Select(m => $"{m.Name} ({m.Level}, {m.Hp}/{m.MaxHp} HP)").ToList();
        var text = _rules is null
            ? "These plugins are older than the characters' records, so the party is empty. Import the ROM again."
            : $"Party, by level and hit points: {(members.Count == 0 ? "no one" : string.Join(", ", members))}. {party.Gold:N0} gold.";
        var stats = _script.Nodes.SelectMany(n => n.Ops ?? [])
            .Where(o => o.Command is DialogueCommand.CheckBestStat or DialogueCommand.CheckWorstStat or DialogueCommand.CheckSpeakerStat)
            .Select(o => o.Value ?? 0).Where(v => v is >= 0 and <= (int)Stat.Level).Distinct().Order();
        foreach (var stat in stats)
            text += $" {(Stat)stat}: best {party.BestStat((Stat)stat)}, worst {party.WorstStat((Stat)stat)}.";
        _party.Text = text;
        foreach (var (member, box) in _members)
            box.SetPressedNoSignal(party.Contains(member));
        _gold?.SetValueNoSignal(party.Gold);
    }

    private static CheckBox Answer(string text, HashSet<RecordId> set, RecordId id)
    {
        var box = new CheckBox { Text = text, ButtonPressed = set.Contains(id) };
        box.Toggled += on =>
        {
            if (on)
                set.Add(id);
            else
                set.Remove(id);
        };
        return box;
    }

    private void Restart()
    {
        if (_flags is null)
            return;
        _log.Clear();
        _world.Memory.Clear();
        var randomSkillChecks = _catalog.Plugins.All<StateFlagsRecord>(RecordTypes.StateFlags).SingleOrDefault()?.RandomSkillChecks;
        _runner = new ScriptRunner(_script, _flags.Network, _world, new Host(cue => Log(Describe(cue))), new Lcg(), id => _catalog.Plugins.TryGet<DialogueRecord>(id),
            randomSkillChecks: randomSkillChecks);
        Act(_runner.Start);
    }

    /// <summary>Makes a move, then logs the flags it changed and shows where the script is.</summary>
    private void Act(Action move)
    {
        if (_flags is null || _runner is null)
            return;
        var network = _flags.Network;
        _before ??= new bool[network.Count];
        for (var h = 0; h < network.Count; h++)
            _before[h] = network.Peek(h);
        move();
        network.Process();
        ShowParty();
        var changed = Enumerable.Range(2, network.Count - 2).Where(h => !network.IsDerived(h) && network.Peek(h) != _before[h]).ToList();
        foreach (var h in changed)
            Log($"Flag {network.Id(h).Key} {(network.Peek(h) ? "set" : "cleared")}: {NameOf(network.Id(h))}");
        if (changed.Count > 0)
            _flags.Edited();
        ShowPage();
    }

    private void ShowPage()
    {
        foreach (var child in _page.GetChildren())
        {
            _page.RemoveChild(child);
            child.QueueFree();
        }
        if (_runner is not { } runner)
            return;

        if (runner.Status != ScriptStatus.Showing && _conversation is not null)
        {
            _conversation.Visible = false;
            _shown = null;
        }
        switch (runner.Status)
        {
            case ScriptStatus.Ended:
                var ending = runner.Ending!;
                _status.Text = ending.Kind switch
                {
                    ScriptEndKind.Shop => "Ended, opening the speaker's shop",
                    ScriptEndKind.Train => "Ended, opening the speaker's training",
                    ScriptEndKind.Battle => "Ended, starting a battle",
                    ScriptEndKind.Camp => "Ended with the party healed",
                    _ => "Ended",
                } + (ending.RestHours > 0 ? $"; {ending.RestHours} hours pass" : "")
                    + (ending.PartnerJoined ? ", and the one talking leaves to join" : "") + (ending.Fault is { } fault ? $". {fault}" : ".");
                if (ending.Battle is { } battle)
                    _page.AddChild(Dimmed($"Battle in arena {battle.Battlefield} against {string.Join(", ", battle.Enemies.Select(NameOf))}{(battle.CanFlee ? "" : ", no fleeing")}."));
                return;
            case ScriptStatus.Waiting:
                _status.Text = $"Waiting: {runner.Wait}.";
                var resume = new Button { Text = runner.Wait == ScriptWait.Timer ? "Let the time pass" : $"{runner.Wait} done" };
                resume.Pressed += () => Act(runner.Wait == ScriptWait.Timer ? () => runner.Step(int.MaxValue) : runner.Resume);
                _page.AddChild(resume);
                return;
        }

        var page = runner.Page!;
        if (!ReferenceEquals(page, _shown))
        {
            _shown = page;
            _conversation?.Show(page);
        }
        // A line from the speaker of the choices under it shows alone first, and A brings them up.
        var lineFirst = _conversation?.Box.ShowsLineBeforeChoices == true;
        Focus(page.Line.Node);
        _status.Text = runner.Wait == ScriptWait.Player ? "Waiting for A." : $"At node {page.Line.Node}.";
        _page.AddChild(Dimmed(page.Line.Speaker is { } speaker ? NameOf(speaker) : "No speaker"));
        _page.AddChild(new Label { Text = Display(page.Line.Text), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) });
        if (page.Choices.Count > 0 && runner.Wait != ScriptWait.Player && !lineFirst)
        {
            for (var i = 0; i < page.Choices.Count; i++)
            {
                var choice = i;
                var button = new Button
                {
                    Text = Display(page.Choices[i].Text),
                    Alignment = HorizontalAlignment.Left,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    TooltipText = $"Node {page.Choices[i].Node}",
                };
                button.Pressed += () => Act(() => runner.Choose(choice));
                button.MouseEntered += () => _conversation?.Point(choice);
                button.FocusEntered += () => _conversation?.Point(choice);
                _page.AddChild(button);
            }
        }
        else
        {
            var next = new Button { Text = "Continue (A)" };
            next.Pressed += lineFirst
                ? () =>
                {
                    _conversation!.ShowChoices();
                    ShowPage();
                }
                : () => Act(runner.Continue);
            _page.AddChild(next);
        }
    }

    private static string Display(string text) => text.Replace("{player}", PlayerName, StringComparison.Ordinal);

    private void Log(string line) => _log.AddItem(line);

    private string Describe(ScriptCue cue) => cue switch
    {
        CameraCue c => $"Camera {c.Command}" + (c.RefPoint is { } p ? $" ref {p}" : "") + (c.Actor is { } a ? $" {NameOf(a)}" : "") + $", {c.Ticks} ticks",
        FaceCue f => $"{Who(f.Actor)} faces ref {f.RefPoint}",
        SpawnCue s => $"{Who(s.Actor)} spawns at ref {s.RefPoint}",
        MoveCue m => $"{Who(m.Actor)} {(m.Run ? "runs" : "walks")} to ref {m.RefPoint}",
        AnimateCue a => a.Actor is { } actor ? $"{NameOf(actor)} plays animation {a.Request}" : $"Animation {a.Request} with no actor: nothing plays",
        HoldCue h => h.Item is { } item ? $"{Who(h.Actor)} holds {NameOf(item)}" : $"{Who(h.Actor)} puts it away",
        SoundCue s => s.Sound == ScriptSound.Table ? $"Sound {s.Index}" : $"Sound: {s.Sound}",
        FadeOutCue => "Fade out",
        _ => cue.ToString(),
    };

    private string Who(RecordId? actor) => actor is { } id ? NameOf(id) : "No one";

    private sealed class Host(Action<ScriptCue> log) : IScriptHost
    {
        public void Cue(ScriptCue cue) => log(cue);
    }

    /// <summary>
    /// The party, which the script and the answers change, and the inventory, as the answers set it.
    /// The script's changes are logged.
    /// </summary>
    private sealed class EditorWorld(Action<string> log, Func<RecordId, string> name, Func<RecordId, Character?> create) : IScriptWorld
    {
        public Party Party { get; set; } = new();
        public HashSet<RecordId> Items { get; } = [];
        /// <summary>Skill checks fixed by hand, by skill; the rest roll.</summary>
        public Dictionary<int, int> FixedChecks { get; } = [];
        public Dictionary<ScriptMemory, int> Memory { get; } = [];
        public int PartySize => Party.Count;

        public uint Gold
        {
            get => Party.Gold;
            set => Party.Gold = value;
        }

        public bool InParty(RecordId entity) => Party.Contains(entity);
        public bool HasItem(RecordId item) => Items.Contains(item);
        public int BestStat(int stat) => IsStat(stat) ? Party.BestStat((Stat)stat) : 0;
        public int WorstStat(int stat) => IsStat(stat) ? Party.WorstStat((Stat)stat) : 0;
        public int MemberStat(RecordId? member, int stat) => member is { } m && IsStat(stat) ? Party.MemberStat(m, (Stat)stat) : 0;
        public bool TryRecall(ScriptMemory memory, out int value) => Memory.TryGetValue(memory, out value);
        public void Remember(ScriptMemory memory, int value) => Memory[memory] = value;

        private static bool IsStat(int stat) => stat is >= 0 and <= (int)Stat.Level;

        public int SkillCheck(int skill, IRandom random)
        {
            if (FixedChecks.TryGetValue(skill, out var step))
            {
                log($"{SkillName(skill)} check: fixed at {step}");
                return step;
            }
            var result = Party.SkillCheck((Skill)skill, random);
            log($"{SkillName(skill)} check: rolled {result}");
            return result;
        }

        public void CampHeal()
        {
            Party.CampHeal();
            log("The party is healed");
        }

        public void AddItem(RecordId item)
        {
            Items.Add(item);
            log($"{name(item)} added");
        }

        public bool TakeItem(RecordId item)
        {
            var taken = Items.Remove(item);
            log(taken ? $"{name(item)} taken" : $"{name(item)} isn't held, so nothing is taken");
            return taken;
        }

        public bool AddMember(RecordId entity)
        {
            var joined = Join(entity);
            log(joined ? $"{name(entity)} joins" : $"{name(entity)} can't join: " + (create(entity) is null ? "there's no character record" : "the party is full"));
            return joined;
        }

        public void RemoveMember(RecordId entity) =>
            log(Leave(entity) ? $"{name(entity)} leaves" : $"{name(entity)} isn't in the party, so no one leaves");

        /// <summary>Adds a member as their record starts them. True if they're in the party after.</summary>
        public bool Join(RecordId entity) => Party.Contains(entity) || (create(entity) is { } member && Party.Add(member));

        public bool Leave(RecordId entity) => Party.Remove(entity);

        public bool AddExp(RecordId member, int amount)
        {
            if (Party.Get(member) is not { } character)
            {
                log($"{name(member)} would gain {amount} experience, but isn't in the party");
                return false;
            }
            var (level, before) = (character.Level, character.TotalExp);
            character.AddExp(amount);
            log($"{name(member)} gains {character.TotalExp - before:N0} experience" + (character.Level > level ? $" and reaches level {character.Level}" : ""));
            return true;
        }
    }
}
