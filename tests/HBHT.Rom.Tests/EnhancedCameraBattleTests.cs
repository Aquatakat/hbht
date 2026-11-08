using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Audio;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Plugins;
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
        var caps = CameraTuning.Default.Caps;
        var (aimMost, turnMost, zoomMost) = (0f, 0f, 0f);
        (Vector3 Aim, float Yaw, float Pitch, float Distance)? last = null;
        var run = Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:1:40/wait:10/accept/accept", r =>
        {
            if (r.Battle.CombatCamera.Enhanced?.Motion is not { } m)
                return;
            var (y, p, d) = m.Speeds;
            Assert.True(m.Current.Distance >= CameraTuning.Default.NearestDistance, $"the eye {m.Current.Distance} from its aim");
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
        Assert.Equal(0, enhanced.Cuts);
        Assert.True(aimMost <= caps.Aim * 1.001f, $"aim {aimMost}");
        Assert.True(turnMost <= caps.Turn * 1.001f, $"turn {turnMost}");
        Assert.True(zoomMost <= caps.Zoom * 1.001f, $"zoom {zoomMost}");
    }

    [Fact]
    public void TheCameraStickLetsGoOfTheDirectorUntilTheTurnEnds()
    {
        // Each member's turn: wait, turn the camera with its stick, wait, then attack.
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
        Assert.True(freshTurns > 10, $"{freshTurns} turns");
        // Where the original's own camera jumps late in the wait (its leash snapping behind the character), the shown one is still on its way.
        Assert.True(near > 10 && far * 10 <= near, $"{near} waits ended within a unit of the original's camera, {far} further");
    }

    [Fact]
    public void ThePlayersTurnFramesTheCharacterWithItsTargetHidingNeither()
    {
        var (framed, hidden, held) = (0, 0, 0);
        Play(host => Plain(Set, 1, host), CameraStyle.Enhanced, "*=stick:0:1:40/wait:40/accept/accept", run =>
        {
            var battle = run.Battle;
            var e = battle.CombatCamera.Enhanced!;
            if (battle.State != CombatState.Turn || battle.Current is not { Controlled: false } me || e.Target is not { } target || e.Finder is not { } finder)
                return;
            framed++;
            ShotSubject[] both = [new(me.Body.Sphere.Position, me.Body.ScaledRadius), new(target.Body.Sphere.Position, target.Body.ScaledRadius)];
            if (both.All(s => ShotFraming.Holds(e.Goal, s with { Radius = 0 }, CameraTuning.Default.Lens)))
                held++;
            if (finder.HiddenWeight(e.Goal, both) > 0)
                hidden++;
        });
        TestContext.Current.TestOutputHelper?.WriteLine($"{framed} ticks framed with a target: {held} holding both, {hidden} hiding one");
        Assert.True(framed > 200, $"{framed} ticks framed");
        Assert.True(held >= framed * 0.98, $"{held} of {framed} held both");
        Assert.True(hidden <= framed * 0.02, $"{hidden} of {framed} hid one");
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
