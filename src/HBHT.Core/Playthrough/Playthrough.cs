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
/// <param name="MenuPatience">How long the pause menu may stay open: long, for spending a battle's experience.</param>
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
    int MenuPatience = 60 * 60 * 10)
{
    public static readonly RunOptions Default = new();
}

/// <summary>What one step of the run came to.</summary>
/// <param name="Done">The step did what it was expected to: played out, its expectations held.</param>
/// <param name="Outcome">The pilot's outcome, or null for an event or a step not played.</param>
/// <param name="Translated">The step as the pilot was given it.</param>
public sealed record RunStep(int Number, Goal Goal, PlayerAction Action, bool Done, string Summary, StepOutcome? Outcome, TranslatedStep Translated);

/// <summary>
/// The playthrough bot's run (docs/systems/playthrough.md, layer 6): it plays from a host's game to a goal. Each step it plans from the
/// real state (<see cref="PlanState.From"/>), with the world graph behind the planner's places from where the player stands
/// (<see cref="WorldReach"/>), takes the plan's first step, plays it with the pilot (<see cref="StepTranslation"/>, <see cref="Pilot"/>),
/// and compares what the game did with what the step expected. A step that fails is reported and its action banned in that state, and
/// it plans again. The member goal comes first while the party has room and the planner has a way to it; battles are the
/// <see cref="BattlePilot"/>'s, and experience is spent by <see cref="Playthrough.Levelling"/> after battles and at trainers near the way.
/// At each milestone it writes a save, and it keeps the report (<see cref="RunReport"/>).
/// </summary>
public sealed class Playthrough
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
    private int _joined;
    private long _startTicks;

    /// <param name="host">The game to play, a new game's or a save's.</param>
    /// <param name="model">The quest model over the same plugins.</param>
    /// <param name="graph">The world graph over the same plugins.</param>
    /// <param name="goal">What the run is after.</param>
    public Playthrough(PlaythroughHost host, QuestModel model, WorldGraph graph, Goal goal, RunOptions? options = null)
    {
        Host = host;
        Model = model;
        Graph = graph;
        Goal = goal;
        Options = options ?? RunOptions.Default;
        _planner = new QuestPlanner(model);
        Battles = new BattlePilot();
        Levelling = new Levelling { GoldReserve = Options.GoldReserve };
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
    }

    public PlaythroughHost Host { get; }

    public QuestModel Model { get; }

    public WorldGraph Graph { get; }

    /// <summary>The run's goal, at the bottom of its stack.</summary>
    public Goal Goal { get; }

    public RunOptions Options { get; }

    public BattlePilot Battles { get; }

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
                if (steps.Count == 0)
                {
                    var stuck = Stuck(state, Goal, reach);
                    why = $"no step towards {Goal}: {plan?.Why ?? "the planner found none"}{(stuck.Length > 0 ? $"; {stuck}" : "")}";
                    Entry(EntryKind.Unreachable, null, Goal.Describe(), why, fromStep: false);
                    break;
                }
                var step = Play(steps[0], goal, state, reach);
                if (step.Done)
                {
                    failures[goal] = 0;
                    _rest[goal] = steps.Skip(1).ToList();
                    VisitTrainer();
                }
                else
                {
                    _rest.Remove(goal);
                    failures[goal] = failures.GetValueOrDefault(goal) + 1;
                    if (failures[goal] >= Options.MostFailures)
                    {
                        if (goal is MemberGoal && Goal is not MemberGoal)
                        {
                            Rest($"{Options.MostFailures} steps towards it failed in a row");
                            failures[goal] = 0;
                        }
                        else
                        {
                            why = $"{Options.MostFailures} steps in a row failed";
                        }
                    }
                }
            }
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
        if (Goal is not MemberGoal && !_memberResting && Game.Party.HasEmptySlot())
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
        if (relaxation.Extract(relaxation.Run(state), facts, goal is MemberGoal) is not { } relaxed)
            return "";
        var lines = relaxed.Helpful.Take(5).Select(a => Model.Actions[a]).Select(a =>
            a.Presence.OfType<Reaches>().FirstOrDefault(r => reach.OffFoot(r.Place)) is { } far
                ? $"{a.Describe()} is at a place no way on foot reaches from here ({far.Place})"
                : $"{a.Describe()} needs {state.Blocker(a)?.Describe() ?? "nothing, but the search didn't take it"}");
        return $"the relaxed plan's next steps: {string.Join("; ", lines)}";
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
        var number = ++_step;
        var clock = Stopwatch.StartNew();
        var startTick = Host.Clock.Ticks;
        KeepSaveBefore();
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
            var pilot = new Pilot(Graph, PilotOptions.Default with { PickLocks = translated.PickLocks, Rest = Resting });
            outcome = pilot.Run(Host, pilotStep, Math.Min(Options.StepTicks, Math.Max(1, Options.MostTicks - (Host.Clock.Ticks - _startTicks))));
            var expect = pilotStep.Expect ?? [];
            // Done by the way: what it was for came about though the pilot didn't use it (an encounter caught the party first).
            var byTheWay = !outcome.Done && expect.Count > 0 && outcome.Misses.Count == 0;
            done = (outcome.Done || byTheWay) && outcome.Misses.Count == 0;
            summary = outcome.Done ? (outcome.Misses.Count == 0 ? "done" : $"missed {string.Join(", ", outcome.Misses)}")
                : byTheWay ? $"{outcome.Failure}, but what it was for came about" : $"{outcome.Failure}: {outcome.Reason}";
            Compare(action, outcome, translated, byTheWay);
        }
        if (!done)
            Ban(action, state);
        AfterStep(action, before, startTick);
        var line = new StepLine(number, goal.Describe(), action.Describe(), summary, Host.Clock.Ticks - startTick, clock.Elapsed.TotalSeconds, startTick);
        Report.Steps.Add(line);
        Progress?.Invoke($"{number,4} {Host.Clock.Ticks,10} [{goal}] {action.Describe()}: {summary}");
        var step = new RunStep(number, goal, action, done, summary, outcome, translated);
        Played.Add(step);
        return step;
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
        int Problems, int Purchases)
    {
        public static Before Take(Playthrough run)
        {
            var game = run.Game;
            return new Before(
                [.. game.Party.Members.Select(m => m.Entity)],
                [.. run._journal.Where(game.Flags.IsSet)],
                [.. run.Model.Members.Where(m => game.Flags.IsSet(m.Value.Died)).Select(m => m.Key)],
                run.Host.Transitions.Count, run.Host.BattlesFought.Count, run.Host.Notes.Count, run.Battles.Problems.Count,
                run.Levelling.Bought.Count);
        }
    }

    /// <summary>After a step: the battles' and the host's entries, and the milestones it came to, each with a save.</summary>
    private void AfterStep(PlayerAction? action, Before before, long startTick)
    {
        var game = Game;
        foreach (var fought in Host.BattlesFought.Skip(before.Battles))
        {
            var name = fought.Setup.Party?.ToString() ?? fought.Setup.Source.ToString();
            if (fought.LostFirst)
                Entry(EntryKind.LostFirst, action, $"the party wins against {name}", "lost; played again from its start and won outright");
            if (fought.Stalemate)
                Entry(EntryKind.Stalemate, action, $"the battle against {name} ends", "nobody could end it; won outright");
            // A battle lost is played again from its start, which undoes its deaths: the replay's are the ones that stand.
            if (fought.Outcome == Combat.BattleOutcome.Lost)
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

        var milestones = new List<(MilestoneKind, string)>();
        foreach (var joined in game.Party.Members.Select(m => m.Entity).Where(m => !before.Members.Contains(m)))
        {
            _joined++;
            milestones.Add((MilestoneKind.Joined, $"{Name(joined)} joined"));
        }
        var died = Model.Members.Where(m => game.Flags.IsSet(m.Value.Died) && !before.Died.Contains(m.Key)).Select(m => m.Key)
            .Union(Host.BattlesFought.Skip(before.Battles).Where(b => b.Outcome != Combat.BattleOutcome.Lost).SelectMany(b => b.Deaths)).Distinct();
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
            if (Model.Plugins.TryGet<EntityRecord>(entity) is not { } record)
                continue;
            var trainer = Trainer.From(record, Game.Rules, Model.Plugins.Get<SpellRecord>);
            if (!party.Members.Any(m => Builds.Next(m, Levelling.Builds, trainer, gold) is not null))
                continue;
            if (Graph.Route(Game, here, id) is not { Route: { } route } || route.Length > Options.TrainerReach * 1.5f)
                continue;
            var walk = new Pilot(Graph, PilotOptions.Default with { Rest = Resting }).Run(Host, new PilotStep(id, UseKind.Arrive), Options.StepTicks);
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
            battles.Where(b => b.Outcome != Combat.BattleOutcome.Lost).Sum(b => b.Deaths.Count),
            Trainings,
            Report.Entries.GroupBy(e => e.Kind.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()))
        {
            Camps = Resting.Camps,
        };
    }

    /// <summary>A line on how the run stands, for progress.</summary>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{_step} steps, {Host.Clock.Ticks - _startTicks} ticks, {_wall.Elapsed.TotalSeconds:0} s, {Report.Milestones.Count} milestones, {Report.Entries.Count} entries");
}
