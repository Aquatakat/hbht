using System.Text.RegularExpressions;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Cameras;

/// <summary>Who in a conversation a line or a shot is about: Alaron, the npc talked to, or no body at all.</summary>
public enum Talker { None, Player, Npc }

/// <summary>The kinds of shot the enhanced conversation camera takes (<see cref="DialogueCamera"/>).</summary>
public enum DialogueShotKind
{
    /// <summary>Both, from across the line of action.</summary>
    TwoShot,

    /// <summary>Over the listener's shoulder at the subject.</summary>
    OverShoulder,

    /// <summary>A medium single of the subject.</summary>
    Single,

    /// <summary>A shouted line: the single's axis, nearer.</summary>
    Punch,

    /// <summary>Both, further and higher.</summary>
    Wide,

    /// <summary>The original's framing (<see cref="FollowCamera.Frame"/>), the fitting's last resort.</summary>
    Original,
}

/// <summary>A shot as the planner chooses it: its kind, and for a single or over the shoulder whom it's at.</summary>
public readonly record struct DialogueShot(DialogueShotKind Kind, Talker Subject = Talker.None)
{
    public static DialogueShot TwoShot { get; } = new(DialogueShotKind.TwoShot);
    public static DialogueShot Wide { get; } = new(DialogueShotKind.Wide);

    /// <summary>Whether <paramref name="talker"/>'s face is in it: both are in a two-shot or a wide, the subject alone in the rest.</summary>
    public bool Shows(Talker talker) =>
        Kind is DialogueShotKind.TwoShot or DialogueShotKind.Wide or DialogueShotKind.Original || talker != Talker.None && Subject == talker;

    public override string ToString() => Subject == Talker.None ? Kind.ToString() : $"{Kind}({Subject})";
}

/// <summary>A page as the planner reads it: the node it shows, who speaks, what, and how many choices go with it.</summary>
public readonly record struct DialoguePage(int Node, Talker Speaker, string Text, int Choices = 0);

/// <summary>
/// Chooses a conversation's shots a page at a time from the script alone, and the conversation's history: no world, so what a script
/// gets can be asked headless (<see cref="Plan"/>). The world's part, where each shot stands and whether it fits, is
/// <see cref="DialogueCamera"/>'s. The rules are the design's (docs/systems/cameras.md, the enhanced conversation camera), in order:
/// the first page is the two-shot; a speaker with no body holds the two-shot; the ops ahead are beats (a battle about to start goes
/// wide, a skill check to Alaron, someone joining to the two-shot); a page of choices looks over Alaron's shoulder at the npc; a
/// shouted line punches in; a long run by one speaker goes wide at its third line and to the listener at its fifth, then turns
/// between the single and over the shoulder; any other line is over the listener's shoulder at the speaker. A shot lasts at least
/// <see cref="DialogueTuning.MinShotTicks"/>: a page sooner keeps it if it shows the speaker, else goes to the two-shot.
/// </summary>
public sealed partial class DialoguePlanner(ScriptRecord script, DialogueTuning? tuning = null)
{
    private readonly DialogueTuning _tuning = tuning ?? DialogueTuning.Default;
    private readonly Dictionary<int, ScriptNode> _nodes = script.Nodes.ToDictionary(n => n.Index);

    /// <summary>The shot chosen last, or null before the first page.</summary>
    public DialogueShot? Current { get; private set; }

    /// <summary>The tick the shot last changed.</summary>
    private long _cutAt;

    private Talker _runSpeaker;
    private int _runIndex;

    /// <summary>Who a script's speaker is in the conversation: Alaron, any other entity with a sheet (the npc's body), or no body.</summary>
    public static Talker TalkerOf(PluginSet plugins, RecordId? speaker) =>
        speaker is not { Type: RecordTypes.Entity } entity ? Talker.None
        : entity == GameState.Alaron ? Talker.Player
        : plugins.TryGet<EntityRecord>(entity)?.Sheet is not null ? Talker.Npc
        : Talker.None;

    /// <summary>The shot for a new page, shown from <paramref name="tick"/>.</summary>
    public DialogueShot Next(DialoguePage page, long tick)
    {
        var first = Current is null;
        var previous = _runSpeaker;
        if (page.Speaker != Talker.None && page.Speaker == previous)
            _runIndex++;
        else
            _runIndex = 1;
        _runSpeaker = page.Choices > 0 ? Talker.None : page.Speaker;

        var wanted = first ? DialogueShot.TwoShot : Choose(page);
        if (!first && Current is { } current && wanted != current && tick - _cutAt < _tuning.MinShotTicks)
            wanted = current.Shows(page.Speaker) ? current : DialogueShot.TwoShot;
        if (wanted != Current)
            _cutAt = tick;
        Current = wanted;
        return wanted;
    }

    private DialogueShot Choose(DialoguePage page)
    {
        var speaker = page.Speaker;
        if (speaker == Talker.None)
            return DialogueShot.TwoShot;
        var beat = Beat(page.Node);
        if (beat is { } b)
            return b;
        if (page.Choices > 0)
            return new DialogueShot(DialogueShotKind.OverShoulder, Talker.Npc);
        if (Shouted(page.Text))
            return new DialogueShot(DialogueShotKind.Punch, speaker);
        var listener = speaker == Talker.Player ? Talker.Npc : Talker.Player;
        var run = _runIndex - 1 + RunAhead(page.Node, speaker);
        if (run >= _tuning.RunLong)
        {
            if (_runIndex == _tuning.RunWideAt)
                return DialogueShot.Wide;
            if (_runIndex == _tuning.RunListenerAt && run > _runIndex)
                return new DialogueShot(DialogueShotKind.Single, listener);
            if (_runIndex > _tuning.RunListenerAt)
                return (_runIndex - _tuning.RunListenerAt - 1) / _tuning.RunCycle % 2 == 0
                    ? new DialogueShot(DialogueShotKind.Single, speaker)
                    : new DialogueShot(DialogueShotKind.OverShoulder, speaker);
        }
        return new DialogueShot(DialogueShotKind.OverShoulder, speaker);
    }

    /// <summary>
    /// What the ops from this page to the next shown are a beat for, read ahead through nodes that show nothing: a battle about to
    /// start goes wide, someone joining to the two-shot, a skill check to Alaron.
    /// </summary>
    private DialogueShot? Beat(int node)
    {
        var ops = OpsAhead(node).ToList();
        if (ops.Any(o => o is ScriptOp.BattleStart))
            return DialogueShot.Wide;
        if (ops.Any(o => o is ScriptOp.PartyAddMember))
            return DialogueShot.TwoShot;
        if (ops.Any(o => o is ScriptOp.CheckSkill))
            return new DialogueShot(DialogueShotKind.Single, Talker.Player);
        return null;
    }

    /// <summary>The page's own effect, then the ops of the nodes it leads to until the next that shows a line, or a branch.</summary>
    private IEnumerable<ScriptOp> OpsAhead(int node)
    {
        if (!_nodes.TryGetValue(node, out var n))
            yield break;
        if (n.Effect is { } effect)
            yield return effect;
        if (n.Kind != ScriptNodeKind.Line)
            yield break;
        var seen = new HashSet<int> { node };
        while (n.Links is [var next] && seen.Add(next) && _nodes.TryGetValue(next, out n))
        {
            if (Shows(n))
                yield break;
            foreach (var op in n.Ops)
                yield return op;
            if (n.Kind != ScriptNodeKind.Line)
                yield break;
        }
    }

    /// <summary>How many lines from this one on, this included, the same speaker says in a row, read ahead along single links.</summary>
    private int RunAhead(int node, Talker speaker)
    {
        if (!_nodes.TryGetValue(node, out var n))
            return 1;
        var count = 1;
        var seen = new HashSet<int> { node };
        while (n.Kind == ScriptNodeKind.Line && n.Links is [var next] && seen.Add(next) && _nodes.TryGetValue(next, out n))
        {
            if (!Shows(n))
                continue;
            if (n.Kind != ScriptNodeKind.Line || _talker(n.Speaker) != speaker)
                break;
            count++;
        }
        return count;
    }

    private static bool Shows(ScriptNode n) => n.Text.Length > 0;

    /// <summary>How the planner reads a node's speaker; set by <see cref="For"/>, else Alaron as the player and any other entity as the npc.</summary>
    private Func<RecordId?, Talker> _talker = s =>
        s is not { Type: RecordTypes.Entity } e ? Talker.None : e == GameState.Alaron ? Talker.Player : Talker.Npc;

    /// <summary>A planner for <paramref name="script"/> that reads its speakers by <paramref name="plugins"/>' sheets.</summary>
    public static DialoguePlanner For(ScriptRecord script, PluginSet plugins, DialogueTuning? tuning = null) =>
        new(script, tuning) { _talker = s => TalkerOf(plugins, s) };

    /// <summary>A shouted line: a word of three capitals or more, or "!!". No shake: a punch-in along the same axis.</summary>
    public static bool Shouted(string text) => text.Contains("!!") || Capitals().IsMatch(text);

    [GeneratedRegex(@"\b[A-Z]{3,}\b")]
    private static partial Regex Capitals();

    /// <summary>
    /// The shots along a run of pages, each shown <paramref name="ticksPerPage"/> after the last: what a script gets, with no world.
    /// </summary>
    public static IReadOnlyList<DialogueShot> Plan(ScriptRecord script, IEnumerable<DialoguePage> pages, PluginSet? plugins = null,
        DialogueTuning? tuning = null, int ticksPerPage = 180)
    {
        var planner = plugins is null ? new DialoguePlanner(script, tuning) : For(script, plugins, tuning);
        var tick = 0L;
        var shots = new List<DialogueShot>();
        foreach (var page in pages)
        {
            shots.Add(planner.Next(page, tick));
            tick += ticksPerPage;
        }
        return shots;
    }
}
