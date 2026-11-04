using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Events;

/// <summary>
/// The core's named events (roadmap 2.10, ADR 0008): what <see cref="TriggerRecord.On"/> names. Each is fired where the
/// original's engine code does what the base plugin's triggers carry, and is public mod surface, the base game's too.
/// docs/systems/events.md says what each is about and where it's fired.
/// </summary>
public static class GameEvents
{
    /// <summary>A new game begins, before trek's first tick (<c>Title.Leave</c>'s New Game). About nothing.</summary>
    public const string GameStarted = "game.started";

    /// <summary>A cut-scene's script has ended, its done flag set and its stage gone (the cut-scene's end, <c>0x80051f4c</c>). About the script.</summary>
    public const string ScriptEnded = "script.ended";

    /// <summary>A cinematic has ended in the story, its done flag set (<c>Cinematic.EndCase</c>), but not from the Theatre. About the cinematic.</summary>
    public const string CinematicEnded = "cinematic.ended";

    /// <summary>Every event the core fires.</summary>
    public static IReadOnlyList<string> All { get; } = [GameStarted, ScriptEnded, CinematicEnded];
}

/// <summary>
/// The installed plugins' triggers, by event, and their firing: each whose event and subject match and whose conditions
/// hold does its ops in order, those on the game's state at once. An op that takes over from trek (a cinematic, a
/// script, the credits, the game over) ends the firing and is handed back for <see cref="GameSession"/> to carry out.
/// Conditions and ops run as a script's do (<see cref="ScriptOps"/>, ADR 0034), but with no map object to keep a skill check.
/// </summary>
public sealed class Triggers
{
    private readonly ILookup<string, TriggerRecord> _byEvent;
    private readonly List<string> _problems = [];

    public Triggers(PluginSet plugins)
        : this(plugins.All<TriggerRecord>(RecordTypes.Trigger))
    {
    }

    /// <param name="triggers">The triggers, in the plugins' order.</param>
    public Triggers(IEnumerable<TriggerRecord> triggers)
    {
        var all = triggers.ToList();
        foreach (var trigger in all)
        {
            if (!GameEvents.All.Contains(trigger.On))
                _problems.Add($"{trigger.Id} is on {trigger.On}, which the core never fires.");
            foreach (var condition in trigger.If ?? [])
            {
                if (condition is not ScriptOp.Check)
                    _problems.Add($"{trigger.Id} has a condition that isn't a check, which never holds.");
            }
            foreach (var op in trigger.Do)
            {
                if (op.Use == OpUse.None)
                    _problems.Add($"{trigger.Id} has an op of a type this build doesn't know, which does nothing.");
                else if (op is ScriptOp.Lua)
                    _problems.Add($"{trigger.Id} has a lua op, which does nothing until Lua comes.");
                else if ((op.Use & OpUse.Trigger) == 0)
                    _problems.Add($"{trigger.Id} has {op.Name}, which a trigger can't run: it needs a stage, an actor or a running script.");
            }
        }
        _byEvent = all.ToLookup(t => t.On);
    }

    /// <summary>What's wrong with the installed triggers: an event never fired, an op no build knows or a trigger can't run.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The triggers on an event, in the plugins' order.</summary>
    public IEnumerable<TriggerRecord> On(string name) => _byEvent[name];

    /// <summary>
    /// Fires an event about <paramref name="subject"/>: every trigger on it, about that subject or any, whose conditions hold,
    /// does its ops in turn, until one takes over from trek, which is returned and ends the firing.
    /// </summary>
    /// <param name="warn">Where an op that can't be carried out says so.</param>
    /// <param name="host">Where the ops' sounds go, as a script's do: the gold's and the experience's. None plays nothing.</param>
    public ScriptOp? Fire(GameState game, string name, RecordId? subject, Action<string> warn, IScriptHost? host = null)
    {
        // Skill checks roll from the world stream as a script's do, and honour the random flag, but none is kept.
        var ops = new ScriptOps(game.Flags, game, game.Random.World, game.StateFlagsRecord.RandomSkillChecks, host);
        foreach (var trigger in _byEvent[name])
        {
            if (trigger.Subject is { } about && about != subject)
                continue;
            if (trigger.If is { } conditions && !conditions.All(c => Holds(ops, c)))
                continue;
            foreach (var op in trigger.Do)
            {
                if (TakesOver(op))
                    return op;
                Apply(game, ops, trigger, op, warn);
            }
        }
        return null;
    }

    /// <summary>Whether an op takes over from trek, so <see cref="GameSession"/> carries it out.</summary>
    public static bool TakesOver(ScriptOp op) =>
        op is ScriptOp.CinematicPlay or ScriptOp.StartScript or ScriptOp.CreditsRoll or ScriptOp.GameOver;

    /// <summary>
    /// Evaluates a condition as a script's If or Case does (<see cref="ScriptOps.Condition"/>): it holds when the value is in
    /// its range, or exactly 1. Anything but a check never holds.
    /// </summary>
    private static bool Holds(ScriptOps ops, ScriptOp condition)
    {
        if (condition is not ScriptOp.Check check)
            return false;
        var value = ops.Condition(check);
        return check.Range is { } range ? range.Holds(value) : value == 1;
    }

    /// <summary>
    /// Does an op on the game's state: one a node's effect runs (<see cref="ScriptOps.Effect"/>), or a True Name, which the
    /// member's sheet keeps and a save with it.
    /// </summary>
    private static void Apply(GameState game, ScriptOps ops, TriggerRecord trigger, ScriptOp op, Action<string> warn)
    {
        switch (op)
        {
            case var _ when ScriptOps.ChangesState(op):
                ops.Effect(op);
                break;
            case ScriptOp.PartyLearnTrueName name:
                if (game.Party.Get(name.Member) is { } character)
                    character.TrueName = true;
                break;
            case ScriptOp.Lua:
                break;
            default:
                warn($"{trigger.Id}: {op.Name ?? "an op this build doesn't know"} isn't an op a trigger can run.");
                break;
        }
    }
}
