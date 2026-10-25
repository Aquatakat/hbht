using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Chunks;
using Aidyn.Core.Physics;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>The surfaces the loaded chunks draw, which rain and snow land on, from the base plugin.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ChunkSurfacesTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private (ChunkStreamer Streamer, Vector3 Origin) Load(string name)
    {
        var overworld = Data.MapSets.First(m => m.Label == "Overworld");
        var streamer = new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), new CollisionWorld(), overworld, new VisibleWorld());
        var origin = streamer.Origin(streamer.Find(name)!);
        streamer.Update(origin + new Vector3(50, 0, 50));
        return (streamer, origin);
    }

    [Fact]
    public void AChunkPreparedAheadOnOtherThreadsLoadsTheSameSurfaces()
    {
        // Read ahead from several threads at once, as the runtime's builder reads ahead what a crossing could load, and loaded
        // on this one: the same triangles as loading it cold.
        var overworld = Data.MapSets.First(m => m.Label == "Overworld");
        var cold = new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), new CollisionWorld(), overworld, new VisibleWorld());
        var prepared = new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), new CollisionWorld(), overworld, new VisibleWorld());
        var i08 = cold.Find("I08")!;
        var near = overworld.Chunks.Where(c => Math.Abs(c.Row - i08.Row) <= 2 && Math.Abs(c.Column - i08.Column) <= 2).ToList();
        Parallel.ForEach(near.Concat(near), new ParallelOptions { MaxDegreeOfParallelism = 4 }, prepared.Prepare);
        var at = cold.Origin(i08) + new Vector3(50, 0, 50);
        cold.Update(at);
        prepared.Update(at);

        Assert.Equal(cold.Visible!.Count, prepared.Visible!.Count);
        for (var x = 5; x < 300; x += 7)
        {
            var top = cold.Origin(i08) + new Vector3(x - 100, 300, x / 3f);
            var bottom = top with { Y = -300 };
            var hit = cold.Visible.Raycast(top, bottom, out var a, out _, out var absorbsA);
            Assert.Equal(hit, prepared.Visible.Raycast(top, bottom, out var b, out _, out var absorbsB));
            Assert.Equal((a, absorbsA), (b, absorbsB));
        }
    }

    /// <summary>
    /// Down each column of a chunk, a unit apart: where the drawn surfaces and the collision both have something, how often the
    /// highest of each are within 0.3 of each other, and how many columns the collision stands over the drawn surfaces by more,
    /// or the drawn surfaces have something where it has nothing, and of those how many absorb the rain.
    /// </summary>
    private static (int Both, int Close, int Below, int DrawnOnly, int Absorbing) Columns(ChunkStreamer streamer, Vector3 origin)
    {
        int both = 0, close = 0, below = 0, drawnOnly = 0, absorbing = 0;
        for (var x = 1; x < 100; x++)
        {
            for (var z = 1; z < 100; z++)
            {
                var top = origin + new Vector3(x, 300, z);
                var bottom = origin + new Vector3(x, -300, z);
                var drawn = streamer.Visible!.Raycast(top, bottom, out var surface, out _, out var absorbs);
                var solid = SpherePhysics.SweepNearest(streamer.World, top, bottom, 0.01f, false, -1, out var ground, out _);
                if (drawn && solid)
                {
                    both++;
                    var dy = surface.Y - ground.Y;
                    if (MathF.Abs(dy) < 0.3f)
                        close++;
                    else if (dy < 0)
                        below++;
                }
                else if (drawn)
                {
                    drawnOnly++;
                    if (absorbs)
                        absorbing++;
                }
            }
        }
        return (both, close, below, drawnOnly, absorbing);
    }

    [Fact]
    public void TheDrawnGroundLiesOnTheCollision()
    {
        // The scenes are placed where the collision says the ground is: in M07, Gwernia, and I08, where a new game
        // starts, within 0.3 down nine columns in ten. Gwernia's roofs are among them: its houses' collision is solid,
        // roofed as they're drawn.
        foreach (var name in new[] { "M07", "I08" })
        {
            var (streamer, origin) = Load(name);
            // What the rain splashes on: the canopies over the ground absorb it.
            var surfaces = new ChunkSurfaces(Data.Set);
            streamer.Visible!.Clear();
            foreach (var (chunk, map) in streamer.All)
                streamer.Visible.Add(map.Key, surfaces.For(chunk, map).Where(t => !t.Absorbs), streamer.Origin(chunk));
            var (both, close, _, _, _) = Columns(streamer, origin);
            Assert.True(both > 3000, $"{name}: {both}");
            Assert.True(close > 0.9 * both, $"{name}: {close} of {both}");
        }
    }

    [Fact]
    public void GwerniasSeaCatchesTheRainAndItsInvisibleCollisionDoesnt()
    {
        // The sea, its see-through scene, has no collision under it, and absorbs the rain, down 4,942 columns; and down 70 the
        // collision stands over what's drawn there, where the original's rain splashes in the air and the port's falls
        // to the drawn ground.
        var (streamer, origin) = Load("M07");
        var (_, _, below, drawnOnly, absorbing) = Columns(streamer, origin);
        Assert.InRange(below, 50, 100);
        Assert.True(drawnOnly > 5000, $"{drawnOnly}");
        Assert.True(absorbing > 4500, $"{absorbing} of {drawnOnly}");
        Assert.NotNull(streamer.Find("M07")!.AltScene);
    }

    [Fact]
    public void TheTreesCanopiesShelterTheGroundUnderThemAsTheyFace()
    {
        // The scene objects placed about I08, where a new game starts, with cut-out draws over 2 above them, their
        // canopies. Of the disc of radius 3 about each, straight down, the share whose first drawn surface absorbs the rain:
        // 71% under the 64 with level leaves (most of them 0x0B5C and 0x0BBA), but 22% under the 88 made of upright cards
        // (0x07E1, 0x0B48 and others), which rain falling straight passes between.
        var (streamer, _) = Load("I08");
        var level = new List<float>();
        var upright = new List<float>();
        foreach (var (chunk, map) in streamer.All)
        foreach (var o in map.Record.Objects.OfType<SceneObject>().Where(o => map.IsActive(o.Index) && o.Models.Count > 0))
        {
            var origin = streamer.Origin(chunk);
            var place = Matrix4x4.CreateScale(o.Scale) * Matrix4x4.CreateFromQuaternion(o.Orientation()) * Matrix4x4.CreateTranslation(o.Position);
            var canopy = SceneMesh.For(Data.Set, o.Models[0].Model).Where(t => t.Blend == SurfaceBlend.CutOut)
                .Select(t => (A: Vector3.Transform(t.A, place), B: Vector3.Transform(t.B, place), C: Vector3.Transform(t.C, place)))
                .Where(t => t.A.Y > o.Position.Y + 2).ToList();
            if (canopy.Count == 0)
                continue;
            var levelArea = canopy.Select(t => Vector3.Cross(t.B - t.A, t.C - t.A)).Where(n => n.Length() > 1e-6f && MathF.Abs(Vector3.Normalize(n).Y) > 0.5f).Sum(n => n.Length() / 2);
            int columns = 0, sheltered = 0;
            for (var x = -3f; x <= 3; x += 0.5f)
            {
                for (var z = -3f; z <= 3; z += 0.5f)
                {
                    if (x * x + z * z > 9)
                        continue;
                    columns++;
                    var top = origin + o.Position + new Vector3(x, 50, z);
                    if (streamer.Visible!.Raycast(top, top - new Vector3(0, 100, 0), out _, out _, out var absorbs) && absorbs)
                        sheltered++;
                }
            }
            (levelArea > 1 ? level : upright).Add((float)sheltered / columns);
        }
        Assert.Equal((64, 88), (level.Count, upright.Count));
        Assert.True(level.Average() > 0.5f, $"{level.Average()}");
        Assert.True(upright.Average() < 0.3f, $"{upright.Average()}");
    }
}
