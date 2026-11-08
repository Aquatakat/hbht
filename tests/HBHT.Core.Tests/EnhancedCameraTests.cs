using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Combat;
using HBHT.Core.Physics;

namespace HBHT.Core.Tests;

/// <summary>
/// Camera: Enhanced's parts, on hand-made worlds: whom the target cone picks and how it holds its pick, a shot that holds the character
/// and the target with neither hidden, aiming's shots, the acceleration-capped motion, and the tuning's names
/// (<see cref="EnhancedCamera"/>, <see cref="TargetCone"/>, <see cref="ShotFinder"/>, <see cref="AimFraming"/>, <see cref="CappedMotion"/>).
/// </summary>
public sealed class EnhancedCameraTests
{
    private static readonly CameraTuning T = CameraTuning.Default;

    // ---- The target cone

    // The character at the origin pointing +Z; two bodies ahead, a near one slightly off the middle and a far one nearer it.
    private static readonly TargetCandidate Near = new("near", new Vector3(1.2f, 0, 3), Opponent: true);
    private static readonly TargetCandidate Far = new("far", new Vector3(-1.5f, 0, 16), Opponent: true);

    [Fact]
    public void AMeleeWeaponPointsAtTheNearerBodyAndARangedOneOrAStaffAtTheFarther()
    {
        var melee = new TargetCone();
        melee.Pick(Vector3.Zero, Vector2.UnitY, far: false, [Near, Far], T);
        Assert.Equal("near", melee.Target);

        var ranged = new TargetCone();
        ranged.Pick(Vector3.Zero, Vector2.UnitY, far: true, [Near, Far], T);
        Assert.Equal("far", ranged.Target);
    }

    [Fact]
    public void ABodyOutsideTheConeIsntATargetAndEnemiesOutweighThePartyAtTheSamePlace()
    {
        var behind = new TargetCandidate("behind", new Vector3(0, 0, -5), Opponent: true);
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, far: false, [behind], T);
        Assert.Null(cone.Target);

        var friend = new TargetCandidate("friend", new Vector3(0.5f, 0, 5), Opponent: false);
        var enemy = new TargetCandidate("enemy", new Vector3(-0.5f, 0, 5), Opponent: true);
        cone.Pick(Vector3.Zero, Vector2.UnitY, far: false, [friend, enemy], T);
        Assert.Equal("enemy", cone.Target);
        // A party member alone in the cone is a target too.
        var alone = new TargetCone();
        alone.Pick(Vector3.Zero, Vector2.UnitY, far: false, [friend], T);
        Assert.Equal("friend", alone.Target);
    }

    [Fact]
    public void TheStickWobblingBetweenTwoBanditsDoesntFlickTheTarget()
    {
        // Two bandits 8 away, 20° either side of the middle; the stick wobbles ±25° about the middle every few ticks.
        var left = new TargetCandidate("left", 8 * new Vector3(MathF.Sin(-0.35f), 0, MathF.Cos(-0.35f)), true);
        var right = new TargetCandidate("right", 8 * new Vector3(MathF.Sin(0.35f), 0, MathF.Cos(0.35f)), true);
        var cone = new TargetCone();
        var changes = 0;
        for (var tick = 0; tick < 600; tick++)
        {
            var wobble = 0.44f * MathF.Sin(tick * 0.3f);
            var heading = new Vector2(MathF.Sin(wobble), MathF.Cos(wobble));
            if (cone.Pick(Vector3.Zero, heading, far: false, [left, right], T) && tick > 0)
                changes++;
        }
        Assert.Equal(0, changes);
        Assert.NotNull(cone.Target);
    }

    [Fact]
    public void AClearlyLikelierBodyTakesTheTargetsPlaceOnceItHasBeenSoForAWhile()
    {
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, far: false, [Near, Far], T);
        Assert.Equal("near", cone.Target);
        // The character turns to point straight at the far one: near is now 40° off, within the held cone but much less likely.
        var heading = Vector2.Normalize(new Vector2(-1.5f, 16));
        var near = Near with { At = new Vector3(2.6f, 0, 3) };
        for (var tick = 1; tick < T.TargetHold; tick++)
        {
            cone.Pick(Vector3.Zero, heading, far: false, [near, Far], T);
            Assert.Equal("near", cone.Target);
        }
        Assert.True(cone.Pick(Vector3.Zero, heading, far: false, [near, Far], T));
        Assert.Equal("far", cone.Target);
    }

    // ---- Shots that hide neither

    private static (ShotFinder Finder, List<ShotSubject> Subjects) TwoApart(CollisionWorld? world, params ShotSubject[] bodies)
    {
        var sight = new SightLines(world);
        sight.Bodies.AddRange(bodies);
        var me = new ShotSubject(new Vector3(0, 0.7f, 0), 0.7f);
        var target = new ShotSubject(new Vector3(12, 0.7f, 0), 0.7f);
        sight.Bodies.Add(me);
        sight.Bodies.Add(target);
        return (new ShotFinder(sight, T), [me, target]);
    }

    [Fact]
    public void AShotHoldingTheCharacterAndTheTargetHidesNeitherBehindAWall()
    {
        // The character at the origin, the target 12 along +X, and a wall 4 high at X = −3 facing −X; the camera looks now from −X
        // (yaw −90°), from behind the wall, which a low shot that way leaves across both.
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -40, 40, -40, 40), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Wall(-3, 4), Vector3.Zero);
        var (finder, subjects) = TwoApart(world);
        var from = -MathF.PI / 2;
        var straight = finder.Framed(subjects, from, ShotFinder.Rad(25));
        Assert.True(finder.HiddenWeight(straight, subjects) > 0, "the low shot from behind the wall should be hidden");

        var (shot, _) = finder.Best(subjects, from, behind: from)!.Value;
        Assert.Equal(0, finder.HiddenWeight(shot, subjects));
        Assert.All(subjects, s => Assert.True(ShotFraming.Holds(shot, s with { Radius = 0 }, T.Lens)));
    }

    [Fact]
    public void AShotHoldingTheCharacterAndTheTargetHidesNeitherBehindABody()
    {
        // A troll standing between them, 2.5 across; looking along the line from −X the troll fills the view of the target.
        var troll = new ShotSubject(new Vector3(6, 1.5f, 0), 2.5f);
        var (finder, subjects) = TwoApart(Worlds.Flat(), troll);
        var from = -MathF.PI / 2;
        var along = new Shot(new Vector3(6, 0.7f, 0), from, ShotFinder.Rad(10), 14);
        Assert.True(finder.HiddenWeight(along, subjects) > 0);

        var (shot, _) = finder.Best(subjects, from, behind: from)!.Value;
        Assert.Equal(0, finder.HiddenWeight(shot, subjects));
        Assert.All(subjects, s => Assert.True(ShotFraming.Holds(shot, s with { Radius = 0 }, T.Lens)));
    }

    [Fact]
    public void TheOriginalsShotHiddenBehindAWallIsTurnedOrRaisedUntilItIsnt()
    {
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -40, 40, -40, 40), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Wall(-3, 3), Vector3.Zero);
        var finder = new ShotFinder(new SightLines(world), T);
        var subject = new ShotSubject(new Vector3(0, 0.7f, 0), 0.7f);
        // The original's eye 8 away on −X, nearly level, behind the wall at X = −3.
        var original = Shot.Of(new Vector3(-8, 1.5f, 0), subject.At);
        Assert.True(finder.HiddenWeight(original, [subject]) > 0);
        var by = finder.Clearing(original, [subject]);
        Assert.NotNull(by);
        Assert.Equal(0, finder.HiddenWeight(finder.Corrected(original, by.Value, [subject]), [subject]));
    }

    // ---- Aiming

    private static List<ShotSubject> Reach(Vector3 caster, float reach, int n = 16)
    {
        var points = new List<ShotSubject> { new(caster, 0.7f) };
        for (var i = 0; i < n; i++)
        {
            var (s, c) = MathF.SinCos(MathF.Tau * i / n);
            points.Add(new ShotSubject(caster + reach * new Vector3(c, 0, s) with { Y = 0 }, 0.5f));
        }
        return points;
    }

    [Fact]
    public void AnAreaIsPlacedFromAboveWithItsWholeReachInView()
    {
        // Fireball at level 3, range 4: its centre goes 12 from the caster, its area 3 beyond.
        var reach = Reach(new Vector3(0, 0.7f, 0), 15);
        var framing = new AimFraming(reach, yaw: 0.4f, pitch: ShotFinder.Rad(T.TopDownPitch));
        var shot = framing.Step(new Vector3(0, 0, 0), 3, T);
        Assert.True(shot.Pitch >= ShotFinder.Rad(79), $"pitch {shot.Pitch}");
        Assert.All(reach, p => Assert.True(ShotFraming.Holds(shot, p, T.Lens), $"{p.At} out of view"));
        // The stick's up is the screen's up: the look across the ground is the yaw's, from above too.
        Assert.Equal(new Shot(default, 0.4f, 0, 1).LookXZ, shot.LookXZ);
    }

    [Fact]
    public void ARangedAimHoldsItsWholeReachAndTheReticleMovesFreelyWithinIt()
    {
        var reach = Reach(new Vector3(0, 0.7f, 0), 10);
        var framing = new AimFraming(reach, yaw: 0, pitch: ShotFinder.Rad(T.AimPitch));
        var first = framing.Step(new Vector3(0, 0, 0), 1, T);
        Assert.All(reach, p => Assert.True(ShotFraming.Holds(first, p, T.Lens), $"{p.At} out of view"));
        // Anywhere within the reach the reticle leaves the shot where it is.
        foreach (var p in reach)
            Assert.Equal(first, framing.Step(p.At, p.Radius, T));
    }

    [Fact]
    public void AReachTooWideForTheShotIsFollowedOnlyAsTheReticleNearsTheEdge()
    {
        var tight = T with { MaxAimDistance = 12 };
        var reach = Reach(new Vector3(0, 0.7f, 0), 60);
        var framing = new AimFraming(reach, yaw: 0, pitch: ShotFinder.Rad(T.AimPitch));
        var first = framing.Step(Vector3.Zero, 1, tight);
        Assert.Equal(12, first.Distance);
        // A step inside the view: no move.
        Assert.Equal(first, framing.Step(new Vector3(1, 0, 0), 1, tight));
        // Far to the right: the view follows, and the reticle ends inside its edge.
        var reticle = new Vector3(30, 0, 0);
        var followed = framing.Step(reticle, 1, tight);
        Assert.NotEqual(first.Aim, followed.Aim);
        var (x, _, _) = ShotFraming.OnScreen(followed, reticle, tight.Lens);
        Assert.True(MathF.Abs(x) <= 1, $"x {x}");
    }

    // ---- Motion

    [Fact]
    public void ALongMoveKeepsItsAccelerationAndDecelerationWithinTheCap()
    {
        var caps = T.Caps;
        var start = new Shot(new Vector3(0, 1, 0), 0, ShotFinder.Rad(30), 10);
        var goal = new Shot(new Vector3(60, 1, 40), 2.5f, ShotFinder.Rad(70), 45);
        var motion = new CappedMotion(start, caps);
        var aimSpeed = Vector3.Zero;
        var (yawSpeed, pitchSpeed, distanceSpeed) = (0f, 0f, 0f);
        var most = 0f;
        var ticks = 0;
        for (; ticks < 60 * 20 && (motion.Moving || ticks == 0); ticks++)
        {
            motion.Step(goal);
            var (y, p, d) = motion.Speeds;
            Assert.True((motion.AimSpeed - aimSpeed).Length() * 60 <= caps.Aim * 1.001f, $"aim at tick {ticks}");
            Assert.True(MathF.Abs(y - yawSpeed) * 60 <= caps.Turn * MathF.PI / 180 * 1.001f, $"yaw at tick {ticks}");
            Assert.True(MathF.Abs(p - pitchSpeed) * 60 <= caps.Turn * MathF.PI / 180 * 1.001f, $"pitch at tick {ticks}");
            Assert.True(MathF.Abs(d - distanceSpeed) * 60 <= caps.Zoom * 1.001f, $"distance at tick {ticks}");
            most = MathF.Max(most, motion.AimSpeed.Length());
            (aimSpeed, yawSpeed, pitchSpeed, distanceSpeed) = (motion.AimSpeed, y, p, d);
        }
        Assert.True(Vector3.Distance(motion.Current.Aim, goal.Aim) < 0.05f);
        Assert.True(MathF.Abs(Shot.Wrap(motion.Current.Yaw - goal.Yaw)) < 0.01f);
        // 72 units at 30 a second squared: about three seconds there, at about 45 a second at its fastest, so it isn't capped by speed.
        Assert.InRange(ticks, 150, 400);
        Assert.True(most > 30, $"at most {most}");
    }

    [Fact]
    public void AGoalThatWalksIsFollowedAtItsOwnSpeed()
    {
        var motion = new CappedMotion(new Shot(Vector3.Zero, 0, 0.5f, 8), T.Caps);
        var aim = Vector3.Zero;
        for (var tick = 0; tick < 600; tick++)
        {
            aim += new Vector3(0.05f, 0, 0);
            motion.Step(new Shot(aim, 0, 0.5f, 8));
        }
        Assert.True(Vector3.Distance(motion.Current.Aim, aim) < 0.2f, $"{motion.Current.Aim} behind {aim}");
        Assert.InRange(motion.AimSpeed.X, 2.9f, 3.1f);
    }

    [Fact]
    public void AGoalThatLungesAndStopsIsOvershotByLittleAndTheEyeNeverReachesTheAim()
    {
        // The original's camera chasing a far aim: its aim a unit a tick for 20 ticks, its distance shrinking as fast, then still.
        var t = T;
        var motion = new CappedMotion(new Shot(Vector3.Zero, 0, 0.5f, 22), t.Caps) { MostFollowed = (t.FollowSpeed, ShotFinder.Rad(t.FollowTurn)), NearestDistance = t.NearestDistance };
        var goal = new Shot(Vector3.Zero, 0, 0.5f, 22);
        var most = 0f;
        for (var tick = 0; tick < 600; tick++)
        {
            if (tick < 20)
                goal = goal with { Aim = goal.Aim + new Vector3(0.9f, 0, 0), Distance = goal.Distance - 0.9f };
            motion.Step(goal);
            most = MathF.Max(most, motion.Current.Aim.X - goal.Aim.X);
            Assert.True(motion.Current.Distance >= t.NearestDistance);
        }
        // Following at most 8 a second, it stops within 8² / (2 × 30) of the goal, and some for the settling.
        Assert.True(most < 1.5f, $"overshot by {most}");
        Assert.True(Vector3.Distance(motion.Current.Aim, goal.Aim) < 0.05f);
    }

    [Fact]
    public void AZoomInReversedHalfWayDoesntDiveAtTheGround()
    {
        // Zooming in from 55 toward a framing 7 away, the next turn asks for 22 and then a throw's 70: the battle's case. The zoom turns
        // round by proportion, so it gives up only a share of the distance it's at, never most of it.
        var motion = new CappedMotion(new Shot(Vector3.Zero, 0, 0.8f, 55), T.Caps);
        var nearest = float.MaxValue;
        var atSwitch = 0f;
        for (var tick = 0; tick < 400; tick++)
        {
            if (tick == 45)
                atSwitch = motion.Current.Distance;
            var goal = tick < 45 ? 7 : tick < 75 ? 22.5f : 70;
            motion.Step(new Shot(Vector3.Zero, 0, 0.8f, goal));
            if (tick >= 45)
                nearest = MathF.Min(nearest, motion.Current.Distance);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"{atSwitch:F1} as it turned, {nearest:F1} at the nearest");
        Assert.True(nearest > atSwitch * 0.5f, $"{atSwitch} then down to {nearest}");
        Assert.True(MathF.Abs(motion.Current.Distance - 70) < 0.5f);
    }

    // ---- Tuning

    [Fact]
    public void TheTuningIsSetByName()
    {
        var t = CameraTuning.Default.Parse("aimacceleration=20, ConeAngle=30,Pitches=30;60,TargetHold=5");
        Assert.Equal(20, t.AimAcceleration);
        Assert.Equal(30, t.ConeAngle);
        Assert.Equal([30, 60], t.Pitches);
        Assert.Equal(5, t.TargetHold);
        Assert.Equal("AimAcceleration=20,ConeAngle=30,TargetHold=5,Pitches=30;60", t.ToString());
        Assert.Throws<FormatException>(() => CameraTuning.Default.Parse("Nope=1"));
    }

    [Fact]
    public void AShotsLookAcrossTheGroundIsTheWayItsEyeLooks()
    {
        var shot = Shot.Of(new Vector3(3, 4, -2), new Vector3(0, 1, 1));
        Assert.Equal(Vector3.Distance(new Vector3(3, 4, -2), new Vector3(0, 1, 1)), shot.Distance, 4);
        Assert.True(Vector3.Distance(new Vector3(3, 4, -2), shot.Eye) < 1e-4f);
        var flat = Vector2.Normalize(new Vector2(-3, 3));
        Assert.True(Vector2.Distance(flat, shot.LookXZ) < 1e-5f);
        Assert.Equal(shot.Yaw, Shot.YawOf(shot.LookXZ), 4);
        // Its right is the screen's: the stick's right, as Body.Drive turns it by the look.
        var right = Facing.Rotate(new Vector2(-1, 0), shot.LookXZ.X, shot.LookXZ.Y);
        Assert.True(Vector2.Distance(right, new Vector2(shot.Right.X, shot.Right.Z)) < 1e-5f);
    }
}
