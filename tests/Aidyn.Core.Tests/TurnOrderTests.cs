using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Combat;
using Aidyn.Core.Movement;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// Hand-made battles for the rules that only read a battle's combatants, sides, setup and arena (<c>Battle.Of</c>), where
/// <c>Battle.Begin</c> needs a whole game and a loaded arena.
/// </summary>
internal static class BattleFixtures
{
    private static readonly BattleSetup NoSetup = new(BattleSource.Script, null, null, null, [], null, false, false, null);

    /// <summary>A combat record that names no one and has no rules, as a hand-made battle plays by.</summary>
    private static readonly CombatRecord NoRules = new(RecordId.Decimal(RecordTypes.Combat, 0), Entity(0), [], Entity(0), [], [], []);

    public static Battle Create(int partySize, int onField, params Combatant?[] combatants) =>
        Battle.Of(NoSetup, NoRules, null, partySize, onField, combatants);

    /// <summary>A battle of <paramref name="setup"/> in <paramref name="arena"/>, with no one in it yet.</summary>
    public static Battle Create(BattleSetup setup, Arena arena) => Battle.Of(setup, NoRules, arena, 0, 0, []);

    /// <summary>A combatant of base Dexterity <paramref name="dexterity"/> in slot <paramref name="slot"/>.</summary>
    public static Combatant Fighter(int slot, int dexterity, bool dead = false, bool gone = false, bool active = true)
    {
        var entity = Characters.Alaron() with { Id = Entity(0x2000 + slot), Attributes = new Attributes(15, 12, dexterity, 15, 15, 30) };
        var sheet = Characters.Create(entity);
        if (dead)
        {
            sheet.TakeDamage(1000);
            Assert.True(sheet.IsDead);
        }
        var flags = (active ? CombatantFlags.Active : CombatantFlags.None) | (gone ? CombatantFlags.Gone : CombatantFlags.None);
        var look = new EntityBody(RecordId.Hex(RecordTypes.Actor, 0x2D4A), 0.7f, 1, 1);
        return new Combatant(slot, sheet, look, new Body(Vector3.Zero, 0.7f), flags);
    }
}

/// <summary>
/// A round's turns (<c>TurnOrder.Build</c>, <c>0x8006A16C</c>, and <c>Combatant.TurnsPerRound</c>, <c>0x8005F4D0</c>).
/// </summary>
public sealed class TurnOrderTests
{
    // 0x8005F4D0: base Dexterity d: d <= 0 -> 0, < 18 -> 1, 18-24 -> 1 or 2, 25-29 -> 2, 30-34 -> 2 or 3, >= 35 -> 3,
    // where the two are 1 and 2 (or 2 and 3) with the toggle clear and set.
    [Theory]
    [InlineData(-5, 0, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(1, 1, 1)]
    [InlineData(17, 1, 1)]
    [InlineData(18, 1, 2)]
    [InlineData(24, 1, 2)]
    [InlineData(25, 2, 2)]
    [InlineData(29, 2, 2)]
    [InlineData(30, 2, 3)]
    [InlineData(34, 2, 3)]
    [InlineData(35, 3, 3)]
    [InlineData(60, 3, 3)]
    public void ADexteritysBandIsHowManyTurnsARoundTakesWithTheToggleClearAndSet(int dexterity, int clear, int set)
    {
        Assert.Equal(clear, TurnOrder.TurnsPerRound(dexterity, toggle: false));
        Assert.Equal(set, TurnOrder.TurnsPerRound(dexterity, toggle: true));
    }

    // Groups are drawn in the order none (toggle only), third, second, first, each by Range(0, n - 1) until every member of it
    // is out, drawing again on a slot not in the group or already out.
    [Fact]
    public void TheSecondTurnsComeBeforeTheFirstAndEachGroupIsDrawnByRepeatedRolls()
    {
        // Dexterity 30 takes 2 turns, 20 takes 1, 10 takes 1, 0 none.
        var battle = BattleFixtures.Create(2, 2, BattleFixtures.Fighter(0, 30), BattleFixtures.Fighter(1, 20),
            BattleFixtures.Fighter(2, 10), BattleFixtures.Fighter(3, 0));
        // Second turns {0}: 2 misses, 0 hits. First turns {0, 1, 2}: 3 misses, 1 hits, 1 again misses, 2 hits, 0 hits.
        var random = new ScriptedRandom(Below4(2), Below4(0), Below4(3), Below4(1), Below4(1), Below4(2), Below4(0));

        battle.Order.Build(battle, random);

        Assert.Equal([0, 1, 2, 0], battle.Order.Turns);
        Assert.Equal(0, random.Remaining);
        Assert.Equal(0, battle.Order.Now);
        Assert.Same(battle.Slots[0], battle.Order.Current(battle));
    }

    [Fact]
    public void WithTheToggleSetTheNoTurnOnesGoFirstThenThirdSecondAndFirstTurns()
    {
        var battle = BattleFixtures.Create(2, 2, BattleFixtures.Fighter(0, 30), BattleFixtures.Fighter(1, 20),
            BattleFixtures.Fighter(2, 10), BattleFixtures.Fighter(3, 0));
        battle.Order.Toggle = true;
        // Now 30 takes 3 turns, 20 takes 2, 10 takes 1 and 0 takes one of the toggle's own.
        // None {3}: 3. Third {0}: 0. Second {0, 1}: 0, 1. First {0, 1, 2}: 2, 0, 1.
        var random = new ScriptedRandom(Below4(3), Below4(0), Below4(0), Below4(1), Below4(2), Below4(0), Below4(1));

        battle.Order.Build(battle, random);

        Assert.Equal([3, 0, 0, 1, 2, 0, 1], battle.Order.Turns);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void WithTheToggleClearACombatantWithNoTurnsGetsNone()
    {
        var battle = BattleFixtures.Create(1, 1, BattleFixtures.Fighter(0, 10), BattleFixtures.Fighter(1, 0));
        var random = new ScriptedRandom(BattleDice.Range(0, 1, 0));

        battle.Order.Build(battle, random);

        Assert.Equal([0], battle.Order.Turns);
    }

    [Fact]
    public void ALoneCombatantDrawsNothing()
    {
        var battle = BattleFixtures.Create(1, 0, BattleFixtures.Fighter(0, 30));
        battle.Order.Toggle = true;

        // Range(0, 0) draws nothing, so a scripted stream with no values would throw if it were asked.
        battle.Order.Build(battle, new ScriptedRandom());

        Assert.Equal([0, 0, 0], battle.Order.Turns);
    }

    [Fact]
    public void TheDeadTheGoneAndTheInactiveTakeNoTurnsAndOnlyTheLiveAreCounted()
    {
        // Party of 2 (slots 0 and 1), 3 enemies (slots 2, 3 and 4): 1 is dead, 3 has gone, 4 is alive but can't act.
        var battle = BattleFixtures.Create(2, 3, BattleFixtures.Fighter(0, 20), BattleFixtures.Fighter(1, 20, dead: true),
            BattleFixtures.Fighter(2, 10), BattleFixtures.Fighter(3, 10, gone: true), BattleFixtures.Fighter(4, 10, active: false));
        // First turns {0, 2} over 5 slots: 4 misses, 0, 3 misses, 2.
        var random = new ScriptedRandom(Below(5, 4), Below(5, 0), Below(5, 3), Below(5, 2));

        battle.Order.Build(battle, random);

        Assert.Equal([0, 2], battle.Order.Turns);
        // Counted: 0, 2 and 4 (alive and in play); the party's is 0, the enemies' 2 and 4.
        Assert.Equal((3, 1, 2), battle.ActiveCounts);
        Assert.Equal(0, random.Remaining);
    }

    [Fact]
    public void EveryoneOutLeavesNoTurnsAndNoCurrentCombatantAndDrawsNothing()
    {
        var battle = BattleFixtures.Create(1, 1, BattleFixtures.Fighter(0, 20, dead: true), BattleFixtures.Fighter(1, 20, gone: true));

        battle.Order.Build(battle, new ScriptedRandom());

        Assert.Empty(battle.Order.Turns);
        Assert.Null(battle.Order.Current(battle));
        Assert.Equal((0, 0, 0), battle.ActiveCounts);
    }

    [Fact]
    public void AnEmptySlotIsSkipped()
    {
        var battle = BattleFixtures.Create(2, 0, BattleFixtures.Fighter(0, 20), null);
        var random = new ScriptedRandom(Below(2, 1), Below(2, 0));

        battle.Order.Build(battle, random);

        Assert.Equal([0], battle.Order.Turns);
        Assert.Equal((1, 1, 0), battle.ActiveCounts);
    }

    [Fact]
    public void BuildingAgainStartsTheRoundOver()
    {
        var battle = BattleFixtures.Create(1, 1, BattleFixtures.Fighter(0, 10), BattleFixtures.Fighter(1, 10));
        battle.Order.Build(battle, new ScriptedRandom(BattleDice.Range(0, 1, 1), BattleDice.Range(0, 1, 0)));
        battle.Order.Now = 1;

        battle.Order.Build(battle, new ScriptedRandom(BattleDice.Range(0, 1, 0), BattleDice.Range(0, 1, 1)));

        Assert.Equal([0, 1], battle.Order.Turns);
        Assert.Equal(0, battle.Order.Now);
    }

    private static uint Below4(int value) => Below(4, value);

    private static uint Below(int n, int value) => BattleDice.Below(n, value);
}
