using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Scripts;

/// <summary>
/// Borg13's condition ops, and the effect ops that change the game's state, against the flags and the world: what a script's
/// nodes run (<see cref="ScriptRunner"/>, <c>DialogEvalCallback</c> and <c>DialogCallbackC</c>) and the plugins' triggers run
/// the same way (<see cref="Events.Triggers"/>). What differs between them is given: the speaker, whether skill checks are
/// kept, the map object that remembers them, and where the sounds go.
/// </summary>
public sealed class ScriptOps
{
    /// <summary>The party skill the original doesn't remember per map object (<c>SKILL_Troubador</c>).</summary>
    private const int Troubadour = 9;

    private readonly FlagNetwork _flags;
    private readonly IScriptWorld _world;
    private readonly IRandom _random;
    private readonly int? _randomSkillChecks;
    private readonly IScriptHost? _host;
    private readonly IDictionary<int, int>? _keptChecks;
    private readonly RecordId? _mapObject;

    /// <param name="random">The stream skill checks roll from.</param>
    /// <param name="randomSkillChecks">While this flag is set, skill checks roll 0–21 (<see cref="StateFlagsRecord.RandomSkillChecks"/>).</param>
    /// <param name="host">Where the gold and experience sounds go, or nowhere.</param>
    /// <param name="keptChecks">
    /// The skill checks kept so far, by skill: a script keeps its first check of each for the rest of it. Without it, as for a
    /// trigger, every check rolls.
    /// </param>
    /// <param name="mapObject">The map object that remembers skill checks, which started the script. A trigger has none.</param>
    public ScriptOps(FlagNetwork flags, IScriptWorld world, IRandom random, RecordId? randomSkillChecks = null, IScriptHost? host = null,
        IDictionary<int, int>? keptChecks = null, RecordId? mapObject = null)
    {
        _flags = flags;
        _world = world;
        _random = random;
        _randomSkillChecks = randomSkillChecks is { } flag ? flags.Handle(flag) : null;
        _host = host;
        _keptChecks = keptChecks;
        _mapObject = mapObject;
    }

    /// <summary>The effect ops this runs: those that change the game's state, with no actor, camera or ending in them.</summary>
    public static bool ChangesState(DialogueCommand command) => command is DialogueCommand.SetFlag or DialogueCommand.ClearFlag
        or DialogueCommand.AddGold or DialogueCommand.TakeGold or DialogueCommand.AddItem or DialogueCommand.TakeItem
        or DialogueCommand.AddMember or DialogueCommand.RemoveMember or DialogueCommand.AddExp;

    /// <summary>A condition op's value, as a u16 (<c>DialogEvalCallback</c>); 0 for an op that isn't one.</summary>
    /// <param name="speaker">Whose stat <see cref="DialogueCommand.CheckSpeakerStat"/> reads.</param>
    public int Condition(DialogueCommand command, RecordId? target, int value, RecordId? speaker)
    {
        var result = command switch
        {
            DialogueCommand.CheckMember => target is { } member && _world.InParty(member) ? 1 : 0,
            DialogueCommand.CheckFlag => target is { } flag && _flags.IsSet(flag) ? 1 : 0,
            DialogueCommand.CheckSkill => SkillCheck(value),
            DialogueCommand.CheckItem => target is { } item && _world.HasItem(item) ? 1 : 0,
            DialogueCommand.CheckBestStat => _world.BestStat(value),
            DialogueCommand.CheckWorstStat => _world.WorstStat(value),
            DialogueCommand.CheckSpeakerStat => _world.MemberStat(speaker, value),
            DialogueCommand.CheckPartySize => _world.PartySize,
            DialogueCommand.CheckGold => (int)_world.Gold,
            _ => 0,
        };
        return result & 0xFFFF;
    }

    /// <summary>
    /// The party's check of a skill (<c>dialougSkillCheck</c>). The first check is kept, where checks are, and the map object
    /// remembers it, except for Troubadour. While the random flag is set, the result is a roll instead, which isn't kept.
    /// </summary>
    public int SkillCheck(int skill)
    {
        if (_keptChecks is null || !_keptChecks.TryGetValue(skill, out var level))
        {
            level = _world.SkillCheck(skill, _random) & 0xFF;
            if (skill != Troubadour && _mapObject is { } source)
            {
                var memory = new Memory(source, MemoryKind.SkillCheck, skill);
                if (_world.TryRecall(memory, out var recalled))
                    level = recalled;
                else
                    _world.Remember(memory, level);
            }
            if (_keptChecks is not null)
                _keptChecks[skill] = level;
        }
        return _randomSkillChecks is { } flag && _flags.Get(flag) ? _random.Range(0, 21) : level;
    }

    /// <summary>
    /// Runs an effect op that changes the game's state (<see cref="ChangesState"/>, <c>DialogCallbackC</c>), with its sound.
    /// False where it does nothing: an op this doesn't run, taking more gold than the party has or an item it lacks, adding a
    /// member to a full party, experience for someone not in it.
    /// </summary>
    /// <param name="member">Who <see cref="DialogueCommand.AddExp"/> gives to: a script's speaker.</param>
    public bool Effect(DialogueCommand command, RecordId? target, int value, RecordId? member)
    {
        switch (command)
        {
            case DialogueCommand.SetFlag or DialogueCommand.ClearFlag:
                return target is { } flag && _flags.TrySet(flag, command == DialogueCommand.SetFlag);
            case DialogueCommand.AddGold:
                _world.Gold += (uint)(value & 0xFFFF);
                _host?.Cue(new SoundCue(ScriptSound.GoldGained));
                return true;
            case DialogueCommand.TakeGold:
                if (_world.Gold < (uint)(value & 0xFFFF))
                    return false;
                _world.Gold -= (uint)(value & 0xFFFF);
                _host?.Cue(new SoundCue(ScriptSound.GoldPaid));
                return true;
            case DialogueCommand.AddItem when target is { } item:
                _world.AddItem(item);
                return true;
            case DialogueCommand.TakeItem:
                return target is { } taken && _world.TakeItem(taken);
            case DialogueCommand.AddMember:
                return target is { } joining && _world.AddMember(joining);
            case DialogueCommand.RemoveMember when target is { } leaving:
                _world.RemoveMember(leaving);
                return true;
            case DialogueCommand.AddExp when member is { } given && _world.AddExp(given, value):
                _host?.Cue(new SoundCue(ScriptSound.Experience));
                return true;
            default:
                return false;
        }
    }
}
