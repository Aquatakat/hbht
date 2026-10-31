using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// Actors' timing and root motion, read headlessly from their scenes' assets, against what the runtime's Godot animation
/// player made of the same assets before the core read them (recorded from the runtime at main's e3bef38).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ActorMotionTests(Na10Import fixture)
{
    private static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Actor, 0x25F9);

    /// <summary>Brenna's actor, whose idle repeats 90% of the time and fidgets 10%.</summary>
    private static readonly RecordId Brenna = RecordId.Hex(RecordTypes.Actor, 0x29E3);

    private PluginSet Plugins => fixture.Data.Set;

    private ActorRecord Actor(RecordId id) => Plugins.Get<ActorRecord>(id);

    /// <summary>Each state's length in ticks, as Godot gave it: its clip's length × 60, rounded.</summary>
    [Theory]
    [InlineData(0x25F9, "139,25,25,25,33,55,37,35,35,51,71,21,19,19,29,29,17,17,17,17,17,17,17,17,17,17,17,17,111,91,221,95,121,91,111,121,181,3,111,71,141,91,141,77,117")]
    [InlineData(0x0E6B, "11,81,83,37,53,53,39,11")]
    [InlineData(0x26AC, "101,19,27,27,25,25,141,105,97,143,101,103,151")]
    [InlineData(0x2CC0, "119,23,27,27,31,39,259")]
    [InlineData(0x2D4A, "119,35,39,39,179,139,139,319")]
    [InlineData(0x3019, "159,27,27,23,43,43,219,159,219,99,139,139")]
    public void StatesLastAsLongAsTheRuntimesClips(int actor, string lengths)
    {
        var motion = ActorMotion.For(Plugins, Actor(RecordId.Hex(RecordTypes.Actor, actor)), new Lcg(0))!;
        var machine = motion.Machine;
        Assert.Equal(lengths, string.Join(",", Enumerable.Range(0, machine.Actor.States.Count).Select(machine.Length)));
    }

    /// <summary>
    /// Alaron standing for 60 ticks and then walking, tick by tick from the second: the request, the state and clock
    /// after the tick, and how far his root moved him forward, as the runtime had them. Idle drifts by millionths; each
    /// walk state's first three ticks move nothing.
    /// </summary>
    private static readonly (int Request, int State, int Clock, float Forward)[] Walk =
    [
        (0, 0, 3, -2.1606684E-07f), (2, 1, 1, 0f), (2, 1, 2, 0f), (2, 1, 3, 0f), (2, 1, 4, 0.02580626f),
        (2, 1, 5, 0.025806256f), (2, 1, 6, 0.025818966f), (2, 1, 7, 0.025818966f), (2, 1, 8, 0.025825314f), (2, 1, 9, 0.025825322f),
        (2, 1, 10, 0.025825322f), (2, 1, 11, 0.025825322f), (2, 1, 12, 0.025818974f), (2, 1, 13, 0.025818974f), (2, 1, 14, 0.025806248f),
        (2, 1, 15, 0.025806278f), (2, 1, 16, 0.025787205f), (2, 1, 17, 0.025787175f), (2, 1, 18, 0.025761783f), (2, 1, 19, 0.025761753f),
        (2, 1, 20, 0.025729984f), (2, 1, 21, 0.025730014f), (2, 1, 22, 0.025691867f), (2, 1, 23, 0.025691867f), (2, 1, 24, 0.025647402f),
        (2, 1, 25, 0.025647402f), (2, 2, 1, 0f), (2, 2, 2, 0f), (2, 2, 3, 0f), (2, 2, 4, 0.023792803f), (2, 2, 5, 0.023792803f),
        (2, 2, 6, 0.023792803f), (2, 2, 7, 0.023792803f), (2, 2, 8, 0.023792803f), (2, 2, 9, 0.023792803f), (2, 2, 10, 0.023792803f),
        (2, 2, 11, 0.023792803f), (2, 2, 12, 0.023792803f), (2, 2, 13, 0.023792803f), (2, 2, 14, 0.023792803f), (2, 2, 15, 0.023792803f),
        (2, 2, 16, 0.023792803f), (2, 2, 17, 0.023792744f), (2, 2, 18, 0.023792744f), (2, 2, 19, 0.023792744f), (2, 2, 20, 0.023792744f),
        (2, 2, 21, 0.023792744f), (2, 2, 22, 0.023792744f), (2, 2, 23, 0.023792744f), (2, 2, 24, 0.023792744f), (2, 2, 25, 0.023792744f),
        (2, 3, 1, 0f), (2, 3, 2, 0f), (2, 3, 3, 0f), (2, 3, 4, 0.023775458f), (2, 3, 5, 0.023775578f), (2, 3, 6, 0.023768306f),
        (2, 3, 7, 0.023768306f), (2, 3, 8, 0.02376473f),
    ];

    /// <summary>The ticks of <see cref="Walk"/>, counting from 1: the second, then the 61st on.</summary>
    private static int WalkTick(int i) => i < 1 ? 2 : i + 60;

    /// <summary>
    /// Alaron's body, standing and then walking by his animations' root motion, steps through the same states and
    /// clocks as the runtime did, and as far. Godot resampled each clip at 30 a second as it loaded the glTF, so the
    /// runtime's root stood exactly on the baked samples only every other tick, halfway between them on the ticks
    /// between: each tick it moved him the mean of two ticks' motion, within 0.00002 of the core's, and where Godot stood
    /// on a sample he had gone exactly as far. On his first tick the runtime moved him by where his idle's root starts,
    /// 0.037 back, taking the root to have stood at the origin before; the core moves him from where it starts. The body moves
    /// once a frame, by the frame's two ticks.
    /// </summary>
    [Fact]
    public void ABodyWalksByItsAnimationAsTheRuntimeMovedIt()
    {
        var motion = ActorMotion.For(Plugins, Actor(Alaron), new Lcg(0))!;
        var body = new Body(new Vector3(0, 100, 0), 0.5f) { Heading = Vector2.UnitY };
        var world = new CollisionWorld();
        var last = WalkTick(Walk.Length - 1);
        var checkedTicks = 0;
        float walked = 0, runtimeWalked = 0, frameMoved = 0, frameForward = 0;
        for (var tick = 1; tick <= last; tick++)
        {
            body.Request = tick <= 60 ? Body.Idle : Body.Walk;
            var before = body.Sphere.Position;
            body.Step(world, motion);
            var moved = body.Sphere.Position - before;
            Assert.Equal(0, moved.X);
            frameMoved += moved.Z;
            var at = Enumerable.Range(0, Walk.Length).FirstOrDefault(i => WalkTick(i) == tick, -1);
            if (at >= 0)
            {
                var (request, state, clock, forward) = Walk[at];
                Assert.Equal((request, state, clock), (body.Request, motion.Machine.State, motion.Machine.Clock));
                frameForward += forward;
                checkedTicks++;
            }
            // The body moves once a frame, by the frame's two ticks of root motion, as ProcessPlayers moves it.
            if (tick % Cadence.FrameTicks != 0)
            {
                Assert.Equal(before.Z, body.Sphere.Position.Z);
                continue;
            }
            if (tick == 2 || tick > 60)
                Assert.Equal(frameForward, frameMoved, 4e-5f);
            if (tick > 60)
            {
                walked += frameMoved;
                runtimeWalked += frameForward;
                Assert.Equal(runtimeWalked, walked, 4e-5f);
            }
            (frameMoved, frameForward) = (0, 0);
        }
        Assert.Equal(Walk.Length, checkedTicks);
        Assert.InRange(walked, 1.2f, 1.22f);
    }

    /// <summary>Each animation's root positions are the glTF's samples: one a tick, and one more, which is the first again.</summary>
    [Fact]
    public void RootTracksHaveASampleForEveryTickOfTheLoop()
    {
        var actor = Actor(Alaron);
        var clips = ActorClips.For(Plugins, actor.Scene, actor.RootTransform)!;
        foreach (var state in actor.States)
        {
            var clip = clips.Clip(state.Animation)!;
            Assert.NotNull(clip.Root);
            Assert.Equal(clip.Length + 1, clip.Root.Count);
            Assert.Equal(clip.Root[0], clip.Root[^1]);
        }
    }

    /// <summary>The same actor rolls the same branches given the same stream, whatever else it's stepped beside.</summary>
    [Fact]
    public void BranchRollsFollowTheStream()
    {
        int[] States(IRandom rolls, IRandom? beside = null)
        {
            var motion = ActorMotion.For(Plugins, Actor(Brenna), rolls)!;
            var other = beside is null ? null : ActorMotion.For(Plugins, Actor(RecordId.Hex(RecordTypes.Actor, 0x2CC0)), beside)!;
            var states = new int[12000];
            for (var i = 0; i < states.Length; i++)
            {
                motion.Tick(Body.Idle);
                other?.Tick(Body.Idle);
                states[i] = motion.Machine.State;
            }
            return states;
        }

        var alone = States(new Lcg(0));
        Assert.Equal(alone, States(new Lcg(0), new Lcg(0)));
        Assert.True(alone.Distinct().Count() > 1, "Idle fidgets now and then.");
        // Sharing a stream with another actor, as bodies do, changes what each rolls.
        var shared = new Lcg(0);
        Assert.NotEqual(alone, States(shared, shared));
    }
}
