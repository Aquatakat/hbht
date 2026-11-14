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

    /// <summary>
    /// Who a script's speaker is to the camera: Alaron, or any other entity, which is the npc talked to (a shared script's generic
    /// Goblin or Commoner, or a dialogue entity such as Baldric's, is the npc's body speaking), or no one, the narrator.
    /// </summary>
    public static Talker TalkerOf(RecordId? speaker) =>
        speaker is not { Type: RecordTypes.Entity } entity ? Talker.None : entity == GameState.Alaron ? Talker.Player : Talker.Npc;

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
            if (n.Kind != ScriptNodeKind.Line || TalkerOf(n.Speaker) != speaker)
                break;
            count++;
        }
        return count;
    }

    private static bool Shows(ScriptNode n) => n.Text.Length > 0;

    /// <summary>A shouted line: a word of three capitals or more, or "!!". No shake: a punch-in along the same axis.</summary>
    public static bool Shouted(string text) => text.Contains("!!") || Capitals().IsMatch(text);

    [GeneratedRegex(@"\b[A-Z]{3,}\b")]
    private static partial Regex Capitals();

    /// <summary>
    /// The pages along a script's first path, as a new game's state runs it (the first choice taken at every menu), to at most
    /// <paramref name="most"/>: what <see cref="Plan"/> reads to ask what a real script gets.
    /// </summary>
    /// <param name="choices">The choice taken at each menu in turn, past which the first is taken.</param>
    public static List<DialoguePage> FirstPath(GameState game, ScriptRecord script, int most = 80, IReadOnlyList<int>? choices = null) =>
        Walk(game, script, most, choices).Pages;

    /// <summary>The pages along a path through a script as <see cref="FirstPath"/> walks it, how many menus it met, and whether it ended in a battle.</summary>
    public static (List<DialoguePage> Pages, int Menus, bool Battle) Walk(GameState game, ScriptRecord script, int most = 80, IReadOnlyList<int>? choices = null)
    {
        var runner = game.RunScript(script, new NoHost());
        var menus = 0;
        runner.Start();
        var pages = new List<DialoguePage>();
        for (var guard = 0; pages.Count < most && guard < most * 4 && runner.Status != Scripts.ScriptStatus.Ended; guard++)
        {
            if (runner.Wait == Scripts.ScriptWait.Timer)
            {
                runner.Step(1);
                continue;
            }
            if (runner.Page is not { } page)
                break;
            pages.Add(new DialoguePage(page.Line.Node, TalkerOf(page.Line.Speaker), page.Line.Text, page.Choices.Count));
            if (page.Choices.Count > 0)
            {
                var choice = choices is not null && menus < choices.Count ? choices[menus] : 0;
                menus++;
                runner.Choose(Math.Min(choice, page.Choices.Count - 1));
            }
            else
                runner.Continue();
        }
        return (pages, menus, runner.Ending?.Battle is not null);
    }

    private sealed class NoHost : Scripts.IScriptHost
    {
        public void Cue(Scripts.ScriptCue cue) { }
    }

    /// <summary>
    /// The shots along a run of pages, each shown <paramref name="ticksPerPage"/> after the last: what a script gets, with no world.
    /// </summary>
    public static IReadOnlyList<DialogueShot> Plan(ScriptRecord script, IEnumerable<DialoguePage> pages,
        DialogueTuning? tuning = null, int ticksPerPage = 180)
    {
        var planner = new DialoguePlanner(script, tuning);
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
