using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Events;

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
/// hold does its effects in order, those on the game's state at once. An effect that takes over from trek (a cinematic, a
/// script, the credits, the game over) ends the firing and is handed back for <see cref="GameSession"/> to carry out.
/// Conditions and ops run as a script's do (<see cref="ScriptOps"/>), but with no map object to keep a skill check.
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
            foreach (var effect in trigger.Do.Where(e => e.GetType() == typeof(TriggerEffect)))
                _problems.Add($"{trigger.Id} has an effect of a type this build doesn't know, which does nothing.");
        }
        _byEvent = all.ToLookup(t => t.On);
    }

    /// <summary>What's wrong with the installed triggers: an event never fired, an effect no build knows.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The triggers on an event, in the plugins' order.</summary>
    public IEnumerable<TriggerRecord> On(string name) => _byEvent[name];

    /// <summary>
    /// Fires an event about <paramref name="subject"/>: every trigger on it, about that subject or any, whose conditions hold,
    /// does its effects in turn, until one takes over from trek, which is returned and ends the firing.
    /// </summary>
    /// <param name="warn">Where an effect that can't be carried out says so.</param>
    /// <param name="host">Where the ops' sounds go, as a script's do: the gold's and the experience's. None plays nothing.</param>
    public TriggerEffect? Fire(GameState game, string name, RecordId? subject, Action<string> warn, IScriptHost? host = null)
    {
        // Skill checks roll from the world stream as a script's do, and honour the random flag, but none is kept.
        var ops = new ScriptOps(game.Flags, game, game.Random.World, game.StateFlagsRecord.RandomSkillChecks, host);
        foreach (var trigger in _byEvent[name])
        {
            if (trigger.Subject is { } about && about != subject)
                continue;
            if (trigger.If is { } conditions && !conditions.All(c => Holds(ops, c)))
                continue;
            foreach (var effect in trigger.Do)
            {
                if (TakesOver(effect))
                    return effect;
                Apply(game, ops, trigger, effect, warn);
            }
        }
        return null;
    }

    /// <summary>Whether an effect takes over from trek, so <see cref="GameSession"/> carries it out.</summary>
    public static bool TakesOver(TriggerEffect effect) =>
        effect is TriggerEffect.Cinematic or TriggerEffect.Script or TriggerEffect.Credits or TriggerEffect.GameOver;

    /// <summary>
    /// Evaluates a condition as a script's If or Case does (<see cref="ScriptOps.Condition"/>), its target the speaker for
    /// <see cref="DialogueCommand.CheckSpeakerStat"/>: it holds when the value is in its range, or exactly 1.
    /// </summary>
    private static bool Holds(ScriptOps ops, TriggerCondition condition)
    {
        var value = ops.Condition(condition.Command, condition.Target, condition.Value ?? 0, speaker: condition.Target);
        return condition.Range is { } range ? range.Holds(value) : value == 1;
    }

    /// <summary>
    /// Does an effect on the game's state: a Borg13 op as a node runs it (<see cref="ScriptOps.Effect"/>), its target the
    /// member for <see cref="DialogueCommand.AddExp"/>, or a True Name, which the member's sheet keeps and a save with it.
    /// </summary>
    private static void Apply(GameState game, ScriptOps ops, TriggerRecord trigger, TriggerEffect effect, Action<string> warn)
    {
        switch (effect)
        {
            case TriggerEffect.Op op when ScriptOps.ChangesState(op.Command)
                && (op.Target is not null || op.Command is DialogueCommand.AddGold or DialogueCommand.TakeGold):
                ops.Effect(op.Command, op.Target, op.Value ?? 0, member: op.Target);
                break;
            case TriggerEffect.Op op:
                warn($"{trigger.Id}: {op.Command} isn't an op a trigger can run.");
                break;
            case TriggerEffect.TrueName name:
                if (game.Party.Get(name.Member) is { } character)
                    character.TrueName = true;
                break;
            default:
                warn($"{trigger.Id} has an effect this build doesn't know.");
                break;
        }
    }
}
