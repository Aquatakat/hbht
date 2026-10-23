using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>The special battles, end to end on NA 1.0's data: Lugash's, Pochanargat's, the Goblin Ambush's and the Shadow's.</summary>
public sealed partial class GameSessionTests
{
    private static readonly RecordId Lugash = RecordId.Hex(RecordTypes.Entity, 0x0262), Pochanargat = RecordId.Hex(RecordTypes.Entity, 0x0224);

    /// <summary>A script's battle, as its cut-scene's end starts it (<see cref="BattleSetup.Scripted"/>), begun and faded in.</summary>
    private Battle ScriptBattle(GameSession session, int script)
    {
        var dialogue = Data.Set.Get<DialogueRecord>(RecordId.Hex(RecordTypes.Dialogue, script));
        var ops = dialogue.Nodes.SelectMany(n => n.Ops ?? []).ToList();
        var enemies = ops.Where(o => o.Command == DialogueCommand.AddEnemy).Select(o => o.Target!.Value).ToList();
        var arena = ops.First(o => o.Command == DialogueCommand.SetBattlefield).Value!.Value;
        var (place, terrain) = session.Trek.Here();
        var setup = BattleSetup.Scripted(enemies, arena, 0, 0, dialogue.Mode != DialogueMode.Cutscene, dialogue.BattleFlag)
            with { Place = place, Terrain = terrain };
        session.CueBattle(setup);
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.BattleCued; i++)
            step = Tick(session);
        session.BeginBattle();
        return session.Battle!;
    }

    [Fact]
    public void LugashsCutScenesBattleMayBeFledAndBeatingHimSetsHisFlagButNotItsOwn()
    {
        var (session, _) = Start();
        var battle = ScriptBattle(session, 0x04D1);
        var lugash = Assert.Single(battle.Combatants, c => c.Entity == Lugash);

        // A cut-scene's battle can't be fled, but its encounter flag, 5250, has the last word; Lugash fights in the bosses' style.
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 5250), battle.Setup.EncounterFlag);
        Assert.True(battle.CanFlee);
        Assert.InRange(lugash.Ai!.Style, 9, 10);

        // Won, his boss flag is set, and the encounter flag, derived, isn't.
        battle.Rout();
        battle.EndTurn(battle.Current!);
        Assert.Equal(CombatState.Victory, battle.State);
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.True(session.Game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 533)));
        Assert.Equal(BattleOutcome.Won, battle.Outcome);
    }

    [Fact]
    public void LugashsSecondCutScenesBattleCantBeFled()
    {
        var (session, _) = Start();
        var battle = ScriptBattle(session, 0x04D6);
        Assert.Null(battle.Setup.EncounterFlag);
        Assert.False(battle.CanFlee);
    }

    [Fact]
    public void PochanargatNeverMovesCantBeFledAndBeatingHimSetsHisFlag()
    {
        var (session, _) = Start();
        var battle = ScriptBattle(session, 0x04FD);
        var pochanargat = Assert.Single(battle.Combatants, c => c.Entity == Pochanargat);
        Assert.Equal(0, pochanargat.Radius);
        Assert.False(battle.CanFlee);

        battle.Rout();
        battle.EndTurn(battle.Current!);
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.True(session.Game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 3204)));
    }

    [Fact]
    public void TheDebugWinWaitsForAPlayersTurnThenWinsButNeverTheShadowsBattle()
    {
        var (session, host) = Start();
        var (battle, _, _) = RatBattle(session, host);
        for (var i = 0; i < 2000 && !(battle.State == CombatState.Turn && !battle.Fade.Fading && battle.Current is { Controlled: false }); i++)
        {
            Assert.False(battle.ForceWin());
            Tick(session);
        }
        Assert.True(battle.ForceWin());
        Assert.Equal(CombatState.Victory, battle.State);
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.Equal(BattleOutcome.Won, battle.Outcome);

        var (shadows, _) = Start();
        var shadow = ScriptBattle(shadows, 0x046E);
        for (var i = 0; i < 600; i++)
        {
            Assert.False(shadow.ForceWin());
            Tick(shadows);
        }
        Assert.NotNull(shadow.Shadow);
    }

    private static readonly RecordId Horn = RecordId.Hex(RecordTypes.Item, 0x0D03), Merged = RecordId.Decimal(RecordTypes.Flag, 566);

    [Fact]
    public void TheGoblinAmbushHealsAlaronThenHoldsHimToOneAHitAndTheSecondKeelsHimOver()
    {
        var (session, _) = Start();
        var leader = session.Game.Party.Slots[0]!;
        leader.TakeDamage(20);
        var battle = ScriptBattle(session, 0x0365);
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 978), battle.Setup.EncounterFlag);

        // Combat.Init heals the first member and counts two hits.
        Assert.Equal(leader.MaxHp, leader.Hp);
        Assert.Equal(2, battle.KeelOverHits);
        var alaron = Assert.Single(battle.Combatants, c => c.Entity == GameState.Alaron);

        // A blow takes 1 and says so; nothing that misses counts.
        Assert.Equal(0, battle.KeelOver(alaron, 0));
        Assert.Equal(1, battle.KeelOver(alaron, 7));
        Assert.Equal("Alaron loses 1 hit point", battle.Lines.Queued);
        Assert.True(alaron.Active);

        // The second takes his Strength to 0: he can't act, and at the next turn's change the party has fallen, though he lives.
        Assert.Equal(1, battle.KeelOver(alaron, 3));
        Assert.Equal(0, leader.Stats.Modified(Stat.Strength));
        Assert.False(alaron.Active);
        battle.EndTurn(battle.Current!);
        Assert.Equal(CombatState.DefeatWait, battle.State);
        Assert.False(leader.IsDead);
        Assert.True(battle.Lines.Locked);
    }

    [Fact]
    public void WithTheHornTheShadowIsQuickAndReachingAlaronMergesThenItsCinematicPlaysAndTrekComesBack()
    {
        var (session, host) = Start();
        var game = session.Game;
        game.Party.Inventory.Add(Horn);
        var alaronSheet = game.Party.Slots[0]!;
        var (strength, stamina, protection) = (alaronSheet.Stats.Base(Stat.Strength), alaronSheet.Stats.Base(Stat.Stamina), alaronSheet.Protection);
        var battle = ScriptBattle(session, 0x046E);
        var shadow = battle.Shadow!;
        var alaron = battle.Merger!;
        Assert.Equal(30, shadow.Sheet.Stats.Base(Stat.Dexterity));
        Assert.True(battle.CanFlee);
        Assert.True(battle.TeleportBarred && battle.HealingBarred);

        // Within 10 of it, Alaron's field is 1.
        Assert.True(battle.NearShadow(alaron) && battle.NearShadow(shadow));
        alaron.UpdateRadius(battle);
        Assert.Equal(1, alaron.Radius);

        // Its body reaching his: state 17, the boost, and the battle leaves for the cinematic.
        shadow.Body.Field = null;
        shadow.Body.Sphere.Position = alaron.Body.Sphere.Position + new Vector3(1, 0, 0);
        Tick(session);
        Assert.Equal(CombatState.ShadowMerge, battle.State);
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.Equal(BattleOutcome.Merged, battle.Outcome);
        Assert.True(game.Flags.IsSet(Merged));
        Assert.Equal((strength + 6, stamina + 10, protection + 5), (alaronSheet.Stats.Base(Stat.Strength), alaronSheet.Stats.Base(Stat.Stamina), alaronSheet.Protection));
        Assert.Equal((10, 10), (alaronSheet.Skills.Base(Skill.Stealth), alaronSheet.Skills.Base(Skill.Wizard)));
        Assert.All(Enumerable.Range(0x31C, 5), n => Assert.Equal(10, alaronSheet.FindSpell(RecordId.Hex(RecordTypes.Spell, n))!.Level));

        host.Transitions.Clear();
        session.EndBattle();
        Assert.Equal(GameMode.Cinematic, session.Mode);
        Assert.Equal(RecordId.Decimal(RecordTypes.Cinematic, 1), session.Cinematic!.Record.Id);
        Assert.False(game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 1004)));
        PlayOut(session);
        Assert.True(game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 569)));
        Assert.IsType<Transition.ResumeTrek>(host.Transitions.Last());
        Assert.Equal(GameMode.Trek, session.Mode);
    }

    [Fact]
    public void WithoutTheHornTheShadowsAuraHurtsFourRoundsAndItVanishesAtTheFifth()
    {
        var (session, _) = Start();
        var game = session.Game;
        var battle = ScriptBattle(session, 0x046E);
        var shadow = battle.Shadow!;
        var alaron = battle.Merger!;
        Assert.Equal(1, shadow.Sheet.Stats.Base(Stat.Dexterity));

        // 8 from it: (10 − 8)² = 4 a round.
        alaron.Cell = shadow.Cell + new Vector2(8, 0);
        for (var round = 1; round <= 4; round++)
        {
            var hp = alaron.Sheet.Hp;
            Assert.False(battle.ShadowAura());
            Assert.Equal(hp - 4, alaron.Sheet.Hp);
            Assert.Equal(round, battle.ShadowRounds);
        }
        var before = alaron.Sheet.Hp;
        Assert.True(battle.ShadowAura());
        Assert.Equal(before, alaron.Sheet.Hp);
        Assert.Equal(CombatState.ShadowVanish, battle.State);
        Assert.Equal("The Shadow dissipates", battle.Lines.Shown);

        // The line holds 56 frames, then the fade, and the battle leaves with nothing won.
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        Assert.Equal(BattleOutcome.Vanished, battle.Outcome);
        session.EndBattle();
        Assert.False(game.Flags.IsSet(Merged));
        Assert.Equal(GameMode.Trek, session.Mode);
    }

    [Fact]
    public void WithoutTheHornAnAuraThatWouldKillVanishesTheShadow()
    {
        var (session, _) = Start();
        var battle = ScriptBattle(session, 0x046E);
        var alaron = battle.Merger!;
        // 1 from it: (10 − 1)² = 81, more than his hit points, which it leaves as they are.
        alaron.Cell = battle.Shadow!.Cell + new Vector2(1, 0);
        var hp = alaron.Sheet.Hp;
        Assert.True(hp <= 81);
        Assert.True(battle.ShadowAura());
        Assert.Equal((CombatState.ShadowVanish, hp), (battle.State, alaron.Sheet.Hp));
    }

    [Fact]
    public void PotionsHealNothingAndTheHealerIsRefusedWhileTheShadowIsInTheBattle()
    {
        var (session, _) = Start();
        var game = session.Game;
        var potion = Data.Set.All<ItemRecord>(RecordTypes.Item).First(i => i.Name == "Healing Potion").Id;
        game.Party.Inventory.Add(potion, 2);
        var battle = ScriptBattle(session, 0x046E);
        var alaron = battle.Merger!;
        alaron.Sheet.TakeDamage(10);
        var hp = alaron.Sheet.Hp;
        var draws = game.Random.Combat.Seed;
        Assert.Equal(DrinkResult.Drunk, Potions.Drink(game.Party, alaron.Sheet, game.Party.Inventory.Catalog[potion], game.Random.Combat, alaron, battle));
        Assert.Equal((hp, draws), (alaron.Sheet.Hp, game.Random.Combat.Seed));

        Assert.Equal(HealOutcome.Barred, Crafting.LayOnHands(alaron.Sheet, alaron.Sheet, barred: true).Outcome);

        // The Healer's turn says why, rather than 1.0's "fails to perform healing" over it (the user's choice).
        alaron.Flags |= CombatantFlags.LayOnHands;
        Assert.Equal(0, battle.ResolveHealer(alaron, alaron));
        Assert.Equal((hp, "The Shadow's presence prevents all healing."), (alaron.Sheet.Hp, battle.Lines.Shown));
        Assert.Equal(battle.Lines.Shown, battle.Lines.Queued);
    }
}
