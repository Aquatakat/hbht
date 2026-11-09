
using HBHT.Core.Events;
using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// One way through a script, from its start to its end: the choices the player takes, what the state must be for the script to go
/// that way, and what it does, in order, with what the events it fires do after it (its done flag, a cut-scene's
/// <c>script.ended</c> triggers and the cinematics they play).
/// </summary>
/// <param name="Choices">The nodes of the choices taken, in order: what the pilot picks on each page with choices.</param>
/// <param name="Requires">What the state must be as the script starts, for its conditions to go this way.</param>
/// <param name="Effects">What it does, in order.</param>
/// <param name="End">How it ended.</param>
/// <param name="Keeper">The keeper of the shop or the trainer it ends in, by the op.</param>
/// <param name="Enemies">The enemies of the battle it ends in, in order, by its <c>battle.addEnemy</c>s.</param>
/// <param name="Fault">Why it stopped where the original would crash, if it did.</param>
public sealed record ScriptWay(
    IReadOnlyList<int> Choices,
    IReadOnlyList<Literal> Requires,
    IReadOnlyList<Effect> Effects,
    ScriptEndKind End,
    RecordId? Keeper,
    IReadOnlyList<RecordId> Enemies,
    string? Fault)
{
    /// <summary>Other choices that go the same way, with the same requirements and effects.</summary>
    public IReadOnlyList<IReadOnlyList<int>> OtherChoices { get; init; } = [];
}

/// <summary>Every way through a script that differs in what it needs or does, and whether the search was cut short.</summary>
public sealed record ScriptWaysResult(RecordId Script, IReadOnlyList<ScriptWay> Ways, bool Truncated, int Runs);

/// <summary>
/// Walks every way through a script with the real <see cref="ScriptRunner"/>, which a probe (<see cref="IScriptProbe"/>) answers
/// in the game's place: each condition the state could answer more than one way is a branch, as is each page of choices. A way is
/// run again from the start with the branches it took, so the runner's own walk decides everything else (menus going back, cases,
/// choices taken). Pages seen before with the same knowledge, the same choices taken and the same page aren't walked again.
/// </summary>
public sealed class ScriptWays
{
    /// <summary>How many runs a script is given before its search is cut short.</summary>
    public const int MaxRuns = 50_000;
    private const int MaxPages = 2_000;

    private readonly PluginSet _plugins;
    private readonly FlagModel _flags;
    private readonly Triggers _triggers;
    private readonly FlagNetwork _scratch;

    public ScriptWays(PluginSet plugins, FlagModel flags, Triggers triggers)
    {
        _plugins = plugins;
        _flags = flags;
        _triggers = triggers;
        _scratch = FlagNetwork.Load(plugins);
    }

    /// <summary>Every way through a script, merged where ways differ only in a condition that changed nothing.</summary>
    public ScriptWaysResult Explore(ScriptRecord script)
    {
        var stack = new Stack<List<int>>();
        stack.Push([]);
        var seen = new HashSet<(ulong, ulong)>();
        var ways = new Dictionary<string, ScriptWay>();
        var runs = 0;
        var truncated = false;
        while (stack.Count > 0)
        {
            if (runs == MaxRuns)
            {
                truncated = true;
                break;
            }
            runs++;
            var prefix = stack.Pop();
            var run = new Run(this, script, prefix, seen);
            var way = run.Play();
            foreach (var (depth, options) in run.NewPoints)
            {
                for (var k = options - 1; k >= 1; k--)
                    stack.Push([.. run.Decisions.Take(depth), k]);
            }
            if (way is not null)
            {
                var key = Key(way);
                if (!ways.ContainsKey(key))
                    ways[key] = way;
            }
        }
        return new ScriptWaysResult(script.Id, Merge([.. ways.Values]), truncated, runs);
    }

    /// <summary>The events a game's start fires, as a way of their own (a new game's opening).</summary>
    public IReadOnlyList<ScriptWay> Event(string name, RecordId? subject)
    {
        var stack = new Stack<List<int>>();
        stack.Push([]);
        var ways = new Dictionary<string, ScriptWay>();
        while (stack.Count > 0)
        {
            var prefix = stack.Pop();
            var run = new Run(this, null, prefix, []);
            run.Fire(name, subject);
            var way = run.Finish(ScriptEndKind.Ended, null, [], null);
            foreach (var (depth, options) in run.NewPoints)
            {
                for (var k = options - 1; k >= 1; k--)
                    stack.Push([.. run.Decisions.Take(depth), k]);
            }
            ways.TryAdd(Key(way), way);
        }
        return [.. ways.Values];
    }

    private static string Key(ScriptWay way) =>
        $"{way.End}|{way.Keeper}|{string.Join(",", way.Enemies)}|{string.Join(";", way.Requires.Select(r => r.Describe()).Order())}" +
        $"|{string.Join(";", way.Effects.Select(e => e.Describe()))}";

    /// <summary>
    /// Ways that do the same and whose requirements differ only in one flag, member or item, set in one and clear in the other, are
    /// one way without it; and a way whose requirements hold wherever another's with the same effects do is dropped.
    /// </summary>
    private static List<ScriptWay> Merge(List<ScriptWay> ways)
    {
        var merged = new List<ScriptWay>();
        foreach (var group in ways.GroupBy(w => $"{w.End}|{w.Keeper}|{string.Join(",", w.Enemies)}|{string.Join(";", w.Effects.Select(e => e.Describe()))}"))
        {
            // By their requirements, written out in order; a worklist joins each with its partner across one literal.
            var sets = new Dictionary<string, (SortedDictionary<string, Literal> Requires, ScriptWay Way, List<IReadOnlyList<int>> Others)>();
            var work = new Queue<string>();
            foreach (var way in group)
            {
                var requires = new SortedDictionary<string, Literal>(way.Requires.ToDictionary(r => r.Describe()), StringComparer.Ordinal);
                var key = string.Join(";", requires.Keys);
                if (sets.TryGetValue(key, out var same))
                {
                    same.Others.Add(way.Choices);
                }
                else
                {
                    sets[key] = (requires, way, []);
                    work.Enqueue(key);
                }
            }
            while (work.TryDequeue(out var key))
            {
                if (!sets.TryGetValue(key, out var entry))
                    continue;
                foreach (var (text, literal) in entry.Requires)
                {
                    if (Flip(literal) is not { } flipped)
                        continue;
                    var flippedText = flipped.Describe();
                    var partnerKey = string.Join(";", entry.Requires.Keys.Select(k => k == text ? flippedText : k).Order(StringComparer.Ordinal));
                    if (!sets.TryGetValue(partnerKey, out var partner))
                        continue;
                    sets.Remove(key);
                    sets.Remove(partnerKey);
                    var requires = new SortedDictionary<string, Literal>(entry.Requires, StringComparer.Ordinal);
                    requires.Remove(text);
                    var joinedKey = string.Join(";", requires.Keys);
                    var others = entry.Others.Concat(partner.Others).Append(partner.Way.Choices).ToList();
                    if (sets.TryGetValue(joinedKey, out var existing))
                    {
                        existing.Others.AddRange(others.Append(entry.Way.Choices));
                    }
                    else
                    {
                        sets[joinedKey] = (requires, entry.Way, others);
                        work.Enqueue(joinedKey);
                    }
                    break;
                }
            }
            var list = sets.Values.ToList();
            // Subsumed: another's requirements are a subset.
            if (list.Count < 1000)
            {
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    var j = list.FindIndex(s => s.Requires.Count < list[i].Requires.Count && s.Requires.Keys.All(list[i].Requires.ContainsKey));
                    if (j >= 0)
                    {
                        list[j].Others.Add(list[i].Way.Choices);
                        list.RemoveAt(i);
                    }
                }
            }
            foreach (var (requires, way, others) in list)
            {
                var distinct = others.Where(o => !o.SequenceEqual(way.Choices)).DistinctBy(o => string.Join(",", o)).Take(64).ToList();
                merged.Add(way with { Requires = [.. requires.Values], OtherChoices = distinct });
            }
        }
        return merged;
    }

    private static Literal? Flip(Literal literal) => literal switch
    {
        FlagIs f => f with { Set = !f.Set },
        MemberIs m => m with { In = !m.In },
        ItemIs i => i with { Held = !i.Held },
        _ => null,
    };

    private enum AtomType { Flag, Member, Item, PartySize, Gold, Skill, BestStat, WorstStat, MemberStat, Unknown }

    private readonly record struct Atom(AtomType Type, RecordId? A = null, RecordId? B = null)
    {
        public bool IsBool => Type is AtomType.Flag or AtomType.Member or AtomType.Item;

        public (int Lo, int Hi) Domain => Type switch
        {
            AtomType.Flag or AtomType.Member or AtomType.Item => (0, 1),
            AtomType.PartySize => (1, Characters.Party.MaxSize),
            // A skill check is 0 to 21 (Party.SkillCheck).
            AtomType.Skill => (0, 21),
            _ => (0, 0xFFFF),
        };

        public static Atom Of(ScriptOp.Check check) => check switch
        {
            ScriptOp.CheckFlag f => new(AtomType.Flag, f.Flag),
            ScriptOp.CheckMember m => new(AtomType.Member, m.Entity),
            ScriptOp.CheckItem i => new(AtomType.Item, i.Item),
            ScriptOp.CheckPartySize => new(AtomType.PartySize),
            ScriptOp.CheckGold => new(AtomType.Gold),
            ScriptOp.CheckSkill s => new(AtomType.Skill, s.Skill),
            ScriptOp.CheckBestStat b => new(AtomType.BestStat, b.Stat),
            ScriptOp.CheckWorstStat w => new(AtomType.WorstStat, w.Stat),
            ScriptOp.CheckStat s => new(AtomType.MemberStat, s.Member, s.Stat),
            _ => new(AtomType.Unknown),
        };
    }

    /// <summary>One run of a script, deciding its branches by a prefix and taking the first option past it.</summary>
    private sealed class Run(ScriptWays owner, ScriptRecord? script, List<int> prefix, HashSet<(ulong, ulong)> seen) : IScriptProbe, IScriptWorld, IScriptHost
    {
        public List<int> Decisions { get; } = [];
        public List<(int Depth, int Options)> NewPoints { get; } = [];

        private readonly Dictionary<Atom, (int Lo, int Hi)> _pre = [];
        private readonly Dictionary<Atom, int> _written = [];
        private readonly HashSet<Atom> _implied = [];
        private readonly List<Effect> _effects = [];
        private readonly List<int> _choices = [];
        private int _partyDelta, _goldDelta;
        private bool _tookOver;

        public ScriptWay? Play()
        {
            var s = script!;
            var runner = new ScriptRunner(s, owner._scratch, this, this, new Lcg(), id => owner._plugins.TryGet<ScriptRecord>(id),
                new ScriptStart(), null, this);
            runner.Start();
            for (var pages = 0; runner.Status != ScriptStatus.Ended; pages++)
            {
                if (pages == MaxPages)
                    return null;
                if (runner.Status == ScriptStatus.Waiting)
                {
                    if (runner.Wait == ScriptWait.Timer)
                        runner.Step(int.MaxValue);
                    else
                        runner.Resume();
                    continue;
                }
                var page = runner.Page;
                var choosing = runner.Wait != ScriptWait.Player && page is { Choices.Count: > 0 };
                // Pages before the prefix runs out were walked by the run that found the branch.
                if (Decisions.Count >= prefix.Count && !seen.Add(Fingerprint(runner, page, choosing)))
                    return null;
                if (!choosing)
                {
                    runner.Continue();
                    continue;
                }
                var choice = Decide(page!.Choices.Count);
                _choices.Add(page.Choices[choice].Node);
                runner.Choose(choice);
            }
            var ending = runner.Ending!;
            if (s.DoneFlag is { } done)
                Write(done, true);
            if (s.Mode == ScriptMode.Cutscene)
                Fire(GameEvents.ScriptEnded, s.Id);
            var enemies = ending.Battle?.Enemies.Select(e => e.Entity).ToList() ?? [];
            var keeper = ending.Kind is ScriptEndKind.Shop or ScriptEndKind.Train ? ending.Speaker : null;
            return Finish(ending.Kind, keeper, enemies, ending.Fault);
        }

        public ScriptWay Finish(ScriptEndKind end, RecordId? keeper, IReadOnlyList<RecordId> enemies, string? fault)
        {
            var requires = new List<Literal>();
            foreach (var (atom, (lo, hi)) in _pre)
            {
                if ((lo, hi) == atom.Domain || _implied.Contains(atom))
                    continue;
                requires.Add(atom.Type switch
                {
                    AtomType.Flag => new FlagIs(atom.A!.Value, lo == 1),
                    AtomType.Member => new MemberIs(atom.A!.Value, lo == 1),
                    AtomType.Item => new ItemIs(atom.A!.Value, lo == 1),
                    AtomType.PartySize => new PartySizeIn(lo, hi),
                    AtomType.Gold => new CodeGate($"gold {Range(lo, hi)}"),
                    AtomType.Skill => new CodeGate($"a {Name(atom.A)} check of {Range(lo, hi)}"),
                    AtomType.BestStat => new CodeGate($"the best {Name(atom.A)} {Range(lo, hi)}"),
                    AtomType.WorstStat => new CodeGate($"the worst {Name(atom.A)} {Range(lo, hi)}"),
                    AtomType.MemberStat => new CodeGate($"{atom.A}'s {Name(atom.B)} {Range(lo, hi)}"),
                    _ => new CodeGate($"an unknown check {Range(lo, hi)}"),
                });
            }
            return new ScriptWay([.. _choices], requires, [.. _effects], end, keeper, enemies, fault);
        }

        private static string Range(int lo, int hi) => hi >= 0xFFFF ? $"{lo} or more" : lo == hi ? $"{lo}" : lo == 0 ? $"{hi} or less" : $"{lo}–{hi}";

        private string Name(RecordId? id) => id is { } r ? owner._plugins.PeekString(r, "name") ?? r.ToString() : "?";

        /// <summary>Fires an event's triggers as the session does, their conditions branching as a script's do.</summary>
        public void Fire(string name, RecordId? subject)
        {
            foreach (var trigger in owner._triggers.On(name))
            {
                if (_tookOver)
                    return;
                if (trigger.Subject is { } about && about != subject)
                    continue;
                if (trigger.If is { } conditions && !conditions.All(c => c is ScriptOp.Check check && TriggerHolds(check, subject)))
                    continue;
                foreach (var op in trigger.Do)
                {
                    switch (op)
                    {
                        case var _ when ScriptOps.ChangesState(op):
                            Effect(-1, op);
                            break;
                        case ScriptOp.CinematicPlay play:
                            _tookOver = true;
                            if (owner._plugins.TryGet<CinematicRecord>(play.Cinematic) is { } cinematic)
                            {
                                Write(cinematic.DoneFlag, true);
                                _tookOver = false;
                                Fire(GameEvents.CinematicEnded, cinematic.Id);
                                _tookOver = true;
                            }
                            return;
                        case ScriptOp.StartScript start:
                            _effects.Add(new StartScript(start.From));
                            _tookOver = true;
                            return;
                        case ScriptOp.CreditsRoll credits:
                            if (credits.Ending)
                                _effects.Add(new RollCredits());
                            _tookOver = true;
                            return;
                        case ScriptOp.GameOver:
                            _effects.Add(new EndGame());
                            _tookOver = true;
                            return;
                    }
                }
            }
        }

        private bool TriggerHolds(ScriptOp.Check check, RecordId? subject)
        {
            if (check is ScriptOp.CheckSubjectKind kind)
                return subject is { } id && owner._plugins.TryGet<ItemRecord>(id) is { } item && item.Kind == kind.Kind;
            var range = check.Range ?? new ScriptRange(1, 1);
            var value = Answer(check, Breaks([range]));
            return range.Holds(value);
        }

        // IScriptProbe.

        public int Condition(int node, ScriptOp.Check check)
        {
            var at = script!.Nodes[node];
            IEnumerable<ScriptRange> ranges = at.Kind == ScriptNodeKind.Case
                ? (at.Links ?? []).Select(l => script.Nodes[l].Selects).OfType<ScriptRange>()
                : [new ScriptRange(1, 1)];
            return Answer(check, Breaks(ranges));
        }

        public bool Effect(int node, ScriptOp op)
        {
            switch (op)
            {
                case ScriptOp.FlagSet set:
                    Write(set.Flag, true);
                    break;
                case ScriptOp.FlagClear clear:
                    Write(clear.Flag, false);
                    break;
                case ScriptOp.PartyAddGold add:
                    _goldDelta += add.Amount & 0xFFFF;
                    break;
                case ScriptOp.PartyTakeGold take:
                    _goldDelta -= take.Amount & 0xFFFF;
                    break;
                case ScriptOp.PartyAddItem item:
                    _written[new Atom(AtomType.Item, item.Item)] = 1;
                    _effects.Add(new Gain(item.Item));
                    break;
                case ScriptOp.PartyTakeItem item:
                    _written[new Atom(AtomType.Item, item.Item)] = 0;
                    _effects.Add(new Lose(item.Item));
                    break;
                case ScriptOp.PartyAddMember add:
                    if (Known(new Atom(AtomType.Member, add.Entity)) != 1)
                        _partyDelta++;
                    _written[new Atom(AtomType.Member, add.Entity)] = 1;
                    _effects.Add(new Join(add.Entity));
                    break;
                case ScriptOp.PartyRemoveMember remove:
                    if (Known(new Atom(AtomType.Member, remove.Entity)) != 0)
                        _partyDelta--;
                    _written[new Atom(AtomType.Member, remove.Entity)] = 0;
                    _effects.Add(new Leave(remove.Entity));
                    break;
            }
            return true;
        }

        private void Write(RecordId flag, bool value)
        {
            _written[new Atom(AtomType.Flag, flag)] = value ? 1 : 0;
            _effects.Add(new SetFlag(flag, value));
        }

        /// <summary>How many this run knows are in the party.</summary>
        private int MembersIn()
        {
            var members = new HashSet<RecordId>();
            foreach (var (atom, range) in _pre)
            {
                if (atom.Type == AtomType.Member && range.Lo == 1)
                    members.Add(atom.A!.Value);
            }
            foreach (var (atom, value) in _written)
            {
                if (atom.Type == AtomType.Member)
                {
                    if (value == 1)
                        members.Add(atom.A!.Value);
                    else
                        members.Remove(atom.A!.Value);
                }
            }
            members.Add(GameState.Alaron);
            return members.Count;
        }

        /// <summary>A bool atom's value if this run already knows it, else null.</summary>
        private int? Known(Atom atom)
        {
            if (_written.TryGetValue(atom, out var w))
                return w;
            return _pre.TryGetValue(atom, out var r) && r.Lo == r.Hi ? r.Lo : null;
        }

        /// <summary>Where the values of a condition split: each range's start and the value past its end.</summary>
        private static List<int> Breaks(IEnumerable<ScriptRange> ranges)
        {
            var breaks = new SortedSet<int>();
            foreach (var r in ranges)
            {
                breaks.Add(r.Min);
                if (r.Max is { } max)
                    breaks.Add(max + 1);
            }
            return [.. breaks];
        }

        /// <summary>
        /// A condition's value: what this run knows, or else a branch among the classes of values that the ranges tell apart and
        /// that what it knows allows, which narrows what the state must be as the script starts.
        /// </summary>
        private int Answer(ScriptOp.Check check, List<int> breaks)
        {
            var atom = Atom.Of(check);
            if (atom.Type == AtomType.Unknown)
                return 0;
            var domain = atom.Domain;
            var pre = _pre.TryGetValue(atom, out var p) ? p : domain;
            var delta = atom.Type switch
            {
                AtomType.PartySize => _partyDelta,
                AtomType.Gold => _goldDelta,
                _ => 0,
            };
            if (atom.IsBool && _written.TryGetValue(atom, out var written))
                return written;
            // A flag the engine writes, or a derived flag over flags this run has written, is read as the state has it then: the
            // planner takes it as the state before the script, which is near enough for the few that do.
            var (lo, hi) = (pre.Lo + delta, pre.Hi + delta);
            var classes = new List<(int Lo, int Hi)>();
            var start = lo;
            foreach (var b in breaks)
            {
                if (b <= start || b > hi)
                    continue;
                classes.Add((start, b - 1));
                start = b;
            }
            classes.Add((start, hi));
            // No more than a full party can be in it, Alaron always among them. What that rules out goes without saying, so ways
            // that differ in it still merge.
            if (atom.Type == AtomType.Member && classes.Count == 2 && MembersIn() >= Characters.Party.MaxSize)
            {
                classes.RemoveAt(1);
                _implied.Add(atom);
            }
            var pick = classes.Count == 1 ? classes[0] : classes[Decide(classes.Count)];
            _pre[atom] = (pick.Lo - delta, pick.Hi - delta);
            return pick.Lo;
        }

        private int Decide(int options)
        {
            var d = Decisions.Count;
            int value;
            if (d < prefix.Count)
            {
                value = prefix[d];
            }
            else
            {
                value = 0;
                NewPoints.Add((d, options));
            }
            Decisions.Add(value);
            return value;
        }

        /// <summary>
        /// What decides where a page leads: the page, the choices taken, and what this run knows and has done, as two 64-bit hashes
        /// (a string of it all made the search several times slower).
        /// </summary>
        private (ulong, ulong) Fingerprint(ScriptRunner runner, ScriptPage? page, bool choosing)
        {
            var h = new Hash();
            h.Add(choosing ? 1 : 2);
            h.Add((int)runner.Status);
            h.Add(runner.Wait is { } wait ? (int)wait : -1);
            if (page is not null)
            {
                h.Add(page.Line.Node);
                foreach (var c in page.Choices)
                    h.Add(c.Node + 100_000);
            }
            h.Add(-1);
            // A backdrop's or the help menu's choices can be taken again, so what's been taken changes nothing.
            if (script!.Mode is not (ScriptMode.Backdrop or ScriptMode.Help))
            {
                for (var i = 0; i < script.Nodes.Count; i++)
                {
                    if (runner.Visited(i))
                        h.Add(i);
                }
            }
            h.Add(-2);
            // What it knows, in no order.
            ulong a = 0, b = 0;
            foreach (var (atom, range) in _pre)
            {
                var e = new Hash();
                e.Add(atom.GetHashCode());
                e.Add(range.Lo);
                e.Add(range.Hi);
                a += e.A;
                b ^= e.B;
            }
            h.Add((int)a);
            h.Add((int)(a >> 32));
            h.Add((int)b);
            h.Add((int)(b >> 32));
            foreach (var e in _effects)
                h.Add(e.GetHashCode());
            h.Add(_partyDelta);
            h.Add(_goldDelta);
            return (h.A, h.B);
        }

        /// <summary>Two FNV-style hashes, run side by side.</summary>
        private struct Hash()
        {
            public ulong A = 14695981039346656037UL;
            public ulong B = 0x9E3779B97F4A7C15UL;

            public void Add(int value)
            {
                A = (A ^ (uint)value) * 1099511628211UL;
                B = ulong.RotateLeft((B ^ (uint)value) * 0xC2B2AE3D27D4EB4FUL, 29);
            }
        }

        // IScriptWorld: the probe answers conditions and takes effects, so the runner only asks for these.

        bool IScriptWorld.InParty(RecordId entity) => false;
        bool IScriptWorld.HasItem(RecordId item) => false;
        int IScriptWorld.SkillCheck(int skill, IRandom random) => 0;
        int IScriptWorld.BestStat(int stat) => 0;
        int IScriptWorld.WorstStat(int stat) => 0;
        int IScriptWorld.MemberStat(RecordId? member, int stat) => 0;
        int IScriptWorld.PartySize => 1;
        string? IScriptWorld.PlayerName => null;
        uint IScriptWorld.Gold { get => 0; set { } }
        void IScriptWorld.AddItem(RecordId item) { }
        bool IScriptWorld.TakeItem(RecordId item) => true;
        bool IScriptWorld.AddMember(RecordId entity) => true;
        void IScriptWorld.RemoveMember(RecordId entity) { }
        bool IScriptWorld.AddExp(RecordId member, int amount) => true;
        void IScriptWorld.CampHeal() { }
        bool IScriptWorld.TryRecall(Memory memory, out int value)
        {
            value = 0;
            return false;
        }
        void IScriptWorld.Remember(Memory memory, int value) { }

        void IScriptHost.Cue(ScriptCue cue) { }
    }
}
