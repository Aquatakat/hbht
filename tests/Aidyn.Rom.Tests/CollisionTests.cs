using System.Numerics;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Tests;

public class CollisionTests
{
    private static readonly Lazy<List<(int Index, Borg9Map Map)>> AllMaps = new(() =>
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        return [.. archive.Listings.Where(l => l.Type == BorgType.MapChunk)
            .Select(l => (l.Index, Borg9Map.Parse(archive.Read(l.Index))))];
    });

    private static IEnumerable<RomCollisionTriangle> AllTriangles => AllMaps.Value.SelectMany(m => m.Map.Triangles);

    [Fact]
    public void EveryBorg9Parses()
    {
        var maps = AllMaps.Value;
        Assert.Equal(584, maps.Count);
        Assert.Equal(168_317, AllTriangles.Count());
        Assert.Equal(26_091, maps.Sum(m => m.Map.Objects.Count));

        var surfaces = AllTriangles.CountBy(t => t.Surface).ToDictionary();
        Assert.Equal(101_394, surfaces[CollisionSurface.PlayerAndCamera]);
        Assert.Equal(10_345, surfaces[CollisionSurface.Player]);
        Assert.Equal(3_165, surfaces[CollisionSurface.Water]);
        Assert.Equal(19, surfaces.Count);

        // The grid starts at the collision's minimum corner and covers all of it, give or take
        // rounding.
        Assert.All(maps.Where(m => m.Map.Vertices.Count > 0), m =>
        {
            var min = m.Map.Vertices.Aggregate(Vector3.Min);
            var max = m.Map.Vertices.Aggregate(Vector3.Max);
            Assert.Equal(m.Map.Origin, min);
            Assert.True(max.X <= m.Map.Origin.X + m.Map.Columns * m.Map.CellSize.X + 0.01f);
            Assert.True(max.Z <= m.Map.Origin.Z + m.Map.Rows * m.Map.CellSize.Y + 0.01f);
        });
    }

    [Fact]
    public void TwentyTwoBorg9sDrawNeighboursWhateverTheView()
    {
        // Header byte 0x1B, which RenderZones and RenderTransZones read for the player's chunk.
        var forcing = AllMaps.Value.Where(m => m.Map.DrawnNeighbours != 0).ToDictionary(m => m.Index, m => m.Map.DrawnNeighbours);
        Assert.Equal(22, forcing.Count);
        Assert.Equal(0x0F, forcing[0x0FFE]);
        Assert.Equal(0xEA, forcing[0x1004]);
    }

    [Fact]
    public void FlagsAreTheOnesThePhysicsReads()
    {
        var flags = AllTriangles.Aggregate(CollisionFlags.None, (all, t) => all | t.Flags);
        Assert.Equal(CollisionFlags.None, flags & (CollisionFlags.Passable | CollisionFlags.TakesSpeed |
            CollisionFlags.RecomputeNormal | CollisionFlags.NoMargin));
        // Invisible walls: the player's surface blocks the player, and the camera sees through it.
        Assert.All(AllTriangles, t =>
            Assert.Equal(t.Surface == CollisionSurface.Player, t.Flags.HasFlag(CollisionFlags.SeeThrough)));
    }

    [Fact]
    public void TrianglesWindCounterClockwiseAboutTheirNormals()
    {
        var mismatched = 0;
        foreach (var (_, map) in AllMaps.Value)
        {
            foreach (var t in map.Triangles)
            {
                var v = map.Vertices;
                var cross = Vector3.Cross(v[t.B] - v[t.A], v[t.C] - v[t.A]);
                if (cross.Length() < 1e-9f || Vector3.Dot(Vector3.Normalize(cross), t.Normal) < 0.99f)
                    mismatched++;
            }
        }
        // 27 have no area, and the rest are slivers whose stored normals point elsewhere.
        Assert.Equal(46, mismatched);
    }

    [Fact]
    public void EdgesNoOtherTriangleSharesHaveTheirEdgeBit()
    {
        foreach (var (index, map) in AllMaps.Value)
        {
            // Some triangles share an edge through different vertices at the same place.
            (float, float, float) Key(int vertex) => (MathF.Round(map.Vertices[vertex].X, 3),
                MathF.Round(map.Vertices[vertex].Y, 3), MathF.Round(map.Vertices[vertex].Z, 3));
            ((float, float, float), (float, float, float)) Edge(int a, int b)
            {
                var (ka, kb) = (Key(a), Key(b));
                return ka.CompareTo(kb) < 0 ? (ka, kb) : (kb, ka);
            }
            var counts = map.Triangles.SelectMany(t => new[] { Edge(t.A, t.B), Edge(t.B, t.C), Edge(t.C, t.A) })
                .CountBy(e => e).ToDictionary();
            foreach (var t in map.Triangles)
            {
                foreach (var (a, b, bit) in new[] { (t.A, t.B, CollisionFlags.EdgeAB), (t.B, t.C, CollisionFlags.EdgeBC),
                    (t.C, t.A, CollisionFlags.EdgeCA) })
                {
                    if (counts[Edge(a, b)] == 1)
                        Assert.True(t.Flags.HasFlag(bit), $"Borg9 {index}: an open edge without {bit}.");
                }
            }
        }
    }

    [Fact]
    public void TriggerAndToggleTrianglesNameTheirObjects()
    {
        var triggered = new List<VoxelType>();
        var toggles = 0;
        foreach (var (_, map) in AllMaps.Value)
        {
            triggered.AddRange(map.Triangles.Where(t => t.TriggerObject != null)
                .Select(t => map.Objects[t.TriggerObject!.Value].Type));

            var changes = map.Objects.Select(o => o.Data).OfType<TriggerData>()
                .Where(t => t.Kind == TriggerKind.ChangeCollision).ToList();
            var ids = map.Triangles.Where(t => t.ToggleId != null).Select(t => (int)t.ToggleId!).ToList();
            toggles += ids.Count;
            Assert.Equal(changes.Select(c => (int)c.A).Order(), ids.Distinct().Order());
            // Each keeps the triangles' flags and adds Passable, so the way opens.
            Assert.All(changes, c => Assert.Equal((0xFFFF, (int)CollisionFlags.Passable), (c.B, c.C)));
        }

        Assert.Equal(1_849, triggered.Count);
        Assert.Equal(1_532, triggered.Count(t => t == VoxelType.Camera));
        Assert.Equal(317, triggered.Count(t => t == VoxelType.Dialogue));
        Assert.Equal(62, toggles);
        Assert.Equal(5_932, AllTriangles.Count(t => t.Role == CollisionRole.ExpansionPak));
    }

    [Fact]
    public void CellsListNearbyTrianglesAndUsableObjects()
    {
        var listed = new HashSet<VoxelType>();
        var unlisted = new List<int>();
        foreach (var (index, map) in AllMaps.Value)
        {
            var inCells = map.Cells.SelectMany(c => c.Triangles).ToHashSet();
            unlisted.AddRange(Enumerable.Repeat(index, map.Triangles.Count - inCells.Count));
            listed.UnionWith(map.Cells.SelectMany(c => c.Objects).Select(o => map.Objects[o].Type));
        }
        // Nothing collides with these: no cell lists them.
        Assert.Equal(26, unlisted.Count);
        Assert.Equal([5211, 5784, 6143, 6294, 6328, 6503, 6515, 13668], unlisted.Distinct());
        Assert.Equal([VoxelType.Scene, VoxelType.Container, VoxelType.Light, VoxelType.Teleporter, VoxelType.Camera,
            VoxelType.Dialogue, VoxelType.Trigger, VoxelType.SavePoint], listed.Order());
    }

    /// <summary>
    /// Neighbouring chunks' collision overlaps, and where it does, the ground should be at the same
    /// height once each chunk is placed at its <see cref="MapSets.ChunkOrigin"/>. Placing rows
    /// along Z instead gets almost none right.
    /// </summary>
    [Fact]
    public void OverworldNeighboursMeetAtTheirBorders()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var maps = AllMaps.Value.ToDictionary(m => m.Index, m => m.Map);
        var chunks = MapChunks.Read(rom).Where(c => c.Set == MapSet.Overworld).ToDictionary(c => (c.Row, c.Column));

        (int Agree, int Compared) Compare(Func<MapChunk, Vector3> origin)
        {
            var (agree, compared) = (0, 0);
            foreach (var chunk in chunks.Values)
            {
                foreach (var next in new[] { (chunk.Row + 1, chunk.Column), (chunk.Row, chunk.Column + 1) })
                {
                    if (!chunks.TryGetValue(next, out var neighbour))
                        continue;
                    var offset = origin(neighbour) - origin(chunk);
                    // Points in a band 10 units deep along the shared side, in the chunk's coordinates.
                    for (var along = 2.5f; along < 100; along += 5)
                    {
                        for (var into = 1f; into < 10; into += 2)
                        {
                            var p = offset.X != 0 ? new Vector2(100 - into, along) : new Vector2(along, 100 - into);
                            if (Ground(maps[chunk.Collision], p) is not { } a ||
                                Ground(maps[neighbour.Collision], p - new Vector2(offset.X, offset.Z)) is not { } b)
                                continue;
                            compared++;
                            if (MathF.Abs(a - b) < 0.05f)
                                agree++;
                        }
                    }
                }
            }
            return (agree, compared);
        }

        var (agree, compared) = Compare(MapSets.ChunkOrigin);
        Assert.True(compared > 1000 && agree > compared * 0.9, $"{agree} of {compared} agree.");
        (agree, compared) = Compare(c => new Vector3(c.Column, 0, c.Row) * 100);
        Assert.True(agree < compared * 0.1, $"{agree} of {compared} agree with rows along Z.");
    }

    /// <summary>
    /// Chests have collision boxes baked into the chunk. Each face of a box should lie along one
    /// of the chest model's axes as <see cref="SceneData.Orientation"/> turns them. That checks the
    /// rotation's axes, order and direction against data the machine code didn't cover: some
    /// chests are knocked over, and Borg5's order (roll and pitch swapped) gets 9 of those 11 wrong.
    /// </summary>
    [Fact]
    public void ChestsLineUpWithTheirCollisionBoxes()
    {
        var results = new List<(bool Tilted, bool Aligned)>();
        foreach (var (_, map) in AllMaps.Value)
        {
            foreach (var chest in map.Objects.Where(o => o.Data is ContainerData { Kind: 0 }))
            {
                foreach (var model in chest.Links.Where(l => l >= 0).Distinct().Select(l => map.Objects[l]))
                {
                    if (model.Data is not SceneData scene)
                        continue;
                    // Triangles within 1 of the chest, leaving out level ground, which says nothing.
                    var near = map.Triangles.Where(t => MathF.Abs(t.Normal.Y) < 0.999f && new[] { t.A, t.B, t.C }.All(v =>
                        Vector2.Distance(new(map.Vertices[v].X, map.Vertices[v].Z), new(model.Position.X, model.Position.Z)) < 1))
                        .ToList();
                    if (near.Count == 0)
                        continue;
                    var axes = new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ }
                        .Select(a => Vector3.Transform(a, scene.Orientation)).ToList();
                    var aligned = near.All(t => axes.Any(a => MathF.Abs(Vector3.Dot(a, t.Normal)) > 0.999f));
                    results.Add((MathF.Abs(scene.Rotation.X) > 0.01f || MathF.Abs(scene.Rotation.Z) > 0.01f, aligned));
                }
            }
        }
        // Every knocked-over chest's box lines up. The upright ones that don't have other walls
        // within reach.
        Assert.Equal(11, results.Count(r => r.Tilted && r.Aligned));
        Assert.Equal(0, results.Count(r => r.Tilted && !r.Aligned));
        Assert.Equal(156, results.Count(r => !r.Tilted && r.Aligned));
        Assert.Equal(12, results.Count(r => !r.Tilted && !r.Aligned));
    }

    [Fact]
    public void CollisionExportsANodePerChunkAtItsOrigin()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var maps = AllMaps.Value.ToDictionary(m => m.Index, m => m.Map);
        var chunks = MapChunks.Read(rom).Where(c => c.Set == MapSet.Lighthouse).Select(c => (c, maps[c.Collision])).ToList();

        var glb = CollisionGltf.Export(chunks);
        var json = System.Text.Json.Nodes.JsonNode.Parse(glb.AsSpan(20, BitConverter.ToInt32(glb, 12)))!;
        var nodes = json["nodes"]!.AsArray();
        Assert.Equal(chunks.Count, nodes.Count);
        Assert.Equal([100f, 0, 100], nodes[0]!["translation"]!.AsArray().Select(v => (float)v!));
        var attributes = json["meshes"]![0]!["primitives"]![0]!["attributes"]!.AsObject().Select(a => a.Key);
        Assert.Equal(["POSITION", "_SURFACE", "_ROLE", "_FLAGS", "_TARGET"], attributes);
        Assert.Equal(chunks.Sum(c => c.Item2.Triangles.Count) * 3,
            json["meshes"]!.AsArray().Sum(m => (int)json["accessors"]![(int)m!["primitives"]![0]!["attributes"]!["POSITION"]!]!["count"]!));
    }

    /// <summary>The highest upward-facing triangle at <paramref name="p"/> (X, Z), or null.</summary>
    private static float? Ground(Borg9Map map, Vector2 p)
    {
        float? best = null;
        foreach (var t in map.Triangles)
        {
            if (t.Normal.Y < 0.3f)
                continue;
            Vector3 a = map.Vertices[t.A], b = map.Vertices[t.B], c = map.Vertices[t.C];
            var d = (b.Z - c.Z) * (a.X - c.X) + (c.X - b.X) * (a.Z - c.Z);
            if (MathF.Abs(d) < 1e-9f)
                continue;
            var l1 = ((b.Z - c.Z) * (p.X - c.X) + (c.X - b.X) * (p.Y - c.Z)) / d;
            var l2 = ((c.Z - a.Z) * (p.X - c.X) + (a.X - c.X) * (p.Y - c.Z)) / d;
            var l3 = 1 - l1 - l2;
            if (l1 < -1e-6f || l2 < -1e-6f || l3 < -1e-6f)
                continue;
            var y = l1 * a.Y + l2 * b.Y + l3 * c.Y;
            if (best == null || y > best)
                best = y;
        }
        return best;
    }
}
