using System.Numerics;

namespace HBHT.Rom.Graphics;

/// <summary>
/// A floor of sea under an island's water (docs/systems/rendering.md#seams-between-meshes). The islands' shallows are see-through water
/// over a seabed that stops short of where the opaque sea begins, and nothing lies under the opaque sea, so looking out through the
/// shallows the sky shows under their edge. Under each island scene's opaque sea a flat sheet of it is laid, below the deepest seabed,
/// across the sheet's whole extent: the opaque sea and the seabed hide it from above, and through the shallows it's deeper sea.
/// </summary>
internal static class SeaFloors
{
    /// <summary>How far under the deepest seabed the floor lies.</summary>
    internal const float Below = 0.25f;

    /// <summary>How far apart the floor's points are, across the ground: the fog is worked out at each.</summary>
    internal const float Spacing = 25;

    /// <summary>How far under the sea a non-scrolling point may be and count as seabed.</summary>
    private const float SeabedDepth = 5;

    /// <summary>
    /// The opaque sea draws of the scenes of <paramref name="triangles"/>, each with a floor under it, every triangle of the draw as
    /// well, as a scene's draw is replaced (<see cref="SceneCut"/>), by the scene's archive index.
    /// </summary>
    internal static Dictionary<int, SceneCut> Lay(IReadOnlyList<ChunkCuts.Source> sources, IReadOnlyList<ChunkCuts.Triangle> triangles)
    {
        var scenes = new Dictionary<int, SceneCut>();
        foreach (var bySource in triangles.GroupBy(t => t.Source))
        {
            // The scene's sea: its opaque scrolling draw that covers the most.
            var sea = bySource.Where(t => t.Scrolls && t.Opaque).GroupBy(t => t.Draw).MaxBy(g => g.Sum(Area));
            if (sea is null)
                continue;
            var sheet = sea.ToList();
            var points = sheet.SelectMany(t => new[] { t.A.World, t.B.World, t.C.World }).ToList();
            var height = points.Average(p => p.Y);
            var (x0, x1, z0, z1) = (points.Min(p => p.X), points.Max(p => p.X), points.Min(p => p.Z), points.Max(p => p.Z));
            // Below every seabed point under the sheet, the island's chunks' all.
            var seabed = triangles.Where(t => !t.Scrolls).SelectMany(t => new[] { t.A.World, t.B.World, t.C.World })
                .Where(p => p.X >= x0 && p.X <= x1 && p.Z >= z0 && p.Z <= z1 && p.Y < height && p.Y > height - SeabedDepth)
                .Select(p => p.Y).DefaultIfEmpty(height).Min();
            var floor = seabed - Below;

            // Laid as the sheet lays its texture and shades its corners.
            var model = sheet.Select(t => (t, Map: Mapping(t))).FirstOrDefault(m => m.Map is not null);
            if (model.Map is not { } map)
                continue;
            var look = sheet.SelectMany(t => new[] { t.A.Vertex, t.B.Vertex, t.C.Vertex }).GroupBy(v => (v.Normal, v.Color)).MaxBy(g => g.Count())!.First();
            Matrix4x4.Invert(model.t.World, out var toNode);
            var origin = sources[bySource.Key].Origin;
            CutVertex At(float x, float z)
            {
                var world = new Vector3(x, floor, z);
                return look with { Position = Vector3.Transform(world - origin, toNode), St = map.Origin + map.S * x + map.T * z };
            }
            var up = Vector3.Cross(model.t.B.World - model.t.A.World, model.t.C.World - model.t.A.World).Y > 0;
            var polygons = sheet.Select(t => new[] { t.A.Vertex, t.B.Vertex, t.C.Vertex }).ToList();
            var (nx, nz) = ((int)MathF.Ceiling((x1 - x0) / Spacing), (int)MathF.Ceiling((z1 - z0) / Spacing));
            float X(int i) => i == nx ? x1 : x0 + i * (x1 - x0) / nx;
            float Z(int k) => k == nz ? z1 : z0 + k * (z1 - z0) / nz;
            for (var i = 0; i < nx; i++)
            {
                for (var k = 0; k < nz; k++)
                {
                    // This order turns as a triangle whose normal points up does; reversed for a sheet that faces the other way.
                    CutVertex[] quad = [At(X(i), Z(k)), At(X(i), Z(k + 1)), At(X(i + 1), Z(k + 1)), At(X(i + 1), Z(k))];
                    polygons.Add(up ? quad : [.. quad.Reverse()]);
                }
            }
            var scene = sources[bySource.Key].Scene;
            if (!scenes.TryGetValue(scene, out var cut))
                scenes[scene] = cut = new SceneCut();
            cut.Draws[sea.Key] = polygons;
        }
        return scenes;
    }

    /// <summary>How a triangle lays its texture on the world: its coordinates at the origin and per unit of X and Z, or null for one with no area across the ground.</summary>
    private static (Vector2 Origin, Vector2 S, Vector2 T)? Mapping(ChunkCuts.Triangle t)
    {
        var (a, b, c) = (t.A, t.B, t.C);
        var e1 = new Vector2(b.World.X - a.World.X, b.World.Z - a.World.Z);
        var e2 = new Vector2(c.World.X - a.World.X, c.World.Z - a.World.Z);
        var det = e1.X * e2.Y - e1.Y * e2.X;
        if (MathF.Abs(det) < 0.25f)
            return null;
        var (d1, d2) = (b.Vertex.St - a.Vertex.St, c.Vertex.St - a.Vertex.St);
        // St = a.St + S (x - a.x) + T (z - a.z), with S and T the change per unit of X and of Z.
        var s = (d1 * e2.Y - d2 * e1.Y) / det;
        var tz = (d2 * e1.X - d1 * e2.X) / det;
        return (a.Vertex.St - s * a.World.X - tz * a.World.Z, s, tz);
    }

    private static float Area(ChunkCuts.Triangle t) =>
        Vector3.Cross(t.B.World - t.A.World, t.C.World - t.A.World).Length() / 2;
}
