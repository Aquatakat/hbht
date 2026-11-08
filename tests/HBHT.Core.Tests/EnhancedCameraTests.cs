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
    private static readonly TargetCandidate Near = new("near", new Vector3(0.8f, 0, 3));
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
        // Two bandits 8 away, 15° either side of the middle; the stick wobbles ±20° about the middle every few ticks.
        var left = new TargetCandidate("left", 8 * new Vector3(MathF.Sin(-0.26f), 0, MathF.Cos(-0.26f)));
        var right = new TargetCandidate("right", 8 * new Vector3(MathF.Sin(0.26f), 0, MathF.Cos(0.26f)));
        var cone = new TargetCone();
        var changes = 0;
        for (var tick = 0; tick < 600; tick++)
        {
            var wobble = 0.35f * MathF.Sin(tick * 0.3f);
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
        // The cone alone: not held for being near (HoldNear).
        var t = T with { HoldNear = 0 };
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, [Near, Far], t);
        Assert.Equal("near", cone.Target);
        // The character turns to point straight at the far one: near is now 42° off, within the held cone but much less likely.
        var heading = Vector2.Normalize(new Vector2(-1.5f, 16));
        var near = Near with { At = new Vector3(2.2f, 0, 3) };
        for (var tick = 1; tick < t.TargetHold; tick++)
        {
            cone.Pick(Vector3.Zero, heading, [near, Far], t);
            Assert.Equal("near", cone.Target);
        }
        Assert.True(cone.Pick(Vector3.Zero, heading, [near, Far], t));
        Assert.Equal("far", cone.Target);
    }

    [Fact]
    public void ATargetThatLeavesTheConeIsHeldAWhileBeforeItsLetGo()
    {
        // The stick swung away from the only enemy ahead: the camera keeps it for a while rather than flicking to no one and back.
        var t = T with { HoldNear = 0 };
        var cone = new TargetCone();
        cone.Pick(Vector3.Zero, Vector2.UnitY, [Near], t);
        Assert.Equal("near", cone.Target);
        for (var tick = 1; tick < t.LoseHold; tick++)
        {
            cone.Pick(Vector3.Zero, Vector2.UnitX, [Near], t);
            Assert.Equal("near", cone.Target);
        }
        Assert.True(cone.Pick(Vector3.Zero, Vector2.UnitX, [Near], t));
        Assert.Null(cone.Target);
    }

    [Fact]
    public void ATargetHeldStaysItFurtherThanATargetIsTaken()
    {
        var cone = new TargetCone();
        var edge = new TargetCandidate("edge", new Vector3(0, 0, T.TargetRange - 1));
        cone.Pick(Vector3.Zero, Vector2.UnitY, [edge], T);
        Assert.Equal("edge", cone.Target);
        // Walking back from it, past the range: still held, without a tick of no one.
        for (var back = 0; back < 5; back++)
        {
            cone.Pick(new Vector3(0, 0, -back), Vector2.UnitY, [edge], T);
            Assert.Equal("edge", cone.Target);
        }
        // A new one that far isn't taken.
        var fresh = new TargetCone();
        fresh.Pick(new Vector3(0, 0, -4), Vector2.UnitY, [edge], T);
        Assert.Null(fresh.Target);
    }

    [Fact]
    public void TheTargetHeldStaysWhileTheCharacterGoesRoundItNearButNotWhileTheyWalkAway()
    {
        // Running round a pack for its back: the character heads across the way to the rat 5 off, and it stays the target.
        var cone = new TargetCone();
        var rat = new TargetCandidate("rat", new Vector3(0, 0, 5));
        cone.Pick(Vector3.Zero, Vector2.UnitY, [rat], T);
        Assert.Equal("rat", cone.Target);
        for (var tick = 0; tick < T.LoseHold * 3; tick++)
        {
            cone.Pick(Vector3.Zero, Vector2.UnitX, [rat], T);
            Assert.Equal("rat", cone.Target);
            Assert.False(cone.Losing);
        }
        // Heading away from it, near as it is, it's let go once LoseHold has passed, losing it till then.
        for (var tick = 1; tick < T.LoseHold; tick++)
        {
            cone.Pick(Vector3.Zero, -Vector2.UnitY, [rat], T);
            Assert.True(cone.Losing);
            Assert.Equal("rat", cone.Target);
        }
        Assert.True(cone.Pick(Vector3.Zero, -Vector2.UnitY, [rat], T));
        Assert.Null(cone.Target);
    }

    [Fact]
    public void TheFollowPointMovesEvenlyWhereTheBodyStridesAndSettlesWithoutPassingIt()
    {
        // A stride as the original moves a body: a step every other tick, its root stopping for three frames of every ten.
        var point = new FollowPoint();
        point.Reset(Vector3.Zero);
        var body = Vector3.Zero;
        var speeds = new List<float>();
        for (var tick = 0; tick < 600; tick++)
        {
            var frame = tick / 2;
            if (tick % 2 == 0 && frame % 10 >= 3)
                body += new Vector3(0.2f, 0, 0);
            point.Step(body, T.FollowSmoothing);
            if (tick >= 120)
                speeds.Add(point.Moved.X);
        }
        var mean = speeds.Average();
        var spread = speeds.Max() - speeds.Min();
        Assert.True(spread < mean * 0.5f, $"the point's step ran from {speeds.Min():F3} to {speeds.Max():F3}, the body's mean {mean:F3}");
        Assert.True(body.X - point.At.X < 2 * T.FollowSmoothing * mean * 60 * 1.3f, $"trailing by {body.X - point.At.X:F2}");
        // Stopped, it settles on the body and never goes past it.
        for (var tick = 0; tick < 120; tick++)
        {
            point.Step(body, T.FollowSmoothing);
            Assert.True(point.At.X <= body.X + 1e-4f);
        }
        Assert.True(body.X - point.At.X < 0.01f);
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
    public void TwoWhoDealWithEachOtherAreFramedTheWayRoundTheirSideSays()
    {
        // Straight over the shoulder the character hides the target, so the shot goes to one side; which, is the side the pair says.
        var (finder, subjects) = TwoApart(Worlds.Flat());
        var behind = -MathF.PI / 2;
        var (me, target) = (subjects[0].At, subjects[1].At);
        var (right, _) = finder.Best(subjects, behind, behind, [new ScreenSide(me, target)])!.Value;
        var (left, _) = finder.Best(subjects, behind, behind, [new ScreenSide(target, me)])!.Value;
        TestContext.Current.TestOutputHelper?.WriteLine($"right {ShotFinder.Across(right, new ScreenSide(me, target), T.Lens):F3} yaw {right.Yaw * 180 / MathF.PI:F0} pitch {right.Pitch * 180 / MathF.PI:F0}; left {ShotFinder.Across(left, new ScreenSide(me, target), T.Lens):F3} yaw {left.Yaw * 180 / MathF.PI:F0} pitch {left.Pitch * 180 / MathF.PI:F0}");
        Assert.True(ShotFinder.Across(right, new ScreenSide(me, target), T.Lens) > 0, "framed on the right");
        Assert.True(ShotFinder.Across(left, new ScreenSide(me, target), T.Lens) < 0, "framed on the left");
        Assert.Equal(0, finder.HiddenWeight(right, subjects));
        Assert.Equal(0, finder.HiddenWeight(left, subjects));
    }

    [Fact]
    public void NoOneIsFramedUnderTheConversationBox()
    {
        // Over the shoulder of the character at the target 12 along +X, aimed just ahead of the character: the bottom of each one's sphere
        // is above the lens's floor, the conversation box's top.
        var (finder, subjects) = TwoApart(Worlds.Flat());
        var behind = -MathF.PI / 2;
        var aim = subjects[0].At + new Vector3(T.AimLeadMost, 0, 0);
        var (shot, _) = finder.Best(subjects, behind, behind, aim: aim, most: T.ShoulderDistance)!.Value;
        var half = MathF.Tan(T.FieldOfView * MathF.PI / 360);
        foreach (var s in subjects)
        {
            var v = s.At - shot.Eye;
            var (depth, up) = (Vector3.Dot(v, shot.Forward), Vector3.Dot(v, shot.Up));
            Assert.True((up - s.Radius) / depth >= -T.FrameFloor * half * 1.001f, $"{s.At} framed {(up - s.Radius) / depth / half:F2} down");
        }
        // The lens judges it: a subject held is above the floor.
        Assert.All(subjects, s => Assert.True(ShotFraming.Holds(shot, s, T.Lens)));
        var below = new ShotSubject(shot.Aim - shot.Up * (T.FrameFloor + 0.1f) * half * shot.Distance, 0);
        Assert.False(ShotFraming.Holds(shot, below, T.Lens));
    }

    [Fact]
    public void AFarTargetIsSmallBeyondTheCharacterWhoStaysNearAndLow()
    {
        // The target 28 off: the shot doesn't draw back to hold the two large, it stays near the character, low, the target beyond.
        var sight = new SightLines(Worlds.Flat());
        var me = new ShotSubject(new Vector3(0, 0.7f, 0), 0.7f);
        var target = new ShotSubject(new Vector3(28, 0.7f, 0), 0.7f);
        sight.Bodies.AddRange([me, target]);
        var finder = new ShotFinder(sight, T);
        var behind = -MathF.PI / 2;
        var aim = me.At + new Vector3(T.AimLeadMost, 0, 0);
        var (shot, _) = finder.Best([me, target], behind, behind, aim: aim, most: T.ShoulderDistance)!.Value;
        TestContext.Current.TestOutputHelper?.WriteLine($"yaw {shot.Yaw * 180 / MathF.PI:F0} pitch {shot.Pitch * 180 / MathF.PI:F0} distance {shot.Distance:F1}");
        Assert.True(Vector3.Distance(shot.Eye, me.At) <= T.ShoulderDistance, $"the eye {Vector3.Distance(shot.Eye, me.At):F1} from the character");
        Assert.True(shot.Pitch <= ShotFinder.Rad(30) + 1e-4f);
        Assert.All(new[] { me, target }, s => Assert.True(ShotFraming.Holds(shot, s with { Radius = 0 }, T.Lens)));
        Assert.Equal(0, finder.HiddenWeight(shot, [me, target]));
    }

    // ---- The vantage

    [Fact]
    public void TheVantageStandsOffTheFightOnTheSideWithRoomAndKeepsItAsTheFightMoves()
    {
        // A field 60 by 40; the party at z 30 and the enemies at z 10, near the field's low-x edge: off the line's middle (12, 20) toward
        // high x, where there's room, by 0.6 of the line's 20.
        (Vector2, Vector2) field = (Vector2.Zero, new Vector2(60, 40));
        var (at, side) = Vantage.Place(new Vector2(12, 30), new Vector2(12, 10), field, 0, T)!.Value;
        Assert.True(Vector2.Distance(at, new Vector2(24, 20)) < 1e-3f, $"{at}");
        // The fight moves near the other edge: the vantage stays on its side of it, beyond the field's edge (it's a side, not an eye).
        var (moved, kept) = Vantage.Place(new Vector2(50, 30), new Vector2(50, 10), field, side, T)!.Value;
        Assert.Equal(side, kept);
        Assert.True(Vector2.Distance(moved, new Vector2(62, 20)) < 1e-3f, $"{moved}");
        // With no room left on that side, it goes to the other.
        var (_, other) = Vantage.Place(new Vector2(56, 30), new Vector2(56, 10), field, side, T)!.Value;
        Assert.Equal(-side, other);
    }

    [Fact]
    public void FromTheVantageEveryPartyMemberIsOnTheSameSideOfTheirEnemy()
    {
        // The party to one side, the enemies to the other, the vantage off the line between: whoever faces whom, the party member is on
        // the same side of the screen from it, and a shot over either's shoulder honours it.
        var vantage = new Vector2(24, 20);
        var party = new[] { new Vector3(10, 0.7f, 30), new Vector3(16, 0.7f, 26), new Vector3(8, 0.7f, 24) };
        var enemies = new[] { new Vector3(12, 0.7f, 10), new Vector3(6, 0.7f, 14), new Vector3(17, 0.7f, 12) };
        var sides = (from m in party from e in enemies select Vantage.Side(m, e, vantage)).ToList();
        Assert.All(sides, s => Assert.NotNull(s));
        var memberRight = sides.Select(s => party.Contains(s!.Value.Right)).Distinct().ToList();
        Assert.Single(memberRight);
        // Over the first one's shoulder at its enemy, with its side, the shot keeps it.
        var sight = new SightLines(Worlds.Flat());
        var (me, enemy) = (new ShotSubject(party[0], 0.7f), new ShotSubject(enemies[0], 0.7f));
        sight.Bodies.AddRange([me, enemy]);
        var finder = new ShotFinder(sight, T);
        var pair = Vantage.Side(me.At, enemy.At, vantage)!.Value;
        var behind = MathF.Atan2(me.At.X - enemy.At.X, me.At.Z - enemy.At.Z);
        var (shot, _) = finder.Best([me, enemy], behind, behind, [pair])!.Value;
        Assert.True(ShotFinder.Across(shot, pair, T.Lens) > 0);
        // In line with the vantage, either side is as good: no side.
        Assert.Null(Vantage.Side(new Vector3(24, 0, 30), new Vector3(24, 0, 40), vantage));
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
    public void AnAreaIsPlacedFromAboveItsRingDownToTheBoxWhateverItsSize()
    {
        // Two rings, 5 and 10 across the ground: each fills the screen above the conversation box, its near side at the box's top, so the
        // two look the same size.
        var finder = new ShotFinder(new SightLines(null), T);
        var centre = new Vector3(5, 0, 5);
        var pitch = ShotFinder.Rad(T.TopDownPitch);
        var lens = T.Lens with { Floor = T.AreaFloor };
        var sizes = new List<float>();
        foreach (var radius in new[] { 5f, 10f })
        {
            var ring = Enumerable.Range(0, 16).Select(i => MathF.SinCos(MathF.Tau * i / 16))
                .Select(a => new ShotSubject(centre + new Vector3(a.Cos, 0, a.Sin) * radius)).ToList();
            var shot = finder.Placed(centre, ring, yaw: 0.3f, pitch);
            Assert.True(shot.Pitch >= ShotFinder.Rad(79));
            var (x, y, _) = ShotFraming.OnScreen(shot, centre, T.Lens);
            Assert.True(MathF.Abs(x) < 1e-3f && MathF.Abs(y) < 1e-3f, $"the centre at {x}, {y}");
            Assert.All(ring, p => Assert.True(ShotFraming.Holds(shot, p, lens)));
            // The nearest of it at the box's top, the rest above.
            var lowest = ring.Min(p => ShotFraming.OnScreen(shot, p.At, lens).Y);
            Assert.InRange(lowest, -1.001f, -0.98f);
            sizes.Add(shot.Distance / radius);
        }
        Assert.Equal(sizes[0], sizes[1], 0.01f);
        // Nearer than the old sphere round it, half again its radius under the frame's margins: the ring nearly twice the size.
        Assert.True(sizes[0] < 4.5f, $"{sizes[0]} times its radius away");
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
    public void ADirectorsGoalThatMovesAndStopsIsTrailedNeverOvershot()
    {
        // The director follows none of a goal's own speed: a reticle or a framing that slides for two seconds and stops is trailed, closing
        // on it without passing it and swinging back. Its yaw the same.
        var motion = new CappedMotion(new Shot(Vector3.Zero, 0, 0.5f, 12), T.Caps) { MostFollowed = (0, 0) };
        var goal = new Shot(Vector3.Zero, 0, 0.5f, 12);
        var (overshoot, overturn, lag) = (0f, 0f, 0f);
        for (var tick = 0; tick < 600; tick++)
        {
            if (tick < 120)
                goal = goal with { Aim = goal.Aim + new Vector3(0.1f, 0, 0), Yaw = goal.Yaw + 0.01f };
            motion.Step(goal);
            overshoot = MathF.Max(overshoot, motion.Current.Aim.X - goal.Aim.X);
            overturn = MathF.Max(overturn, Shot.Wrap(motion.Current.Yaw - goal.Yaw));
            lag = MathF.Max(lag, goal.Aim.X - motion.Current.Aim.X);
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"overshot by {overshoot:F3} and {overturn * 180 / MathF.PI:F2}°, {lag:F1} behind at most");
        Assert.True(overshoot < 0.01f, $"overshot by {overshoot}");
        Assert.True(overturn < 0.002f, $"turned past by {overturn}");
        Assert.True(Vector3.Distance(motion.Current.Aim, goal.Aim) < 0.05f);
        Assert.True(lag < 4, $"{lag} behind");
    }

    [Fact]
    public void AShotCarriedAndTurnedWithWhomItFollowsKeepsUpCappingOnlyTheRest()
    {
        // A character walking round its target: the goal moves 0.1 a tick and turns 1° a tick for two seconds. Carried and turned along
        // with them, the shot keeps its goal exactly, never trailing it, and none of it is speed the caps must take off again.
        var motion = new CappedMotion(new Shot(Vector3.Zero, 0, 0.3f, 10), T.Caps) { MostFollowed = (0, 0) };
        var goal = motion.Current;
        var (trail, turned) = (0f, 0f);
        for (var tick = 0; tick < 120; tick++)
        {
            var (step, turn) = (new Vector3(0.1f, 0, 0), ShotFinder.Rad(1));
            goal = goal with { Aim = goal.Aim + step, Yaw = Shot.Wrap(goal.Yaw + turn) };
            motion.Carry(step);
            motion.Turn(turn);
            motion.Step(goal);
            trail = MathF.Max(trail, Vector3.Distance(motion.Current.Aim, goal.Aim));
            turned = MathF.Max(turned, MathF.Abs(Shot.Wrap(motion.Current.Yaw - goal.Yaw)));
        }
        Assert.True(trail < 1e-3f, $"{trail} behind");
        Assert.True(turned < 1e-4f, $"{turned} turned behind");
        Assert.True(motion.AimSpeed.Length() < 1e-3f && MathF.Abs(motion.Speeds.Yaw) < 1e-3f);
    }

    [Fact]
    public void ANewShotsAngleArrivesWithItsPlace()
    {
        // A new shot 6 along and 40° round: the turn lands no later than the aim does, rather than the view orbiting on once it's there.
        var motion = new CappedMotion(new Shot(Vector3.Zero, 0, 0.3f, 10), T.Caps) { MostFollowed = (0, 0) };
        var goal = new Shot(new Vector3(6, 0, 0), ShotFinder.Rad(40), 0.3f, 10);
        int? aimed = null, turned = null;
        for (var tick = 0; tick < 600 && (aimed is null || turned is null); tick++)
        {
            motion.Step(goal);
            if (aimed is null && Vector3.Distance(motion.Current.Aim, goal.Aim) < 6 * 0.05f)
                aimed = tick;
            if (turned is null && MathF.Abs(Shot.Wrap(motion.Current.Yaw - goal.Yaw)) < ShotFinder.Rad(40) * 0.05f)
                turned = tick;
        }
        TestContext.Current.TestOutputHelper?.WriteLine($"the aim within 5% after {aimed} ticks, the turn after {turned}");
        Assert.True(turned <= aimed, $"the turn landed {turned - aimed} ticks after the aim");
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
