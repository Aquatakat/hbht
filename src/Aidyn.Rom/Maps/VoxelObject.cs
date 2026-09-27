using System.Buffers.Binary;
using System.Numerics;
using Aidyn.Rom.Db;
using Aidyn.Rom.Text;

namespace Aidyn.Rom.Maps;

/// <summary>What a map object is (the decomp's <c>Vobject</c>).</summary>
public enum VoxelType : ushort
{
    /// <summary>A model: a Borg5 scene or a Borg7 actor.</summary>
    Scene,
    /// <summary>Loot: a chest, sack, crate or barrel, a herb, spice or gem source, or loot with its own model.</summary>
    Container,
    Light,
    Audio,
    /// <summary>A point wandering NPCs walk between.</summary>
    WanderNode,
    /// <summary>An NPC or a group of enemies.</summary>
    MonsterParty,
    /// <summary>A named point that teleporters, cameras and dialogues refer to.</summary>
    ReferencePoint,
    /// <summary>A door, ladder or portal to another chunk or map set.</summary>
    Teleporter,
    Camera,
    /// <summary>Starts a Borg13: a conversation, cut-scene, sign or book.</summary>
    Dialogue,
    Trigger,
    /// <summary>Unused: the game saves anywhere.</summary>
    SavePoint,
    /// <summary>One in a corner of most chunks. The game does nothing with them.</summary>
    Code,
}

/// <summary><c>voxelHeader.Bitfeild</c>, as stored. The game changes some bits at run time.</summary>
[Flags]
public enum VoxelFlags : ushort
{
    None = 0,
    /// <summary>Only present without the Expansion Pak.</summary>
    NoExpansionPak = 0x20,
    /// <summary>Only present with the Expansion Pak.</summary>
    ExpansionPak = 0x40,
    /// <summary>Inverts <see cref="VoxelObject.Visible"/>.</summary>
    InvertVisible = 0x80,
    /// <summary>Inverts <see cref="VoxelObject.Enabled"/>.</summary>
    InvertEnabled = 0x100,
    /// <summary>Using the object clears <see cref="VoxelObject.FlagB"/> instead of setting it.</summary>
    ClearFlagBOnUse = 0x200,
    /// <summary>The player can reach the object to use it.</summary>
    Reachable = 0x400,
    /// <summary>Walking into the object uses it.</summary>
    Tangible = 0x2000,
    Unknown4000 = 0x4000,
    /// <summary>The object is active.</summary>
    Active = 0x8000,
}

/// <summary>
/// A condition on an event flag: true when the flag's value differs from <see cref="Inverted"/>.
/// Flag 0 is always false and flag 1 always true, so an inverted flag 0 always holds.
/// </summary>
public readonly record struct FlagGate(ushort Flag, bool Inverted)
{
    /// <summary>The gate's value when it doesn't depend on a real flag, else null.</summary>
    public bool? Constant => Flag switch { 0 => Inverted, 1 => !Inverted, _ => null };

    public override string ToString() => Constant is { } c ? (c ? "always" : "never") : (Inverted ? "not " : "") + Flag;
}

/// <summary>A map object in a Borg9 (a <c>voxelObject</c>, 0x6C bytes).</summary>
/// <param name="Index">Within its Borg9.</param>
/// <param name="Radius">How near the player has to be to use or trigger it.</param>
/// <param name="Links">Indices of related objects in the same Borg9, such as a container's model, or -1.</param>
/// <param name="FlagA">See <see cref="Enabled"/>.</param>
/// <param name="FlagB">
/// Set when the object is used, or cleared with <see cref="VoxelFlags.ClearFlagBOnUse"/>. A monster
/// party's is set when it's beaten, or talked to if it has a dialogue, and it hides the party.
/// </param>
/// <param name="FlagC">See <see cref="Visible"/>.</param>
/// <param name="Data">The type's own fields, for the types that refer to flags or other records.</param>
public sealed record VoxelObject(
    int Index,
    Vector3 Position,
    float Radius,
    VoxelType Type,
    VoxelFlags Flags,
    IReadOnlyList<short> Links,
    ushort FlagA,
    ushort FlagB,
    ushort FlagC,
    VoxelData? Data)
{
    public const int Size = 0x6C;

    /// <summary>Whether a scene, container, teleporter or trigger is there at all.</summary>
    public FlagGate Visible => new(FlagC, Flags.HasFlag(VoxelFlags.InvertVisible));

    /// <summary>Whether a teleporter, trigger or dialogue works, and whether a monster party is there.</summary>
    public FlagGate Enabled => new(FlagA, Flags.HasFlag(VoxelFlags.InvertEnabled));
}

/// <summary>A map object's type-specific fields.</summary>
public abstract record VoxelData;

/// <param name="OpenFlag">Set when the container is opened. It stays empty once set.</param>
/// <param name="ExplodeFlag">Set when its trap destroys it.</param>
/// <param name="Loot">A chest loot table, or none.</param>
/// <param name="Kind">
/// Chest, sack, crate, barrel, herb, spice, gem, or loot with its own model (0-7). Kinds 4-7 are
/// gated by flags A and C like triggers; the others show until opened or destroyed.
/// </param>
/// <param name="Item">A fixed item, or none.</param>
public sealed record ContainerData(ushort OpenFlag, ushort ExplodeFlag, ItemId Loot, ushort Kind, ushort Gold, ItemId Item)
    : VoxelData;

/// <param name="Destination">Where it leads. Row and column are those of a <see cref="MapChunk"/>.</param>
/// <param name="LockFlag">Unlocked while set.</param>
/// <param name="UnlockFlag">Set when the lock is opened.</param>
/// <param name="TrapFlag">Disarmed while set.</param>
/// <param name="DisarmFlag">Set when the trap is disarmed.</param>
/// <param name="SecretFlag">Found while set, and set when found.</param>
/// <param name="SecretLevel">The skill needed to find it, or 0 if it isn't secret.</param>
public sealed record TeleporterData(
    (MapSet Set, int Row, int Column) Destination,
    ushort RefPoint,
    ushort LockLevel,
    ushort LockFlag,
    ushort UnlockFlag,
    ushort TrapFlag,
    ushort DisarmFlag,
    ushort SecretFlag,
    ushort SecretLevel,
    string Name) : VoxelData;

/// <param name="Entity">The NPC, or the group's leader.</param>
/// <param name="Dialogue">The Borg13 that talking to it starts, or 0 for a fight.</param>
public sealed record MonsterPartyData(
    IReadOnlyList<(ItemId Enemy, byte Min, byte Max)> Enemies,
    ItemId Entity,
    ItemId Loot,
    ushort PartyFlags,
    int Dialogue) : VoxelData;

public sealed record ReferencePointData(ushort Id, string Name) : VoxelData;

/// <param name="Script">The Borg13 to start.</param>
/// <param name="Map">For cut-scenes, the chunk to stage it in.</param>
/// <param name="RefPoint">The reference point its ops count from.</param>
public sealed record DialogueData(int Script, (MapSet Set, int Row, int Column) Map, ushort RefPoint, string Name)
    : VoxelData;

/// <summary>What a trigger does (the decomp's <c>VObject_Triggers</c>).</summary>
public enum TriggerKind : short
{
    Nothing0,
    /// <summary>Sets <see cref="TriggerData.A"/>.</summary>
    SetFlag,
    /// <summary>Plays animation <see cref="TriggerData.A"/> on the linked model.</summary>
    Animate,
    Nothing3,
    /// <summary>Changes the flags of the collision with ground type <see cref="TriggerData.A"/>.</summary>
    ChangeCollision,
    Nothing5,
}

public sealed record TriggerData(TriggerKind Kind, ushort A, ushort B, ushort C) : VoxelData;

/// <summary>Reads the map objects of a Borg9. The rest of the Borg9, its collision, is roadmap 1C.</summary>
public static class Borg9Voxels
{
    private const int HeaderSize = 0x4C;
    private const int PayloadOffset = 0x28;

    public static IReadOnlyList<VoxelObject> Read(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg9 is at least 0x{HeaderSize:X} bytes, not 0x{data.Length:X}.");
        var count = U16(data, 0x28);
        var start = U32(data, 0x44);
        var next = U32(data, 0x48);
        // The collision sections follow the objects, 8-byte aligned.
        if (start + count * VoxelObject.Size > next || next - (start + count * VoxelObject.Size) >= 8 || next > data.Length)
            throw new InvalidDataException(
                $"Borg9 map objects at 0x{start:X} ({count}) don't end where its collision starts (0x{next:X}).");

        var objects = new VoxelObject[count];
        for (var i = 0; i < count; i++)
            objects[i] = ReadObject(data.AsSpan((int)start + i * VoxelObject.Size, VoxelObject.Size), i);
        return objects;
    }

    private static VoxelObject ReadObject(ReadOnlySpan<byte> v, int index)
    {
        var type = U16(v, 0x16);
        if (type > (int)VoxelType.Code)
            throw new InvalidDataException($"Map object {index} has unknown type {type}.");
        // A time stamp, padding and a pointer, all set at run time.
        if (U32(v, 0x10) != 0 || U16(v, 0x22) != 0 || U32(v, 0x24) != 0)
            throw new InvalidDataException($"Map object {index} has run-time fields set.");

        var p = v[PayloadOffset..];
        VoxelData? data = (VoxelType)type switch
        {
            VoxelType.Container => new ContainerData(U16(p, 0x8), U16(p, 0xA), new ItemId(U16(p, 0x10)), U16(p, 0x12),
                U16(p, 0x16), new ItemId(U16(p, 0x18))),
            VoxelType.Teleporter => new TeleporterData(((MapSet)U16(p, 0), U16(p, 2), U16(p, 4)), U16(p, 6), U16(p, 0xC),
                U16(p, 0x10), U16(p, 0x12), U16(p, 0x14), U16(p, 0x16), U16(p, 0x18), U16(p, 0x1A), GameText.Field(p.Slice(0x1C, 16))),
            VoxelType.MonsterParty => new MonsterPartyData(Enemies(p), new ItemId(U16(p, 0x20)), new ItemId(U16(p, 0x22)),
                U16(p, 0x30), (int)U32(p, 0x34)),
            VoxelType.ReferencePoint => new ReferencePointData(U16(p, 0), GameText.Field(p.Slice(4, 16))),
            VoxelType.Dialogue => new DialogueData(U16(p, 0), ((MapSet)U16(p, 2), U16(p, 4), U16(p, 6)), U16(p, 8),
                GameText.Field(p.Slice(0xC, 56))),
            VoxelType.Trigger => new TriggerData((TriggerKind)(short)U16(p, 0), U16(p, 4), U16(p, 6), U16(p, 8)),
            _ => null,
        };

        return new VoxelObject(
            index,
            new Vector3(F32(v, 0), F32(v, 4), F32(v, 8)),
            F32(v, 0xC),
            (VoxelType)type,
            (VoxelFlags)U16(v, 0x14),
            [(short)U16(v, 0x18), (short)U16(v, 0x1A)],
            U16(v, 0x1C), U16(v, 0x1E), U16(v, 0x20),
            data);
    }

    private static List<(ItemId, byte, byte)> Enemies(ReadOnlySpan<byte> p)
    {
        var enemies = new List<(ItemId, byte, byte)>();
        for (var i = 0; i < 8; i++)
        {
            var id = U16(p, i * 4);
            if (id != 0)
                enemies.Add((new ItemId(id), p[i * 4 + 2], p[i * 4 + 3]));
        }
        return enemies;
    }

    private static ushort U16(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt16BigEndian(s[at..]);
    private static uint U32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadUInt32BigEndian(s[at..]);
    private static float F32(ReadOnlySpan<byte> s, int at) => BinaryPrimitives.ReadSingleBigEndian(s[at..]);
}
