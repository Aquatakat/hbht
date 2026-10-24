using System.Numerics;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Import;

/// <summary>
/// Corrections the importer makes to the ROM's maps, each the user's choice and a departure from the original
/// (docs/hardcoded-content.md#corrections-to-the-roms-maps). Each is applied to the parsed Borg9 before its map record and its
/// collision are written, so what's drawn and what's hit stay together, and checks first that the ROM holds what it corrects.
/// </summary>
internal static class MapCorrections
{
    /// <summary>
    /// A scene object slid across the ground, with the collision triangles that are its body: a box the ROM's map carries in the
    /// chunk's own triangles, whose vertices no other triangle uses.
    /// </summary>
    /// <param name="Object">The scene object's index in the Borg9.</param>
    /// <param name="Scene">The scene it draws, its first model's Borg.</param>
    /// <param name="At">Where the ROM puts it, in the chunk's coordinates.</param>
    /// <param name="FirstTriangle">The first of its collision triangles.</param>
    /// <param name="Triangles">How many there are, one after another.</param>
    /// <param name="By">How far it's moved.</param>
    internal sealed record MovedObject(MapSet Set, string Chunk, int Object, int Scene, Vector3 At, int FirstTriangle, int Triangles, Vector3 By);

    /// <summary>
    /// The mushroom in the Roog Cave's D01 (<c>aidyn:voxel/0x1205/12</c>, scene <c>0x0BCB</c>), in the neck of the passage between
    /// two leaning rocks, at local (11.2, 22.5). Its collision is a box of 8 triangles (926–933), 0.71 square and 1.07 tall. The
    /// box all but touches the rock east of it, so the way past is west of it, against the corner of the other rock at
    /// (10.34, 22.29), where the best way past brings the middle of Alaron's sphere (radius 0.5) within 0.32 of a wall. Running
    /// north to south he wedges there more often than not, and walking or sneaking he can't pass either way; on the console
    /// players squeeze by, or not, or resort to tricks. The user chose to keep the collision as the original has it and move the
    /// mushroom instead. It's slid 0.1 toward the rock east of it, to 5 cm in front of its face, and 0.7 north along that face,
    /// past the corner opposite: (−0.124, 0, 0.696), 0.71 in all. Then he walks, runs or sneaks past it either way. The floor
    /// there is flat, so it stays on the ground. Slid only toward the rock, as far as it goes before its model enters the rock
    /// (0.15), he runs past both ways but still wedges walking.
    /// </summary>
    internal static readonly MovedObject RoogCaveMushroom = new(MapSet.RoogCave, "D01", 12, 0x0BCB,
        new Vector3(11.234437f, -0.11293948f, 22.542595f), 926, 8, new Vector3(-0.124f, 0, 0.696f));

    internal static readonly IReadOnlyList<MovedObject> Moved = [RoogCaveMushroom];

    /// <summary>The ROM's maps by Borg9, with the corrections made to those they name.</summary>
    public static void Apply(IReadOnlyList<MapChunk> chunks, Dictionary<int, Borg9Map> maps)
    {
        foreach (var moved in Moved)
        {
            var borg9 = chunks.Single(c => c.Set == moved.Set && c.Name == moved.Chunk).Collision;
            maps[borg9] = Apply(moved, borg9, maps[borg9]);
        }
    }

    internal static Borg9Map Apply(MovedObject moved, int borg9, Borg9Map map)
    {
        string Where() => $"Borg9 {borg9} ({moved.Set} {moved.Chunk}), object {moved.Object}";
        var o = moved.Object < map.Objects.Count ? map.Objects[moved.Object] : null;
        if (o?.Data is not SceneData scene || scene.Models.Count == 0 || scene.Models[0].Borg != moved.Scene
            || Vector3.Distance(o.Position, moved.At) > 0.001f)
            throw new InvalidDataException($"{Where()} isn't scene 0x{moved.Scene:X4} at {moved.At}, which the importer moves.");
        var body = Enumerable.Range(moved.FirstTriangle, moved.Triangles).ToList();
        if (body[^1] >= map.Triangles.Count)
            throw new InvalidDataException($"{Where()}: the map has no triangle {body[^1]}.");
        var vertices = body.SelectMany(t => new[] { map.Triangles[t].A, map.Triangles[t].B, map.Triangles[t].C }).ToHashSet();
        var shared = map.Triangles.Index().Where(t => !body.Contains(t.Index)
            && (vertices.Contains(t.Item.A) || vertices.Contains(t.Item.B) || vertices.Contains(t.Item.C))).Select(t => t.Index).ToList();
        if (shared.Count > 0)
            throw new InvalidDataException($"{Where()}: its triangles share vertices with triangles {string.Join(", ", shared)}.");
        var centre = vertices.Aggregate(Vector3.Zero, (sum, v) => sum + map.Vertices[v]) / vertices.Count;
        if (new Vector2(centre.X - moved.At.X, centre.Z - moved.At.Z).Length() > 0.01f)
            throw new InvalidDataException($"{Where()}: triangles {body[0]}–{body[^1]} aren't its body, centred at {centre}.");

        // The triangles' normals and the grid's cells stay as they are: a slide keeps the one, and both cells that list the box
        // still cover it.
        return map with
        {
            Vertices = [.. map.Vertices.Select((v, i) => vertices.Contains(i) ? v + moved.By : v)],
            Objects = [.. map.Objects.Select(x => x.Index == o.Index ? x with { Position = x.Position + moved.By } : x)],
        };
    }
}
