using System.Numerics;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Plugins.Assets;

namespace HBHT.Core.Tests;

/// <summary>Modern movement (docs/systems/movement.md#modern-movement) on small worlds.</summary>
public class ModernMovementTests
{
    /// <summary>Plain collision, a barrier: "player and camera".</summary>
    private const int Plain = 1;

    /// <summary>Sand, which keeps no velocity: ground.</summary>
    private const int Sand = 5;

    /// <summary>A run's root motion a tick, about the original's 3.6 units a second.</summary>
    private static readonly SteadyMotion Run = new(0.06f);

    private static PlayerMotion Player(Vector3 at, Vector2 heading, float pace = 1) =>
        new(at, heading) { Movement = MovementMode.Modern, Pace = pace };

    /// <summary>Holds the stick toward <paramref name="way"/> for <paramref name="ticks"/>, calling <paramref name="each"/> after each tick.</summary>
    private static void Hold(CollisionWorld world, PlayerMotion player, Vector2 way, int ticks, Action? each = null)
    {
        for (var i = 0; i < ticks; i++)
        {
            player.Step(world, new MoveInput(new Vector2(0, 1)), way, Run);
            each?.Invoke();
        }
    }

    /// <summary>A slope rising toward +X from <paramref name="x0"/>, its normal's Y <paramref name="up"/>, from Z −20 to 20.</summary>
    private static IEnumerable<CollisionTriangle> Slope(float x0, float length, float up, int surface, float minZ = -20, float maxZ = 20)
    {
        var height = length * MathF.Tan(MathF.Acos(up));
        var x1 = x0 + length;
        yield return Worlds.Triangle(new(x0, 0, minZ), new(x0, 0, maxZ), new(x1, height, minZ), surface);
        yield return Worlds.Triangle(new(x1, height, minZ), new(x0, 0, maxZ), new(x1, height, maxZ), surface);
    }

    private static CollisionWorld SlopeWorld(float up, int surface)
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 2, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Slope(2, 8, up, surface), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 2), Worlds.Floor(8 * MathF.Tan(MathF.Acos(up)), 10, 30, -20, 20), Vector3.Zero);
        return world;
    }

    [Fact]
    public void SlidesAlongAPlainWall()
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 20, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Wall(3, 5).Select(t => t with { Surface = Plain }), Vector3.Zero);
        var way = Vector2.Normalize(new Vector2(1, 1));
        var player = Player(new Vector3(0, 0.5f, 0), way);
        Hold(world, player, way, 120, () => Assert.InRange(player.Sphere.Position.X, -1, 3 - PlayerMotion.Radius + 0.01f));
        // Along the wall at about the run's share across it, never up it.
        Assert.True(player.Sphere.Position.Z > 3.5f, $"{player.Sphere.Position}");
        Assert.Equal(0.5f, player.Sphere.Position.Y, 0.02f);
        Assert.True(player.Grounded);
    }

    [Theory]
    [InlineData(0.55f, 1)]
    [InlineData(0.55f, SessionSettings.BriskPace)]
    [InlineData(0.48f, SessionSettings.BriskPace)]
    [InlineData(0.85f, 1)]
    public void NeverClimbsASteepPlainFace(float up, float pace)
    {
        var world = SlopeWorld(up, Plain);
        var player = Player(new Vector3(0, 0.5f, 0), Vector2.UnitX, pace);
        var top = 0f;
        Hold(world, player, Vector2.UnitX, 300, () => top = MathF.Max(top, player.Feet.Y));
        Assert.True(top < 0.05f, $"climbed {top}");
        Assert.False(player.FellOut);
    }

    [Theory]
    [InlineData(0.48f)]
    [InlineData(0.71f)]
    [InlineData(0.95f)]
    public void ClimbsASteepRampOfGround(float up)
    {
        var world = SlopeWorld(up, Sand);
        var player = Player(new Vector3(0, 0.5f, 0), Vector2.UnitX);
        Hold(world, player, Vector2.UnitX, 180, () => Assert.True(player.Grounded, $"off the ground at {player.Sphere.Position}"));
        var height = 8 * MathF.Tan(MathF.Acos(up));
        Assert.True(player.Feet.Y > height - 0.05f, $"got to {player.Feet}, the top {height}");
    }

    [Fact]
    public void KeepsItsSpeedAcrossTheGroundOnASlope()
    {
        var flat = Worlds.Flat();
        var player = Player(new Vector3(0, 0.5f, 0), Vector2.UnitX);
        Hold(flat, player, Vector2.UnitX, 60);
        var onFlat = player.Sphere.Position.X;

        // Up a slope of 45° from where he starts: the same distance across the ground in the same time.
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Slope(-1, 20, MathF.Sqrt(0.5f), Sand), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Floor(0, -20, -1, -20, 20), Vector3.Zero);
        var climber = Player(new Vector3(0, 1 + 0.5f * MathF.Sqrt(2) + 0.01f, 0), Vector2.UnitX);
        climber.Step(world, new MoveInput(Vector2.Zero), Vector2.UnitX, Run);
        Assert.True(climber.Grounded);
        var start = climber.Sphere.Position;
        Hold(world, climber, Vector2.UnitX, 60);
        Assert.Equal(onFlat, climber.Sphere.Position.X - start.X, 0.02f);
        Assert.Equal(climber.Sphere.Position.X - start.X, climber.Sphere.Position.Y - start.Y, 0.02f);
    }

    [Theory]
    [InlineData(0.4f, true)]
    [InlineData(0.3f, true)]
    [InlineData(0.6f, false)]
    public void StepsUpALedgeLowerThanItsRadius(float height, bool onto)
    {
        var world = Worlds.Ledge(2, height);
        var player = Player(new Vector3(0, 0.5f, 0), Vector2.UnitX);
        Hold(world, player, Vector2.UnitX, 120);
        if (onto)
        {
            Assert.True(player.Sphere.Position.X > 4, $"{player.Sphere.Position}");
            Assert.Equal(height, player.Feet.Y, 0.02f);
        }
        else
        {
            Assert.InRange(player.Sphere.Position.X, 1, 2 - PlayerMotion.Radius + 0.01f);
            Assert.Equal(0, player.Feet.Y, 0.02f);
        }
    }

    [Fact]
    public void KeepsToTheGroundDownStairs()
    {
        // Ten stairs down toward +X, each 0.3 high and 0.5 deep.
        var world = new CollisionWorld();
        for (var i = 0; i <= 10; i++)
        {
            var y = -0.3f * i;
            var x = i * 0.5f;
            world.Add(Worlds.Chunk(Worlds.OwnParts + 2 * i), Worlds.Floor(y, i == 0 ? -10 : x, i == 10 ? 20 : x + 0.5f, -5, 5), Vector3.Zero);
            if (i > 0)
                world.Add(Worlds.Chunk(Worlds.OwnParts + 2 * i + 1),
                    Worlds.Wall(x, 0.3f).Select(t => t with { A = t.A with { Y = t.A.Y + y }, B = t.B with { Y = t.B.Y + y }, C = t.C with { Y = t.C.Y + y } }),
                    Vector3.Zero);
        }
        var player = Player(new Vector3(-1, 0.5f, 0), Vector2.UnitX);
        player.Step(world, new MoveInput(Vector2.Zero), Vector2.UnitX, Run);
        Hold(world, player, Vector2.UnitX, 150, () => Assert.True(player.Grounded, $"off the ground at {player.Sphere.Position}"));
        Assert.True(player.Sphere.Position.X > 6, $"{player.Sphere.Position}");
        Assert.Equal(-3, player.Feet.Y, 0.02f);
    }

    [Fact]
    public void NeverRidesUpAPlainFaceBesideARampOfGround()
    {
        // A ramp of ground rising toward +X, and along it, from the line Z = 0 on it, a steep plain face rising toward +Z (normal
        // Y 0.5), as at Overworld Q25. Running up the ramp and into the face, he keeps to the ramp.
        var world = new CollisionWorld();
        var rampUp = MathF.Tan(MathF.Acos(0.7f));
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 2, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Slope(2, 10, 0.7f, Sand), Vector3.Zero);
        var along = new Vector3(10, 10 * rampUp, 0);
        var up = new Vector3(0, 14, 10);
        var foot = new Vector3(2, 0, 0);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 2),
            [Worlds.Triangle(foot, foot + up, foot + along, Plain), Worlds.Triangle(foot + along, foot + up, foot + along + up, Plain)], Vector3.Zero);
        var way = Vector2.Normalize(new Vector2(1, 1));
        var player = Player(new Vector3(0, 0.5f, -3), way);
        Hold(world, player, way, 240, () => Assert.True(player.Feet.Y < MathF.Max(0, player.Sphere.Position.X + PlayerMotion.Radius - 2) * rampUp + 0.05f,
            $"rode up the face to {player.Feet}"));
        Assert.True(player.Feet.Y > 3, $"{player.Feet}");
    }

    [Fact]
    public void FallsOffALedgeAndLands()
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(2, -20, 0, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Floor(0, 0, 20, -20, 20), Vector3.Zero);
        var player = Player(new Vector3(-1, 2.5f, 0), Vector2.UnitX);
        var left = false;
        Hold(world, player, Vector2.UnitX, 120, () => left |= !player.Grounded);
        Assert.True(left);
        Assert.True(player.Grounded);
        Assert.Equal(0, player.Feet.Y, 0.02f);
        Assert.False(player.FellOut);
    }

    [Fact]
    public void SlidesDownASteepPlainFaceItStandsOn()
    {
        var world = SlopeWorld(0.55f, Plain);
        var player = Player(new Vector3(5, 5, 0), Vector2.UnitX);
        for (var i = 0; i < 240; i++)
            player.Step(world, new MoveInput(Vector2.Zero), Vector2.UnitX, Run);
        Assert.True(player.Grounded);
        Assert.True(player.Sphere.Position.X < 2, $"{player.Sphere.Position}");
    }

    [Fact]
    public void StandsStillOnASlope()
    {
        var world = SlopeWorld(0.6f, Sand);
        var player = Player(new Vector3(5, 5, 0), Vector2.UnitX);
        for (var i = 0; i < 60; i++)
            player.Step(world, new MoveInput(Vector2.Zero), Vector2.UnitX, Run);
        var rest = player.Sphere.Position;
        for (var i = 0; i < 120; i++)
            player.Step(world, new MoveInput(Vector2.Zero), Vector2.UnitX, Run);
        Assert.True(player.Grounded);
        Assert.True(Vector3.Distance(rest, player.Sphere.Position) < 1e-3f, $"crept from {rest} to {player.Sphere.Position}");
    }

    /// <summary>
    /// A body put with its sphere's centre a little under a floor, as the set-down puts I13's actors in 0x0527, comes out on top of
    /// it as 1.0's collision lifts it (<c>MovingPlane</c>); one put further under than its radius stays, the floor one-sided.
    /// </summary>
    [Theory]
    [InlineData(1f, 1.7f)]
    [InlineData(2f, 0.7f)]
    public void ComesOutOnTopOfAFloorItIsPutALittleUnder(float floor, float rests)
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 20, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Floor(floor, -5, 5, -5, 5, Sand), Vector3.Zero);
        var body = new Body(new Vector3(0, 0.7f, 0), 0.7f) { Movement = MovementMode.Modern };
        for (var i = 0; i < 30; i++)
            body.Step(world, Run);
        Assert.Equal(rests, body.Sphere.Position.Y, 0.02f);
    }

    /// <summary>
    /// Running between two posts, square ones standing corner on, whose corners are nearer than his sphere is wide: he slips
    /// through where they're wider apart than <see cref="ModernRules.WallEdge"/> of it, as 1.0's sphere slips past walls' edges,
    /// and is held where they're narrower.
    /// </summary>
    [Theory]
    [InlineData(0.85f, true)]
    [InlineData(0.65f, false)]
    public void SlipsBetweenPostsNarrowerThanItsSphere(float gap, bool through)
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 20, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Post(new Vector2(3, gap / 2 + 0.3f), 0.3f), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 2), Post(new Vector2(3, -gap / 2 - 0.3f), 0.3f), Vector3.Zero);
        var player = Player(new Vector3(0, 0.5f, 0), Vector2.UnitX);
        Hold(world, player, Vector2.UnitX, 180);
        Assert.Equal(through, player.Sphere.Position.X > 5);
        Assert.Equal(0.5f, player.Sphere.Position.Y, 0.02f);
    }

    /// <summary>A square post of plain collision 3 high, standing corner on, its corners <paramref name="size"/> from its middle.</summary>
    private static IEnumerable<CollisionTriangle> Post(Vector2 middle, float size)
    {
        Vector2[] corners = [middle + new Vector2(size, 0), middle + new Vector2(0, size), middle - new Vector2(size, 0), middle - new Vector2(0, size)];
        for (var i = 0; i < 4; i++)
        {
            var (p, q) = (corners[i], corners[(i + 1) % 4]);
            var (a, b) = (new Vector3(p.X, 0, p.Y), new Vector3(q.X, 0, q.Y));
            var up = new Vector3(0, 3, 0);
            // Facing out of the post.
            var outward = new Vector3((p + q).X / 2 - middle.X, 0, (p + q).Y / 2 - middle.Y);
            var face = Vector3.Cross(up, b - a);
            if (Vector3.Dot(face, outward) < 0)
                (a, b) = (b, a);
            yield return Worlds.Triangle(a, a + up, b, Plain);
            yield return Worlds.Triangle(b, a + up, b + up, Plain);
        }
    }

    /// <summary>
    /// A body steered to a point just behind a wall, pressed against the wall in front of it, is as near it as 1.0's, which
    /// steers by where a frame's root motion left it before the next collision pushed it out of the wall: it gets there, as
    /// 0x0365's runner does at H08, rather than giving up and popping into the wall.
    /// </summary>
    [Fact]
    public void ReachesAGoalJustBehindTheWallItPressesAgainst()
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 20, -20, 20), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Wall(3, 3).Select(t => t with { Surface = Plain }), Vector3.Zero);
        var body = new Body(new Vector3(0, 0.505f, 0), 0.5f) { Movement = MovementMode.Modern, Heading = Vector2.UnitX, GiveUpTicks = 300 };
        body.GoTo(new Vector2(3 - 0.5f + 1.03f, 0), 1, walk: false);
        for (var i = 0; i < 400 && body.Moving; i++)
            body.Step(world, Run);
        Assert.False(body.Moving);
        Assert.Equal(0, body.PutAtGoal);
        Assert.True(body.Sphere.Position.X < 2.5f, $"{body.Sphere.Position}");
    }

    /// <summary>
    /// A walk whose state, let go of, carries its root back, as entity 0x0226's walk start (state 4 of <c>aidyn:actor/0x2C17</c>)
    /// snaps it back 2.25 at its end: the rest of the step takes him back, where the stop's reach threw on it.
    /// </summary>
    [Fact]
    public void ARootGoingBackAsHeStopsTakesHimBack()
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -20, 20, -20, 20), Vector3.Zero);
        var body = new Body(new Vector3(0, 0.5f, 0), 0.5f) { Movement = MovementMode.Modern, Heading = Vector2.UnitX };
        var walk = new SnapBack();
        body.GoTo(new Vector2(3, 0), 1, walk: true);
        var furthest = 0f;
        for (var i = 0; i < 120; i++)
        {
            body.Step(world, walk);
            furthest = MathF.Max(furthest, body.Sphere.Position.X);
        }
        Assert.True(furthest > 2, $"{furthest}");
        // Played StopPace times as fast at the same speed, so its root moves him a StopPace'th as far.
        Assert.Equal(furthest - SnapBack.Back / body.Rules.StopPace, body.Sphere.Position.X, 0.01f);
    }

    /// <summary>A walk entered as it's asked for, moving 0.03 a tick, whose root goes back <see cref="Back"/> on its tenth tick after.</summary>
    private sealed class SnapBack : Actors.IActorMotion
    {
        public const float Back = 0.5f;

        private int _request, _after;

        public int StateLength => 60;

        public bool Entered { get; private set; }

        public float Tick(int request)
        {
            Entered = request != _request && request != Body.Idle;
            _request = request;
            if (request != Body.Idle)
                return 0.03f;
            return ++_after == 10 ? -Back : 0;
        }
    }
}
