using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

public sealed partial class StartEarlyTests
{
    [Theory]
    [InlineData(CombatTurns.Original)]
    [InlineData(CombatTurns.StartEarly)]
    public void EveryBodyASpellAtSeveralTargetsKillsIsDrawnFadingOut(CombatTurns turns)
    {
        // The user's Fireball at the rats (Nov 11): some fell and were gone a tick after, with no fade. A freed body is drawn whole for
        // a second and fades over the next (Body.FadeOut). Under Start Early a cast's earlier target, still where it fell, had its fade
        // recorded into the next target's part, drawn ahead, so it read as already played. Alaron casts Dragon Flames at every rat he
        // can; every body freed is held to its whole fade, but where the screen's own fade has begun (the battle won, as 1.0's) or the
        // last word brought it on (a reserve come on where it lies).
        var plan = BattlePlan.Parse("Alaron=spell:Dragon Flames/accept/accept/accept/accept/accept/accept/accept;*=accept/accept/accept");
        var flames = Set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Name == "Dragon Flames");
        var seen = new Dictionary<Combatant, Fade>();
        var run = Play(host =>
        {
            var built = Plain(Set, 1, host);
            built.Battle.Combatants.First(c => c.Sheet.Name.StartsWith("Alaron", StringComparison.Ordinal)).Sheet.LearnSpell(flames, 10);
            return built;
        }, turns, run => run.Input = plan.Tick(run.Drawing) is { } input ? () => input : null, begun: run => run.Each = r =>
        {
            var f = r.Drawing.Frame;
            var resolved = r.Drawing.Composer.Resolved;
            foreach (var (c, gone) in seen)
            {
                // Gone on a tick the last word brought a body on.
                if (!gone.Ended && !f.Bodies.Any(b => b.Combatant == c))
                    (gone.Ended, gone.Brought) = (true, resolved != gone.Resolved);
            }
            foreach (var b in f.Bodies)
            {
                if ((b.Standing & BodyState.Freed) == 0)
                    continue;
                if (!seen.TryGetValue(b.Combatant, out var fade))
                    seen[b.Combatant] = fade = new Fade();
                fade.Resolved = resolved;
                fade.Drawn++;
                if (b.Alpha < b.Combatant.Look.Alpha)
                    fade.Fading++;
                fade.Least = Math.Min(fade.Least, b.Alpha);
                fade.Screen = f.Fade;
            }
        });
        Assert.Equal(BattleOutcome.Won, run.Battle.Outcome);
        var held = seen.Where(p => p.Value.Screen >= 1 && !p.Value.Brought).ToList();
        foreach (var (c, fade) in seen)
            TestContext.Current.TestOutputHelper?.WriteLine($"{c}: drawn freed {fade.Drawn}, fading {fade.Fading}, least alpha {fade.Least}, screen {fade.Screen:0.00}{(fade.Brought ? ", brought on" : "")}");
        Assert.True(held.Count >= 6, $"{held.Count} fades to hold");
        foreach (var (c, fade) in held)
        {
            Assert.True(fade.Drawn - fade.Fading >= Battle.FadeShownTicks && fade.Fading >= Battle.FadeShownTicks - 1 && fade.Least < 10,
                $"{c} drawn freed {fade.Drawn} ticks, {fade.Fading} fading, to alpha {fade.Least}");
        }
    }

    private sealed class Fade
    {
        public int Resolved { get; set; }
        public bool Ended { get; set; }
        public int Drawn { get; set; }
        public int Fading { get; set; }
        public int Least { get; set; } = 255;
        public float Screen { get; set; } = 1;
        public bool Brought { get; set; }
    }
}
