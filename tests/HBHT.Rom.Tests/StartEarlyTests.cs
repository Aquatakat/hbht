using System.Globalization;
using System.Text;
using HBHT.Core.Audio;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// Combat Turns' Start Early: a battle drawn from its recording plays the same whether its turns are drawn as the
/// original's or started early. Each battle is built twice from the same checkpoint or parity log, and both are stepped a tick of the
/// drawing at a time through <see cref="BattleDrawing"/>, one under Original and one under Start Early, with the player's turns played
/// the same way (pressed as the drawing reaches them, never A in a walk). The drawn frames, tick for tick, are the same (their cues, the
/// bodies and their actors, the visuals, numbers, missile, fade, camera and battle screen), and so are the log, every combatant's health,
/// cell and place, the outcome, and the rules' random streams.
/// </summary>
/// <remarks>
/// The cosmetic stream is left out, and with it what it decides: which of a key's sounds plays, and where a ring's teardrop settles. The
/// arena's map sounds and weather draw on it at the drawing's pace, which Start Early runs the battle ahead of, so their draws interleave
/// with the battle's differently; no rule reads it (the ADR's consequences).
/// </remarks>
public sealed class StartEarlyTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>The most ticks of the drawing a battle is played for.</summary>
    private const int Most = 60 * 60 * 20;

    [Fact]
    public void TheRatsBattleWithItsReservesJoiningIsDrawnTheSameEitherWay()
    {
        // Checkpoint 01's ten rats, four on the field and six reserves, the party attacking every turn it can.
        var (original, early) = Both(host => Plain(Set, 1, host), Attacking);
        Same(original, early);
        Assert.Equal(BattleOutcome.Won, original.Battle.Outcome);
        Assert.True(original.Battle.NextReserve >= original.Battle.Enemies.Count, "every reserve joined");
    }

    [Fact]
    public void TheChaosCastersBattleIsDrawnTheSameEitherWay() => Logged("20261031-123809-setup2/battle-01", Skipping);

    [Fact]
    public void TheSkeletonArchersBattleIsDrawnTheSameEitherWay() => Logged("20261031-230020-setup8/battle-01", Skipping);

    [Fact]
    public void TheTrollsDefeatIsDrawnTheSameEitherWay()
    {
        var (original, _) = Logged("20261031-235130-setup10/battle-01", Skipping);
        Assert.Contains(original.Battle.Outcome, (BattleOutcome[])[BattleOutcome.Lost, BattleOutcome.Spared]);
    }

    [Fact]
    public void FleeingFromTheBanditsIsDrawnTheSameEitherWay()
    {
        var (original, _) = Logged("20261031-190448-setup4/battle-01", Fleeing);
        Assert.Equal(BattleOutcome.Fled, original.Battle.Outcome);
    }

    [Fact]
    public void UnderStartEarlyTheBattleRunsAheadOfTheDrawingAndWaitsForItAtThePlayersTurn()
    {
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Skipping, stopAtFirstTurn: true);
        Assert.True(early.MostAhead > 10, $"it ran {early.MostAhead} ticks ahead");
        // The player's turn opened once its combatant was free in the drawing, and the battle waits there for the press.
        Assert.True(early.Drawing.TakesPresses);
        Assert.True(early.Drawing.Composer.Free(early.Battle.Current!));
        Assert.Equal(CombatState.Turn, early.Battle.State);
        Assert.Equal(CombatState.Turn, early.Drawing.Frame.State);
    }

    [Fact]
    public void UnderStartEarlyAInAnAisWalkOnlyFastForwardsTheDrawing()
    {
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Skipping, stopAtWalk: true);
        var drawing = early.Drawing;
        Assert.False(drawing.CaughtUp);
        Assert.True(drawing.Walking);
        var walker = early.Battle.Current!;
        var at = walker.Body.Sphere.Position;
        var d = drawing.Drawn;
        var before = drawing.Composer.DrawingNow().ToDictionary(a => a, a => a.TickAt(d));
        // Each action drawing in its walk, and the last tick of that walk.
        var walks = before.Where(p => p.Value <= p.Key.Last && drawing.Actions[p.Value].Walking).ToDictionary(p => p.Key, p =>
        {
            var end = p.Value;
            while (end < p.Key.Last && drawing.Actions[end + 1].Walking)
                end++;
            return end;
        });
        Assert.NotEmpty(walks);
        Assert.True(drawing.Press(MenuAction.Accept));
        // Each walk drawing is at its end; an action sharing no body with one is where it was (Start Early).
        foreach (var (a, end) in walks)
            Assert.Equal(end, a.TickAt(d));
        foreach (var (a, tick) in before)
        {
            if (!walks.ContainsKey(a) && !walks.Keys.Any(w => w.Episodes.Any(x => a.Episodes.Any(y => y.Body == x.Body))))
                Assert.Equal(tick, a.TickAt(d));
        }
        // The battle went on as if A weren't pressed: the walker is where it was, and 1.0's skip isn't Start Early's.
        Assert.Equal(at, walker.Body.Sphere.Position);
        Assert.False(early.Battle.SkipWalk());
        // And it plays on to the end, every one-off once, no body going back.
        Finish(early);
    }

    [Fact]
    public void AInEveryAiWalkChangesOnlyTheDrawing()
    {
        // The rats, the party attacking, A pressed in every AI walk drawn under Start Early: the battle plays tick for tick as Original's,
        // and the drawing still never draws a body back, in two actions at once, through another, or a one-off twice.
        var original = Play(host => Plain(Set, 1, host), CombatTurns.Original, Attacking);
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Attacking, meanwhile: SkippingWalks);
        Same(original, early);
        TestContext.Current.TestOutputHelper?.WriteLine($"{early.Drawing.Composer.Forwarded} actions fast-forwarded on their own");
    }

    [Fact]
    public void ThePlayersWalksInTurnsOpenedEarlyAreDrawnThroughNoOne()
    {
        // The rats, each party member walking forward 40 ticks of its turn before attacking: the same battle either way, and under Start
        // Early no body drawn through another (the watch checks every drawn tick).
        var (original, early) = Both(host => Plain(Set, 1, host), Walking);
        Same(original, early);
        Assert.True(early.Watch!.OpenedEarly > 0);
        TestContext.Current.TestOutputHelper?.WriteLine($"{early.Drawing.Composer.Forwarded} actions fast-forwarded on their own");
    }

    [Fact]
    public void UnderStartEarlyAimingAtAnEnemyStillDrawnDrawsItWhereItIs()
    {
        // The rats, the party attacking: wherever a target is aimed at in a turn opened early, it's drawn where it is (the watch checks).
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Attacking);
        Assert.True(early.Watch!.OpenedEarly > 10, $"{early.Watch.OpenedEarly} player's turns opened early");
        Assert.True(early.Watch.Aimed > 0, $"{early.Watch.Aimed} drawn ticks aimed");
        TestContext.Current.TestOutputHelper?.WriteLine($"{early.Watch.Aimed} drawn ticks aimed in turns opened early, " +
            $"{early.Watch.AimForwarded} of them fast-forwarding; {early.Drawing.Composer.Forwarded} actions fast-forwarded on their own");
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Playing a battle both ways

    private (Run Original, Run Early) Logged(string folder, Action<Run> player)
    {
        var path = Path.Combine(Environment.GetEnvironmentVariable("HBHT_PARITY") ?? Path.Combine(TestRoms.RepoRoot(), "out", "parity"), folder);
        if (!Directory.Exists(path))
            Assert.Skip($"No parity log {folder}.");
        var log = Log(path);
        var both = Both(host => FromSetup(Set, log, folder, host: host), player);
        Same(both.Original, both.Early);
        return both;
    }

    private (Run Original, Run Early) Both(Func<ICombatHost, Built> build, Action<Run> player) =>
        (Play(build, CombatTurns.Original, player), Play(build, CombatTurns.StartEarly, player));

    private sealed class Run
    {
        public required Built Built { get; init; }
        public required BattleDrawing Drawing { get; init; }
        public required BattleRecorder Recorder { get; init; }
        public required ZoneEngine Zone { get; init; }
        public Battle Battle => Built.Battle;
        /// <summary>The battle's frames as it recorded them, tick by tick: what the rules did.</summary>
        public List<string> Recorded { get; } = [];

        /// <summary>The session's ticks the battle took to draw.</summary>
        public int Drawn { get; set; }
        public int MostAhead { get; set; }
        public bool Paused { get; set; }

        /// <summary>The longest a tick of the drawing took, stepping and all, in milliseconds.</summary>
        public double Slowest { get; set; }

        /// <summary>What checks the drawing under Start Early.</summary>
        public Watch? Watch { get; set; }

        /// <summary>The player's controls the drawing steps the battle with, if the script holds any.</summary>
        public Func<PlayerInput>? Input { get; set; }

        /// <summary>Ticks of the player's turn the script has walked in.</summary>
        public int TurnTicks { get; set; }

        /// <summary>What the script does while the battle doesn't take its presses (A in an AI's walk).</summary>
        public Action<Run>? Meanwhile { get; set; }

        /// <summary>The player's turn the script last acted in, and whether its attack was refused there.</summary>
        public (Combatant? Turn, bool Refused) Acting { get; set; }
    }

    /// <summary>Plays a run stopped early on to its end, the player attacking.</summary>
    private static void Finish(Run run)
    {
        for (var tick = 0; tick < Most; tick++)
        {
            if (run.Drawing.TakesPresses && run.Battle.WaitsOnPlayer && !run.Battle.Over)
                Attacking(run);
            var step = run.Drawing.Tick(run.Zone, skyDrawn: false, readInput: null);
            run.Watch?.Drew(run.Drawing.Frame);
            if (step is BattleStep.Ended)
            {
                run.Watch?.Done();
                return;
            }
        }
        Assert.Fail("The battle didn't end.");
    }

    private Run Play(Func<ICombatHost, Built> build, CombatTurns turns, Action<Run> player, bool stopAtFirstTurn = false, bool stopAtWalk = false,
        Action<Run>? meanwhile = null)
    {
        var audio = GameAudio.Silent(Set);
        var recorder = new BattleRecorder(audio);
        var built = build(recorder);
        var zone = new ZoneEngine(built.Game, built.Streamer);
        zone.Keys.Output = recorder;
        recorder.Keys = zone.Keys;
        var drawing = new BattleDrawing(built.Battle, recorder, null, audio) { Budget = null, TicksAhead = 97, Turns = turns };
        zone.Camera = drawing.Camera;
        var run = new Run { Built = built, Drawing = drawing, Recorder = recorder, Zone = zone, Meanwhile = meanwhile };
        var watch = turns == CombatTurns.StartEarly ? new Watch(drawing) : null;
        run.Watch = watch;
        run.Recorded.Add(Digest(drawing.Frame));
        watch?.Recorded(drawing.Frame);
        watch?.Drew(drawing.Frame);
        drawing.Recorded = frame =>
        {
            run.Recorded.Add(Digest(frame));
            watch?.Recorded(frame);
        };
        for (var tick = 0; tick < Most; tick++)
        {
            if (drawing.TakesPresses && built.Battle.WaitsOnPlayer && !built.Battle.Over)
            {
                if (stopAtFirstTurn && built.Battle.State == CombatState.Turn)
                    return run;
                player(run);
            }
            else if (!drawing.TakesPresses)
            {
                run.Meanwhile?.Invoke(run);
            }
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var step = drawing.Tick(zone, skyDrawn: false, readInput: run.Input);
            run.Slowest = Math.Max(run.Slowest, clock.Elapsed.TotalMilliseconds);
            run.MostAhead = Math.Max(run.MostAhead, drawing.Ahead);
            run.Drawn++;
            watch?.Drew(drawing.Frame);
            if (stopAtWalk && drawing.Walking && drawing.Ahead > 30)
                return run;
            if (step is BattleStep.Paused)
            {
                // The flee's pause menu rolls up, fleeing.
                built.Battle.Unpause(flee: true);
                run.Paused = true;
            }
            if (step is BattleStep.Ended)
            {
                // The battle's end comes only once the drawing has caught up.
                Assert.True(drawing.CaughtUp, "the battle ended before the drawing caught up");
                watch?.Done();
                return run;
            }
        }
        Assert.Fail($"The battle didn't end in {Most} ticks ({turns}).");
        return run;
    }

    /// <summary>The player skips every turn (Z).</summary>
    private static void Skipping(Run run)
    {
        if (run.Battle.State == CombatState.Turn && !run.Battle.Fade.Fading)
            run.Drawing.Press(MenuAction.TabPrevious);
    }

    /// <summary>The player attacks the target A picks every turn, and skips one whose attack is refused.</summary>
    private static void Attacking(Run run)
    {
        var battle = run.Battle;
        if (battle.Fade.Fading)
            return;
        if (run.Acting.Turn != battle.Current)
            run.Acting = (battle.Current, false);
        switch (battle.State)
        {
            case CombatState.Turn when run.Acting.Refused || !run.Drawing.Press(MenuAction.Accept):
                run.Drawing.Press(MenuAction.TabPrevious);
                break;
            case CombatState.TargetAttack or CombatState.TargetThrown when !run.Drawing.Press(MenuAction.Accept):
                run.Drawing.Press(MenuAction.Back);
                run.Acting = (battle.Current, true);
                break;
        }
    }

    /// <summary>The player walks forward for 40 ticks of each turn, lets go of the stick, then attacks as <see cref="Attacking"/> does.</summary>
    private static void Walking(Run run)
    {
        var battle = run.Battle;
        run.Input = null;
        if (battle.Fade.Fading)
            return;
        if (run.Acting.Turn != battle.Current)
        {
            run.Acting = (battle.Current, false);
            run.TurnTicks = 0;
        }
        if (battle.State == CombatState.Turn && run.TurnTicks < 42)
        {
            if (++run.TurnTicks <= 40)
                run.Input = () => new PlayerInput(new System.Numerics.Vector2(0, 1));
            return;
        }
        Attacking(run);
    }

    /// <summary>A in every AI's walk the drawing shows, while the battle doesn't take presses.</summary>
    private static void SkippingWalks(Run run)
    {
        if (run.Drawing.Walking)
            Assert.True(run.Drawing.Press(MenuAction.Accept));
    }

    /// <summary>The player pauses at the first turn and flees.</summary>
    private static void Fleeing(Run run)
    {
        if (run.Battle.State == CombatState.Turn && !run.Battle.Fade.Fading && !run.Paused)
            run.Drawing.Press(MenuAction.Start);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // What's checked of the drawing under Start Early

    /// <summary>
    /// Checks a battle's drawing under Start Early as it's drawn (Start Early): every body is drawn from a tick of the recording
    /// no earlier than the last it was drawn from, as the recording has it there; no body is in two actions' episodes at once; no two
    /// bodies drawn from different ticks come nearer than either tick recorded them, where they touch; a turn the player plays shows only
    /// once the drawing has caught up; and every cue, sound call and emitter start recorded is played once.
    /// </summary>
    private sealed class Watch(BattleDrawing drawing)
    {
        private readonly Dictionary<int, Dictionary<Combatant, System.Numerics.Vector3>> _places = [];
        private readonly Dictionary<object, int> _recorded = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<object, int> _drawn = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(object, int), int> _emittersRecorded = [], _emittersDrawn = [];
        private readonly Dictionary<Combatant, int> _ticks = [];
        private readonly HashSet<DrawnAction> _started = [];

        /// <summary>Drawn ticks on which two actions drew their own ticks at once.</summary>
        public int Overlapping { get; private set; }

        /// <summary>Actions that started while an earlier one they share a body with still drew its own ticks.</summary>
        public int SharedEarly { get; private set; }

        /// <summary>Drawn ticks on which a body was drawn from a different tick than the newest action's.</summary>
        public int Apart { get; private set; }

        /// <summary>Drawn ticks of a turn the player plays, opened early, with a target aimed at, each target drawn where it is.</summary>
        public int Aimed { get; private set; }

        /// <summary>Drawn ticks aimed on which something was fast-forwarded first.</summary>
        public int AimForwarded { get; private set; }

        private int _forwarded;

        /// <summary>Turns the player plays that opened before the drawing had caught up.</summary>
        public int OpenedEarly { get; private set; }

        /// <summary>Drawn frames that a fast-forward passed sounds or emitters over in, and the sound calls it skipped.</summary>
        public int FastForwarded { get; private set; }

        public int SkippedSounds { get; private set; }

        public Dictionary<DrawnAction, int> Starts { get; } = [];

        public void Recorded(BattleFrame f)
        {
            _places[f.Tick] = f.Bodies.ToDictionary(b => b.Combatant, b => b.Centre);
            foreach (var cue in f.Cues)
                _recorded[cue] = _recorded.GetValueOrDefault(cue) + 1;
            foreach (var call in f.Sounds)
                _recorded[call] = _recorded.GetValueOrDefault(call) + 1;
            foreach (var e in f.Emitters)
                _emittersRecorded[(e.Owner, e.Request)] = _emittersRecorded.GetValueOrDefault((e.Owner, e.Request)) + 1;
        }

        public void Drew(BattleFrame f)
        {
            foreach (var cue in f.Cues)
                _drawn[cue] = _drawn.GetValueOrDefault(cue) + 1;
            foreach (var call in f.Sounds)
                _drawn[call] = _drawn.GetValueOrDefault(call) + 1;
            foreach (var e in f.Emitters)
                _emittersDrawn[(e.Owner, e.Request)] = _emittersDrawn.GetValueOrDefault((e.Owner, e.Request)) + 1;
            // What a fast-forward passed over is drawn once too, its sounds and emitters skipped.
            foreach (var call in f.SkippedSounds)
            {
                _drawn[call] = _drawn.GetValueOrDefault(call) + 1;
                SkippedSounds++;
            }
            foreach (var e in f.SkippedEmitters)
                _emittersDrawn[(e.Owner, e.Request)] = _emittersDrawn.GetValueOrDefault((e.Owner, e.Request)) + 1;
            if (f.SkippedSounds.Count > 0 || f.SkippedEmitters.Count > 0)
                FastForwarded++;

            var composer = drawing.Composer;
            var now = composer.DrawingNow().ToList();
            var d = drawing.Drawn;
            var ticks = new Dictionary<Combatant, int>();
            foreach (var b in f.Bodies)
            {
                var c = b.Combatant;
                // Each body is drawn from its own tick, never an earlier one than before, as the recording has it there.
                var t = composer.DrawnTickOf(c) ?? throw new Xunit.Sdk.XunitException($"{c} is drawn but has no tick");
                if (_ticks.TryGetValue(c, out var before))
                    Assert.True(t >= before, $"{c} went back from tick {before} to {t} at drawn tick {d}");
                _ticks[c] = t;
                ticks[c] = t;
                Assert.Equal(_places[t][c], b.Centre);
                if (t != f.Tick)
                    Apart++;
                // No body is in two actions' episodes at once.
                var holding = now.Count(a => a.Episodes.Any(e => e.Body == c && e.From <= a.TickAt(d) && a.TickAt(d) <= e.To));
                Assert.True(holding <= 1, $"{c} is in {holding} actions' episodes at drawn tick {d}: " + string.Join("; ", now.Where(a =>
                    a.Episodes.Any(e => e.Body == c)).Select(a => $"{a} start {a.Start} at {a.TickAt(d)} tail {a.TailEnd} episodes " +
                    string.Join(",", a.Episodes.Where(e => e.Body == c).Select(e => $"{e.From}-{e.To}")))));
            }
            // No two bodies drawn from different ticks touch nearer than either tick had them.
            for (var i = 0; i < f.Bodies.Length; i++)
            {
                for (var j = i + 1; j < f.Bodies.Length; j++)
                {
                    var (x, y) = (f.Bodies[i], f.Bodies[j]);
                    var (tx, ty) = (ticks[x.Combatant], ticks[y.Combatant]);
                    if (tx == ty)
                        continue;
                    var reach = x.Combatant.Body.Sphere.Radius + y.Combatant.Body.Sphere.Radius;
                    var apart = System.Numerics.Vector3.Distance(x.Centre, y.Centre);
                    if (apart >= reach)
                        continue;
                    var then = Math.Min(Between(x.Combatant, y.Combatant, tx), Between(x.Combatant, y.Combatant, ty));
                    Assert.True(apart >= then - BattleComposer.Slack - 0.01f,
                        $"At drawn tick {d}, {x.Combatant} (tick {tx}) and {y.Combatant} (tick {ty}) are drawn {apart:F3} apart, " +
                        $"where the recording had them {then:F3}");
                }
            }
            if (now.Count(a => a.First <= a.TickAt(d) && a.TickAt(d) <= a.Last) >= 2)
                Overlapping++;
            foreach (var a in now)
            {
                if (!_started.Add(a))
                    continue;
                Starts[a] = d;
                foreach (var e in now)
                {
                    if (e.Index < a.Index && e.TickAt(d) <= e.Last
                        && a.Episodes.Any(x => e.Episodes.Any(y => y.Body == x.Body)))
                    {
                        SharedEarly++;
                        break;
                    }
                }
            }
            // What a turn the player plays aims at is drawn where it is: no earlier action still draws it.
            var battle = drawing.Battle;
            if (composer.Live is not null && battle.State is CombatState.TargetAttack or CombatState.TargetThrown or CombatState.TargetSpell)
            {
                var set = battle.State == CombatState.TargetSpell ? battle.TargetsB : battle.TargetsA;
                foreach (var slot in set.SelectedSlots.Append(set.Cursor))
                {
                    if (slot >= 0 && slot < battle.Slots.Count && battle.Slots[slot] is { } target)
                        Assert.True(composer.Free(target), $"{target} is aimed at at drawn tick {d} but still drawn in an earlier action");
                }
                Aimed++;
                if (composer.Forwarded > _forwarded)
                    AimForwarded++;
            }
            _forwarded = composer.Forwarded;
            // A turn the player plays opens only once its combatant has drawn its earlier parts (Start Early).
            if (f.State == CombatState.Turn && _state != CombatState.Turn)
            {
                var c = f.Current!;
                var held = drawing.Actions.Actions.Where(a => a.Index < drawing.Actions.Actions.Count - 1 && a.Episodes.Any(e =>
                    e.Body == c && (a.Start is not { } s || s > d || a.TickAt(d) <= e.To))).ToList();
                Assert.True(held.Count == 0, $"the player's turn opened at drawn tick {d} while {c} was still drawn in {string.Join(", ", held)}");
                if (!drawing.CaughtUp)
                    OpenedEarly++;
            }
            _state = f.State;
        }

        private CombatState _state;

        private float Between(Combatant x, Combatant y, int t) =>
            _places[t].TryGetValue(x, out var a) && _places[t].TryGetValue(y, out var b) ? System.Numerics.Vector3.Distance(a, b) : float.PositiveInfinity;

        /// <summary>The battle is over: every cue, sound call and emitter start recorded was played once.</summary>
        public void Done()
        {
            foreach (var (item, n) in _recorded)
                Assert.True(n == 1 && _drawn.GetValueOrDefault(item) == 1, $"{item} was recorded {n} times and played {_drawn.GetValueOrDefault(item)}");
            Assert.Equal(_recorded.Count, _drawn.Count);
            foreach (var (start, n) in _emittersRecorded)
                Assert.True(_emittersDrawn.GetValueOrDefault(start) == n, $"{start}'s emitters were started {n} times and drawn {_emittersDrawn.GetValueOrDefault(start)}");
            Assert.Equal(_emittersRecorded.Count, _emittersDrawn.Count);
            if (Environment.GetEnvironmentVariable("HBHT_ACTIONS") is not null)
            {
                foreach (var a in drawing.Actions.Actions.Take(60))
                {
                    TestContext.Current.TestOutputHelper?.WriteLine(
                        $"  {a} ({a.Last - a.First + 1}) drawn from {(Starts.TryGetValue(a, out var s) ? s : -1)}: " +
                        string.Join(", ", a.Episodes.Select(e => $"{e.Body.Slot} {e.From - a.First}–{e.To - a.First}")) +
                        " things " + string.Join(",", a.Things.Select(t => $"{t.Key.GetType().Name}–{t.Value - a.First}")));
                }
            }
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Start Early: {Overlapping} drawn ticks with two actions' own ticks at once, {SharedEarly} actions began while an earlier one " +
                $"sharing a body still drew, bodies drawn from another tick than the newest action's {Apart} times; {OpenedEarly} player's turns " +
                $"opened before the drawing caught up; {FastForwarded} frames fast-forwarded over {SkippedSounds} sound calls");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // What's compared

    private static void Same(Run original, Run early)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{original.Recorded.Count} ticks recorded, {original.Battle.Log.Count} lines, {original.Battle.Outcome}; Start Early ran up to " +
            $"{early.MostAhead} ahead; drawn in {original.Drawn} ticks under Original and {early.Drawn} under Start Early, " +
            $"{100.0 * (original.Drawn - early.Drawn) / original.Drawn:F1}% shorter; the slowest tick took {original.Slowest:F1} ms under " +
            $"Original and {early.Slowest:F1} ms under Start Early, {early.Drawing.SlowestCompose.TotalMilliseconds:F1} ms of it composing");
        Assert.True(early.MostAhead > 0, "Start Early never ran ahead");
        Assert.Equal(0, original.MostAhead);
        var n = Math.Min(original.Recorded.Count, early.Recorded.Count);
        for (var i = 0; i < n; i++)
        {
            if (original.Recorded[i] != early.Recorded[i])
                Assert.Fail($"The recorded frames part at tick {i}:\nOriginal:    {original.Recorded[i]}\nStart Early: {early.Recorded[i]}");
        }
        Assert.Equal(original.Recorded.Count, early.Recorded.Count);
        Assert.Equal(original.Recorder.Ticks, early.Recorder.Ticks);
        // Under Original each tick is drawn as it's recorded; under Start Early the actions overlap, so it's drawn in no more.
        Assert.Equal(original.Recorded.Count - 1, original.Drawn);
        Assert.True(early.Drawn <= original.Drawn, $"Start Early drew in {early.Drawn} ticks, Original in {original.Drawn}");

        var (a, b) = (original.Battle, early.Battle);
        Assert.Equal(a.State, b.State);
        Assert.Equal(a.Outcome, b.Outcome);
        Assert.Equal(a.Log, b.Log);
        Assert.Equal(a.Slots.Select(Standing), b.Slots.Select(Standing));
        var (ra, rb) = (original.Built.Game.Random, early.Built.Game.Random);
        Assert.Equal((ra.Combat.Seed, ra.Sort.Seed, ra.Animation.Seed, ra.Loot.Seed, ra.World.Seed),
            (rb.Combat.Seed, rb.Sort.Seed, rb.Animation.Seed, rb.Loot.Seed, rb.World.Seed));

        static string Standing(Combatant? c) =>
            c is null ? "-" : $"{c} hp {c.Sheet.Hp}/{c.Sheet.MaxHp} cell {c.Cell} at {V(c.Body.Sphere.Position)} freed {c.Freed}";
    }

    /// <summary>A frame as it's drawn, all but what the cosmetic stream decides.</summary>
    private static string Digest(BattleFrame f)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{f.Tick} {f.State} {f.Current?.Slot} {f.Step} fade {f.Fade:R} music {f.MusicHeld} sky {f.SkyDim:R} fog {f.FogLifted}");
        s.Append(CultureInfo.InvariantCulture, $" camera {V(f.Camera.Position)} {V(f.Camera.Aim)} | bodies");
        foreach (var b in f.Bodies)
        {
            s.Append(CultureInfo.InvariantCulture, $" [{b.Combatant.Slot} {V(b.Centre)} {b.Heading.X:R},{b.Heading.Y:R} a{b.Alpha} t{b.Tint.Level:R}/{b.Tint.Rate:R}");
            s.Append(CultureInfo.InvariantCulture, $" {b.Unfogged} s{b.ActorState} c{b.ActorClock} {b.Arm?.Scene} {b.Hand?.Scene} {b.Notes?.Scene}]");
        }
        s.Append(" | visuals");
        foreach (var v in f.Visuals)
            s.Append(CultureInfo.InvariantCulture, $" [{v.Visual.Id} {v.Visual.Role} {V(v.Position)} {v.Showing} {v.Alpha} s{v.ActorState} c{v.ActorClock}]");
        s.Append(" | numbers");
        foreach (var n in f.Numbers)
            s.Append(CultureInfo.InvariantCulture, $" [{n.Number.Slot} {n.Number.Amount} {n.Ticks}]");
        if (f.Missile is { } m)
            s.Append(CultureInfo.InvariantCulture, $" | missile {m.Flight.Scene} {V(m.Drawn)}");
        s.Append(CultureInfo.InvariantCulture, $" | aim {f.Aim} ring {f.Ring.Length} {f.RingShown} | log {f.Gui.Log} ({f.Gui.LogPortrait})");
        s.Append(CultureInfo.InvariantCulture, $" dots {string.Join(",", f.Gui.RadarDots.Select(d => $"{d.X}:{d.Y}"))}");
        s.Append(CultureInfo.InvariantCulture, $" bars {string.Join(",", f.Gui.HpBars.Select(h => $"{h.Fill.Y}:{h.Fill.Height}"))}");
        s.Append(CultureInfo.InvariantCulture, $" menu {f.Menu is not null} aura {f.Aura}");
        s.Append(CultureInfo.InvariantCulture, $" | sounds {f.Sounds.Count} emitters {string.Join(",", f.Emitters.Select(e => $"{Owner(e.Owner)}:{e.Request}"))}");
        s.Append(" | cues ").AppendJoin("; ", f.Cues.Select(Cue));
        return s.ToString();

        static string Owner(object o) => o switch
        {
            Combatant c => $"c{c.Slot}",
            SpellFxVisual v => $"v{v.Id}",
            _ => o.ToString() ?? "",
        };
    }

    private static string Cue(CombatCue cue) => cue switch
    {
        CombatantJoined j => $"joined {j.Combatant.Slot}",
        CombatantAnimated a => $"animated {a.Combatant.Slot} {a.Request}",
        CombatantDied d => $"died {d.Combatant.Slot}",
        CombatantGone g => $"gone {g.Combatant.Slot} {g.Fled}",
        NumberShown n => $"number {n.Number.Slot} {n.Number.Amount} {n.Number.Healing}",
        NumberGone n => $"number gone {n.Number.Slot} {n.Number.Amount}",
        MissileLaunched m => $"missile {m.Flight.Scene} {V(m.Flight.From)}",
        MissileLanded => "landed",
        SpellVisualStarted v => $"visual {v.Visual.Id} {v.Visual.Role} {v.Visual.Owner.Slot}",
        SpellVisualEnded v => $"visual ended {v.Visual.Id} {v.Visual.Owner.Slot}",
        LogLine l => $"log {l.Text}",
        _ => cue.ToString(),
    };

    private static string V(System.Numerics.Vector3 v) => string.Create(CultureInfo.InvariantCulture, $"{v.X:R},{v.Y:R},{v.Z:R}");
}
