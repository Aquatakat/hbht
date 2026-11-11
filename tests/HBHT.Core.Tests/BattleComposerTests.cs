using System.Numerics;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

/// <summary>
/// Start Early's scheduling (Start Early, <see cref="BattleComposer"/>) over made-up recordings: bodies walking in straight lines
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
    public void ReleasingABodyStillDrawnFastForwardsOnlyTheActionsHoldingIt()
    {
        // C walks far off on the left for 200 ticks; D walks far off on the right; then A's turn, whose reach neither comes near.
        var script = new Script(_c, _d, _a, _b) { Field = new TurnField(new(0, 0, -10), 3, 0.05f) };
        script.Place(_a, new(0, 0, -10)).Place(_b, new(4, 0, 10)).Place(_c, new(-15, 0, -10)).Place(_d, new(15, 0, 10));
        script.Walk(_c, new(-15, 0, 10), 200);
        script.Walk(_d, new(15, 0, -2), 100);
        script.Stand(_d, 30);
        script.Stand(_a, 1, CombatState.Turn);

        var composer = Recorded(script);
        OpenedAt(composer);
        var actions = composer.Actions.Actions;
        var (cs, ds) = (actions[0], actions[1]);
        Assert.True(cs.Current == _c && ds.Current == _d);
        var cAt = cs.TickAt(composer.Drawn);
        Assert.False(composer.Free(_d));

        composer.Release([_d]);
        var frame = composer.Next();
        // D's action, which holds D, is drawn to its end and D with it; C's walk draws on a tick at a time.
        Assert.True(ds.TickAt(composer.Drawn) > ds.TailEnd);
        Assert.True(composer.Free(_d));
        Assert.Equal(new Vector3(15, 0, -2), Place(frame, _d));
        Assert.Equal(cAt + 1, cs.TickAt(composer.Drawn));
        Assert.Contains(_d, frame.Jumped);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Piece A: beats, the turn's wait, and the held stick

    [Fact]
    public void BeatsOneShotCantHoldDontOverlapThoughTheLaterWalkStartsEarly()
    {
        // A walks up to B on the left and hits it 60 ticks in; C walks up to D on the right, 24 away, and hits it 60 ticks in.
        var script = Beating(new(-12, 0, 5), new(12, 0, 5));
        var drawn = Draw(script);
        var (b, d) = (drawn.Actions.Actions[0].Beats.Single(), drawn.Actions.Actions[1].Beats.Single());
        Assert.Equal((_b, BeatKind.Damage), (b.Subject, b.Kind));
        Assert.Equal(_d, d.Subject);
        Assert.Equal(new StartEarlyPacing().BeatLeast, b.To - b.From + 1);
        // D's beat lands the drawn tick after B's ends: C's turn starts just that much later, its walk long before B is hit.
        var bEnds = drawn.Starts[0] + (b.To!.Value - drawn.Actions.Actions[0].First);
        var dStrikes = drawn.Starts[1] + (d.From - drawn.Actions.Actions[1].First);
        Assert.Equal(bEnds + 1, dStrikes);
        Assert.True(drawn.Starts[1] < drawn.Starts[0] + (b.From - drawn.Actions.Actions[0].First), $"C's walk starts at {drawn.Starts[1]}");
    }

    [Fact]
    public void BeatsOneShotCanHoldOverlap()
    {
        // The same, with a shot that holds the whole field: C's turn starts on the first drawn tick, as before beats.
        var drawn = Draw(Beating(new(-12, 0, 5), new(12, 0, 5)), new SpreadFraming(100));
        Assert.Equal(1, drawn.Starts[1]);
        // And with the stand-in's shot, two beats near each other overlap too.
        var near = Draw(Beating(new(-2, 0, 5), new(2, 0, 5)));
        Assert.Equal(1, near.Starts[1]);
    }

    [Fact]
    public void StartEarlysShotQuestionLetsBeatsAcrossTheFieldOverlapAndKeepsFarOnesApart()
    {
        // The director's answer (BeatShots): two beats 24 apart share a wide shot, so C's turn starts on the first drawn tick; 120 apart,
        // none holds both, and C's beat waits. Each question it's asked again is answered as before, without looking again.
        var shots = new BeatShots(null, null, HBHT.Core.Combat.CameraTuning.Default);
        Assert.Equal(1, Draw(Beating(new(-12, 0, 5), new(12, 0, 5)), shots).Starts[1]);
        var far = new BeatShots(null, null, HBHT.Core.Combat.CameraTuning.Default);
        Assert.True(Draw(Beating(new(-60, 0, 5), new(60, 0, 5)), far).Starts[1] > 1);
        Assert.True(far.Looked < far.Asked, $"looked into {far.Looked} of {far.Asked}");
    }

    [Fact]
    public void APlayersTurnWaitsForTheBeatsBeforeIt()
    {
        // C walks up to B and hits it 60 ticks in, then stands 60; then A's turn, far off.
        var composer = Recorded(Hitting(new(-10, 0, 0)));
        var beat = composer.Actions.Actions[0].Beats.Single();
        var opened = OpenedAt(composer);
        // Not while C walks, nor while B's beat plays: the drawn tick after it ends (C's action draws its ticks on the same drawn ticks).
        Assert.Equal(beat.To!.Value + 1, opened);
        Assert.False(composer.CaughtUp);
    }

    [Fact]
    public void APlayersTurnOpensWhileABeatPlaysThatItsShotCanHold()
    {
        // The same, with A standing near B: the stand-in's shot holds both, so the turn opens as B is hit, not before.
        var composer = Recorded(Hitting(new(8, 0, 0)));
        var beat = composer.Actions.Actions[0].Beats.Single();
        Assert.Equal(beat.From, OpenedAt(composer));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The outcome hold (the user's, Nov 10)

    [Fact]
    public void TwoActionsResultsStrikeAnOutcomeHoldApartThoughOneShotHoldsBoth()
    {
        // The beats one shot holds, which overlapped from the first drawn tick: C's now strikes a second after B's.
        var hold = new StartEarlyPacing().OutcomeHold;
        var drawn = Draw(Beating(new(-12, 0, 5), new(12, 0, 5)), new SpreadFraming(100), new StartEarlyPacing());
        var (b, d) = (drawn.Actions.Actions[0].Beats.Single(), drawn.Actions.Actions[1].Beats.Single());
        var bStrikes = drawn.Starts[0] + (b.From - drawn.Actions.Actions[0].First);
        var dStrikes = drawn.Starts[1] + (d.From - drawn.Actions.Actions[1].First);
        Assert.Equal(bStrikes + hold, dStrikes);
        // Its 60 ticks' walk began as B was hit, overlapping B's beat as before.
        Assert.True(drawn.Starts[1] <= bStrikes, $"C's walk starts at {drawn.Starts[1]}");
    }

    [Fact]
    public void APlayersTurnOpensOnlyOnceTheResultBeforeItHasBeenHeld()
    {
        // A standing near B, whose shot holds both: the turn opens a second after B is hit, not as it's hit.
        var composer = Recorded(Hitting(new(8, 0, 0)), new StartEarlyPacing());
        var beat = composer.Actions.Actions[0].Beats.Single();
        Assert.Equal(beat.From + new StartEarlyPacing().OutcomeHold, OpenedAt(composer));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // A spell at several targets, its hits at once

    [Fact]
    public void ASpellCastAtSeveralTargetsInTurnStrikesThemAllOnOneDrawnTick()
    {
        // D's turn, then A casts at B, C and D in turn as 1.0 does: each cast 40 ticks before its hit, 60 after it before the next.
        var script = new Script(_d, _a, _b, _c);
        script.Place(_a, Vector3.Zero).Place(_b, new(-3, 0, 5)).Place(_c, new(0, 0, 6)).Place(_d, new(3, 0, 5));
        script.Stand(_d, 10);
        script.Stand(_a, 20);
        var casts = new List<(Combatant Target, CombatCue Cast, CombatCue Hit)>();
        var handle = 0;
        foreach (var target in new[] { _b, _c, _d })
        {
            var visual = new SpellFxVisual(handle++, SpellFxRole.Caster, HBHT.Plugins.RecordId.Hex(HBHT.Plugins.RecordTypes.Actor, 1), _a, 0) { Other = target };
            var (cast, hit) = (new CombatantAnimated(_a, Battle.CastRequest), new CombatantAnimated(target, Battle.HitRequest));
            script.Cue(new SpellVisualStarted(visual));
            script.Cue(cast);
            script.Stand(_a, 40);
            script.Cue(new SpellVisualEnded(visual));
            script.Cue(hit);
            script.Stand(_a, 60);
            casts.Add((target, cast, hit));
        }
        script.Stand(_b, 10);

        var composer = Recorded(script);
        var recorded = script.Frames.Skip(1).SelectMany(f => f.Cues).ToList();
        var played = new Dictionary<CombatCue, int>(ReferenceEqualityComparer.Instance);
        var ticks = new Dictionary<Combatant, int>();
        for (var i = 0; i < 2000 && !composer.CaughtUp; i++)
        {
            var frame = composer.Next();
            foreach (var cue in frame.Cues)
            {
                Assert.False(played.ContainsKey(cue), $"{cue} played twice");
                played[cue] = composer.Drawn;
            }
            // No body is drawn from an earlier tick than before.
            foreach (var b in frame.Bodies)
            {
                var t = composer.DrawnTickOf(b.Combatant)!.Value;
                Assert.True(t >= ticks.GetValueOrDefault(b.Combatant), $"{b.Combatant} went back to {t} at drawn tick {composer.Drawn}");
                ticks[b.Combatant] = t;
            }
        }
        Assert.True(composer.CaughtUp);
        Assert.Equal(recorded.Count, played.Count);

        // A's turn is cut into a part a target, each drawn ahead of the first by its hit's distance from the first's.
        var action = composer.Actions.Actions[1];
        Assert.Equal(3, action.Parts.Count);
        Assert.Equal([_b, _c, _d], action.Parts.Select(p => p.Target));
        Assert.Equal([0, 100, 200], action.Parts.Select(p => p.Shift!.Value));
        // Every target's hit strikes on one drawn tick, each cast its 40 ticks before it, and the battle is drawn 200 ticks sooner.
        var strike = played[casts[0].Hit];
        foreach (var (target, cast, hit) in casts)
        {
            Assert.Equal(strike, played[hit]);
            Assert.Equal(strike - 40, played[cast]);
            Assert.Single(action.Beats, b => b.Subject == target);
        }
        Assert.True(composer.Drawn <= script.Frames.Count - 1 - 200, $"drawn in {composer.Drawn} ticks");
    }

    [Fact]
    public void ThePlayersStickWaitsTillNoBodyWalkingInTheDrawingIsInItsReach()
    {
        // C walks slowly north along x = 5, past A at its nearest 5 away; A's field is 4 across its reach.
        var script = Passing(5);
        var composer = Recorded(script);
        var opened = OpenedAt(composer);
        var cs = composer.Actions.Actions[0];
        // It opens while C still walks, but only once nowhere A could have run to by each drawn tick comes near where C is then.
        Assert.InRange(opened, 60, 140);
        Assert.True(cs.TickAt(composer.Drawn) < cs.Last, "C still walks as A's turn opens");
        var need = _a.Body.Sphere.Radius + 2 * _c.Body.Sphere.Radius;
        var control = composer.Drawn + composer.Actions.Last - composer.Live!.First;
        for (var d = control; d <= cs.DrawnAt(cs.Last); d++)
        {
            var reach = MathF.Min(4, 0.05f * (d - control + 1));
            var c = Along(cs.TickAt(d));
            Assert.True(new Vector2(c.X, c.Z).Length() - reach > need, $"C at {c} within reach {reach:F2} at drawn tick {d}");
        }
        // A drawn tick sooner, and it wouldn't have held.
        var sooner = false;
        for (var d = control - 1; d <= cs.DrawnAt(cs.Last); d++)
        {
            var c = Along(cs.TickAt(d));
            sooner |= new Vector2(c.X, c.Z).Length() - MathF.Min(4, 0.05f * (d - control + 2)) <= need;
        }
        Assert.True(sooner, "the stick came later than it had to");
        // And A, running east toward C's path as fast as it can once it has the stick, never touches C as drawn.
        var reachNow = _a.Body.Sphere.Radius + _c.Body.Sphere.Radius;
        for (var i = 0; i < 400; i++)
        {
            if (composer.AtPresent)
            {
                var a = script.At(_a);
                composer.Record(script.Step(_a, _a, a.X < 4 ? a + new Vector3(0.05f, 0, 0) : a, CombatState.Turn));
            }
            var frame = composer.Next();
            Assert.True(Vector3.Distance(Place(frame, _a), Place(frame, _c)) >= reachNow, $"A touches C at drawn tick {composer.Drawn}");
        }
        Assert.Equal(0, composer.Resolved);

        // Tick k of the script is its walk's (k + 1)th step.
        static Vector3 Along(int tick) => Vector3.Lerp(new(5, 0, -10), new(5, 0, 10), Math.Clamp(tick + 1, 0, 300) / 300f);
    }

    [Fact]
    public void ThePlayersStickComesAtOnceWhenNoBodyWalkingIsNear()
    {
        var composer = Recorded(Passing(30));
        Assert.InRange(OpenedAt(composer), 1, 2);
        Assert.True(composer.DrawnTickOf(_c) < 10);
    }

    [Fact]
    public void ASkipsTheWaitForThePlayersTurn()
    {
        // As above, A pressed ten ticks in: C's walk, which holds the turn, is drawn to its end, and the turn opens.
        var composer = Recorded(Passing(5));
        for (var i = 0; i < 10; i++)
            composer.Next();
        Assert.NotNull(composer.Pending);
        Assert.Null(composer.Live);
        Assert.True(composer.SkipHold());
        var frame = composer.Next();
        Assert.NotNull(composer.Live);
        Assert.Equal(new Vector3(5, 0, 10), Place(frame, _c));
        Assert.True(composer.Free(_c));
    }

    [Fact]
    public void ASkipsABeatHoldingThePlayersTurn()
    {
        var composer = Recorded(Hitting(new(-10, 0, 0)));
        for (var i = 0; i < 10; i++)
            composer.Next();
        Assert.True(composer.SkipHold());
        composer.Next();
        Assert.NotNull(composer.Live);
        Assert.True(composer.Free(_b));
    }

    /// <summary>A walks up to B and hits it, then C walks up to D and hits it, each 60 ticks into its turn; then D's turn.</summary>
    private Script Beating(Vector3 b, Vector3 d)
    {
        var script = new Script(_a, _b, _c, _d);
        script.Place(_a, b - new Vector3(0, 0, 10)).Place(_b, b).Place(_c, d - new Vector3(0, 0, 10)).Place(_d, d);
        script.Walk(_a, b - new Vector3(0, 0, 1.2f), 60);
        script.Cue(new CombatantAnimated(_b, Battle.HitRequest));
        script.Stand(_a, 60);
        script.Walk(_c, d - new Vector3(0, 0, 1.2f), 60);
        script.Cue(new CombatantAnimated(_d, Battle.HitRequest));
        script.Stand(_c, 60);
        script.Stand(_d, 10);
        return script;
    }

    /// <summary>C walks up to B at (10, 5) and hits it 60 ticks in, then stands 60; then A's turn, A standing at <paramref name="a"/>.</summary>
    private Script Hitting(Vector3 a)
    {
        var script = new Script(_c, _a, _b, _d);
        script.Place(_a, a).Place(_b, new(10, 0, 5)).Place(_c, new(10, 0, -5)).Place(_d, new(-20, 0, -20));
        script.Walk(_c, new(10, 0, 3.8f), 60);
        script.Cue(new CombatantAnimated(_b, Battle.HitRequest));
        script.Stand(_c, 60);
        script.Stand(_a, 1, CombatState.Turn);
        return script;
    }

    /// <summary>
    /// C walks north along x = <paramref name="x"/> from z = −10 to 10 over 300 ticks; then A's turn at the origin, its field 4 across
    /// and its greatest travel 0.05 a tick.
    /// </summary>
    private Script Passing(float x)
    {
        var script = new Script(_c, _a, _b, _d) { Field = new TurnField(Vector3.Zero, 4, 0.05f) };
        script.Place(_a, Vector3.Zero).Place(_b, new(-20, 0, 20)).Place(_c, new(x, 0, -10)).Place(_d, new(-20, 0, -20));
        script.Walk(_c, new(x, 0, 10), 300);
        script.Stand(_a, 1, CombatState.Turn);
        return script;
    }

    private static Vector3 Place(BattleFrame f, Combatant c) => f.Bodies.Single(b => b.Combatant == c).Centre;

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

    /// <summary>
    /// The pacing the tests of the other rules draw with: no outcome hold, so a beat's timing is its own. The hold has tests of its own,
    /// with the pacing as it is.
    /// </summary>
    private static readonly StartEarlyPacing NoHold = new() { OutcomeHold = 0 };

    private static BattleComposer Recorded(Script script, StartEarlyPacing? pacing = null)
    {
        var composer = new BattleComposer(script.Frames[0], () => true, pacing ?? NoHold);
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
    private static Drawing Draw(Script script, IBeatFraming? framing = null, StartEarlyPacing? pacing = null)
    {
        var composer = new BattleComposer(script.Frames[0], () => true, pacing ?? NoHold);
        if (framing is not null)
            composer.Framing = framing;
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

        /// <summary>The field of the player's turns, on their frames.</summary>
        public TurnField? Field { get; init; }

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
                Field = state is CombatState.Turn ? Field : null,
            });
            _cues.Clear();
        }
    }
}
