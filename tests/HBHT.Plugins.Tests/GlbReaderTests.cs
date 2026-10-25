using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using HBHT.Plugins.Assets;

namespace HBHT.Plugins.Tests;

public class GlbReaderTests
{
    /// <summary>A binary glTF of <paramref name="json"/> and, if given, a binary chunk.</summary>
    private static byte[] Glb(string json, byte[]? bin = null)
    {
        var text = Encoding.UTF8.GetBytes(json.PadRight((json.Length + 3) / 4 * 4));
        var length = 12 + 8 + text.Length + (bin is null ? 0 : 8 + bin.Length);
        var glb = new byte[length];
        BinaryPrimitives.WriteUInt32LittleEndian(glb, 0x46546C67);
        BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(4), 2);
        BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(8), length);
        BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(12), text.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(16), 0x4E4F534A);
        text.CopyTo(glb, 20);
        if (bin is not null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(glb.AsSpan(20 + text.Length), bin.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(glb.AsSpan(24 + text.Length), 0x004E4942);
            bin.CopyTo(glb, 28 + text.Length);
        }
        return glb;
    }

    private static byte[] Floats(params float[] values)
    {
        var bytes = new byte[4 * values.Length];
        for (var i = 0; i < values.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 * i), values[i]);
        return bytes;
    }

    [Fact]
    public void RefusesWhatIsNotAGlb()
    {
        Assert.Throws<InvalidDataException>(() => GlbReader.Parse(new byte[40]));
        Assert.Throws<InvalidDataException>(() => GlbReader.Parse([1, 2, 3]));
        var cut = Glb("{}");
        BinaryPrimitives.WriteInt32LittleEndian(cut.AsSpan(12), 1000);
        Assert.Throws<InvalidDataException>(() => GlbReader.Parse(cut));
    }

    [Fact]
    public void KnowsWhetherThereIsABinaryChunk()
    {
        using var without = GlbReader.Parse(Glb("{}"));
        using var with = GlbReader.Parse(Glb("{}", Floats(1)));
        Assert.False(without.HasBinaryChunk);
        Assert.True(with.HasBinaryChunk);
    }

    [Fact]
    public void ReadsPackedAccessorsAtTheirOffsets()
    {
        // Two VEC3s, then (after the view's own offset of 8 more bytes) a SCALAR run starting one float in.
        const string json = """
            {"accessors":[
              {"bufferView":0,"componentType":5126,"count":2,"type":"VEC3"},
              {"bufferView":1,"byteOffset":4,"componentType":5126,"count":2,"type":"SCALAR"}],
             "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":24},{"buffer":0,"byteOffset":32,"byteLength":12}]}
            """;
        var bin = Floats(1, 2, 3, 4, 5, 6, 0, 0, 9, 10, 11);
        using var glb = GlbReader.Parse(Glb(json, bin));
        Assert.Equal([1f, 2, 3, 4, 5, 6], glb.Floats(0));
        Assert.Equal([10f, 11], glb.Floats(1));
    }

    [Fact]
    public void HonoursAViewsStride()
    {
        // Position (VEC3) and a float of something else per vertex, interleaved.
        const string json = """
            {"accessors":[{"bufferView":0,"componentType":5126,"count":2,"type":"VEC3"}],
             "bufferViews":[{"buffer":0,"byteLength":32,"byteStride":16}]}
            """;
        using var glb = GlbReader.Parse(Glb(json, Floats(1, 2, 3, 99, 4, 5, 6, 99)));
        Assert.Equal([1f, 2, 3, 4, 5, 6], glb.Floats(0));
    }

    [Fact]
    public void ReadsIndicesOfEachWidth()
    {
        const string json = """
            {"accessors":[
              {"bufferView":0,"componentType":5121,"count":3,"type":"SCALAR"},
              {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"},
              {"bufferView":2,"componentType":5125,"count":2,"type":"SCALAR"}],
             "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":3},{"buffer":0,"byteOffset":4,"byteLength":6},{"buffer":0,"byteOffset":12,"byteLength":8}]}
            """;
        var bin = new byte[20];
        bin[0] = 1;
        bin[1] = 2;
        bin[2] = 200;
        BinaryPrimitives.WriteUInt16LittleEndian(bin.AsSpan(4), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(bin.AsSpan(6), 300);
        BinaryPrimitives.WriteUInt16LittleEndian(bin.AsSpan(8), 65000);
        BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(12), 70000);
        BinaryPrimitives.WriteUInt32LittleEndian(bin.AsSpan(16), 5);
        using var glb = GlbReader.Parse(Glb(json, bin));
        Assert.Equal([1, 2, 200], glb.Indices(0));
        Assert.Equal([7, 300, 65000], glb.Indices(1));
        Assert.Equal([70000, 5], glb.Indices(2));
    }

    [Fact]
    public void RefusesAccessorsItDoesNotKnow()
    {
        const string json = """
            {"accessors":[
              {"bufferView":0,"componentType":5123,"count":1,"type":"SCALAR"},
              {"bufferView":0,"componentType":5126,"count":1,"type":"MAT4"},
              {"bufferView":0,"componentType":5126,"count":1,"type":"SCALAR"}],
             "bufferViews":[{"buffer":0,"byteLength":4}]}
            """;
        using var glb = GlbReader.Parse(Glb(json, Floats(1)));
        Assert.Throws<InvalidDataException>(() => glb.Floats(0));
        Assert.Throws<InvalidDataException>(() => glb.Floats(1));
        Assert.Throws<InvalidDataException>(() => glb.Indices(2));
    }

    [Fact]
    public void ReadsACollisionMeshByTheAttributeNamesItPublishes()
    {
        var json = $$$"""
            {"meshes":[{"primitives":[{"attributes":{
                "POSITION":0,"{{{CollisionMesh.SurfaceAttribute}}}":1,"{{{CollisionMesh.RoleAttribute}}}":2,
                "{{{CollisionMesh.FlagsAttribute}}}":3,"{{{CollisionMesh.TargetAttribute}}}":4}}]}],
             "accessors":[
              {"bufferView":0,"componentType":5126,"count":3,"type":"VEC3"},
              {"bufferView":1,"componentType":5126,"count":3,"type":"SCALAR"},
              {"bufferView":2,"componentType":5126,"count":3,"type":"SCALAR"},
              {"bufferView":3,"componentType":5126,"count":3,"type":"SCALAR"},
              {"bufferView":4,"componentType":5126,"count":3,"type":"SCALAR"}],
             "bufferViews":[
              {"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":12},
              {"buffer":0,"byteOffset":48,"byteLength":12},{"buffer":0,"byteOffset":60,"byteLength":12},
              {"buffer":0,"byteOffset":72,"byteLength":12}]}
            """;
        var bin = Floats(0, 0, 0, 1, 0, 0, 0, 0, 1, 4, 4, 4, 2, 2, 2, 0x204, 0x204, 0x204, 9, 9, 9);
        var triangle = Assert.Single(CollisionMesh.Read(Glb(json, bin)));
        Assert.Equal(new CollisionTriangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitZ, 4, CollisionRole.Trigger,
            CollisionFlags.EdgeAB | CollisionFlags.SeeThrough, 9), triangle);
    }

    [Fact]
    public void ReadsTheExtrasTheImporterWrites()
    {
        const string json = """
            {"scene":0,
             "extensions":{"KHR_lights_punctual":{"lights":[{"type":"directional","color":[1,0.5,0.25]},{"type":"directional","color":[0,1,0]}]}},
             "nodes":[
               {"name":"transform 3","extras":{"transform":3,"op":"PivotRotate","billboard":false,"pivot":[1,2,3]}},
               {"name":"transform 4 billboard","extras":{"transform":4,"billboard":true,"pivot":[7,7,7]}},
               {"name":"transform 5","extras":{"transform":5,"billboard":false}}],
             "scenes":[{"nodes":[0],"extras":{
               "scrollingTextures":[{"borg1":3755,"width":32,"move":2,"loads":1}],
               "ambientLight":[0.1,0.2,0.3],
               "particleEmitters":[{"color":[[0,0,0,0],[1,0.5,0.25,1]]},{"color":[[0,0,0,0],[0,1,0,1]]}],
               "stillAnimations":[{"borg6":12,"length":40}],
               "mirrors":true}}]}
            """;
        using var glb = GlbReader.Parse(Glb(json));
        var extras = SceneExtras.Read(glb);
        Assert.Equal([new ScrollingTexture(3755, 32, 2, 1)], extras.Scrolling);
        Assert.True(extras.Mirrored);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), extras.Ambient);
        Assert.Equal([new Vector3(1, 0.5f, 0.25f), Vector3.UnitY], extras.LightColors);
        Assert.Equal([new Vector3(1, 0.5f, 0.25f), Vector3.UnitY], extras.EmitterColors);
        Assert.Equal([new StillAnimation(12, 40)], extras.StillAnimations);
        // Only a transform's own node, not a billboard's, gives its pivot.
        Assert.Equal(new Dictionary<int, Vector3> { [3] = new(1, 2, 3) }, extras.Pivots);
    }

    [Fact]
    public void ASceneWithNoExtrasHasNone()
    {
        using var glb = GlbReader.Parse(Glb("""{"scenes":[{"nodes":[]}]}"""));
        var extras = SceneExtras.Read(glb);
        Assert.Empty(extras.Scrolling);
        Assert.False(extras.Mirrored);
        Assert.Null(extras.Ambient);
        Assert.Empty(extras.LightColors);
        Assert.Empty(extras.EmitterColors);
        Assert.Empty(extras.StillAnimations);
        Assert.Empty(extras.Pivots);
    }

    [Fact]
    public void NamesRoundTrip()
    {
        Assert.Equal("transform 7", SceneNames.Transform(7));
        Assert.Equal("transform 7 billboard", SceneNames.Billboard(7));
        Assert.Equal("transform 7 facing", SceneNames.Facing(7));
        Assert.Equal("transform 7 card", SceneNames.Card(7));
        Assert.Equal("light 1", SceneNames.Light(1));
        Assert.Equal("borg6 1234", SceneNames.Animation(1234));
        Assert.Equal("borg1 3755", SceneNames.Image(3755));

        Assert.True(SceneNames.IsFacing(SceneNames.Facing(7)));
        Assert.False(SceneNames.IsFacing(SceneNames.Billboard(7)));
        Assert.True(SceneNames.TryLocator(SceneNames.Locator(3), out var locator));
        Assert.Equal(3, locator);
        Assert.True(SceneNames.TryEmitter(SceneNames.Emitter(12), out var emitter));
        Assert.Equal(12, emitter);
        Assert.True(SceneNames.TryLight(SceneNames.Light(0), out var light));
        Assert.Equal(0, light);
        Assert.True(SceneNames.TryAnimation(SceneNames.Animation(99), out var borg6));
        Assert.Equal(99, borg6);

        Assert.False(SceneNames.TryLocator(SceneNames.Emitter(3), out _));
        Assert.False(SceneNames.TryLocator("locator x", out _));
        Assert.False(SceneNames.TryLocator("locator", out _));
    }
}
