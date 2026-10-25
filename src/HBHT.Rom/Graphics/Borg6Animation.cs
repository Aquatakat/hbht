using System.Buffers.Binary;
using System.Numerics;

namespace HBHT.Rom.Graphics;

/// <summary>
/// Which fields of a <see cref="Borg5Transform"/> an animation drives, in the order of its tracks.
/// </summary>
[Flags]
public enum Borg6Channels : ushort
{
    None = 0,
    RotationX = 0x1,
    RotationY = 0x2,
    RotationZ = 0x4,
    PositionX = 0x8,
    PositionY = 0x10,
    PositionZ = 0x20,
    ScaleX = 0x40,
    ScaleY = 0x80,
    ScaleZ = 0x100,
    All = 0x1FF,
}

/// <summary>
/// A key of a <see cref="Borg6Track"/>. The state it loads is a value and its first three forward
/// differences, and <see cref="Mask"/> says which of the four it holds.
/// </summary>
/// <param name="Tick">The track's counter value at which the key loads. The first key is at 1.</param>
/// <param name="Mask">Bit <c>i</c> is state <c>i</c>: 1 the value, then 2, 4 and 8 the differences.</param>
/// <param name="Values">One per set bit of <see cref="Mask"/>, from bit 0 up.</param>
public sealed record Borg6Key(int Tick, byte Mask, float[] Values);

/// <param name="Channel">A single channel.</param>
/// <param name="Keys">In tick order. The end marker, whose tick is the animation's length, isn't included: the game never loads it.</param>
public sealed record Borg6Track(Borg6Channels Channel, Borg6Key[] Keys);

/// <summary>The tracks that drive one transform of the scene.</summary>
/// <param name="Transform">An index into <see cref="Borg5Scene.Transforms"/>.</param>
/// <param name="Tracks">One per channel, in bit order.</param>
public sealed record Borg6Target(int Transform, Borg6Channels Channels, Borg6Track[] Tracks);

/// <summary>
/// A Borg6: an animation of the transforms of one Borg5 scene (roadmap 1B). Actors (Borg7) switch
/// between them, and each shot of a cinematic plays one.
/// </summary>
/// <remarks>
/// <para>
/// The file is a 0x10-byte header (the scene's archive index, the target count, the targets' offset,
/// which is always 0x10, and the length in ticks), then the 0x18-byte targets. A target is the
/// scene's index again, a type, the transform index, the channel mask, the track count and the
/// tracks' offset. Each 0x10-byte track is its length, a zero u16, the keys' offset, a playback
/// mode and a zero word. Each 8-byte key is a u16 tick, a u16 mask and the offset of its floats.
/// The key lists end with a key whose tick is the track's length. Offsets are from the start of the
/// data.
/// </para>
/// <para>
/// The game can also animate other Borgs' fields (target types 0, 2 and 3), and it has playback
/// modes other than looping, but no animation in the ROM uses them: every target is a transform,
/// every mode is 0 and every track is as long as its animation.
/// </para>
/// </remarks>
public sealed class Borg6Animation
{
    public const int HeaderSize = 0x10;
    private const int TargetSize = 0x18, TrackSize = 0x10, KeySize = 8;
    private const int TransformTarget = 1;

    private Borg6Animation(int scene, int length, Borg6Target[] targets)
    {
        Scene = scene;
        Length = length;
        Targets = targets;
    }

    /// <summary>The Borg5's archive index.</summary>
    public int Scene { get; }

    /// <summary>
    /// The length in 60 Hz ticks. The tracks loop after this many, and an actor's animation ends
    /// when the scene's clock reaches it.
    /// </summary>
    public int Length { get; }

    public IReadOnlyList<Borg6Target> Targets { get; }

    public static Borg6Animation Parse(byte[] data)
    {
        int Word(int offset) => Be.S32(Be.Slice(data, offset, 4, "Borg6"), 0);
        ushort Half(int offset) => Be.U16(Be.Slice(data, offset, 2, "Borg6"), 0);

        int scene = Word(0), targetCount = Word(4), targets = Word(8), length = Word(0xC);
        if (targets != HeaderSize)
            throw new InvalidDataException($"The targets are at 0x{targets:X}, not straight after the header.");
        if (targetCount < 0 || length < 2)
            throw new InvalidDataException($"{targetCount} targets over {length} ticks.");

        var end = targets + targetCount * TargetSize;
        var list = new Borg6Target[targetCount];
        for (var i = 0; i < targetCount; i++)
        {
            var at = targets + i * TargetSize;
            if (Word(at) != scene)
                throw new InvalidDataException($"Target {i} names scene {Word(at)}, not the animation's {scene}.");
            if (Word(at + 4) != TransformTarget)
                throw new InvalidDataException($"Target {i} has type {Word(at + 4)}; only transforms (1) are known.");
            int transform = Word(at + 8), mask = Word(at + 0xC), trackCount = Word(at + 0x10), tracks = Word(at + 0x14);
            if (mask is <= 0 or > (int)Borg6Channels.All || BitOperations.PopCount((uint)mask) != trackCount)
                throw new InvalidDataException($"Target {i} has channel mask 0x{mask:X} and {trackCount} tracks.");

            var channels = (Borg6Channels)mask;
            var trackList = new Borg6Track[trackCount];
            var bit = 1;
            for (var j = 0; j < trackCount; j++, bit <<= 1)
            {
                while ((mask & bit) == 0)
                    bit <<= 1;
                var t = tracks + j * TrackSize;
                int trackLength = Half(t), keys = Word(t + 4);
                if (trackLength != length)
                    throw new InvalidDataException($"Track {j} of target {i} is {trackLength} ticks, not {length}.");
                if (Half(t + 2) != 0 || Word(t + 8) != 0 || Word(t + 0xC) != 0)
                    throw new InvalidDataException($"Track {j} of target {i} has an unknown mode or field set.");
                trackList[j] = new Borg6Track((Borg6Channels)bit, ReadKeys(data, keys, length, ref end));
                end = Math.Max(end, t + TrackSize);
            }
            list[i] = new Borg6Target(transform, channels, trackList);
        }

        // Files are padded to 8 bytes.
        if (end > data.Length || data.Length - end >= 8)
            throw new InvalidDataException($"The animation's data ends at 0x{end:X}, but the Borg6 is 0x{data.Length:X} bytes.");
        return new Borg6Animation(scene, length, list);
    }

    private static Borg6Key[] ReadKeys(byte[] data, int offset, int length, ref int end)
    {
        var keys = new List<Borg6Key>();
        for (var k = 0; ; k++)
        {
            var at = offset + k * KeySize;
            var key = Be.Slice(data, at, KeySize, "Borg6");
            int tick = BinaryPrimitives.ReadUInt16BigEndian(key), mask = BinaryPrimitives.ReadUInt16BigEndian(key[2..]);
            var floats = BinaryPrimitives.ReadInt32BigEndian(key[4..]);
            end = Math.Max(end, at + KeySize);
            if (mask > 0xF)
                throw new InvalidDataException($"The key at 0x{at:X} has mask 0x{mask:X}; a key holds at most four values.");
            if (tick <= (keys.Count > 0 ? keys[^1].Tick : 0) || tick > length)
                throw new InvalidDataException($"The key at 0x{at:X} is at tick {tick}, out of order.");
            if (k == 0 && (tick != 1 || (mask & 1) == 0))
                throw new InvalidDataException($"The first key at 0x{at:X} doesn't set a value at tick 1.");

            var values = new float[BitOperations.PopCount((uint)mask)];
            var span = Be.Slice(data, floats, values.Length * 4, "Borg6");
            for (var v = 0; v < values.Length; v++)
                values[v] = BinaryPrimitives.ReadSingleBigEndian(span[(v * 4)..]);
            if (values.Length > 0)
                end = Math.Max(end, floats + values.Length * 4);

            if (tick == length)
                return keys.ToArray();
            keys.Add(new Borg6Key(tick, (byte)mask, values));
        }
    }
}
