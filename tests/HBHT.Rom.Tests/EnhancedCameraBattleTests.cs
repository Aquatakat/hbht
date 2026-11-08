using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Audio;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// Camera: Enhanced on real battles, played turn by turn by a script (<see cref="BattlePlan"/>): with the same presses the battle's rules
/// and random streams are Original's tick for tick; its motion keeps its acceleration within the caps; the camera stick lets go of it until
/// the turn ends; the player's turn frames the character with its target, hiding neither; and under Original the camera shown is the
/// original's.
/// </summary>
public sealed class EnhancedCameraBattleTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private const int Most = 60 * 60 * 20;

    /// <summary>Attack the target A picks every turn (A, then A to confirm); a refused one is put away and the turn skipped.</summary>
    private const string Attacking = "*=accept/accept";

    [Fact]
    public void TheRatsBattlePlaysTheSameUnderEitherCameraWithTheSamePresses()
    {
        var original = Play(host => Plain(Set, 1, host), CameraStyle.Original, Attacking);
        var enhanced = Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, Attacking);
        Same(original, enhanced);
        Assert.Equal(BattleOutcome.Won, original.Battle.Outcome);
        Assert.True(enhanced.Apart > 100, $"the enhanced camera was apart from the original's on {enhanced.Apart} ticks");
    }

    [Fact]
    public void TheSkeletonArchersBattlePlaysTheSameUnderEitherCamera()
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HBHT_PARITY") ?? Path.Combine(TestRoms.RepoRoot(), "out", "parity"),
            "20261031-230020-setup8/battle-01");
        if (!Directory.Exists(path))
            Assert.Skip("No parity log for the skeleton archers.");
        var log = Log(path);
        var original = Play(host => FromSetup(Set, log, "archers", host: host), CameraStyle.Original, "*=skip");
        var enhanced = Play(host => FromSetup(Set, log, "archers", host: host), CameraStyle.Enhanced, "*=skip");
        Same(original, enhanced);
    }

    [Fact]
    public void UnderOriginalTheCameraShownIsTheOriginalsEveryTick()
    {
        Play(host => Plain(Set, 1, host), CameraStyle.Original, Attacking, run =>
        {
            Assert.Same(run.Battle.CombatCamera.Camera, run.Battle.Camera);
            Assert.Null(run.Battle.CombatCamera.Enhanced);
        });
    }

    [Fact]
    public void TheCamerasAccelerationStaysWithinItsCapsAndItsEyeOffItsAimThroughABattle()
    {
        // The party walks forward in each turn, then attacks: turns, aims, glides and the AI's walks, all under the caps.
        // A cut starts again at rest, and the player's hand and an aim raise the caps, so those ticks aren't measured.
        var caps = CameraTuning.Default.Caps;
        var (aimMost, turnMost, zoomMost) = (0f, 0f, 0f);
        (Vector3 Aim, float Yaw, float Pitch, float Distance)? last = null;
        var cuts = 0;
        var run = Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:1:40/wait:10/accept/accept", r =>
        {
            if (r.Battle.CombatCamera.Enhanced is not { Motion: { } m } e)
                return;
            var (y, p, d) = m.Speeds;
            Assert.True(m.Current.Distance >= CameraTuning.Default.NearestDistance, $"the eye {m.Current.Distance} from its aim");
            var cut = e.Cuts != cuts;
            cuts = e.Cuts;
            if (cut || e.Quick)
            {
                last = null;
                return;
            }
            if (last is { } l)
            {
                aimMost = MathF.Max(aimMost, (m.AimSpeed - l.Aim).Length() * 60);
                turnMost = MathF.Max(turnMost, MathF.Max(MathF.Abs(y - l.Yaw), MathF.Abs(p - l.Pitch)) * 60 * 180 / MathF.PI);
                zoomMost = MathF.Max(zoomMost, MathF.Abs(d - l.Distance) * 60);
            }
            last = (m.AimSpeed, y, p, d);
        });
        var enhanced = run.Battle.CombatCamera.Enhanced!;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"most acceleration: aim {aimMost:F1}, turn {turnMost:F1}, zoom {zoomMost:F1}; cuts {enhanced.Cuts}; {run.Ticks} ticks"));
        Assert.True(aimMost <= caps.Aim * 1.001f, $"aim {aimMost}");
        Assert.True(turnMost <= caps.Turn * 1.001f, $"turn {turnMost}");
        Assert.True(zoomMost <= caps.Zoom * 1.001f, $"zoom {zoomMost}");
    }

    [Fact]
    public void ATurnOpensOnOneShotHeldTillThePlayerMoves()
    {
        // Each member's turn: a second standing still, then a walk and an attack. The shot the turn opens on, over the shoulder at the enemy
        // ahead or the nearest in range, doesn't change under the player while they've done nothing.
        var t = CameraTuning.Default;
        var (turns, onEnemy, ranged, changed, standing) = (0, 0, 0, 0, 0);
        (Combatant Who, Vector3 At, Vector2 Heading, Shot Goal)? opened = null;
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=wait:60/stick:0:1:40/wait:40/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            // From the tick the camera has seen the turn (the battle's state may change after it steps).
            if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || !e.Walking)
            {
                opened = null;
                return;
            }
            var at = me.Body.Sphere.Position;
            if (opened is not { } o || o.Who != me)
            {
                opened = (me, at, me.Body.Heading, e.Goal);
                turns++;
                var inRange = battle.Combatants.Any(c => c.Hostile != me.Hostile && !c.Freed && !c.Sheet.IsDead && c.Active
                    && Vector2.Distance(new(c.Body.Sphere.Position.X, c.Body.Sphere.Position.Z), new(at.X, at.Z)) <= t.TargetRange);
                if (inRange)
                    ranged++;
                if (inRange && e.Target is not null)
                    onEnemy++;
                return;
            }
            if (Vector3.Distance(at, o.At) > 0.05f || Vector2.Dot(me.Body.Heading, o.Heading) < 0.999f)
                return;
            standing++;
            // It may follow the bodies settling by hundredths, but never jumps nor drifts far.
            var g = e.Goal;
            if (Vector3.Distance(g.Aim, o.Goal.Aim) > 0.5f || MathF.Abs(Shot.Wrap(g.Yaw - o.Goal.Yaw)) > ShotFinder.Rad(5)
                || MathF.Abs(g.Pitch - o.Goal.Pitch) > ShotFinder.Rad(1) || MathF.Abs(g.Distance - o.Goal.Distance) > 0.5f)
                changed++;
        });
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{turns} turns, {onEnemy} of the {ranged} with an enemy in range opened on one; the shot changed on {changed} of {standing} ticks standing");
        Assert.True(turns > 20, $"{turns} turns");
        Assert.True(standing > turns * 30, $"{standing} ticks standing");
        Assert.Equal(ranged, onEnemy);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void WithNoEnemyAheadTheViewTrailsTheCharacterOnALeashLeadingThem()
    {
        // Each member's turn: walking toward the camera, away from the enemies, then across the view and back. With no enemy ahead the view
        // trails them as a third person's camera does: a step across it turns it round after them, a step toward it or away only draws it
        // along; and its aim leads them the way they face. All of it by the point that follows them (FollowSmoothing), not their body.
        var t = CameraTuning.Default;
        var (ticks, led, across, turnedAfter, along, still) = (0, 0, 0, 0, 0, 0);
        (Combatant Who, Vector3 At, float Yaw)? last = null;
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:-1:30/stick:1:0:30/stick:-1:0:30/stick:0:1:30/wait:20/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || !e.Walking || e.Target is not null
                || e.Motion is not { } m || e.FollowAt is not { } at)
            {
                last = null;
                return;
            }
            ticks++;
            var g = e.Goal;
            var lead = MathF.Min(t.AimLead * t.LookAhead, t.AimLeadMost);
            if (Vector3.Distance(g.Aim, at + new Vector3(me.Body.Heading.X, 0, me.Body.Heading.Y) * lead) < 0.01f)
                led++;
            if (last is { } l && l.Who == me)
            {
                // The step across the shot shown, to its right, and how the goal's yaw turned: a step right turns it round to the left.
                var step = at - l.At;
                var right = Vector3.Dot(step, m.Current.Right);
                var turned = Shot.Wrap(g.Yaw - l.Yaw);
                if (MathF.Abs(turned) < ShotFinder.Rad(2))
                {
                    if (MathF.Abs(right) > 0.02f)
                    {
                        across++;
                        if (MathF.Sign(turned) == -MathF.Sign(right))
                            turnedAfter++;
                    }
                    else if (step.Length() > 0.02f)
                    {
                        along++;
                        if (MathF.Abs(turned) < ShotFinder.Rad(0.2f))
                            still++;
                    }
                }
            }
            last = (me, at, g.Yaw);
        });
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{ticks} ticks with no enemy ahead: the aim leading on {led}; {turnedAfter} of {across} steps across turned it after the character, " +
            $"{still} of {along} steps toward or away left it be");
        Assert.True(across > 100 && along > 30, $"{across} steps across, {along} along");
        Assert.Equal(ticks, led);
        Assert.True(turnedAfter >= across * 0.95, $"{turnedAfter} of {across} turned after");
        Assert.True(still >= along * 0.95, $"{still} of {along} left be");
    }

    [Fact]
    public void GoingRoundAPackTheShotTurnsSmoothly()
    {
        // Each member's turn: toward the enemies, then round them. Over the shoulder at the target, the shot orbits it with the character
        // and eases onto a new framing, a new target's among them: tick to tick as they walk, its yaw turns no faster than the orbit and
        // the easing together, its pitch than the easing.
        var t = CameraTuning.Default;
        var (ticks, steps, most) = (0, 0, 0f);
        (Combatant Who, int Cuts, Vector3 At, float Yaw, float Pitch)? last = null;
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:1:30/stick:1:0:60/wait:20/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || !e.Walking || e.Target is null)
            {
                last = null;
                return;
            }
            var g = e.Goal;
            var at = me.Body.Sphere.Position;
            // As they walk: a turn's opening, on a tick they stand, is a new shot.
            if (last is { } l && l.Who == me && l.Cuts == e.Cuts && Vector3.Distance(at, l.At) > 1e-3f)
            {
                ticks++;
                var yaw = MathF.Abs(Shot.Wrap(g.Yaw - l.Yaw));
                most = MathF.Max(most, yaw);
                if (yaw > ShotFinder.Rad(t.CarryTurn + t.FramingTurn) / 60 + 1e-4f || MathF.Abs(g.Pitch - l.Pitch) > ShotFinder.Rad(t.FramingTurn) / 60 + 1e-4f)
                    steps++;
            }
            last = (me, e.Cuts, at, g.Yaw, g.Pitch);
        });
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{ticks} ticks over the shoulder: {steps} stepped; the most it turned in a tick {most * 180 / MathF.PI:F2}°");
        Assert.True(ticks > 1000, $"{ticks} ticks");
        Assert.Equal(0, steps);
    }

    [Fact]
    public void TheCameraStickLetsGoOfTheDirectorUntilThePlayerAimsOrTheTurnEnds()
    {
        // Each member's turn: wait, turn the camera with its stick, wait, then attack.
        var aimedReleased = 0;
        Combatant? turn = null;
        var (released, orbited, freshTurns, near, far) = (0, 0, 0, 0, 0);
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=wait:20/orbit:1:0:30/wait:60/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.Current != turn)
            {
                turn = battle.Current;
                // A new turn: the director has the camera again.
                Assert.False(e.Released);
                freshTurns++;
                orbited = 0;
                return;
            }
            // Aiming takes it back.
            if (battle.State is CombatState.TargetAttack or CombatState.TargetSpell or CombatState.TargetThrown or CombatState.PlaceArea && e.Released)
                aimedReleased++;
            if (battle.Current is not { Controlled: false } || battle.State != CombatState.Turn)
                return;
            if (e.Released)
            {
                released++;
                // Let go, it aims for the original's shot and nothing else.
                var own = Shot.Of(battle.CombatCamera.Camera.Position, battle.CombatCamera.Camera.Aim);
                Assert.True(Vector3.Distance(own.Eye, e.Goal.Eye) < 1e-3f && Vector3.Distance(own.Aim, e.Goal.Aim) < 1e-3f);
                // Once let go, the camera shown follows the original's, a second and a half after the stick was turned.
                // At the end of the wait after the orbit, it has caught up.
                if (++orbited == 88)
                {
                    var now = Vector3.Distance(battle.Camera.Position, battle.CombatCamera.Camera.Position);
                    TestContext.Current.TestOutputHelper?.WriteLine($"{turn}: {now:F2} apart");
                    if (now < 1)
                        near++;
                    else
                        far++;
                }
            }
        });
        Assert.True(released > 100, $"{released} ticks let go");
        Assert.Equal(0, aimedReleased);
        Assert.True(freshTurns > 10, $"{freshTurns} turns");
        // Where the original's own camera jumps late in the wait (its leash snapping behind the character), the shown one is still on its way.
        Assert.True(near > 10 && far * 10 <= near, $"{near} waits ended within a unit of the original's camera, {far} further");
    }

    [Fact]
    public void ThePlayersTurnFramesTheCharacterWithItsTargetHidingNeither()
    {
        var t = CameraTuning.Default;
        var (framed, held, low, walkingCuts) = (0, 0, 0, 0);
        var (nearTicks, nearHidden, farTicks, farHidden) = (0, 0, 0, 0);
        var (turnTicks, shown, close) = (0, 0, 0);
        var (cuts, walked) = (0, (Combatant?)null);
        var (landed, trailing, trail, longest) = (0, 0, 0, 0);
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:1:40/wait:40/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            // The view's angle doesn't trail in after its place: where the aim has landed, the yaw lands soon after, even from a new
            // framing chosen as the character walks among the pack.
            var trailed = false;
            if (battle.State == CombatState.Turn && e.Target is not null && !e.Quick && e.Motion is { } m
                && Vector3.Distance(m.Current.Aim, e.Goal.Aim) < 0.3f)
            {
                landed++;
                trailed = MathF.Abs(Shot.Wrap(m.Current.Yaw - e.Goal.Yaw)) > ShotFinder.Rad(5);
                if (trailed)
                    trailing++;
            }
            trail = trailed ? trail + 1 : 0;
            longest = Math.Max(longest, trail);
            // It never cuts while the player walks the character, but on the tick their turn begins.
            if (e.Cuts != cuts && e.Walking && walked == battle.Current)
                walkingCuts++;
            (cuts, walked) = (e.Cuts, e.Walking ? battle.Current : null);
            if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || e.Released)
                return;
            // The character in the shot shown, above the conversation box: the view moves with them, and catches up as they near its edge.
            turnTicks++;
            var sphere = new ShotSubject(me.Body.Sphere.Position, me.Body.ScaledRadius);
            if (ShotFraming.Holds(e.Motion!.Current, sphere with { Radius = 0 }, t.Lens))
                shown++;
            if (e.Target is not { } target || e.Finder is not { } finder)
                return;
            framed++;
            // The character stays large: the eye never goes far from them for a target far off.
            if (Vector3.Distance(e.Goal.Eye, sphere.At) <= t.ShoulderDistance + t.AimLeadMost)
                close++;
            ShotSubject[] both = [sphere, new(target.Body.Sphere.Position, target.Body.ScaledRadius)];
            if (both.All(s => ShotFraming.Holds(e.Goal, s with { Radius = 0 }, t.Lens)))
                held++;
            // A target near is hidden only for a moment; one far off, beyond the pack's range, may be behind another body rather than seen from high.
            var hidden = finder.HiddenWeight(e.Goal, both) > 0;
            if (Vector3.Distance(both[0].At, both[1].At) <= t.PackRange)
                (nearTicks, nearHidden) = (nearTicks + 1, nearHidden + (hidden ? 1 : 0));
            else
                (farTicks, farHidden) = (farTicks + 1, farHidden + (hidden ? 1 : 0));
            if (e.Goal.Pitch <= ShotFinder.Rad(30) + 1e-4f)
                low++;
            // Only enemies are targets.
            Assert.NotEqual(me.Hostile, target.Hostile);
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{framed} ticks framed with a target: {held} holding both, {low} low, {close} near the character; " +
            $"hidden on {nearHidden} of {nearTicks} near, {farHidden} of {farTicks} far; {walkingCuts} cuts walking; the character shown on {shown} of {turnTicks} turn ticks; " +
            $"the angle more than 5° off on {trailing} of {landed} ticks its aim had landed, {longest} in a row at most");
        Assert.True(longest <= 45, $"the angle trailed {longest} ticks in a row");
        Assert.True(trailing <= landed * 0.1, $"the angle trailing on {trailing} of {landed}");
        Assert.Equal(0, walkingCuts);
        Assert.True(shown >= turnTicks * 0.98, $"the character shown on {shown} of {turnTicks}");
        Assert.Equal(framed, close);
        Assert.True(framed > 200, $"{framed} ticks framed");
        Assert.True(held >= framed * 0.98, $"{held} of {framed} held both");
        // A framing that comes to hide one is eased away from rather than stepped (FramingTurn), so it may hide it for a moment.
        Assert.True(nearHidden <= nearTicks * 0.05, $"{nearHidden} of {nearTicks} near hid one");
        Assert.True(farHidden <= farTicks * 0.1, $"{farHidden} of {farTicks} far hid one");
        // Over the shoulder, never from high above.
        Assert.Equal(framed, low);
    }

    [Fact]
    public void AnAIsTurnIsOneShotPlannedOnceAndHeld()
    {
        // Through every AI's turn, the goal is planned once it acts and then held: planned again only if the walk or the target leaves it.
        Combatant? turn = null;
        Shot? planned = null;
        var (turns, plans, moved) = (0, 0, 0);
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, Attacking, run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.Current != turn)
            {
                // The camera sees the turn begin on the tick after.
                turn = battle.Current;
                planned = null;
                if (turn is { Controlled: true })
                    turns++;
                return;
            }
            if (!e.Kind.EndsWith("planned", StringComparison.Ordinal))
                return;
            // Never from beyond the battlefield, behind the scenery round it.
            Assert.False(e.Finder!.Outside(e.Goal.Eye), $"{e.Goal.Eye} beyond the field");
            if (turn is not { Controlled: true })
                return;
            if (planned != e.Goal)
            {
                plans++;
                planned = e.Goal;
            }
            if (!e.Motion!.Moving)
                return;
            moved++;
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{turns} AI turns, {plans} plans, {moved} ticks moving");
        Assert.True(turns > 5, $"{turns} AI turns");
        Assert.True(plans <= turns * 1.2, $"{plans} plans in {turns} turns");
    }

    [Fact]
    public void EveryActionIsShotFromTheVantagesSide()
    {
        // Through every action at an enemy, the player's and the AI's, a party member and their enemy both before the eye are framed the
        // way round the battle's vantage sees them.
        var (pairs, kept) = (0, 0);
        var vantages = new HashSet<Vector2>();
        var (had, turned) = (0, 0);
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, Attacking, run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (e.VantagePoint is { } v)
                vantages.Add(v);
            if (e.VantageSide != 0 && had != 0 && e.VantageSide != had)
                turned++;
            had = e.VantageSide != 0 ? e.VantageSide : had;
            if (battle.Current is not { } current || e.VantagePoint is not { } vantage
                || !(e.Kind.EndsWith("planned", StringComparison.Ordinal) || e.Kind.StartsWith("loosing", StringComparison.Ordinal)))
                return;
            var slot = current.Controlled && battle.State is CombatState.AI or CombatState.Moving ? current.Ai?.Target ?? -1 : battle.Target;
            if (slot < 0 || slot >= battle.Slots.Count || battle.Slots[slot] is not { Freed: false } other || other.Hostile == current.Hostile)
                return;
            if (Vantage.Side(current.Body.Sphere.Position, other.Body.Sphere.Position, vantage) is not { } side)
                return;
            var lens = CameraTuning.Default.Lens;
            if (ShotFraming.OnScreen(e.Goal, side.Right, lens).Depth <= 0 || ShotFraming.OnScreen(e.Goal, side.Left, lens).Depth <= 0)
                return;
            pairs++;
            if (ShotFinder.Across(e.Goal, side, lens) >= 0)
                kept++;
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{vantages.Count} vantages, its side changed {turned} times; {kept} of {pairs} action ticks framed their pair the vantage's way");
        Assert.True(pairs > 1000, $"{pairs} ticks");
        Assert.True(kept >= pairs * 0.9, $"{kept} of {pairs} the vantage's way");
        // Placed as the battle begins and again as the fight moves, seldom, keeping its side: the camera doesn't cross the line.
        Assert.InRange(vantages.Count, 1, 20);
        Assert.InRange(turned, 0, 1);
    }

    [Fact]
    public void AnArrowOrASpellAtSomeoneFurtherOffIsLoosedThenCutToTheHit()
    {
        // The skeleton archers shoot the party from afar: each shot is the loosing, over the archer's shoulder, then a cut to the hit, at
        // its target, while the arrow is still in the air.
        var path = Path.Combine(Environment.GetEnvironmentVariable("HBHT_PARITY") ?? Path.Combine(TestRoms.RepoRoot(), "out", "parity"),
            "20261031-230020-setup8/battle-01");
        if (!Directory.Exists(path))
            Assert.Skip("No parity log for the skeleton archers.");
        var log = Log(path);
        var (loosings, hits, inFlight, holdLoosing, holdHit) = (0, 0, 0, 0, 0);
        var (kind, cuts) = ("", 0);
        Play(host => FromSetup(Set, log, "archers", host: host), CameraStyle.Enhanced, "*=skip", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            var cut = e.Cuts != cuts;
            cuts = e.Cuts;
            var was = kind;
            kind = e.Kind;
            if (kind == was || battle.Current is not { } current || battle.Target < 0 || battle.Slots[battle.Target] is not { } target)
                return;
            var lens = CameraTuning.Default.Lens;
            if (kind.StartsWith("loosing", StringComparison.Ordinal))
            {
                loosings++;
                Assert.True(cut, "the loosing is cut to");
                if (ShotFraming.Holds(e.Goal, new ShotSubject(current.Body.Sphere.Position, 0), lens))
                    holdLoosing++;
            }
            else if (kind.StartsWith("the hit", StringComparison.Ordinal))
            {
                hits++;
                Assert.True(cut, "the hit is cut to");
                Assert.StartsWith("loosing", was, StringComparison.Ordinal);
                if (battle.Flight is not null)
                    inFlight++;
                if (ShotFraming.Holds(e.Goal, new ShotSubject(target.Body.Sphere.Position, 0), lens))
                    holdHit++;
            }
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{loosings} loosings ({holdLoosing} holding the archer), {hits} hits ({holdHit} holding the target, {inFlight} cut to in flight)");
        Assert.True(loosings >= 3, $"{loosings} loosings");
        Assert.Equal(loosings, hits);
        Assert.Equal(loosings, holdLoosing);
        Assert.Equal(hits, holdHit);
        Assert.True(inFlight >= hits * 0.8, $"{inFlight} of {hits} cut to in flight");
    }

    [Fact]
    public void TheViewFollowsACharacterEvenlyThoughTheirStrideIsUneven()
    {
        // Under the original's movement a body moves every other tick and its root stops and pushes off each stride. The point the
        // camera follows them by moves evenly: tick to tick its step changes by little of itself, where the body's changes by more than it.
        var (bodyChange, pointChange, bodySum, pointSum, ticks) = (0f, 0f, 0f, 0f, 0);
        (Combatant Who, Vector3 Body, Vector3 Point, float BodyStep, float PointStep)? last = null;
        Play(host =>
        {
            var b = Plain(Set, 1, host);
            b.Battle.Movement = MovementMode.Original;
            return b;
        }, CameraStyle.Enhanced, "*=stick:0:1:60/wait:10/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || !e.Walking || e.FollowAt is not { } point)
            {
                last = null;
                return;
            }
            var body = me.Body.Sphere.Position;
            if (last is { } l && l.Who == me)
            {
                var (bodyStep, pointStep) = (Vector3.Distance(body, l.Body), Vector3.Distance(point, l.Point));
                // Under way: the point moving at a walk's pace.
                if (pointStep > 0.03f && l.PointStep > 0.03f)
                {
                    ticks++;
                    bodyChange += MathF.Abs(bodyStep - l.BodyStep);
                    pointChange += MathF.Abs(pointStep - l.PointStep);
                    bodySum += bodyStep;
                    pointSum += pointStep;
                }
                last = (me, body, point, bodyStep, pointStep);
            }
            else
            {
                last = (me, body, point, 0, 0);
            }
        });
        var (bodyJerk, pointJerk) = (bodyChange / bodySum, pointChange / pointSum);
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"{ticks} ticks under way: the body's step changes by {bodyJerk:F2} of itself a tick, the point's by {pointJerk:F3}"));
        Assert.True(ticks > 300, $"{ticks} ticks");
        Assert.True(bodyJerk > 0.5f, $"the body's {bodyJerk}");
        Assert.True(pointJerk < bodyJerk / 10, $"the point's {pointJerk} against the body's {bodyJerk}");
    }

    [Fact]
    public void OverTheShoulderTheShotNeverTurnsItselfToTheFrontOfAWalker()
    {
        // Four bandits; each member's turn walks at them, then past them either side. The shot over the shoulder orbits the target and eases
        // onto new framings, but never turns itself further than MostAcross from straight behind the way the character heads, unless it
        // was already further, where they turned toward it themselves.
        var t = CameraTuning.Default;
        var bandit = RecordId.Hex(RecordTypes.Entity, 0x025E);
        var (ticks, past, turnedOn) = (0, 0, 0);
        (Combatant Who, float Yaw)? last = null;
        Play(host => Plain(Set, 1, host, [.. Enumerable.Repeat(new PartyEnemy(bandit, 1, 1), 4)]), CameraStyle.Enhanced,
            "*=stick:0:1:40/stick:1:1:50/stick:-1:1:60/wait:10/accept/accept", run =>
            {
                var battle = run.Battle;
                var e = battle.CombatCamera.Enhanced!;
                if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || !e.Walking || e.Target is null)
                {
                    last = null;
                    return;
                }
                var h = me.Body.Heading;
                var back = MathF.Atan2(-h.X, -h.Y);
                var yaw = e.Goal.Yaw;
                if (last is { } l && l.Who == me)
                {
                    ticks++;
                    var (now, was) = (MathF.Abs(Shot.Wrap(yaw - back)), MathF.Abs(Shot.Wrap(l.Yaw - back)));
                    if (now > ShotFinder.Rad(t.MostAcross) + 1e-3f)
                    {
                        past++;
                        if (now > was + 1e-3f)
                            turnedOn++;
                    }
                }
                last = (me, yaw);
            });
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{ticks} ticks over the shoulder: past the character's side on {past}, the shot turning itself further on {turnedOn}");
        Assert.True(ticks > 500, $"{ticks} ticks");
        Assert.Equal(0, turnedOn);
    }

    [Fact]
    public void AnAimIsPannedToNotCutTo()
    {
        // Sholeh places Dispel Naming from above and Rheda aims Weakness; the others aim their blows. Into an aim, about it and back from
        // it, the camera pans: it never cuts while aiming, and each pan ends on its goal.
        var (pans, aimTicks, cutsAiming, unfinished) = (0, 0, 0, 0);
        var (cuts, panning, wasAiming) = (0, false, false);
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced,
            "Sholeh=spell:Dispel Naming/wait:60/accept;Rheda=spell:Weakness/wait:60/accept/accept;*=accept/wait:40/accept", run =>
            {
                var battle = run.Battle;
                var e = battle.CombatCamera.Enhanced!;
                var aiming = battle.State is CombatState.TargetAttack or CombatState.TargetSpell or CombatState.PlaceArea or CombatState.TargetThrown;
                if (aiming)
                    aimTicks++;
                if (e.Cuts != cuts && (aiming || wasAiming && battle.State == CombatState.Turn))
                    cutsAiming++;
                if (e.Panning && !panning)
                    pans++;
                // A pan that ends in its own state ends on its goal.
                if (panning && !e.Panning && e.Motion is { } m && aiming && Vector3.Distance(m.Current.Aim, e.Goal.Aim) > 0.01f)
                    unfinished++;
                (cuts, panning, wasAiming) = (e.Cuts, e.Panning, aiming);
            });
        TestContext.Current.TestOutputHelper?.WriteLine($"{aimTicks} ticks aiming, {pans} pans, {cutsAiming} cuts aiming, {unfinished} pans short of their goal");
        Assert.True(pans >= 4, $"{pans} pans");
        Assert.Equal(0, cutsAiming);
        Assert.Equal(0, unfinished);
    }

    [Fact]
    public void AnAimAtABlowKeepsTheShotThatAlreadyHoldsBoth()
    {
        // Each member walks at the enemy, stands, then aims a blow. Where the shot the turn was settling on holds the attacker and the
        // body under the cursor and hides neither, it's kept as the aim begins, and isn't changed while the cursor stays.
        var t = CameraTuning.Default;
        var (aims, couldKeep, kept, changed) = (0, 0, 0, 0);
        Shot? before = null;
        (Combatant Cursor, Shot Shot)? keeping = null;
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:1:30/wait:40/accept/wait:30/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (e.Finder is not { } finder)
                return;
            if (battle.State == CombatState.TargetAttack && battle.Current is { Controlled: false } me && e.Kind.StartsWith("aiming a blow")
                && battle.CameraOwner is { } cursor && cursor != me)
            {
                if (before is { } b)
                {
                    aims++;
                    var both = new[] { me, cursor }.All(c => ShotFraming.Holds(b, new ShotSubject(c.Body.Sphere.Position, 0), t.Lens)
                        && !finder.Sight.Hidden(b.Eye, new ShotSubject(c.Body.Sphere.Position, c.Body.ScaledRadius)));
                    if (both)
                    {
                        couldKeep++;
                        if (e.Kind == "aiming a blow, the shot kept" && e.Goal == b)
                        {
                            kept++;
                            keeping = (cursor, b);
                        }
                    }
                }
                else if (keeping is { } k && k.Cursor == cursor && e.Goal != k.Shot)
                {
                    changed++;
                    keeping = null;
                }
                before = null;
                return;
            }
            keeping = null;
            before = battle.State == CombatState.Turn ? e.Goal : null;
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{aims} blows aimed, {couldKeep} with the shot holding both, {kept} kept, {changed} changed after");
        Assert.True(couldKeep >= 3, $"{couldKeep} could keep");
        Assert.Equal(couldKeep, kept);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void ASpellAtAnAllyIsShownLandingOnThemHoweverNear()
    {
        // Rheda casts Endurance on the party: each who isn't her is shown from the loosing over her shoulder, then the effect on them.
        var (onAllies, hits) = (0, 0);
        (Combatant Who, Combatant At)? volley = null;
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "Rheda=spell:Endurance/accept/accept/accept/accept;*=accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.Current is not { } caster || battle.Target < 0 || battle.Target >= battle.Slots.Count || battle.Slots[battle.Target] is not { } at
                || at == caster || at.Hostile != caster.Hostile)
                return;
            if (e.Kind == "loosing, over the shoulder" && volley != (caster, at))
            {
                volley = (caster, at);
                onAllies++;
            }
            if (e.Kind == "the hit, at the target" && volley == (caster, at))
            {
                volley = null;
                hits++;
            }
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{onAllies} spells at allies loosed, {hits} shown landing");
        Assert.True(onAllies >= 1, $"{onAllies} at allies");
        Assert.Equal(onAllies, hits);
    }

    [Fact]
    public void AnAIsWalkIsFollowed()
    {
        // The rats walk to their targets: the shot moves with each, as far as the point it follows them by.
        var (walks, followed) = (0, 0);
        (Combatant Who, Vector3 Point, Vector3 Aim, float Walked, float Moved)? walk = null;
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, Attacking, run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            var walking = battle.Current is { Controlled: true } ai && e.Kind == "an AI's turn, following its walk" && e.FollowAt is not null
                && e.Motion is not null;
            if (walk is { } w && (!walking || battle.Current != w.Who))
            {
                // A walk of a unit or more, the shot moving with it most of the way.
                if (w.Walked > 1)
                {
                    walks++;
                    if (w.Moved >= w.Walked * 0.8f)
                        followed++;
                }
                walk = null;
            }
            if (!walking)
                return;
            var (point, aim) = (e.FollowAt!.Value, e.Motion!.Current.Aim);
            walk = walk is { } v
                ? v with { Point = point, Aim = aim, Walked = v.Walked + Vector3.Distance(point, v.Point), Moved = v.Moved + Vector3.Distance(aim, v.Aim) }
                : (battle.Current!, point, aim, 0, 0);
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{walks} AI walks, {followed} followed");
        Assert.True(walks >= 5, $"{walks} walks");
        Assert.Equal(walks, followed);
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    private sealed class Run
    {
        public required Built Built { get; init; }
        public required BattleDrawing Drawing { get; init; }
        public Battle Battle => Built.Battle;

        /// <summary>The battle's frames as it recorded them, what the rules did: the bodies, the state and the log, but not the camera.</summary>
        public List<string> Recorded { get; } = [];

        public int Ticks { get; set; }

        /// <summary>Ticks the camera shown was more than a unit from the original's.</summary>
        public int Apart { get; set; }
    }

    private Run Play(Func<ICombatHost, Built> build, CameraStyle style, string plan, Action<Run>? each = null)
    {
        var audio = GameAudio.Silent(Set);
        var recorder = new BattleRecorder(audio);
        var built = build(recorder);
        var zone = new ZoneEngine(built.Game, built.Streamer);
        zone.Keys.Output = recorder;
        recorder.Keys = zone.Keys;
        var drawing = new BattleDrawing(built.Battle, recorder, null, audio) { Budget = null, Turns = CombatTurns.Original };
        built.Battle.CombatCamera.Style = style;
        zone.Camera = drawing.Camera;
        var run = new Run { Built = built, Drawing = drawing };
        drawing.Recorded = frame => run.Recorded.Add(Digest(frame));
        var script = BattlePlan.Parse(plan);
        for (var tick = 0; tick < Most; tick++)
        {
            var input = script.Tick(drawing);
            var step = drawing.Tick(zone, skyDrawn: false, readInput: input is { } i ? () => i : null);
            run.Ticks++;
            var battle = built.Battle;
            if (Vector3.Distance(battle.Camera.Position, battle.CombatCamera.Camera.Position) > 1)
                run.Apart++;
            each?.Invoke(run);
            if (step is BattleStep.Paused)
                battle.Unpause(flee: true);
            if (step is BattleStep.Ended)
                return run;
        }
        Assert.Fail($"The battle didn't end in {Most} ticks ({style}).");
        return run;
    }

    private static void Same(Run original, Run enhanced)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{original.Recorded.Count} ticks recorded, {original.Battle.Log.Count} lines, {original.Battle.Outcome}; the enhanced camera apart " +
            $"from the original's on {enhanced.Apart} of {enhanced.Ticks} ticks, {enhanced.Battle.CombatCamera.Enhanced?.Cuts} cuts");
        var n = Math.Min(original.Recorded.Count, enhanced.Recorded.Count);
        for (var i = 0; i < n; i++)
        {
            if (original.Recorded[i] != enhanced.Recorded[i])
                Assert.Fail($"The recorded frames part at tick {i}:\nOriginal: {original.Recorded[i]}\nEnhanced: {enhanced.Recorded[i]}");
        }
        Assert.Equal(original.Recorded.Count, enhanced.Recorded.Count);
        var (a, b) = (original.Battle, enhanced.Battle);
        Assert.Equal(a.Outcome, b.Outcome);
        Assert.Equal(a.Log, b.Log);
        Assert.Equal(a.Slots.Select(Standing), b.Slots.Select(Standing));
        var (ra, rb) = (original.Built.Game.Random, enhanced.Built.Game.Random);
        Assert.Equal((ra.Combat.Seed, ra.Sort.Seed, ra.Animation.Seed, ra.Loot.Seed, ra.World.Seed),
            (rb.Combat.Seed, rb.Sort.Seed, rb.Animation.Seed, rb.Loot.Seed, rb.World.Seed));

        static string Standing(Combatant? c) =>
            c is null ? "-" : $"{c} hp {c.Sheet.Hp}/{c.Sheet.MaxHp} cell {c.Cell} at {c.Body.Sphere.Position} freed {c.Freed}";
    }

    /// <summary>What a recorded frame says of the rules: the state, whose turn, the bodies and their actors, the numbers and the log.</summary>
    private static string Digest(BattleFrame f)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{f.Tick} {f.State} {f.Current?.Slot} {f.Step} | bodies");
        foreach (var b in f.Bodies)
            s.Append(CultureInfo.InvariantCulture, $" [{b.Combatant.Slot} {b.Centre} {b.Heading.X:R},{b.Heading.Y:R} a{b.Alpha} s{b.ActorState} c{b.ActorClock}]");
        s.Append(" | numbers");
        foreach (var n in f.Numbers)
            s.Append(CultureInfo.InvariantCulture, $" [{n.Number.Slot} {n.Number.Amount} {n.Ticks}]");
        if (f.Missile is { } m)
            s.Append(CultureInfo.InvariantCulture, $" | missile {m.Flight.Scene} {m.Drawn}");
        s.Append(CultureInfo.InvariantCulture, $" | aim {f.Aim} | log {f.Gui.Log}");
        return s.ToString();
    }
}
