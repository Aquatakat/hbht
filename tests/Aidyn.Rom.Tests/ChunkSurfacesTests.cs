using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Chunks;
using Aidyn.Core.Physics;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>The surfaces the loaded chunks draw, which rain and snow land on, from the base plugin.</summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ChunkSurfacesTests(MapObjectFixture fixture)
{
    private MapObjectFixture.Loaded Data => fixture.Data;

    private (ChunkStreamer Streamer, Vector3 Origin) Load(string name)
    {
        var overworld = Data.MapSets.First(m => m.Label == "Overworld");
        var streamer = new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), new CollisionWorld(), overworld, new VisibleWorld());
        var origin = streamer.Origin(streamer.Find(name)!);
        streamer.Update(origin + new Vector3(50, 0, 50));
        return (streamer, origin);
    }

    /// <summary>
    /// Down each column of a chunk, a unit apart: where the drawn surfaces and the collision both have something, how often the
    /// highest of each are within 0.3 of each other, and how many columns the collision stands over the drawn surfaces by more,
    /// or the drawn surfaces have something where it has nothing.
    /// </summary>
    private static (int Both, int Close, int Below, int DrawnOnly) Columns(ChunkStreamer streamer, Vector3 origin)
    {
        int both = 0, close = 0, below = 0, drawnOnly = 0;
        for (var x = 1; x < 100; x++)
        {
            for (var z = 1; z < 100; z++)
            {
                var top = origin + new Vector3(x, 300, z);
                var bottom = origin + new Vector3(x, -300, z);
                var drawn = streamer.Visible!.Raycast(top, bottom, out var surface, out _);
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
                    drawnOnly++;
            }
        }
        return (both, close, below, drawnOnly);
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
            var (both, close, _, _) = Columns(streamer, origin);
            Assert.True(both > 3000, $"{name}: {both}");
            Assert.True(close > 0.9 * both, $"{name}: {close} of {both}");
        }
    }

    [Fact]
    public void GwerniasSeaCatchesTheRainAndItsInvisibleCollisionDoesnt()
    {
        // The sea, its see-through scene, has no collision under it; and down 70 columns the collision stands over what's
        // drawn there, where the original's rain splashes in the air and the port's falls to the drawn ground.
        var (streamer, origin) = Load("M07");
        var (_, _, below, drawnOnly) = Columns(streamer, origin);
        Assert.InRange(below, 50, 100);
        Assert.True(drawnOnly > 5000, $"{drawnOnly}");
        Assert.NotNull(streamer.Find("M07")!.AltScene);
    }

    [Fact]
    public void RainFallsThroughTheTreesCanopiesToTheGround()
    {
        // Every scene object placed about I08, where a new game starts, with cut-out draws over 2 above it, its canopy:
        // straight down through the canopy, 1.5 out from the middle, the first drawn surface is the ground the collision
        // has there for 125 of the 152. Of the rest, two meet a broad trunk's side, and most are bushes (0x0B5C) whose
        // collision stands over them where the drawn ground is under it.
        var (streamer, _) = Load("I08");
        int trees = 0, ground = 0;
        foreach (var (chunk, map) in streamer.All)
        foreach (var o in map.Record.Objects.OfType<SceneObject>().Where(o => map.IsActive(o.Index) && o.Models.Count > 0))
        {
            var origin = streamer.Origin(chunk);
            var place = Matrix4x4.CreateScale(o.Scale) * Matrix4x4.CreateFromQuaternion(o.Orientation()) * Matrix4x4.CreateTranslation(o.Position);
            if (!SceneMesh.For(Data.Set, o.Models[0].Model).Any(t => t.Blend == SurfaceBlend.CutOut && Vector3.Transform(t.A, place).Y > o.Position.Y + 2))
                continue;
            trees++;
            var top = origin + o.Position + new Vector3(1.5f, 50, 0);
            var bottom = top - new Vector3(0, 100, 0);
            if (streamer.Visible!.Raycast(top, bottom, out var surface, out _)
                && SpherePhysics.SweepNearest(streamer.World, top, bottom, 0.01f, false, -1, out var solid, out _)
                && MathF.Abs(surface.Y - solid.Y) < 0.3f)
                ground++;
        }
        Assert.True(trees > 100, $"{trees}");
        Assert.True(ground > 0.8 * trees, $"{ground} of {trees}");
    }
}
