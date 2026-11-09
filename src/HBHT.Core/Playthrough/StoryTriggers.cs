using System.Numerics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// A story trigger: a tangible script object, whose script plays as the player walks within its radius, wherever he was going.
/// </summary>
/// <param name="Id">Its map object.</param>
/// <param name="Set">Its map set.</param>
/// <param name="At">Its middle, in the map set's world coordinates.</param>
/// <param name="Ways">Its script's ways (<see cref="QuestModel.Ways"/>), which its <see cref="Blocking"/> numbers.</param>
/// <param name="Plays">Its play actions (<see cref="ActionKind.Play"/>), each with the way it takes.</param>
public sealed record StoryTrigger(RecordId Id, ScriptObject Object, RecordId Set, Vector3 At, IReadOnlyList<ScriptWay> Ways,
    IReadOnlyList<PlayerAction> Plays)
{
    /// <summary>The ways seen to turn the player back: each played twice on one step, so he never got past it.</summary>
    public HashSet<int> Blocking { get; } = [];

    /// <summary>
    /// The ways that send the player elsewhere: each opens a tangible teleporter about the trigger, so the scene ends with him taken
    /// through it (the dryads' 0x0531 sends him to their queen, or to the Gorgon's cave). Whether that turns him back depends on where
    /// he was going, which the planner doesn't know, so it is information, not a gate.
    /// </summary>
    public HashSet<int> Sends { get; } = [];

    /// <summary>The literals of its object's own flag gates.</summary>
    public IEnumerable<FlagIs> Gates => Plays.Count > 0 ? Plays[0].Presence.OfType<FlagIs>() : [];
}

/// <summary>
/// The story triggers a route may walk into (docs/systems/playthrough.md, "Story triggers"), and which of their ways turn the player
/// back: learned where the pilot sees the same way play twice on one step. While a trigger would play such a way, a route through it
/// doesn't get past it: the run first makes it let him by (<see cref="PassGoal"/>), by closing its gates or making it play another way.
/// </summary>
public sealed class StoryTriggers
{
    private readonly QuestModel _model;
    private readonly Dictionary<RecordId, StoryTrigger> _triggers = [];

    public StoryTriggers(QuestModel model)
    {
        _model = model;
        var plugins = model.Plugins;
        var plays = model.Actions.Where(a => a.Kind == ActionKind.Play && a.Place is not null).GroupBy(a => a.Place!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());
        var teleporters = new List<(RecordId Set, Vector3 At, TeleporterObject Door)>();
        var triggers = new List<StoryTrigger>();
        foreach (var chunk in plugins.All<ChunkRecord>(RecordTypes.Chunk))
        {
            var origin = plugins.Get<MapSetRecord>(chunk.MapSet).Origin(chunk);
            foreach (var o in chunk.Objects)
            {
                if (o is TeleporterObject { Tangible: true } door)
                    teleporters.Add((chunk.MapSet, origin + o.Position, door));
                if (o is not ScriptObject { Tangible: true } play || !model.Ways.TryGetValue(play.Plays, out var ways))
                    continue;
                var id = chunk.ObjectId(o.Index);
                triggers.Add(new StoryTrigger(id, play, chunk.MapSet, origin + o.Position, ways.Ways, plays.GetValueOrDefault(id) ?? []));
            }
        }
        foreach (var trigger in triggers)
        {
            var near = teleporters.Where(t => t.Set == trigger.Set && Vector3.Distance(t.At, trigger.At) <= t.Door.Radius + trigger.Object.Radius)
                .Select(t => t.Door).ToList();
            for (var w = 0; w < trigger.Ways.Count; w++)
            {
                if (near.Any(door => Opens(trigger.Ways[w].Effects, door)))
                    trigger.Sends.Add(w);
            }
            _triggers[trigger.Id] = trigger;
        }
    }

    /// <summary>Every story trigger, by its map object.</summary>
    public IReadOnlyDictionary<RecordId, StoryTrigger> All => _triggers;

    public StoryTrigger? Get(RecordId id) => _triggers.GetValueOrDefault(id);

    /// <summary>Whether a way's effects open a door's gates: the last word on a gate's flag is the value it wants, where it wasn't before.</summary>
    private static bool Opens(IReadOnlyList<Effect> effects, TeleporterObject door)
    {
        var opened = false;
        foreach (var gate in new[] { door.Visible, door.Enabled })
        {
            if (gate is not { } g)
                continue;
            if (effects.OfType<SetFlag>().LastOrDefault(s => s.Flag == g.Flag) is not { } last)
                continue;
            if (last.Value == g.Invert)
                return false;
            opened = true;
        }
        return opened;
    }

    /// <summary>The ways a trigger would play in a state: those whose script's conditions hold there (code gates taken to).</summary>
    public IEnumerable<int> WaysIn(StoryTrigger trigger, PlanState state)
    {
        for (var w = 0; w < trigger.Ways.Count; w++)
        {
            if (trigger.Ways[w].Requires.All(r => r is CodeGate or Reaches || state.Holds(r)))
                yield return w;
        }
    }

    /// <summary>Whether the trigger is there and working in a state: its object's flag gates hold.</summary>
    public static bool Working(StoryTrigger trigger, PlanState state) => trigger.Gates.All(state.Holds);

    /// <summary>Whether a trigger turns the player back in a state: it's working, and a way it would play there does.</summary>
    public bool Blocks(StoryTrigger trigger, PlanState state) =>
        trigger.Blocking.Count > 0 && Working(trigger, state) && WaysIn(trigger, state).Any(trigger.Blocking.Contains);

    /// <summary>Whether a trigger turns the player back as the game stands.</summary>
    public bool Blocks(RecordId id, GameState game) =>
        Get(id) is { Blocking.Count: > 0 } trigger && Blocks(trigger, PlanState.From(_model, game));

    /// <summary>The ways a trigger would play as the game stands.</summary>
    public IReadOnlyList<int> WaysNow(RecordId id, GameState game) =>
        Get(id) is { } trigger ? [.. WaysIn(trigger, PlanState.From(_model, game))] : [];

    /// <summary>Notes ways seen to turn the player back. Whether any was new.</summary>
    public bool Learn(RecordId id, IEnumerable<int> ways)
    {
        if (Get(id) is not { } trigger)
            return false;
        var added = false;
        foreach (var w in ways)
            added |= trigger.Blocking.Add(w);
        return added;
    }

    /// <summary>
    /// The ways a trigger can be made to let the player by, each as the facts that must all come true: one of its gates closed, or the
    /// conditions a way that doesn't turn him back needs that don't hold yet. A way some of whose conditions aren't facts is left out.
    /// </summary>
    public IEnumerable<IReadOnlyList<int>> PassOptions(StoryTrigger trigger, PlanState state)
    {
        var seen = new HashSet<string>();
        foreach (var gate in trigger.Gates)
        {
            if (_model.Fact(gate with { Set = !gate.Set }) is { } f && seen.Add($"{f}"))
                yield return [f];
        }
        for (var w = 0; w < trigger.Ways.Count; w++)
        {
            if (trigger.Blocking.Contains(w))
                continue;
            var facts = new List<int>();
            var whole = true;
            foreach (var literal in trigger.Ways[w].Requires)
            {
                if (literal is CodeGate or Reaches || state.Holds(literal))
                    continue;
                if (_model.Fact(literal) is { } f)
                    facts.Add(f);
                else
                    whole = false;
            }
            if (whole && facts.Count > 0 && seen.Add(string.Join(",", facts.Order())))
                yield return facts;
        }
    }

    /// <summary>The trigger's own plays that turn the player back: never a step towards getting past it.</summary>
    public IEnumerable<int> BlockingPlays(StoryTrigger trigger) =>
        trigger.Plays.Where(a => a.Way is { } way && trigger.Blocking.Contains(IndexOf(trigger, way))).Select(a => a.Index);

    /// <summary>A way's number among its trigger's, by reference: -1 if it isn't one.</summary>
    public static int IndexOf(StoryTrigger trigger, ScriptWay way)
    {
        for (var w = 0; w < trigger.Ways.Count; w++)
        {
            if (ReferenceEquals(trigger.Ways[w], way))
                return w;
        }
        return -1;
    }
}
