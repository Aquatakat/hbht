using System.Globalization;
using HBHT.Core.Combat;
using HBHT.Plugins;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// What a turn shows, as the cues the battle sends its host (<see cref="ICombatHost"/>, ADR 0019), in order: a bite that hits, a blow that
/// kills, a cast and a flask's landing, each played through <see cref="Battle.Tick"/> from the checkpoints the other combat tests start
/// from (10 for the rat's bite, 05 for Alaron's blow and flask, 07 for Rheda's Endurance), until the next turn's line. The tests skip
/// without the ROM and the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
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

    /// <summary>Checkpoint 05's battle in Alaron's turn, faded in, with a recorder on it.</summary>
    private (Built Built, Recorder Host) AlaronsTurn()
    {
        var built = At(Set, 5);
        var battle = built.Battle;
        Poke(battle, nameof(Battle.State), CombatState.Turn);
        for (var i = 0; i < 120 && battle.Fade.Fading; i++)
            battle.Fade.Step();
        var host = new Recorder();
        battle.Host = host;
        return (built, host);
    }

    /// <summary>
    /// Checkpoint 10's battle wound back to the start of rat 4's turn, faded in, with a recorder on it, as
    /// <see cref="AiCheckpointTests"/> replays its bite on Brenna.
    /// </summary>
    private (Built Built, Recorder Host) RatsTurn()
    {
        var built = At(Set, 10);
        var (battle, c) = (built.Battle, built[4]);
        battle.Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Cell = c.TurnStart;
        c.Body.Sphere.Position = c.Body.Sphere.Position with { X = battle.Origin.X + c.TurnStart.X, Z = battle.Origin.Z + c.TurnStart.Y };
        c.ReadFrom = c.Body.Sphere.Position;
        battle.Grid.Occupy(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Body.ClearPath();
        c.Ai!.ClearTarget();
        battle.Current = c;
        Poke(battle, nameof(Battle.State), CombatState.AI);
        Poke(battle, nameof(Battle.NextState), CombatState.AI);
        for (var i = 0; i < 120 && battle.Fade.Fading; i++)
            battle.Fade.Step();
        var host = new Recorder();
        battle.Host = host;
        return (built, host);
    }

    /// <summary>Ticks until the next turn's line, which ends what the host records, and returns the steps the ticks came to.</summary>
    private static List<BattleStep> PlayOut(Built built, Recorder host)
    {
        var zone = Zone(built);
        var from = host.Cues.Count;
        var steps = new List<BattleStep>();
        for (var i = 0; i < 2000 && !host.Cues.Skip(from).Any(c => c.EndsWith(" begins action", StringComparison.Ordinal)); i++)
            steps.Add(built.Battle.Tick(zone, false, _ => null));
        Assert.EndsWith(" begins action", host.Cues[^1]);
        return steps;
    }

    [Fact]
    public void TheBattlesSetupCuesEachBodyAsItsMadeThenTheFirstTurnsLine()
    {
        var host = new Recorder();
        var battle = Plain(Set, 1, host).Battle;
        // The party (Sholeh among them, in slot 3), then the four rats on the field, then Rheda's turn's line.
        Assert.Equal(["joined 0", "joined 1", "joined 2", "joined 3", "joined 4", "joined 5", "joined 6", "joined 7", "log Rheda begins action"], host.Cues);
        Assert.Equal(8, battle.Combatants.Count());
    }

    [Fact]
    public void ARatsBiteThatHitsBrennaSwingsHitsShowsItsNumberAndFlushesTheLine()
    {
        var (built, host) = RatsTurn();
        var (battle, rat, brenna) = (built.Battle, built[4], built[2]);
        battle.StepAI(rat);
        built.Game.Random.Combat.Seed = 0xA195A333u;

        var steps = PlayOut(built, host);

        Assert.Equal(99, brenna.Sheet.Hp);
        Assert.Equal(
        [
            "log Giant Rat attacks Brenna",
            "animated 4 16",
            "animated 2 7",
            "number 2 1",
            "log Brenna loses 1 hit point",
            "log Giant Rat begins action",
        ], host.Cues);
        Assert.All(steps, s => Assert.Same(BattleStep.Ran, s));
    }

    [Fact]
    public void AlaronsBlowThatKillsRat7DiesAtTheSwingFallsAndFadesOutWhileTheNextTurnBegins()
    {
        var (built, host) = AlaronsTurn();
        var battle = built.Battle;
        var rat = built[7];
        Assert.True(battle.Attack());
        Assert.True(battle.Confirm());
        Assert.Equal(7, battle.Target);
        built.Game.Random.Combat.Seed = 1;

        PlayOut(built, host);

        // As AttackCheckpointTests' kill has it: the death found as the blow is dealt, the hit, its 49 and the fall as it lands, the
        // queued line with them, and its number gone as the fall ends and its body is freed. The next turn begins at once, as the
        // body starts its fade (Combat.FreeBody's Body.FadeOut): the battle doesn't wait for it.
        Assert.Equal(
        [
            "log Alaron attacks Giant Rat",
            "animated 0 12",
            "died 7",
            "animated 7 7",
            "number 7 49",
            "animated 7 9",
            "log Giant Rat killed",
            "number gone 7",
            "log Brenna begins action",
        ], host.Cues);
        Assert.Equal(["Alaron attacks Giant Rat", "Giant Rat killed", "Brenna begins action"], battle.Log.TakeLast(3));
        Assert.True(rat.Freed);
        Assert.Equal(Battle.FadeTicks, rat.Fading);

        // ProcessPlayers: drawn whole for a second where it lay, then fading to nothing over the next, (u8)(left / 60 × 255), and gone.
        var zone = Zone(built);
        var alphas = new List<int>();
        for (var i = 0; i < 200 && !host.Cues.Contains("gone 7"); i++)
        {
            battle.Tick(zone, false, _ => null);
            alphas.Add(rat.Alpha);
        }
        Assert.Equal(Battle.FadeTicks, alphas.Count);
        Assert.All(alphas.Take(60), a => Assert.Equal(255, a));
        Assert.Equal([250, 246, 242], alphas.Skip(60).Take(3));
        Assert.Equal([12, 8, 4, 0], alphas.TakeLast(4));
        Assert.Equal("gone 7", host.Cues[^1]);
    }

    [Fact]
    public void RhedasEnduranceStartsItsVisualsAtEachTargetAndLogsBothLinesOfEachCast()
    {
        var built = At(Set, 7);
        built.Game.Random.Combat.Seed = built.Cp.ReadWord(RandomState);
        var battle = built.Battle;
        var rheda = built[1];
        rheda.Spell = new SpellChoice(SpellSourceKind.Known, rheda.Sheet.Spells.Select((s, i) => (s, i)).Single(p => p.s.Spell.Name == "Endurance").i);
        Poke(battle, nameof(Battle.State), CombatState.TargetSpell);
        battle.TargetsB.BeginSpell(battle, rheda);
        var host = new Recorder();
        battle.Host = host;
        Assert.True(battle.Confirm());

        PlayOut(built, host);

        // Each cast: its line, the caster's cast, her actor and the target's lasting scene (headless, the actor ends at once), the target
        // helped, and the queued line as the result shows; the second cast's line comes up over the first's queued one.
        Assert.Equal(
        [
            "log Rheda casts Endurance",
            "animated 1 5",
            "visual 1 Caster",
            "visual 1 Aura",
            "visual ended 1 Caster",
            "animated 1 10",
            "log Endurance cast on Rheda",
            "log Rheda casts Endurance",
            "animated 1 5",
            "visual 1 Caster",
            "visual 0 Aura",
            "visual ended 1 Caster",
            "animated 0 10",
            "log Endurance cast on Alaron",
            "log Giant Rat begins action",
        ], host.Cues);
    }

    [Fact]
    public void AFailedCastPlaysTheRefusedActorAtTheCasterAndEndsThePlayersTurn()
    {
        // CastSpell (0x800648CC) returns 0xFFFE for a roll PrepareCast (0x8006406C) failed, which CastCurrentSpell (0x80065C08) hands
        // CombatSpellFx.Start (0x8008C43C) as −2: the refused cast's actor at the caster (role 3, 0x1CAA), its rising puffs of smoke,
        // with no glow and no dimming; the caster still plays request 5 and the spell's sound.
        var built = At(Set, 7);
        built.Game.Random.Combat.Seed = built.Cp.ReadWord(RandomState);
        var battle = built.Battle;
        var rheda = built[1];
        // No Intelligence leaves the chance at the level's part alone, which this draw fails.
        rheda.Sheet.Stats.ChangeModified(HBHT.Core.Characters.Stat.Intelligence, -rheda.Sheet.Stats.Modified(HBHT.Core.Characters.Stat.Intelligence));
        rheda.Spell = new SpellChoice(SpellSourceKind.Known, rheda.Sheet.Spells.Select((s, i) => (s, i)).Single(p => p.s.Spell.Name == "Endurance").i);
        Poke(battle, nameof(Battle.State), CombatState.TargetSpell);
        battle.TargetsB.BeginSpell(battle, rheda);
        var host = new Recorder();
        battle.Host = host;
        Assert.True(battle.Confirm());

        PlayOut(built, host);

        Assert.Equal(
        [
            "log Rheda fails to cast the spell",
            "animated 1 5",
            "visual 1 Refused",
            "visual ended 1 Refused",
            "animated 1 10",
            "log Giant Rat begins action",
        ], host.Cues);
        Assert.Equal(1.0f, battle.Fx.Sky.Goal);
        var refused = Assert.Single(host.Visuals);
        Assert.Equal((RecordId.Hex(RecordTypes.Actor, 0x1CAA), rheda, rheda), (refused.Id, refused.Owner, refused.Other));
    }

    [Fact]
    public void AlaronsFireFlaskFliesLandsBurstsAndHitsRat4ForTwiceItsShare()
    {
        var (built, host) = AlaronsTurn();
        var battle = built.Battle;
        var alaron = built[0];
        built.Game.Party.Inventory.Add(PotionId(BasePotion.FireFlask), 2);
        alaron.HoldPotion(battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], battle);
        Assert.True(battle.Attack());
        Assert.Equal(CombatState.TargetThrown, battle.State);
        Assert.True(battle.Confirm());
        Assert.Equal(4, battle.Target);
        built.Game.Random.Combat.Seed = 1;

        PlayOut(built, host);

        // The throw, the flask's flight and landing, the blast's potion visual on its target, then the blow landing its share twice,
        // as the original's two hits do, with the number and the queued line saying so (the port's fix); the visual ends before the next turn.
        Assert.Equal(
        [
            "log Alaron attacks Giant Rat",
            "animated 0 15",
            "missile launched",
            "missile landed",
            "animated 4 5",
            "visual 4 Potion",
            "animated 4 7",
            "number 4 26",
            "log Giant Rat loses 26 hit points",
            "visual ended 4 Potion",
            "log Brenna begins action",
        ], host.Cues);
    }

    [Fact]
    public void ABodyStillFadingWhenAReserveTakesItsSlotFadesOutBesideIt()
    {
        // Checkpoint 08: rats 4 and 5 on the field, 6 and 7 dead, two reserves left.
        var built = At(Set, 8);
        var battle = built.Battle;
        var host = new Recorder();
        battle.Host = host;
        var old = built[4];
        old.Sheet.TakeDamage(old.Sheet.Hp, old);
        battle.Death(old);
        battle.FreeBody(old);
        built.Game.Random.Combat.Seed = 0x12345;
        battle.SpawnReserves();

        // The reserve has slot 4 and a body of its own, made beside the old one, which goes on fading where it lay.
        var newcomer = built[4];
        Assert.NotSame(old, newcomer);
        Assert.False(newcomer.Freed);
        Assert.Equal(Battle.FadeTicks, old.Fading);
        var zone = Zone(built);
        for (var i = 0; i < Battle.FadeTicks; i++)
            battle.Tick(zone, false, _ => null);
        Assert.Equal(0, old.Fading);
        Assert.Single(host.Cues, "gone 4");
        Assert.DoesNotContain("gone 6", host.Cues);
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
        var steps = Enumerable.Range(0, 6).Select(_ => battle.Tick(zone, false, _ => null)).ToList();
        Assert.Equal(["paused True True"], host.Cues);
        Assert.All(steps.Take(3), s => Assert.Same(BattleStep.Ran, s));
        Assert.All(steps.Skip(3), s => Assert.Equal(new BattleStep.Paused(PartyTab: true, CanFlee: true), s));
        battle.Unpause(flee: false);

        // The party flees from the pause: ended, fled, as the next tick's step says.
        host.Cues.Clear();
        Assert.True(battle.Pause());
        for (var i = 0; i < 4; i++)
            battle.Tick(zone, false, _ => null);
        battle.Unpause(flee: true);
        Assert.Equal(["paused True True", "ended Fled"], host.Cues);
        Assert.Equal(new BattleStep.Ended(BattleOutcome.Fled), battle.Tick(zone, false, _ => null));
    }
}
