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
        public void Walk(Combatant c, Vector3 to, int ticks)
        {
            var from = _at[c];
            for (var i = 1; i <= ticks; i++)
            {
                _at[c] = Vector3.Lerp(from, to, (float)i / ticks);
                Add(c);
            }
        }

        /// <summary><paramref name="c"/>'s turn goes on (or begins) with it standing for <paramref name="ticks"/>.</summary>
        public void Stand(Combatant c, int ticks)
        {
            for (var i = 0; i < ticks; i++)
                Add(c);
        }

        private void Add(Combatant current)
        {
            Frames.Add(new BattleFrame
            {
                Tick = Frames.Count,
                Step = BattleStep.Ran,
                State = CombatState.AI,
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
