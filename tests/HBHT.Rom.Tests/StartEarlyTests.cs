using System.Globalization;
using System.Text;
using HBHT.Core.Audio;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Ui;
using HBHT.Plugins;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// Combat Turns' Start Early (ADR 0040, stage 2): a battle drawn from its recording plays the same whether its turns are drawn as the
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
        // The player's turn opened only once the drawing had caught up, and the battle waits there for the press.
        Assert.True(early.Drawing.CaughtUp);
        Assert.Equal(CombatState.Turn, early.Battle.State);
        Assert.Equal(CombatState.Turn, early.Drawing.Frame.State);
    }

    [Fact]
    public void UnderStartEarlyAInAnAisWalkOnlyFastForwardsTheDrawing()
    {
        var early = Play(host => Plain(Set, 1, host), CombatTurns.StartEarly, Skipping, stopAtWalk: true);
        Assert.False(early.Drawing.CaughtUp);
        Assert.True(early.Drawing.Frame.Walking);
        var walker = early.Drawing.Frame.Current!;
        var at = walker.Body.Sphere.Position;
        Assert.True(early.Drawing.Press(MenuAction.Accept));
        // The drawing is at the battle's present, and the battle went on as if A weren't pressed: it's where it was.
        Assert.True(early.Drawing.CaughtUp);
        Assert.Equal(early.Recorder.Ticks, early.Drawing.Frame.Tick);
        Assert.Equal(at, walker.Body.Sphere.Position);
        Assert.False(early.Battle.SkipWalk());
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
        public Battle Battle => Built.Battle;
        public List<string> Drawn { get; } = [];
        public int MostAhead { get; set; }
        public bool Paused { get; set; }

        /// <summary>The player's turn the script last acted in, and whether its attack was refused there.</summary>
        public (Combatant? Turn, bool Refused) Acting { get; set; }
    }

    private Run Play(Func<ICombatHost, Built> build, CombatTurns turns, Action<Run> player, bool stopAtFirstTurn = false, bool stopAtWalk = false)
    {
        var audio = GameAudio.Silent(Set);
        var recorder = new BattleRecorder(audio);
        var built = build(recorder);
        var zone = new ZoneEngine(built.Game, built.Streamer);
        zone.Keys.Output = recorder;
        var drawing = new BattleDrawing(built.Battle, recorder, null, audio) { Budget = null, TicksAhead = 97, Turns = turns };
        zone.Camera = drawing.Camera;
        var run = new Run { Built = built, Drawing = drawing, Recorder = recorder };
        run.Drawn.Add(Digest(drawing.Frame));
        for (var tick = 0; tick < Most; tick++)
        {
            if (drawing.CaughtUp && built.Battle.WaitsOnPlayer && !built.Battle.Over)
            {
                if (stopAtFirstTurn && built.Battle.State == CombatState.Turn)
                    return run;
                player(run);
            }
            var step = drawing.Tick(zone, skyDrawn: false, readInput: null);
            run.MostAhead = Math.Max(run.MostAhead, drawing.Ahead);
            run.Drawn.Add(Digest(drawing.Frame));
            if (stopAtWalk && drawing.Frame.Walking && drawing.Ahead > 30)
                return run;
            if (step is BattleStep.Paused)
            {
                // The flee's pause menu rolls up, fleeing.
                built.Battle.Unpause(flee: true);
                run.Paused = true;
            }
            if (step is BattleStep.Ended)
                return run;
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

    /// <summary>The player pauses at the first turn and flees.</summary>
    private static void Fleeing(Run run)
    {
        if (run.Battle.State == CombatState.Turn && !run.Battle.Fade.Fading && !run.Paused)
            run.Drawing.Press(MenuAction.Start);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // What's compared

    private static void Same(Run original, Run early)
    {
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"{original.Drawn.Count} ticks drawn, {original.Battle.Log.Count} lines, {original.Battle.Outcome}; Start Early ran up to {early.MostAhead} ahead");
        Assert.True(early.MostAhead > 0, "Start Early never ran ahead");
        Assert.Equal(0, original.MostAhead);
        var n = Math.Min(original.Drawn.Count, early.Drawn.Count);
        for (var i = 0; i < n; i++)
        {
            if (original.Drawn[i] != early.Drawn[i])
                Assert.Fail($"The drawn frames part at tick {i}:\nOriginal:    {original.Drawn[i]}\nStart Early: {early.Drawn[i]}");
        }
        Assert.Equal(original.Drawn.Count, early.Drawn.Count);
        Assert.Equal(original.Recorder.Ticks, early.Recorder.Ticks);

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
