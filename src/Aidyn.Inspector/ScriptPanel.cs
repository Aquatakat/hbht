using System.Globalization;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// A script's page: its nodes drawn as a graph, with their ops' records as links, and a player
/// that runs it with the core's <see cref="ScriptRunner"/> against the editor's flags. The party,
/// items and skills the script checks are answered by controls beside it.
/// </summary>
public partial class ScriptPanel : VBoxContainer
{
    private const float NodeWidth = 300, ColumnGap = 90, RowGap = 28;
    private const string PlayerName = "Alaron";
    private static readonly Color Dim = new(0.65f, 0.65f, 0.65f);
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
    private readonly Dictionary<int, GraphNode> _nodes = [];
    private readonly Label _status = new() { AutowrapMode = TextServer.AutowrapMode.WordSmart };
    private readonly VBoxContainer _page = new();
    private readonly VBoxContainer _answers = new();
    private readonly ItemList _log = new() { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 160) };
    private readonly EditorWorld _world;
    private ScriptRunner? _runner;
    private bool[]? _before;
    private bool _focused;

    public ScriptPanel(PluginCatalog catalog, FlagState? flags, DialogueRecord script, Action<RecordId> open)
    {
        (_catalog, _flags, _script, _open) = (catalog, flags, script, open);
        _world = new EditorWorld(Log, NameOf);
        AddThemeConstantOverride("separation", 6);

        AddChild(Header());
        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(_graph);
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

    private void BuildGraph()
    {
        var positions = Layout();
        foreach (var node in _script.Nodes)
        {
            var graphNode = new GraphNode
            {
                Name = $"n{node.Index}",
                Title = $"{node.Index} · {node.Kind}",
                PositionOffset = positions[node.Index],
                TooltipText = node.Text,
            };
            var color = KindColor(node.Kind);
            graphNode.AddThemeStyleboxOverride("titlebar", new StyleBoxFlat { BgColor = color.Darkened(0.55f), ContentMarginLeft = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
            graphNode.AddThemeStyleboxOverride("titlebar_selected", new StyleBoxFlat { BgColor = color.Darkened(0.1f), ContentMarginLeft = 8, ContentMarginTop = 4, ContentMarginBottom = 4 });
            var body = new VBoxContainer { CustomMinimumSize = new Vector2(NodeWidth, 0) };
            graphNode.AddChild(body);
            graphNode.SetSlot(0, true, 0, color, true, 0, color);

            if (node.Text.Length > 0)
                body.AddChild(new Label { Text = Truncate(Display(node.Text), 240), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(NodeWidth, 0) });
            foreach (var op in node.Ops ?? [])
                body.AddChild(OpRow(op));
            if (LinkText(node) is { } links)
                body.AddChild(Dimmed(links));
            _nodes[node.Index] = graphNode;
            _graph.AddChild(graphNode);
        }
        foreach (var node in _script.Nodes)
        {
            foreach (var link in node.Links ?? [])
                _graph.ConnectNode($"n{node.Index}", 0, $"n{link}", 0);
        }
        // The graph has no size until it's laid out.
        _graph.Resized += () =>
        {
            if (!_focused)
                Focus(_runner?.Page?.Line.Node ?? 0);
        };
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
            row.AddChild(Dimmed(op.Command == DialogueCommand.CheckSkill && value < Skills.Length ? Skills[value] : value.ToString(CultureInfo.InvariantCulture)));
        return row;
    }

    private string? LinkText(DialogueNode node)
    {
        var links = node.Links ?? [];
        var range = node.Selects is { } r ? $"selected by {r.Min}{(r.Max is { } max ? max == r.Min ? "" : $"–{max}" : "+")}. " : "";
        var text = node.Kind switch
        {
            DialogueNodeKind.If when links.Count == 2 => $"1 → {links[0]}, else → {links[1]}",
            _ when links.Count > 0 => "→ " + string.Join(", ", links),
            DialogueNodeKind.Line => "No link: back to the last menu.",
            _ => "",
        };
        return range.Length + text.Length > 0 ? range + text : null;
    }

    private static Label Dimmed(string text)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(NodeWidth, 0) };
        label.AddThemeColorOverride("font_color", Dim);
        return label;
    }

    /// <summary>
    /// Lays the script out to read across: a node's first unplaced link goes to its right, and each
    /// other branch starts a new row below everything so far. Nodes nothing reaches go in a last row.
    /// Ports are on the nodes' sides, so chains read along a row with short links.
    /// </summary>
    private Dictionary<int, Vector2> Layout()
    {
        var positions = new Dictionary<int, Vector2>();
        var bottom = 0f;
        Place(0, 0, 0);
        var x = 0f;
        var top = bottom;
        foreach (var node in _script.Nodes.Where(n => !positions.ContainsKey(n.Index)))
        {
            positions[node.Index] = new Vector2(x, top);
            x += NodeWidth + ColumnGap;
        }
        return positions;

        void Place(int index, int column, float rowTop)
        {
            positions[index] = new Vector2(column * (NodeWidth + ColumnGap), rowTop);
            bottom = Math.Max(bottom, rowTop + EstimatedHeight(_script.Nodes[index]) + RowGap);
            var first = true;
            foreach (var link in _script.Nodes[index].Links ?? [])
            {
                if (positions.ContainsKey(link))
                    continue;
                Place(link, column + 1, first ? rowTop : bottom);
                first = false;
            }
        }
    }

    private static float EstimatedHeight(DialogueNode node)
    {
        var lines = node.Text.Length == 0 ? 0 : Math.Min(240, node.Text.Length) / 42 + 1 + node.Text.Count(c => c == '\n');
        return 40 + lines * 23 + (node.Ops?.Count ?? 0) * 31 + 23 + (node.Selects is null ? 0 : 23);
    }

    /// <summary>Selects a node and scrolls the graph to it, with more room for what follows than for what came before.</summary>
    private void Focus(int index)
    {
        foreach (var (i, node) in _nodes)
            node.Selected = i == index;
        if (!_nodes.TryGetValue(index, out var focused) || _graph.Size.X <= 0)
            return;
        _focused = true;
        var zoom = _graph.Zoom;
        _graph.ScrollOffset = new Vector2(focused.PositionOffset.X * zoom - _graph.Size.X / 4, (focused.PositionOffset.Y + focused.Size.Y / 2) * zoom - _graph.Size.Y / 2);
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
        box.AddChild(_status);
        box.AddChild(_page);
        box.AddChild(new HSeparator());
        box.AddChild(Dimmed("What the script checks, answered here:"));
        var answers = new ScrollContainer { CustomMinimumSize = new Vector2(0, 120) };
        answers.AddChild(_answers);
        box.AddChild(answers);
        box.AddChild(new HSeparator());
        box.AddChild(Dimmed("What it did:"));
        box.AddChild(_log);
        BuildAnswers();
        _status.Text = _flags is null ? "These plugins have no flag network, so it can't play." : $"Press Play to run it against {_flags.Name}'s flags.";
        play.Disabled = _flags is null;
        return box;
    }

    /// <summary>A control for each thing the script checks, other than flags.</summary>
    private void BuildAnswers()
    {
        var ops = _script.Nodes.SelectMany(n => n.Ops ?? []).Where(o => o.Command.IsCondition()).ToList();
        foreach (var member in ops.Where(o => o.Command == DialogueCommand.CheckMember).Select(o => o.Target).OfType<RecordId>().Distinct())
            _answers.AddChild(Answer($"{NameOf(member)} is in the party", _world.Members, member));
        foreach (var item in ops.Where(o => o.Command == DialogueCommand.CheckItem).Select(o => o.Target).OfType<RecordId>().Distinct())
            _answers.AddChild(Answer($"The party has {NameOf(item)}", _world.Items, item));
        foreach (var skill in ops.Where(o => o.Command == DialogueCommand.CheckSkill).Select(o => o.Value ?? 0).Distinct().Order())
            _answers.AddChild(Number($"Best {(skill < Skills.Length ? Skills[skill] : $"skill {skill}")}", 0, 30, v => _world.Skills[skill] = v));
        foreach (var stat in ops.Where(o => o.Command is DialogueCommand.CheckBestStat or DialogueCommand.CheckWorstStat or DialogueCommand.CheckSpeakerStat)
            .Select(o => o.Value ?? 0).Distinct().Order())
            _answers.AddChild(Number($"Stat {stat}", 0, 40, v => _world.Stats[stat] = v));
        if (ops.Any(o => o.Command == DialogueCommand.CheckPartySize))
            _answers.AddChild(Number("Party size", 1, 4, v => _world.PartySize = v));
        if (ops.Any(o => o.Command == DialogueCommand.CheckGold) || _script.Nodes.Any(n => n.Ops?.Any(o => o.Command is DialogueCommand.TakeGold) == true))
            _answers.AddChild(Number("Gold", 0, 100_000, v => _world.Gold = (uint)v));
        if (_answers.GetChildCount() == 0)
            _answers.AddChild(Dimmed("Nothing but flags."));
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

    private static HBoxContainer Number(string text, int min, int max, Action<int> set)
    {
        var row = new HBoxContainer();
        var spin = new SpinBox { MinValue = min, MaxValue = max, Value = min, Step = 1 };
        spin.ValueChanged += v => set((int)v);
        set(min);
        row.AddChild(new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        row.AddChild(spin);
        return row;
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
        Focus(page.Line.Node);
        _status.Text = runner.Wait == ScriptWait.Player ? "Waiting for A." : $"At node {page.Line.Node}.";
        _page.AddChild(Dimmed(page.Line.Speaker is { } speaker ? NameOf(speaker) : "No speaker"));
        _page.AddChild(new Label { Text = Display(page.Line.Text), AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(360, 0) });
        if (page.Choices.Count > 0 && runner.Wait != ScriptWait.Player)
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
                _page.AddChild(button);
            }
        }
        else
        {
            var next = new Button { Text = "Continue (A)" };
            next.Pressed += () => Act(runner.Continue);
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

    /// <summary>The party and inventory, as the answers set them; the script's changes are logged.</summary>
    private sealed class EditorWorld(Action<string> log, Func<RecordId, string> name) : IScriptWorld
    {
        public HashSet<RecordId> Members { get; } = [];
        public HashSet<RecordId> Items { get; } = [];
        public Dictionary<int, int> Skills { get; } = [];
        public Dictionary<int, int> Stats { get; } = [];
        public Dictionary<ScriptMemory, int> Memory { get; } = [];
        public int PartySize { get; set; } = 1;
        public uint Gold { get; set; }

        public bool InParty(RecordId entity) => Members.Contains(entity);
        public bool HasItem(RecordId item) => Items.Contains(item);
        public int BestSkill(int skill) => Skills.GetValueOrDefault(skill);
        public int BestStat(int stat) => Stats.GetValueOrDefault(stat);
        public int WorstStat(int stat) => Stats.GetValueOrDefault(stat);
        public int MemberStat(RecordId? member, int stat) => Stats.GetValueOrDefault(stat);
        public bool TryRecall(ScriptMemory memory, out int value) => Memory.TryGetValue(memory, out value);
        public void Remember(ScriptMemory memory, int value) => Memory[memory] = value;
        public void CampHeal() => log("The party is healed");

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
            Members.Add(entity);
            log($"{name(entity)} joins");
            return true;
        }

        public void RemoveMember(RecordId entity)
        {
            Members.Remove(entity);
            log($"{name(entity)} leaves");
        }

        public bool AddExp(RecordId member, int amount)
        {
            log($"{name(member)} gains {amount} experience" + (Members.Contains(member) ? "" : ", but isn't in the party"));
            return Members.Contains(member);
        }
    }
}
