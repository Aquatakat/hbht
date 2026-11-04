using System.Globalization;
using System.Numerics;
using System.Text.Json.Serialization;

namespace HBHT.Plugins.Records;

/// <summary>
/// A set of map chunks loaded as one world, such as the overworld or the Gwernia interiors
/// (<c>aidyn:mapset/n</c>, the original's <c>EnumMapDatA</c>). Teleporters move between sets. Its chunks are records of their own
/// (<see cref="ChunkRecord"/>), which say they're in it: the plugin set finds them by their cell (<see cref="ChunkIndex"/>, ADR 0035).
/// </summary>
/// <param name="Label">The set's name, for tools.</param>
/// <param name="CellSize">The spacing of the chunks, the same along X and Z.</param>
/// <param name="Music">
/// The music that plays in the set, one at random as each chunk is come into, where the chunk has none of its own
/// (<see cref="ChunkRecord.Music"/>). With neither, the music stops.
/// </param>
/// <param name="Spawns">
/// The places the game puts the player when it enters the set by number, not through a teleporter (<c>Zones.SetMapSet</c>'s portals,
/// <c>0x80026264</c>), or null for a set the code gives none. The Overworld's portal 0 is a new game's start
/// (<see cref="NewGameRecord"/>).
/// </param>
/// <param name="Minimap">Its minimap in trek, or none (8 of the 17 have one).</param>
public sealed record MapSetRecord(
    RecordId Id,
    string Label,
    float CellSize,
    IReadOnlyList<RecordId>? Music = null,
    [property: KeyedList(nameof(SpawnPoint.Portal))] IReadOnlyList<SpawnPoint>? Spawns = null,
    MinimapPlan? Minimap = null)
{
    /// <summary>Where a chunk's own coordinates start: rows run along X and columns along Z.</summary>
    public Vector3 Origin(int row, int column) => new Vector3(row, 0, column) * CellSize;

    /// <summary>Where a chunk of the set's own coordinates start.</summary>
    public Vector3 Origin(ChunkRecord chunk) => Origin(chunk.Row, chunk.Column);
}

/// <summary>
/// A place a map set puts the player by number (<c>Zones.SetMapSet</c>): a chunk and where in it, the code's row and column
/// immediates and x and z floats. The player's body is made 10 up and set down from there (<c>InitGameTrek</c>).
/// </summary>
/// <param name="Portal">The number the code is asked for: 0, or 1 in the Misc interiors. A Battle arena's is its number.</param>
/// <param name="Row">As in <see cref="ChunkRecord.Row"/>.</param>
/// <param name="Position">Where in the chunk, x and z.</param>
public sealed record SpawnPoint(int Portal, int Row, int Column, Vector2 Position);

/// <summary>
/// One cell of a map set (<c>aidyn:chunk/&lt;set&gt;/&lt;name&gt;</c>, ADR 0035): its scenery, its collision, and its map objects. The
/// runtime keeps the 3×3 chunks around the player loaded, and draws the scenery of those its <see cref="Shows"/> name and, under Draw
/// Distance's Extended, of the 5×5 (docs/systems/rendering.md#draw-distance). The base plugin writes each in a file of its own,
/// <c>records/chunks/&lt;set&gt;/&lt;name&gt;.json</c>, which is read when the chunk is first asked for (ADR 0036).
/// </summary>
/// <param name="MapSet">The map set it's a cell of.</param>
/// <param name="Row">1-based: row 8 is H. Rows run along X.</param>
/// <param name="Column">Columns run along Z.</param>
/// <param name="Terrain">The terrain type, which the sky, the weather, camping and a battle's arena go by.</param>
/// <param name="Objects">
/// Its map objects, keyed by <see cref="MapObject.Index"/>, which is also the last segment of the object's ID
/// (<c>aidyn:mapobject/8/C05/17</c>, <see cref="ObjectId(int)"/>). Every type is imported but the code objects, which the game does
/// nothing with.
/// </param>
/// <param name="Borg">
/// The Borg9 it came from, such as <c>0x1436</c>, for the emulator and AidynEditor: nothing reads it. The original shares 9 between
/// cells (the sea's and 21 arenas'), which the importer copies into each.
/// </param>
/// <param name="Collision">
/// Its collision, an asset (<c>aidyn:collision/0xHHHH</c>): a glTF mesh in chunk coordinates whose vertices carry their triangle's
/// <c>_SURFACE</c>, <c>_ROLE</c>, <c>_FLAGS</c> and <c>_TARGET</c>. Chunks copied from one Borg9 name the same. None, no collision.
/// </param>
/// <param name="Scene">Its scenery, drawn at its origin.</param>
/// <param name="AltScene">Its see-through scenery, such as water, drawn after everything else.</param>
/// <param name="Music">The music that plays as the player comes into it, before its map set's own.</param>
/// <param name="Region">
/// The world map region flag entering it sets, in trek or a cut-scene's stage, where its terrain draws the sky and its map set has
/// no minimap (<c>LoadGameBorgScenes</c>). The original's table names a chunk by its row and column alone, so a few chunks of
/// other map sets carry the Overworld's flags too.
/// </param>
/// <param name="Swap">The scenes it draws in place of its own while a flag holds, as it loads (<see cref="Drawn"/>).</param>
/// <param name="Fill">
/// What other chunks' scenes drew over its cell that its own don't, which the importer took out of theirs: drawn with it, whatever
/// it swaps (<c>aidyn:fill/n</c>, a glTF in the chunk's coordinates like a scene's). The importer makes a chunk of its own for a
/// cell that had only this, as the original has for some of the sea.
/// </param>
/// <param name="Extended">
/// Whether Draw Distance's Extended draws the 5×5 round it while it's the centre, and draws it in the 5×5 round another. Off, the
/// player in it sees the 3×3, and it isn't drawn further than the 3×3 round another.
/// </param>
/// <param name="Shows">
/// The chunks that draw what its scenes drew over their cells, which the importer took out of its scenes: their scenery is
/// drawn while it's loaded, out to one past the 3×3 or the 5×5 round the player, so the view past the loaded chunks is the
/// chunks themselves rather than a copy of them. Null for none.
/// </param>
/// <param name="Grid">The objects each cell of the chunk's collision grid lists, or none for a chunk without one.</param>
public sealed record ChunkRecord(
    RecordId Id,
    RecordId MapSet,
    int Row,
    int Column,
    RecordId Terrain,
    [property: KeyedList(nameof(MapObject.Index)), JsonPropertyOrder(2)] IReadOnlyList<MapObject> Objects,
    string? Borg = null,
    RecordId? Collision = null,
    RecordId? Scene = null,
    RecordId? AltScene = null,
    RecordId? Music = null,
    RecordId? Region = null,
    SceneSwap? Swap = null,
    RecordId? Fill = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Extended = false,
    IReadOnlyList<RecordId>? Shows = null,
    [property: JsonPropertyOrder(1)] ObjectGrid? Grid = null)
{
    /// <summary>
    /// The neighbours drawn while the player is in this chunk even when the camera's view doesn't cross them
    /// (Borg9 header byte 0x1B, which <c>RenderZones</c> and <c>RenderTransZones</c> read). Only 21 chunks of the
    /// Overworld have any. Null for none.
    /// </summary>
    [JsonPropertyOrder(1)]
    public Neighbours? DrawnNeighbours { get; init; }

    /// <summary>
    /// The chunk as it's drawn by the flags as they are, which it's read by as it loads: with its <see cref="Swap"/>'s scenes
    /// while that holds, else as it is.
    /// </summary>
    public ChunkRecord Drawn(Func<FlagGate, bool> holds) =>
        Swap is { } swap && holds(swap.While) ? this with { Scene = swap.Scene, AltScene = swap.AltScene } : this;

    /// <summary>Every scene it could draw: its own, its <see cref="Swap"/>'s, and its <see cref="Fill"/>.</summary>
    [JsonIgnore]
    public IEnumerable<RecordId> AllScenes =>
        new[] { Scene, AltScene, Swap?.Scene, Swap?.AltScene, Fill }.OfType<RecordId>().Distinct();

    /// <summary>The chunk as the debug overlay names it, such as <c>I08</c> (<see cref="NameOf"/>), whatever its ID.</summary>
    [JsonIgnore]
    public string Name => NameOf(Row, Column);

    /// <summary>
    /// The <see cref="Region"/> entering the chunk sets, or none: only where its terrain draws the sky and its map set has no
    /// minimap (<c>LoadGameBorgScenes</c>, <c>0x8000b5f4</c>).
    /// </summary>
    public RecordId? RegionFound(PluginSet plugins) =>
        Region is { } region && plugins.TryGet<TerrainRecord>(Terrain)?.SkyDrawn == true
            && plugins.TryGet<MapSetRecord>(MapSet)?.Minimap is null
            ? region
            : null;

    /// <summary>Its object at an index, or null.</summary>
    public MapObject? Object(int index) => Objects.FirstOrDefault(o => o.Index == index);

    /// <summary>The ID of its object at <paramref name="index"/>, such as <c>aidyn:mapobject/8/C05/17</c>.</summary>
    public RecordId ObjectId(int index) => ObjectId(Id, index);

    /// <summary>
    /// A cell's name, as the original's debug build prints it (<c>"New Grid: %d-%c%02d"</c>): the row's letter, <c>row − 1 + 'A'</c>,
    /// then the column in two digits, such as <c>C05</c>. A cell outside rows A–Z or at a negative column, which the original has none
    /// of (the importer's sea north of the Overworld's row A), is its row and column, such as <c>0_13</c> and <c>-1_13</c>.
    /// </summary>
    public static string NameOf(int row, int column) =>
        row is >= 1 and <= 26 && column >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"{(char)('A' + row - 1)}{column:D2}")
            : string.Create(CultureInfo.InvariantCulture, $"{row}_{column}");

    /// <summary>The row and column a cell's name (<see cref="NameOf"/>) says, or null for a name it doesn't spell.</summary>
    public static (int Row, int Column)? CellOf(string name)
    {
        if (name.Length >= 3 && name[0] is >= 'A' and <= 'Z'
            && int.TryParse(name.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var column))
            return (name[0] - 'A' + 1, column);
        var underscore = name.IndexOf('_', StringComparison.Ordinal);
        return underscore > 0
            && int.TryParse(name.AsSpan(0, underscore), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var row)
            && int.TryParse(name.AsSpan(underscore + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var col)
            ? (row, col)
            : null;
    }

    /// <summary>A base-game chunk's ID: <c>aidyn:chunk/&lt;set&gt;/&lt;name&gt;</c>, such as <c>aidyn:chunk/8/C05</c>.</summary>
    public static RecordId IdOf(int mapSet, int row, int column) =>
        RecordId.Parse(string.Create(CultureInfo.InvariantCulture, $"{RecordId.BaseNamespace}:{RecordTypes.Chunk}/{mapSet}/{NameOf(row, column)}"));

    /// <summary>A base-game chunk's ID in a map set by its record, <c>aidyn:mapset/n</c>.</summary>
    public static RecordId IdOf(RecordId mapSet, int row, int column) =>
        IdOf(int.Parse(mapSet.Key, CultureInfo.InvariantCulture), row, column);

    /// <summary>
    /// The map set, row and column a base-game chunk's ID names, read from the ID alone (the plugin set indexes the base plugin's
    /// chunks without reading them, ADR 0036); null for a mod's chunk, whose key is a name.
    /// </summary>
    public static (RecordId MapSet, int Row, int Column)? PlaceOf(RecordId chunk)
    {
        if (chunk.Namespace != RecordId.BaseNamespace || chunk.Type != RecordTypes.Chunk)
            return null;
        var slash = chunk.Key.IndexOf('/', StringComparison.Ordinal);
        return CellOf(chunk.Key[(slash + 1)..]) is { } cell
            ? (RecordId.Parse($"{RecordId.BaseNamespace}:{RecordTypes.MapSet}/{chunk.Key[..slash]}"), cell.Row, cell.Column)
            : null;
    }

    /// <summary>The ID of the object at <paramref name="index"/> in <paramref name="chunk"/>, such as <c>aidyn:mapobject/8/C05/17</c>.</summary>
    public static RecordId ObjectId(RecordId chunk, int index) =>
        RecordId.Parse(string.Create(CultureInfo.InvariantCulture, $"{chunk.Namespace}:{RecordTypes.MapObject}/{chunk.Key}/{index}"));

    /// <summary>The chunk a map object's ID names an object of.</summary>
    public static RecordId ChunkOf(RecordId mapObject) =>
        RecordId.Parse($"{mapObject.Namespace}:{RecordTypes.Chunk}/{mapObject.Key[..mapObject.Key.LastIndexOf('/')]}");

    /// <summary>The index within its chunk of the object a map object's ID names, <see cref="MapObject.Index"/>.</summary>
    public static int IndexOf(RecordId mapObject) =>
        int.Parse(mapObject.Key.AsSpan(mapObject.Key.LastIndexOf('/') + 1), NumberStyles.None, CultureInfo.InvariantCulture);

    /// <summary>The object a map object's ID names, or null if no plugin has its chunk or the chunk has none at that index.</summary>
    public static MapObject? Find(PluginSet plugins, RecordId mapObject) =>
        plugins.TryGet<ChunkRecord>(ChunkOf(mapObject))?.Object(IndexOf(mapObject));
}

/// <summary>
/// A map set's minimap in trek (<c>MiniMap</c>), its <see cref="MapSetRecord.Minimap"/>: pieces of a drawn plan, one of which is
/// the player's, chosen by the chunk he's in, and in some chunks by where he stands or by flags. The pieces of the chosen one's group
/// that have been seen show around it, and seeing a piece is saved.
/// </summary>
/// <param name="Sections">The pieces, in the order the game saves whether they've been seen.</param>
/// <param name="Rules">
/// Which piece a chunk shows, where it isn't simply the chunk's own: for the player's chunk, the first rule whose flag
/// and bounds hold chooses its piece, or none. With no rule holding, the chunk's first piece is chosen, and a chunk with
/// none shows no minimap.
/// </param>
public sealed record MinimapPlan(IReadOnlyList<MinimapSection> Sections, IReadOnlyList<MinimapRule> Rules);

/// <summary>
/// A piece of a minimap: its image's top-left at (<paramref name="X"/>, <paramref name="Y"/>) in its group's plan, in
/// screen pixels.
/// </summary>
/// <param name="Row">The row in its map set, counting from 1 for A, of the chunk the piece is for.</param>
/// <param name="Group">Pieces drawn together: a floor or a building.</param>
/// <param name="Seen">Whether it's seen from the start.</param>
public sealed record MinimapSection(int Row, int Column, int Group, RecordId Image, int X, int Y, bool Seen = false);

/// <summary>
/// In the chunk at <paramref name="Row"/> and <paramref name="Column"/> of its map set, chooses piece <paramref name="Section"/>, or
/// none when it's null, while <paramref name="Flag"/> is set and the player is within <paramref name="Bounds"/>.
/// </summary>
public sealed record MinimapRule(int Row, int Column, int? Section, RecordId? Flag = null, IReadOnlyList<MinimapBound>? Bounds = null);

/// <summary>A bound on where the player stands in the chunk, in its own coordinates: below a value, above one, or both.</summary>
public sealed record MinimapBound(MinimapAxis Axis, float? Below = null, float? Above = null);

public enum MinimapAxis { X, Y, Z }

/// <summary>
/// The eight chunks around one, a bit each, in the order the original's zone engine walks them (its table at
/// <c>0x800CD700</c> in 1.0): the corners, then the sides. Rows run along X, east, and columns along Z, south.
/// </summary>
[Flags]
public enum Neighbours
{
    None = 0,
    /// <summary>Row − 1, column − 1.</summary>
    NorthWest = 0x01,
    /// <summary>Row + 1, column − 1.</summary>
    NorthEast = 0x02,
    /// <summary>Row − 1, column + 1.</summary>
    SouthWest = 0x04,
    /// <summary>Row + 1, column + 1.</summary>
    SouthEast = 0x08,
    /// <summary>Column − 1.</summary>
    North = 0x10,
    /// <summary>Column + 1.</summary>
    South = 0x20,
    /// <summary>Row − 1.</summary>
    West = 0x40,
    /// <summary>Row + 1.</summary>
    East = 0x80,
}

/// <summary>
/// The chunk's collision grid as it lists objects (<c>CollideSection</c>): A weighs only the objects the
/// cell the player is in lists (<c>obj_ref_func</c>), which an object's radius alone doesn't bound. The
/// grid is in the chunk's coordinates, from <see cref="Origin"/> in X and Z.
/// </summary>
/// <param name="Cells">Row by row: the cell at (x, z) is <c>Cells[z × Columns + x]</c>, each the indices of the objects it lists.</param>
public sealed record ObjectGrid(Vector2 Origin, Vector2 CellSize, int Columns, int Rows, IReadOnlyList<IReadOnlyList<int>> Cells)
{
    /// <summary>
    /// The objects listed by the cell a point is in (<c>getZonePositionShorts</c>, <c>getCollideSection</c>).
    /// The cell is found truncating toward zero, so up to a cell before the grid counts as its first, and a
    /// point off the grid, either way, counts as in the first cell, (0, 0), as the machine code has it.
    /// </summary>
    public IReadOnlyList<int> At(Vector3 point)
    {
        var x = (int)((point.X - Origin.X) / CellSize.X);
        var z = (int)((point.Z - Origin.Y) / CellSize.Y);
        return x < 0 || z < 0 || x >= Columns || z >= Rows ? Cells[0] : Cells[z * Columns + x];
    }
}

/// <summary>A condition on an event flag: true when the flag's value differs from <see cref="Invert"/>.</summary>
public sealed record FlagGate(RecordId Flag, bool Invert = false);

/// <summary>
/// The scenes a chunk draws in place of its own while a flag holds, such as the lighthouse's lit ones
/// (<see cref="ChunkRecord.Swap"/>).
/// </summary>
/// <param name="While">The flag, read as the chunk loads.</param>
/// <param name="Scene">Its scenery then, as <see cref="ChunkRecord.Scene"/>.</param>
/// <param name="AltScene">Its see-through scenery then, as <see cref="ChunkRecord.AltScene"/>.</param>
public sealed record SceneSwap(FlagGate While, RecordId? Scene, RecordId? AltScene);

/// <summary>A flag an object writes when it's used: it sets it, or clears it if <see cref="Clear"/>.</summary>
public sealed record FlagWrite(RecordId Flag, bool Clear = false);

/// <summary>
/// A placed map object: a voxel in the original (<c>aidyn:mapobject/&lt;chunk&gt;/&lt;index&gt;</c>, ADR 0035). The flag slots are the
/// original's C, A and B, and each type has the ones the game reads; the others are left out.
/// </summary>
/// <param name="Index">Within its chunk.</param>
/// <param name="Position">In chunk coordinates.</param>
/// <param name="Radius">
/// How near the player has to be to use it or to set it off, or for scenery, how far away it's
/// drawn at all.
/// </param>
/// <param name="Visible">
/// Whether it's there at all, as the chunk loads (flag C). Scenes, lights, sounds, wander nodes,
/// reference points, teleporters, cameras, tripwires, and herb, spice, gem and custom containers
/// have one. Npcs, encounters, script objects and save points are always there, but for their
/// <see cref="Enabled"/>. Null means always, as the importer writes the original's flag 0 inverted.
/// </param>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(SceneObject), "scene")]
[JsonDerivedType(typeof(ContainerObject), "container")]
[JsonDerivedType(typeof(LightObject), "light")]
[JsonDerivedType(typeof(SoundObject), "sound")]
[JsonDerivedType(typeof(WanderNodeObject), "wanderNode")]
[JsonDerivedType(typeof(EncounterObject), "encounter")]
[JsonDerivedType(typeof(NpcObject), "npc")]
[JsonDerivedType(typeof(ReferencePointObject), "refPoint")]
[JsonDerivedType(typeof(TeleporterObject), "teleporter")]
[JsonDerivedType(typeof(CameraObject), "camera")]
[JsonDerivedType(typeof(ScriptObject), "script")]
[JsonDerivedType(typeof(TripwireObject), "tripwire")]
[JsonDerivedType(typeof(SavePointObject), "savePoint")]
public record MapObject(
    [property: JsonPropertyOrder(-2)] int Index,
    [property: JsonPropertyOrder(-2)] Vector3 Position,
    [property: JsonPropertyOrder(-2)] float Radius,
    [property: JsonPropertyOrder(-2)] FlagGate? Visible)
{
    /// <summary>
    /// Whether it works (flag A): whether a teleporter, tripwire, script object or herb, spice, gem or
    /// custom container can be used, and whether an npc or an encounter is there at all. Null means always.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public FlagGate? Enabled { get; init; }

    /// <summary>
    /// What using it writes (flag B): opening a container, setting off a tripwire, talking to an npc or
    /// beating an encounter. Left out where it would write flag 0, which the game ignores.
    /// </summary>
    [JsonPropertyOrder(-1)]
    public FlagWrite? Used { get; init; }

    /// <summary>
    /// A teleporter, tripwire or script object that goes off when the player walks within its radius,
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
    /// Another object in the same chunk it works with. To find a container's model or the actor a
    /// tripwire animates, the game follows links from object to object until it reaches one of the
    /// type it wants (<c>GetVoxelObjectFromLink</c>, checked in 1.0's code).
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
public sealed record SceneModel(RecordId Model, float DrawDistance)
{
    /// <summary>
    /// The scene draws nothing: every draw is one the game skips, as a hidden model's. The first level of Oriana's
    /// path lights (<c>0x1910</c>) is one, a grey quad whose model is hidden, which only marks where the light goes
    /// out. The nearest detail draws nothing where the original draws only it (<c>ObjectDistance.NearestDetail</c>).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Empty { get; init; }
}

/// <summary>
/// A model placed in the chunk: scaled, then rotated, then moved to <see cref="MapObject.Position"/>.
/// </summary>
/// <param name="Models">Its levels of detail, nearest first. A container's model has one.</param>
/// <param name="Rotation">
/// In radians: roll about X, then pitch about Y, then heading about Z (libultra's
/// <c>guRotateRPYF</c>). Most objects only turn about Y, the vertical.
/// </param>
public sealed record SceneObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    IReadOnlyList<SceneModel> Models,
    Vector3 Rotation,
    Vector3 Scale,
    Rgba Tint,
    SceneObjectFlags Flags) : MapObject(Index, Position, Radius, Visible)
{
    /// <summary>
    /// It goes out once the player reaches it: when its first level of detail is drawn and its second isn't, it
    /// stops being active, and what map objects remember keeps it out for 16 map loads. The original does this
    /// for the objects whose second model is <c>BORG5_OrianaLight</c> (0x374A), Oriana's path lights, testing that
    /// index in <c>RenderVoxelScenes</c>; the importer marks them.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool GoesOut { get; init; }

    /// <summary>
    /// For a container's model, what its nearest level shows once the container is opened (<c>replace_container_voxel</c>).
    /// The 370 scene objects containers link to all have one, and only they do.
    /// </summary>
    public SceneModel? Opened { get; init; }

    /// <summary>For a container's model, what its nearest level shows once the container's trap destroys it.</summary>
    public SceneModel? Destroyed { get; init; }

    /// <summary>
    /// What its nearest level shows: its first model, or once the container linked to it is opened or destroyed,
    /// <see cref="Opened"/> or <see cref="Destroyed"/> where it has one.
    /// </summary>
    public SceneModel Nearest(bool opened, bool destroyed) =>
        (opened ? Opened : destroyed ? Destroyed : null) ?? Models[0];

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
/// <param name="Reach">
/// How near the player's sphere has to be to use it, which the player's radius is added to: 2 for most,
/// where <see cref="MapObject.Radius"/> is 10, and 2 if not given.
/// </param>
public sealed record ContainerObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    ContainerKind Kind,
    RecordId? Opened,
    RecordId? Exploded,
    int LockLevel,
    int TrapLevel,
    RecordId? Loot,
    int Gold,
    RecordId? Item,
    float Reach = 2) : MapObject(Index, Position, Radius, Visible);

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

/// <summary>
/// A light that colours the player and the actors the map places within its radius, more the nearer they are (see
/// <c>HBHT.Core.Lights.WorldLights</c>).
/// </summary>
/// <param name="From">The colour it starts at. <see cref="LightKind.Static"/> lights stay there.</param>
/// <param name="Speed">Seconds a way, a cycle or the whole way, by <see cref="Kind"/>.</param>
/// <param name="Occludable">Collision between it and a body blocks it.</param>
/// <param name="FacesCamera">It shines on a body from the camera's eye, not from where it is, which only its distance is measured from.</param>
public sealed record LightObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    Rgba From,
    Rgba To,
    LightKind Kind,
    float Speed,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Occludable,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool FacesCamera = false) : MapObject(Index, Position, Radius, Visible);

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
    FlagGate? Visible,
    RecordId Sound,
    float Volume,
    float MinWait,
    float MaxWait,
    SoundFlags Flags) : MapObject(Index, Position, Radius, Visible);

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

/// <summary>A point npcs and encounters wander between. Each names its home node.</summary>
/// <param name="WanderRadius">How far a wanderer can stray from its goal before it walks back.</param>
/// <param name="Scatter">Each visit's goal is up to this far from the node, in a random direction.</param>
/// <param name="Chance">The chance of going on to <see cref="Next"/>[1] rather than [0].</param>
/// <param name="Wait">Ticks before moving on.</param>
/// <param name="Next">The nodes that follow, in the same chunk.</param>
public sealed record WanderNodeObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    float WanderRadius,
    float Scatter,
    float Chance,
    int Wait,
    WanderFlags Flags,
    IReadOnlyList<RecordId> Next) : MapObject(Index, Position, Radius, Visible);

/// <summary>One kind of enemy in a party.</summary>
/// <param name="Min">How many a battle always has.</param>
/// <param name="Max">How many it can have, with random extras.</param>
public sealed record PartyEnemy(RecordId Entity, int Min, int Max);

/// <summary>The original's <c>monsterparty_dat.flags</c>.</summary>
[Flags]
public enum PartyFlags
{
    None = 0,
    /// <summary>
    /// Walks to its wander nodes rather than running (<c>FUN_80012d44</c>, which the decomp reads as a
    /// wander node's flags; 1.0's code reads the party's). Set on 990 of the 1,079 npcs and encounters.
    /// </summary>
    Walks = 0x2,
    /// <summary>
    /// Talking to it does nothing, though it has a script: it's the body in a scene a script
    /// object starts. Only an npc has it.
    /// </summary>
    Silent = 0x4,
}

/// <summary>
/// Someone or something that wanders from its home node: the original's monster party, which is an <see cref="NpcObject"/> with a
/// script or an <see cref="EncounterObject"/> without one (ADR 0035). It's there while <see cref="MapObject.Enabled"/> holds.
/// </summary>
/// <param name="Entity">Who the player sees: the npc, or an encounter's leader.</param>
/// <param name="Home">The wander node it starts from, in the same chunk.</param>
public abstract record PartyObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    [property: JsonPropertyOrder(-1)] RecordId Entity,
    [property: JsonPropertyOrder(-1)] RecordId? Home,
    [property: JsonPropertyOrder(-1)] PartyFlags Flags) : MapObject(Index, Position, Radius, Visible)
{
    /// <summary>Who fights the party in a battle with it. An npc whose script starts no battle has none.</summary>
    [JsonIgnore]
    public abstract IReadOnlyList<PartyEnemy> Fighters { get; }

    /// <summary>A loot table the party drops in a battle with it, besides its enemies', or a single item.</summary>
    [JsonIgnore]
    public abstract RecordId? BonusLoot { get; }

    /// <summary>A battle with fewer enemies than this, after the random extras, has none.</summary>
    [JsonIgnore]
    public abstract int FewestFighters { get; }

    /// <summary>The most enemies a battle with it has.</summary>
    [JsonIgnore]
    public abstract int MostFighters { get; }

    /// <summary>
    /// The battle's encounter flag (<c>BattleSetup.FromMonsterParty</c>): an encounter's <see cref="MapObject.Used"/>, which winning
    /// sets, or an npc's <see cref="NpcObject.BattleFlag"/>, which winning its battle sets too.
    /// </summary>
    [JsonIgnore]
    public abstract RecordId? EncounterFlag { get; }

    /// <summary>
    /// The rules of a battle with it, or none: what the original keys by its encounter flag (<c>Combat.ApplyFleeRuleByFlag</c> and
    /// the Goblin Ambush's), which the importer puts here (ADR 0035).
    /// </summary>
    [JsonPropertyOrder(1)]
    public BattleRules? BattleRules { get; init; }
}

/// <summary>
/// A group of enemies that wanders from its home node and fights the party: using it, or its catching the party, starts a battle
/// (<c>monsterpary_func</c>, <c>WanderTick</c>). It's gone while <see cref="MapObject.Used"/> is set, and winning sets it, so
/// <c>Used</c> is the flag the original keys the battle's rules on, its encounter flag.
/// </summary>
/// <param name="Enemies">Who fights: each kind with how many always come and how many can.</param>
/// <param name="Loot">A loot table the party drops, besides its enemies', or a single item.</param>
/// <param name="MinSize">A battle with fewer enemies than this, after the random extras, has none.</param>
/// <param name="MaxSize">The most enemies a battle with it has.</param>
public sealed record EncounterObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    RecordId Entity,
    RecordId? Home,
    PartyFlags Flags,
    IReadOnlyList<PartyEnemy> Enemies,
    RecordId? Loot,
    int MinSize,
    int MaxSize) : PartyObject(Index, Position, Radius, Visible, Entity, Home, Flags)
{
    [JsonIgnore]
    public override IReadOnlyList<PartyEnemy> Fighters => Enemies;

    [JsonIgnore]
    public override RecordId? BonusLoot => Loot;

    [JsonIgnore]
    public override int FewestFighters => MinSize;

    [JsonIgnore]
    public override int MostFighters => MaxSize;

    [JsonIgnore]
    public override RecordId? EncounterFlag => Used?.Flag;
}

/// <summary>
/// Someone who wanders from its home node and talks: using it starts its script and sets <see cref="MapObject.Used"/>. A silent npc
/// is the body in a scene a script object starts, and using it does nothing. Its script can start a battle, which the original fights
/// against the npc's party rather than the script's list (<c>0x80051A4C</c>): its <see cref="Enemies"/>, <see cref="Loot"/> and size,
/// with <see cref="BattleFlag"/> set by winning. 35 of the base game's npcs fight that way.
/// </summary>
/// <param name="Script">The script talking to it starts.</param>
public sealed record NpcObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    RecordId Entity,
    RecordId? Home,
    PartyFlags Flags,
    RecordId Script) : PartyObject(Index, Position, Radius, Visible, Entity, Home, Flags)
{
    /// <summary>
    /// Who fights the party when its script starts a battle, or none. The original gives every npc a list, most naming only the npc
    /// itself; the importer leaves out those that name only the npc, or none, where the script starts no battle (355), and keeps the
    /// rest.
    /// </summary>
    public IReadOnlyList<PartyEnemy>? Enemies { get; init; }

    /// <summary>A loot table the party drops in its battle, besides its enemies', or a single item.</summary>
    public RecordId? Loot { get; init; }

    /// <summary>A battle with fewer enemies than this, after the random extras, has none. 0 if not given.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MinSize { get; init; }

    /// <summary>The most enemies its battle has. 0 if not given.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int MaxSize { get; init; }

    /// <summary>The flag winning the battle its script starts sets (the original's flag C), which the battle's rules keyed on.</summary>
    public RecordId? BattleFlag { get; init; }

    /// <summary>
    /// The script object whose marks and stage its script plays a cut-scene with, rather than from marks 0 in the chunk the player is
    /// in: the port's fix, chosen by the user, for a cut-scene that names marks only that object's find, which the original crashes on
    /// (Keelin's in Terminor C05; docs/systems/scripts.md). The importer sets it where the npc isn't silent, its cut-scene names a mark
    /// that marks 0 doesn't find in its chunk, and a script object in its chunk that plays the same script finds them all.
    /// </summary>
    public RecordId? Stage { get; init; }

    [JsonIgnore]
    public override IReadOnlyList<PartyEnemy> Fighters => Enemies ?? [];

    [JsonIgnore]
    public override RecordId? BonusLoot => Loot;

    [JsonIgnore]
    public override int FewestFighters => MinSize;

    [JsonIgnore]
    public override int MostFighters => MaxSize;

    [JsonIgnore]
    public override RecordId? EncounterFlag => BattleFlag;
}

/// <summary>A named point that teleporters, cameras and scripts refer to. Most sit in clusters where cut-scenes place their cameras.</summary>
/// <param name="Mark">
/// What scripts name it by, counting from the number they were started with: a script object's <see cref="ScriptObject.Marks"/>.
/// </param>
/// <param name="Name">Battle arenas find their spawn and retreat points by name.</param>
/// <param name="Facing">The way a teleporter that arrives at it turns the player.</param>
public sealed record ReferencePointObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    int Mark,
    string Name,
    Vector3? Facing) : MapObject(Index, Position, Radius, Visible);

/// <summary>A door, ladder or portal to another chunk or map set.</summary>
/// <param name="Destination">
/// The chunk it leads to, written only where it has no <see cref="Arrival"/>: an arrival's chunk is the destination
/// (<see cref="Leads"/>).
/// </param>
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
    FlagGate? Visible,
    RecordId? Destination,
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
    string Name) : MapObject(Index, Position, Radius, Visible)
{
    /// <summary>The chunk it leads to: its <see cref="Arrival"/>'s, else its <see cref="Destination"/>; null for a teleporter with neither.</summary>
    [JsonIgnore]
    public RecordId? Leads => Arrival is { } arrival ? ChunkRecord.ChunkOf(arrival) : Destination;
}

/// <summary>How a camera object moves the camera (the original's <c>CamObjFlags</c>).</summary>
[Flags]
public enum CameraFlags
{
    None = 0,
    /// <summary>Jumps straight there.</summary>
    Cut = 0x1,
    /// <summary>Moves there at a steady speed over <see cref="CameraObject.Ticks"/> ticks.</summary>
    Pan = 0x2,
    /// <summary>Eases there over <see cref="CameraObject.Ticks"/> ticks.</summary>
    Sine = 0x4,
    /// <summary>Eases in there, slow then fast (1 − cos), over <see cref="CameraObject.Ticks"/> ticks.</summary>
    EaseIn = 0x8,
    /// <summary>Eases out there, fast then slow (sin), over <see cref="CameraObject.Ticks"/> ticks.</summary>
    EaseOut = 0x10,
    /// <summary>Has the outdoor camera follow <see cref="CameraObject.Distance"/> behind, but no nearer than 3.</summary>
    Distance = 0x100,
    /// <summary>Has the outdoor camera follow <see cref="CameraObject.Pitch"/> degrees up.</summary>
    Pitch = 0x200,
    /// <summary>Keeps the camera aimed at the player.</summary>
    Track = 0x2000,
    /// <summary>Moves the camera's aim to the reference point.</summary>
    Aim = 0x4000,
    /// <summary>Moves the camera to the reference point.</summary>
    Position = 0x8000,
}

/// <summary>Takes over the camera while the player is within its radius, or walks on a trigger triangle that names it.</summary>
/// <param name="RefPoint">The reference point in the same chunk to move to. Without one, just above the player.</param>
/// <param name="Ticks">How long a pan, sine, ease in or ease out takes (the original's values' Z). 0 if not given.</param>
/// <param name="Pitch">Degrees up the outdoor camera follows at, for <see cref="CameraFlags.Pitch"/> (X). 0 if not given.</param>
/// <param name="Distance">How far behind the outdoor camera follows, for <see cref="CameraFlags.Distance"/> (Y). 0 if not given.</param>
public sealed record CameraObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    RecordId? RefPoint,
    CameraFlags Flags,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] float Ticks = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] float Pitch = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] float Distance = 0) : MapObject(Index, Position, Radius, Visible);

/// <summary>
/// Starts a script while <see cref="MapObject.Enabled"/> holds: when the player uses it, walks
/// within its radius if it's tangible, or walks on a trigger triangle that names it. The original's dialogue object.
/// </summary>
/// <param name="Plays">The script it starts: a conversation, cut-scene, sign or book.</param>
/// <param name="Stage">The chunk a cut-scene is staged in. Without one, the chunk it's in.</param>
/// <param name="Marks">
/// The number the script's marks count from (<c>ScriptStart.Marks</c>): a script names mark 3, and the stage's reference point
/// <see cref="ReferencePointObject.Mark"/> <c>Marks + 3</c> is meant. A script names marks, not map objects.
/// </param>
/// <param name="Name">For tools.</param>
public sealed record ScriptObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    RecordId Plays,
    RecordId? Stage,
    int Marks,
    string Name) : MapObject(Index, Position, Radius, Visible);

/// <summary>
/// Goes off while <see cref="MapObject.Enabled"/> holds, when the player walks within its radius if
/// it's tangible, or walks on a trigger triangle that names it: it does <see cref="Does"/> and writes
/// <see cref="MapObject.Used"/>. The original calls it a trigger (<c>VObject_Triggers</c>); the port says tripwire, since
/// trigger records (<see cref="TriggerRecord"/>) are something else.
/// </summary>
/// <param name="Does">What it does when it goes off.</param>
public sealed record TripwireObject(
    int Index,
    Vector3 Position,
    float Radius,
    FlagGate? Visible,
    TripwireAction Does) : MapObject(Index, Position, Radius, Visible);

/// <summary>
/// What a tripwire does, written with its <c>type</c>: the original's kinds 1, 2 and 4 (<c>VObject_Triggers</c>). Its kinds 0, 3
/// and 5 do nothing, and no object has one. A type no build knows loads as a plain <see cref="TripwireAction"/>, which does nothing.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", IgnoreUnrecognizedTypeDiscriminators = true)]
[JsonDerivedType(typeof(SetFlagAction), "setFlag")]
[JsonDerivedType(typeof(AnimateAction), "animate")]
[JsonDerivedType(typeof(ChangeCollisionAction), "changeCollision")]
public record TripwireAction;

/// <summary>Sets a flag.</summary>
public sealed record SetFlagAction(RecordId Flag) : TripwireAction;

/// <summary>Asks the tripwire's linked model, an actor, for an animation request.</summary>
public sealed record AnimateAction(RecordId Request) : TripwireAction;

/// <summary>
/// Changes the collision flags of the chunk's toggle triangles with id <see cref="Toggle"/>: ANDs them with <see cref="KeepFlags"/>,
/// then ORs in <see cref="AddFlags"/>.
/// </summary>
public sealed record ChangeCollisionAction(int Toggle, int KeepFlags, int AddFlags) : TripwireAction;

/// <summary>A save point. The game saves anywhere, and does nothing with them; there are three.</summary>
public sealed record SavePointObject(int Index, Vector3 Position, float Radius, FlagGate? Visible)
    : MapObject(Index, Position, Radius, Visible);
