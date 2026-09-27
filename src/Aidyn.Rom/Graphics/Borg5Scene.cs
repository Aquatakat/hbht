using System.Buffers.Binary;
using System.Numerics;

namespace Aidyn.Rom.Graphics;

/// <summary><c>Borg5Transform.flag</c>, as stored. The game also sets bits 0x2 and 0x10 at run time.</summary>
[Flags]
public enum Borg5TransformFlags : ushort
{
    None = 0,
    /// <summary>The transform has no parent. Every other transform is exactly one transform's child.</summary>
    Root = 0x100,
    /// <summary>The model turns to face the camera; only its scale and position apply.</summary>
    Billboard = 0x200,
}

/// <summary><c>Borg5Transform.mtxOp</c>: how the transform's fields become a matrix. Ops 0 and 2 are never used.</summary>
public enum Borg5TransformOp : byte
{
    /// <summary>Scale, rotate and translate about a pivot. Every model node uses this.</summary>
    PivotRotate = 1,
    /// <summary>Only a direction, from two of the angles. Lights use this.</summary>
    Direction = 3,
    /// <summary>A camera looking from the position along the angles. Always transform 0.</summary>
    LookAt = 4,
}

/// <summary>A node of a Borg5 scene's transform tree.</summary>
/// <param name="Rotation">In degrees.</param>
/// <param name="Children">Indices into <see cref="Borg5Scene.Transforms"/>.</param>
public sealed record Borg5Transform(Borg5TransformFlags Flags, Borg5TransformOp Op, int[] Children,
    Vector3 Rotation, Vector3 Position, Vector3 Scale, Vector3 Pivot)
{
    public const int Size = 0x40;

    /// <summary>
    /// The transform relative to its parent, for row vectors (<c>v' = v × M</c>, as in libultra
    /// and <see cref="System.Numerics"/>).
    /// </summary>
    public Matrix4x4 LocalMatrix() => Op switch
    {
        // Scale, then roll about X, pitch about Y and heading about Z (libultra's guRotateRPYF
        // order), then move the pivot to the position.
        Borg5TransformOp.PivotRotate => PivotRotate(),
        // Only the Z row is set, to the direction; the rest is 0.
        Borg5TransformOp.Direction => new Matrix4x4 { M31 = Direction.X, M32 = Direction.Y, M33 = Direction.Z, M44 = 1 },
        Borg5TransformOp.LookAt => LookAt(),
        _ => throw new InvalidOperationException($"Unknown transform op {Op}."),
    };

    /// <summary>
    /// For <see cref="Borg5TransformOp.Direction"/> and <see cref="Borg5TransformOp.LookAt"/>:
    /// Z rotated by roll <c>Rotation.Y</c> and pitch <c>Rotation.X</c>. For a light, this points
    /// toward the light. For a camera, it points from what it's looking at back to the camera.
    /// </summary>
    public Vector3 Direction =>
        Vector3.Transform(Vector3.UnitZ, Rpy(Rotation.Y, Rotation.X, 0));

    private Matrix4x4 PivotRotate()
    {
        var m = Matrix4x4.CreateScale(Scale) * Rpy(Rotation.X, Rotation.Y, Rotation.Z);
        m.Translation = Position - Vector3.TransformNormal(Pivot, m);
        return m;
    }

    /// <summary>
    /// A camera matrix: Z is <see cref="Direction"/>, X is up × Z, Y is Z × X, and the origin is
    /// the position. Up is Y rolled by <c>Rotation.Z</c>. The camera looks down −Z, as glTF's do.
    /// </summary>
    private Matrix4x4 LookAt()
    {
        var z = Vector3.Normalize(Direction);
        var up = Vector3.Transform(Vector3.UnitY, Rpy(0, 0, Rotation.Z));
        var x = Vector3.Normalize(Vector3.Cross(up, z));
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(
            x.X, x.Y, x.Z, 0,
            y.X, y.Y, y.Z, 0,
            z.X, z.Y, z.Z, 0,
            Position.X, Position.Y, Position.Z, 1);
    }

    /// <summary>libultra's <c>guRotateRPYF</c>: roll about X, then pitch about Y, then heading about Z, in degrees.</summary>
    private static Matrix4x4 Rpy(float roll, float pitch, float heading) =>
        Matrix4x4.CreateRotationX(Radians(roll)) * Matrix4x4.CreateRotationY(Radians(pitch)) *
        Matrix4x4.CreateRotationZ(Radians(heading));

    private static float Radians(float degrees) => degrees * MathF.PI / 180;
}

/// <summary>A Borg5 instruction: the high byte of each u16.</summary>
public enum Borg5Opcode : byte
{
    /// <summary>Bind <see cref="Borg5Scene.Textures"/>[value], or unbind for 0xFF.</summary>
    LoadTexture = 1,
    /// <summary>Use <see cref="Borg5Scene.Transforms"/>[value] for the following draws.</summary>
    Matrix = 2,
    /// <summary>Use <see cref="Borg5Scene.Models"/>[value] for the following draws.</summary>
    LoadModel = 4,
    /// <summary>Draw display list [value] of the current model.</summary>
    Draw = 5,
}

/// <summary>One display list drawn with one transform and texture.</summary>
/// <param name="Texture">An index into <see cref="Borg5Scene.Textures"/>, or null for untextured.</param>
/// <param name="Transform">An index into <see cref="Borg5Scene.Transforms"/>.</param>
/// <param name="Model">An index into <see cref="Borg5Scene.Models"/>.</param>
/// <param name="DisplayList">An index into that model's <see cref="Borg2Geometry.Meshes"/>.</param>
public readonly record struct Borg5Draw(int? Texture, int Transform, int Model, int DisplayList);

/// <summary>
/// A Borg5: a scene that draws Borg2 models with Borg1 textures through a transform tree, seen by
/// a Borg3 camera and lit by Borg4 lights (roadmap 1A).
/// </summary>
/// <remarks>
/// <para>
/// The 0x44-byte header is the counts of transforms, lights, models, textures, animated textures
/// and instructions. Then come offsets from the start of the data to the transforms, two words that
/// are always 0, the Borg3's archive index, offsets to the lists of light, model and texture archive
/// indices, to the animated textures (there are none), the instructions and the particle emitters,
/// and the emitter count.
/// </para>
/// <para>
/// Transform 0 is the camera. Next come one direction per light but the last (the last light is
/// ambient: in scenes with more than one, it's almost always the darkest), then the model nodes.
/// </para>
/// <para>
/// <b>Units.</b> Positions are in scene units. The game draws at 16 times that, and scales a model's
/// vertices by its <see cref="Borg2Geometry.Scale"/>, so a vertex is at
/// <c>v × Scale / 16 × World</c> in scene units: <see cref="ModelMatrix"/>.
/// </para>
/// </remarks>
public sealed class Borg5Scene
{
    public const int HeaderSize = 0x44;
    private const byte Untextured = 0xFF;
    private const int ParticleEmitterSize = 0xBC;

    private readonly int[] _parents;

    private Borg5Scene(int camera, int[] lights, int[] models, int[] textures, Borg5Transform[] transforms,
        (Borg5Opcode, byte)[] instructions, Borg5Draw[] draws, int particleEmitters, int[] parents)
    {
        Camera = camera;
        Lights = lights;
        Models = models;
        Textures = textures;
        Transforms = transforms;
        Instructions = instructions;
        Draws = draws;
        ParticleEmitterCount = particleEmitters;
        _parents = parents;
    }

    /// <summary>The Borg3's archive index. Its transform is <c>Transforms[0]</c>.</summary>
    public int Camera { get; }

    /// <summary>
    /// Borg4 archive indices. Light <c>i</c> shines from <c>Transforms[1 + i].Direction</c>,
    /// except the last, which is ambient.
    /// </summary>
    public IReadOnlyList<int> Lights { get; }

    /// <summary>Borg2 archive indices.</summary>
    public IReadOnlyList<int> Models { get; }

    /// <summary>Borg1 archive indices.</summary>
    public IReadOnlyList<int> Textures { get; }

    public IReadOnlyList<Borg5Transform> Transforms { get; }
    public IReadOnlyList<(Borg5Opcode Op, byte Value)> Instructions { get; }

    /// <summary>What the instructions draw, in order.</summary>
    public IReadOnlyList<Borg5Draw> Draws { get; }

    /// <summary>Particle emitters (0xBC bytes each) aren't parsed yet.</summary>
    public int ParticleEmitterCount { get; }

    /// <summary>The index of the first model node, after the camera and the light directions.</summary>
    public int FirstNode => 1 + Math.Max(0, Lights.Count - 1);

    public int? Parent(int transform) => _parents[transform] < 0 ? null : _parents[transform];

    /// <summary>The transform relative to the scene.</summary>
    public Matrix4x4 WorldMatrix(int transform)
    {
        var m = Transforms[transform].LocalMatrix();
        for (var p = Parent(transform); p is { } parent; p = Parent(parent))
            m *= Transforms[parent].LocalMatrix();
        return m;
    }

    /// <summary>Where a model drawn with this transform puts its vertices, in scene units.</summary>
    public Matrix4x4 ModelMatrix(int transform, Borg2Geometry model) =>
        Matrix4x4.CreateScale(model.Scale / 16) * WorldMatrix(transform);

    public static Borg5Scene Parse(byte[] data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException($"A Borg5 is at least 0x{HeaderSize:X} bytes, not {data.Length}.");

        int Word(int offset) => BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(offset));
        int transformCount = Word(0), lightCount = Word(4), modelCount = Word(8), textureCount = Word(0xC);
        int animatedCount = Word(0x10), instructionCount = Word(0x14);
        int transforms = Word(0x18), camera = Word(0x24), lights = Word(0x28), models = Word(0x2C);
        int textures = Word(0x30), animated = Word(0x34), instructions = Word(0x38), particles = Word(0x3C);
        var particleCount = Word(0x40);

        if (Word(0x1C) != 0 || Word(0x20) != 0)
            throw new InvalidDataException("The header words at 0x1C and 0x20 aren't 0; nothing is known to use them.");
        if (animatedCount != 0)
            throw new InvalidDataException("Animated textures aren't supported (the game crashes on them).");
        if (transforms != HeaderSize)
            throw new InvalidDataException($"The transforms are at 0x{transforms:X}, not straight after the header.");
        if (transformCount < 1 + Math.Max(0, lightCount - 1))
            throw new InvalidDataException($"{transformCount} transforms can't hold a camera and {lightCount} lights.");

        var transformList = new Borg5Transform[transformCount];
        for (var i = 0; i < transformCount; i++)
            transformList[i] = ReadTransform(data, transforms + i * Borg5Transform.Size, transformCount);
        var parents = Hierarchy(transformList);

        var firstNode = 1 + Math.Max(0, lightCount - 1);
        for (var i = 0; i < transformCount; i++)
        {
            var expected = i == 0 ? Borg5TransformOp.LookAt : i < firstNode ? Borg5TransformOp.Direction : (Borg5TransformOp?)null;
            if (expected is { } op && transformList[i].Op != op)
                throw new InvalidDataException($"Transform {i} is {transformList[i].Op}, not {op}.");
        }

        var lightList = Indices(data, lights, lightCount);
        var modelList = Indices(data, models, modelCount);
        var textureList = Indices(data, textures, textureCount);
        if (animated != textures + textureCount * 4 || instructions != animated)
            throw new InvalidDataException($"The instructions are at 0x{instructions:X}, not after the texture list.");

        var instructionList = new (Borg5Opcode, byte)[instructionCount];
        for (var i = 0; i < instructionCount; i++)
        {
            var word = BinaryPrimitives.ReadUInt16BigEndian(Slice(data, instructions + i * 2, 2));
            instructionList[i] = ((Borg5Opcode)(word >> 8), (byte)word);
        }
        var draws = Interpret(instructionList, textureCount, modelCount, firstNode, transformCount);

        var end = particleCount == 0 ? particles : particles + particleCount * (4 + ParticleEmitterSize);
        if (particles < instructions + instructionCount * 2 || end != data.Length)
            throw new InvalidDataException($"The particle emitters at 0x{particles:X} don't end the Borg5 at 0x{data.Length:X}.");

        return new Borg5Scene(camera, lightList, modelList, textureList, transformList, instructionList, draws,
            particleCount, parents);
    }

    private static Borg5Transform ReadTransform(byte[] data, int offset, int transformCount)
    {
        var t = Slice(data, offset, Borg5Transform.Size);
        var flags = (Borg5TransformFlags)BinaryPrimitives.ReadUInt16BigEndian(t);
        var op = (Borg5TransformOp)t[2];
        var childCount = t[3];
        var links = BinaryPrimitives.ReadInt32BigEndian(t[4..]);
        if ((flags & ~(Borg5TransformFlags.Root | Borg5TransformFlags.Billboard)) != 0)
            throw new InvalidDataException($"The transform at 0x{offset:X} has unknown flags 0x{(ushort)flags:X}.");
        if (!Enum.IsDefined(op))
            throw new InvalidDataException($"The transform at 0x{offset:X} uses op {(byte)op}, which nothing is known to use.");
        if (BinaryPrimitives.ReadUInt64BigEndian(t[8..]) != 0)
            throw new InvalidDataException($"The run-time fields of the transform at 0x{offset:X} aren't 0.");

        var children = Indices(data, links, childCount);
        foreach (var child in children)
            if (child < 0 || child >= transformCount)
                throw new InvalidDataException($"The transform at 0x{offset:X} links to transform {child} of {transformCount}.");

        return new Borg5Transform(flags, op, children, ReadVector(t[0x10..]), ReadVector(t[0x1C..]),
            ReadVector(t[0x28..]), ReadVector(t[0x34..]));
    }

    private static Vector3 ReadVector(ReadOnlySpan<byte> b) => new(
        BinaryPrimitives.ReadSingleBigEndian(b),
        BinaryPrimitives.ReadSingleBigEndian(b[4..]),
        BinaryPrimitives.ReadSingleBigEndian(b[8..]));

    /// <summary>Each transform's parent, or −1 for roots, checking that it's a forest.</summary>
    private static int[] Hierarchy(Borg5Transform[] transforms)
    {
        var parents = Enumerable.Repeat(-1, transforms.Length).ToArray();
        for (var i = 0; i < transforms.Length; i++)
            foreach (var child in transforms[i].Children)
            {
                if (child <= i)
                    throw new InvalidDataException($"Transform {i} links back to transform {child}.");
                if (parents[child] >= 0)
                    throw new InvalidDataException($"Transform {child} has two parents.");
                parents[child] = i;
            }
        for (var i = 0; i < transforms.Length; i++)
            if (transforms[i].Flags.HasFlag(Borg5TransformFlags.Root) != parents[i] < 0)
                throw new InvalidDataException($"Transform {i}'s root flag doesn't match whether it has a parent.");
        return parents;
    }

    private static Borg5Draw[] Interpret((Borg5Opcode Op, byte Value)[] instructions, int textureCount, int modelCount,
        int firstNode, int transformCount)
    {
        var draws = new List<Borg5Draw>();
        int? texture = null, model = null, transform = null;
        foreach (var (op, value) in instructions)
        {
            switch (op)
            {
                case Borg5Opcode.LoadTexture:
                    texture = value == Untextured ? null : Check(value, textureCount, "texture");
                    break;
                case Borg5Opcode.LoadModel:
                    model = Check(value, modelCount, "model");
                    break;
                case Borg5Opcode.Matrix:
                    if (value < firstNode)
                        throw new InvalidDataException($"A Matrix instruction uses transform {value}, which is the camera or a light.");
                    transform = Check(value, transformCount, "transform");
                    break;
                case Borg5Opcode.Draw:
                    if (model is not { } m || transform is not { } t)
                        throw new InvalidDataException("A Draw instruction comes before a model and a transform are set.");
                    draws.Add(new Borg5Draw(texture, t, m, value));
                    break;
                default:
                    throw new InvalidDataException($"Unknown Borg5 instruction {(byte)op}.");
            }
        }
        return draws.ToArray();

        static int Check(int value, int count, string what) => value < count
            ? value
            : throw new InvalidDataException($"An instruction uses {what} {value} of {count}.");
    }

    private static int[] Indices(byte[] data, int offset, int count)
    {
        var span = Slice(data, offset, count * 4);
        var indices = new int[count];
        for (var i = 0; i < count; i++)
            indices[i] = BinaryPrimitives.ReadInt32BigEndian(span[(i * 4)..]);
        return indices;
    }

    private static ReadOnlySpan<byte> Slice(byte[] data, int offset, int length) =>
        offset >= 0 && length >= 0 && offset <= data.Length - length
            ? data.AsSpan(offset, length)
            : throw new InvalidDataException($"0x{length:X} bytes at 0x{offset:X} run past the end of the Borg5 (0x{data.Length:X}).");
}
