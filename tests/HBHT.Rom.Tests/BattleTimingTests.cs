using System.Numerics;
using HBHT.Core.Actors;
using HBHT.Core.Audio;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The timing of a battle's turns as the user saw them in 1.0 (docs/systems/combat.md): A skipping the AI's walk, the blow landing at
/// the attack's strike while the swing plays on, and the bodies' keys sounding for the swing, the hit, the dodge and the death. Played
/// headless with the actors' real animations over checkpoint 01's battle, the party attacking what's in reach or skipping.
/// </summary>
public sealed class BattleTimingTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private sealed class Run
    {
        private readonly PluginSet _set;
        private readonly CombatCheckpoints.Built _built;
        private readonly HBHT.Core.Chunks.ZoneEngine _zone;

        /// <summary>Whether the party attacks what's in reach; otherwise it skips every turn, and only the rats strike.</summary>
        private readonly bool _attack;

        public Run(PluginSet set, bool attack = true)
        {
            _set = set;
            _attack = attack;
            _built = CombatCheckpoints.Plain(set, 2);
            _zone = CombatCheckpoints.Zone(_built);
        }

        public Battle Battle => _built.Battle;

        /// <summary>A combatant's actor, the core's own (ADR 0040).</summary>
        public IActorMotion? Motion(Combatant c) => c.Actor;

        /// <summary>A tick, the party attacking what's in reach or skipping on its turns.</summary>
        public void Tick()
        {
            if (Battle.State == CombatState.Turn && !Battle.Fade.Fading && Battle.Current is { Controlled: false })
            {
                if (_attack && Battle.Attack())
                    Battle.Confirm();
                else
                    Battle.Skip();
            }
            Battle.Tick(_zone, false);
        }

        public bool Over => Battle.Over || Battle.State is CombatState.Victory or CombatState.Defeat;
    }

    [Fact]
    public void AnAiWalkSkippedWithAEndsAtOnceAtItsLegsEnd()
    {
        var run = new Run(Set);
        var skipped = 0;
        for (var i = 0; i < 100_000 && !run.Over && skipped < 3; i++)
        {
            run.Tick();
            if (run.Battle.State != CombatState.Moving || run.Battle.Current is not { } c || !c.Body.Moving)
                continue;
            // Combat.InputMoving: Body.GiveUp puts the body at the last point queued, the leg's end, whatever lies between.
            var end = c.Body.Queued.Count > 0 ? c.Body.Queued.Last().Goal : c.Body.Goal;
            Assert.True(run.Battle.SkipWalk());
            Assert.Equal(end, new Vector2(c.Body.Sphere.Position.X, c.Body.Sphere.Position.Z));
            Assert.False(c.Body.Moving);
            Assert.Empty(c.Body.Queued);
            // The state's next step goes on as on arriving: the turn toward the way it walked, then the action or the rest.
            run.Tick();
            Assert.NotEqual(CombatState.Moving, run.Battle.State);
            Assert.Same(c, run.Battle.Current);
            skipped++;
        }
        Assert.Equal(3, skipped);
        Assert.False(run.Battle.SkipWalk());
    }

    [Fact]
    public void ABlowLandsAtTheAttacksStrikeWhileTheSwingPlaysOn()
    {
        // ProcessPlayers lets the attacker's body stop counting as playing its attack at (u16)(int)(length × share), its frame before
        // the step; the action's timer then lands the blow on the next tick, and never before 30. The length is the attack state's,
        // which an actor at the end of its last swing enters a tick late, by way of another.
        var run = new Run(Set);
        var attacks = new List<(string Name, int Ticks, int Expected)>();
        Combatant? attacker = null;
        int started = 0, expected = 0, length = 0, frame = 0;
        for (var i = 0; i < 100_000 && !run.Over && attacks.Count < 8; i++)
        {
            var before = run.Battle.State;
            run.Tick();
            var state = run.Battle.State;
            if (state == CombatState.Action && before != CombatState.Action && run.Battle.Current is { } a)
                (attacker, started, expected) = (a, i, 0);
            if (state == CombatState.Action && attacker is { } at && run.Motion(at) is { } motion)
            {
                if (at.Body.Playing is >= 12 and <= 18 && (motion.StateLength != length || motion.Frame < frame))
                {
                    var strike = (ushort)(int)(motion.StateLength * at.Look.Strike);
                    expected = Math.Max(Battle.SwingTicks, i - started - 1 + strike + 2);
                }
                (length, frame) = (motion.StateLength, motion.Frame);
            }
            if (before == CombatState.Action && state != CombatState.Action && attacker is { } done)
            {
                attacks.Add((done.Sheet.Name, i - started, expected));
                // The swing plays on past the blow: the attacker's actor is still partway through its attack.
                var swing = run.Motion(done)!;
                Assert.InRange(swing.Frame, 2, swing.StateLength - 1);
                attacker = null;
            }
        }
        Assert.True(attacks.Count >= 4);
        Assert.All(attacks, a => Assert.Equal(a.Expected, a.Ticks));
        // A rat's bite (279 ticks, 0.23) lands at 66 and Alaron's club (111, 0.42) at 48.
        Assert.Contains(attacks, a => a.Name.Contains("Rat", StringComparison.Ordinal) && a.Ticks == 66);
        Assert.Contains(attacks, a => a.Name == "Alaron" && a.Ticks == 48);
    }

    [Fact]
    public void TheSwingTheHitTheDodgeAndTheDeathSound()
    {
        // The one-shot requests are held through a frame (Combat.AfterFrame) and an actor takes only a request its state has a branch
        // for (ActorStateMachine.SetRequest), so each latches its request and its keys sound past their first frame. Alaron's club (12,
        // the weapon's sound), a dodge (8) and a rat's death (9) come with the party attacking; a bite felt (7) with the party only
        // skipping, as its blows may all dodge or kill.
        var heard = Heard(new Run(Set), [8, 9, 12]);
        heard.UnionWith(Heard(new Run(Set, attack: false), [7]));
        Assert.Superset(new HashSet<int> { 7, 8, 9, 12 }, heard);
    }

    /// <summary>The requests whose keys sounded in a battle, until it's over or <paramref name="wanted"/> have.</summary>
    private static HashSet<int> Heard(Run run, int[] wanted)
    {
        var heard = new HashSet<int>();
        var slots = new Dictionary<Combatant, (SoundHandle, SoundHandle)>();
        for (var i = 0; i < 100_000 && !run.Over && !heard.IsSupersetOf(wanted); i++)
        {
            run.Tick();
            foreach (var c in run.Battle.Combatants)
            {
                var now = (c.Audio.Slots[0], c.Audio.Slots[1]);
                if (slots.TryGetValue(c, out var was) && !was.Equals(now))
                    heard.Add(c.Audio.Request);
                slots[c] = now;
            }
        }
        return heard;
    }
}
