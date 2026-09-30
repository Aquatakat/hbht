using System.Numerics;
using System.Text.Json.Serialization;

namespace Aidyn.Plugins.Records;

/// <summary>
/// A set of map chunks loaded as one world, such as the overworld or the Gwernia interiors
/// (<c>aidyn:mapset/n</c>, the original's <c>EnumMapDatA</c>). Teleporters move between sets.
/// </summary>
/// <param name="Label">The set's name, for tools.</param>
/// <param name="CellSize">The spacing of the chunks, the same along X and Z.</param>
public sealed record MapSetRecord(RecordId Id, string Label, float CellSize, IReadOnlyList<ChunkEntry> Chunks)
{
    /// <summary>Where a chunk's own coordinates start: rows run along X and columns along Z.</summary>
    public Vector3 Origin(int row, int column) => new Vector3(row, 0, column) * CellSize;
}

/// <summary>
/// One chunk of a <see cref="MapSetRecord"/>. The runtime keeps the 3×3 chunks around the player
/// loaded.
/// </summary>
/// <param name="Row">1-based. The debug overlay shows it as a letter, so row 8 is H.</param>
/// <param name="Scene">The chunk's scenery, drawn at its origin.</param>
/// <param name="AltScene">Its see-through scenery, such as water, drawn after everything else.</param>
/// <param name="Map">Its collision and map objects.</param>
/// <param name="Terrain">The terrain type, which weather and camp encounters use.</param>
public sealed record ChunkEntry(int Row, int Column, RecordId? Scene, RecordId? AltScene, RecordId Map, int Terrain)
{
    /// <summary>The chunk as the debug overlay names it, such as <c>I08</c>.</summary>
    [JsonIgnore]
    public string Name => Row is >= 1 and <= 26 ? $"{(char)('A' + Row - 1)}{Column:D2}" : $"{Row},{Column}";
}

/// <summary>
/// One chunk's map objects (<c>aidyn:map/0xHHHH</c>, a Borg9). Its collision is the asset of the
/// same ID: a glTF mesh in chunk coordinates whose vertices carry their triangle's
/// <c>_SURFACE</c>, <c>_ROLE</c> and <c>_FLAGS</c>.
/// </summary>
/// <param name="Objects">
/// Keyed by <see cref="MapObject.Index"/>, which is also the last segment of the object's
/// <c>aidyn:voxel/…</c> ID (<see cref="VoxelId"/>). Every type is imported except the code
/// objects, which the game does nothing with.
/// </param>
public sealed record MapRecord(RecordId Id, IReadOnlyList<MapObject> Objects)
{
    /// <summary>The ID of the object at <paramref name="index"/> in the map <paramref name="map"/>, such as <c>aidyn:voxel/0x1A20/12</c>.</summary>
    public static RecordId VoxelId(RecordId map, int index) =>
        RecordId.Parse(string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{map.Namespace}:{RecordTypes.Voxel}/{map.Key}/{index}"));

    /// <summary>The map a voxel ID names an object of.</summary>
    public static RecordId MapOf(RecordId voxel) =>
        RecordId.Parse($"{voxel.Namespace}:{RecordTypes.Map}/{voxel.Key[..voxel.Key.IndexOf('/', StringComparison.Ordinal)]}");
}

/// <summary>Which Expansion Pak setting a map object exists under.</summary>
public enum PakVariant
{
    Any,
    /// <summary>Only with the Expansion Pak. The runtime always plays as if one is present.</summary>
    With,
    /// <summary>Only without it, so the runtime leaves these out.</summary>
    Without,
}

/// <summary>A condition on an event flag: true when the flag's value differs from <see cref="Invert"/>.</summary>
public sealed record FlagGate(RecordId Flag, bool Invert = false);

/// <summary>A flag an object writes when it's used: it sets it, or clears it if <see cref="Clear"/>.</summary>
public sealed record FlagWrite(RecordId Flag, bool Clear = false);

/// <summary>A chunk of a map set, which teleporters lead to and cut-scenes are staged in.</summary>
/// <param name="Row">As in <see cref="ChunkEntry.Row"/>.</param>
public sealed record ChunkRef(RecordId MapSet, int Row, int Column);

/// <summary>
/// A placed map object: a voxel in the original. The flag slots are the original's C, A and B, and
/// each type has the ones the game reads; the others are left out.
/// </summary>
/// <param name="Index">Within its map.</param>
/// <param name="Position">In chunk coordinates.</param>
/// <param name="Radius">
/// How near the player has to be to use it or to set it off, or for scenery, how far away it's
/// drawn at all.
/// </param>
/// <param name="Visible">
/// Whether it's there at all, as the chunk loads (flag C). Scenes, lights, sounds, wander nodes,
/// reference points, teleporters, cameras, triggers, and herb, spice, gem and custom containers
/// have one. Monster parties, dialogues and save points are always there, but for their
/// <see cref="Enabled"/>.
/// </param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(SceneObject), "scene")]
[JsonDerivedType(typeof(ContainerObject), "container")]
[JsonDerivedType(typeof(LightObject), "light")]
[JsonDerivedType(typeof(SoundObject), "sound")]
[JsonDerivedType(typeof(WanderNodeObject), "wanderNode")]
[JsonDerivedType(typeof(MonsterPartyObject), "monsterParty")]
[JsonDerivedType(typeof(ReferencePointObject), "refPoint")]
[JsonDerivedType(typeof(TeleporterObject), "teleporter")]
[JsonDerivedType(typeof(CameraObject), "camera")]
[JsonDerivedType(typeof(DialogueObject), "dialogue")]
[JsonDerivedType(typeof(TriggerObject), "trigger")]
[JsonDerivedType(typeof(SavePointObject), "savePoint")]
public record MapObject(
    [property: JsonPropertyOrder(-2)] int Index,
    [property: JsonPropertyOrder(-2)] Vector3 Position,
    [property: JsonPropertyOrder(-2)] float Radius,
    [property: JsonPropertyOrder(-2)] PakVariant Pak,
    [property: JsonPropertyOrder(-2)] FlagGate? Visible)
{
    /// <summary>
    /// Whether it works (flag A): whether a teleporter, trigger, dialogue or herb, spice, gem or
    /// custom container can be used, and whether a monster party is there at all.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public FlagGate? Enabled { get; init; }

    /// <summary>
    /// What using it writes (flag B): opening a container, setting off a trigger, or talking to or
    /// beating a monster party. Left out where it would write flag 0, which the game ignores.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public FlagWrite? Used { get; init; }

    /// <summary>
    /// A teleporter, trigger or dialogue that goes off when the player walks within its radius,
    /// rather than when the player uses it. A tangible teleporter skips its lock, trap and secret.
    /// </summary>
    [JsonPropertyOrder(-1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Tangible { get; init; }

    /// <summary>The player can use it: the nearest one within its radius is what the player faces to use.</summary>
    [JsonPropertyOrder(-1)]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Reachable { get; init; }

    /// <summary>
    /// Another object in the same map it works with. To find a container's model or the actor a
    /// trigger animates, the game follows links from object to object until it reaches one of the
    /// type it wants (<c>GetVoxelFromObjectLink</c>, checked in 1.0's code).
    /// </summary>
    [JsonPropertyOrder(1)]
    public RecordId? Link { get; init; }
}

/// <summary>A colour, 0–255 per channel.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>What a scene object's model is and how it's drawn (the original's <c>sceneflags</c>).</summary>
[Flags]
public enum SceneObjectFlags
{
    None = 0,
    /// <summary>Stays opaque when it comes between the camera and the player. Others fade.</summary>
    Solid = 0x1,
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

/// <summary>A model, or one level of detail of one.</summary>
/// <param name="Model">A scene, or an actor.</param>
/// <param name="DrawDistance">Drawn out to this far from the camera, and from 2 short of the previous level's distance.</param>
public sealed record SceneModel(RecordId Model, float DrawDistance);

/// <summary>
/// A model placed in the chunk: scaled, then rotated, then moved to <see cref="MapObject.Position"/>.
/// </summary>
/// <param name="Models">
/// The first <see cref="Lods"/> are levels of detail, nearest first. A container's model has one,
/// followed by the models it shows once opened and once destroyed.
/// </param>
/// <param name="Rotation">
/// In radians: roll about X, then pitch about Y, then heading about Z (libultra's
/// <c>guRotateRPYF</c>). Most objects only turn about Y, the vertical.
/// </param>
public sealed record SceneObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate Visible,
    IReadOnlyList<SceneModel> Models,
    int Lods,
    Vector3 Rotation,
    Vector3 Scale,
    Rgba Tint,
    SceneObjectFlags Flags) : MapObject(Index, Position, Radius, Pak, Visible)
{
    /// <summary><see cref="Rotation"/> as a quaternion.</summary>
    public Quaternion Orientation() => Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(
        Matrix4x4.CreateRotationX(Rotation.X) * Matrix4x4.CreateRotationY(Rotation.Y) * Matrix4x4.CreateRotationZ(Rotation.Z)));
}

/// <summary>What a container is, which decides its sound and what it gives (the original's <c>TreasureType</c>).</summary>
public enum ContainerKind
{
    Chest,
    Sack,
    Crate,
    Barrel,
    /// <summary>Gives herbs, as many as <see cref="ContainerObject.Gold"/> times the Ranger skill's bonus.</summary>
    Herb,
    /// <summary>Gives spices, as a herb does.</summary>
    Spice,
    /// <summary>Gives gemstones, as a herb does.</summary>
    Gem,
    /// <summary>Loot with a model of its own, which it hides once taken.</summary>
    Custom,
}

/// <summary>
/// Loot the player opens. A chest, sack, crate or barrel is there until it's opened or destroyed,
/// and its model, which it links to, shows it opened or destroyed after. Herb, spice, gem and custom
/// containers are gated as triggers are, by <see cref="MapObject.Visible"/> and
/// <see cref="MapObject.Enabled"/>.
/// </summary>
/// <param name="Opened">Set when it's opened. It stays empty once set. Without one, the original remembers it for a while.</param>
/// <param name="Exploded">Set when its trap goes off and destroys it.</param>
/// <param name="LockLevel">The lock's level, or 0 for none. Picking it opens it.</param>
/// <param name="TrapLevel">The trap's level, or 0 for none. Disarming it opens it; failing sets it off.</param>
/// <param name="Loot">
/// A loot table it rolls when opened. Two containers in map 0x10F5 name a potion instead, and the
/// game takes the loot table at the potion's index, as the import does.
/// </param>
/// <param name="Gold">Gold, or for a herb, spice or gem source, how many it gives before the Ranger skill's bonus.</param>
/// <param name="Item">An item it always holds.</param>
public sealed record ContainerObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    ContainerKind Kind,
    RecordId? Opened,
    RecordId? Exploded,
    int LockLevel,
    int TrapLevel,
    RecordId? Loot,
    int Gold,
    RecordId? Item) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>How a light's colour changes (the original's <c>LightTypes</c>).</summary>
public enum LightKind
{
    Static,
    /// <summary>Fades back and forth between the colours, each way taking <see cref="LightObject.Speed"/> × 30 ticks.</summary>
    Alternate,
    /// <summary>Swings between the colours along a sine, a cycle taking <see cref="LightObject.Speed"/> × 60 ticks.</summary>
    Sine,
    /// <summary>Drifts toward random points between the colours, 1 / (<see cref="LightObject.Speed"/> × 60) of the way a tick.</summary>
    Random,
}

/// <summary>A light that colours the bodies near it.</summary>
/// <param name="From">The colour it starts at. <see cref="LightKind.Static"/> lights stay there.</param>
/// <param name="Occludable">Collision between it and a body blocks it.</param>
public sealed record LightObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    Rgba From,
    Rgba To,
    LightKind Kind,
    float Speed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Occludable) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>How a sound plays (the original's <c>VoxAudioFlags</c>).</summary>
[Flags]
public enum SoundFlags
{
    None = 0,
    Loop = 0x1,
    /// <summary>Plays again after a random wait between <see cref="SoundObject.MinWait"/> and <see cref="SoundObject.MaxWait"/> seconds.</summary>
    Repeat = 0x2,
    /// <summary>Doesn't get quieter with distance.</summary>
    NoFalloff = 0x4,
    /// <summary>Doesn't pan with the camera.</summary>
    NoPan = 0x8,
    /// <summary>Music, at the music volume. Only played with the Expansion Pak.</summary>
    Music = 0x10,
}

/// <summary>A sound or piece of music that plays while the player is within its radius.</summary>
/// <param name="Sound">What it plays.</param>
public sealed record SoundObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    RecordId Sound,
    float Volume,
    float MinWait,
    float MaxWait,
    SoundFlags Flags) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>What wanderers do at a node (the original's <c>nodeflags</c>).</summary>
[Flags]
public enum WanderFlags
{
    None = 0,
    /// <summary>Wanderers walk to the node. A party's home node must have it.</summary>
    Move = 0x1,
    /// <summary>Wanderers turn to face away from the node instead.</summary>
    Rotate = 0x2,
}

/// <summary>A point wandering NPCs and monster parties move between. Each party names its home node.</summary>
/// <param name="WanderRadius">How far a wanderer can stray from its goal before it walks back.</param>
/// <param name="Scatter">Each visit's goal is up to this far from the node, in a random direction.</param>
/// <param name="Chance">The chance of going on to <see cref="Next"/>[1] rather than [0].</param>
/// <param name="Wait">Ticks before moving on.</param>
/// <param name="Next">The nodes that follow, in the same map.</param>
public sealed record WanderNodeObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    float WanderRadius,
    float Scatter,
    float Chance,
    int Wait,
    WanderFlags Flags,
    IReadOnlyList<RecordId> Next) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>One kind of enemy in a monster party.</summary>
/// <param name="Min">How many a battle always has.</param>
/// <param name="Max">How many it can have, with random extras.</param>
public sealed record PartyEnemy(RecordId Entity, int Min, int Max);

/// <summary>The original's <c>monsterparty_dat.flags</c>.</summary>
[Flags]
public enum PartyFlags
{
    None = 0,
    /// <summary>Set on most parties. The game doesn't read it.</summary>
    Unknown2 = 0x2,
    /// <summary>
    /// Talking to it does nothing, though it has a dialogue: it's the body in a scene a dialogue
    /// object starts.
    /// </summary>
    Silent = 0x4,
}

/// <summary>
/// An NPC, or a group of enemies, which wanders from its home node. It's there while
/// <see cref="MapObject.Enabled"/> holds. With a dialogue, using it talks to it and sets
/// <see cref="MapObject.Used"/>. Without, it's a fight: it's gone while <c>Used</c> is set, and
/// winning sets it.
/// </summary>
/// <param name="Entity">The NPC, or the group's leader, which is what the player sees.</param>
/// <param name="Loot">A loot table the party drops, besides its enemies', or a single item.</param>
/// <param name="MinSize">A battle with fewer enemies than this, after the random extras, has none.</param>
/// <param name="MaxSize">The most enemies a battle with the party has.</param>
/// <param name="Home">The wander node it starts from, in the same map.</param>
/// <param name="Dialogue">The script talking to it starts.</param>
/// <param name="BattleFlag">
/// With a dialogue that starts a battle, the flag winning it sets (flag C). Otherwise a battle sets
/// <see cref="MapObject.Used"/>.
/// </param>
public sealed record MonsterPartyObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    IReadOnlyList<PartyEnemy> Enemies,
    RecordId Entity,
    RecordId? Loot,
    int MinSize,
    int MaxSize,
    RecordId? Home,
    PartyFlags Flags,
    RecordId? Dialogue,
    RecordId? BattleFlag) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>A named point that teleporters, cameras and scripts refer to. Most sit in clusters where cut-scenes place their cameras.</summary>
/// <param name="Id">What scripts name it by, counting from their dialogue object's <see cref="DialogueObject.RefPointBase"/>.</param>
/// <param name="Name">Battle arenas find their spawn and retreat points by name.</param>
/// <param name="Facing">The way a teleporter that arrives at it turns the player.</param>
public sealed record ReferencePointObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    int Id,
    string Name,
    Vector3? Facing) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>A door, ladder or portal to another chunk or map set.</summary>
/// <param name="Arrival">
/// The reference point in the destination chunk the player arrives at, facing its
/// <see cref="ReferencePointObject.Facing"/>. Without one, the player arrives at the teleporter's
/// own position in the destination. One base-game teleporter, patioL2 in Jundar's interiors K07,
/// names a point its destination doesn't have, where the original would crash; it has none.
/// </param>
/// <param name="Camera">
/// A reference point in the destination chunk for the camera. Five base-game teleporters into
/// Jundar's interiors A01 name points it doesn't have, which the original ignores, so they have none.
/// </param>
/// <param name="LockLevel">The lock's level, or 0 for none.</param>
/// <param name="LockFlag">Unlocked while set.</param>
/// <param name="PickedFlag">Set when the lock is picked.</param>
/// <param name="TrapLevel">The trap's level, or 0 for none.</param>
/// <param name="TrapFlag">Disarmed while set.</param>
/// <param name="DisarmFlag">Set when the trap is disarmed.</param>
/// <param name="SecretFlag">
/// A secret door: using it while the flag is clear finds it and sets the flag, and only then does
/// it work.
/// </param>
/// <param name="SecretLevel">The skill a secret door needs before it's there at all, until it's found. 0 in the base game.</param>
/// <param name="Name">For tools.</param>
public sealed record TeleporterObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    ChunkRef Destination,
    RecordId? Arrival,
    RecordId? Camera,
    int LockLevel,
    RecordId? LockFlag,
    RecordId? PickedFlag,
    int TrapLevel,
    RecordId? TrapFlag,
    RecordId? DisarmFlag,
    RecordId? SecretFlag,
    int SecretLevel,
    string Name) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>How a camera object moves the camera (the original's <c>CamObjFlags</c>).</summary>
[Flags]
public enum CameraFlags
{
    None = 0,
    /// <summary>Jumps straight there.</summary>
    Cut = 0x1,
    /// <summary>Moves there at a steady speed over <see cref="CameraObject.Duration"/> ticks.</summary>
    Pan = 0x2,
    /// <summary>Eases there over <see cref="CameraObject.Duration"/> ticks.</summary>
    Sine = 0x4,
    Unknown8 = 0x8,
    Unknown10 = 0x10,
    /// <summary>Sets a camera field from <see cref="CameraObject.Values"/>.Y.</summary>
    Unknown100 = 0x100,
    /// <summary>Sets a camera field from <see cref="CameraObject.Values"/>.X.</summary>
    Unknown200 = 0x200,
    /// <summary>Keeps the camera aimed at the player.</summary>
    Track = 0x2000,
    /// <summary>Moves the camera's aim to the reference point.</summary>
    Aim = 0x4000,
    /// <summary>Moves the camera to the reference point.</summary>
    Position = 0x8000,
}

/// <summary>Takes over the camera while the player is within its radius, or walks on a trigger triangle that names it.</summary>
/// <param name="RefPoint">The reference point in the same map to move to. Without one, just above the player.</param>
/// <param name="Values">Z is <see cref="Duration"/>. X and Y are read with two unexplained flags.</param>
public sealed record CameraObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    RecordId? RefPoint,
    CameraFlags Flags,
    Vector3 Values) : MapObject(Index, Position, Radius, Pak, Visible)
{
    [JsonIgnore]
    public float Duration => Values.Z;
}

/// <summary>
/// Starts a script while <see cref="MapObject.Enabled"/> holds: when the player uses it, walks
/// within its radius if it's tangible, or walks on a trigger triangle that names it.
/// </summary>
/// <param name="Script">The script it starts: a conversation, cut-scene, sign or book.</param>
/// <param name="Stage">The chunk a cut-scene is staged in. Without one, the chunk it's in.</param>
/// <param name="RefPointBase">Added to the reference point numbers the script names (the runner's <c>ScriptStart.RefPoint</c>).</param>
/// <param name="Name">For tools.</param>
public sealed record DialogueObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    RecordId Script,
    ChunkRef? Stage,
    int RefPointBase,
    string Name) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>What a trigger does (the original's <c>VObject_Triggers</c>).</summary>
public enum TriggerKind
{
    Nothing0,
    /// <summary>Sets <see cref="TriggerObject.Flag"/>.</summary>
    SetFlag,
    /// <summary>Asks its linked model, an actor, for animation <see cref="TriggerObject.Request"/>.</summary>
    Animate,
    Nothing3,
    /// <summary>
    /// Changes the collision flags of the map's toggle triangles with id <see cref="TriggerObject.Toggle"/>:
    /// ANDs them with <see cref="TriggerObject.KeepFlags"/>, then ORs in <see cref="TriggerObject.AddFlags"/>.
    /// </summary>
    ChangeCollision,
    Nothing5,
}

/// <summary>
/// Goes off while <see cref="MapObject.Enabled"/> holds, when the player walks within its radius if
/// it's tangible, or walks on a trigger triangle that names it, and writes <see cref="MapObject.Used"/>.
/// </summary>
public sealed record TriggerObject(
    int Index,
    Vector3 Position,
    float Radius,
    PakVariant Pak,
    FlagGate? Visible,
    TriggerKind Kind,
    RecordId? Flag = null,
    int? Request = null,
    int? Toggle = null,
    int? KeepFlags = null,
    int? AddFlags = null) : MapObject(Index, Position, Radius, Pak, Visible);

/// <summary>A save point. The game saves anywhere, and does nothing with them; there are three.</summary>
public sealed record SavePointObject(int Index, Vector3 Position, float Radius, PakVariant Pak, FlagGate? Visible)
    : MapObject(Index, Position, Radius, Pak, Visible);
