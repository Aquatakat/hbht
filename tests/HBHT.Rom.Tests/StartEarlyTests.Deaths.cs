using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

public sealed partial class StartEarlyTests
{
    [Theory]
    [InlineData("Dragon Flames", CombatTurns.Original)]
    [InlineData("Dragon Flames", CombatTurns.StartEarly)]
    [InlineData("Fireball", CombatTurns.Original)]
    [InlineData("Fireball", CombatTurns.StartEarly)]
    public void EveryBodyASpellAtSeveralTargetsKillsIsDrawnFadingOut(string spellName, CombatTurns turns)
    {
        // The user's Fireball at the rats (Nov 11): some fell and were gone a tick after, with no fade. A freed body is drawn whole for
        // a second and fades over the next (Body.FadeOut). Under Start Early a cast's earlier target, still where it fell, had its fade
        // recorded into the next target's part, drawn ahead, so it read as already played; a caster its own Fireball killed was drawn
        // from its fall to its going at once; and a reserve coming on where a dead rat lay had the last word take the rat away. Alaron
        // casts at every rat he can, Fireball taking the party too; every body freed is drawn through its whole fade, but where the
        // screen's own fade, as the battle ends, has begun (1.0 waits for no fade).
        var plan = BattlePlan.Parse($"Alaron=spell:{spellName}/accept/accept/accept/accept/accept/accept/accept;*=accept/accept/accept");
        var spell = Set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Name == spellName);
        var seen = new Dictionary<Combatant, Fade>();
        var run = Play(host =>
        {
            var built = Plain(Set, 1, host);
            built.Battle.Combatants.First(c => c.Sheet.Name.StartsWith("Alaron", StringComparison.Ordinal)).Sheet.LearnSpell(spell, 10);
            return built;
        }, turns, run => run.Input = plan.Tick(run.Drawing) is { } input ? () => input : null, begun: run => run.Each = r =>
        {
            var f = r.Drawing.Frame;
            foreach (var b in f.Bodies)
            {
                if ((b.Standing & BodyState.Freed) == 0)
                    continue;
                if (!seen.TryGetValue(b.Combatant, out var fade))
                    seen[b.Combatant] = fade = new Fade();
                fade.Drawn++;
                if (b.Alpha < b.Combatant.Look.Alpha)
                    fade.Fading++;
                fade.Least = Math.Min(fade.Least, b.Alpha);
                fade.Screen = f.Fade;
            }
        });
        // Fireball takes the party too, till it takes Alaron, and the battle with him.
        Assert.Equal(spellName == "Fireball" ? BattleOutcome.Lost : BattleOutcome.Won, run.Battle.Outcome);
        foreach (var (c, fade) in seen)
            TestContext.Current.TestOutputHelper?.WriteLine($"{c}: drawn freed {fade.Drawn}, fading {fade.Fading}, least alpha {fade.Least}, screen {fade.Screen:0.00}");
        var whole = 0;
        foreach (var (c, fade) in seen)
        {
            if (fade.Drawn - fade.Fading >= Battle.FadeShownTicks && fade.Fading >= Battle.FadeShownTicks - 1 && fade.Least < 10)
                whole++;
            else
                Assert.True(fade.Screen < 1, $"{c} drawn freed {fade.Drawn} ticks, {fade.Fading} fading, to alpha {fade.Least}, the screen not fading");
        }
        Assert.True(whole >= (spellName == "Fireball" ? 4 : 8), $"{whole} fades drawn whole");
    }

    private sealed class Fade
    {
        public int Drawn { get; set; }
        public int Fading { get; set; }
        public int Least { get; set; } = 255;
        public float Screen { get; set; } = 1;
    }
}
