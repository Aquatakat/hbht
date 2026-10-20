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

    [Fact]
    public void BillboardsExportAsANodeToTurnAndACardThatRolls()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        // A scene whose billboard, transform 7, is rolled by −20.9°.
        var root = Json(archive, 0x1A7B);
        var nodes = root.GetProperty("nodes").EnumerateArray().ToList();
        var billboard = nodes.Single(n => n.GetProperty("name").GetString() == "transform 7 billboard");
        Assert.False(billboard.TryGetProperty("mesh", out _));
        var facing = nodes[billboard.GetProperty("children")[0].GetInt32()];
        Assert.Equal("transform 7 facing", facing.GetProperty("name").GetString());
        Assert.False(facing.TryGetProperty("rotation", out _));
        var card = nodes[facing.GetProperty("children")[0].GetInt32()];
        Assert.Equal("transform 7 card", card.GetProperty("name").GetString());
        Assert.True(card.TryGetProperty("mesh", out _));
        var q = card.GetProperty("rotation");
        var roll = Borg5Scene.Parse(archive.Read(0x1A7B)).Transforms[7].Rotation.Z;
        Assert.Equal(-20.932812f, roll, 0.0001f);
        AssertNear(new Vector3(0, 0, MathF.Sin(roll * MathF.PI / 360)), new Vector3(q[0].GetSingle(), q[1].GetSingle(), q[2].GetSingle()));
    }

    [Fact]
    public void ScenesListTheTexturesTheyScroll()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        // Gwernia's chunk, M07, whose water scrolls 2 texels every third load.
        var root = Json(archive, 0x105E);
        var scrolling = root.GetProperty("scenes")[0].GetProperty("extras").GetProperty("scrollingTextures");
        Assert.Equal(1, scrolling.GetArrayLength());
        var water = scrolling[0];
        Assert.Equal((0xEAB, 32, 2, 1), (water.GetProperty("borg1").GetInt32(), water.GetProperty("width").GetInt32(),
            water.GetProperty("move").GetInt32(), water.GetProperty("loads").GetInt32()));
        Assert.Contains(root.GetProperty("materials").EnumerateArray(),
            m => m.GetProperty("extras").TryGetProperty("borg1", out var b) && b.GetInt32() == 0xEAB);

        // Bushes don't scroll.
        Assert.False(Json(archive, 0x0B5E).GetProperty("scenes")[0].GetProperty("extras").TryGetProperty("scrollingTextures", out _));
    }

    /// <summary>
    /// What a body holds hangs from a node under each locator's, turned by the locator model's rotation (<c>SceneGetLocatorMtx</c>:
    /// <c>guRotateRPYF</c> of the Borg2's rotation, X, Y and Z in that order, with no swap): Alaron's shield arm, locator 2, is turned
    /// by (2.36, 0.36, −3.11) radians, which puts the shield's face outward on his forearm.
    /// </summary>
    [Fact]
    public void ALocatorsAttachmentIsTurnedByItsModelsRotation()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        const int alaron = 0x25CB;
        var scene = Borg5Scene.Parse(archive.Read(alaron));
        var model = scene.Models.Select(m => Borg2Geometry.Parse(archive.Read(m))).First(m => m.Locator == 2);
        Assert.Equal(2.36f, model.Rotation.X, 0.01f);
        Assert.Equal(0.36f, model.Rotation.Y, 0.01f);
        Assert.Equal(-3.11f, model.Rotation.Z, 0.01f);

        var nodes = Json(archive, alaron).GetProperty("nodes").EnumerateArray().ToList();
        var locator = nodes.Single(n => n.TryGetProperty("name", out var name) && name.GetString() == "locator 2");
        var attachment = nodes[Assert.Single(locator.GetProperty("children").EnumerateArray()).GetInt32()];
        Assert.Equal("locator 2 attachment", attachment.GetProperty("name").GetString());
        var r = attachment.GetProperty("rotation");
        var q = new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());
        var expected = SceneGltf.RotateRpy(model.Rotation);
        foreach (var axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
            AssertNear(Vector3.TransformNormal(axis, expected), Vector3.Transform(axis, q));
        // guRotateRPYF's first row: (cos p cos h, cos p sin h, −sin p).
        var (p, h) = (model.Rotation.Y, model.Rotation.Z);
        AssertNear(new Vector3(MathF.Cos(p) * MathF.Cos(h), MathF.Cos(p) * MathF.Sin(h), -MathF.Sin(p)), Vector3.TransformNormal(Vector3.UnitX, expected));
    }

    private static System.Text.Json.JsonElement Json(BorgArchive archive, int index)
    {
        var glb = SceneGltf.Export(Borg5Scene.Parse(archive.Read(index)), archive.Read, index);
        return System.Text.Json.JsonDocument.Parse(glb.AsMemory(20, BitConverter.ToInt32(glb, 12))).RootElement;
    }

    private static void AssertNear(Vector3 expected, Vector3 actual) =>
        Assert.True(Vector3.Distance(expected, actual) < 1e-4, $"Expected {expected}, got {actual}.");
}
