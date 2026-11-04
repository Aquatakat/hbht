using HBHT.Core.Combat;
using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Plugins.Records;
using HBHT.Plugins;

namespace HBHT.Core.Scripts;

/// <summary>What a script needs from where it was started (<c>dialoug_func</c>'s arguments).</summary>
/// <param name="Marks">
/// The number the script's marks count from: a script object's <see cref="ScriptObject.Marks"/>, or 0 for a talker. The stage's
/// reference point whose <see cref="ReferencePointObject.Mark"/> is this plus the op's is the one meant.
/// </param>
/// <param name="MapObject">The map object that started it, which remembers skill checks and random lines. None when the engine starts it.</param>
public sealed record ScriptStart(int Marks = 0, RecordId? MapObject = null);

/// <summary>A line of text a script shows, as a line or a choice.</summary>
/// <param name="Node">The node it comes from, which choosing it leaves.</param>
/// <param name="Speaker">Who says it, for the portrait: its node's <see cref="ScriptNode.Speaker"/>.</param>
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
    /// <summary>
    /// The player pressing A: a random line is showing, or a cut-scene's animation with no actor has the last
    /// line showing again.
    /// </summary>
    Player,
}

public enum ScriptEndKind
{
    Ended,
    /// <summary>The keeper's shop opens.</summary>
    Shop,
    /// <summary>A battle starts (<see cref="ScriptEnding.Battle"/>).</summary>
    Battle,
    /// <summary>The trainer's training opens.</summary>
    Train,
    /// <summary>The party was healed (<see cref="ScriptOp.PartyCampHeal"/>).</summary>
    Camp,
}

/// <summary>How a script ended, for the runtime to carry on from.</summary>
/// <param name="Speaker">
/// The keeper of a shop or the trainer the op names, or else the speaker of the last line shown. In a conversation with
/// someone who wanders, the original has them keep the shop or train instead (<c>set_shopkeep</c>), and for a battle, their party.
/// </param>
/// <param name="RestHours">Hours to pass: 8 after a camp heal, and 8 more when the script is a camp scene.</param>
/// <param name="PartnerJoined">
/// A member joined during a conversation, so the one talking leaves the map rather than going back
/// to wandering (<c>DialougeAddPartyMember</c>).
/// </param>
/// <param name="Fault">
/// Why the script stopped where the original crashes or reads past its nodes. The runner ends it
/// instead, as <see cref="ScriptOp.ScriptEnd"/> would.
/// </param>
/// <param name="Battle">The battle it starts (<c>encounterDat_func</c>), as the script has it, before where it starts is known.</param>
public sealed record ScriptEnding(ScriptEndKind Kind, RecordId? Speaker, BattleSetup? Battle, int RestHours, bool PartnerJoined,
    string? Fault = null);

/// <summary>
/// Runs a script (Borg13) against the flags and the game state: the original's walk over its nodes
/// (<c>DialogNode.cpp</c>) and the callbacks that carry out its ops (<c>dialogue.cpp</c>), checked
/// against the debug build's machine code. The ops are the plugin's typed ones (ADR 0034).
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
/// <para>
/// Ops a node can't run where they are (a trigger's own, one this build doesn't know, a reserved <c>lua</c>) do nothing;
/// validation reports them.
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

    /// <summary>Steps through nodes without the player before the runner gives up on a loop.</summary>
    private const int MaxSteps = 10_000;

    private readonly ScriptRecord _script;
    private readonly FlagNetwork _flags;
    private readonly IScriptWorld _world;
    private readonly IScriptHost _host;
    private readonly IRandom _random;
    private readonly Func<RecordId, ScriptRecord?> _scripts;
    private readonly ScriptStart _start;
    /// <summary>The condition ops and the effect ops that change the game's state, which triggers run too.</summary>
    private readonly ScriptOps _ops;

    // The original's dialougeInstance.
    private readonly bool[] _visited;
    private readonly List<ScriptLine> _lines = new(MaxLines);
    private readonly List<RecordId> _enemies = [];
    /// <summary>The script's actors spawned so far, which actor ops can move (<c>ActorAndID::pPlayer</c>).</summary>
    private readonly HashSet<RecordId> _spawned = [];
    /// <summary>The speaker of the last line shown.</summary>
    private RecordId? _speaker;
    private int _cameraTime = 60;
    private int _collisionByte, _aniByte;
    /// <summary>The battle's arena, if a <see cref="ScriptOp.BattleArena"/> named one.</summary>
    private RecordId? _battlefield;
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

    /// <param name="scripts">Finds another script, for <see cref="ScriptOp.ScriptRandomLine"/>.</param>
    /// <param name="random">The stream skill rolls and random lines draw from.</param>
    /// <param name="randomSkillChecks">While this flag is set, skill checks roll 0–21 (<see cref="StateFlagsRecord.RandomSkillChecks"/>).</param>
    public ScriptRunner(ScriptRecord script, FlagNetwork flags, IScriptWorld world, IScriptHost host, IRandom random,
        Func<RecordId, ScriptRecord?> scripts, ScriptStart? start = null, RecordId? randomSkillChecks = null)
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

    public ScriptRecord Script => _script;

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

    /// <summary>The speaker of the last line shown: a menu's last choice, a random line's asker.</summary>
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
    /// Ends the script at once, as <see cref="ScriptOp.ScriptEnd"/> does: the debug build's B.
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

    private ScriptNode Node(int index) => _script.Nodes[index];

    private static IReadOnlyList<int> Links(ScriptNode node) => node.Links ?? [];

    private static int? Link(ScriptNode node, int i) => node.Links is { } links && i < links.Count ? links[i] : null;

    /// <summary>Whether a choice can be offered: not yet taken, or the script lets choices be taken again.</summary>
    private bool Takeable(int node) => !_visited[node] || _script.Mode is ScriptMode.Backdrop or ScriptMode.Help;

    private bool Selects(int node, int value) => Node(node).Selects?.Holds(value) == true;

    private bool Stopped => Ending is not null || _waitingNode is not null;

    /// <summary>
    /// Adds a line under its node's speaker (<c>FUN_800b6b54</c>), the player's name in place of
    /// <see cref="PlayerToken"/>. The original has room for nine.
    /// </summary>
    private void Push(int node, string text)
    {
        if (_lines.Count < MaxLines)
        {
            if (_world.PlayerName is { } name)
                text = text.Replace(PlayerToken, name, StringComparison.Ordinal);
            _speaker = Node(node).Speaker;
            _lines.Add(new ScriptLine(node, _speaker, text));
        }
    }

    /// <summary>
    /// Shows a line node's text as the page's line. In a conversation, a line whose effect is
    /// <see cref="ScriptOp.ActorAnimate"/> asks its actor, the speaker the box shows, for the request as it shows,
    /// in place of the talk (<see cref="AnimateCue"/>). That's the port's fix: 1.0 animates no one there (<see cref="Effect"/>).
    /// </summary>
    private void Show(int node)
    {
        Push(node, Node(node).Text);
        if (_script.Mode == ScriptMode.Conversation && Node(node).Effect is ScriptOp.ActorAnimate animate)
            _host.Cue(new AnimateCue(animate.Actor, Requests.Number(animate.Request)));
    }

    /// <summary>The start: node 0 is reached, and shown or walked through (<c>DialogNode_func</c>).</summary>
    private void Begin()
    {
        _visited[0] = true;
        Setup(0);
        var start = Node(0);
        switch (start.Kind)
        {
            case ScriptNodeKind.Line:
                if (start.Text.Length == 0)
                    Advance(0);
                else
                    Show(0);
                break;
            case ScriptNodeKind.If:
                if (Branch(0) is { } through)
                    Advance(through);
                break;
            case ScriptNodeKind.Case:
                _lastMenu = 0;
                ShowCase(0, setUp: true);
                break;
            case ScriptNodeKind.Menu:
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
                        Show(chosen);
                        return;
                    }
                    node = chosen;
                    continue;
                }
            }

            var current = Node(node);
            switch (current.Kind)
            {
                case ScriptNodeKind.If:
                    if (Branch(node) is { } through)
                    {
                        node = through;
                        continue;
                    }
                    return;
                case ScriptNodeKind.Line:
                    if ((Link(current, 0) ?? _lastMenu) is not { } next)
                    {
                        Fault($"Node {node} has no link and there's no menu to go back to: the original reads past the script's nodes.");
                        return;
                    }
                    switch (Node(next).Kind)
                    {
                        case ScriptNodeKind.Menu:
                            _lastMenu = next;
                            ShowMenu(next, setUp: false);
                            return;
                        case ScriptNodeKind.Case:
                            _lastMenu = next;
                            ShowCase(next, setUp: false);
                            return;
                    }
                    Setup(next);
                    if (Node(next).Text.Length > 0)
                    {
                        Show(next);
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
        Show(target);
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
    /// Runs a node's setup ops as it's reached (<c>Dialoug_commands</c>), in order. 1.0 runs the ones that set something up
    /// for later (the camera's time, the battle's enemies and bytes, the choices reset) in a pass before the cameras and
    /// actors, and the importer writes them first.
    /// </summary>
    private void Setup(int node)
    {
        if (Node(node).Setup is not { } ops)
            return;
        foreach (var op in ops)
        {
            switch (op)
            {
                case ScriptOp.CameraTime time:
                    _cameraTime = time.Ticks;
                    break;
                case ScriptOp.BattleAddEnemy enemy:
                    if (_enemies.Count == MaxEnemies)
                    {
                        Fault($"Node {node} adds a thirteenth enemy: the original crashes (\"Too many monsters\").");
                        return;
                    }
                    _enemies.Add(enemy.Entity);
                    break;
                case ScriptOp.BattleCollisionByte collision:
                    _collisionByte = collision.Value & 0xFF;
                    break;
                case ScriptOp.BattleArena arena:
                    _battlefield = arena.Arena;
                    break;
                case ScriptOp.BattleAnimationByte animation:
                    _aniByte = animation.Value & 0xFF;
                    break;
                case ScriptOp.ScriptResetChoices:
                    Array.Clear(_visited);
                    break;
                case ScriptOp.CameraCutTo cut:
                    _host.Cue(new CameraCue(CameraShot.CutTo, Mark(cut.Mark), cut.Actor, 1));
                    break;
                case ScriptOp.CameraLookAt look:
                    _host.Cue(new CameraCue(CameraShot.LookAt, Mark(look.Mark), look.Actor, 1));
                    break;
                case ScriptOp.CameraTurnTo turn:
                    _host.Cue(new CameraCue(CameraShot.TurnTo, Mark(turn.Mark), turn.Actor, turn.Ticks ?? _cameraTime, turn.Steady));
                    break;
                case ScriptOp.CameraMoveTo move:
                    _host.Cue(new CameraCue(CameraShot.MoveTo, Mark(move.Mark), move.Actor, move.Ticks ?? _cameraTime));
                    break;
                case ScriptOp.CameraFollow follow:
                    _host.Cue(new CameraCue(CameraShot.Follow, null, follow.Actor, _cameraTime));
                    break;
                case ScriptOp.ActorFace face:
                    _host.Cue(new FaceCue(face.Actor, Mark(face.Mark)));
                    break;
                case ScriptOp.ActorSpawn spawn:
                    if (IsActor(spawn.Actor))
                        _spawned.Add(spawn.Actor);
                    _host.Cue(new SpawnCue(spawn.Actor, Mark(spawn.Mark)));
                    break;
            }
        }
    }

    /// <summary>A mark of the stage: the script's, counted from the number it was started with.</summary>
    private int Mark(int mark) => (mark + _start.Marks) & 0xFFFF;

    private int? Mark(int? mark) => mark is { } m ? Mark(m) : null;

    /// <summary>
    /// Evaluates a node's condition, or 0 if it has none (<c>DialogEvalCallback</c>, <see cref="ScriptOps.Condition"/>):
    /// a skill check's first result is kept for the rest of the script, and the map object that started it remembers it.
    /// </summary>
    private int Condition(int node) => Node(node).Condition is ScriptOp.Check check ? _ops.Condition(check) : 0;

    /// <summary>
    /// Runs a node's effect (<c>dialoug_func_c_check</c> and <c>DialogCallbackC</c>). Ops that take time mark the node as
    /// waiting, unless they say not to wait; ops that end the script end it.
    /// </summary>
    private void Effect(int node)
    {
        if (Node(node).Effect is not { } op)
            return;
        if (ScriptOps.ChangesState(op))
        {
            // A member joining a conversation leaves the map with the party.
            if (_ops.Effect(op) && op is ScriptOp.PartyAddMember && _script.Mode == ScriptMode.Conversation)
                _partnerJoined = true;
            return;
        }
        switch (op)
        {
            case ScriptOp.ScriptEnd:
                End(ScriptEndKind.Ended);
                break;
            case ScriptOp.ScreenDip:
                _host.Cue(new FadeOutCue());
                break;
            case ScriptOp.ActorWalkTo walk:
                if (walk.Wait != false)
                    WaitOn(node, ScriptWait.Move);
                _host.Cue(new MoveCue(walk.Actor, Mark(walk.Mark), Run: false));
                break;
            case ScriptOp.ActorRunTo run:
                if (run.Wait != false)
                    WaitOn(node, ScriptWait.Move);
                _host.Cue(new MoveCue(run.Actor, Mark(run.Mark), Run: true));
                break;
            case ScriptOp.ActorAnimate animate:
                // In a conversation the original looks the actor up from the page it has just cleared (FUN_800591e4), so
                // no one animates, and it waits for A with the line showing again. The port's fix plays it as the line
                // shows instead (Show), and leaves nothing to wait for. Other scripts only animate actors they've
                // spawned (FUN_80059ae8).
                if (_script.Mode == ScriptMode.Conversation)
                    break;
                var actor = _spawned.Contains(animate.Actor) ? animate.Actor : (RecordId?)null;
                if (animate.Wait != false)
                    WaitOn(node, actor is null ? ScriptWait.Player : ScriptWait.Animation);
                _host.Cue(new AnimateCue(actor, Requests.Number(animate.Request)));
                break;
            case ScriptOp.SoundPlay sound:
                _host.Cue(new SoundCue(ScriptSound.Table, sound.Sound));
                break;
            case ScriptOp.ShopOpen shop:
                End(ScriptEndKind.Shop, keeper: shop.Keeper);
                break;
            case ScriptOp.BattleStart:
                End(ScriptEndKind.Battle);
                break;
            case ScriptOp.TrainingOpen training:
                End(ScriptEndKind.Train, keeper: training.Trainer);
                break;
            case ScriptOp.ScriptRandomLine random:
                WaitOn(node, ScriptWait.Player);
                RandomLine(node, random);
                break;
            case ScriptOp.ActorHold hold:
                _host.Cue(new HoldCue(hold.Actor, hold.Item));
                break;
            case ScriptOp.ActorPutAway putAway:
                _host.Cue(new HoldCue(putAway.Actor, null));
                break;
            case ScriptOp.PartyCampHeal:
                _world.CampHeal();
                End(ScriptEndKind.Camp);
                break;
            case ScriptOp.ScriptWait wait:
                WaitOn(node, ScriptWait.Timer);
                _timer = wait.Ticks;
                break;
            case ScriptOp.CameraWait:
                WaitOn(node, ScriptWait.Camera);
                break;
        }
    }

    private void WaitOn(int node, ScriptWait wait)
    {
        _waitingNode = node;
        _wait = wait;
    }

    private bool IsActor(RecordId entity) => _script.Actors?.Contains(entity) == true;

    /// <summary>
    /// Shows a line of another script, chosen by a roll of 0–100 that the map object remembers
    /// (<c>FUN_800b6e4c</c>). The roll picks among the script's nodes, its first counted
    /// <see cref="ScriptOp.ScriptRandomLine.FirstLineWeight"/> times: 1.0 counts an empty node a script ends with, and shows
    /// node 0's text for it. The line shows under the asking node's speaker, and the node waits for the player.
    /// </summary>
    private void RandomLine(int node, ScriptOp.ScriptRandomLine op)
    {
        if (_scripts(op.From) is not { } other)
        {
            Fault($"Node {node} shows a random line of {op.From}, which isn't loaded.");
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
        if (other.Nodes.Count == 0)
            return;
        var count = other.Nodes.Count + Math.Max(op.FirstLineWeight ?? 1, 1) - 1;
        var fraction = roll / 100f;
        var index = fraction == 1f ? count - 1 : (byte)(int)(fraction / (1f / count));
        _lines.Clear();
        // Pushed as the asking node's line, so under its speaker.
        Push(node, other.Nodes[index < other.Nodes.Count ? index : 0].Text);
    }

    /// <summary>
    /// Ends the script (<c>DialogCallbackC</c>'s ends, then the mode's end, such as
    /// <c>FUN_80059c70</c>): cut-scenes, backdrops and the help menu set their done flag.
    /// </summary>
    /// <param name="keeper">Who keeps the shop or trains, by the op.</param>
    private void End(ScriptEndKind kind, string? fault = null, RecordId? keeper = null)
    {
        if (Ending is not null)
            return;
        BattleSetup? battle = null;
        if (kind == ScriptEndKind.Battle)
        {
            battle = BattleSetup.Scripted(_enemies, _battlefield, _collisionByte, _aniByte,
                _script.Mode != ScriptMode.Cutscene, _script.BattleFlag, _script.BattleRules);
        }
        var rest = (kind == ScriptEndKind.Camp ? 8 : 0) + (_script.Rest ? 8 : 0);
        Ending = new ScriptEnding(kind, keeper ?? _speaker, battle, rest, _partnerJoined, fault);
        _waitingNode = null;
        _wait = null;
        if (_script.DoneFlag is { } done)
            _flags.TrySet(done, true);
    }

    private void Fault(string why) => End(ScriptEndKind.Ended, why);
}
