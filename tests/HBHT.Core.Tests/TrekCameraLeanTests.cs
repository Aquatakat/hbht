using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Physics;
using HBHT.Plugins.Assets;

namespace HBHT.Core.Tests;

/// <summary>
/// The enhanced trek camera's slices 2 and 3 on small worlds: the path's bend (<see cref="PathBendLean"/>), the slope's pitch,
/// the nudges (<see cref="NudgeLean"/>) and sneaking's framing (<see cref="SneakFraming"/>).
/// </summary>
public class TrekCameraLeanTests
{
    private static readonly Vector3 Player = new(0, 0.5f, 0);
    private static readonly TrekCameraTuning Tuning = TrekCameraTuning.Default;

    /// <summary>A wall across Z = <paramref name="z"/>, from X <paramref name="minX"/> to <paramref name="maxX"/>.</summary>
    private static IEnumerable<CollisionTriangle> WallZ(float z, float minX, float maxX, float height = 3)
    {
        yield return Worlds.Triangle(new(minX, 0, z), new(minX, height, z), new(maxX, 0, z));
        yield return Worlds.Triangle(new(maxX, 0, z), new(minX, height, z), new(maxX, height, z));
    }

    private static CollisionWorld With(params IEnumerable<CollisionTriangle>[] walls)
    {
        var world = Worlds.Flat();
        for (var i = 0; i < walls.Length; i++)
            world.Add(Worlds.Chunk(i + 1), walls[i], Vector3.Zero);
        return world;
    }

    [Fact]
    public void OnOpenGroundAndAlongAStraightCorridorTheBendLeansNot()
    {
        Assert.Equal(0, PathBendLean.Probe(Worlds.Flat(), Player, Vector2.UnitX, Tuning), 3);
        var corridor = With(WallZ(2, -20, 40), WallZ(-2, -20, 40));
        Assert.Equal(0, PathBendLean.Probe(corridor, Player, Vector2.UnitX, Tuning), 1);
    }

    [Theory]
    // A wall ahead across X = 5, open beyond it on the +Z side: the lean is toward +Z, and mirrored, toward −Z.
    [InlineData(1)]
    [InlineData(-1)]
    public void AtABendItLeansTowardTheOpenSide(int side)
    {
        var world = With(Worlds.Wall(5, 3, side > 0 ? -20 : -3, side > 0 ? 3 : 20), WallZ(-2 * side, -20, 5));
        var lean = PathBendLean.Probe(world, Player, Vector2.UnitX, Tuning);
        Assert.True(lean * side > 10, $"leaned {lean}");
        Assert.True(MathF.Abs(lean) <= Tuning.BendBound);
    }

    [Fact]
    public void TheBendIsEasedAndWaitsWhileThePlayerTurnsTheCamera()
    {
        var world = With(Worlds.Wall(8, 3, -20, 3), WallZ(-2, -20, 8));
        var camera = new FollowCamera(Player, Vector2.UnitX);
        var enhanced = new EnhancedTrekCamera();
        var bend = new PathBendLean(enhanced, () => world);
        enhanced.Leans.Add(bend);
        enhanced.Arrive(camera, world, null, Player, Vector2.UnitX, interior: false, kept: false);
        var player = Player;
        var last = 0f;
        void Walk(int ticks, Vector2 turn = default)
        {
            for (var i = 0; i < ticks; i++)
            {
                enhanced.Step(camera, world, null, player, Vector2.UnitX, interior: false, turn);
                // It never changes faster than its cap.
                Assert.True(MathF.Abs(bend.Value - last) <= Tuning.BendRate / 60 + 1e-4f);
                last = bend.Value;
                player += new Vector3(0.02f, 0, 0);
            }
        }

        Walk(90);
        Assert.True(bend.Value > 5, $"leaned {bend.Value}");
        var yaw = enhanced.Yaw;
        Walk(10, new Vector2(1, 0));
        var turned = enhanced.Yaw;
        Assert.NotEqual(yaw, turned, 1f);
        // While it waits the yaw holds where the player left it.
        Walk(100);
        Assert.Equal(turned, enhanced.Yaw, 0.5f);
    }

    [Fact]
    public void TheNudgeFallsOffSmoothlyAndNeverTurnsRound()
    {
        var travel = Vector2.UnitX;
        float At(float x, float z) => NudgeLean.Toward(Player, travel, new Vector3(x, 0.5f, z), Tuning.NudgeReach, Tuning);
        // Ahead to the +Z side it leans toward it, bounded; mirrored, the other way.
        Assert.Equal(Tuning.NudgeBound, At(3, 2), 3);
        Assert.Equal(-Tuning.NudgeBound, At(3, -2), 3);
        // Beside him and behind, nothing.
        Assert.Equal(0, At(0, 3), 3);
        Assert.Equal(0, At(-3, 2), 3);
        // Nothing at its reach or past it, and nothing on top of him.
        Assert.Equal(0, At(Tuning.NudgeReach * 0.8f, Tuning.NudgeReach * 0.6f), 3);
        Assert.Equal(0, At(0.5f, 0.3f), 3);
        // From half its reach out it falls without a step.
        var last = At(Tuning.NudgeReach / 2 * MathF.Cos(0.3f), Tuning.NudgeReach / 2 * MathF.Sin(0.3f));
        for (var d = Tuning.NudgeReach / 2; d <= Tuning.NudgeReach; d += 0.05f)
        {
            var lean = At(d * MathF.Cos(0.3f), d * MathF.Sin(0.3f));
            Assert.True(lean <= last + 1e-4f && last - lean < 1, $"at {d}: {lean} after {last}");
            last = lean;
        }
        Assert.True(last < 0.05f);
    }

    [Fact]
    public void TheNudgeLetsGoWhileSneaking()
    {
        var camera = new EnhancedTrekCamera();
        var sneaking = false;
        var nudge = new NudgeLean(camera) { Hunter = _ => new Vector3(4, 0.5f, 2), Sneaking = () => sneaking };
        for (var i = 0; i < 120; i++)
            nudge.Lean(Player, Vector2.UnitX * 3, 0);
        Assert.True(nudge.Value > 1.5f);
        sneaking = true;
        for (var i = 0; i < 240; i++)
            nudge.Lean(Player, Vector2.UnitX * 3, 0);
        Assert.Equal(0, nudge.Value, 1);
    }

    [Fact]
    public void SneakingSwingsTheCameraToHisFarSideFromTheHunter()
    {
        var world = Worlds.Flat();
        var camera = new FollowCamera(Player, Vector2.UnitX);
        var enhanced = new EnhancedTrekCamera();
        var hunter = new Vector3(0, 0.5f, 10);
        enhanced.Sneak.Hunters = _ => [(hunter, 10)];
        enhanced.Sneak.Sneaking = () => true;
        enhanced.Arrive(camera, world, null, Player, Vector2.UnitX, interior: false, kept: false);
        // The camera starts behind him at −X; the hunter is at +Z, so its place is at −Z.
        var arm = 0f;
        var lastPosition = camera.Position;
        for (var i = 0; i < 90; i++)
        {
            enhanced.Step(camera, world, null, Player, Vector2.UnitX, interior: false, Vector2.Zero);
            // A swing, never a cut.
            Assert.True(Vector3.Distance(camera.Position, lastPosition) < 0.5f);
            lastPosition = camera.Position;
            arm = enhanced.Arm;
        }
        Assert.Equal(-90, enhanced.Yaw, 3f);
        Assert.True(camera.Position.Z < -4);
        Assert.True(arm > Tuning.OutdoorLength + 0.5f);
        Assert.True(enhanced.Pitch > Tuning.OutdoorPitch + 3);
    }

    [Fact]
    public void ThePlayersTurnWinsOverSneaking()
    {
        var world = Worlds.Flat();
        var camera = new FollowCamera(Player, Vector2.UnitX);
        var enhanced = new EnhancedTrekCamera();
        enhanced.Sneak.Hunters = _ => [(new Vector3(0, 0.5f, 10), 10)];
        enhanced.Sneak.Sneaking = () => true;
        enhanced.Arrive(camera, world, null, Player, Vector2.UnitX, interior: false, kept: false);
        for (var i = 0; i < 10; i++)
            enhanced.Step(camera, world, null, Player, Vector2.UnitX, interior: false, new Vector2(-1, 0));
        var turned = enhanced.Yaw;
        for (var i = 0; i < 100; i++)
            enhanced.Step(camera, world, null, Player, Vector2.UnitX, interior: false, Vector2.Zero);
        Assert.Equal(turned, enhanced.Yaw, 0.5f);
    }

    [Fact]
    public void SneakingChangesHunterOnlyForOneClearlyNearer()
    {
        var framing = new SneakFraming { Sneaking = () => true };
        var a = new Vector3(10, 0.5f, 0);
        var b = new Vector3(0, 0.5f, 9.5f);
        framing.Hunters = _ => [(a, 10), (b, 10)];
        framing.Step(Player, Tuning);
        // b is nearer from the first.
        Assert.Equal(b, framing.Hunter);
        // a coming a little nearer than b doesn't take it.
        a = new Vector3(9, 0.5f, 0);
        framing.Step(Player, Tuning);
        Assert.Equal(b, framing.Hunter);
        // Clearly nearer, it does.
        a = new Vector3(7, 0.5f, 0);
        framing.Step(Player, Tuning);
        Assert.Equal(a, framing.Hunter);
        for (var i = 0; i < 60; i++)
            framing.Step(Player, Tuning);
        Assert.Equal(1, framing.Weight, 3);
        // Out of twice its perception, it lets go, smoothly.
        framing.Hunters = _ => [(new Vector3(30, 0.5f, 0), 10)];
        for (var i = 0; i < 20; i++)
            framing.Step(Player, Tuning);
        var weight = framing.Weight;
        Assert.False(framing.Holding);
        Assert.InRange(weight, 0.01f, 0.99f);
        for (var i = 0; i < 60; i++)
            framing.Step(Player, Tuning);
        Assert.Equal(0, framing.Weight, 3);
    }

    [Fact]
    public void ThePitchFollowsTheSlopeAheadByPartOfIt()
    {
        // A ramp rising 1 in 4 (14°) along +X.
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(1), [
            Worlds.Triangle(new(-20, -5, -20), new(-20, -5, 20), new(40, 10, -20)),
            Worlds.Triangle(new(40, 10, -20), new(-20, -5, 20), new(40, 10, 20)),
        ], Vector3.Zero);
        Assert.Equal(14, EnhancedTrekCamera.GroundSlope(world, Player, Vector2.UnitX, 2.5f)!.Value, 0.5f);
        var camera = new FollowCamera(Player, Vector2.UnitX);
        var enhanced = new EnhancedTrekCamera();
        var player = new Vector3(0, 0.5f, 0);
        enhanced.Arrive(camera, world, null, player, Vector2.UnitX, interior: false, kept: false);
        for (var i = 0; i < 120; i++)
        {
            player += new Vector3(0.05f, 0.0125f, 0);
            enhanced.Step(camera, world, null, player, Vector2.UnitX, interior: false, Vector2.Zero);
        }
        // Uphill it looks up a little: the pitch's goal lowered, by half the slope but no more than the bound.
        Assert.Equal(-Tuning.SlopeBound, enhanced.SlopePitch, 0.5f);
        // Downhill, down.
        for (var i = 0; i < 240; i++)
        {
            player -= new Vector3(0.05f, 0.0125f, 0);
            enhanced.Step(camera, world, null, player, -Vector2.UnitX, interior: false, Vector2.Zero);
        }
        Assert.Equal(Tuning.SlopeBound, enhanced.SlopePitch, 0.5f);
    }
}
