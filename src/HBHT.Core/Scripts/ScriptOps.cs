using HBHT.Core.Characters;
using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Scripts;

/// <summary>
/// The condition ops, and the effect ops that change the game's state, against the flags and the world: what a script's
/// nodes run (<see cref="ScriptRunner"/>, <c>DialogEvalCallback</c> and <c>DialogCallbackC</c>) and the plugins' triggers run
/// the same way (<see cref="Events.Triggers"/>). What differs between them is given: whether skill checks are kept, the map
/// object that remembers them, and where the sounds go.
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
    public static bool ChangesState(ScriptOp op) => op is ScriptOp.FlagSet or ScriptOp.FlagClear or ScriptOp.PartyAddGold
        or ScriptOp.PartyTakeGold or ScriptOp.PartyAddItem or ScriptOp.PartyTakeItem or ScriptOp.PartyAddMember
        or ScriptOp.PartyRemoveMember or ScriptOp.PartyAddExp;

    /// <summary>A condition op's value, as a u16 (<c>DialogEvalCallback</c>); 0 for an op this build doesn't know.</summary>
    public int Condition(ScriptOp.Check check)
    {
        var result = check switch
        {
            ScriptOp.CheckMember member => _world.InParty(member.Entity) ? 1 : 0,
            ScriptOp.CheckFlag flag => _flags.IsSet(flag.Flag) ? 1 : 0,
            // A mod's skill isn't one the party holds (ADR 0032): the check is 0.
            ScriptOp.CheckSkill skill => SkillId.TryFromRecord(skill.Skill) is { IsSkill: true } id ? SkillCheck(id.Slot) : 0,
            ScriptOp.CheckItem item => _world.HasItem(item.Item) ? 1 : 0,
            ScriptOp.CheckBestStat best => StatNumber(best.Stat) is { } stat ? _world.BestStat(stat) : 0,
            ScriptOp.CheckWorstStat worst => StatNumber(worst.Stat) is { } stat ? _world.WorstStat(stat) : 0,
            ScriptOp.CheckStat of => StatNumber(of.Stat) is { } stat ? _world.MemberStat(of.Member, stat) : 0,
            ScriptOp.CheckPartySize => _world.PartySize,
            ScriptOp.CheckGold => (int)_world.Gold,
            _ => 0,
        };
        return result & 0xFFFF;
    }

    private static int? StatNumber(RecordId stat) => StatId.TryFromRecord(stat) is { } s ? (int)s : null;

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
    public bool Effect(ScriptOp op)
    {
        switch (op)
        {
            case ScriptOp.FlagSet set:
                return _flags.TrySet(set.Flag, true);
            case ScriptOp.FlagClear clear:
                return _flags.TrySet(clear.Flag, false);
            case ScriptOp.PartyAddGold add:
                _world.Gold += (uint)(add.Amount & 0xFFFF);
                _host?.Cue(new SoundCue(ScriptSound.GoldGained));
                return true;
            case ScriptOp.PartyTakeGold take:
                if (_world.Gold < (uint)(take.Amount & 0xFFFF))
                    return false;
                _world.Gold -= (uint)(take.Amount & 0xFFFF);
                _host?.Cue(new SoundCue(ScriptSound.GoldPaid));
                return true;
            case ScriptOp.PartyAddItem item:
                _world.AddItem(item.Item);
                return true;
            case ScriptOp.PartyTakeItem taken:
                return _world.TakeItem(taken.Item);
            case ScriptOp.PartyAddMember joining:
                return _world.AddMember(joining.Entity);
            case ScriptOp.PartyRemoveMember leaving:
                _world.RemoveMember(leaving.Entity);
                return true;
            case ScriptOp.PartyAddExp exp when _world.AddExp(exp.Member, exp.Amount):
                _host?.Cue(new SoundCue(ScriptSound.Experience));
                return true;
            default:
                return false;
        }
    }
}
