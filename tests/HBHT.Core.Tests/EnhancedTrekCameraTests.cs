using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Physics;

namespace HBHT.Core.Tests;

/// <summary>The enhanced trek camera (<see cref="EnhancedTrekCamera"/>) on small worlds: its spring arm, its arrival and its wait after input.</summary>
public class EnhancedTrekCameraTests
{
    private static readonly Vector3 Player = new(0, 0.5f, 0);

    private static (EnhancedTrekCamera Enhanced, FollowCamera Camera) Arrived(CollisionWorld world, Vector2 heading, VisibleWorld? visible = null,
        bool interior = false)
    {
        var camera = new FollowCamera(Player, heading);
        var enhanced = new EnhancedTrekCamera();
        enhanced.Arrive(camera, world, visible, Player, heading, interior, kept: false);
        return (enhanced, camera);
    }

    private static CollisionWorld FlatWith(IEnumerable<Plugins.Assets.CollisionTriangle> wall)
    {
        var world = Worlds.Flat();
        world.Add(Worlds.Chunk(1), wall, Vector3.Zero);
        return world;
    }

    [Fact]
    public void ArrivingItIsBehindHimLookingHisWayAtTheBoomsLength()
    {
        var (enhanced, camera) = Arrived(Worlds.Flat(), new Vector2(0, 1));
        Assert.Equal(TrekCameraTuning.Default.OutdoorLength, enhanced.Arm, 3);
        Assert.True(camera.Position.Z < -4.5f);
        Assert.Equal(0, camera.Position.X, 3);
        Assert.Equal(0, camera.LookXZ.X, 3);
        Assert.Equal(1, camera.LookXZ.Y, 3);
        Assert.True(camera.TakeCut());

        var (indoors, _) = Arrived(Worlds.Flat(), new Vector2(0, 1), interior: true);
        Assert.Equal(TrekCameraTuning.Default.InteriorLength, indoors.Arm, 3);
    }

    [Theory]
    // A wall at X = -2 facing -X, met by the arm from behind: one a one-sided sweep out from the pivot never sees.
    [InlineData(-2f, 1f)]
    // The same wall met from in front, at X = 2, behind a player facing -X.
    [InlineData(2f, -1f)]
    public void TheArmStopsShortOfAWallFromEitherSide(float wallX, float headingX)
    {
        var world = FlatWith(Worlds.Wall(wallX, 10));
        var (enhanced, camera) = Arrived(world, new Vector2(headingX, 0));
        var t = TrekCameraTuning.Default;
        Assert.True(MathF.Abs(camera.Position.X) < MathF.Abs(wallX) - t.Radius, $"the camera at {camera.Position} is past the wall at {wallX}");
        // Behind him the arm can't reach the length, so the arrival frame is round to one side where it does.
        Assert.NotEqual(TrekArrivalKind.Behind, enhanced.Arrival.Kind);
        Assert.True(enhanced.Arrival.Reached);
        // Stepping on, standing, it stays inside.
        for (var i = 0; i < 120; i++)
        {
            enhanced.Step(camera, world, null, Player, new Vector2(headingX, 0), interior: false, Vector2.Zero);
            Assert.True(MathF.Abs(camera.Position.X) < MathF.Abs(wallX) - t.Radius);
        }
    }

    [Fact]
    public void TheArmStopsUnderACeilingOnlyTheDrawnWorldHas()
    {
        var world = Worlds.Flat();
        var visible = new VisibleWorld();
        // A ceiling 2.5 above the floor, drawn but with no collision.
        visible.Add(Worlds.Chunk(1), Worlds.Floor(3, -50, 60, -50, 60).Drawn(), Vector3.Zero);
        var tuning = TrekCameraTuning.Default with { OutdoorPitch = 45 };
        var camera = new FollowCamera(Player, Vector2.UnitX);
        var enhanced = new EnhancedTrekCamera { Tuning = tuning };
        enhanced.Arrive(camera, world, visible, Player, Vector2.UnitX, interior: false, kept: false);
        Assert.True(camera.Position.Y < 3 - tuning.Radius, $"the camera at {camera.Position} is through the ceiling");
        Assert.True(enhanced.Arm < tuning.OutdoorLength - 1);
        // A light shaft, which absorbs, doesn't stop it.
        var shafts = new VisibleWorld();
        shafts.Add(Worlds.Chunk(1), Worlds.Floor(3, -50, 60, -50, 60).Drawn(absorbs: true), Vector3.Zero);
        var open = new EnhancedTrekCamera { Tuning = tuning };
        open.Arrive(new FollowCamera(Player, Vector2.UnitX), world, shafts, Player, Vector2.UnitX, interior: false, kept: false);
        Assert.Equal(tuning.OutdoorLength, open.Arm, 3);
    }

    [Fact]
    public void TheAutomaticYawWaitsTwoSecondsAfterThePlayerTurnsIt()
    {
        var world = Worlds.Flat();
        var (enhanced, camera) = Arrived(world, Vector2.UnitX);
        var player = Player;
        void Walk(int ticks, Vector2 turn = default)
        {
            for (var i = 0; i < ticks; i++)
            {
                player += new Vector3(0.06f, 0, 0);
                enhanced.Step(camera, world, null, player, Vector2.UnitX, interior: false, turn);
            }
        }

        Walk(30);
        Assert.Equal(180, MathF.Abs(enhanced.Yaw), 1f);
        // Turning it: the yaw moves at once, 1.5° a tick with the stick full over.
        Walk(20, new Vector2(1, 0));
        var turned = enhanced.Yaw;
        Assert.Equal(180 - 30, MathF.Abs(turned), 1f);
        Assert.True(enhanced.Waiting);
        // For two seconds it holds where he left it, though he walks on.
        Walk(110);
        Assert.Equal(turned, enhanced.Yaw, 0.5f);
        // Then it trails behind him again.
        Walk(240);
        Assert.False(enhanced.Waiting);
        Assert.Equal(180, MathF.Abs(enhanced.Yaw), 2f);
    }

    [Fact]
    public void ACameraMovedByAnotherIsTakenUpFromWhereItIs()
    {
        var world = Worlds.Flat();
        var (enhanced, camera) = Arrived(world, Vector2.UnitX);
        // A camera object puts it off to the side, then lets go.
        camera.PlaceAt(new Vector3(0, 3, 6));
        camera.AimAt(Player + new Vector3(0, 1.05f, 0));
        var before = camera.Position;
        enhanced.Step(camera, world, null, Player, Vector2.UnitX, interior: false, Vector2.Zero);
        // It glides from there rather than snapping back behind him.
        Assert.True(Vector3.Distance(before, camera.Position) < 0.2f, $"it jumped from {before} to {camera.Position}");
    }

    [Theory]
    // In front of him, looking back at him, and behind him: an authored frame either way.
    [InlineData(6f)]
    [InlineData(-6f)]
    public void ATeleportersCameraPointIsKeptWhereItFramesHim(float pointX)
    {
        var camera = new FollowCamera(Player, Vector2.UnitX);
        camera.PlaceAt(new Vector3(pointX, 3, 1));
        var enhanced = new EnhancedTrekCamera();
        enhanced.Arrive(camera, Worlds.Flat(), null, Player, Vector2.UnitX, interior: false, kept: true);
        Assert.Equal(pointX, camera.Position.X, 3);
        Assert.Equal(TrekArrivalKind.Authored, enhanced.Arrival.Kind);
        // One looking away from him is only where the original's camera starts: the arrival frame instead.
        camera.PlaceAt(new Vector3(pointX, 3, 1));
        camera.AimAt(new Vector3(pointX, 3, 20));
        enhanced.Arrive(camera, Worlds.Flat(), null, Player, Vector2.UnitX, interior: false, kept: true);
        Assert.Equal(TrekArrivalKind.Behind, enhanced.Arrival.Kind);
        Assert.True(camera.Position.X < -4);
    }

    [Fact]
    public void AViewHeDidntChooseSwingsBehindHimQuicklyEvenOutdoors()
    {
        var world = Worlds.Flat();
        var camera = new FollowCamera(Player, Vector2.UnitX);
        var enhanced = new EnhancedTrekCamera();
        enhanced.Step(camera, world, null, Player, Vector2.UnitX, interior: false, Vector2.Zero);
        // A camera object leaves the camera in front of him, looking back at him, and lets go.
        camera.PlaceAt(new Vector3(5, 2, 0));
        camera.AimAt(Player + new Vector3(0, 1.05f, 0));
        var player = Player;
        var ticks = 0;
        for (; ticks < 300 && MathF.Abs(enhanced.Yaw) < 170; ticks++)
        {
            player += new Vector3(0.06f, 0, 0);
            enhanced.Step(camera, world, null, player, Vector2.UnitX, interior: false, Vector2.Zero);
        }
        Assert.True(ticks < 90, $"it took {ticks} ticks to come round behind him");
    }

    [Fact]
    public void ItsNumbersAreSetByName()
    {
        var tuning = TrekCameraTuning.Default.Parse("OpenTurn=60, interiorlength=3");
        Assert.Equal(60, tuning.OpenTurn);
        Assert.Equal(3, tuning.InteriorLength);
        Assert.Equal(TrekCameraTuning.Default.OutdoorLength, tuning.OutdoorLength);
        Assert.Throws<FormatException>(() => TrekCameraTuning.Default.Parse("Nothing=1"));
    }

    [Fact]
    public void BackedAgainstADoorItFramesHimFromTheSideAndSwingsBehindTheOpenWay()
    {
        // A wall 0.6 behind him, as at the door he came through, with a drawn ceiling 4.8 up.
        var world = FlatWith(Worlds.Wall(-0.6f, 10));
        var visible = new VisibleWorld();
        visible.Add(Worlds.Chunk(1), Worlds.Floor(4.8f, -50, 60, -50, 60).Drawn(), Vector3.Zero);
        var (enhanced, camera) = Arrived(world, Vector2.UnitX, visible, interior: true);
        Assert.Equal(TrekArrivalKind.Side, enhanced.Arrival.Kind);
        Assert.True(enhanced.Arrival.Reached);
        Assert.True(camera.Position.X > -0.6f + 0.25f, $"the camera at {camera.Position} is past the wall");
        // Walking off, it comes round behind him without going through the wall's side, where the arm would close up.
        var player = Player;
        var lifted = 0;
        for (var i = 0; i < 120; i++)
        {
            player += new Vector3(0.06f, 0, 0);
            enhanced.Step(camera, world, visible, player, Vector2.UnitX, interior: true, Vector2.Zero);
            Assert.True(camera.Position.X > -0.6f + 0.25f, $"the camera at {camera.Position} is past the wall");
            if (enhanced.Pivot.Y > player.Y + 1.05f + 0.5f)
                lifted++;
        }
        Assert.Equal(180, MathF.Abs(enhanced.Yaw), 10f);
        Assert.True(lifted < 10, $"lifted over his head for {lifted} ticks of the swing");
    }

    [Fact]
    public void InAClosetItRisesOverHisHead()
    {
        // Walls 0.6 from him on every side, with a drawn ceiling 4.8 up: no angle has room.
        var world = Worlds.Flat();
        var across = Worlds.Wall(-0.6f, 10).Concat(Worlds.Wall(0.6f, 10)).ToList();
        world.Add(Worlds.Chunk(1), across.Concat(across.Select(t => t with { A = Swap(t.A), B = Swap(t.B), C = Swap(t.C) })), Vector3.Zero);
        var visible = new VisibleWorld();
        visible.Add(Worlds.Chunk(1), Worlds.Floor(4.8f, -50, 60, -50, 60).Drawn(), Vector3.Zero);
        var (enhanced, camera) = Arrived(world, Vector2.UnitX, visible, interior: true);
        Assert.True(enhanced.Arrival.Kind == TrekArrivalKind.Lifted, $"{enhanced.Arrival}");
        for (var i = 0; i < 60; i++)
            enhanced.Step(camera, world, visible, Player, Vector2.UnitX, interior: true, Vector2.Zero);
        var head = Player + new Vector3(0, 1.05f, 0);
        Assert.True(camera.Position.Y - head.Y > 1, $"the camera at {camera.Position} isn't over his head");
        Assert.True(camera.Position.Y < 4.8f - 0.25f);

        static Vector3 Swap(Vector3 v) => new(v.Z, v.Y, v.X);
    }

    [Fact]
    public void HisModelFadesAsTheCameraComesClose()
    {
        var enhanced = new EnhancedTrekCamera();
        var head = Player + new Vector3(0, 1.05f, 0);
        Assert.Equal(1, enhanced.Fade(head + new Vector3(-2, 0, 0), Player));
        Assert.Equal(0, enhanced.Fade(head + new Vector3(-0.3f, 0, 0), Player));
        Assert.InRange(enhanced.Fade(head + new Vector3(-0.85f, 0, 0), Player), 0.4f, 0.6f);
    }
}
