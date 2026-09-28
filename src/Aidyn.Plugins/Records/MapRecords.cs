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
/// <c>aidyn:voxel/…</c> ID. Only scene objects are imported so far.
/// </param>
public sealed record MapRecord(RecordId Id, IReadOnlyList<MapObject> Objects);

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

/// <summary>A placed map object: a voxel in the original.</summary>
/// <param name="Index">Within its map.</param>
/// <param name="Position">In chunk coordinates.</param>
/// <param name="Radius">How near the player has to be to use it, or for scenery, how far away it's drawn at all.</param>
/// <param name="Visible">Whether it's there at all.</param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(SceneObject), "scene")]
public record MapObject(
    [property: JsonPropertyOrder(-1)] int Index,
    [property: JsonPropertyOrder(-1)] Vector3 Position,
    [property: JsonPropertyOrder(-1)] float Radius,
    [property: JsonPropertyOrder(-1)] PakVariant Pak,
    [property: JsonPropertyOrder(-1)] FlagGate Visible);

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
