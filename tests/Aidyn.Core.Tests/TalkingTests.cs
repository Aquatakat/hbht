using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using CharacterParty = Aidyn.Core.Characters.Party;

namespace Aidyn.Core.Tests;

/// <summary>The player turning where he stands to face someone, and the talking request a conversation asks of him.</summary>
public class PlayerFacingTests
{
    private static (PlayerMotion Player, FollowCamera Camera) Standing(Vector2 heading)
    {
        var player = new PlayerMotion(new Vector3(0, 0.5f, 0), heading);
        return (player, new FollowCamera(player.Sphere.Position, new Vector2(0, 1)));
    }

    private static void Step(PlayerMotion player, FollowCamera camera, Vector2 stick = default) =>
        player.Step(Worlds.Flat(), new PlayerInput(stick), camera, new SteadyMotion(0));

    private static float Degrees(Vector2 a, Vector2 b) => MathF.Acos(Math.Clamp(Vector2.Dot(Vector2.Normalize(a), Vector2.Normalize(b)), -1, 1)) * 180 / MathF.PI;

    [Fact]
    public void HeTurnsTenDegreesAFrameWhichIsFiveATickAndAsksToStand()
    {
        Assert.Equal(10 * MathF.PI / 180, Body.FaceTurnPerFrame, 1e-6f);
        Assert.Equal(5 * MathF.PI / 180, Cadence.PerTick(Body.FaceTurnPerFrame), 1e-6f);

        var (player, camera) = Standing(Vector2.UnitY);
        player.Face(Vector2.UnitX);
        player.Asked = 4;
        for (var tick = 1; tick <= 6; tick++)
        {
            Step(player, camera);
            Assert.Equal(PlayerMotion.Idle, player.Request);
            Assert.Equal(5 * tick, Degrees(player.Heading, Vector2.UnitY), 0.01f);
        }
        Assert.NotNull(player.TurnTo);
    }

    [Fact]
    public void HeTurnsTheShortWayRoundAndFacesWhatHeWasToldToFaceOnce()
    {
        var (player, camera) = Standing(Vector2.UnitY);
        player.Face(new Vector2(3, 0));
        var ticks = 0;
        for (; player.TurnTo is not null && ticks < 100; ticks++)
            Step(player, camera);
        // 90° at 5° a tick, and a tick to land on it.
        Assert.InRange(ticks, 17, 19);
        Assert.Null(player.TurnTo);
        Assert.Equal(1, player.Heading.X, 1e-5f);
        Assert.Equal(0, player.Heading.Y, 1e-5f);

        // Once he faces it he stays, and stands.
        Step(player, camera);
        Assert.Equal(1, player.Heading.X, 1e-5f);
        Assert.Equal(PlayerMotion.Idle, player.Request);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(271)]
    [InlineData(359)]
    public void HeFacesAnyHeadingWithinAHalfTurn(float degrees)
    {
        var (player, camera) = Standing(Vector2.UnitY);
        var to = new Vector2(MathF.Sin(degrees * MathF.PI / 180), MathF.Cos(degrees * MathF.PI / 180));
        player.Face(to * 7);
        var ticks = 0;
        for (; player.TurnTo is not null && ticks < 100; ticks++)
            Step(player, camera);
        Assert.Null(player.TurnTo);
        Assert.InRange(ticks, 0, 37);
        Assert.Equal(0, Degrees(player.Heading, to), 0.01f);
    }

    /// <summary>
    /// A turn ends within rounding of its goal: <c>PlayerMotion.Turn</c> normalizes what it arrives at again,
    /// which differs from the stored goal in its last bit for about a third of whole degrees.
    /// </summary>
    [Fact]
    public void HeFacesEveryWholeDegreeOfHeading()
    {
        var never = new List<int>();
        for (var degrees = 0; degrees < 360; degrees++)
        {
            var (player, camera) = Standing(Vector2.UnitY);
            player.Face(new Vector2(MathF.Sin(degrees * MathF.PI / 180), MathF.Cos(degrees * MathF.PI / 180)) * 3.7f);
            for (var tick = 0; tick < 100 && player.TurnTo is not null; tick++)
                Step(player, camera);
            if (player.TurnTo is not null)
                never.Add(degrees);
        }
        Assert.Empty(never);
    }

    [Fact]
    public void FacingNothingTurnsNowhere()
    {
        var (player, _) = Standing(Vector2.UnitY);
        player.Face(Vector2.Zero);
        Assert.Null(player.TurnTo);
        player.Face(Vector2.UnitX);
        player.Face(Vector2.Zero);
        Assert.Null(player.TurnTo);
    }

    [Fact]
    public void MovingTheStickCancelsTheTurn()
    {
        var (player, camera) = Standing(Vector2.UnitY);
        player.Face(Vector2.UnitX);
        Step(player, camera);
        Assert.NotNull(player.TurnTo);

        Step(player, camera, new Vector2(0, 1));
        Assert.Null(player.TurnTo);
        Assert.Equal(PlayerMotion.Run, player.Request);

        // And letting go of the stick doesn't bring it back.
        Step(player, camera);
        Assert.Null(player.TurnTo);
    }

    [Fact]
    public void AskedIsHisRequestForTheNextTickOnlyInPlaceOfStanding()
    {
        var (player, camera) = Standing(Vector2.UnitY);
        player.Asked = 4;
        Step(player, camera);
        Assert.Equal(4, player.Request);
        Assert.Null(player.Asked);

        Step(player, camera);
        Assert.Equal(PlayerMotion.Idle, player.Request);
    }

    [Fact]
    public void ATurnOverridesAskedAndSoDoesTheStick()
    {
        var (player, camera) = Standing(Vector2.UnitY);
        player.Face(Vector2.UnitX);
        player.Asked = 4;
        Step(player, camera);
        Assert.Equal(PlayerMotion.Idle, player.Request);
        Assert.Null(player.Asked);

        player.Asked = 4;
        Step(player, camera, new Vector2(0, 0.5f));
        Assert.Equal(PlayerMotion.Walk, player.Request);
    }

    [Fact]
    public void AskedWithNoTurnKeepsTheHeading()
    {
        var (player, camera) = Standing(new Vector2(1, 1));
        var heading = player.Heading;
        player.Asked = 4;
        Step(player, camera);
        Assert.Equal(heading, player.Heading);
    }
}

/// <summary>The camera framing two bodies talking, and giving the player back afterwards.</summary>
public class ConversationCameraTests
{
    private static readonly float HalfAngle = MathF.Tan((75 - 8) * 0.5f * MathF.PI / 180);

    /// <summary>Frames the two every tick from a camera that starts looking the other way.</summary>
    private static FollowCamera Framed(Vector3 a, Vector3 b, int ticks = 600)
    {
        var world = Worlds.Flat();
        var camera = new FollowCamera(a, new Vector2(0, -1));
        for (var i = 0; i < ticks; i++)
            camera.Frame(world, a, b);
        return camera;
    }

    private static Vector2 Ground(Vector3 from, Vector3 to) => new(to.X - from.X, to.Z - from.Z);

    [Fact]
    public void ACameraObjectHoldingTheCameraKeepsItWhereItIs()
    {
        var world = Worlds.Flat();
        var a = new Vector3(0, 0.5f, 0);
        var camera = new FollowCamera(a, new Vector2(0, -1));
        var (position, aim) = (camera.Position, camera.Aim);
        for (var i = 0; i < 60; i++)
        {
            camera.Hold();
            camera.Frame(world, a, a + new Vector3(1.6f, 0, 0));
        }
        Assert.Equal(position, camera.Position);
        Assert.Equal(aim, camera.Aim);
        Assert.False(camera.Held);

        camera.Frame(world, a, a + new Vector3(1.6f, 0, 0));
        Assert.NotEqual(aim, camera.Aim);
    }

    [Theory]
    [InlineData(1.6f, 0f)]
    [InlineData(20f, 0f)]
    [InlineData(0f, 1.6f)]
    [InlineData(0f, -20f)]
    [InlineData(-6f, 8f)]
    [InlineData(7.5f, -2f)]
    public void ItAimsAtTheirMidpointAndSettlesOnTheMinusXPlusZSideAtTheDistanceThatFitsThem(float dx, float dz)
    {
        var a = new Vector3(0, 0.5f, 0);
        var b = a + new Vector3(dx, 0, dz);
        var focus = (a + b) * 0.5f;
        var h = focus - a;
        var camera = Framed(a, b);

        Assert.Equal(focus.X, camera.Aim.X, 0.01f);
        Assert.Equal(focus.Y + camera.Height, camera.Aim.Y, 0.01f);
        Assert.Equal(focus.Z, camera.Aim.Z, 0.01f);

        var expectedAway = Vector2.Normalize(new Vector2(-MathF.Abs(h.Z), MathF.Abs(h.X)));
        var expectedDistance = MathF.Max((h.Length() + 4) / HalfAngle, 5);
        var offset = Ground(camera.Aim, camera.Position);
        Assert.Equal(expectedDistance, offset.Length(), 0.05f);
        Assert.Equal(expectedAway.X, offset.X / offset.Length(), 0.01f);
        Assert.Equal(expectedAway.Y, offset.Y / offset.Length(), 0.01f);
        // Always toward -X and +Z.
        Assert.True(offset.X <= 0.01f && offset.Y >= -0.01f);
        // Above the aim by the follow camera's pitch of its distance.
        Assert.Equal(MathF.Sin(camera.DefaultPitch * MathF.PI / 180) * expectedDistance, camera.Position.Y - camera.Aim.Y, 0.05f);
    }

    [Fact]
    public void TheTwoBodiesTwiceAndAHalfApartAreSevenAndAQuarterAwayAndTwentyApartTwentyOne()
    {
        var near = Framed(new Vector3(0, 0.5f, 0), new Vector3(1.6f, 0.5f, 0));
        Assert.Equal(7.25f, Ground(near.Aim, near.Position).Length(), 0.05f);

        var far = Framed(new Vector3(0, 0.5f, 0), new Vector3(20, 0.5f, 0));
        Assert.Equal(14 / HalfAngle, Ground(far.Aim, far.Position).Length(), 0.05f);
    }

    [Fact]
    public void ClosePairsGetTheFiveUnitMinimum()
    {
        // Even two on one spot are (0 + 4) / tan 33.5°, about 6, away, so 5 is only the formula's floor.
        var camera = Framed(new Vector3(0, 0.5f, 0), new Vector3(0.01f, 0.5f, 0));
        Assert.True(Ground(camera.Aim, camera.Position).Length() >= 5);
    }

    [Fact]
    public void TheOrderOfTheTwoMakesNoDifference()
    {
        var a = new Vector3(2, 0.5f, 3);
        var b = new Vector3(-4, 0.5f, 9);
        var one = Framed(a, b);
        var other = Framed(b, a);
        Assert.Equal(one.Position.X, other.Position.X, 0.02f);
        Assert.Equal(one.Position.Y, other.Position.Y, 0.02f);
        Assert.Equal(one.Position.Z, other.Position.Z, 0.02f);
        Assert.Equal(one.Aim.X, other.Aim.X, 0.01f);
        Assert.Equal(one.Aim.Z, other.Aim.Z, 0.01f);
    }

    [Fact]
    public void TheDistanceIsTheFollowCamerasAgainAfterTheCall()
    {
        var camera = new FollowCamera(Vector3.Zero, new Vector2(0, 1)) { Distance = 7 };
        camera.Frame(Worlds.Flat(), new Vector3(0, 0.5f, 0), new Vector3(10, 0.5f, 0));
        Assert.Equal(camera.DefaultDistance, camera.Distance);
    }

    [Fact]
    public void FramingTwoOnTheSameSpotDoesntBlowUp()
    {
        var camera = Framed(new Vector3(1, 0.5f, 1), new Vector3(1, 0.5f, 1), 100);
        Assert.True(float.IsFinite(camera.Position.X) && float.IsFinite(camera.Position.Z));
        Assert.True(Ground(camera.Aim, camera.Position).Length() >= 3);
    }

    [Fact]
    public void ReleaseGivesTheCameraBackWithTheDefaultPitchAndAQuickSwing()
    {
        var camera = new FollowCamera(Vector3.Zero, new Vector2(0, 1)) { Pitch = 40, Snap = 0 };
        camera.Release(new Vector3(1, 0.5f, 1));
        Assert.Equal(5, camera.Snap);
        Assert.Equal(camera.DefaultPitch, camera.Pitch);
    }

    [Fact]
    public void ReleaseInAnInteriorZeroesThePitchAsFollowingDoesThere()
    {
        var camera = new FollowCamera(Vector3.Zero, new Vector2(0, 1)) { Pitch = 40, Mode = CameraMode.Interior };
        camera.Release(Vector3.Zero);
        Assert.Equal(0, camera.Pitch);
        Assert.Equal(5, camera.Snap);
    }
}

/// <summary>Whether A reaches a wanderer's body (<c>0x80004480</c>).</summary>
public class WandererReachTests
{
    private static readonly RecordId MapId = RecordId.Hex(RecordTypes.Map, 0x1000);

    private static Wanderer Standing(float scale, PartyFlags flags = PartyFlags.None, bool talks = true)
    {
        var party = new MonsterPartyObject(0, Vector3.Zero, 20, PakVariant.Any, null, [], RecordId.Hex(RecordTypes.Entity, 0x300), null, 0, 0, null,
            flags, talks ? RecordId.Hex(RecordTypes.Dialogue, 1) : null, null);
        var map = LoadedMap.Activate(new MapRecord(MapId, [party]), "chunk", ItemData.Flags(), new MapObjectMemory(), new CharacterParty(), new Lcg());
        return new Wanderer(map, party, new EntityBody(RecordId.Hex(RecordTypes.Actor, 1), 0.5f, scale, 2), new Body(new Vector3(0, 0.5f, 0), 0.5f, scale), 10);
    }

    private static CollisionSphere Player(float y = 0.5f, float radius = 0.5f) =>
        new() { Radius = radius, Position = new Vector3(0, y, 0) };

    [Theory]
    [InlineData(1f, 1.5f, true)]
    [InlineData(1f, 1.4999f, true)]
    [InlineData(1f, 1.5001f, false)]
    [InlineData(2f, 2f, true)]
    [InlineData(2f, 2.0001f, false)]
    [InlineData(0.5f, 1.25f, true)]
    [InlineData(0.5f, 1.2501f, false)]
    public void ItsReachedWithinItsScaledRadiusPlusThePlayersPlusHalfAUnitInclusive(float scale, float distance, bool reached)
    {
        // The body's radius is 0.5, the player's 0.5, and the margin 0.5.
        Assert.Equal(reached, Wanderers.Reaches(Standing(scale), distance, Player()));
    }

    [Fact]
    public void ThePlayersRadiusCountsToo()
    {
        var wanderer = Standing(1);
        Assert.False(Wanderers.Reaches(wanderer, 2f, Player(radius: 0.5f)));
        Assert.True(Wanderers.Reaches(wanderer, 2f, Player(radius: 1f)));
    }

    [Theory]
    [InlineData(1.99f, true)]
    [InlineData(2f, false)]
    [InlineData(5f, false)]
    [InlineData(-1.99f, true)]
    [InlineData(-2f, false)]
    [InlineData(0f, true)]
    public void ItsReachedOnlyLessThanTwoApartInHeight(float above, bool reached)
    {
        var wanderer = Standing(1);
        Assert.Equal(reached, Wanderers.Reaches(wanderer, 1, Player(y: wanderer.Body.Sphere.Position.Y + above)));
    }

    [Fact]
    public void ASilentPartyIsntReachedHoweverNear()
    {
        var silent = Standing(1, PartyFlags.Silent);
        Assert.False(Wanderers.Reaches(silent, 0, Player()));
        Assert.False(Wanderers.Reaches(Standing(1, PartyFlags.Silent | PartyFlags.Walks), 0, Player()));
        Assert.True(Wanderers.Reaches(Standing(1, PartyFlags.Walks), 0, Player()));
    }
}
