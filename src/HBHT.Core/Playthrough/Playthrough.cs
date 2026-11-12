using System.Diagnostics;
using System.Globalization;
using System.Text;
using HBHT.Core.Characters;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Playthrough;

/// <summary>How the run plays, and when it gives up.</summary>
/// <param name="MostTicks">The game's ticks the run may play, all told.</param>
/// <param name="MostWall">The wall time the run may take, all told; no limit if null.</param>
/// <param name="MostFailures">Steps in a row that may fail before the run gives up on a goal.</param>
/// <param name="StepTicks">The ticks one step may take, its consequences' included: battles, cut-scenes, levelling.</param>
/// <param name="PlanBudget">How many states the planner may look at for one plan.</param>
/// <param name="Saves">The folder the milestones' saves go in: the data folder's <c>saves/playthrough</c> if null.</param>
/// <param name="WriteSaves">Whether saves are written at all.</param>
/// <param name="TrainerReach">How far, in units of walking, a trainer may be from where a step leaves the player to be visited.</param>
/// <param name="GoldReserve">The gold kept back from training.</param>
/// <param name="ShopReach">
/// How far a shop may be from where a step leaves the player to be visited, 1.5 times it of walking: further than a trainer, since the
/// shops are in the towns' interiors, where nothing wanders.
/// </param>
/// <param name="MenuPatience">How long the pause menu may stay open: long, for spending a battle's experience.</param>
/// <param name="SaveEveryStep">
/// Whether a save is written before every step the player has the controls for (<c>steps/t&lt;n&gt;.sav</c> in the saves folder), each
/// listed in <see cref="Playthrough.StepSaves"/>, and the flags a ledger on the host sees put down to the step they were seen in
/// (<see cref="Playthrough.StepSeen"/>): the trunk (<see cref="Trunk"/>).
/// </param>
public sealed record RunOptions(
    long MostTicks = long.MaxValue,
    TimeSpan? MostWall = null,
    int MostFailures = 8,
    long StepTicks = 60 * 60 * 15,
    int PlanBudget = 4000,
    string? Saves = null,
    bool WriteSaves = true,
    float TrainerReach = 40,
    uint GoldReserve = 300,
    int MenuPatience = 60 * 60 * 10,
    float ShopReach = 150,
    bool SaveEveryStep = false)
{
    public static readonly RunOptions Default = new();
}

/// <summary>What one step of the run came to.</summary>
/// <param name="Done">The step did what it was expected to: played out, its expectations held.</param>
/// <param name="Outcome">The pilot's outcome, or null for an event or a step not played.</param>
/// <param name="Translated">The step as the pilot was given it.</param>
public sealed record RunStep(int Number, Goal Goal, PlayerAction Action, bool Done, string Summary, StepOutcome? Outcome, TranslatedStep Translated)
{
    /// <summary>A step that missed on a roll it may try again, which isn't counted against its goal.</summary>
    public bool Again { get; init; }
}

/// <summary>
/// The playthrough bot's run (docs/systems/playthrough.md, layer 6): it plays from a host's game to a goal. Each step it plans from the
/// real state (<see cref="PlanState.From"/>), with the world graph behind the planner's places from where the player stands
/// (<see cref="WorldReach"/>), takes the plan's first step, plays it with the pilot (<see cref="StepTranslation"/>, <see cref="Pilot"/>),
/// and compares what the game did with what the step expected. A step that fails is reported and its action banned in that state, and
/// it plans again. The member goal comes first while the party has room and the planner has a way to it; battles are the
/// <see cref="BattlePilot"/>'s, and experience is spent by <see cref="Playthrough.Levelling"/> after battles and at trainers near the way.
/// At each milestone it writes a save, and it keeps the report (<see cref="RunReport"/>).
/// </summary>
public sealed partial class Playthrough
{
    private readonly QuestPlanner _planner;
    private readonly HashSet<RecordId> _journal;
    private readonly List<(RecordId Object, RecordId Trainer)> _trainers = [];
    private readonly HashSet<RecordId> _visited = [];
    private readonly Dictionary<PlanState, HashSet<int>> _bannedIn = [];
    private readonly Dictionary<int, int> _failedTimes = [];
    private readonly HashSet<int> _bannedForGood = [];
    private readonly Dictionary<RecordId, uint> _trained = [];
    private readonly Dictionary<Goal, List<PlayerAction>> _rest = [];
    private readonly Stopwatch _wall = new();
    private int _saves;
    private int _step;
    private string? _before;
    private string? _beforePath;
    private bool _memberResting;
    private PassGoal? _passing;
    private readonly HashSet<PlanState> _unbanned = [];
    private readonly HashSet<BattleFought> _told = [];
    private readonly Dictionary<RecordId, List<PlayerAction>> _shops = [];
    private readonly Dictionary<RecordId, uint> _shopped = [];
    private readonly Dictionary<int, int> _rerolls = [];

    /// <summary>How many times a step whose Troubadour check went against it is played again.</summary>
    private const int MostRerolls = 8;
    private readonly HashSet<RecordId> _hopeless = [];
    private int _joined;
    private long _startTicks;

    /// <param name="host">The game to play, a new game's or a save's.</param>
    /// <param name="model">The quest model over the same plugins; the run plays on a model of its own made from it (<see cref="QuestModel.ForRun"/>), so runs side by side may share one.</param>
    /// <param name="graph">The world graph over the same plugins, which runs side by side may share.</param>
    /// <param name="goal">What the run is after.</param>
    public Playthrough(PlaythroughHost host, QuestModel model, WorldGraph graph, Goal goal, RunOptions? options = null)
    {
        Host = host;
        model = model.ForRun();
        Model = model;
        Graph = graph;
        Goal = goal;
        Options = options ?? RunOptions.Default;
        Triggers = new StoryTriggers(model);
        _planner = new QuestPlanner(model) { Triggers = Triggers };
        Battles = new BattlePilot();
        Outfitting = new Outfitting();
        Levelling = new Levelling(Outfitting) { GoldReserve = Options.GoldReserve };
        host.Battles = Battles;
        Resting = new Resting(Levelling);
        host.Menus = Resting;
        host.MenuPatience = Options.MenuPatience;
        var plugins = model.Plugins;
        _journal = [.. plugins.All<FlagRecord>(RecordTypes.Flag).Where(f => f.Journal is not null).Select(f => f.Id)];
        foreach (var chunk in plugins.All<ChunkRecord>(RecordTypes.Chunk))
        {
            foreach (var npc in chunk.Objects.OfType<NpcObject>())
            {
                if ((npc.Flags & PartyFlags.Silent) == 0 && model.Ways.TryGetValue(npc.Script, out var ways)
                    && ways.Ways.Any(w => w.End == ScriptEndKind.Train))
                    _trainers.Add((chunk.ObjectId(npc.Index), npc.Entity));
            }
        }
        // The npcs whose conversation has a way that opens their shop, with those ways.
        foreach (var action in model.Actions.Where(a => a is { Kind: ActionKind.Talk, Buys: null, Place: { } } && a.Way?.End == ScriptEndKind.Shop))
        {
            if (!_shops.TryGetValue(action.Place!.Value, out var ways))
                _shops[action.Place.Value] = ways = [];
            ways.Add(action);
        }
    }

    public PlaythroughHost Host { get; }

    public QuestModel Model { get; }

    public WorldGraph Graph { get; }

    /// <summary>The run's goal, at the bottom of its stack.</summary>
    public Goal Goal { get; }

    public RunOptions Options { get; }

    public BattlePilot Battles { get; }

    /// <summary>What puts better gear on the party from the inventory, a menu policy under <see cref="Levelling"/>.</summary>
    public Outfitting Outfitting { get; }

    /// <summary>The story triggers, with the ways the run has seen turn the player back.</summary>
    public StoryTriggers Triggers { get; }

    public Levelling Levelling { get; }

    /// <summary>The camping when the party is hurt, a menu policy over <see cref="Levelling"/>.</summary>
    public Resting Resting { get; }

    public RunReport Report { get; } = new();

    /// <summary>Every step played, in order.</summary>
    public List<RunStep> Played { get; } = [];

    /// <summary>A line for each step as it's played, and for each milestone.</summary>
    public Action<string>? Progress { get; set; }

    /// <summary>The folder the run's saves go in.</summary>
    public string SavesFolder => Options.Saves ?? Path.Combine(DataPaths.Saves, "playthrough");

    private GameState Game => Host.Game;

    /// <summary>Plays to the goal, or until a budget runs out or nothing more can be done. The report's summary says which.</summary>
    public RunReport Run()
    {
        _wall.Start();
        _startTicks = Host.Clock.Ticks;
        if (Goal is CoverGoal cover)
            StartCover(cover);
        string? why = null;
        var reached = false;
        var failures = new Dictionary<Goal, int>();
        var noControl = 0;
        try
        {
            if (!Host.PlayerHasControl && Host.RunUntilControl(Options.StepTicks) != HostRun.Done && !EndingReached())
            {
                why = $"the player never had the controls: {Host.End}";
            }
            else
            {
                if (Here() is { } start)
                    _visited.Add(start.MapSet);
                Milestone(MilestoneKind.Start, "the run begins");
            }
            while (why is null)
            {
                if (Reached(Goal))
                {
                    reached = true;
                    Milestone(MilestoneKind.Goal, Goal.Describe());
                    break;
                }
                if (Host.End != HostEnd.None)
                {
                    why = $"the game ended ({Host.End})";
                    break;
                }
                if (OutOfBudget() is { } budget)
                {
                    why = budget;
                    break;
                }
                if (!Host.PlayerHasControl && Host.RunUntilControl(Options.StepTicks) != HostRun.Done)
                {
                    Entry(EntryKind.OutOfTicks, null, "the player gets the controls back", $"{Options.StepTicks} ticks without them, in {Host.Session.Mode}",
                        fromStep: false);
                    if (++noControl >= Options.MostFailures)
                        why = "the player never got the controls back";
                    continue;
                }
                noControl = 0;

                var state = PlanState.From(Model, Game);
                var reach = MakeReach(state);
                var (goal, steps, plan) = Choose(state);
                // A cover goal with nothing to play has given up on what it couldn't plan to, or has nothing left.
                if (steps.Count == 0 && Goal is CoverGoal)
                    continue;
                if (steps.Count == 0 && Probe(state, Goal) is { } probe)
                {
                    // No plan, but the relaxed plan's next step can be done here: the story may move the player on from it.
                    Entry(EntryKind.Unreachable, null, Goal.Describe(),
                        $"no plan towards it ({plan?.Why ?? "the planner found none"}); the relaxed plan's next step that can be done here is played: {probe.Describe()}",
                        fromStep: false);
                    (goal, steps) = (Goal, [probe]);
                    var probed = Play(probe, goal, state, reach);
                    // One that changed nothing the planner knows isn't played again from here.
                    if (probed.Done && PlanState.From(Model, Game).Equals(state))
                        Ban(probe, state);
                    if (!probed.Done && (failures[goal] = failures.GetValueOrDefault(goal) + 1) >= Options.MostFailures)
                        why = $"{Options.MostFailures} steps in a row failed";
                    continue;
                }
                // With nothing left to try, the steps banned where it stands are tried once more: what failed may have failed for where
                // the player stood, or a moment's chance, as a tripwire walked into at the edge of its radius.
                if (steps.Count == 0 && _bannedIn.TryGetValue(state, out var bannedHere) && bannedHere.Count > 0 && _unbanned.Add(state))
                {
                    Entry(EntryKind.Unreachable, null, Goal.Describe(),
                        $"no step towards it; the {bannedHere.Count} step(s) that failed here are tried again", fromStep: false);
                    _bannedIn.Remove(state);
                    continue;
                }
                if (steps.Count == 0)
                {
                    var stuck = Stuck(state, Goal, reach);
                    why = $"no step towards {Goal}: {plan?.Why ?? "the planner found none"}{(stuck.Length > 0 ? $"; {stuck}" : "")}";
                    Entry(EntryKind.Unreachable, null, Goal.Describe(), why, fromStep: false);
                    break;
                }
                var joinedBefore = _joined;
                var step = Play(steps[0], goal, state, reach);
                // Turned back by a story trigger: getting past it comes first.
                if (step.Outcome is { Failure: StepFailure.Blocked, Blocker: { } blocker } && !_hopeless.Contains(blocker))
                    _passing = new PassGoal(blocker);
                if (step.Done)
                {
                    if (goal is FlagGoal done && Goal is CoverGoal)
                        CoverDone(done, step, state);
                    if (goal is MemberGoal && Goal is CoverGoal)
                        CoverMemberDone(step, state, _joined > joinedBefore);
                    if (goal is PartyGoal && Goal is CoverGoal)
                        CoverPartyDone(step, state);
                    failures[goal] = 0;
                    _rest[goal] = steps.Skip(1).ToList();
                    VisitTrainer();
                    VisitShop();
                }
                else
                {
                    _rest.Remove(goal);
                    if (!step.Again)
                        failures[goal] = failures.GetValueOrDefault(goal) + 1;
                    if (goal is FlagGoal target && Goal is CoverGoal)
                    {
                        // A target is given up on after its own few failures; the cover goal goes on.
                        if (!step.Again)
                            CoverFailed(target, step);
                        failures[goal] = 0;
                    }
                    else if (failures[goal] >= Options.MostFailures)
                    {
                        if (goal is PassGoal pass)
                        {
                            GiveUp(pass, $"{Options.MostFailures} steps towards it failed in a row");
                            failures[goal] = 0;
                        }
                        else if (goal is MemberGoal && Goal is not MemberGoal)
                        {
                            Rest($"{Options.MostFailures} steps towards it failed in a row");
                            failures[goal] = 0;
                        }
                        else if (goal is PartyGoal && Goal is CoverGoal)
                        {
                            CoverPartyFailed();
                            failures[goal] = 0;
                        }
                        else
                        {
                            why = $"{Options.MostFailures} steps in a row failed";
                        }
                    }
                }
            }
            FinishCover();
            TakeStepSeen();
        }
        catch (Exception e)
        {
            why = $"an exception: {e.Message}";
            Entry(EntryKind.Exception, null, "the run plays on", e.ToString(), fromStep: false);
        }
        _wall.Stop();
        Summarise(reached, why);
        return Report;
    }

    // Goals.

    /// <summary>Whether a goal has come about in the real game.</summary>
    private bool Reached(Goal goal) => goal switch
    {
        FlagGoal f => Game.Flags.IsSet(f.Flag),
        MemberGoal => _joined > 0,
        PartyGoal party => party.Members.All(Game.Party.Contains),
        CoverGoal => _coverDone,
        EndingGoal => EndingReached(),
        _ => false,
    };

    private bool EndingReached() => Host.Transitions.Any(t => t.Transition is Transition.PlayCredits { Ending: true });

    /// <summary>
    /// The goal to plan for, and its steps: the member goal while the party has a slot to fill and it isn't resting, if the planner has
    /// a step towards it; else the run's own.
    /// </summary>
    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan) Choose(PlanState state)
    {
        if (Goal is CoverGoal)
            return ChooseCover(state);
        if (ChoosePass(state) is { } passing)
            return passing;
        if (Goal is not (MemberGoal or PartyGoal) && !_memberResting && Game.Party.HasEmptySlot())
        {
            var member = new MemberGoal();
            var (steps, plan) = PlanTo(member, state);
            if (steps.Count > 0)
                return (member, steps, plan);
            Rest($"no step towards it: {plan?.Why ?? "the planner found none"}");
        }
        var (main, mainPlan) = PlanTo(Goal, state);
        return (Goal, main, mainPlan);
    }

    /// <summary>Getting past the story trigger that turned the player back, while it still does and the planner has steps; else null.</summary>
    private (Goal Goal, List<PlayerAction> Steps, Plan? Plan)? ChoosePass(PlanState state)
    {
        if (_passing is { } pass)
        {
            if (_planner.Achieved(state, pass, []))
            {
                _passing = null;
                _rest.Remove(pass);
            }
            else
            {
                var (steps, plan) = PlanTo(pass, state);
                if (steps.Count > 0)
                    return (pass, steps, plan);
                GiveUp(pass, $"no step towards it: {plan?.Why ?? "the planner found none"}");
            }
        }
        return null;
    }

    /// <summary>
    /// Why the planner has no step: the relaxed plan's first actions, those it would take next, and what keeps each from being done
    /// here: a place no way on foot reaches from where the player stands, or the first precondition that doesn't hold.
    /// </summary>
    private string Stuck(PlanState state, Goal goal, WorldReach reach)
    {
        var facts = _planner.GoalFacts(state, goal);
        if (facts.Count == 0)
            return "";
        var relevant = _planner.Relevant(facts);
        var relaxation = new Relaxation(Model, relevant);
        if (relaxation.Extract(relaxation.Run(state), facts, QuestPlanner.AnyFact(goal)) is not { } relaxed)
            return "";
        var lines = relaxed.Helpful.Take(5).Select(a => Model.Actions[a]).Select(a =>
            a.Presence.OfType<Reaches>().FirstOrDefault(r => reach.OffFoot(r.Place)) is { } far
                ? $"{a.Describe()} is at a place no way on foot reaches from here ({far.Place})"
                : $"{a.Describe()} needs {state.Blocker(a)?.Describe() ?? "nothing, but the search didn't take it"}");
        return $"the relaxed plan's next steps: {string.Join("; ", lines)}";
    }

    /// <summary>
    /// With no plan, the relaxed plan's helpful actions, first first, that can be done in the state as it stands and aren't banned
    /// there: a place the story moves the player to (off foot, which the search never steps to) often follows from one, as Jundar's
    /// 11/A01/19 follows from walking into 11/A01/4. Null if none can.
    /// </summary>
    private PlayerAction? Probe(PlanState state, Goal goal)
    {
        var facts = _planner.GoalFacts(state, goal);
        if (facts.Count == 0)
            return null;
        var relaxation = new Relaxation(Model, _planner.Relevant(facts));
        if (relaxation.Extract(relaxation.Run(state), facts, QuestPlanner.AnyFact(goal)) is not { } relaxed)
            return null;
        var banned = Banned(state);
        return relaxed.Helpful.Select(a => Model.Actions[a]).FirstOrDefault(a => !banned.Contains(a.Index) && state.CanDo(a));
    }

    /// <summary>Getting past a story trigger is given up on until the next milestone, and the run's goals go on.</summary>
    private void GiveUp(PassGoal pass, string why)
    {
        _hopeless.Add(pass.Trigger);
        _passing = null;
        _rest.Remove(pass);
        Entry(EntryKind.Unreachable, null, pass.Describe(), $"given up on until the next milestone: {why}", fromStep: false);
    }

    /// <summary>The member goal rests until the next milestone.</summary>
    private void Rest(string why)
    {
        _memberResting = true;
        _rest.Remove(new MemberGoal());
        Entry(EntryKind.Unreachable, null, "a member joins", $"the member goal rests until the next milestone: {why}", fromStep: false);
    }

    /// <summary>
    /// The steps to a goal from the real state: what was left of the last plan to it, if it all still holds from here; else a new plan,
    /// or as far as the search got.
    /// </summary>
    private (List<PlayerAction> Steps, Plan? Plan) PlanTo(Goal goal, PlanState state)
    {
        var banned = Banned(state);
        if (_rest.TryGetValue(goal, out var rest) && rest.Count > 0 && Holds(state, rest, banned))
            return (rest, null);
        var plan = _planner.Plan(state, goal, Options.PlanBudget, banned);
        return (plan.Steps.ToList(), plan);
    }

    private static bool Holds(PlanState state, IEnumerable<PlayerAction> steps, IReadOnlySet<int> banned)
    {
        foreach (var step in steps)
        {
            if (banned.Contains(step.Index) || !state.CanDo(step))
                return false;
            state = state.Apply(step);
        }
        return true;
    }

    private HashSet<int> Banned(PlanState state)
    {
        var banned = new HashSet<int>(_bannedForGood);
        if (_bannedIn.TryGetValue(state, out var here))
            banned.UnionWith(here);
        return banned;
    }

    /// <summary>A failed action is banned in the state it was tried in, and for good once it has failed three times.</summary>
    private void Ban(PlayerAction action, PlanState state)
    {
        if (!_bannedIn.TryGetValue(state, out var here))
            _bannedIn[state] = here = [];
        here.Add(action.Index);
        if ((_failedTimes[action.Index] = _failedTimes.GetValueOrDefault(action.Index) + 1) >= 3)
            _bannedForGood.Add(action.Index);
    }

    /// <summary>
    /// The world graph behind the planner's places, from where the player stands, found in rounds (<see cref="WorldReach.Layered"/>): a gate
    /// opens only once a relaxation over the places got to so far makes its literal true.
    /// </summary>
    private WorldReach MakeReach(PlanState state) => WorldReach.Layered(Model, Graph, Game, state, Here() ?? Graph.PlaceOf(Game));

    private Place? Here() => Host.Session is { Mode: GameMode.Trek } s ? new Place(s.Trek.Chunks.MapSet.Id, s.Trek.Player.Feet) : null;

    private string? OutOfBudget()
    {
        if (Host.Clock.Ticks - _startTicks >= Options.MostTicks)
            return $"{Options.MostTicks} ticks of game time ran out";
        if (Options.MostWall is { } wall && _wall.Elapsed >= wall)
            return $"{wall.TotalMinutes:0} minutes of wall time ran out";
        return null;
    }

    // Steps.

    /// <summary>
    /// Plays one of the planner's actions from the real game: an event by running until the player has the controls, anything else by
    /// the pilot, after checking its code gates. The report gets what went differently, and milestones their saves.
    /// </summary>
    /// <param name="state">The real state it's played from, which a failure bans it in; taken from the game if null.</param>
    /// <param name="reach">The places' reach it was planned with, for whether its way picks locks; none if null.</param>
    public RunStep Play(PlayerAction action, Goal goal, PlanState? state = null, WorldReach? reach = null)
    {
        state ??= PlanState.From(Model, Game);
        if (_visited.Count == 0 && Here() is { } first)
            _visited.Add(first.MapSet);
        TakeStepSeen();
        var number = ++_step;
        var clock = Stopwatch.StartNew();
        var startTick = Host.Clock.Ticks;
        KeepSaveBefore();
        SaveStep(action);
        var before = Before.Take(this);
        var translated = StepTranslation.Translate(action, Model, Game, Graph, reach);
        StepOutcome? outcome = null;
        bool done;
        string summary;
        foreach (var note in translated.Notes)
            Entry(EntryKind.ChoiceMissed, action, "the step as planned", note);
        if (translated.Unmet.Count > 0)
        {
            done = false;
            summary = $"not played: {string.Join("; ", translated.Unmet.Select(g => g.What))}";
            Entry(EntryKind.CodeGate, action, string.Join("; ", translated.Unmet.Select(g => g.What)), "the party doesn't meet it as it stands, so the step isn't played");
        }
        else if (translated.IsEvent)
        {
            var run = Host.RunUntilControl(Options.StepTicks);
            done = run == HostRun.Done;
            summary = done ? "played until the player had the controls" : $"ran out: {run}";
            if (!done)
                Entry(EntryKind.OutOfTicks, action, "the player gets the controls", summary);
        }
        else if (translated.Step is not { } pilotStep)
        {
            done = false;
            summary = "no map object to go to";
            Entry(EntryKind.NoRoute, action, $"a map object at {action.Place}", string.Join("; ", translated.Notes));
        }
        else
        {
            var pilot = new Pilot(Graph, PilotOptions.Default with { PickLocks = translated.PickLocks, Rest = Resting, Triggers = Triggers, Dress = Outfitting, Waits = WaitsForTheHour, Shun = (Goal as CoverGoal)?.Shun });
            outcome = pilot.Run(Host, pilotStep, Math.Min(Options.StepTicks, Math.Max(1, Options.MostTicks - (Host.Clock.Ticks - _startTicks))));
            var expect = pilotStep.Expect ?? [];
            // Done by the way: what it was for came about though the pilot didn't use it (an encounter caught the party first).
            var byTheWay = !outcome.Done && expect.Count > 0 && outcome.Misses.Count == 0;
            done = (outcome.Done || byTheWay) && outcome.Misses.Count == 0;
            summary = outcome.Done ? (outcome.Misses.Count == 0 ? "done" : $"missed {string.Join(", ", outcome.Misses)}")
                : byTheWay ? $"{outcome.Failure}, but what it was for came about" : $"{outcome.Failure}: {outcome.Reason}";
            Compare(action, outcome, translated, byTheWay);
        }
        // A Troubadour check isn't kept by its map object, so a step that rolled against it is played again, a few times, before it's banned.
        var reroll = !done && outcome is { Used: true } && translated.Rolls.Any(g => g.Check is { Kind: CodeCheckKind.Skill, A: { } skill }
            && Characters.SkillId.TryFromRecord(skill) is { } id && id.Slot == (int)Characters.Skill.Troubadour)
            && (_rerolls[action.Index] = _rerolls.GetValueOrDefault(action.Index) + 1) <= MostRerolls;
        // A roll the game keeps, a trap that went off or a skill check its map object remembers, is played again from the save before the
        // step, as a player reloads: a trap's container is destroyed, and its Opened and Used flags never come.
        if (!done && !reroll && outcome is { Used: true, Misses.Count: > 0 } && _before is { } kept
            && translated.Rolls.Any(g => g.Check?.Kind is CodeCheckKind.Trap or CodeCheckKind.Skill)
            && (_rerolls[action.Index] = _rerolls.GetValueOrDefault(action.Index) + 1) <= MostRerolls)
            reroll = LoadBefore(kept, _rerolls[action.Index]);
        // Stuck the first time, the game is saved and loaded where he stands, and the step may be tried again from there.
        var reloaded = outcome?.Failure == StepFailure.Stuck && _failedTimes.GetValueOrDefault(action.Index) == 0 && SaveAndLoad();
        if (reloaded)
            _failedTimes[action.Index] = 1;
        else if (!done && !reroll)
            Ban(action, state);
        AfterStep(action, before, startTick);
        var line = new StepLine(number, goal.Describe(), action.Describe(), summary, Host.Clock.Ticks - startTick, clock.Elapsed.TotalSeconds, startTick);
        Report.Steps.Add(line);
        Progress?.Invoke($"{number,4} {Host.Clock.Ticks,10} [{goal}] {action.Describe()}: {summary}");
        var step = new RunStep(number, goal, action, done, summary, outcome, translated) { Again = reroll };
        Played.Add(step);
        return step;
    }

    /// <summary>
    /// After a roll went against a step, the game starts again on the save from before it, and the player stands a while (17 ticks a try)
    /// so that the world's random stream, which the weather draws on every frame, rolls anew. Whether it did.
    /// </summary>
    private bool LoadBefore(string save, int tries)
    {
        Host.Restart(GameState.Load(Model.Plugins, SaveFile.Parse(save, Model.Plugins)));
        if (Host.RunUntilControl(Options.StepTicks) != HostRun.Done)
            return false;
        var until = Host.Clock.Ticks + 17 * tries;
        Host.RunUntil(h => h.Clock.Ticks >= until, 17 * tries + 1);
        Progress?.Invoke($"     a roll went against it: loaded the save before the step, for try {tries + 1}");
        return true;
    }

    /// <summary>
    /// After the pilot stuck, the game is saved and loaded where the player stands, as a player whose party is wedged does from the
    /// System tab: the session starts again on the save, the player set down afresh. In one run he stalled at Overworld P17 by the
    /// teleporter P17/4, 2 short of a waypoint, through two steps; from a save of the same moment he walks on.
    /// </summary>
    private bool SaveAndLoad()
    {
        if (!Host.PlayerHasControl || !Host.Session.NotePlace())
            return false;
        var save = SaveFile.Serialize(Save());
        Host.Restart(GameState.Load(Model.Plugins, SaveFile.Parse(save, Model.Plugins)));
        Host.RunUntilControl(Options.StepTicks);
        Progress?.Invoke("     saved and loaded where the player stood");
        return true;
    }

    /// <summary>
    /// Whether a way the route found shut is shut only by gates the hour opens: each gate's flag is closed now, and the planner, which
    /// takes the flags the engine writes (the calendar) as either value, says it can open. The Burial Chamber's door in Ugarit2 A05 is
    /// locked behind derived 5022, which the time of day (5283) opens.
    /// </summary>
    private bool WaitsForTheHour(RouteRefusal refusal)
    {
        var state = PlanState.From(Model, Game);
        return refusal.Blockers.Count > 0 && refusal.Blockers.All(g => g.Flag is { } flag && Model.Flags.TryHandle(flag, out var h)
            && Model.Flags.Kind(h) == FlagKind.Derived && state.Holds(new FlagIs(flag, !g.Invert)));
    }

    /// <summary>The report's entries for what the pilot's outcome says went differently.</summary>
    private void Compare(PlayerAction action, StepOutcome outcome, TranslatedStep translated, bool byTheWay)
    {
        if (!outcome.Done && !byTheWay)
        {
            var kind = outcome.Failure switch
            {
                StepFailure.NoRoute => EntryKind.NoRoute,
                StepFailure.Stuck => EntryKind.Stuck,
                StepFailure.NotUsed => EntryKind.NotUsed,
                StepFailure.Blocked => EntryKind.Blocked,
                _ => EntryKind.OutOfTicks,
            };
            if (outcome.Failure != StepFailure.GameOver)
                Entry(kind, action, $"{outcome.Step.How} {outcome.Step.MapObject}", outcome.Reason ?? outcome.Failure.ToString(), where: outcome.Where);
        }
        // What a step that never used its object didn't do is its failure's to say.
        if (outcome.Misses.Count > 0 && (outcome.Used || outcome.Done))
        {
            Entry(EntryKind.StepMissed, action, string.Join(", ", outcome.Misses), $"changed {outcome.Changes}", where: outcome.Where);
            foreach (var miss in outcome.ChoicesMissed)
                Entry(EntryKind.ChoiceMissed, action, $"choice {miss.Wanted.Choice} at {miss.Wanted.Script} node {miss.Wanted.Node}",
                    $"offered {string.Join(", ", miss.Offered)}", where: outcome.Where);
            foreach (var unseen in outcome.ChoicesNotReached)
                Entry(EntryKind.ChoiceMissed, action, $"choice {unseen.Choice} at {unseen.Script} node {unseen.Node}", "its page never came",
                    where: outcome.Where);
            var rolls = translated.Rolls.Where(g => g.Check?.Kind is not CodeCheckKind.Win).ToList();
            if (rolls.Count > 0)
                Entry(EntryKind.CodeGate, action, $"{string.Join("; ", rolls.Select(g => g.What))}, taken to hold", "left to a roll, which went against it",
                    where: outcome.Where);
        }
    }

    /// <summary>What a step's milestones are measured against: the members, the journal flags set, and how far the host's records went.</summary>
    private sealed record Before(HashSet<RecordId> Members, HashSet<RecordId> Journal, HashSet<RecordId> Died, int Transitions, int Battles, int Notes,
        int Problems, int Purchases, int Outfits)
    {
        public static Before Take(Playthrough run)
        {
            var game = run.Game;
            return new Before(
                [.. game.Party.Members.Select(m => m.Entity)],
                [.. run._journal.Where(game.Flags.IsSet)],
                [.. run.Model.Members.Where(m => game.Flags.IsSet(m.Value.Died)).Select(m => m.Key)],
                run.Host.Transitions.Count, run.Host.BattlesFought.Count, run.Host.Notes.Count, run.Battles.Problems.Count,
                run.Levelling.Bought.Count, run.Outfitting.Worn.Count);
        }
    }

    /// <summary>After a step: the battles' and the host's entries, and the milestones it came to, each with a save.</summary>
    private void AfterStep(PlayerAction? action, Before before, long startTick)
    {
        var game = Game;
        // Battles over since the last step's: one still playing as a step runs out is told once it's over, undone or not.
        var ended = Host.BattlesFought.Where(b => b.Ended is not null && _told.Add(b)).ToList();
        foreach (var fought in ended)
        {
            var name = fought.Setup.Party?.ToString() ?? fought.Setup.Source.ToString();
            if (fought.LostFirst)
                Entry(EntryKind.LostFirst, action, $"the party wins against {name}", "lost; played again from its start and won outright");
            if (fought.DiedFirst)
                Entry(EntryKind.DiedFirst, action, $"the party wins against {name} with everyone living", "won with a member dead; played again from its start and won outright");
            if (fought.Stalemate)
                Entry(EntryKind.Stalemate, action, $"the battle against {name} ends", "nobody could end it; won outright");
            // A battle lost is played again from its start, which undoes its deaths: the replay's are the ones that stand.
            if (fought.Outcome == Combat.BattleOutcome.Lost || fought.Undone)
                continue;
            foreach (var dead in fought.Deaths)
                Entry(EntryKind.Death, action, $"{Name(dead)} lives", $"{Name(dead)} died against {name}");
        }
        foreach (var note in Host.Notes.Skip(before.Notes).Where(n => n.Kind == NoteKind.Warn))
            Entry(EntryKind.Warning, action, "no warning", note.Text);
        foreach (var problem in Battles.Problems.Skip(before.Problems))
            Entry(EntryKind.Warning, action, "the battle pilot plays it", problem);
        foreach (var bought in Levelling.Bought.Skip(before.Purchases))
            Report.Purchases.Add(bought.ToString());
        foreach (var worn in Outfitting.Worn.Skip(before.Outfits))
        {
            Report.Purchases.Add($"wore {worn}");
            Progress?.Invoke($"     wore {worn}");
        }

        var milestones = new List<(MilestoneKind, string)>();
        foreach (var joined in game.Party.Members.Select(m => m.Entity).Where(m => !before.Members.Contains(m)))
        {
            _joined++;
            milestones.Add((MilestoneKind.Joined, $"{Name(joined)} joined"));
        }
        // A death in a battle still playing as the step ran out is told once it's over, since it may yet be played again.
        var playing = Host.BattlesFought.LastOrDefault() is { Ended: null } now ? now.Deaths : [];
        var died = Model.Members.Where(m => game.Flags.IsSet(m.Value.Died) && !before.Died.Contains(m.Key) && !playing.Contains(m.Key)).Select(m => m.Key)
            .Union(ended.Where(b => b.Outcome != Combat.BattleOutcome.Lost && !b.Undone).SelectMany(b => b.Deaths)).Distinct();
        foreach (var dead in died)
            milestones.Add((MilestoneKind.Died, $"{Name(dead)} died"));
        foreach (var entry in _journal.Where(f => game.Flags.IsSet(f) && !before.Journal.Contains(f)).OrderBy(f => f.Key.Length).ThenBy(f => f.Key, StringComparer.Ordinal))
            milestones.Add((MilestoneKind.Journal, $"journal entry {Model.Plugins.Get<FlagRecord>(entry).Journal!.Value.Key} (flag {entry.Key})"));
        foreach (var cinematic in Host.Transitions.Skip(before.Transitions).Select(t => t.Transition).OfType<Transition.PlayCinematic>())
            milestones.Add((MilestoneKind.Cinematic, $"cinematic {cinematic.Cinematic.Title ?? cinematic.Cinematic.Id.Key}"));
        if (Here() is { } here && _visited.Add(here.MapSet))
            milestones.Add((MilestoneKind.MapSet, $"entered {Model.Plugins.Get<MapSetRecord>(here.MapSet).Label}"));
        foreach (var (kind, what) in milestones)
            Milestone(kind, what);
    }

    private string Name(RecordId entity) => Model.Plugins.TryGet<EntityRecord>(entity)?.Name ?? entity.ToString();

    // Trainers.

    /// <summary>
    /// After a step done, a trainer within <see cref="RunOptions.TrainerReach"/> of walking from where it left the player, whose own gates
    /// are open, and who teaches something a member's build wants that the experience and gold (less the reserve) can pay for, is
    /// walked up to and trained with. A trainer isn't visited again until the party has more experience to spend than when it last was.
    /// </summary>
    private void VisitTrainer()
    {
        if (!Host.PlayerHasControl || Here() is not { } here)
            return;
        var party = Game.Party;
        var exp = (uint)party.Members.Sum(m => m.SpendableExp);
        var gold = party.Gold > Options.GoldReserve ? party.Gold - Options.GoldReserve : 0;
        foreach (var (id, entity) in _trainers)
        {
            if (_trained.TryGetValue(entity, out var at) && exp <= at)
                continue;
            var chunk = Model.Plugins.Get<ChunkRecord>(ChunkRecord.ChunkOf(id));
            if (chunk.MapSet != here.MapSet || chunk.Object(ChunkRecord.IndexOf(id)) is not { } o)
                continue;
            var at3 = Model.Plugins.Get<MapSetRecord>(chunk.MapSet).Origin(chunk) + o.Position;
            if (System.Numerics.Vector3.Distance(at3, here.Feet) > Options.TrainerReach
                || WorldGraph.ObjectGates(id, o).Any(g => g.Closed(Game.Flags)))
                continue;
            // A dialogue entity teaches from the sheet it trains as (Fyrsil, 0x140D at Overworld Q27/9, as STUG02); one with none is no trainer.
            if (Model.Plugins.TryGet<EntityRecord>(entity) is not { } record || Trainer.Of(record, Game.Rules, Model.Plugins) is not { } trainer)
                continue;
            if (!party.Members.Any(m => Builds.Next(m, Levelling.Builds, trainer, gold) is not null))
                continue;
            if (Graph.Route(Game, here, id) is not { Route: { } route } || route.Length > Options.TrainerReach * 1.5f)
                continue;
            var walk = new Pilot(Graph, PilotOptions.Default with { Rest = Resting, Triggers = Triggers, Dress = Outfitting, Waits = WaitsForTheHour, Shun = (Goal as CoverGoal)?.Shun }).Run(Host, new PilotStep(id, UseKind.Arrive), Options.StepTicks);
            if (!walk.Done || !Host.PlayerHasControl)
                return;
            var bought = Levelling.Train(Host, trainer);
            _trained[entity] = (uint)Game.Party.Members.Sum(m => m.SpendableExp);
            foreach (var b in bought)
                Report.Purchases.Add(b.ToString());
            Trainings++;
            Progress?.Invoke($"     trained with {record.Name} at {id}: {bought.Count} bought");
            return;
        }
    }

    /// <summary>
    /// After a step done, a shop within <see cref="RunOptions.ShopReach"/> of walking from where it left the player, whose keeper's
    /// conversation has a way to it that can be taken now, and whose stock has something better than the party has of its kind
    /// (<see cref="Outfitting.Gain"/>) within the gold less its reserve, is walked up to and talked to by that way; the shop policy buys
    /// (<see cref="Outfitting.Shopping"/>), and the pilot's dressing puts it on. A shop isn't visited again until the party has more gold
    /// than when it last was.
    /// </summary>
    private void VisitShop()
    {
        if (!Host.PlayerHasControl || Here() is not { } here)
            return;
        var party = Game.Party;
        if (party.Gold <= Outfitting.GoldReserve)
            return;
        var gold = party.Gold - Outfitting.GoldReserve;
        var catalog = party.Inventory.Catalog;
        PlanState? state = null;
        foreach (var (id, actions) in _shops)
        {
            if (_shopped.TryGetValue(id, out var at) && party.Gold <= at)
                continue;
            var chunk = Model.Plugins.Get<ChunkRecord>(ChunkRecord.ChunkOf(id));
            if (chunk.MapSet != here.MapSet || chunk.Object(ChunkRecord.IndexOf(id)) is not NpcObject npc)
                continue;
            var at3 = Model.Plugins.Get<MapSetRecord>(chunk.MapSet).Origin(chunk) + npc.Position;
            if (System.Numerics.Vector3.Distance(at3, here.Feet) > Options.ShopReach
                || WorldGraph.ObjectGates(id, npc).Any(g => g.Closed(Game.Flags))
                || Items.Shop.For(Model.Plugins.All<ShopRecord>(RecordTypes.Shop), npc.Entity) is not { } shop
                || !shop.Stock.Any(s => catalog[s.Item] is var item && item.Price * 3 / 2 <= gold && Outfitting.Gain(party, item) > Outfitting.Least))
                continue;
            state ??= PlanState.From(Model, Game);
            if (actions.FirstOrDefault(state.CanDo) is not { } action
                || StepTranslation.Translate(action, Model, Game, Graph) is not { Step: { } step, Unmet.Count: 0 })
                continue;
            if (Graph.Route(Game, here, id) is not { Route: { } route } || route.Length > Options.ShopReach * 1.5f)
                continue;
            _shopped[id] = party.Gold;
            var bought = Outfitting.Bought.Count;
            Outfitting.Shopping = true;
            try
            {
                new Pilot(Graph, PilotOptions.Default with { Rest = Resting, Triggers = Triggers, Dress = Outfitting, Waits = WaitsForTheHour, Shun = (Goal as CoverGoal)?.Shun })
                    .Run(Host, step with { Expect = [] }, Options.StepTicks);
            }
            finally
            {
                Outfitting.Shopping = false;
            }
            foreach (var b in Outfitting.Bought.Skip(bought))
                Report.Purchases.Add($"bought {b}");
            Shops++;
            Progress?.Invoke($"     shopped with {Name(npc.Entity)} at {id}: {Outfitting.Bought.Count - bought} bought");
            return;
        }
    }

    /// <summary>How many times a shop was visited.</summary>
    public int Shops { get; private set; }

    /// <summary>How many times a trainer was visited.</summary>
    public int Trainings { get; private set; }

    // Saves and the report.

    /// <summary>
    /// Keeps the game as it stands before a step, to write if the step has an entry (<see cref="ReportEntry.Save"/>): the player in
    /// trek with the controls, as a save from the menu is made.
    /// </summary>
    private void KeepSaveBefore()
    {
        _beforePath = null;
        _before = Host.PlayerHasControl && Host.Session.NotePlace() ? SaveFile.Serialize(Save()) : null;
    }

    private SaveGame Save()
    {
        Game.Label = null;
        return Game.Save();
    }

    /// <summary>The save before the step, written once its first entry needs it.</summary>
    private string? SaveBefore()
    {
        if (_beforePath is not null || _before is null || !Options.WriteSaves)
            return _beforePath;
        _beforePath = NextSavePath($"before-step-{_step}");
        File.WriteAllText(_beforePath, _before);
        return _beforePath;
    }

    /// <summary>With <see cref="RunOptions.SaveEveryStep"/>: the save before each step the player had the controls for, in order.</summary>
    public List<TrunkSave> StepSaves { get; } = [];

    /// <summary>
    /// With <see cref="RunOptions.SaveEveryStep"/> and a ledger on the host: every flag first seen, put down to the step it was seen in
    /// (what a trainer or shop visit after it sets included), with that step's save; step 0 is before the first step.
    /// </summary>
    public List<CoverSeen> StepSeen { get; } = [];

    private string? _stepSave;

    /// <summary>With <see cref="RunOptions.SaveEveryStep"/>, the save kept before this step written as the step's own, and listed.</summary>
    private void SaveStep(PlayerAction action)
    {
        _stepSave = null;
        if (!Options.SaveEveryStep || _before is null)
            return;
        var folder = Path.Combine(SavesFolder, "steps");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"t{_step:000}.sav");
        File.WriteAllText(path, _before);
        // An entry from this step names this save rather than writing another.
        _beforePath = _stepSave = path;
        var party = Game.Party.Members.Select(m => new MemberLevel(m.Entity.ToString(), m.Name, m.Level, m.SpendableExp)).ToList();
        StepSaves.Add(new TrunkSave(_step, action.Describe(), Host.Clock.Ticks, Spot.Of(Here()), party, path));
    }

    /// <summary>The flags a ledger saw first since the last step began, put down to that step (a cover run puts them down itself).</summary>
    private void TakeStepSeen()
    {
        if (!Options.SaveEveryStep || _cover is not null || Host.Ledger is not { } ledger)
            return;
        foreach (var h in ledger.TakeFresh())
            StepSeen.Add(new CoverSeen(Model.Flags.Id(h), _step, Host.Clock.Ticks, _step > 0 ? _stepSave : null));
    }

    private string NextSavePath(string slug)
    {
        Directory.CreateDirectory(SavesFolder);
        return Path.Combine(SavesFolder, $"{++_saves:00}-{Slug(slug)}.sav");
    }

    /// <summary>A file name's words: lower case, letters and digits, the rest dashes.</summary>
    public static string Slug(string text)
    {
        var slug = new StringBuilder();
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsAsciiLetterOrDigit(c))
                slug.Append(c);
            else if (slug.Length > 0 && slug[^1] != '-')
                slug.Append('-');
        }
        return slug.ToString().Trim('-') is { Length: > 0 } s ? s[..Math.Min(s.Length, 48)].TrimEnd('-') : "milestone";
    }

    /// <summary>A milestone: a save written (the player in trek), the party's levels, and the member goal woken.</summary>
    private void Milestone(MilestoneKind kind, string what)
    {
        _memberResting = false;
        _hopeless.Clear();
        string? path = null;
        if (Options.WriteSaves && Host.End == HostEnd.None && Host.PlayerHasControl && Host.Session.NotePlace())
        {
            path = NextSavePath($"{kind}-{what}");
            SaveFile.Write(path, Save());
        }
        var party = Game.Party.Members.Select(m => new MemberLevel(m.Entity.ToString(), m.Name, m.Level, m.SpendableExp)).ToList();
        var milestone = new Milestone(Report.Milestones.Count + 1, kind, what, _step, Host.Clock.Ticks, Spot.Of(Here()), path, party);
        Report.Milestones.Add(milestone);
        Progress?.Invoke($"     milestone {milestone}");
    }

    private void Entry(EntryKind kind, PlayerAction? action, string expected, string happened, Place? where = null, bool fromStep = true) =>
        Report.Entries.Add(new ReportEntry(kind, fromStep ? _step : 0, action?.Describe(), expected, happened, Host.Clock.Ticks,
            Spot.Of(where ?? Here()), fromStep ? SaveBefore() : null));

    private void Summarise(bool reached, string? why)
    {
        var battles = Host.BattlesFought;
        Report.Summary = new RunSummary(
            Goal.Describe(),
            reached,
            reached ? null : why ?? "the run stopped",
            _step,
            Host.Clock.Ticks - _startTicks,
            _wall.Elapsed.TotalSeconds,
            battles.Count,
            battles.Count(b => b.LostFirst),
            battles.Count(b => b.Stalemate),
            battles.Where(b => b.Outcome != Combat.BattleOutcome.Lost && !b.Undone).Sum(b => b.Deaths.Count),
            Trainings,
            Report.Entries.GroupBy(e => e.Kind.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()))
        {
            Camps = Resting.Camps,
            DiedFirst = battles.Count(b => b.DiedFirst),
        };
    }

    /// <summary>A line on how the run stands, for progress.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{_step} steps, {Host.Clock.Ticks - _startTicks} ticks, {_wall.Elapsed.TotalSeconds:0} s, {Report.Milestones.Count} milestones, {Report.Entries.Count} entries");
}
