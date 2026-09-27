using System.Buffers.Binary;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Tests;

public class AnimationTests
{
    /// <summary>
    /// A cubic from a value of 0 with differences 1, 2 and 6: after <c>n</c> ticks the value is
    /// <c>n + 2·C(n,2) + 6·C(n,3)</c>. At tick 5 a key loads new differences and keeps the value.
    /// </summary>
    [Fact]
    public void TracksStepTheirDifferencesAndLoop()
    {
        var animation = Borg6Animation.Parse(Build(length: 8, Borg6Channels.PositionY,
            (1, 0xF, [0, 1, 2, 6]), (5, 0x2, [-10])));
        var player = new Borg6Player(animation);

        var values = new List<float> { player.Value(0, 0) };
        for (var i = 0; i < 9; i++)
        {
            player.Tick();
            values.Add(player.Value(0, 0));
        }

        // Times 1–4 follow the cubic. The key at 5 loads a slope of −10 (mask 2 zeroes the higher
        // differences) and steps it at once. At time 8 the counter reaches the length, so the next
        // tick goes back to the first key without stepping, and time 10 is time 2 again.
        Assert.Equal([0, 1, 4, 15, 40, 30, 20, 10, 0, 1], values);
        Assert.Equal(10, player.Time);
    }

    [Fact]
    public void PosesReplaceOnlyTheDrivenChannels()
    {
        var animation = Borg6Animation.Parse(Build(length: 4, Borg6Channels.RotationZ, (1, 0x1, [30])));
        var rest = new Borg5Transform(Borg5TransformFlags.Root, Borg5TransformOp.PivotRotate, [],
            new(1, 2, 3), new(4, 5, 6), new(7, 8, 9), new(10, 11, 12));

        var pose = new Borg6Player(animation).Pose(0, rest);

        Assert.Equal(rest with { Rotation = new(1, 2, 30) }, pose);
    }

    [Fact]
    public void EveryBorg6Parses()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        int animations = 0, targets = 0, tracks = 0, keys = 0;
        var scenes = new HashSet<int>();
        var sceneCache = new Dictionary<int, Borg5Scene>();
        var targetKinds = new Dictionary<string, int>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Animation))
        {
            var animation = Borg6Animation.Parse(archive.Read(listing.Index));
            animations++;
            scenes.Add(animation.Scene);
            Assert.Equal(BorgType.Scene, archive.Listings[animation.Scene].Type);
            if (!sceneCache.TryGetValue(animation.Scene, out var scene))
                sceneCache[animation.Scene] = scene = Borg5Scene.Parse(archive.Read(animation.Scene));

            foreach (var target in animation.Targets)
            {
                targets++;
                tracks += target.Tracks.Length;
                keys += target.Tracks.Sum(t => t.Keys.Length);
                Assert.InRange(target.Transform, 0, scene.Transforms.Count - 1);
                var kind = target.Transform == 0 ? "camera" : target.Transform < scene.FirstNode ? "light" : "node";
                targetKinds[kind] = targetKinds.GetValueOrDefault(kind) + 1;
            }
        }

        Assert.Equal((1988, 259, 29_473, 84_958), (animations, scenes.Count, targets, tracks));
        Assert.Equal(731_767, keys);
        // Cinematics also move the camera and turn the lights.
        Assert.Equal(new Dictionary<string, int> { ["camera"] = 32, ["light"] = 35, ["node"] = 29_406 }, targetKinds);
    }

    /// <summary>
    /// Every animation plays with finite values, drives each transform once (so the game's zeroing
    /// and adding comes to setting), and is back at its start after <see cref="Borg6Animation.Length"/> ticks.
    /// </summary>
    [Fact]
    public void EveryBorg6LoopsBackToItsStart()
    {
        var archive = BorgArchive.Open(TestRoms.Get(RomVersionId.Na10));
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Animation))
        {
            var animation = Borg6Animation.Parse(archive.Read(listing.Index));
            Assert.Equal(animation.Targets.Count, animation.Targets.Select(t => t.Transform).Distinct().Count());

            var player = new Borg6Player(animation);
            var start = Values(player);
            for (var i = 0; i < animation.Length; i++)
            {
                player.Tick();
                Assert.All(Values(player), v => Assert.True(float.IsFinite(v), $"Borg6 {listing.Index} at time {player.Time}"));
            }
            Assert.Equal(start, Values(player));
        }

        static float[] Values(Borg6Player p) => p.Animation.Targets
            .SelectMany((t, i) => t.Tracks.Select((_, j) => p.Value(i, j))).ToArray();
    }

    /// <summary>Builds a Borg6 of one target on transform 1 with one track.</summary>
    private static byte[] Build(int length, Borg6Channels channel, params (int Tick, int Mask, float[] Values)[] keys)
    {
        const int target = 0x10, track = 0x28, keyList = 0x38;
        var floats = keyList + (keys.Length + 1) * 8;
        var data = new byte[floats + keys.Sum(k => k.Values.Length) * 4];

        void Word(int at, int value) => BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(at), value);
        void Half(int at, int value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(at), (ushort)value);

        Word(0, 99);
        Word(4, 1);
        Word(8, target);
        Word(0xC, length);
        Word(target, 99);
        Word(target + 4, 1);
        Word(target + 8, 1);
        Word(target + 0xC, (int)channel);
        Word(target + 0x10, 1);
        Word(target + 0x14, track);
        Half(track, length);
        Word(track + 4, keyList);

        var at = floats;
        for (var k = 0; k <= keys.Length; k++)
        {
            var (tick, mask, values) = k < keys.Length ? keys[k] : (length, 0, []);
            Half(keyList + k * 8, tick);
            Half(keyList + k * 8 + 2, mask);
            Word(keyList + k * 8 + 4, values.Length > 0 ? at : 0);
            foreach (var v in values)
            {
                BinaryPrimitives.WriteSingleBigEndian(data.AsSpan(at), v);
                at += 4;
            }
        }
        return data;
    }
}
