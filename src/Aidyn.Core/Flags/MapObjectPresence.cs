using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Flags;

/// <summary>
/// Whether a map object is there as its chunk loads, by the flags (the original's
/// <c>gVoxelFuncs2</c>, checked in 1.0's code; see docs/formats/maps.md). Whether it works once
/// there, its <see cref="MapObject.Enabled"/>, is another question, except for monster parties.
/// </summary>
public static class MapObjectPresence
{
    /// <summary>Whether a flag gate holds: whether the flag differs from its <see cref="FlagGate.Invert"/>.</summary>
    public static bool Holds(FlagGate gate, Func<RecordId, bool> isSet) => isSet(gate.Flag) != gate.Invert;

    /// <summary>
    /// Whether the object is there. Most types are there while their <see cref="MapObject.Visible"/>
    /// holds, which the types that don't read it leave out. A monster party is there while its
    /// <see cref="MapObject.Enabled"/> holds and, if it has no dialogue, until beating it sets its
    /// <see cref="MapObject.Used"/>. A chest, sack, crate or barrel is there until it's opened or
    /// destroyed, when its model shows it opened or destroyed instead. A herb, spice, gem or custom
    /// container is there while its <see cref="MapObject.Enabled"/> holds as well, and hides its
    /// model when it isn't (<c>exploding_container_check</c>).
    /// </summary>
    /// <param name="isSet">Whether a flag is set.</param>
    public static bool There(MapObject o, Func<RecordId, bool> isSet)
    {
        if (o.Visible is { } visible && !Holds(visible, isSet))
            return false;
        return o switch
        {
            MonsterPartyObject party => (party.Enabled is not { } enabled || Holds(enabled, isSet))
                && (party.Dialogue is not null || party.Used is not { } used || !isSet(used.Flag)),
            ContainerObject { Kind: ContainerKind.Chest or ContainerKind.Sack or ContainerKind.Crate or ContainerKind.Barrel } container =>
                !(container.Opened is { } opened && isSet(opened)) && !(container.Exploded is { } exploded && isSet(exploded)),
            ContainerObject container => container.Enabled is not { } enabled || Holds(enabled, isSet),
            _ => true,
        };
    }
}
