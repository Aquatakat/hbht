using HBHT.Core.Combat;
using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Plugins.Records;
using HBHT.Plugins;

namespace HBHT.Core.Scripts;

/// <summary>What a script needs from where it was started (<c>dialoug_func</c>'s arguments).</summary>
/// <param name="RefPoint">The reference point the script's own are numbered from.</param>
/// <param name="MapObject">The map object that started it, which remembers skill checks and random lines. None when the engine starts it.</param>
public sealed record ScriptStart(int RefPoint = 0, RecordId? MapObject = null);

/// <summary>A line of text a script shows, as a line or a choice.</summary>
/// <param name="Node">The node it comes from, which choosing it leaves.</param>
/// <param name="Speaker">Who says it, for the portrait.</param>
/// <param name="Text">
/// With <see cref="ScriptRunner.PlayerToken"/> replaced by the first party member's name as it stood
/// when the page was made. A page's line has its leading spaces trimmed, as the original trims
/// them; choices keep theirs.
/// </param>
public sealed record ScriptLine(int Node, RecordId? Speaker, string Text);

/// <summary>
/// What a script shows: a line, and choices under it if it's a menu. When the first choice has the
/// same speaker as the line, a conversation shows the line alone first, then the choices under
/// Alaron's portrait (<c>DialougeClass::Setup</c>); choosing is the same.
/// </summary>
public sealed record ScriptPage(ScriptLine Line, IReadOnlyList<ScriptLine> Choices);

public enum ScriptStatus
{
    /// <summary>A page is up: <see cref="ScriptRunner.Continue"/> or <see cref="ScriptRunner.Choose"/>.</summary>
    Showing,
    /// <summary>Waiting on the runtime, which calls <see cref="ScriptRunner.Resume"/>, or on a timer.</summary>
    Waiting,
    Ended,
}

/// <summary>What a script is waiting for.</summary>
public enum ScriptWait
{
    /// <summary>An actor walking or running (<see cref="MoveCue"/>).</summary>
    Move,
    /// <summary>An animation (<see cref="AnimateCue"/>).</summary>
    Animation,
    /// <summary>A number of ticks, which <see cref="ScriptRunner.Step"/> counts down.</summary>
    Timer,
    /// <summary>The scripted camera finishing its move.</summary>
    Camera,
    /// <summary>The player pressing A: a random line, or an animation with no actor, is showing.</summary>
    Player,
}

public enum ScriptEndKind
{
    Ended,
    /// <summary>The speaker's shop opens.</summary>
    Shop,
    /// <summary>A battle starts (<see cref="ScriptEnding.Battle"/>).</summary>
    Battle,
    /// <summary>The speaker's training opens.</summary>
    Train,
    /// <summary>The party was healed (<see cref="DialogueCommand.CampHeal"/>).</summary>
    Camp,
}

/// <summary>How a script ended, for the runtime to carry on from.</summary>
/// <param name="Speaker">
/// The speaker at the end: the keeper of a shop or training. In a conversation with someone who
/// wanders, the original uses them instead (<c>set_shopkeep</c>), and for a battle, their party.
/// </param>
/// <param name="RestHours">Hours to pass: 8 after a camp heal, and 8 more when the script is a camp scene.</param>
/// <param name="PartnerJoined">
/// A member joined during a conversation, so the one talking leaves the map rather than going back
/// to wandering (<c>DialougeAddPartyMember</c>).
/// </param>
/// <param name="Fault">
/// Why the script stopped where the original crashes or reads past its nodes. The runner ends it
/// instead, as <see cref="DialogueCommand.EndDialogue"/> would.
/// </param>
/// <param name="Battle">The battle it starts (<c>encounterDat_func</c>), as the script has it, before where it starts is known.</param>
public sealed record ScriptEnding(ScriptEndKind Kind, RecordId? Speaker, BattleSetup? Battle, int RestHours, bool PartnerJoined,
    string? Fault = null);

/// <summary>
/// Runs a script (Borg13) against the flags and the game state: the original's walk over its nodes
/// (<c>DialogNode.cpp</c>) and the callbacks that carry out its ops (<c>dialogue.cpp</c>), checked
/// against the debug build's machine code.
/// </summary>
/// <remarks>
/// <para>
/// A node is left when the player moves on from it, and leaving runs its effect. Where the walk
/// can go on without the player, as through an If or a line with no text, it does so at once.
/// </para>
/// <para>
/// Where the original crashes (a menu or case with no text, leaving a menu, a thirteenth enemy)
/// or reads past its nodes (a line with no link and no menu to go back to, as node 24 of script
/// 1040 is), the runner ends the script and says why in <see cref="ScriptEnding.Fault"/>.
/// </para>
/// </remarks>
public sealed class ScriptRunner
{
    /// <summary>What a script's text says where the first party member's name goes (<c>HBHT.Rom</c>'s <c>DialogueText.Player</c>).</summary>
    public const string PlayerToken = "{player}";
    /// <summary>What the original marks an empty case value with, so a value of 65535 reads as none.</summary>
    private const int NoCase = 0xFFFF;
    private const int MaxLines = 9;
    private const int MaxEnemies = 12;
    private const int MaxBattlefield = 0x1B;
    /// <summary>Steps through nodes without the player before the runner gives up on a loop.</summary>
    private const int MaxSteps = 10_000;

    private readonly DialogueRecord _script;
    private readonly FlagNetwork _flags;
    private readonly IScriptWorld _world;
    private readonly IScriptHost _host;
    private readonly IRandom _random;
    private readonly Func<RecordId, DialogueRecord?> _scripts;
    private readonly ScriptStart _start;
    /// <summary>The condition ops and the effect ops that change the game's state, which triggers run too.</summary>
    private readonly ScriptOps _ops;

    // The original's dialougeInstance.
    private readonly bool[] _visited;
    private readonly List<ScriptLine> _lines = new(MaxLines);
    private readonly List<RecordId> _enemies = [];
    /// <summary>The script's actors spawned so far, which actor ops can move (<c>ActorAndID::pPlayer</c>).</summary>
    private readonly HashSet<RecordId> _spawned = [];
    private RecordId? _speaker;
    private int _cameraTime = 60;
    private int _collisionByte, _battlefield, _aniByte;
    /// <summary>A case's value, when fewer than two of its links matched (<c>unk10e</c>).</summary>
    private int? _pendingCase;
    /// <summary>The menu or case a line with no link goes back to (<c>unk116</c>).</summary>
    private int? _lastMenu;
    /// <summary>The node whose effect is waiting, which is left when it's done (<c>unk110</c>).</summary>
    private int? _waitingNode;
    private ScriptWait? _wait;
    private int _timer;
    /// <summary>What was showing before the last move, which stays up while the script waits for A.</summary>
    private ScriptPage? _previous;
    private bool _partnerJoined;
    private bool _started;

    /// <param name="scripts">Finds another script, for <see cref="DialogueCommand.RandomLine"/>.</param>
    /// <param name="random">The stream skill rolls and random lines draw from.</param>
    /// <param name="randomSkillChecks">While this flag is set, skill checks roll 0–21 (<see cref="StateFlagsRecord.RandomSkillChecks"/>).</param>
    public ScriptRunner(DialogueRecord script, FlagNetwork flags, IScriptWorld world, IScriptHost host, IRandom random,
        Func<RecordId, DialogueRecord?> scripts, ScriptStart? start = null, RecordId? randomSkillChecks = null)
    {
        _script = script;
        _flags = flags;
        _world = world;
        _host = host;
        _random = random;
        _scripts = scripts;
        _start = start ?? new ScriptStart();
        // The first check of each skill is kept for the rest of the script (dialougeInstance's skill levels).
        _ops = new ScriptOps(flags, world, random, randomSkillChecks, host, new Dictionary<int, int>(), _start.MapObject);
        _visited = new bool[script.Nodes.Count];
        for (var i = 0; i < script.Nodes.Count; i++)
        {
            if (script.Nodes[i].Index != i)
                throw new InvalidDataException($"{script.Id} node {i} says it's node {script.Nodes[i].Index}.");
        }
    }

    public DialogueRecord Script => _script;

    public ScriptStatus Status => Ending is not null ? ScriptStatus.Ended
        : _wait is null or ScriptWait.Player ? ScriptStatus.Showing
        : ScriptStatus.Waiting;

    /// <summary>What the script is waiting for, if anything.</summary>
    public ScriptWait? Wait => Ending is null ? _wait : null;

    /// <summary>What's on screen while <see cref="Status"/> is <see cref="ScriptStatus.Showing"/>.</summary>
    public ScriptPage? Page
    {
        get
        {
            if (Status != ScriptStatus.Showing)
                return null;
            if (_lines.Count == 0)
                return _wait == ScriptWait.Player ? _previous : null;
            var line = _lines[0];
            return new ScriptPage(line with { Text = line.Text.TrimStart(' ') }, _lines.Skip(1).ToList());
        }
    }

    /// <summary>Set once the script has ended.</summary>
    public ScriptEnding? Ending { get; private set; }

    /// <summary>The speaker the latest <see cref="DialogueCommand.SetSpeaker"/> named.</summary>
    public RecordId? Speaker => _speaker;

    /// <summary>Where it starts from, and the map object that started it.</summary>
    public ScriptStart Origin => _start;

    /// <summary>Whether a node has been taken, which hides it as a choice.</summary>
    public bool Visited(int node) => _visited[node];

    /// <summary>Reaches node 0 and walks on until the script shows something, waits or ends (<c>DialogNode_func</c>).</summary>
    public void Start()
    {
        if (_started)
            throw new InvalidOperationException("The script has already started.");
        _started = true;
        Act(Begin);
    }

    /// <summary>The player moves on from the line on screen (A). Menus need <see cref="Choose"/>.</summary>
    public void Continue()
    {
        if (Status != ScriptStatus.Showing)
            throw new InvalidOperationException($"The script isn't showing a page ({Status}).");
        if (Page is { Choices.Count: > 0 } && _waitingNode is null)
            throw new InvalidOperationException("The page has choices; choose one.");
        var node = _waitingNode ?? _lines[0].Node;
        Act(() => Advance(node));
    }

    /// <summary>The player takes one of the page's choices.</summary>
    public void Choose(int choice)
    {
        if (Page is not { } page || choice < 0 || choice >= page.Choices.Count)
            throw new ArgumentOutOfRangeException(nameof(choice), $"The page has {Page?.Choices.Count ?? 0} choices.");
        var node = _waitingNode ?? page.Choices[choice].Node;
        Act(() => Advance(node));
    }

    /// <summary>The runtime has finished what the script is waiting for (<c>DialougeClass::m8004f264</c>).</summary>
    public void Resume()
    {
        if (Status != ScriptStatus.Waiting || _waitingNode is not { } node)
            throw new InvalidOperationException($"The script isn't waiting ({Status}).");
        Act(() => Advance(node));
    }

    /// <summary>Counts down a <see cref="ScriptWait.Timer"/> wait, and moves on when it runs out (<c>FUN_80058b30</c>).</summary>
    public void Step(int ticks)
    {
        if (Wait != ScriptWait.Timer)
            return;
        if (ticks < _timer)
        {
            _timer -= ticks;
            return;
        }
        _timer = 0;
        Resume();
    }

    /// <summary>
    /// Ends the script at once, as <see cref="DialogueCommand.EndDialogue"/> does: the debug build's B.
    /// 1.0 has no skip: the decomp's R + A + Z + B isn't in it (<c>cutScene_control_func</c> at
    /// <c>0x80051378</c> only tests B, which does nothing).
    /// </summary>
    public void Skip()
    {
        if (Ending is null)
            End(ScriptEndKind.Ended);
    }

    /// <summary>
    /// Moves the script on (<c>FUN_800b6cb8</c>): clears the page, then does <paramref name="move"/>.
    /// Afterwards the script has to show something, wait or have ended; if not, the original would
    /// be left with nothing on screen.
    /// </summary>
    private void Act(Action move)
    {
        _previous = Page;
        _lines.Clear();
        _wait = null;
        move();
        if (Ending is null && _wait is null && _lines.Count == 0)
            Fault("Nothing to show: the original would be left with no text on screen.");
    }

    private DialogueNode Node(int index) => _script.Nodes[index];

    private static IReadOnlyList<int> Links(DialogueNode node) => node.Links ?? [];

    private static int? Link(DialogueNode node, int i) => node.Links is { } links && i < links.Count ? links[i] : null;

    /// <summary>Whether a choice can be offered: not yet taken, or the script lets choices be taken again.</summary>
    private bool Takeable(int node) => !_visited[node] || _script.Mode is DialogueMode.Backdrop or DialogueMode.Help;

    private bool Selects(int node, int value) => Node(node).Selects?.Holds(value) == true;

    private bool Stopped => Ending is not null || _waitingNode is not null;

    /// <summary>
    /// Adds a line with the current speaker (<c>FUN_800b6b54</c>), the player's name in place of
    /// <see cref="PlayerToken"/>. The original has room for nine.
    /// </summary>
    private void Push(int node, string text)
    {
        if (_lines.Count < MaxLines)
        {
            if (_world.PlayerName is { } name)
                text = text.Replace(PlayerToken, name, StringComparison.Ordinal);
            _lines.Add(new ScriptLine(node, _speaker, text));
        }
    }

    /// <summary>The start: node 0 is reached, and shown or walked through (<c>DialogNode_func</c>).</summary>
    private void Begin()
    {
        _visited[0] = true;
        Setup(0);
        var start = Node(0);
        switch (start.Kind)
        {
            case DialogueNodeKind.Line:
                if (start.Text.Length == 0)
                    Advance(0);
                else
                    Push(0, start.Text);
                break;
            case DialogueNodeKind.If:
                if (Branch(0) is { } through)
                    Advance(through);
                break;
            case DialogueNodeKind.Case:
                _lastMenu = 0;
                ShowCase(0, setUp: true);
                break;
            case DialogueNodeKind.Menu:
                _lastMenu = 0;
                ShowMenu(0, setUp: true);
                break;
        }
    }

    /// <summary>
    /// Leaves a node and walks on until something shows, the script waits, or it ends
    /// (<c>dialogNode_func_2</c>). The original recurses where this loops.
    /// </summary>
    private void Advance(int node)
    {
        for (var steps = 0; ; steps++)
        {
            if (steps == MaxSteps)
            {
                Fault($"Walked {MaxSteps} nodes without showing anything, from node {node}.");
                return;
            }
            if (Leave(node))
                return;
            _visited[node] = true;

            if (_pendingCase is { } value)
            {
                int? next = null;
                foreach (var link in Links(Node(node)))
                {
                    if (Selects(link, value) && Takeable(link))
                    {
                        next = link;
                        break;
                    }
                }
                if (next is { } chosen)
                {
                    _pendingCase = null;
                    Setup(chosen);
                    if (Node(chosen).Text.Length > 0)
                    {
                        Push(chosen, Node(chosen).Text);
                        return;
                    }
                    node = chosen;
                    continue;
                }
            }

            var current = Node(node);
            switch (current.Kind)
            {
                case DialogueNodeKind.If:
                    if (Branch(node) is { } through)
                    {
                        node = through;
                        continue;
                    }
                    return;
                case DialogueNodeKind.Line:
                    if ((Link(current, 0) ?? _lastMenu) is not { } next)
                    {
                        Fault($"Node {node} has no link and there's no menu to go back to: the original reads past the script's nodes.");
                        return;
                    }
                    switch (Node(next).Kind)
                    {
                        case DialogueNodeKind.Menu:
                            _lastMenu = next;
                            ShowMenu(next, setUp: false);
                            return;
                        case DialogueNodeKind.Case:
                            _lastMenu = next;
                            ShowCase(next, setUp: false);
                            return;
                    }
                    Setup(next);
                    if (Node(next).Text.Length > 0)
                    {
                        Push(next, Node(next).Text);
                        return;
                    }
                    node = next;
                    continue;
                default:
                    Fault($"Node {node} is a {current.Kind}, which can't be left: the original crashes (\"Invalid node to evaluate\").");
                    return;
            }
        }
    }

    /// <summary>
    /// Runs a node's effect as it's left, unless it's the node that was waiting, whose effect has
    /// run (<c>FUN_800b59b8</c>). True when the walk stops here: the effect waits, or the script has ended.
    /// </summary>
    private bool Leave(int node)
    {
        if (_waitingNode is null)
        {
            Effect(node);
            if (_waitingNode is not null)
                return true;
        }
        else
        {
            _waitingNode = null;
        }
        return Ending is not null;
    }

    /// <summary>
    /// An If: exactly 1 takes the first link, anything else the second. The target is reached, and
    /// shown if it has text; otherwise it's returned to walk through.
    /// </summary>
    private int? Branch(int node)
    {
        var next = Link(Node(node), Condition(node) == 1 ? 0 : 1);
        if (next is not { } target)
        {
            Fault($"If node {node} has no link to take.");
            return null;
        }
        Setup(target);
        if (Node(target).Text.Length == 0)
            return target;
        Push(target, Node(target).Text);
        return null;
    }

    /// <summary>Shows a menu's text and the choices not yet taken, each reached as it's added.</summary>
    private void ShowMenu(int node, bool setUp)
    {
        var menu = Node(node);
        if (menu.Text.Length == 0)
        {
            Fault($"Menu node {node} has no text: the original crashes (\"Need text in top node of Tree\").");
            return;
        }
        if (!setUp)
            Setup(node);
        Effect(node);
        if (Stopped)
            return;
        Push(node, menu.Text);
        foreach (var link in Links(menu))
        {
            if (Takeable(link))
            {
                Setup(link);
                Push(link, Node(link).Text);
            }
        }
    }

    /// <summary>
    /// Shows a case's text and evaluates it. With two or more matching links, those not yet taken
    /// are offered; otherwise the value waits for the player to move on, and the match is taken then.
    /// </summary>
    private void ShowCase(int node, bool setUp)
    {
        var @case = Node(node);
        if (@case.Text.Length == 0)
        {
            Fault($"Case node {node} has no text: the original crashes (\"Need text in top node of Case\").");
            return;
        }
        if (!setUp)
            Setup(node);
        Effect(node);
        if (Stopped)
            return;
        Push(node, @case.Text);
        var value = Condition(node);
        // The match count ignores whether a link has been taken.
        if (Links(@case).Count(l => Selects(l, value)) < 2)
        {
            _pendingCase = value == NoCase ? null : value;
            return;
        }
        foreach (var link in Links(@case))
        {
            if (Selects(link, value) && Takeable(link))
            {
                Setup(link);
                Push(link, Node(link).Text);
            }
        }
    }

    /// <summary>
    /// Runs a node's setup ops as it's reached (<c>Dialoug_commands</c>): the speaker from any op,
    /// then the settings, then the cameras and actors, each in order.
    /// </summary>
    private void Setup(int node)
    {
        var ops = Node(node).Ops;
        if (ops is null)
            return;
        foreach (var op in ops)
        {
            if (op.Command == DialogueCommand.SetSpeaker)
                _speaker = op.Target;
        }
        foreach (var op in ops)
        {
            switch (op.Command)
            {
                case DialogueCommand.CameraTime:
                    _cameraTime = op.Value ?? 0;
                    break;
                case DialogueCommand.AddEnemy:
                    if (_enemies.Count == MaxEnemies)
                    {
                        Fault($"Node {node} adds a thirteenth enemy: the original crashes (\"Too many monsters\").");
                        return;
                    }
                    if (op.Target is { } enemy)
                        _enemies.Add(enemy);
                    break;
                case DialogueCommand.SetCollideByte:
                    _collisionByte = (op.Value ?? 0) & 0xFF;
                    break;
                case DialogueCommand.SetBattlefield:
                    _battlefield = (op.Value ?? 0) & 0xFF;
                    break;
                case DialogueCommand.SetAniByte:
                    _aniByte = (op.Value ?? 0) & 0xFF;
                    break;
                case DialogueCommand.ResetVisited:
                    Array.Clear(_visited);
                    break;
            }
        }
        foreach (var op in ops)
        {
            switch (op.Command)
            {
                case DialogueCommand.CameraCutTo or DialogueCommand.CameraOn:
                    _host.Cue(new CameraCue(op.Command, RefPoint(op), null, _speaker, 1));
                    break;
                case DialogueCommand.CameraAngleTo or DialogueCommand.CameraPanTo or DialogueCommand.CameraSineTo:
                    _host.Cue(new CameraCue(op.Command, RefPoint(op), null, _speaker, _cameraTime));
                    break;
                case DialogueCommand.CameraCutToActor or DialogueCommand.CameraOnActor:
                    _host.Cue(new CameraCue(op.Command, null, op.Target, _speaker, 1));
                    break;
                case DialogueCommand.CameraAngleToActor or DialogueCommand.CameraPanToActor or DialogueCommand.CameraSineToActor
                    or DialogueCommand.CameraTrackOn:
                    _host.Cue(new CameraCue(op.Command, null, op.Target, _speaker, _cameraTime));
                    break;
                case DialogueCommand.FaceRefPoint:
                    _host.Cue(new FaceCue(_speaker, RefPoint(op)));
                    break;
                case DialogueCommand.CreateActor:
                    if (IsActor(_speaker))
                        _spawned.Add(_speaker!.Value);
                    _host.Cue(new SpawnCue(_speaker, RefPoint(op)));
                    break;
            }
        }
    }

    private int RefPoint(DialogueOp op) => ((op.Value ?? 0) + _start.RefPoint) & 0xFFFF;

    /// <summary>
    /// Evaluates a node's first condition op, or 0 if it has none (<c>DialogEvalCallback</c>, <see cref="ScriptOps.Condition"/>):
    /// a skill check's first result is kept for the rest of the script, and the map object that started it remembers it.
    /// </summary>
    private int Condition(int node) =>
        Node(node).Ops?.FirstOrDefault(o => o.Command.IsCondition()) is { } op ? _ops.Condition(op.Command, op.Target, op.Value ?? 0, _speaker) : 0;

    /// <summary>
    /// Runs a node's first effect op (<c>dialoug_func_c_check</c> and <c>DialogCallbackC</c>). Ops
    /// that take time mark the node as waiting; ops that end the script end it.
    /// </summary>
    private void Effect(int node)
    {
        if (Node(node).Ops?.FirstOrDefault(o => o.Command.IsEffect()) is not { } op)
            return;
        var value = op.Value ?? 0;
        if (ScriptOps.ChangesState(op.Command))
        {
            // AddExp gives to the speaker. A member joining a conversation leaves the map with the party.
            if (_ops.Effect(op.Command, op.Target, value, _speaker) && op.Command == DialogueCommand.AddMember
                && _script.Mode == DialogueMode.Conversation)
                _partnerJoined = true;
            return;
        }
        switch (op.Command)
        {
            case DialogueCommand.EndDialogue:
                End(ScriptEndKind.Ended);
                break;
            case DialogueCommand.FadeOut:
                _host.Cue(new FadeOutCue());
                break;
            case DialogueCommand.WalkTo or DialogueCommand.RunTo:
                WaitOn(node, ScriptWait.Move);
                _host.Cue(new MoveCue(_speaker, RefPoint(op), op.Command == DialogueCommand.RunTo));
                break;
            case DialogueCommand.PlayAnimation:
                // A conversation looks the actor up from a line it has already cleared (FUN_800591e4), and
                // other scripts only animate actors they've spawned (FUN_80059ae8).
                var actor = _script.Mode != DialogueMode.Conversation && _speaker is { } speaker && _spawned.Contains(speaker) ? speaker : (RecordId?)null;
                WaitOn(node, actor is null ? ScriptWait.Player : ScriptWait.Animation);
                _host.Cue(new AnimateCue(actor, value));
                break;
            case DialogueCommand.PlaySound:
                _host.Cue(new SoundCue(ScriptSound.Table, value));
                break;
            case DialogueCommand.Shop:
                End(ScriptEndKind.Shop);
                break;
            case DialogueCommand.Battle:
                End(ScriptEndKind.Battle);
                break;
            case DialogueCommand.Train:
                End(ScriptEndKind.Train);
                break;
            case DialogueCommand.Unimplemented:
                Fault($"Node {node} runs command 49: the original crashes (\"This command has not been implemented yet!\").");
                break;
            case DialogueCommand.RandomLine:
                WaitOn(node, ScriptWait.Player);
                RandomLine(node, op.Target);
                break;
            case DialogueCommand.ShowItem:
                _host.Cue(new HoldCue(_speaker, op.Target));
                break;
            case DialogueCommand.HideItem:
                _host.Cue(new HoldCue(_speaker, null));
                break;
            case DialogueCommand.CampHeal:
                _world.CampHeal();
                End(ScriptEndKind.Camp);
                break;
            case DialogueCommand.Wait:
                WaitOn(node, ScriptWait.Timer);
                _timer = value;
                break;
            case DialogueCommand.WaitForCamera:
                WaitOn(node, ScriptWait.Camera);
                break;
        }
    }

    private void WaitOn(int node, ScriptWait wait)
    {
        _waitingNode = node;
        _wait = wait;
    }

    private bool IsActor(RecordId? entity) => entity is { } id && _script.Actors?.Contains(id) == true;

    /// <summary>
    /// Shows a line of another script, chosen by a roll of 0–100 that the map object remembers
    /// (<c>FUN_800b6e4c</c>). The roll picks among the script's nodes and blank nodes, and a blank
    /// node shows node 0's text. The node that asked for it waits for the player.
    /// </summary>
    private void RandomLine(int node, RecordId? target)
    {
        if (target is not { } id || _scripts(id) is not { } other)
        {
            Fault($"Node {node} shows a random line of {target?.ToString() ?? "no script"}, which isn't loaded.");
            return;
        }
        var roll = _random.Range(0, 100);
        if (_start.MapObject is { } source)
        {
            var memory = new Memory(source, MemoryKind.RandomLine);
            if (_world.TryRecall(memory, out var recalled))
                roll = recalled;
            else
                _world.Remember(memory, roll);
        }
        var count = other.Nodes.Count + other.BlankNodes;
        if (count == 0)
            return;
        var fraction = roll / 100f;
        var index = fraction == 1f ? count - 1 : (byte)(int)(fraction / (1f / count));
        _lines.Clear();
        Push(node, other.Nodes[index < other.Nodes.Count ? index : 0].Text);
    }

    /// <summary>
    /// Ends the script (<c>DialogCallbackC</c>'s ends, then the mode's end, such as
    /// <c>FUN_80059c70</c>): cut-scenes, backdrops and the help menu set their done flag.
    /// </summary>
    private void End(ScriptEndKind kind, string? fault = null)
    {
        if (Ending is not null)
            return;
        BattleSetup? battle = null;
        if (kind == ScriptEndKind.Battle)
        {
            battle = BattleSetup.Scripted(_enemies, _battlefield > MaxBattlefield ? 0 : _battlefield, _collisionByte, _aniByte,
                _script.Mode != DialogueMode.Cutscene, _script.BattleFlag);
        }
        var rest = (kind == ScriptEndKind.Camp ? 8 : 0) + (_script.Rest ? 8 : 0);
        Ending = new ScriptEnding(kind, _speaker, battle, rest, _partnerJoined, fault);
        _waitingNode = null;
        _wait = null;
        if (_script.DoneFlag is { } done)
            _flags.TrySet(done, true);
    }

    private void Fault(string why) => End(ScriptEndKind.Ended, why);
}
