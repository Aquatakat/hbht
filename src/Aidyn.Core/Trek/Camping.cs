using Aidyn.Core.Characters;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

/// <summary>How a camp went.</summary>
public enum CampOutcome
{
    /// <summary>The terrain allows no camping.</summary>
    Refused,
    /// <summary>The party was ambushed: no heal and no time passing. Trek fades out into the battle (<see cref="TrekLoop.CueBattle"/>).</summary>
    Ambushed,
    /// <summary>The party healed and a camp scene started; eight hours pass when it ends.</summary>
    Camped,
}

/// <summary>Camping's ambush (<c>get_ranger_camping_bool</c>, <c>0x800788cc</c> in 1.0). See <see cref="CampingRecord"/>.</summary>
public static class Camping
{
    /// <summary>
    /// The ambush's chance in percent on a terrain with this factor: trunc((15 − r) × 1/15 × factor) in single precision,
    /// where r is the party's best modified Ranger (<c>GetMostSkilledMember</c>), 0 if it's 0 or less, at most 10.
    /// </summary>
    public static int AmbushChance(CampingRecord rules, float factor, Party party)
    {
        var ranger = party.MostSkilled(Skill.Ranger)?.Skills.Modified(Skill.Ranger) ?? 0;
        var r = Math.Clamp(ranger, 0, rules.RangerCap);
        var chance = (rules.AmbushBase - r) * rules.AmbushScale * factor;
        return (ushort)chance;
    }

    /// <summary>Whether a camp is ambushed: only while the camping record's flag holds, then d100 against the chance.</summary>
    public static bool Ambushed(CampingRecord rules, float factor, Party party, Func<RecordId, bool> isSet, IRandom random) =>
        isSet(rules.Ambushes) && random.RollD(1, 100) <= AmbushChance(rules, factor, party);
}
