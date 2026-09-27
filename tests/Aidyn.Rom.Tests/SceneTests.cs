using System.Numerics;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class SceneTests
{
    [Fact]
    public void PivotRotateTurnsAboutThePivotAndPutsItAtThePosition()
    {
        var t = new Borg5Transform(Borg5TransformFlags.Root, Borg5TransformOp.PivotRotate, [],
            Rotation: new Vector3(0, 90, 0), Position: new Vector3(10, 20, 30), Scale: new Vector3(2, 2, 2),
            Pivot: new Vector3(0, 1, 0));
        var m = t.LocalMatrix();

        AssertNear(new Vector3(10, 20, 30), Vector3.Transform(new Vector3(0, 1, 0), m));
        // A roll of 90° takes +Y to +Z (row vectors), and the scale doubles the offset from the pivot.
        AssertNear(new Vector3(10, 20, 32), Vector3.Transform(new Vector3(0, 2, 0), m));
    }

    /// <summary>
    /// Rotation.Y rolls about X, then Rotation.X pitches about Y, then Rotation.Z turns about Z:
    /// the order of the game's machine code, not of the decomp's pseudocode. With Y up, as the
    /// scenes are, Rotation.X turns a model about the vertical, as the camera's does.
    /// </summary>
    [Fact]
    public void RotationsApplyRollThenPitchThenHeading()
    {
        var t = new Borg5Transform(Borg5TransformFlags.Root, Borg5TransformOp.PivotRotate, [],
            new Vector3(90, 90, 0), Vector3.Zero, Vector3.One, Vector3.Zero);

        // Roll (Rotation.Y) takes +Y to +Z; pitch (Rotation.X) then takes +Z to +X.
        AssertNear(Vector3.UnitX, Vector3.Transform(Vector3.UnitY, t.LocalMatrix()));

        var turn = t with { Rotation = new Vector3(90, 0, 0) };
        AssertNear(Vector3.UnitY, Vector3.Transform(Vector3.UnitY, turn.LocalMatrix()));
        AssertNear(Vector3.UnitX, Vector3.Transform(Vector3.UnitZ, turn.LocalMatrix()));
    }

    [Fact]
    public void LookAtCamerasLookDownNegativeZ()
    {
        // Pitch 90° turns the direction from +Z to +X, so the camera looks toward −X.
        var t = new Borg5Transform(Borg5TransformFlags.Root, Borg5TransformOp.LookAt, [],
            new Vector3(90, 0, 0), new Vector3(1, 2, 3), Vector3.One, Vector3.Zero);
        var m = t.LocalMatrix();

        AssertNear(-Vector3.UnitX, Vector3.TransformNormal(-Vector3.UnitZ, m));
        AssertNear(Vector3.UnitY, Vector3.TransformNormal(Vector3.UnitY, m));
        AssertNear(new Vector3(1, 2, 3), m.Translation);
    }

    [Fact]
    public void EveryBorg5Parses()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        int scenes = 0, draws = 0, textured = 0, emitters = 0, billboards = 0;
        var missing = new List<int>();
        var displayLists = new Dictionary<int, int>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Scene))
        {
            var scene = Borg5Scene.Parse(archive.Read(listing.Index));
            scenes++;
            emitters += scene.ParticleEmitters.Count;
            billboards += scene.Transforms.Count(t => t.Flags.HasFlag(Borg5TransformFlags.Billboard));
            Assert.Equal(BorgType.Camera, archive.Listings[scene.Camera].Type);
            Assert.All(scene.Lights, i => Assert.Equal(BorgType.Light, archive.Listings[i].Type));
            Assert.All(scene.Models, i => Assert.Equal(BorgType.Geometry, archive.Listings[i].Type));
            Assert.All(scene.Textures, i => Assert.Equal(BorgType.Texture, archive.Listings[i].Type));

            foreach (var draw in scene.Draws)
            {
                draws++;
                textured += draw.Texture is null ? 0 : 1;
                var model = scene.Models[draw.Model];
                if (!displayLists.TryGetValue(model, out var count))
                    displayLists[model] = count = Borg2Geometry.Parse(archive.Read(model)).Meshes.Count;
                if (draw.DisplayList >= count && !missing.Contains(listing.Index))
                    missing.Add(listing.Index);
            }
        }

        Assert.Equal((1402, 16_597, 15_230, 218, 321), (scenes, draws, textured, emitters, billboards));
        // These draw display lists their models don't have. What the game does then is unknown.
        Assert.Equal([5246, 13851, 13922, 13924, 13928], missing);
    }

    [Fact]
    public void CamerasAndLightsParse()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var cameras = archive.Listings.Where(l => l.Type == BorgType.Camera)
            .Select(l => Borg3Camera.Parse(archive.Read(l.Index))).ToList();
        var lights = archive.Listings.Where(l => l.Type == BorgType.Light)
            .Select(l => Borg4Light.Parse(archive.Read(l.Index))).ToList();

        Assert.Equal(5, cameras.Count);
        Assert.All(cameras, c => Assert.Equal((45f, 4f / 3), (c.FieldOfViewY, c.Aspect)));
        Assert.Equal(507, lights.Count);
        Assert.All(lights, l => Assert.InRange(Math.Min(l.Red, Math.Min(l.Green, l.Blue)), 0, 1));
    }

    /// <summary>
    /// The game sets a texture scale of ½, so a texel is 64 units of S or T. Many billboards map
    /// their texture exactly once at that scale, and none do at 32 units per texel.
    /// </summary>
    [Fact]
    public void BillboardsMapTheirTextureOnceAtSixtyFourUnitsPerTexel()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        int exact64 = 0, exact32 = 0;
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Scene))
        {
            var scene = Borg5Scene.Parse(archive.Read(listing.Index));
            foreach (var draw in scene.Draws)
            {
                if (draw.Texture is not { } texture || !scene.Transforms[draw.Transform].Flags.HasFlag(Borg5TransformFlags.Billboard))
                    continue;
                var meshes = Borg2Geometry.Parse(archive.Read(scene.Models[draw.Model])).Meshes;
                if (draw.DisplayList >= meshes.Count || meshes[draw.DisplayList].Vertices.Length == 0)
                    continue;
                var mesh = meshes[draw.DisplayList];
                var image = Borg1Texture.Parse(archive.Read(scene.Textures[texture]));
                var s = mesh.Vertices.Max(v => v.S) - mesh.Vertices.Min(v => v.S);
                var t = mesh.Vertices.Max(v => v.T) - mesh.Vertices.Min(v => v.T);
                if (s == image.Width * 64 && t == image.Height * 64)
                    exact64++;
                if (s == image.Width * 32 && t == image.Height * 32)
                    exact32++;
            }
        }

        Assert.Equal((71, 0), (exact64, exact32));
    }

    [Fact]
    public void EverySceneExportsDeterministically()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        var scenes = archive.Listings.Where(l => l.Type == BorgType.Scene).ToList();
        long meshes = 0;
        foreach (var listing in scenes)
        {
            var glb = SceneGltf.Export(Borg5Scene.Parse(archive.Read(listing.Index)), archive.Read, listing.Index);

            Assert.Equal("glTF"u8.ToArray(), glb[..4]);
            Assert.Equal(glb.Length, BitConverter.ToInt32(glb, 8));
            var json = System.Text.Json.JsonDocument.Parse(glb.AsMemory(20, BitConverter.ToInt32(glb, 12)));
            if (json.RootElement.TryGetProperty("meshes", out var m))
                meshes += m.GetArrayLength();
        }

        // Every drawn transform gets one mesh; billboards get theirs on a separate node.
        Assert.Equal(6437, meshes);

        var house = Borg5Scene.Parse(archive.Read(2558));
        Assert.Equal(SceneGltf.Export(house, archive.Read, 2558), SceneGltf.Export(house, archive.Read, 2558));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4, $"Expected {expected}, got {actual}.");
}
