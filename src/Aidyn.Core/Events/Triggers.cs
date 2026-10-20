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
/// </summary>
public sealed class Triggers
{
    private readonly ILookup<string, TriggerRecord> _byEvent;
    private readonly List<string> _problems = [];

    public Triggers(PluginSet plugins)
    {
        var all = plugins.All<TriggerRecord>(RecordTypes.Trigger);
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
    public TriggerEffect? Fire(GameState game, string name, RecordId? subject, Action<string> warn)
    {
        foreach (var trigger in _byEvent[name])
        {
            if (trigger.Subject is { } about && about != subject)
                continue;
            if (trigger.If is { } conditions && !conditions.All(c => Holds(game, c)))
                continue;
            foreach (var effect in trigger.Do)
            {
                if (TakesOver(effect))
                    return effect;
                Apply(game, trigger, effect, warn);
            }
        }
        return null;
    }

    /// <summary>Whether an effect takes over from trek, so <see cref="GameSession"/> carries it out.</summary>
    public static bool TakesOver(TriggerEffect effect) =>
        effect is TriggerEffect.Cinematic or TriggerEffect.Script or TriggerEffect.Credits or TriggerEffect.GameOver;

    /// <summary>
    /// Evaluates a condition as a script's If or Case does (<see cref="ScriptRunner"/>'s condition ops), against the game:
    /// it holds when the value is in its range, or exactly 1.
    /// </summary>
    public static bool Holds(GameState game, TriggerCondition condition)
    {
        IScriptWorld world = game;
        var value = condition.Command switch
        {
            DialogueCommand.CheckMember => condition.Target is { } member && world.InParty(member) ? 1 : 0,
            DialogueCommand.CheckFlag => condition.Target is { } flag && game.Flags.IsSet(flag) ? 1 : 0,
            DialogueCommand.CheckSkill => world.SkillCheck(condition.Value ?? 0, game.Random.World) & 0xFF,
            DialogueCommand.CheckItem => condition.Target is { } item && world.HasItem(item) ? 1 : 0,
            DialogueCommand.CheckBestStat => world.BestStat(condition.Value ?? 0),
            DialogueCommand.CheckWorstStat => world.WorstStat(condition.Value ?? 0),
            DialogueCommand.CheckSpeakerStat => world.MemberStat(condition.Target, condition.Value ?? 0),
            DialogueCommand.CheckPartySize => world.PartySize,
            DialogueCommand.CheckGold => (int)world.Gold,
            _ => 0,
        } & 0xFFFF;
        return condition.Range is { } range ? range.Holds(value) : value == 1;
    }

    /// <summary>Does an effect on the game's state: a Borg13 op, or a True Name.</summary>
    private static void Apply(GameState game, TriggerRecord trigger, TriggerEffect effect, Action<string> warn)
    {
        IScriptWorld world = game;
        switch (effect)
        {
            case TriggerEffect.Op { Command: DialogueCommand.SetFlag or DialogueCommand.ClearFlag, Target: { } flag } op:
                game.Flags.TrySet(flag, op.Command == DialogueCommand.SetFlag);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.AddGold } op:
                world.Gold += (uint)((op.Value ?? 0) & 0xFFFF);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.TakeGold } op when world.Gold >= (uint)((op.Value ?? 0) & 0xFFFF):
                world.Gold -= (uint)((op.Value ?? 0) & 0xFFFF);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.TakeGold }:
                break;
            case TriggerEffect.Op { Command: DialogueCommand.AddItem, Target: { } item }:
                world.AddItem(item);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.TakeItem, Target: { } item }:
                world.TakeItem(item);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.AddMember, Target: { } member }:
                world.AddMember(member);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.RemoveMember, Target: { } member }:
                world.RemoveMember(member);
                break;
            case TriggerEffect.Op { Command: DialogueCommand.AddExp, Target: { } member } op:
                world.AddExp(member, op.Value ?? 0);
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
