using System.Globalization;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

using static HBHT.Rom.Tests.TestBattles;

namespace HBHT.Rom.Tests;

/// <summary>
/// What a turn shows, as the cues the battle sends its host (<see cref="ICombatHost"/>, ADR 0019), in order: a bite that hits, a blow that
/// kills, a cast and a flask's landing, each played through <see cref="Battle.Tick"/> in the rats' battle the port sets up
/// (<see cref="TestBattles"/>), from the turn the battle comes to with the party skipping, until the next turn's line. The tests skip
/// without the ROM and the story saves.
/// </summary>
public sealed class CombatCueTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>A host that writes each cue down as a line, naming combatants by slot.</summary>
    private sealed class Recorder : ICombatHost
    {
        public List<string> Cues { get; } = [];

        /// <summary>The visuals started, in order.</summary>
        public List<SpellFxVisual> Visuals { get; } = [];

        public void Cue(CombatCue cue)
        {
            if (cue is SpellVisualStarted started)
                Visuals.Add(started.Visual);
            Cues.Add(Line(cue));
        }

        private static string Line(CombatCue cue) => cue switch
        {
            CombatantJoined j => $"joined {j.Combatant.Slot}",
            CombatantAnimated a => $"animated {a.Combatant.Slot} {a.Request}",
            CombatantDied d => $"died {d.Combatant.Slot}",
            CombatantGone g => $"gone {g.Combatant.Slot}{(g.Fled ? " fled" : "")}",
            NumberShown n => $"number {n.Number.Slot} {n.Number.Amount}{(n.Number.Healing ? " healing" : "")}",
            NumberGone n => $"number gone {n.Number.Slot}",
            MissileLaunched => "missile launched",
            MissileLanded => "missile landed",
            SpellVisualStarted v => $"visual {v.Visual.Owner.Slot} {v.Visual.Role}",
            SpellVisualEnded v => $"visual ended {v.Visual.Owner.Slot} {v.Visual.Role}",
            LogLine l => $"log {l.Text}",
            BattlePaused p => string.Create(CultureInfo.InvariantCulture, $"paused {p.PartyTab} {p.CanFlee}"),
            BattleEnded e => $"ended {e.Outcome}",
            _ => cue.ToString(),
        };
    }

    /// <summary>The rats' battle played on, the party skipping, to Alaron's turn with a rat in his reach, faded in, with a recorder on it.</summary>
    private (Built Built, Recorder Host) AlaronsTurn()
    {
        var built = Plain(Set);
        TurnOf(built, Zone(built), "Alaron", c => EnemyInReach(built, c));
        var host = new Recorder();
        built.Battle.Host = host;
        return (built, host);
    }

    /// <summary>The rats' battle played on, the party skipping, to Rheda's turn with Endurance chosen from C-down, with a recorder on it.</summary>
    private (Built Built, Recorder Host, Combatant Rheda) RhedasEndurance()
    {
        var built = Plain(Set);
        var rheda = TurnOf(built, Zone(built), "Rheda");
        Choose(built.Battle, false, r => r.Text == "Endurance");
        Assert.Equal(CombatState.TargetSpell, built.Battle.State);
        var host = new Recorder();
        built.Battle.Host = host;
        return (built, host, rheda);
    }

    /// <summary>Ticks until the next turn's line, which ends what the host records, and returns the steps the ticks came to.</summary>
    private static List<BattleStep> PlayOut(Built built, Recorder host)
    {
        var zone = Zone(built);
        var from = host.Cues.Count;
        var steps = new List<BattleStep>();
        for (var i = 0; i < 2000 && !host.Cues.Skip(from).Any(c => c.EndsWith(" begins action", StringComparison.Ordinal)); i++)
            steps.Add(built.Battle.Tick(zone));
        Assert.EndsWith(" begins action", host.Cues[^1]);
        return steps;
    }

    /// <summary>The next turn's line, as the host records it: whoever's turn it now is.</summary>
    private static string Begins(Battle battle) => $"log {battle.Current!.Sheet.Name} begins action";

    /// <summary>A blow's or a blast's line for the hit points it took.</summary>
    private static string Loses(Combatant c, int hp) => $"log {c.Sheet.Name} loses {hp} hit point{(hp == 1 ? "" : "s")}";

    [Fact]
    public void TheBattlesSetupCuesEachBodyAsItsMadeThenTheFirstTurnsLine()
    {
        var host = new Recorder();
        var battle = Plain(Set, host).Battle;
        // The party (Sholeh among them, in slot 3), then the four rats on the field, then the first turn's line, Brenna's.
        Assert.Equal(["joined 0", "joined 1", "joined 2", "joined 3", "joined 4", "joined 5", "joined 6", "joined 7", "log Brenna begins action"], host.Cues);
        Assert.Equal(8, battle.Combatants.Count());
    }

    [Fact]
    public void ARatsBiteThatHitsSwingsHitsShowsItsNumberAndFlushesTheLine()
    {
        // The party skips its turns while the rats come at it, until a rat's bite hits: from its line to the next turn's.
        var host = new Recorder();
        var built = Plain(Set, host);
        var battle = built.Battle;
        var zone = Zone(built);
        (int From, Combatant Rat, Combatant Bitten, int Hp)? bite = null;
        var steps = new List<BattleStep>();
        for (var i = 0; i < 100_000 && !battle.Over; i++)
        {
            if (battle.Current is { Controlled: false } && battle.State == CombatState.Turn && !battle.Fade.Fading && battle.Lines.Hold is null)
                Assert.True(battle.Skip());
            var seen = host.Cues.Count;
            var step = battle.Tick(zone);
            var attack = host.Cues.FindIndex(seen, c => c.StartsWith("log Giant Rat attacks ", StringComparison.Ordinal));
            if (bite is null && attack >= 0)
            {
                var bitten = battle.Slots[battle.Target]!;
                (bite, steps) = ((attack, battle.Current!, bitten, bitten.Sheet.Hp), []);
            }
            if (bite is not { } b)
                continue;
            steps.Add(step);
            if (!host.Cues.Skip(seen).Any(c => c.EndsWith(" begins action", StringComparison.Ordinal)))
                continue;
            // The next turn's line: a bite that hit, or one more to wait for.
            if (b.Bitten.Sheet.Hp < b.Hp)
                break;
            bite = null;
        }
        Assert.NotNull(bite);
        var (from, rat, target, hp) = bite.Value;
        var lost = hp - target.Sheet.Hp;
        Assert.InRange(lost, 1, hp);
        // Its number goes as its time is up, which may come before the next turn's line, or after.
        var cues = host.Cues.Skip(from).ToList();
        cues.Remove($"number gone {target.Slot}");
        Assert.Equal(
        [
            $"log Giant Rat attacks {target.Sheet.Name}",
            $"animated {rat.Slot} 16",
            $"animated {target.Slot} 7",
            $"number {target.Slot} {lost}",
            Loses(target, lost),
            Begins(battle),
        ], cues);
        Assert.All(steps, s => Assert.Same(BattleStep.Ran, s));
    }

    [Fact]
    public void AlaronsBlowThatKillsARatDiesAtTheSwingFallsAndFadesOutWhileTheNextTurnBegins()
    {
        var (built, host) = AlaronsTurn();
        var battle = built.Battle;
        Assert.True(battle.Attack());
        Assert.True(battle.Confirm());
        var rat = battle.Slots[battle.Target]!;
        Assert.True(rat.Hostile);
        var hp = rat.Sheet.Hp;
        built.Game.Random.Combat.Seed = 1;

        PlayOut(built, host);

        // The death found as the blow is dealt, the hit, its number and the fall as it lands, the queued line with them, and its number
        // gone as the fall ends and its body is freed. The next turn begins at once, as the body starts its fade (Combat.FreeBody's
        // Body.FadeOut): the battle doesn't wait for it.
        Assert.True(rat.Sheet.IsDead);
        var number = host.Cues.Single(c => c.StartsWith($"number {rat.Slot} ", StringComparison.Ordinal));
        Assert.True(int.Parse(number.Split(' ')[2], CultureInfo.InvariantCulture) >= hp, $"{number} for a rat of {hp}");
        Assert.Equal(
        [
            "log Alaron attacks Giant Rat",
            "animated 0 12",
            $"died {rat.Slot}",
            $"animated {rat.Slot} 7",
            number,
            $"animated {rat.Slot} 9",
            "log Giant Rat killed",
            $"number gone {rat.Slot}",
            Begins(battle),
        ], host.Cues);
        Assert.Equal(["Alaron attacks Giant Rat", "Giant Rat killed", Begins(battle)[4..]], battle.Log.TakeLast(3));
        Assert.True(rat.Freed);
        Assert.Equal(Battle.FadeTicks, rat.Fading);

        // ProcessPlayers: drawn whole for a second where it lay, then fading to nothing over the next, (u8)(left / 60 × 255), and gone.
        var zone = Zone(built);
        var alphas = new List<int>();
        for (var i = 0; i < 200 && !host.Cues.Contains($"gone {rat.Slot}"); i++)
        {
            battle.Tick(zone);
            alphas.Add(rat.Alpha);
        }
        Assert.Equal(Battle.FadeTicks, alphas.Count);
        Assert.All(alphas.Take(60), a => Assert.Equal(255, a));
        Assert.Equal([250, 246, 242], alphas.Skip(60).Take(3));
        Assert.Equal([12, 8, 4, 0], alphas.TakeLast(4));
        Assert.Equal($"gone {rat.Slot}", host.Cues[^1]);
    }

    [Fact]
    public void RhedasEnduranceStartsItsVisualsAtEachTargetAndLogsBothLinesOfEachCast()
    {
        var (built, host, rheda) = RhedasEndurance();
        var battle = built.Battle;
        var targets = battle.TargetsB.SelectedSlots.Select(s => battle.Slots[s]!).ToList();
        Assert.True(targets.Count >= 2, $"{targets.Count} targets");
        Assert.True(battle.Confirm());

        PlayOut(built, host);

        // Each cast: its line, the caster's cast, her actor and the target's lasting scene, the target helped, and the queued line as the
        // result shows; the next cast's line comes up over the last one's queued one. Her actor plays its cast and its particles out, the
        // core's own (Start Early), and ends after the result: the battle waits for its strike, not its end. The last target selected is
        // cast at first, and so on down (Combat.ConfirmSpellTarget's target, the set's last).
        var r = rheda.Slot;
        Assert.Equal(
        [
            .. targets.OrderByDescending(t => t.Slot).SelectMany(t => new[]
            {
                "log Rheda casts Endurance",
                $"animated {r} 5",
                $"visual {r} Caster",
                $"visual {t.Slot} Aura",
                $"animated {t.Slot} 10",
                $"log Endurance cast on {t.Sheet.Name}",
                $"visual ended {r} Caster",
            }),
            Begins(battle),
        ], host.Cues);
    }

    /// <summary>A combat seed whose cast roll fails Rheda's Endurance with no Intelligence.</summary>
    private const uint FailingSeed = 0xDAA66D2B;

    [Fact]
    public void AFailedCastPlaysTheRefusedActorAtTheCasterAndEndsThePlayersTurn()
    {
        // CastSpell (0x800648CC) returns 0xFFFE for a roll PrepareCast (0x8006406C) failed, which CastCurrentSpell (0x80065C08) hands
        // CombatSpellFx.Start (0x8008C43C) as −2: the refused cast's actor at the caster (role 3, 0x1CAA), its rising puffs of smoke,
        // with no glow and no dimming; the caster still plays request 5 and the spell's sound.
        var (built, host, rheda) = RhedasEndurance();
        var battle = built.Battle;
        // No Intelligence leaves the chance at the levels' part alone, the spell's and hers, which this draw fails.
        rheda.Sheet.Stats.ChangeModified(HBHT.Core.Characters.Stat.Intelligence, -rheda.Sheet.Stats.Modified(HBHT.Core.Characters.Stat.Intelligence));
        built.Game.Random.Combat.Seed = FailingSeed;
        Assert.True(battle.Confirm());
        var target = battle.Target;

        PlayOut(built, host);

        // The cast that failed still lands on its target with nothing, which an ally takes as help (Battle.Land).
        var r = rheda.Slot;
        Assert.Equal(
        [
            "log Rheda fails to cast the spell",
            $"animated {r} 5",
            $"visual {r} Refused",
            $"animated {target} 10",
            $"visual ended {r} Refused",
            Begins(battle),
        ], host.Cues);
        Assert.Equal(1.0f, battle.Fx.Sky.Goal);
        var refused = Assert.Single(host.Visuals);
        // At the caster, its other end the cast's target.
        Assert.Equal((RecordId.Hex(RecordTypes.Actor, 0x1CAA), rheda, battle.Slots[target]), (refused.Id, refused.Owner, refused.Other));
    }

    [Fact]
    public void AlaronsFireFlaskFliesLandsBurstsAndHitsARatForTwiceItsShare()
    {
        // A rat alone, so the blast catches no one but its target: Alaron's first turn, the rat still far off.
        var built = Plain(Set, enemies: [new PartyEnemy(Rat, 1, 1)]);
        var alaron = TurnOf(built, Zone(built), "Alaron");
        var host = new Recorder();
        var battle = built.Battle;
        battle.Host = host;
        built.Game.Party.Inventory.Add(PotionId(BasePotion.FireFlask), 2);
        alaron.HoldPotion(battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], battle);
        Assert.True(battle.Attack());
        Assert.Equal(CombatState.TargetThrown, battle.State);
        Assert.True(battle.Confirm());
        var rat = battle.Slots[battle.Target]!;
        Assert.True(rat.Hostile);
        var hp = rat.Sheet.Hp;
        built.Game.Random.Combat.Seed = 1;

        PlayOut(built, host);

        // The throw, the flask's flight and landing, the blast's potion visual on its target, then the blow landing its share twice,
        // as the original's two hits do, with the number and the queued line saying so (the port's fix). The visual's actor and its
        // particles play on into the next turn: nothing waits for them.
        var lost = hp - rat.Sheet.Hp;
        Assert.True(lost > 0 && lost % 2 == 0, $"the rat lost {lost}");
        Assert.Equal(
        [
            "log Alaron attacks Giant Rat",
            "animated 0 15",
            "missile launched",
            "missile landed",
            $"animated {rat.Slot} 5",
            $"visual {rat.Slot} Potion",
            $"animated {rat.Slot} 7",
            $"number {rat.Slot} {lost}",
            Loses(rat, lost),
            $"number gone {rat.Slot}",
            Begins(battle),
        ], host.Cues);
        Assert.Contains(battle.Fx.Visuals, v => v.Role == SpellFxRole.Potion);
    }

    [Fact]
    public void ABodyStillFadingWhenAReserveTakesItsSlotFadesOutBesideIt()
    {
        // The four rats on the field and six reserves: one of the four killed, its body freed, and the reserves brought in.
        var built = Plain(Set);
        var battle = built.Battle;
        var host = new Recorder();
        battle.Host = host;
        var old = built[4];
        old.Sheet.TakeDamage(old.Sheet.Hp, old);
        battle.Death(old);
        battle.FreeBody(old);
        // A seed whose 2d6 doesn't send the reserve off as it arrives: with all four of the rats that began counted able yet, it may
        // (Battle.ReserveFleesOnArrival).
        built.Game.Random.Combat.Seed = 0xDAA66D2B;
        battle.SpawnReserves();

        // The reserve has slot 4 and a body of its own, made beside the old one, which goes on fading where it lay.
        var newcomer = built[4];
        Assert.NotSame(old, newcomer);
        Assert.False(newcomer.Freed);
        Assert.Equal(Battle.FadeTicks, old.Fading);
        var zone = Zone(built);
        for (var i = 0; i < Battle.FadeTicks; i++)
            battle.Tick(zone);
        Assert.Equal(0, old.Fading);
        Assert.Equal(["gone 4"], host.Cues.Where(c => c.StartsWith("gone ", StringComparison.Ordinal)));
        Assert.False(newcomer.Freed);
    }

    [Fact]
    public void StartPausesOnceItsTwoFramesHavePassedAndFleeingEndsTheBattle()
    {
        var (built, host) = AlaronsTurn();
        var battle = built.Battle;
        var zone = Zone(built);

        // Start in a turn: the pause cued once, and the step saying so from the tick its two frames have passed.
        Assert.True(battle.Pause());
        var steps = Enumerable.Range(0, 6).Select(_ => battle.Tick(zone)).ToList();
        Assert.Equal(["paused True True"], host.Cues);
        Assert.All(steps.Take(3), s => Assert.Same(BattleStep.Ran, s));
        Assert.All(steps.Skip(3), s => Assert.Equal(new BattleStep.Paused(PartyTab: true, CanFlee: true), s));
        battle.Unpause(flee: false);

        // The party flees from the pause: ended, fled, as the next tick's step says.
        host.Cues.Clear();
        Assert.True(battle.Pause());
        for (var i = 0; i < 4; i++)
            battle.Tick(zone);
        battle.Unpause(flee: true);
        Assert.Equal(["paused True True", "ended Fled"], host.Cues);
        Assert.Equal(new BattleStep.Ended(BattleOutcome.Fled), battle.Tick(zone));
    }
}
