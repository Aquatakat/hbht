using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Chunks;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Trek;

/// <summary>
/// The collision walls that nothing drawn stands at, for the enhanced trek camera's question whether its boom may pass them
/// (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced): every wall triangle (under 0.45 up or down) the camera's
/// sweeps meet (not see-through), sampled at its centre and at the middles from there to its corners, a sample counted as
/// drawn where a solid drawn surface is within <see cref="Near"/> of it across the wall. By area, interiors and outdoors apart,
/// and how high such walls reach above the floor at their foot.
/// </summary>
public static class CameraBarriers
{
    /// <summary>How near a drawn surface must be across the wall for it to stand there.</summary>
    public const float Near = 0.35f;

    public sealed record Tally(float WallArea, float BareArea, Dictionary<int, float> BareBySurface, List<float> BareTops, int Chunks);

    /// <param name="perChunk">Told each chunk's area of wholly bare walls under 2.2 over the floor: furniture.</param>
    public static (Tally Interior, Tally Outdoor) Run(PluginSet plugins, int mostChunksPerSet = 40, Action<ChunkRecord, bool, float>? perChunk = null)
    {
        var interior = new Tally(0, 0, [], [], 0);
        var outdoor = new Tally(0, 0, [], [], 0);
        foreach (var set in plugins.All<MapSetRecord>(RecordTypes.MapSet))
        {
            var chunks = plugins.ChunksIn(set.Id).Take(mostChunksPerSet).ToList();
            if (chunks.Count == 0)
                continue;
            var game = GameState.NewGame(plugins);
            var streamer = new ChunkStreamer(plugins, game, new CollisionWorld(), set, new VisibleWorld());
            foreach (var chunk in chunks)
            {
                var origin = set.Origin(chunk);
                streamer.Update(origin + new Vector3(set.CellSize / 2, 0, set.CellSize / 2));
                var isInterior = plugins.TryGet<TerrainRecord>(chunk.Terrain) is { SkyDrawn: false };
                var tally = isInterior ? interior : outdoor;
                var key = new ChunkKey(set.Id, chunk.Row, chunk.Column);
                var triangles = new List<CollisionWorld.Triangle>();
                streamer.World.Near(origin.X, origin.Z, origin.X + set.CellSize, origin.Z + set.CellSize, triangles, key);
                var any = false;
                var low = 0f;
                foreach (var t in triangles.Distinct())
                {
                    if (MathF.Abs(t.Normal.Y) >= 0.45f || t.Has(Plugins.Assets.CollisionFlags.SeeThrough))
                        continue;
                    any = true;
                    var area = Vector3.Cross(t.B - t.A, t.C - t.A).Length() / 2;
                    var centre = (t.A + t.B + t.C) / 3;
                    Vector3[] samples = [centre, (centre + t.A) / 2, (centre + t.B) / 2, (centre + t.C) / 2];
                    var bare = 0;
                    foreach (var s in samples)
                    {
                        var drawn = streamer.Visible!.RaycastSolid(s + t.Normal * Near, s - t.Normal * Near) is not null
                            || streamer.Visible.RaycastSolid(s - t.Normal * Near, s + t.Normal * Near) is not null;
                        if (!drawn)
                            bare++;
                    }
                    tally = tally with { WallArea = tally.WallArea + area, BareArea = tally.BareArea + area * bare / samples.Length };
                    if (bare == samples.Length)
                    {
                        tally.BareBySurface[t.Surface] = tally.BareBySurface.GetValueOrDefault(t.Surface) + area;
                        // How high its top is over the floor at its foot.
                        var top = MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y));
                        var foot = centre + t.Normal * 0.3f;
                        if (SpherePhysics.SweepNearest(streamer.World, foot with { Y = top + 0.5f }, foot with { Y = top - 8 }, 0.05f, true, 0.5f, out var floor, out _))
                        {
                            tally.BareTops.Add(top - floor.Y);
                            if (top - floor.Y < 2.2f)
                                low += area;
                        }
                    }
                }
                perChunk?.Invoke(chunk, isInterior, low);
                if (isInterior)
                    interior = tally with { Chunks = tally.Chunks + (any ? 1 : 0) };
                else
                    outdoor = tally with { Chunks = tally.Chunks + (any ? 1 : 0) };
            }
        }
        return (interior, outdoor);
    }

    /// <summary>The wall triangles within <paramref name="radius"/> of <paramref name="at"/> across the ground: surface, flags, role, how low and high, and whether anything drawn stands at their middle.</summary>
    public static string Describe(CollisionWorld world, VisibleWorld? visible, Vector3 at, float radius)
    {
        var triangles = new List<CollisionWorld.Triangle>();
        world.Near(at.X - radius, at.Z - radius, at.X + radius, at.Z + radius, triangles);
        var text = new StringBuilder();
        foreach (var g in triangles.Distinct().Where(t => MathF.Abs(t.Normal.Y) < 0.45f)
            .GroupBy(t => (t.Surface, t.Flags, t.Role)))
        {
            var lo = g.Min(t => MathF.Min(t.A.Y, MathF.Min(t.B.Y, t.C.Y))) - at.Y;
            var hi = g.Max(t => MathF.Max(t.A.Y, MathF.Max(t.B.Y, t.C.Y))) - at.Y;
            var drawn = g.Count(t =>
            {
                var c = (t.A + t.B + t.C) / 3;
                return visible?.RaycastSolid(c + t.Normal * Near, c - t.Normal * Near) is not null || visible?.RaycastSolid(c - t.Normal * Near, c + t.Normal * Near) is not null;
            });
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  surface {g.Key.Surface} flags {g.Key.Flags} role {g.Key.Role}: {g.Count()} triangles, from {lo:F2} to {hi:F2} over the point, {drawn} with something drawn at the middle"));
        }
        return text.ToString();
    }

    public static string Report((Tally Interior, Tally Outdoor) tallies)
    {
        var text = new StringBuilder();
        foreach (var (label, t) in new[] { ("interior", tallies.Interior), ("outdoor", tallies.Outdoor) })
        {
            var tops = t.BareTops.Order().ToList();
            float Share(Func<float, bool> f) => tops.Count == 0 ? 0 : (float)tops.Count(f) / tops.Count;
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{label}: {t.Chunks} chunks, wall area {t.WallArea:F0}, nothing drawn at {t.BareArea / MathF.Max(t.WallArea, 1):P1}; wholly bare walls' tops over the floor: median {(tops.Count == 0 ? 0 : tops[tops.Count / 2]):F2}, under 2.5 {Share(h => h < 2.5f):P0}, 2.5 to 5 {Share(h => h is >= 2.5f and < 5):P0}, 5 and over {Share(h => h >= 5):P0}"));
            text.AppendLine("  bare by surface: " + string.Join(", ", t.BareBySurface.OrderByDescending(x => x.Value).Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Key} {x.Value / MathF.Max(t.BareArea, 1):P0}"))));
        }
        return text.ToString();
    }
}
