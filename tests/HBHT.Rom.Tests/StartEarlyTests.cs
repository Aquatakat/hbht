using System.Globalization;
using System.Text;
using HBHT.Core.Audio;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
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
public sealed partial class StartEarlyTests(Na10Import fixture)
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
        Directed(early);
        Assert.True(early.Watch!.Handed > 0, "the director's camera was never handed to the battle");
        Assert.Equal(BattleOutcome.Won, original.Battle.Outcome);
        Assert.True(original.Battle.NextReserve >= original.Battle.Enemies.Count, "every reserve joined");
    }

    [Fact]
    public void TheBanditsWithTheirBossAndReservesAreDrawnTheSameEitherWay()
    {
        // The user's fight of Nov 10: Human Bandits and their Boss in C03, four on the field and four reserves, the party attacking.
        PartyEnemy Bandit() => new(RecordId.Hex(RecordTypes.Entity, 0x021B), 1, 1);
        var boss = new PartyEnemy(RecordId.Hex(RecordTypes.Entity, 0x021C), 1, 1);
        var (original, early) = Both(host => Plain(Set, 1, host, [Bandit(), Bandit(), Bandit(), boss, Bandit(), Bandit(), Bandit(), Bandit()]), Attacking);
        Same(original, early);
        Directed(early);
    }

    [Fact]
    public void TheChaosCastersBattleIsDrawnTheSameEitherWay()
    {
        var (_, early) = Logged("20261031-123809-setup2/battle-01", Skipping);
        // Its spells cast at several targets: each caster drawn facing the middle of them, or as it stood with them all round it.
        var (middle, round) = early.Watch!.CastsFacedCount;
        Assert.True(middle + round > 0, $"casters faced the middle of their targets {middle} times and stayed as they stood {round}");
    }

    [Fact]
    public void ThePlayersSpellAtSeveralTargetsIsDrawnWithEveryCastAndThroughNoOne()
    {
        // The user's fight with Alaron taught Dragon Flames, cast at every bandit he can each turn (their Nov 11 play). A turn the player
        // plays has its later casts decided as they're recorded, while its first already draws; the watch holds the drawing to no body
        // in two actions' episodes at once, which a cast drawn ahead over its target's own action, still drawing, broke. And every
        // cast's flame is drawn: one visual a target, each started once with its emitters, all showing together.
        var plan = BattlePlan.Parse("Alaron=spell:Dragon Flames/accept/accept/accept/accept/accept/accept;*=skip");
        var flames = Set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Name == "Dragon Flames");
        PartyEnemy Bandit() => new(RecordId.Hex(RecordTypes.Entity, 0x021B), 1, 1);
        var boss = new PartyEnemy(RecordId.Hex(RecordTypes.Entity, 0x021C), 1, 1);
        var (most, started, lit) = (0, new HashSet<SpellFxVisual>(), new HashSet<SpellFxVisual>());
        var early = Play(host =>
        {
            var built = Plain(Set, 1, host, [Bandit(), Bandit(), Bandit(), boss, Bandit(), Bandit(), Bandit(), Bandit()]);
            built.Battle.Combatants.First(c => c.Sheet.Name.StartsWith("Alaron", StringComparison.Ordinal)).Sheet.LearnSpell(flames, 10);
            return built;
        }, CombatTurns.StartEarly, run => run.Input = plan.Tick(run.Drawing) is { } input ? () => input : null, begun: run => run.Each = r =>
        {
            var f = r.Drawing.Frame;
            foreach (var cue in f.Cues.OfType<SpellVisualStarted>())
            {
                if (cue.Visual.Id == flames.Presentation!.Target)
                    Assert.True(started.Add(cue.Visual), "a flame started twice");
            }
            foreach (var e in f.Emitters)
            {
                if (e.Owner is SpellFxVisual v && started.Contains(v) && e.Request == 5)
                {
                    lit.Add(v);
                    Assert.Contains(f.Visuals, drawn => drawn.Visual == v);
                }
            }
            most = Math.Max(most, f.Visuals.Count(v => v.Visual.Id == flames.Presentation!.Target));
        });
        var (casts, parts, _, apart) = early.Watch!.Volleys();
        TestContext.Current.TestOutputHelper?.WriteLine($"{casts} casts at several targets in {parts} parts; {started.Count} flames started, {lit.Count} lit, {most} at once");
        Assert.True(casts > 0 && parts >= 2 * casts, $"{casts} casts in {parts} parts");
        Assert.True(started.Count >= parts && lit.SetEquals(started), $"{started.Count} flames started for {parts} parts, {lit.Count} lit");
        Assert.True(most >= 3, $"{most} flames drawn at once");
        Assert.True(apart.Count == 0, string.Join("\n", apart.Take(10)));
    }

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
        // Early no body drawn through another (the watch checks every drawn tick). The stick reads the camera the player sees, which
        // under Start Early is the director's, so the same stick walks elsewhere, the player's own input: here both read the original's,
        // so their walks compare tick for tick.
        var original = Play(host => Plain(Set, 1, host), CombatTurns.Original, Walking);
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Walking, hand: false);
        Same(original, early);
        Assert.True(early.Watch!.OpenedEarly > 0);
        TestContext.Current.TestOutputHelper?.WriteLine($"{early.Drawing.Composer.Forwarded} actions fast-forwarded on their own");
    }

    [Fact]
    public void UnderStartEarlyTheAimIsHeldTillWhatItCouldPickIsDrawnWhereItIs()
    {
        // The rats, the party attacking: wherever the battle takes a press in an aim of a turn opened early, all it could pick is drawn
        // where it is, and while it isn't the battle is stepped no further (the watch checks).
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Attacking);
        Assert.True(early.Watch!.OpenedEarly > 10, $"{early.Watch.OpenedEarly} player's turns opened early");
        Assert.True(early.Watch.Aimed > 0, $"{early.Watch.Aimed} drawn ticks aimed");
        TestContext.Current.TestOutputHelper?.WriteLine($"{early.Watch.Aimed} drawn ticks aimed in turns opened early, " +
            $"{early.Watch.AimHeld} of them held; {early.Drawing.Composer.Forwarded} actions fast-forwarded on their own");
    }

    [Fact]
    public void AWheneverThePlayerIsHeldChangesOnlyTheDrawing()
    {
        // The rats, the party attacking, A pressed whenever the player's turn waits to open or its aim is held: the battle plays tick
        // for tick as Original's, and the drawing still never draws a body back, in two actions at once, through another, or a one-off
        // twice.
        var original = Play(host => Plain(Set, 1, host), CombatTurns.Original, Attacking);
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Attacking, meanwhile: SkippingHolds);
        Same(original, early);
        Assert.True(early.Watch!.Skipped > 0, "A never skipped a hold");
        TestContext.Current.TestOutputHelper?.WriteLine($"A skipped {early.Watch.Skipped} holds");
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
        Directed(both.Early);
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

        /// <summary>Called after each tick of the drawing, with the frame it drew.</summary>
        public Action<Run>? Each { get; set; }

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
        Action<Run>? meanwhile = null, bool hand = true, Action<Run>? begun = null)
    {
        var audio = GameAudio.Silent(Set);
        var recorder = new BattleRecorder(audio);
        var built = build(recorder);
        var zone = new ZoneEngine(built.Game, built.Streamer);
        zone.Keys.Output = recorder;
        recorder.Keys = zone.Keys;
        var drawing = new BattleDrawing(built.Battle, recorder, null, audio) { Budget = null, TicksAhead = 97, Turns = turns, HandCamera = hand };
        zone.Camera = drawing.Camera;
        var run = new Run { Built = built, Drawing = drawing, Recorder = recorder, Zone = zone, Meanwhile = meanwhile };
        var watch = turns == CombatTurns.StartEarly ? new Watch(drawing) : null;
        run.Watch = watch;
        begun?.Invoke(run);
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
            run.Each?.Invoke(run);
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

    /// <summary>A whenever the player is held: its turn waiting to open, or its aim held.</summary>
    private static void SkippingHolds(Run run)
    {
        if (run.Drawing.Composer.Pending is not null || run.Drawing.AimHeld)
        {
            if (run.Drawing.Press(MenuAction.Accept))
                run.Watch!.Skipped++;
        }
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

        /// <summary>Drawn ticks aimed on which the aim was held, the battle stepped no further.</summary>
        public int AimHeld { get; private set; }

        /// <summary>Holds A skipped.</summary>
        public int Skipped { get; set; }

        /// <summary>Drawn ticks a player's turn waited to open on.</summary>
        public int Pending { get; private set; }

        /// <summary>Player's turns that opened while a beat before them still played, which their shot could hold.</summary>
        public int OpenedOnABeat { get; private set; }

        private readonly HashSet<DrawnAction> _opened = [];
        private int _heldTick = -1;

        /// <summary>Turns the player plays that opened before the drawing had caught up.</summary>
        public int OpenedEarly { get; private set; }

        /// <summary>Drawn frames that a fast-forward passed sounds or emitters over in, and the sound calls it skipped.</summary>
        public int FastForwarded { get; private set; }

        public int SkippedSounds { get; private set; }

        public Dictionary<DrawnAction, int> Starts { get; } = [];

        /// <summary>Ticks the battle stepped with the director's camera handed to it, the stick and cursor read against the camera drawn.</summary>
        public int Handed { get; private set; }

        private DrawnCamera? _drawnCamera;

        public void Recorded(BattleFrame f)
        {
            // The battle read the stick, the cursor and the bodies' sounds against the camera the player saw: the last drawn.
            if (f.Handed)
            {
                Handed++;
                var look = drawing.Battle.Camera.LookXZ;
                Assert.True(_drawnCamera is { } seen && seen.LookXZ == look && seen.Position == drawing.Battle.Camera.Position,
                    $"tick {f.Tick} was read against {look}, but {_drawnCamera?.LookXZ} was drawn");
            }
            _places[f.Tick] = f.Bodies.ToDictionary(b => b.Combatant, b => b.Centre);
            foreach (var cue in f.Cues)
            {
                _recorded[cue] = _recorded.GetValueOrDefault(cue) + 1;
                if (cue is LogLine)
                    _linePortraits[cue] = f.Gui.LogPortrait;
            }
            // What the battle's own screen never does, the drawn one mustn't either.
            Screen(f, _screenRecorded, "recorded");
            foreach (var call in f.Sounds)
                _recorded[call] = _recorded.GetValueOrDefault(call) + 1;
            foreach (var e in f.Emitters)
                _emittersRecorded[(e.Owner, e.Request)] = _emittersRecorded.GetValueOrDefault((e.Owner, e.Request)) + 1;
        }

        /// <summary>
        /// What's wrong with battle screens (the user's Nov 11 play: portraits gone, a frame empty with its bar, portraits flickering,
        /// a line beside another's portrait): of the recorded ones, which never should be; of the drawn ones.
        /// </summary>
        public List<string> ScreenRecorded => _screenRecorded.Wrong;

        public List<string> ScreenDrawn => _screenDrawn.Wrong;

        private readonly ScreenWatch _screenRecorded = new(), _screenDrawn = new();
        private readonly Dictionary<object, RecordId?> _linePortraits = new(ReferenceEqualityComparer.Instance);

        private sealed class ScreenWatch
        {
            public List<string> Wrong { get; } = [];

            /// <summary>The last few layouts shown, each with the tick it was last shown on: a layout come back to soon is a flicker.</summary>
            public List<(string Layout, int At)> Layouts { get; } = [];
        }

        /// <summary>
        /// A battle screen is one moment's: every HP bar on a slot with a portrait, every frame that
        /// holds a slot's portrait having that portrait, and its layout (who has a portrait, where, in play or not) never going back
        /// within a second to one it left.
        /// </summary>
        private static void Screen(BattleFrame f, ScreenWatch w, string kind, int? at = null)
        {
            var g = f.Gui;
            var tick = at ?? f.Tick;
            foreach (var slot in g.HpSlots.Distinct())
            {
                if (!g.Portraits.Any(p => p.Slot == slot))
                    w.Wrong.Add($"{kind} tick {tick}: an HP bar for slot {slot}, which has no portrait");
            }
            foreach (var slot in g.FrameSlots)
            {
                if (slot is { } held && !g.Portraits.Any(p => p.Slot == held))
                    w.Wrong.Add($"{kind} tick {tick}: a frame for slot {held}, which has no portrait");
            }
            // Where each portrait rests, not where it is as it slides.
            var layout = string.Join(" ", g.Portraits.Select(p => $"{(p.Party ? "P" : "E")}{p.Slot?.ToString() ?? "-"}:{p.Width}")) + " | " + string.Join(",", g.FrameSlots);
            if (w.Layouts.Count == 0 || w.Layouts[^1].Layout != layout)
            {
                var before = w.Layouts.FindIndex(l => l.Layout == layout);
                if (before >= 0 && w.Layouts.Count - before <= 8 && w.Layouts.Skip(before + 1).All(l => tick - l.At < 60) && w.Wrong.Count < 50)
                    w.Wrong.Add($"{kind} tick {tick}: the portraits went back to a layout left {w.Layouts.Count - before - 1} layouts ago: {layout}");
                w.Layouts.Add((layout, tick));
                if (w.Layouts.Count > 16)
                    w.Layouts.RemoveAt(0);
            }
            w.Layouts[^1] = (layout, tick);
        }

        private int _screens;

        public void Drew(BattleFrame f)
        {
            _drawnCamera = f.Camera;
            // The screen drawn, by the drawn tick; and the log's line beside the portrait its own tick had.
            Screen(f, _screenDrawn, "drawn", _screens++);
            if (f.Cues.LastOrDefault(c => c is LogLine) is LogLine line && _linePortraits.TryGetValue(line, out var portrait)
                && (f.Gui.Log != line.Text || f.Gui.LogPortrait != portrait))
                _screenDrawn.Wrong.Add($"drawn tick {_screens}: '{f.Gui.Log}' beside {f.Gui.LogPortrait}, where '{line.Text}' was written beside {portrait}");
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
                var holding = now.Count(a => a.Episodes.Any(e => e.Body == c && a.Decided(e.Part) && e.From <= a.TickAt(d, e.Part) && a.TickAt(d, e.Part) <= e.To));
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
            // What a turn the player plays could pick as it aims is drawn where it is whenever the battle takes its presses; while it
            // isn't, the aim is held and the battle steps no further.
            var battle = drawing.Battle;
            if (composer.Live is not null && battle.State is CombatState.TargetAttack or CombatState.TargetThrown or CombatState.TargetSpell
                or CombatState.PlaceArea)
            {
                Aimed++;
                if (drawing.AimHeld)
                {
                    AimHeld++;
                    Assert.False(drawing.TakesPresses);
                    if (_heldTick >= 0)
                        Assert.Equal(_heldTick, drawing.Actions.Last);
                    _heldTick = drawing.Actions.Last;
                }
                else
                {
                    _heldTick = -1;
                    foreach (var target in drawing.Pickable())
                        Assert.True(composer.Free(target), $"{target} could be picked at drawn tick {d} but is still drawn in an earlier action");
                }
            }
            else
            {
                _heldTick = -1;
            }
            if (composer.Pending is not null)
                Pending++;
            // A turn the player plays opens once every beat before it has played, or those still playing can stay in its shot.
            if (composer.Live is { } live && _opened.Add(live))
            {
                var playing = new List<FramedBeat>();
                var last = drawing.Actions.Last;
                foreach (var a in drawing.Actions.Actions)
                {
                    if (a.Index >= live.Index || a.Start is null)
                        continue;
                    foreach (var beat in a.Beats)
                    {
                        // A beat is shown till its end, or with an outcome hold, till the hold's.
                        var hold = composer.Pacing.OutcomeHold;
                        var shown = hold > 0 ? Math.Min(beat.End(last), beat.From + hold - 1) : beat.End(last);
                        if (a.DrawnAt(shown, beat.Part) < d)
                            continue;
                        Assert.True(a.DrawnAt(beat.From, beat.Part) <= d, $"the player's turn opened at drawn tick {d} before {beat} struck");
                        var at = Math.Clamp(a.TickAt(d, beat.Part), beat.From, shown);
                        playing.Add(new FramedBeat(beat, at, _places[at][beat.Subject], null));
                    }
                }
                if (playing.Count > 0)
                {
                    OpenedOnABeat++;
                    Assert.True(composer.Framing.CanHold(d, playing, _places[last][live.Current!]),
                        $"the player's turn opened at drawn tick {d} while {string.Join(", ", playing.Select(p => p.Beat))} played");
                }
            }
            // A turn the player plays opens only once its combatant has drawn its earlier parts (Start Early).
            if (f.State == CombatState.Turn && _state != CombatState.Turn)
            {
                var c = f.Current!;
                var held = drawing.Actions.Actions.Where(a => a.Index < drawing.Actions.Actions.Count - 1 && a.Episodes.Any(e =>
                    e.Body == c && (a.Start is not { } s || s > d || a.TickAt(d, e.Part) <= e.To))).ToList();
                Assert.True(held.Count == 0, $"the player's turn opened at drawn tick {d} while {c} was still drawn in {string.Join(", ", held)}");
                if (!drawing.CaughtUp)
                    OpenedEarly++;
            }
            _state = f.State;
            Direct(f, composer, d);
        }

        private CombatState _state;

        /// <summary>Beats that struck, and those off the shot drawn or hidden in it as they struck.</summary>
        public int Struck { get; private set; }

        /// <summary>The drawn tick each beat struck on.</summary>
        public Dictionary<Beat, int> StruckAt { get; } = [];

        /// <summary>
        /// The spells cast at several targets, and those whose targets' hits didn't strike on one drawn tick though their parts were
        /// shifted to (a part's shift is cut short only where it would start before its action, or on a drawn tick already drawn).
        /// </summary>
        public (int Casts, int Parts, int Together, List<string> Apart) Volleys()
        {
            var (casts, parts, together) = (0, 0, 0);
            var apart = new List<string>();
            foreach (var a in drawing.Actions.Actions)
            {
                if (a.Parts.Count < 2)
                    continue;
                casts++;
                parts += a.Parts.Count;
                var first = a.Parts[0];
                var strikes = new List<(ActionPart Part, int At)>();
                foreach (var part in a.Parts)
                {
                    var beat = a.Beats.FirstOrDefault(b => b.Part == part.Index && b.Subject == part.Target);
                    var aligned = part.Strike is { } s && first.Strike is { } f && part.Shift == s - f;
                    if (beat is not null && aligned && StruckAt.TryGetValue(beat, out var at))
                        strikes.Add((part, at));
                }
                if (strikes.Count < 2)
                    continue;
                together++;
                if (strikes.Select(x => x.At).Distinct().Count() > 1)
                    apart.Add($"{a}: " + string.Join(", ", strikes.Select(x => $"{x.Part} struck at {x.At}")));
            }
            return (casts, parts, together, apart);
        }

        public List<string> OffShot { get; } = [];

        public List<string> HiddenAtStrike { get; } = [];

        /// <summary>Drawn ticks with beats of different actions playing at once, and those whose shot didn't hold them all.</summary>
        public int Overlapped { get; private set; }

        public List<string> OverlapOffShot { get; } = [];

        /// <summary>The camera's most acceleration between cuts, as a share of its caps.</summary>
        public float AimMost { get; private set; }

        public float TurnMost { get; private set; }

        public float ZoomMost { get; private set; }

        private (System.Numerics.Vector3 Aim, float Yaw, float Pitch, float Distance)? _speeds;
        private int _cuts, _lastCut = -1000;
        private Shot? _beforeCut, _shown;
        private DrawnAction? _cutLead;
        private string _cutWhy = "";
        private bool _lastWasBack;

        /// <summary>Cuts back that were themselves cut back to: two shots alternating, the user's flicker.</summary>
        public List<string> Flickers { get; } = [];

        private static bool Near(Shot a, Shot b) =>
            System.Numerics.Vector3.Distance(a.Eye, b.Eye) < 2 && System.Numerics.Vector3.Distance(a.Aim, b.Aim) < 2;
        private readonly List<(FramedBeat Beat, int At)> _outcomes = [];
        private readonly Dictionary<Combatant, System.Numerics.Vector3> _lastDrawn = [];

        /// <summary>How soon after a cut another counts as a flicker.</summary>
        public const int QuickCut = 30;

        /// <summary>Cuts back to about the shot the last cut left, within <see cref="QuickCut"/> of it.</summary>
        public List<string> QuickCuts { get; } = [];

        /// <summary>Results cut away from, or let off the screen, within the outcome hold of striking on it.</summary>
        public List<string> OutcomeCut { get; } = [];

        public List<string> OutcomeLost { get; } = [];

        /// <summary>Drawn ticks a body walked on, and those it was off the screen.</summary>
        public int Moving { get; private set; }

        public List<string> MoverOff { get; } = [];

        public Dictionary<string, int> MoverOffBy { get; } = [];

        /// <summary>How long a walker is held before the camera should have it on the screen, and the ticks one held that long wasn't.</summary>
        public const int WalkerSettle = 60;

        public int WalkerLost { get; private set; }

        private readonly Dictionary<Combatant, int> _heldFor = [];

        /// <summary>Called each drawn tick the director drew, with whether it cut: a camera test's own measure.</summary>
        public Action<BattleFrame, BattleComposer, int, bool>? Trace { get; set; }

        /// <summary>What the director drew on a drawn tick (Start Early).</summary>
        private void Direct(BattleFrame f, BattleComposer composer, int d)
        {
            var director = drawing.Director;
            if (director.Camera.Motion is not { } m || director.Camera.Finder is not { } finder)
                return;
            var lens = director.Tuning.Lens;
            var shot = Shot.Of(f.Camera.Position, f.Camera.Aim, m.Current.Yaw);
            var playing = composer.PlayingNow().ToList();
            foreach (var p in playing)
            {
                if (p.Tick != p.Beat.From)
                    continue;
                Struck++;
                StruckAt.TryAdd(p.Beat, d);
                Faces(f, p.Beat.Action, d);
                var subject = BeatShots.Subject(p);
                if (!ShotFraming.Holds(shot, subject with { Radius = 0 }, lens))
                    OffShot.Add($"{p.Beat} struck off the shot at drawn tick {d} ({director.Camera.Kind}; leading {director.Lead}, live {composer.Live}, walking {director.Camera.Walking})");
                else if (finder.Sight.Hidden(shot.Eye, subject))
                    HiddenAtStrike.Add($"{p.Beat} struck hidden at drawn tick {d} ({director.Camera.Kind}; leading {director.Lead}, live {composer.Live}, walking {director.Camera.Walking})");
            }
            if (playing.Select(p => p.Beat.Action).Distinct().Count() >= 2)
            {
                Overlapped++;
                foreach (var p in playing)
                {
                    if (!ShotFraming.Holds(shot, BeatShots.Subject(p) with { Radius = 0 }, lens))
                    {
                        OverlapOffShot.Add($"{p.Beat} off the shot at drawn tick {d} with {playing.Count - 1} others playing ({director.Camera.Kind})");
                        break;
                    }
                }
            }
            // A cut soon after another flickers (the user's Nov 10 play: two shots alternating every tick).
            var cut = director.Camera.Cuts != _cuts;
            _cuts = director.Camera.Cuts;
            // A flicker: a cut back to about the shot the last cut left, soon after it (the user's Nov 10 play, two shots every tick).
            if (cut)
            {
                var back = d - _lastCut < QuickCut && _beforeCut is { } left && Near(shot, left) && director.Lead == _cutLead;
                if (back && _lastWasBack)
                    Flickers.Add($"cut back again at drawn tick {d}, {d - _lastCut} after the last ({director.Camera.Kind}; leading {director.Lead})");
                _lastWasBack = back;
                if (back)
                    QuickCuts.Add($"cut back at drawn tick {d}, {d - _lastCut} after the last ({director.Camera.Kind}; {director.Camera.Intent.Moved?.Why}; leading {director.Lead}, live {composer.Live}; the cut before: {_cutWhy})");
                _lastCut = d;
                _cutWhy = $"{director.Camera.Kind}, {director.Framed}, beat cuts {director.BeatCuts}, state {f.State}";
                _beforeCut = _shown;
                _cutLead = director.Lead;
            }
            _shown = shot;
            // A result struck on the screen is held there a moment (the outcome hold): no cut, and its body kept on the screen.
            foreach (var p in playing)
            {
                if (p.Tick == p.Beat.From && ShotFraming.Holds(shot, BeatShots.Subject(p) with { Radius = 0 }, lens))
                    _outcomes.Add((p, d));
            }
            _outcomes.RemoveAll(o => d - o.At >= drawing.Composer.Pacing.OutcomeHold);
            foreach (var (o, at) in _outcomes)
            {
                if (d == at)
                    continue;
                // Cut away from: to a shot that doesn't hold it. A cut to one holding it and a result struck since (one action's second
                // hit) keeps it in view.
                var stillThere = playing.Where(x => x.Beat == o.Beat).Select(x => (FramedBeat?)x).FirstOrDefault();
                if (cut && (stillThere is not { } kept || !ShotFraming.Holds(shot, BeatShots.Subject(kept) with { Radius = 0 }, lens)))
                    OutcomeCut.Add($"{o.Beat} struck at drawn tick {at} and was cut away from {d - at} later ({director.Camera.Kind}; {director.Camera.Intent.Moved?.Why}; leading {director.Lead}, live {composer.Live})");
                else if (!cut && stillThere is { } still && !ShotFraming.Holds(shot, BeatShots.Subject(still) with { Radius = 0 }, lens))
                    OutcomeLost.Add($"{o.Beat} struck at drawn tick {at} and left the screen {d - at} later ({director.Camera.Kind}; leading {director.Lead}, live {composer.Live})");
            }
            if (cut)
                _outcomes.RemoveAll(o => o.At != d && !playing.Any(x => x.Beat == o.Beat.Beat && ShotFraming.Holds(shot, BeatShots.Subject(x) with { Radius = 0 }, lens)));
            // A body walking in the drawing is kept on the screen, so the player sees where it goes.
            foreach (var b in f.Bodies)
            {
                var heldNow = director.Walkers.Any(w => w.At == b.Centre);
                _heldFor[b.Combatant] = heldNow ? _heldFor.GetValueOrDefault(b.Combatant) + 1 : 0;
                if (_lastDrawn.TryGetValue(b.Combatant, out var was) && System.Numerics.Vector3.Distance(was, b.Centre) > 0.02f && b.Alpha > 0
                    && b.Combatant != director.Lead?.Current && b.Combatant != composer.Live?.Current)
                {
                    Moving++;
                    if (_heldFor[b.Combatant] > WalkerSettle && !ShotFraming.Holds(shot, new ShotSubject(b.Centre, b.Combatant.Body.ScaledRadius) with { Radius = 0 }, lens))
                    {
                        WalkerLost++;
                    }
                    if (!ShotFraming.Holds(shot, new ShotSubject(b.Centre, b.Combatant.Body.ScaledRadius) with { Radius = 0 }, lens))
                    {
                        var key = $"{(director.Walkers.Any(w => w.At == b.Centre) ? "held" : "not held")}, {System.Text.RegularExpressions.Regex.Replace(director.Camera.Kind, "[0-9]+", "n")}, {director.Framed}, {(composer.Live is null ? "" : "live")}";
                        MoverOffBy[key] = MoverOffBy.GetValueOrDefault(key) + 1;
                    }
                    if (!ShotFraming.Holds(shot, new ShotSubject(b.Centre, b.Combatant.Body.ScaledRadius) with { Radius = 0 }, lens))
                        MoverOff.Add($"{b.Combatant} walked off the screen at drawn tick {d}, {(director.Walkers.Any(w => w.At == b.Centre) ? "held" : "not held")} ({director.Camera.Kind}; {director.Camera.Intent.Moved?.Why}; leading {director.Lead}, live {composer.Live})");
                }
                _lastDrawn[b.Combatant] = b.Centre;
            }
            Trace?.Invoke(f, composer, d, cut);
            var (y, pitch, z) = m.Speeds;
            if (cut || director.Camera.Quick)
            {
                _speeds = null;
                return;
            }
            var caps = m.Caps;
            if (_speeds is { } l)
            {
                AimMost = MathF.Max(AimMost, (m.AimSpeed - l.Aim).Length() * 60 / caps.Aim);
                TurnMost = MathF.Max(TurnMost, MathF.Max(MathF.Abs(y - l.Yaw), MathF.Abs(pitch - l.Pitch)) * 60 * 180 / MathF.PI / caps.Turn);
                ZoomMost = MathF.Max(ZoomMost, MathF.Abs(z - l.Distance) * 60 / caps.Zoom);
            }
            _speeds = (m.AimSpeed, y, pitch, z);
        }

        /// <summary>Casts at several targets whose caster faced the middle of them as they struck, stayed as it was for targets all round, or faced elsewhere.</summary>
        public (int Middle, int Round, List<string> Wrong) CastsFaced { get; } = (0, 0, []);

        private (int Middle, int Round) _castsFaced;
        private readonly HashSet<DrawnAction> _faced = [];

        public (int Middle, int Round) CastsFacedCount => _castsFaced;

        /// <summary>
        /// As a spell cast at several targets strikes: its caster is drawn facing the middle of the arc they spread over about it, where
        /// that's within <see cref="StartEarlyPacing.CastArc"/>.
        /// </summary>
        private void Faces(BattleFrame f, DrawnAction a, int d)
        {
            if (a.Parts.Count < 2 || a.Current is not { } caster || !_faced.Add(a))
                return;
            var me = f.Bodies.FirstOrDefault(b => b.Combatant == caster);
            if (me.Combatant is null)
                return;
            var ways = new List<float>();
            foreach (var target in a.Parts.Select(p => p.Target).Distinct())
            {
                var there = f.Bodies.FirstOrDefault(b => b.Combatant == target);
                if (target is null || target == caster || there.Combatant is null)
                    continue;
                ways.Add(MathF.Atan2(there.Centre.Z - me.Centre.Z, there.Centre.X - me.Centre.X));
            }
            if (ways.Count < 2)
                return;
            ways.Sort();
            var (gap, after) = (MathF.Tau - (ways[^1] - ways[0]), 0);
            for (var i = 1; i < ways.Count; i++)
            {
                if (ways[i] - ways[i - 1] > gap)
                    (gap, after) = (ways[i] - ways[i - 1], i);
            }
            var arc = MathF.Tau - gap;
            if (arc > drawing.Composer.Pacing.CastArc * MathF.PI / 180)
            {
                _castsFaced.Round++;
                return;
            }
            _castsFaced.Middle++;
            var middle = ways[after] + arc / 2;
            var off = MathF.Acos(Math.Clamp(System.Numerics.Vector2.Dot(me.Heading, new(MathF.Cos(middle), MathF.Sin(middle))), -1, 1)) * 180 / MathF.PI;
            if (off > 5)
                CastsFaced.Wrong.Add($"{a}: {caster} faces {off:F0}° off the middle of its {ways.Count} targets as they strike at drawn tick {d}");
        }

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
                        " things " + string.Join(",", a.Things.Select(t => $"{t.Key.GetType().Name}–{t.Value - a.First}")) +
                        " beats " + string.Join(", ", a.Beats.Select(b => $"{b.Kind} {b.Subject.Slot} {b.From - a.First}–{b.To - a.First}")) +
                        " cues " + string.Join(",", Enumerable.Range(a.First, Math.Max(0, Math.Min(a.Last, drawing.Actions.Last) - Math.Max(a.First, drawing.Actions.Kept) + 1)).Select(t => t).Where(t => t >= drawing.Actions.Kept).SelectMany(t => drawing.Actions[t].Cues.OfType<CombatantAnimated>().Select(c => $"{c.Combatant.Slot}:{c.Request}@{t - a.First}"))));
                }
            }
            var beats = drawing.Actions.Actions.Sum(a => a.Beats.Count);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"Start Early: {Overlapping} drawn ticks with two actions' own ticks at once, {SharedEarly} actions began while an earlier one " +
                $"sharing a body still drew, bodies drawn from another tick than the newest action's {Apart} times; {OpenedEarly} player's turns " +
                $"opened before the drawing caught up, {OpenedOnABeat} of them on a beat their shot could hold; {Pending} drawn ticks a turn " +
                $"waited to open; {AimHeld} drawn ticks an aim was held; {beats} beats; the last word fired {drawing.Composer.Resolved} " +
                $"times; {FastForwarded} frames fast-forwarded over {SkippedSounds} sound calls");
        }
    }

    /// <summary>
    /// Start Early's director, through a battle drawn: every beat is in the shot drawn and unhidden as it strikes; wherever beats of
    /// different actions play at once, the shot drawn holds them all, as the composer was told it could; and between cuts the camera's
    /// acceleration stays within its caps.
    /// </summary>
    private static void Directed(Run early)
    {
        var w = early.Watch!;
        var director = early.Drawing.Director;
        TestContext.Current.TestOutputHelper?.WriteLine(string.Create(CultureInfo.InvariantCulture,
            $"Director: {w.Struck} beats struck, {w.OffShot.Count} off the shot, {w.HiddenAtStrike.Count} hidden; {w.Overlapped} drawn ticks " +
            $"with beats of different actions at once, {w.OverlapOffShot.Count} not all on the screen; most acceleration of its cap: aim " +
            $"{w.AimMost:F2}, turn {w.TurnMost:F2}, zoom {w.ZoomMost:F2}; cuts {director.Camera.Cuts}, {director.BeatCuts} to keep a beat; " +
            $"drawn back {director.Widened}, regrouped {director.Regrouped}; the shot question asked {director.Framing.Asked} times, " +
            $"looked into {director.Framing.Looked}; the battle read the camera drawn on {w.Handed} ticks; {w.QuickCuts.Count} cuts within " +
            $"{Watch.QuickCut} ticks of the last; results cut away from in their hold {w.OutcomeCut.Count}, off the screen {w.OutcomeLost.Count}; " +
            $"bodies walked on {w.Moving} ticks, off the screen on {w.MoverOff.Count}, {w.WalkerLost} of them held a second already; walk ends held {director.EndsHeld}, holds {director.Holds} over {director.Held} ticks"));
        foreach (var (k, n) in w.MoverOffBy.OrderByDescending(x => x.Value).Take(12))
            TestContext.Current.TestOutputHelper?.WriteLine($"  walkers off the screen {n}: {k}");
        foreach (var line in w.QuickCuts.Take(15).Concat(w.OutcomeCut.Take(15)).Concat(w.OutcomeLost.Take(10)).Concat(w.MoverOff.Take(15)))
            TestContext.Current.TestOutputHelper?.WriteLine("  " + line);
        // A spell cast at several targets strikes them all on one drawn tick, each on the screen as it strikes (the beats' check above).
        var (casts, parts, together, apart) = w.Volleys();
        TestContext.Current.TestOutputHelper?.WriteLine($"Spells at several targets: {casts}, in {parts} parts; {together} with two or more hits drawn, {apart.Count} of them apart");
        Assert.True(apart.Count == 0, string.Join("\n", apart.Take(10)));
        // The battle screen drawn is one moment's, as every recorded one is, and its log's line stands beside its own portrait.
        TestContext.Current.TestOutputHelper?.WriteLine($"Battle screens wrong: {w.ScreenRecorded.Count} recorded, {w.ScreenDrawn.Count} drawn");
        Assert.True(w.ScreenRecorded.Count == 0, string.Join("\n", w.ScreenRecorded.Take(10)));
        Assert.True(w.ScreenDrawn.Count == 0, string.Join("\n", w.ScreenDrawn.Take(10)));
        // Its caster faces the middle of them as they strike, where they're within an arc of it; all round it, it stays as it stood.
        TestContext.Current.TestOutputHelper?.WriteLine($"Their casters faced the middle of them {w.CastsFacedCount.Middle} times, stayed for targets all round {w.CastsFacedCount.Round}");
        Assert.True(w.CastsFaced.Wrong.Count == 0, string.Join("\n", w.CastsFaced.Wrong.Take(10)));
        Assert.True(w.OffShot.Count == 0, string.Join("\n", w.OffShot.Take(10)));
        Assert.True(w.HiddenAtStrike.Count == 0, string.Join("\n", w.HiddenAtStrike.Take(10)));
        Assert.True(w.OverlapOffShot.Count == 0, string.Join("\n", w.OverlapOffShot.Take(10)));
        // No two shots alternate (the user's flicker, Nov 10), and no result is cut away from, or let off the screen, in its outcome hold.
        Assert.True(w.Flickers.Count == 0, string.Join("\n", w.Flickers.Take(10)));
        Assert.True(w.OutcomeCut.Count == 0, string.Join("\n", w.OutcomeCut.Take(10)));
        Assert.True(w.OutcomeLost.Count == 0, string.Join("\n", w.OutcomeLost.Take(10)));
        Assert.True(w.AimMost <= 1.001f, $"aim {w.AimMost}");
        Assert.True(w.TurnMost <= 1.001f, $"turn {w.TurnMost}");
        Assert.True(w.ZoomMost <= 1.001f, $"zoom {w.ZoomMost}");
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
            var (x, y) = (original.Recorded[i].Split(Handed), early.Recorded[i].Split(Handed));
            if (x[0] != y[0] || x[1] != "handed" && y[1] != "handed" && x[1] != y[1])
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

    /// <summary>
    /// A frame as it's drawn, all but what the cosmetic stream decides, and on a tick the battle's camera was the director's (Start Early, a
    /// turn the player plays), all but what goes by the camera: the camera, the radar's dots and how many sounds were heard.
    /// </summary>
    private static string Digest(BattleFrame f)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture, $"{f.Tick} {f.State} {f.Current?.Slot} {f.Step} fade {f.Fade:R} music {f.MusicHeld} sky {f.SkyDim:R} fog {f.FogLifted}");
        s.Append(" | bodies");
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
        s.Append(CultureInfo.InvariantCulture, $" bars {string.Join(",", f.Gui.HpBars.Select(h => $"{h.Fill.Y}:{h.Fill.Height}"))}");
        s.Append(CultureInfo.InvariantCulture, $" menu {f.Menu is not null} aura {f.Aura}");
        s.Append(CultureInfo.InvariantCulture, $" | emitters {string.Join(",", f.Emitters.Select(e => $"{Owner(e.Owner)}:{e.Request}"))}");
        s.Append(" | cues ").AppendJoin("; ", f.Cues.Select(Cue));
        // What goes by the camera, compared only where neither run's battle had the director's.
        s.Append(Handed);
        if (f.Handed)
            s.Append("handed");
        else
            s.Append(CultureInfo.InvariantCulture, $"camera {V(f.Camera.Position)} {V(f.Camera.Aim)} dots {string.Join(",", f.Gui.RadarDots.Select(d => $"{d.X}:{d.Y}"))} sounds {f.Sounds.Count}");
        return s.ToString();

        static string Owner(object o) => o switch
        {
            Combatant c => $"c{c.Slot}",
            SpellFxVisual v => $"v{v.Id}",
            _ => o.ToString() ?? "",
        };
    }

    /// <summary>What parts what goes by the camera from the rest of a frame's digest.</summary>
    private const string Handed = " || ";

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
