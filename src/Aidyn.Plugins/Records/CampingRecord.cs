namespace Aidyn.Plugins.Records;

/// <summary>
/// How camping goes (<c>aidyn:camping/0</c>, the only one): the ambush, the heal and the camp scenes. Each terrain's
/// chance of an ambush, and whether camping is allowed there at all, is its <see cref="TerrainRecord.Camp"/>.
/// </summary>
/// <param name="CannotCamp">What's said where the terrain allows no camping.</param>
/// <param name="Ambushes">While this flag is set, camping can be ambushed.</param>
/// <param name="AmbushBase">
/// The ambush's chance in percent is trunc((<paramref name="AmbushBase"/> − the party's best modified Ranger, at most
/// <paramref name="RangerCap"/>) × <paramref name="AmbushScale"/> × the terrain's factor), in single precision.
/// </param>
/// <param name="Heal">The share of maximum hit points and of every battery's capacity that camping restores.</param>
/// <param name="EffectTicks">Ticks of play that pass for the members' potions and other effects as they heal.</param>
/// <param name="Checking">Set while the game looks for a camp scene, which the scenes' flags read.</param>
/// <param name="Scenes">
/// A dialogue object in the player's chunk whose enabling flag is one of these, and holds, starts as the party camps,
/// the first found, wherever it is.
/// </param>
/// <param name="Campsite">With no camp scene, this map's first dialogue object starts.</param>
public sealed record CampingRecord(
    RecordId Id,
    string CannotCamp,
    RecordId Ambushes,
    int AmbushBase,
    int RangerCap,
    float AmbushScale,
    float Heal,
    int EffectTicks,
    RecordId Checking,
    IReadOnlyList<RecordId> Scenes,
    RecordId Campsite);
