using System.Buffers.Binary;
using System.Numerics;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
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
    /// <summary>
    /// A named point that teleporters, cameras and dialogues refer to. Most sit in clusters where
    /// cut-scenes place their cameras.
    /// </summary>
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
    /// <summary>A light that shines from the camera's eye rather than from where it is (<c>WorldLights.AddNear</c>, 0x8004d384).</summary>
    FacesCamera = 0x1,
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
    /// <summary>A light that collision can block.</summary>
    Occludable = 0x1000,
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

/// <summary><c>Scene_obj_dat.sceneflags</c>. The record's <see cref="Plugins.Records.SceneObjectFlags"/> lacks <see cref="Actor"/>: the importer folds it into the model's record type.</summary>
[Flags]
public enum SceneObjectFlags : ushort
{
    None = 0,
    /// <summary>Stays opaque when it comes between the camera and the player. Others fade.</summary>
    Solid = 0x1,
    /// <summary>The model is a Borg7 actor, not a Borg5 scene.</summary>
    Actor = 0x2,
    /// <summary>Drawn within half its draw distance even when out of view.</summary>
    DrawWhenNear = 0x4,
    Unknown8 = 0x8,
    /// <summary>
    /// Tinted: unless the tint is all zero, its colour is averaged with the scenery's
    /// (<c>SetSceneColors</c>). Its alpha isn't used.
    /// </summary>
    Tint = 0x10,
    /// <summary>Unlit.</summary>
    Fullbright = 0x20,
    /// <summary>Drawn in the later pass, with the see-through scenery.</summary>
    Deferred = 0x40,
}

/// <param name="Borg">A Borg5, or a Borg7 with <see cref="SceneObjectFlags.Actor"/>.</param>
/// <param name="DrawDistance">
/// Drawn out to this far from the camera, and from 2 short of the previous model's distance.
/// </param>
public readonly record struct SceneModel(int Borg, float DrawDistance);

/// <summary>
/// A model placed in the chunk. The object's radius is how far away it's drawn at all. The game
/// scales it, then rotates it, then moves it to the object's position.
/// </summary>
/// <param name="Models">
/// The first <paramref name="Lods"/> are levels of detail, nearest first. A container's model has
/// one, followed by the models it shows once opened and once destroyed.
/// </param>
/// <param name="Rotation">
/// In radians: roll about X, then pitch about Y, then heading about Z (<c>guRotateRPYF</c>, checked
/// against 1.0's code). Most objects only turn about Y, the vertical.
/// </param>
public sealed record SceneData(
    IReadOnlyList<SceneModel> Models,
    int Lods,
    Vector3 Rotation,
    Vector3 Scale,
    Rgba Tint,
    SceneObjectFlags SceneFlags) : VoxelData
{
    /// <summary><see cref="Rotation"/> as a quaternion, for row vectors as in <see cref="System.Numerics"/>.</summary>
    public Quaternion Orientation => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
        Matrix4x4.CreateRotationX(Rotation.X) * Matrix4x4.CreateRotationY(Rotation.Y) * Matrix4x4.CreateRotationZ(Rotation.Z)));
}

/// <summary>A light that colours the actors near it (<c>light_dat</c>).</summary>
/// <param name="From">The colour it starts at. <see cref="LightKind.Static"/> lights stay there.</param>
public sealed record LightData(Rgba From, Rgba To, LightKind Kind, float Speed) : VoxelData;

/// <summary>A sound or piece of music that plays near the object (<c>audio_obj_dat</c>).</summary>
/// <param name="Sequence">The Borg12 it plays.</param>
public sealed record AudioData(int Sequence, float Volume, float MinWait, float MaxWait, SoundFlags AudioFlags) : VoxelData;

/// <summary>
/// A point wandering NPCs move between (<c>wandernode_dat</c>). Their monster party names the
/// first one.
/// </summary>
/// <param name="Radius">How far a wanderer can stray from its goal before it walks back.</param>
/// <param name="Scatter">Each visit's goal is up to this far from the node, in a random direction.</param>
/// <param name="Chance">The chance of going on to <see cref="Next"/>[1] rather than [0].</param>
/// <param name="Wait">Ticks before moving on.</param>
/// <param name="Next">Indices of the nodes that follow, in the same Borg9.</param>
public sealed record WanderNodeData(float Radius, float Scatter, float Chance, ushort Wait, WanderFlags WanderFlags,
    IReadOnlyList<ushort> Next) : VoxelData;

/// <summary>
/// Takes over the camera while the player is within the object's radius, or walks on a trigger
/// triangle that names it (<c>voxelCamera</c>).
/// </summary>
/// <param name="RefPoint">The reference point to move to, or 0 for just above the player.</param>
/// <param name="Values">Z is <see cref="Duration"/>; X is the pitch for <see cref="CameraFlags.Pitch"/> and Y the distance for <see cref="CameraFlags.Distance"/>.</param>
public sealed record CameraData(ushort RefPoint, CameraFlags CameraFlags, Vector3 Values) : VoxelData
{
    public float Duration => Values.Z;
}

/// <param name="OpenFlag">Set when the container is opened. It stays empty once set.</param>
/// <param name="ExplodeFlag">Set when its trap destroys it.</param>
/// <param name="TrapLevel">
/// The trap's level, or 0 for none. The word before it is 0 while it's armed and 0xFF once it's
/// disarmed or has gone off; it's 0 in all the data. The decomp names the two the other way round,
/// but <c>TrapMenu::GetTrapLV</c> returns the first and the menu reads the level from the second.
/// </param>
/// <param name="Loot">A chest loot table, or none.</param>
/// <param name="Kind">
/// Chest, sack, crate, barrel, herb, spice, gem, or loot with its own model (0-7). Kinds 4-7 are
/// gated by flags A and C like triggers; the others show until opened or destroyed.
/// </param>
/// <param name="LockLevel">The lock's level, or 0 for none. Picking it clears it.</param>
/// <param name="Gold">Gold, or for a herb, spice or gem source, how many it gives before the Ranger skill's bonus.</param>
/// <param name="Item">A fixed item, or none.</param>
/// <param name="Reach">
/// How near the player's sphere has to be to use it (<c>chestSize</c>, which <c>container_obj_check</c>
/// adds the player's radius to): 2 for most, from 1 to 5. The decomp says it seems the same as the
/// object's radius, which is 10 for most; it isn't. The word before it is a copy of the radius.
/// </param>
public sealed record ContainerData(ushort OpenFlag, ushort ExplodeFlag, ushort TrapLevel, ItemId Loot, ushort Kind, ushort LockLevel,
    ushort Gold, ItemId Item, float Reach) : VoxelData;

/// <param name="Destination">Where it leads. Row and column are those of a <see cref="MapChunk"/>.</param>
/// <param name="RefPoint">
/// The reference point in the destination chunk to arrive at, facing the way it points, or
/// <see cref="OwnPosition"/> to arrive at the teleporter's own position.
/// </param>
/// <param name="TrapLevel">The trap's level, or 0 for none. As for a container, the word before it is 0 in all the data.</param>
/// <param name="LockLevel">The lock's level, or 0 for none.</param>
/// <param name="CameraRefPoint">A reference point in the destination chunk for the camera, or 0.</param>
/// <param name="LockFlag">Unlocked while set.</param>
/// <param name="UnlockFlag">Set when the lock is opened.</param>
/// <param name="TrapFlag">Disarmed while set.</param>
/// <param name="DisarmFlag">Set when the trap is disarmed.</param>
/// <param name="SecretFlag">
/// Unless it's 0, a secret door: using it while the flag is clear finds it and sets the flag, and
/// only then does it work.
/// </param>
/// <param name="SecretLevel">
/// The skill a secret door needs before it's there at all, until it's found. 0 in all the data.
/// </param>
public sealed record TeleporterData(
    (MapSet Set, int Row, int Column) Destination,
    ushort RefPoint,
    ushort TrapLevel,
    ushort LockLevel,
    ushort CameraRefPoint,
    ushort LockFlag,
    ushort UnlockFlag,
    ushort TrapFlag,
    ushort DisarmFlag,
    ushort SecretFlag,
    ushort SecretLevel,
    string Name) : VoxelData
{
    /// <summary>The <see cref="RefPoint"/> that means the teleporter's own position.</summary>
    public const ushort OwnPosition = 0x7FF8;
}

/// <param name="Entity">The NPC, or the group's leader.</param>
/// <param name="MinSize">A battle with fewer enemies than this, after the random extras, has none.</param>
/// <param name="MaxSize">The most enemies a battle with the party has.</param>
/// <param name="WanderNode">The index of its home wander node, in the same Borg9.</param>
/// <param name="Dialogue">The Borg13 that talking to it starts, or 0 for a fight.</param>
public sealed record MonsterPartyData(
    IReadOnlyList<(ItemId Enemy, byte Min, byte Max)> Enemies,
    ItemId Entity,
    ItemId Loot,
    ushort MinSize,
    ushort MaxSize,
    ushort WanderNode,
    PartyFlags PartyFlags,
    int Dialogue) : VoxelData;

/// <param name="Id">What teleporters, cameras and scripts name it by.</param>
/// <param name="Name">Battle arenas find their spawn and retreat points by name.</param>
/// <param name="Facing">The way a teleporter that arrives at it turns the player, or zero.</param>
public sealed record ReferencePointData(ushort Id, string Name, Vector3 Facing) : VoxelData;

/// <param name="Script">The Borg13 to start.</param>
/// <param name="Map">For cut-scenes, the chunk to stage it in.</param>
/// <param name="RefPoint">The reference point its ops count from.</param>
public sealed record DialogueData(int Script, (MapSet Set, int Row, int Column) Map, ushort RefPoint, string Name)
    : VoxelData;

public sealed record TriggerData(TriggerKind Kind, ushort A, ushort B, ushort C) : VoxelData;

/// <summary>Reads the map objects of a Borg9. <see cref="Borg9Map"/> reads the rest.</summary>
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
            VoxelType.Scene => Scene(p),
            VoxelType.Light => new LightData(CodeSegment.Colour(p, 4), CodeSegment.Colour(p, 8), (LightKind)U16(p, 0xC), F32(p, 0x14)),
            VoxelType.Audio => new AudioData((int)U32(p, 0), F32(p, 8), F32(p, 0xC), F32(p, 0x10), (SoundFlags)U16(p, 0x14)),
            VoxelType.WanderNode => new WanderNodeData(F32(p, 8), F32(p, 0xC), F32(p, 0x10), U16(p, 0x14),
                (WanderFlags)U16(p, 0x16), [U16(p, 0x18), U16(p, 0x1A)]),
            VoxelType.Camera => new CameraData(U16(p, 0), (CameraFlags)U16(p, 2), new Vector3(F32(p, 8), F32(p, 0xC), F32(p, 0x10))),
            VoxelType.Container => new ContainerData(U16(p, 0x8), U16(p, 0xA), U16(p, 0xE), new ItemId(U16(p, 0x10)), U16(p, 0x12),
                U16(p, 0x14), U16(p, 0x16), new ItemId(U16(p, 0x18)), F32(p, 0x4)),
            VoxelType.Teleporter => new TeleporterData(((MapSet)U16(p, 0), U16(p, 2), U16(p, 4)), U16(p, 6), U16(p, 0xA), U16(p, 0xC),
                U16(p, 0xE), U16(p, 0x10), U16(p, 0x12), U16(p, 0x14), U16(p, 0x16), U16(p, 0x18), U16(p, 0x1A),
                GameText.Field(p.Slice(0x1C, 16))),
            VoxelType.MonsterParty => new MonsterPartyData(Enemies(p), new ItemId(U16(p, 0x20)), new ItemId(U16(p, 0x22)),
                U16(p, 0x28), U16(p, 0x2A), U16(p, 0x2E), (PartyFlags)U16(p, 0x30), (int)U32(p, 0x34)),
            VoxelType.ReferencePoint => new ReferencePointData(U16(p, 0), GameText.Field(p.Slice(4, 16)),
                new Vector3(F32(p, 0x14), F32(p, 0x18), F32(p, 0x1C))),
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

    private static SceneData Scene(ReadOnlySpan<byte> p)
    {
        // Three model slots, each a draw distance, an index and a run-time pointer.
        var models = new List<SceneModel>();
        for (var i = 0; i < 3 && U32(p, i * 12 + 4) is var borg and not 0; i++)
            models.Add(new SceneModel((int)borg, F32(p, i * 12)));
        return new SceneData(models, U16(p, 0x40), new Vector3(F32(p, 0x24), F32(p, 0x28), F32(p, 0x2C)),
            new Vector3(F32(p, 0x30), F32(p, 0x34), F32(p, 0x38)), CodeSegment.Colour(p, 0x3C), (SceneObjectFlags)U16(p, 0x42));
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
