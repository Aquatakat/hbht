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
/// How a shot is taken, beside what it is: for a two-shot or a wide, the turn round the line tried first and a change of pitch; for over
/// the shoulder, changes to the roomy placing's distance back, out to the side and up. <see cref="Plain"/> is the unvaried shot.
/// </summary>
public readonly record struct DialogueStyle(float Turn = 0, float Pitch = 0, float Back = 0, float Side = 0, float Rise = 0)
{
    public static DialogueStyle Plain { get; } = new();
}

/// <summary>How a conversation opened: its first shot, and for a two-shot or a wide the turn round the line it stood at (else 0).</summary>
public readonly record struct DialogueOpening(DialogueShot Shot, float Turn = 0);

/// <summary>
/// Chooses a conversation's shots a page at a time from the script alone, and the conversation's history: no world, so what a script
/// gets can be asked headless (<see cref="Plan"/>). The world's part, where each shot stands and whether it fits, is
/// <see cref="DialogueCamera"/>'s. The rules are the design's (docs/systems/cameras.md, the enhanced conversation camera), in order:
/// the first page is the two-shot; a speaker with no body holds the two-shot; the ops ahead are beats (a battle about to start goes
/// wide, a skill check to Alaron, someone joining to the two-shot); a page of choices looks over Alaron's shoulder at the npc; a
/// shouted line punches in; a long run by one speaker goes wide at its third line and to the listener at its fifth, then turns
/// between the single and over the shoulder; any other line is over the listener's shoulder at the speaker. A shot lasts at least
/// <see cref="DialogueTuning.MinShotTicks"/>: a page sooner keeps it if it shows the speaker, else goes to the two-shot.
/// Given a seed (<see cref="SeedOf"/>), it varies a tad by it (the user's ask of Nov 14, 2026): the opening, how each shot is taken
/// (<see cref="Style"/>), sometimes the single or a held two-shot for an ordinary line, and where a long run's cycle starts. The beats,
/// the shouts, a run's wide and listener, and the minimum shot don't vary. Without a seed, or with <see cref="DialogueTuning.Variety"/>
/// off, it's the unvaried plan. The seed is cosmetic: it never touches the game's own random numbers (<see cref="Rng"/>).
/// </summary>
/// <param name="seed">The cosmetic seed, or null for the unvaried plan.</param>
/// <param name="last">How the conversation before this one opened, which this one doesn't repeat where another opening is there.</param>
public sealed partial class DialoguePlanner(ScriptRecord script, DialogueTuning? tuning = null, ulong? seed = null, DialogueOpening? last = null)
{
    private readonly DialogueTuning _tuning = tuning ?? DialogueTuning.Default;
    private readonly Dictionary<int, ScriptNode> _nodes = script.Nodes.ToDictionary(n => n.Index);

    /// <summary>Whether this conversation varies by its seed.</summary>
    public bool Varied => seed is not null && _tuning.Variety;

    /// <summary>How the conversation before this one opened.</summary>
    public DialogueOpening? Last => last;

    // Two streams from the seed: one drawn once as the conversation starts, one a page at a time.
    private Cosmetic _start = new((seed ?? 0) ^ 0x5EED_0F_0CA3E7A5UL), _pages = new(seed ?? 0);

    /// <summary>How the shot chosen last is taken.</summary>
    public DialogueStyle Style { get; private set; } = DialogueStyle.Plain;

    /// <summary>Whether, where both sides of the line fit equally with about the same room, the other side is taken (<see cref="DialogueCamera.ChooseSide"/>).</summary>
    public bool FlipsSide { get { Begin(); return _flips; } }

    private bool _flips, _begun;

    /// <summary>The draws made once, in a fixed order whatever's asked first: the openings, the side, the run's phase.</summary>
    private void Begin()
    {
        if (_begun)
            return;
        _begun = true;
        _openings = MakeOpenings();
        _flips = Varied && _start.Chance(_tuning.SideFlipChance);
        _phase = Varied && _tuning.RunPhaseVaries && _tuning.RunPattern.Length > 0 ? (int)(_start.Next() * _tuning.RunPattern.Length) : 0;
    }

    /// <summary>
    /// The openings, best first: the one drawn by the seed (not the last conversation's, where another is there), then the rest in the
    /// order drawn. The camera takes the first that fits and doesn't repeat the last conversation's as it's shown.
    /// </summary>
    public IReadOnlyList<DialogueOpening> Openings { get { Begin(); return _openings!; } }

    private List<DialogueOpening>? _openings;

    private int RunPhase { get { Begin(); return _phase; } }

    private int _phase;

    private int _pageCount, _holds;

    private List<DialogueOpening> MakeOpenings()
    {
        var turns = _tuning.AcrossYaws.Take(3).ToList();
        var straight = turns.Count > 0 ? turns[0] : 0;
        if (!Varied)
            return [new DialogueOpening(DialogueShot.TwoShot, straight)];
        var kinds = new List<(DialogueShot Shot, float Weight)> { (DialogueShot.TwoShot, _tuning.OpenTwoShotWeight),
            (new DialogueShot(DialogueShotKind.OverShoulder, Talker.Npc), _tuning.OpenShoulderWeight) };
        if (script.Nodes.Count(n => n.Text.Length > 0) >= _tuning.OpenWideLines)
            kinds.Add((DialogueShot.Wide, _tuning.OpenWideWeight));
        var order = new List<DialogueOpening>();
        while (kinds.Count > 0)
        {
            var total = kinds.Sum(k => MathF.Max(k.Weight, 0));
            var pick = 0;
            if (total > 0)
            {
                var draw = _start.Next() * total;
                while (pick < kinds.Count - 1 && (draw -= MathF.Max(kinds[pick].Weight, 0)) >= 0)
                    pick++;
            }
            var shot = kinds[pick].Shot;
            if (kinds[pick].Weight > 0 || order.Count == 0)
            {
                if (shot.Kind is DialogueShotKind.TwoShot or DialogueShotKind.Wide)
                {
                    var first = DrawTurn(ref _start);
                    order.Add(new DialogueOpening(shot, first));
                    order.AddRange(turns.Where(t => t != first).Select(t => new DialogueOpening(shot, t)));
                }
                else
                    order.Add(new DialogueOpening(shot));
            }
            kinds.RemoveAt(pick);
        }
        // The drawn opening first, unless it's the last conversation's and another is there.
        if (last is { } l && order.Count > 1 && order[0] == l)
        {
            var other = order.FindIndex(o => o != l);
            (order[0], order[other]) = (order[other], order[0]);
        }
        return order;
    }

    /// <summary>The turn round the line a two-shot or a wide tries first: straight across, or by <see cref="DialogueTuning.TurnChance"/> 20° either way.</summary>
    private float DrawTurn(ref Cosmetic random)
    {
        var turns = _tuning.AcrossYaws;
        if (turns.Length == 0)
            return 0;
        if (turns.Length < 3 || !random.Chance(_tuning.TurnChance))
            return turns[0];
        return random.Chance(0.5f) ? turns[1] : turns[2];
    }

    /// <summary>A style for a new shot drawn from the page stream, or the plain one.</summary>
    private DialogueStyle DrawStyle(DialogueShot shot, float? turn = null)
    {
        if (!Varied)
            return DialogueStyle.Plain with { Turn = _tuning.AcrossYaws.FirstOrDefault() };
        float Spread(float most) => most == 0 ? 0 : (float)(_pages.Next() * 2 - 1) * most;
        return shot.Kind switch
        {
            DialogueShotKind.TwoShot or DialogueShotKind.Wide => new DialogueStyle(turn ?? DrawTurn(ref _pages), Spread(_tuning.PitchJitter)),
            DialogueShotKind.OverShoulder => new DialogueStyle(_tuning.AcrossYaws.FirstOrDefault(), 0, Spread(_tuning.ShoulderBackJitter),
                Spread(_tuning.ShoulderSideJitter), Spread(_tuning.ShoulderRiseJitter)),
            _ => DialogueStyle.Plain with { Turn = _tuning.AcrossYaws.FirstOrDefault() },
        };
    }

    /// <summary>
    /// Opens with <paramref name="opening"/> in place of the one planned, keeping the style's draw but its turn: the camera's choice
    /// where the planned opening didn't fit or was shown as the last conversation's was.
    /// </summary>
    public void Open(DialogueOpening opening)
    {
        Current = opening.Shot;
        Style = Style with { Turn = opening.Turn };
    }

    /// <summary>
    /// A cosmetic seed for a conversation: the npc's map object, its script and how many conversations this session has had, so two talks
    /// with the same npc differ. FNV-1a, stable across runs.
    /// </summary>
    public static ulong SeedOf(RecordId who, RecordId script, int conversation)
    {
        var hash = 0xCBF29CE484222325UL;
        foreach (var c in $"{who}|{script}|{conversation}")
            hash = (hash ^ c) * 0x100000001B3UL;
        return hash;
    }

    /// <summary>SplitMix64: the planner's own numbers, apart from the game's.</summary>
    private struct Cosmetic(ulong state)
    {
        private ulong _state = state;

        public double Next()
        {
            var z = _state += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return ((z ^ (z >> 31)) >> 11) * (1.0 / (1UL << 53));
        }

        public bool Chance(float p) => p > 0 && Next() < p;
    }

    /// <summary>The kinds of shot this script will want (<see cref="WantsOf"/>), which the side is chosen by.</summary>
    public IReadOnlyCollection<DialogueShotKind> Wants => _wants ??= WantsOf(script, _tuning);

    private IReadOnlyCollection<DialogueShotKind>? _wants;

    /// <summary>
    /// The kinds of shot a script will want, read from it alone: the two-shot and over the shoulder always; the wide where it has a
    /// long run by one speaker or a battle ahead; the single where it has a shouted line or a long run.
    /// </summary>
    public static IReadOnlyCollection<DialogueShotKind> WantsOf(ScriptRecord script, DialogueTuning? tuning = null)
    {
        var t = tuning ?? DialogueTuning.Default;
        var planner = new DialoguePlanner(script, t);
        var lines = script.Nodes.Where(n => n.Text.Length > 0).ToList();
        var longRun = lines.Any(n => TalkerOf(n.Speaker) != Talker.None && planner.RunAhead(n.Index, TalkerOf(n.Speaker)) >= t.RunLong);
        var battle = script.Nodes.Any(n => n.Ops.Any(o => o is ScriptOp.BattleStart));
        var shouts = lines.Any(n => TalkerOf(n.Speaker) != Talker.None && Shouted(n.Text));
        var wants = new List<DialogueShotKind> { DialogueShotKind.TwoShot, DialogueShotKind.OverShoulder };
        if (longRun || battle)
            wants.Add(DialogueShotKind.Wide);
        if (longRun || shouts)
            wants.Add(DialogueShotKind.Single);
        return wants;
    }

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

        _pageCount++;
        if (first)
        {
            var opening = Openings[0];
            Current = opening.Shot;
            Style = DrawStyle(opening.Shot, opening.Turn);
            _cutAt = tick;
            return opening.Shot;
        }
        var held = false;
        var wanted = Choose(page, ref held);
        _holds = held ? _holds + 1 : 0;
        if (Current is { } current && wanted != current && tick - _cutAt < _tuning.MinShotTicks)
            wanted = current.Shows(page.Speaker) ? current : DialogueShot.TwoShot;
        if (wanted != Current)
        {
            _cutAt = tick;
            Style = DrawStyle(wanted);
        }
        Current = wanted;
        return wanted;
    }

    /// <summary>Whether the page last planned was the conversation's first.</summary>
    public bool Opening => _pageCount == 1;

    private DialogueShot Choose(DialoguePage page, ref bool held)
    {
        var speaker = page.Speaker;
        if (speaker == Talker.None)
            return DialogueShot.TwoShot;
        var beat = Beat(page.Node);
        if (beat is { } b)
            return b;
        if (page.Choices > 0)
            return new DialogueShot(speaker == Talker.Npc && Shouted(page.Text) ? DialogueShotKind.Punch : DialogueShotKind.OverShoulder, Talker.Npc);
        if (Shouted(page.Text))
            return new DialogueShot(DialogueShotKind.Punch, speaker);
        var listener = speaker == Talker.Player ? Talker.Npc : Talker.Player;
        var run = _runIndex - 1 + RunAhead(page.Node, speaker);
        if (run >= _tuning.RunLong)
        {
            if (_runIndex == _tuning.RunWideAt)
                return DialogueShot.Wide;
            var last = _runIndex >= run;
            if (_runIndex == _tuning.RunListenerAt && !last)
                return new DialogueShot(DialogueShotKind.Single, listener);
            if (_runIndex > _tuning.RunListenerAt && _tuning.RunPattern.Length > 0)
                return char.ToUpperInvariant(_tuning.RunPattern[(_runIndex - _tuning.RunListenerAt - 1 + RunPhase) % _tuning.RunPattern.Length]) switch
                {
                    'S' => new DialogueShot(DialogueShotKind.Single, speaker),
                    'W' => DialogueShot.Wide,
                    'L' when !last => new DialogueShot(DialogueShotKind.Single, listener),
                    _ => new DialogueShot(DialogueShotKind.OverShoulder, speaker),
                };
        }
        if (Varied)
        {
            // An ordinary line, varied a tad: a short one may keep a shot showing both (once in a row), or take the clean single.
            var shortLine = page.Text.Length <= _tuning.ShortLine;
            if (shortLine && _holds == 0 && Current is { Kind: DialogueShotKind.TwoShot or DialogueShotKind.Wide } both
                && _pages.Chance(_tuning.HoldChance))
            {
                held = true;
                return both;
            }
            if (_pages.Chance(shortLine ? _tuning.ShortSingleChance : _tuning.SingleChance))
                return new DialogueShot(DialogueShotKind.Single, speaker);
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
        DialogueTuning? tuning = null, int ticksPerPage = 180, ulong? seed = null, DialogueOpening? last = null) =>
        PlanStyled(script, pages, tuning, ticksPerPage, seed, last).Select(s => s.Shot).ToList();

    /// <summary>As <see cref="Plan"/>, with how each shot is taken.</summary>
    public static IReadOnlyList<(DialogueShot Shot, DialogueStyle Style)> PlanStyled(ScriptRecord script, IEnumerable<DialoguePage> pages,
        DialogueTuning? tuning = null, int ticksPerPage = 180, ulong? seed = null, DialogueOpening? last = null)
    {
        var planner = new DialoguePlanner(script, tuning, seed, last);
        var tick = 0L;
        var shots = new List<(DialogueShot, DialogueStyle)>();
        foreach (var page in pages)
        {
            shots.Add((planner.Next(page, tick), planner.Style));
            tick += ticksPerPage;
        }
        return shots;
    }
}
