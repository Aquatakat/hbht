using System.Numerics;
using Aidyn.Core.Chunks;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;

namespace Aidyn.Core.Tests;

/// <summary>The grid both worlds share: which triangles a box finds, and in what order (the original's sweeps take the first).</summary>
public class TriangleGridTests
{
    private const int Parts = 3;

    private static List<CollisionTriangle> Scatter(int seed, int count, float origin)
    {
        var random = new Random(seed);
        Vector3 Point(Vector3 near) => near + new Vector3(random.NextSingle() * 14 - 7, random.NextSingle() * 4, random.NextSingle() * 14 - 7);
        var list = new List<CollisionTriangle>();
        for (var i = 0; i < count; i++)
        {
            var centre = new Vector3(origin + random.NextSingle() * 60, 0, random.NextSingle() * 60);
            list.Add(Worlds.Triangle(Point(centre), Point(centre), Point(centre)));
        }
        return list;
    }

    // The order from the definition: cell by cell (X, then Z), each cell's triangles by index, each once.
    private static List<Vector3> Expected(IReadOnlyList<(Vector3 A, Vector3 B, Vector3 C)> part, float minX, float minZ, float maxX, float maxZ)
    {
        static int Cell(float v) => (int)MathF.Floor(v / 4);
        var found = new List<Vector3>();
        var seen = new HashSet<int>();
        for (var x = Cell(minX); x <= Cell(maxX); x++)
        {
            for (var z = Cell(minZ); z <= Cell(maxZ); z++)
            {
                for (var i = 0; i < part.Count; i++)
                {
                    var (a, b, c) = part[i];
                    var touches = x >= Cell(Math.Min(a.X, Math.Min(b.X, c.X))) && x <= Cell(Math.Max(a.X, Math.Max(b.X, c.X)))
                        && z >= Cell(Math.Min(a.Z, Math.Min(b.Z, c.Z))) && z <= Cell(Math.Max(a.Z, Math.Max(b.Z, c.Z)));
                    if (touches && seen.Add(i))
                        found.Add(a);
                }
            }
        }
        return found;
    }

    [Fact]
    public void CollisionWorldListsTrianglesCellByCellInTheOrderAdded()
    {
        var world = new CollisionWorld();
        var offsets = new[] { new Vector3(0, 0, 0), new Vector3(30, 1, -10), new Vector3(-20, 0, 25) };
        var parts = new List<List<(Vector3, Vector3, Vector3)>>();
        for (var p = 0; p < Parts; p++)
        {
            var triangles = Scatter(p + 1, 150, 0);
            world.Add(Worlds.Chunk(p), triangles, offsets[p]);
            parts.Add([.. triangles.Select(t => (t.A + offsets[p], t.B + offsets[p], t.C + offsets[p]))]);
        }

        var near = new List<CollisionWorld.Triangle>();
        var random = new Random(9);
        for (var q = 0; q < 40; q++)
        {
            var (x, z) = (random.NextSingle() * 80 - 10, random.NextSingle() * 80 - 10);
            var (minX, minZ, maxX, maxZ) = (x, z, x + random.NextSingle() * 12, z + random.NextSingle() * 12);

            world.Near(minX, minZ, maxX, maxZ, near);
            var expected = parts.SelectMany(part => Expected(part, minX, minZ, maxX, maxZ)).ToList();
            Assert.Equal(expected, near.Select(t => t.A).ToList());

            world.Near(minX, minZ, maxX, maxZ, near, Worlds.Chunk(1));
            Assert.Equal(Expected(parts[1], minX, minZ, maxX, maxZ), near.Select(t => t.A).ToList());
        }
    }

    /// <summary>
    /// The same cell of two map sets, I08 of the Overworld and of an interior set, are two parts: adding one doesn't replace
    /// the other, each is found alone by its key, and removing one leaves the other.
    /// </summary>
    [Fact]
    public void TheSameCellOfTwoMapSetsIsTwoParts()
    {
        var overworld = new ChunkKey(RecordId.Decimal(RecordTypes.MapSet, 0), 9, 8);
        var interior = new ChunkKey(RecordId.Decimal(RecordTypes.MapSet, 1), 9, 8);
        Assert.NotEqual(overworld, interior);

        var world = new CollisionWorld();
        world.Add(overworld, Worlds.Floor(0, 0, 8, 0, 8), Vector3.Zero);
        world.Add(interior, Worlds.Floor(3, 0, 8, 0, 8), Vector3.Zero);
        Assert.Equal(4, world.Count);

        var near = new List<CollisionWorld.Triangle>();
        world.Near(-1, -1, 9, 9, near, overworld);
        Assert.All(near, t => Assert.Equal((0f, overworld), (t.A.Y, t.Part)));
        Assert.Equal(2, near.Count);
        world.Near(-1, -1, 9, 9, near, interior);
        Assert.All(near, t => Assert.Equal((3f, interior), (t.A.Y, t.Part)));
        Assert.Equal(2, near.Count);

        Assert.True(world.Remove(interior));
        Assert.Equal(2, world.Count);
        world.Near(-1, -1, 9, 9, near);
        Assert.All(near, t => Assert.Equal(overworld, t.Part));
    }

    [Fact]
    public void RemovingAPartAndAddingItAgainReplacesIt()
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(0), Worlds.Floor(0, 0, 8, 0, 8), Vector3.Zero);
        world.Add(Worlds.Chunk(1), Worlds.Floor(0, 0, 8, 0, 8), new Vector3(100, 0, 0));
        Assert.Equal(4, world.Count);

        world.Add(Worlds.Chunk(0), Worlds.Floor(2, 0, 4, 0, 4), Vector3.Zero);
        Assert.Equal(4, world.Count);
        Assert.True(world.Remove(Worlds.Chunk(1)));
        Assert.False(world.Remove(Worlds.Chunk(1)));
        Assert.Equal(2, world.Count);

        var near = new List<CollisionWorld.Triangle>();
        world.Near(-1, -1, 9, 9, near);
        Assert.All(near, t => Assert.Equal(2, t.A.Y));
        world.Clear();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void VisibleWorldRaycastTakesTheNearestSurfaceOfAnyPart()
    {
        var world = new VisibleWorld();
        var offsets = new[] { new Vector3(0, 0, 0), new Vector3(3, 1, -2), new Vector3(-5, 2, 4) };
        var all = new List<(Vector3 A, Vector3 B, Vector3 C)>();
        for (var p = 0; p < Parts; p++)
        {
            var triangles = Scatter(p + 11, 150, 0);
            world.Add(Worlds.Chunk(p), triangles.Drawn(), offsets[p]);
            all.AddRange(triangles.Select(t => (t.A + offsets[p], t.B + offsets[p], t.C + offsets[p])));
        }
        Assert.Equal(all.Count, world.Count);

        // Brute force: the nearest hit of every triangle (Möller–Trumbore, both faces).
        static float? Hit(Vector3 from, Vector3 delta, (Vector3 A, Vector3 B, Vector3 C) t)
        {
            var ab = t.B - t.A;
            var ac = t.C - t.A;
            var p = Vector3.Cross(delta, ac);
            var det = Vector3.Dot(ab, p);
            if (MathF.Abs(det) < 1e-9f)
                return null;
            var s = from - t.A;
            var u = Vector3.Dot(s, p) / det;
            var q = Vector3.Cross(s, ab);
            var v = Vector3.Dot(delta, q) / det;
            var share = Vector3.Dot(ac, q) / det;
            return u is >= 0 and <= 1 && v >= 0 && u + v <= 1 && share is >= 0 and <= 1 ? share : null;
        }

        var random = new Random(5);
        var hits = 0;
        for (var i = 0; i < 300; i++)
        {
            var from = new Vector3(random.NextSingle() * 70, 10, random.NextSingle() * 70);
            var to = from + new Vector3(random.NextSingle() * 6 - 3, -14, random.NextSingle() * 6 - 3);
            var delta = to - from;
            float? best = null;
            foreach (var t in all)
            {
                if (Hit(from, delta, t) is { } share && (best is null || share < best))
                    best = share;
            }

            var found = world.Raycast(from, to, out var point, out _, out _);
            Assert.Equal(best is not null, found);
            if (best is { } nearest)
            {
                hits++;
                Assert.True(Vector3.Distance(from + delta * nearest, point) < 1e-3f);
            }
        }
        Assert.True(hits > 20);
    }
}
