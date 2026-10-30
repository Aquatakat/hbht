using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// Wedged against leaning plain walls (docs/systems/movement.md#wedged-against-a-leaning-plain-wall): with plain collision's
/// push down once a frame, he walks out of the crease at Overworld A22 where the user found him wedged; and where he's still
/// held falling, as in C15's slot, the port's release lets him steer out.
/// </summary>
public sealed partial class GameSessionTests
{
    /// <summary>Alaron's actor, whose run moves him.</summary>
    private static readonly RecordId AlaronActor = RecordId.Hex(RecordTypes.Actor, 0x25F9);

    /// <summary>A tick with the stick held, a journal entry's notice closed first if one is up.</summary>
    private static void Hold(GameSession session, Vector2 stick)
    {
        if (session.Trek.Use.Popup is not null)
            session.Trek.Use.Accept();
        session.Tick(SessionSettings.Original, () => new PlayerInput(stick), false, _ => null);
    }

    [Fact]
    public void AtTheFootOfA22sLeaningRockFaceAlaronWalksIntoTheCreaseAndOutAgainEveryWay()
    {
        // The user's spot, his feet at (46.7, −26.3, 40.1): the rock face aidyn:map/0x0ECB's triangle 476, plain collision
        // (surface 1) leaning back at 76°, meets the grass below it (491). Facing −Z, up on the stick walks him into it.
        for (var way = 0; way < 8; way++)
        {
            var (session, _) = StartIn(0, "A22", 46.7f, 40.1f, y: -25.8f, setDown: false);
            var player = session.Trek.Player;
            session.Trek.PlayerAnimation = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
            for (var i = 0; i < 90; i++)
                Hold(session, new Vector2(0, 1));
            var crease = player.Sphere.Position;
            // Against the face, held up by the ground rule and pressed down once a frame: never falling fast enough to stop his
            // steering, as in 1.0, where the user found he walks off.
            Assert.True(player.Sphere.Velocity.Y > -0.048f, $"{player.Sphere.Velocity}");

            // Out every way within 4 seconds: up on the stick, he turns as he slides along the face, the slowest way in 2.6.
            var stick = new Vector2(MathF.Sin(way * MathF.PI / 4), MathF.Cos(way * MathF.PI / 4));
            for (var i = 0; i < 240; i++)
                Hold(session, stick);
            Assert.True(Vector3.Distance(crease, player.Sphere.Position) > 1, $"way {way}: from {crease} to {player.Sphere.Position}");
            Assert.False(player.FellOut);
        }
    }

    [Fact]
    public void InC15sSlotCollidedOnceAFrameAlaronWalksOutEveryWayWithoutTheRelease()
    {
        // The slot, about 1 wide, between C15's plain wall 335 (leaning back at 80°) and an upright plain wall facing it (1143):
        // collided every tick he hung between the two falling by his velocity, and only the port's release (PinnedTicks) let him
        // steer out. Collided once a frame, as 1.0 does, the frame's fall takes him down to the ground, and he walks out.
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var chunk = set.Chunks.Single(c => c.Name == "C15");
        var origin = set.Origin(chunk.Row, chunk.Column);
        var wall = CollisionMesh.For(Data.Set, chunk.Map)[335];
        var (a, b, c) = (wall.A + origin, wall.B + origin, wall.C + origin);
        var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
        Assert.Equal(1, wall.Surface);
        Assert.InRange(normal.Y, 0.16f, 0.18f);
        var world = new CollisionWorld();
        new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), world, set).Update(a);
        var inward = Vector2.Normalize(new Vector2(-normal.X, -normal.Z));
        var point = a * 0.6f + b * 0.2f + c * 0.2f;
        for (var way = 0; way < 8; way++)
        {
            var player = new PlayerMotion(point - new Vector3(inward.X, 0, inward.Y) * 1.2f + new Vector3(0, 0.3f, 0), inward);
            var run = new SteadyMotion(0.06f);
            for (var i = 0; i < 20; i++)
                player.Step(world, new MoveInput(Vector2.Zero), inward, run);
            for (var i = 0; i < 60; i++)
                player.Step(world, new MoveInput(new Vector2(0, 1)), inward, run);
            var notch = player.Sphere.Position;
            var stick = new Vector2(MathF.Sin(way * MathF.PI / 4), MathF.Cos(way * MathF.PI / 4));
            for (var i = 0; i < 120; i++)
            {
                player.Step(world, new MoveInput(stick), inward, run);
                Assert.True(player.Pinned < PlayerMotion.PinnedTicks, $"way {way}: pinned {player.Pinned}");
            }
            Assert.False(player.FellOut);
            Assert.True(Vector3.Distance(notch, player.Sphere.Position) > 1, $"way {way}: from {notch} to {player.Sphere.Position}");
        }
    }

    [Fact]
    public void InD14sVHeldFallingAlaronSteersAgainAfterAQuarterSecondAndWalksOutEveryWay()
    {
        // The V between D14's leaning rock (Expansion Pak triangles 605 and 606) and the upright face behind it (627), plain
        // collision both, a low rock about 1.4 high: run into it from the south-west, he hangs on it falling by his velocity and
        // never dropping, and only the port's release (PinnedTicks) lets him steer away.
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var chunk = set.Chunks.Single(c => c.Name == "D14");
        var origin = set.Origin(chunk.Row, chunk.Column);
        var world = new CollisionWorld();
        var start = origin + new Vector3(55.2f, -16.8f, 17.6f);
        new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), world, set).Update(start);
        var inward = Vector2.Normalize(new Vector2(0.64f, 0.76f));
        var took = new List<int>();
        for (var way = 0; way < 8; way++)
        {
            var player = new PlayerMotion(start, inward);
            var run = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
            var held = 0;
            for (var i = 0; i < 90; i++)
            {
                player.Step(world, new MoveInput(new Vector2(0, 1)), inward, run);
                held = Math.Max(held, player.Pinned);
            }
            Assert.True(held >= PlayerMotion.PinnedTicks, $"held {held}");
            var notch = player.Sphere.Position;
            var stick = new Vector2(MathF.Sin(way * MathF.PI / 4), MathF.Cos(way * MathF.PI / 4));
            var ticks = 0;
            for (; ticks < 600 && Vector3.Distance(notch, player.Sphere.Position) <= 1; ticks++)
                player.Step(world, new MoveInput(stick), inward, run);
            Assert.False(player.FellOut);
            took.Add(ticks);
        }
        // Six ways in about a third of a second, just past the release; straight on, and straight back, slower.
        Assert.All(took, t => Assert.InRange(t, 1, 420));
        Assert.Equal(6, took.Count(t => t < 30));
    }

    [Fact]
    public void RunningUpQ25sSteepPlainFaceAlaronSlidesBackDownAsIn1_0()
    {
        // The user's climb out of the world, from (91.9, 1.4, 45.7) toward (90.6, 8.6, 41.0): a plain face (surface 1) of normal
        // Y 0.52 to 0.57. Collided once a frame after the frame's ticks of velocity, as ProcessPlayers does, plain collision's
        // kept fall carries him back down it, where collided every tick he walked up it.
        var climbed = RunUpQ25(new Vector3(91.9f, 2.2f, 45.7f), new Vector2(90.6f, 41.0f), pace: 1);
        Assert.True(climbed < 0.5f, $"climbed {climbed}");
    }

    [Fact]
    public void AtBriskPaceAlaronSlidesBackDownQ25sSteepPlainFacesToo()
    {
        // Brisk's faster steps, with his gravity and velocity no faster, walked him up the plain face of normal Y 0.48 east of the
        // user's climb, from 2.7 to above 6; his physics runs at his pace, the user's choice, and he stays at its foot. The user's
        // climb holds at Brisk too.
        Assert.True(RunUpQ25(new Vector3(95.1f, 2.7f, 48.9f), new Vector2(94.83f, 45.9f), SessionSettings.BriskPace) < 0.5f);
        Assert.True(RunUpQ25(new Vector3(91.9f, 2.2f, 45.7f), new Vector2(90.6f, 41.0f), SessionSettings.BriskPace) < 0.5f);
    }

    /// <summary>Alaron with his run at <paramref name="pace"/>, from a point of Q25 toward another for 10 seconds: how far above his start he got.</summary>
    private float RunUpQ25(Vector3 local, Vector2 toward, float pace)
    {
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0));
        var chunk = set.Chunks.Single(c => c.Name == "Q25");
        var origin = set.Origin(chunk.Row, chunk.Column);
        var world = new CollisionWorld();
        var start = origin + local;
        new ChunkStreamer(Data.Set, GameState.NewGame(Data.Set), world, set).Update(start);
        var up = Vector2.Normalize(toward - new Vector2(local.X, local.Z));
        var player = new PlayerMotion(start, up) { Pace = pace };
        var run = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(AlaronActor), new Lcg(0))!;
        var top = start.Y;
        for (var i = 0; i < 600; i++)
        {
            player.Step(world, new MoveInput(new Vector2(0, 1)), up, run);
            top = MathF.Max(top, player.Sphere.Position.Y);
            Assert.False(player.FellOut);
        }
        return top - start.Y;
    }
}
