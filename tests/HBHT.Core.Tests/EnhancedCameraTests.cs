using System.Numerics;
using HBHT.Core.Cameras;
using HBHT.Core.Combat;
using HBHT.Core.Physics;

namespace HBHT.Core.Tests;

/// <summary>
/// Camera: Enhanced's parts, on hand-made worlds: whom the target cone picks and how it holds its pick, a shot that holds the character
/// and the target with neither hidden, aiming's shots, the acceleration-capped motion, and the tuning's names
/// (<see cref="EnhancedCamera"/>, <see cref="TargetCone"/>, <see cref="ShotFinder"/>, <see cref="CappedMotion"/>).
/// </summary>
public sealed class EnhancedCameraTests
{
    private static readonly CameraTuning T = CameraTuning.Default;

    // ---- The target cone

    // The character at the origin pointing +Z; two enemies ahead, a near one slightly off the middle and a far one nearer it.
    private static readonly TargetCandidate Near = new("near", new Vector3(1.2f, 0, 3));
    private static readonly TargetCandidate Far = new("far", new Vector3(-1.5f, 0, 16));

    [Fact]
    public void TheNearerEnemyAheadIsTheTarget()
    {
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, [Near, Far], T);
        Assert.Equal("near", cone.Target);
    }

    [Fact]
    public void AnEnemyOutsideTheConeIsntATarget()
    {
        var behind = new TargetCandidate("behind", new Vector3(0, 0, -5));
        var aside = new TargetCandidate("aside", new Vector3(5, 0, 3));
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, [behind, aside], T);
        Assert.Null(cone.Target);
    }

    [Fact]
    public void TheStickWobblingBetweenTwoBanditsDoesntFlickTheTarget()
    {
        // Two bandits 8 away, 20° either side of the middle; the stick wobbles ±25° about the middle every few ticks.
        var left = new TargetCandidate("left", 8 * new Vector3(MathF.Sin(-0.35f), 0, MathF.Cos(-0.35f)));
        var right = new TargetCandidate("right", 8 * new Vector3(MathF.Sin(0.35f), 0, MathF.Cos(0.35f)));
        var cone = new TargetCone();
        var changes = 0;
        for (var tick = 0; tick < 600; tick++)
        {
            var wobble = 0.44f * MathF.Sin(tick * 0.3f);
            var heading = new Vector2(MathF.Sin(wobble), MathF.Cos(wobble));
            if (cone.Pick(Vector3.Zero, heading, [left, right], T) && tick > 0)
                changes++;
        }
        Assert.Equal(0, changes);
        Assert.NotNull(cone.Target);
    }

    [Fact]
    public void AClearlyLikelierBodyTakesTheTargetsPlaceOnceItHasBeenSoForAWhile()
    {
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, [Near, Far], T);
        Assert.Equal("near", cone.Target);
        // The character turns to point straight at the far one: near is now 40° off, within the held cone but much less likely.
        var heading = Vector2.Normalize(new Vector2(-1.5f, 16));
        var near = Near with { At = new Vector3(2.6f, 0, 3) };
        for (var tick = 1; tick < T.TargetHold; tick++)
        {
            cone.Pick(Vector3.Zero, heading, [near, Far], T);
            Assert.Equal("near", cone.Target);
        }
        Assert.True(cone.Pick(Vector3.Zero, heading, [near, Far], T));
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

    [Fact]
    public void OnOpenGroundTheShotIsLowOverTheCharactersShoulder()
    {
        // Looking now from the other side (yaw +90°): the shot still goes behind the character, low, not over the field from above.
        var (finder, subjects) = TwoApart(Worlds.Flat());
        var behind = -MathF.PI / 2;
        var (shot, _) = finder.Best(subjects, from: MathF.PI / 2, behind)!.Value;
        Assert.True(shot.Pitch <= ShotFinder.Rad(20) + 1e-4f, $"pitch {shot.Pitch * 180 / MathF.PI:F0}°");
        Assert.True(MathF.Abs(Shot.Wrap(shot.Yaw - behind)) <= ShotFinder.Rad(30) + 1e-4f, $"yaw {shot.Yaw * 180 / MathF.PI:F0}°");
        Assert.Equal(0, finder.HiddenWeight(shot, subjects));
        // The eye is on the character's side, nearer it than the target.
        Assert.True(Vector3.Distance(shot.Eye, subjects[0].At) < Vector3.Distance(shot.Eye, subjects[1].At));
    }

    [Fact]
    public void ABodyWhoseUpperHalfIsSeenIsntHidden()
    {
        // A crate-high wall at X = −3, 1 high, in front of a body 1.4 tall standing behind it at the origin: from a low eye on −X its legs
        // are hidden and its middle and top aren't.
        var world = new CollisionWorld();
        world.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -40, 40, -40, 40), Vector3.Zero);
        world.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Wall(-3, 0.5f), Vector3.Zero);
        var sight = new SightLines(world);
        var body = new ShotSubject(new Vector3(0, 0.7f, 0), 0.7f);
        Assert.False(sight.Hidden(new Vector3(-10, 1.2f, 0), body));
        // Behind a wall up to its chest, it's hidden.
        var chest = new CollisionWorld();
        chest.Add(Worlds.Chunk(Worlds.OwnParts), Worlds.Floor(0, -40, 40, -40, 40), Vector3.Zero);
        chest.Add(Worlds.Chunk(Worlds.OwnParts + 1), Worlds.Wall(-3, 1.2f), Vector3.Zero);
        Assert.True(new SightLines(chest).Hidden(new Vector3(-10, 1.2f, 0), body));
    }

    [Fact]
    public void AnAIsTurnIsPlannedAsOneShotHoldingItAllTurnedLeastFromTheShotsEitherSide()
    {
        // An enemy at +X walking toward the party and its target at the origin; the shot now and the next player's look the same way (+X).
        var sight = new SightLines(Worlds.Flat());
        var enemy = new ShotSubject(new Vector3(14, 0.7f, 0), 0.7f);
        var to = new ShotSubject(new Vector3(4, 0.7f, 2), 0.7f);
        var target = new ShotSubject(new Vector3(0, 0.7f, 0), 0.7f);
        sight.Bodies.AddRange([enemy, target]);
        var finder = new ShotFinder(sight, T);
        var look = -MathF.PI / 2;
        var (shot, _) = finder.Planned([enemy, to, target], from: look, next: look)!.Value;
        Assert.All(new[] { enemy, to, target }, s => Assert.True(ShotFraming.Holds(shot, s with { Radius = 0 }, T.Lens), $"{s.At} out of view"));
        Assert.Equal(0, finder.HiddenWeight(shot, [enemy, target]));
        Assert.True(MathF.Abs(Shot.Wrap(shot.Yaw - look)) <= ShotFinder.Rad(45) + 1e-4f, $"yaw {shot.Yaw * 180 / MathF.PI:F0}°");
    }

    // ---- Aiming

    [Fact]
    public void AnAimKeepsTheReticleInTheMiddleOfTheScreen()
    {
        var finder = new ShotFinder(new SightLines(null), T);
        var reticle = new Vector3(30, 0, -12);
        var shot = finder.Aimed(reticle, 1, yaw: 0.4f, pitch: ShotFinder.Rad(T.AimPitch));
        var (x, y, _) = ShotFraming.OnScreen(shot, reticle, T.Lens);
        Assert.True(MathF.Abs(x) < 1e-3f && MathF.Abs(y) < 1e-3f, $"{x}, {y}");
        Assert.Equal(T.AimDistance, shot.Distance);
        // The stick's up is the screen's up: the look across the ground is the yaw's.
        Assert.Equal(new Shot(default, 0.4f, 0, 1).LookXZ, shot.LookXZ);
    }

    [Fact]
    public void AnAreaIsPlacedFromAboveHoldingItsArea()
    {
        // A wide area, 9 across: the shot stands back far enough to hold it with its margin round it.
        var finder = new ShotFinder(new SightLines(null), T);
        var centre = new Vector3(5, 0, 5);
        var radius = 9 * T.AreaMargin;
        var shot = finder.Aimed(centre, radius, yaw: 0, pitch: ShotFinder.Rad(T.TopDownPitch));
        Assert.True(shot.Pitch >= ShotFinder.Rad(79));
        Assert.True(ShotFraming.Holds(shot, new ShotSubject(centre, radius), T.Lens));
        Assert.True(shot.Distance > T.AimDistance);
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
        // 72 units at 12 a second squared: about five seconds there, at about 29 a second at its fastest, so it isn't capped by speed.
        Assert.InRange(ticks, 250, 500);
        Assert.True(most > 25, $"at most {most}");
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
        // Following at most 6 a second, it stops within 6² / (2 × 12) of the goal, and some for the settling.
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
