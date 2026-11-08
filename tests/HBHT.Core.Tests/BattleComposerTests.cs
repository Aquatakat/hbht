using System.Numerics;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

/// <summary>
/// Start Early's scheduling (ADR 0040's stage 3, <see cref="BattleComposer"/>) over made-up recordings: bodies walking in straight lines
/// over made-up turns, recorded whole as the run-ahead records them, then drawn a tick at a time.
/// </summary>
public sealed class BattleComposerTests
{
    private readonly Combatant _a = BattleFixtures.Fighter(0, 10), _b = BattleFixtures.Fighter(1, 10),
        _c = BattleFixtures.Fighter(4, 10), _d = BattleFixtures.Fighter(5, 10);

    [Fact]
    public void TwoActionsWithNoBodyOrPathInCommonAreDrawnAtOnce()
    {
        // A walks on the left, then C walks on the right; D's turn closes C's.
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, new(-10, 0, -5)).Place(_b, new(0, 0, 20)).Place(_c, new(10, 0, -5)).Place(_d, new(0, 0, -20));
        script.Walk(_a, new(-10, 0, 5), 100);
        script.Walk(_c, new(10, 0, 5), 100);
        script.Stand(_d, 10);

        var drawn = Draw(script);
        // C's walk starts as soon as it's scheduled, on the first drawn tick, as A's does.
        Assert.Equal(1, drawn.Starts[1]);
        Assert.True(drawn.Ticks < 150, $"drawn in {drawn.Ticks} ticks");
    }

    [Fact]
    public void AnActionSharingABodyWaitsOnlyTillThatBodysPartIsDrawn()
    {
        // A walks up to B and hits it at tick 60, then stands to its turn's end at 149. C, far off, hits B 30 ticks into its turn.
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, new(0, 0, -5)).Place(_b, new(2, 0, 0)).Place(_c, new(20, 0, 0)).Place(_d, new(-20, 0, 0));
        script.Walk(_a, new(0, 0, 0), 60);
        script.Cue(new CombatantAnimated(_b, 7));
        script.Stand(_a, 89);
        script.Stand(_c, 30);
        script.Cue(new CombatantAnimated(_b, 7));
        script.Stand(_c, 69);
        script.Stand(_d, 10);

        var drawn = Draw(script);
        // B's part in A's turn is drawn on tick 60 (A's turn started at 0), so C's turn starts as B's part in it, 30 ticks in, comes
        // after: on 31, not at A's turn's end.
        Assert.Equal(0, drawn.Starts[0]);
        Assert.Equal(31, drawn.Starts[1]);
        var episodes = drawn.Actions.EpisodesOf(_b);
        Assert.Equal(2, episodes.Count);
        Assert.True(drawn.Starts[1] + (episodes[1].From - episodes[1].Action.First) > drawn.Starts[0] + (episodes[0].To - episodes[0].Action.First));
    }

    [Fact]
    public void CrossingWalksAreDrawnOneAfterTheOther()
    {
        // A walks north across the middle, then C walks east across it; in the recording C crosses once A has gone.
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, new(0, 0, -5)).Place(_b, new(0, 0, 20)).Place(_c, new(-5, 0, 0)).Place(_d, new(0, 0, -20));
        script.Walk(_a, new(0, 0, 5), 100);
        script.Walk(_c, new(5, 0, 0), 100);
        script.Stand(_d, 10);

        var drawn = Draw(script);
        // C starts while A still walks, but late enough that A has cleared the crossing as C reaches it.
        Assert.InRange(drawn.Starts[1], 2, 99);
        var reach = _a.Body.Sphere.Radius + _c.Body.Sphere.Radius;
        foreach (var places in drawn.Places)
            Assert.True(Vector3.Distance(places[_a], places[_c]) >= reach, $"A and C drawn {Vector3.Distance(places[_a], places[_c]):F2} apart");
        // Started as early as that allows: a tick sooner and they'd touch.
        Assert.InRange(drawn.Starts[1], 20, 30);
    }

    [Fact]
    public void AnIdleBodyIsDrawnFromItsOwnTicksNeverBackwards()
    {
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, new(-10, 0, -5)).Place(_b, new(0, 0, 20)).Place(_c, new(10, 0, -5)).Place(_d, new(0, 0, -20));
        script.Walk(_a, new(-10, 0, 5), 100);
        script.Walk(_c, new(10, 0, 5), 100);
        script.Walk(_a, new(-10, 0, -5), 100);
        script.Stand(_d, 10);

        var composer = new BattleComposer(script.Frames[0], () => true);
        foreach (var f in script.Frames.Skip(1))
            composer.Record(f);
        var last = new Dictionary<Combatant, int>();
        while (!composer.CaughtUp)
        {
            var frame = composer.Next();
            foreach (var body in frame.Bodies)
            {
                var t = composer.DrawnTickOf(body.Combatant)!.Value;
                Assert.True(t >= last.GetValueOrDefault(body.Combatant), $"{body.Combatant} went back to tick {t}");
                last[body.Combatant] = t;
                Assert.Equal(script.Frames[t].Bodies.Single(b => b.Combatant == body.Combatant).Centre, body.Centre);
            }
        }
        // A's second walk shares A with its first, so it waits for it; C's runs alongside both.
        Assert.True(composer.Drawn < 300);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Stage 4: the player's turns open early

    [Fact]
    public void APlayersTurnOpensWhileOthersStillDrawOnceItsCombatantIsFree()
    {
        // C walks on the right; then A's turn, which C's walk doesn't touch, waits on the player.
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, new(-10, 0, -5)).Place(_b, new(0, 0, 20)).Place(_c, new(10, 0, -5)).Place(_d, new(0, 0, -20));
        script.Walk(_c, new(10, 0, 5), 100);
        script.Stand(_a, 1, CombatState.Turn);

        var composer = Recorded(script);
        var opened = OpenedAt(composer);
        // It opens at once, C's walk drawing on behind it, and the player's turn is drawn on the present.
        Assert.InRange(opened, 1, 2);
        Assert.False(composer.CaughtUp);
        Assert.True(composer.AtPresent);
        Assert.Equal(script.Frames.Count - 1, composer.Frame.Tick);
        Assert.True(composer.DrawnTickOf(_c) < 10);
        Assert.True(composer.Free(_a));
    }

    [Fact]
    public void APlayersTurnWaitsTillItsCombatantsPartInAnEarlierActionIsDrawn()
    {
        // C walks up to A and hits it 100 ticks in, then stands 40; then A's turn.
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, new(0, 0, 0)).Place(_b, new(0, 0, 20)).Place(_c, new(0, 0, -10)).Place(_d, new(-20, 0, 0));
        script.Walk(_c, new(0, 0, -1.2f), 99);
        script.Cue(new CombatantAnimated(_a, 7));
        script.Stand(_c, 40);
        script.Stand(_a, 1, CombatState.Turn);

        var composer = Recorded(script);
        var opened = OpenedAt(composer);
        // Not before A's hit is drawn (C's action draws tick 99 on drawn tick 99), but before C's standing is.
        Assert.InRange(opened, 100, 102);
        Assert.False(composer.CaughtUp);
    }

    [Fact]
    public void AimingAtABodyStillDrawingFastForwardsOnlyTheActionsHoldingIt()
    {
        // C walks far off on the left for 200 ticks; D walks up to B and hits it; then A's turn, aiming at B.
        var script = new Script(_c, _d, _a, _b);
        script.Place(_a, new(0, 0, -10)).Place(_b, new(4, 0, 10)).Place(_c, new(-15, 0, -10)).Place(_d, new(15, 0, 10));
        script.Walk(_c, new(-15, 0, 10), 200);
        script.Walk(_d, new(5.2f, 0, 10), 100);
        script.Cue(new CombatantAnimated(_b, 7));
        script.Stand(_d, 30);
        script.Stand(_a, 1, CombatState.Turn);

        var composer = Recorded(script);
        var played = new List<CombatCue>();
        OpenedAt(composer, played);
        var actions = composer.Actions.Actions;
        var (cs, ds) = (actions[0], actions[1]);
        Assert.True(cs.Current == _c && ds.Current == _d);
        var d = composer.Drawn;
        var cAt = cs.TickAt(d);
        Assert.False(composer.Free(_b));

        composer.Release([_b]);
        var frame = composer.Next();
        played.AddRange(frame.Cues);
        // D's action, which holds B, is drawn to its end and B with it; C's walk draws on a tick at a time.
        Assert.True(ds.TickAt(composer.Drawn) > ds.TailEnd);
        Assert.True(composer.Free(_b));
        Assert.True(composer.DrawnTickOf(_b) >= composer.Actions.EpisodesOf(_b)[^1].To);
        Assert.Equal(cAt + 1, cs.TickAt(composer.Drawn));
        // The hit on B is handed over as it's passed, once.
        Assert.Single(played, c => c is CombatantAnimated { Request: 7 } hit && hit.Combatant == _b);
        Assert.Contains(_b, frame.Jumped);
    }

    [Fact]
    public void ThePlayersWalkFastForwardsABodyDrawnBehindBeforeItsReached()
    {
        // C walks slowly north across x = 5; then A's turn, A walking east toward C's path from the start.
        var script = new Script(_c, _a, _b, _d);
        script.Place(_a, new(0, 0, 0)).Place(_b, new(-20, 0, 20)).Place(_c, new(5, 0, -10)).Place(_d, new(-20, 0, -20));
        script.Walk(_c, new(5, 0, 10), 300);
        script.Stand(_a, 1, CombatState.Turn);

        var composer = Recorded(script);
        var cs = composer.Actions.Actions[0];
        var reach = _a.Body.Sphere.Radius + _c.Body.Sphere.Radius;
        int? released = null;
        var last = new Dictionary<Combatant, int>();
        for (var i = 0; i < 400; i++)
        {
            // The battle steps with the drawing once the player's turn is drawn on the present: A walks east.
            if (composer.AtPresent)
            {
                var a = script.At(_a);
                composer.Record(script.Step(_a, _a, a.X < 5 ? a + new Vector3(0.05f, 0, 0) : a, CombatState.Turn));
            }
            var frame = composer.Next();
            var (pa, pc) = (Place(frame, _a), Place(frame, _c));
            Assert.True(Vector3.Distance(pa, pc) >= reach, $"A and C drawn {Vector3.Distance(pa, pc):F2} apart at drawn tick {composer.Drawn}");
            if (released is null && cs.TickAt(composer.Drawn) > cs.TailEnd)
                released = composer.Drawn;
            foreach (var body in frame.Bodies)
            {
                var t = composer.DrawnTickOf(body.Combatant)!.Value;
                Assert.True(t >= last.GetValueOrDefault(body.Combatant), $"{body.Combatant} went back to tick {t}");
                last[body.Combatant] = t;
            }
        }
        // C's walk was drawn to its end as A came near its path, before A reached it, and C stands where the core has it.
        Assert.NotNull(released);
        Assert.InRange(released.Value, 50, 99);
        Assert.Equal(new Vector3(5, 0, 10), Place(composer.Frame, _c));

        static Vector3 Place(BattleFrame f, Combatant c) => f.Bodies.Single(b => b.Combatant == c).Centre;
    }

    [Fact]
    public void AInAnAisWalkFastForwardsOnlyTheWalksDrawingToTheirEnds()
    {
        // C walks for 60 ticks (state 5), then stands 60; D's turn, far off, stands 150 ticks; then A's.
        var script = new Script(_c, _d, _a, _b);
        script.Place(_a, new(0, 0, -20)).Place(_b, new(0, 0, 20)).Place(_c, new(-10, 0, 0)).Place(_d, new(10, 0, 0));
        script.Walk(_c, new(-10, 0, 6), 60, CombatState.Moving);
        script.Stand(_c, 60);
        script.Stand(_d, 150);
        script.Stand(_a, 10);

        var composer = Recorded(script);
        for (var i = 0; i < 10; i++)
            composer.Next();
        var (cs, ds) = (composer.Actions.Actions[0], composer.Actions.Actions[1]);
        var d = composer.Drawn;
        var (cAt, dAt) = (cs.TickAt(d), ds.TickAt(d));
        Assert.True(composer.Walking);
        Assert.True(ds.Start <= d && dAt <= ds.Last, "D's turn draws alongside C's walk");

        composer.SkipWalks();
        // C's walk is at its last tick, D's turn where it was; then both play on.
        Assert.True(script.Frames[59].Walking && !script.Frames[60].Walking);
        Assert.Equal(59, cs.TickAt(d));
        Assert.Equal(dAt, ds.TickAt(d));
        var frame = composer.Next();
        Assert.Equal(60, cs.TickAt(composer.Drawn));
        Assert.Equal(dAt + 1, ds.TickAt(composer.Drawn));
        Assert.Equal(new Vector3(-10, 0, 6), frame.Bodies.Single(b => b.Combatant == _c).Centre);
        Assert.False(composer.Walking);
    }

    private static BattleComposer Recorded(Script script)
    {
        var composer = new BattleComposer(script.Frames[0], () => true);
        foreach (var f in script.Frames.Skip(1))
            composer.Record(f);
        return composer;
    }

    /// <summary>Draws till the player's turn opens: the drawn tick it opened on.</summary>
    private static int OpenedAt(BattleComposer composer, List<CombatCue>? played = null)
    {
        for (var i = 0; i < 2000; i++)
        {
            var frame = composer.Next();
            played?.AddRange(frame.Cues);
            if (composer.Live is not null)
                return composer.Drawn;
        }
        throw new Xunit.Sdk.XunitException("The player's turn never opened.");
    }

    // ---------------------------------------------------------------------------------------------------------------------------

    private sealed record Drawing(Dictionary<int, int> Starts, List<Dictionary<Combatant, Vector3>> Places, int Ticks, BattleActions Actions);

    /// <summary>Records the script whole, the battle waiting on the player, and draws it till it has caught up.</summary>
    private static Drawing Draw(Script script)
    {
        var composer = new BattleComposer(script.Frames[0], () => true);
        foreach (var f in script.Frames.Skip(1))
            composer.Record(f);
        var starts = new Dictionary<int, int> { [0] = 0 };
        var places = new List<Dictionary<Combatant, Vector3>>();
        for (var i = 0; i < 2000 && !composer.CaughtUp; i++)
        {
            var frame = composer.Next();
            foreach (var a in composer.Actions.Actions)
            {
                if (a.Start is { } s)
                    starts.TryAdd(a.Index, s);
            }
            places.Add(frame.Bodies.ToDictionary(b => b.Combatant, b => b.Centre));
        }
        Assert.True(composer.CaughtUp);
        return new Drawing(starts, places, composer.Drawn, composer.Actions);
    }

    /// <summary>A made-up recording: each turn its combatant's, every body where the last tick left it unless it walks.</summary>
    private sealed class Script(params Combatant[] bodies)
    {
        private readonly Dictionary<Combatant, Vector3> _at = [];
        private readonly List<CombatCue> _cues = [];

        public List<BattleFrame> Frames { get; } = [];

        public Script Place(Combatant c, Vector3 at)
        {
            _at[c] = at;
            return this;
        }

        /// <summary>A cue on the next tick.</summary>
        public void Cue(CombatCue cue) => _cues.Add(cue);

        /// <summary><paramref name="c"/>'s turn goes on (or begins) with it walking to <paramref name="to"/> over <paramref name="ticks"/>.</summary>
        public void Walk(Combatant c, Vector3 to, int ticks, CombatState state = CombatState.AI)
        {
            var from = _at[c];
            for (var i = 1; i <= ticks; i++)
            {
                _at[c] = Vector3.Lerp(from, to, (float)i / ticks);
                Add(c, state);
            }
        }

        /// <summary><paramref name="c"/>'s turn goes on (or begins) with it standing for <paramref name="ticks"/>.</summary>
        public void Stand(Combatant c, int ticks, CombatState state = CombatState.AI)
        {
            for (var i = 0; i < ticks; i++)
                Add(c, state);
        }

        /// <summary>One tick of <paramref name="c"/>'s turn, <paramref name="mover"/> stepping to <paramref name="to"/>: the frame made.</summary>
        public BattleFrame Step(Combatant c, Combatant mover, Vector3 to, CombatState state)
        {
            _at[mover] = to;
            Add(c, state);
            return Frames[^1];
        }

        public Vector3 At(Combatant c) => _at[c];

        private void Add(Combatant current, CombatState state = CombatState.AI)
        {
            Frames.Add(new BattleFrame
            {
                Tick = Frames.Count,
                Step = BattleStep.Ran,
                State = state,
                Current = current,
                Bodies = [.. bodies.Select(b => new DrawnBody(b, _at[b], Vector2.UnitY, MovementMode.Modern, 255, default, false, 0, 0, null, null, null, 30))],
                Missile = null,
                Visuals = [],
                Numbers = [],
                Ring = [],
                RingShown = true,
                Aim = null,
                Fade = 1,
                MusicHeld = false,
                SkyDim = 1,
                FogLifted = false,
                Camera = new DrawnCamera(_at[current] + new Vector3(0, 10, -10), _at[current], Vector2.UnitY),
                Gui = new CombatGuiFrame("", null, [], default, [], [], []),
                Menu = null,
                Aura = null,
                Cues = [.. _cues],
                Sounds = [],
                Emitters = [],
            });
            _cues.Clear();
        }
    }
}
