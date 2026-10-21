using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

using static Aidyn.Rom.Tests.CombatCheckpoints;

namespace Aidyn.Rom.Tests;

/// <summary>
/// A member's turn played through the menus and the states, headless, in the rats' battle the port sets up from checkpoint 01: a spell
/// from C-down, a potion from C-up, a performance, healing and a thrown flask, each ending the turn as an attack does.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class BattleActionTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private sealed record Run(Built Built, ZoneEngine Zone)
    {
        public Battle Battle => Built.Battle;

        public void Tick() => Battle.Tick(Zone, false, _ => null);

        /// <summary>Plays on, skipping the other members' turns, until <paramref name="name"/>'s turn, faded in.</summary>
        public Combatant TurnOf(string name)
        {
            for (var i = 0; i < 20000; i++)
            {
                if (Battle.Current is { } c && Battle.State == CombatState.Turn && !Battle.Fade.Fading && Battle.Lines.Hold is null)
                {
                    if (c.Sheet.Name == name && !c.Controlled)
                        return c;
                    if (!c.Controlled)
                        Battle.Skip();
                }
                Tick();
            }
            throw new InvalidOperationException($"{name}'s turn never came.");
        }

        /// <summary>Ticks until <paramref name="c"/>'s turn has ended.</summary>
        public void UntilTurnEnds(Combatant c)
        {
            for (var i = 0; i < 2000 && Battle.Current == c; i++)
                Tick();
            Assert.NotSame(c, Battle.Current);
        }
    }

    private Run Start()
    {
        var built = Plain(Set, 1);
        return new Run(built, Zone(built));
    }

    /// <summary>Opens a held menu, moves to the row that <paramref name="pick"/> finds, and lets go.</summary>
    private static void Choose(Battle battle, bool items, Func<CombatMenuRow, bool> pick)
    {
        Assert.True(battle.OpenMenu(items));
        Assert.Equal(items ? CombatState.MenuItems : CombatState.MenuSpells, battle.State);
        var menu = battle.Menu!;
        var row = menu.Rows.Select((r, i) => (r, i)).First(p => pick(p.r)).i;
        for (var i = 0; i < row; i++)
            battle.MoveMenu(1);
        Assert.True(battle.CloseMenu(items));
        Assert.Null(battle.Menu);
    }

    [Fact]
    public void RhedasEnduranceFromCDownCastsAtTwoAndEndsTheTurn()
    {
        var run = Start();
        var rheda = run.TurnOf("Rheda");
        var spice = run.Built.Game.Party.Inventory.Count(RecordId.Hex(RecordTypes.Item, 0x0111));
        var stamina = rheda.Sheet.Stats.Modified(Aidyn.Core.Characters.Stat.Stamina);
        Choose(run.Battle, false, r => r.Text == "Endurance");
        Assert.Equal(CombatState.TargetSpell, run.Battle.State);
        Assert.Equal(2, run.Battle.TargetsB.Selected);
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(rheda);

        var cast = run.Battle.Combatants.Count(c => c.Sheet.Effects.Any(e => e.Source == RecordId.Hex(RecordTypes.Spell, 0x0310)));
        Assert.True(cast is 2 || run.Battle.Log.Any(l => l.Contains("fails to cast", StringComparison.Ordinal)));
        Assert.True(spice - run.Built.Game.Party.Inventory.Count(RecordId.Hex(RecordTypes.Item, 0x0111)) <= 1);
        Assert.True(rheda.Sheet.Stats.Modified(Aidyn.Core.Characters.Stat.Stamina) < stamina);
        Assert.Equal(-1, rheda.CastMargin);
    }

    [Fact]
    public void AStrengthPotionFromCUpIsDrunkByTheMemberAndTheActionComesBack()
    {
        var run = Start();
        var alaron = run.TurnOf("Alaron");
        var action = alaron.Action;
        var strength = alaron.Sheet.Stats.Modified(Aidyn.Core.Characters.Stat.Strength);
        var potions = run.Built.Game.Party.Inventory.Count(PotionId(BasePotion.Strength));
        Choose(run.Battle, true, r => r.Potion?.Id == PotionId(BasePotion.Strength));

        // State 9 with the drinker alone in set A, selected; A drinks.
        Assert.Equal(CombatState.TargetAttack, run.Battle.State);
        Assert.Equal([alaron.Slot], run.Battle.TargetsA.SelectedSlots);
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(alaron);
        Assert.Equal(strength + 10, alaron.Sheet.Stats.Modified(Aidyn.Core.Characters.Stat.Strength));
        Assert.Equal(potions - 1, run.Built.Game.Party.Inventory.Count(PotionId(BasePotion.Strength)));
        Assert.Equal(action, alaron.Action);
        Assert.Contains(run.Battle.Log, l => l.Contains("Alaron", StringComparison.Ordinal) && l.Contains("otion", StringComparison.Ordinal));
    }

    [Fact]
    public void BOnAPotionsAimPutsTheActionBack()
    {
        var run = Start();
        var alaron = run.TurnOf("Alaron");
        var action = alaron.Action;
        Choose(run.Battle, true, r => r.Potion?.Id == PotionId(BasePotion.Strength));
        Assert.Equal(CombatAction.Flask, alaron.Action);
        Assert.True(run.Battle.Cancel());
        Assert.Equal((CombatState.Turn, action), (run.Battle.State, alaron.Action));
    }

    [Fact]
    public void AlaronsPerformanceRestsTheTurnAndEitherPerformsOrFails()
    {
        var run = Start();
        var alaron = run.TurnOf("Alaron");
        Choose(run.Battle, false, r => r.Choice == CombatMenuChoice.Perform);
        Assert.Equal(CombatState.TroubadourPrompt, run.Battle.State);
        Assert.Equal([alaron.Slot], run.Battle.TargetsA.SelectedSlots);
        Assert.True(run.Battle.Confirm());
        Assert.Equal(CombatState.Perform, run.Battle.State);
        Assert.Equal(alaron.Performing, run.Battle.TroubadourBonus > 0);
        run.UntilTurnEnds(alaron);
    }

    [Fact]
    public void AFlaskThrownAtARatIsUsedUpAndEndsTheTurn()
    {
        var run = Start();
        var brenna = run.TurnOf("Brenna");
        var inventory = run.Built.Game.Party.Inventory;
        inventory.Add(PotionId(BasePotion.FireFlask), 2);
        brenna.HoldPotion(run.Battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], run.Battle);

        // A in the turn: a flask aims freely (state 18), at the other side alone, with no reach.
        Assert.True(run.Battle.Attack());
        Assert.Equal(CombatState.TargetThrown, run.Battle.State);
        Assert.All(run.Battle.TargetsA.SelectedSlots, s => Assert.True(run.Battle.Slots[s]!.Hostile));
        Assert.True(run.Battle.Confirm());
        var flew = false;
        for (var i = 0; i < 2000 && run.Battle.Current == brenna; i++)
        {
            run.Tick();
            flew |= run.Battle.Flight is { Scene: var scene } && scene == Battle.FlaskScene;
        }
        Assert.NotSame(brenna, run.Battle.Current);
        Assert.Equal(1, inventory.Count(PotionId(BasePotion.FireFlask)));
        // One more is left, so the next flask is still in hand, and the weapon was put away.
        Assert.True(brenna.HoldsFlask);
        Assert.Null(brenna.Sheet.Equipment.Weapon);
        Assert.True(flew || run.Battle.Log.Any(l => l.Contains("misses", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheLastFlaskOfAStackFliesAndTheHandIsEmptiedAtTheTurnsEnd()
    {
        // The port's fix, the user's choice: the original empties the hand as the last is thrown, so it never flies or bursts.
        var run = Start();
        var brenna = run.TurnOf("Brenna");
        var inventory = run.Built.Game.Party.Inventory;
        while (inventory.Has(PotionId(BasePotion.FireFlask)))
            inventory.Take(PotionId(BasePotion.FireFlask));
        inventory.Add(PotionId(BasePotion.FireFlask));
        brenna.HoldPotion(run.Battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], run.Battle);
        Assert.True(run.Battle.Attack());
        Assert.True(run.Battle.Confirm());
        var flew = false;
        for (var i = 0; i < 2000 && run.Battle.Current == brenna; i++)
        {
            run.Tick();
            flew |= run.Battle.Flight is { Scene: var scene } && scene == Battle.FlaskScene;
        }
        Assert.True(flew);
        Assert.False(inventory.Has(PotionId(BasePotion.FireFlask)));
        Assert.False(brenna.HoldsFlask);
        Assert.Equal(CombatAction.None, brenna.Action);
    }

    [Fact]
    public void EscapeTakesItsTargetsOffTheFieldAndTheWholePartyGoneHasFled()
    {
        // The port's fix, the user's choice: the original's state 2 waits for an escaped member's body for ever.
        var run = Start();
        var rheda = run.TurnOf("Rheda");
        var escape = run.Built.Game.Rules.Magic!.Spell(RecordId.Hex(RecordTypes.Spell, 0x033C))!;
        rheda.Sheet.LearnSpell(escape);
        var learned = rheda.Sheet.Spells[^1];
        learned.Level = 10;
        rheda.Sheet.Skills.Train(Aidyn.Core.Characters.Skill.Wizard, 10);
        Choose(run.Battle, false, r => r.Text == "Escape");
        Assert.Equal(CombatState.TargetSpell, run.Battle.State);
        var targets = run.Battle.TargetsB.SelectedSlots.Select(s => run.Battle.Slots[s]!).ToList();
        Assert.NotEmpty(targets);
        Assert.All(targets, t => Assert.False(t.Hostile));
        Assert.True(run.Battle.Confirm());
        for (var i = 0; i < 3000 && run.Battle.Current == rheda && !run.Battle.Over; i++)
            run.Tick();

        if (run.Battle.Log.Any(l => l.Contains("fails to cast", StringComparison.Ordinal)))
            return;
        Assert.All(targets, t => Assert.True(t.Gone && !t.Sheet.IsDead));
        var everyone = run.Battle.Combatants.Where(c => !c.Hostile).All(targets.Contains);
        Assert.Equal(everyone, run.Battle.Fled);
        if (everyone)
            return;
        // The battle goes on with whoever's left; once the last of the party is gone too, it has fled at the next turn's change.
        Assert.NotSame(rheda, run.Battle.Current);
        Assert.False(run.Battle.Current!.Gone);
        foreach (var left in run.Battle.Combatants.Where(c => !c.Hostile && !c.Gone).ToList())
            run.Battle.Leave(left);
        run.Battle.EndTurn(run.Battle.Current);
        Assert.True(run.Battle.Fled);
        Assert.Equal(BattleOutcome.Fled, run.Battle.Outcome);
    }

    [Fact]
    public void AHealerHealsHerselfWithAHerbFromCDown()
    {
        var run = Start();
        var brenna = run.TurnOf("Brenna");
        brenna.Sheet.Skills.Train(Aidyn.Core.Characters.Skill.Healer, 3);
        brenna.Sheet.TakeDamage(10);
        var herb = run.Built.Game.Party.Inventory.Catalog.Rules.Herb;
        run.Built.Game.Party.Inventory.Add(herb);
        var herbs = run.Built.Game.Party.Inventory.Count(herb);
        var hp = brenna.Sheet.Hp;
        Choose(run.Battle, false, r => r.Choice == CombatMenuChoice.HealWithHerbs);
        Assert.Equal(CombatState.TargetAttack, run.Battle.State);
        Assert.True(brenna.Flags.HasFlag(CombatantFlags.HealWithHerbs));
        Assert.Contains(brenna.Slot, run.Battle.TargetsA.SelectedSlots.Concat(Enumerable.Range(0, Battle.SlotCount).Where(run.Battle.TargetsA.IsCandidate)));
        while (run.Battle.TargetsA.Cursor != brenna.Slot)
            run.Battle.StepTarget(1);
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(brenna);
        Assert.Equal(herbs - 1, run.Built.Game.Party.Inventory.Count(herb));
        Assert.False(brenna.Healing);
        Assert.True(brenna.Sheet.Hp > hp || run.Battle.Log.Any(l => l.Contains("fails to perform healing", StringComparison.Ordinal)));
    }

    [Fact]
    public void SenseAurasPopupHoldsTheTurnUntilAPress()
    {
        var run = Start();
        var rheda = run.TurnOf("Rheda");
        Choose(run.Battle, false, r => r.Text == "Sense Aura");
        Assert.Equal(CombatState.TargetSpell, run.Battle.State);
        var target = run.Battle.Slots[run.Battle.TargetsB.Cursor]!;
        Assert.True(target.Hostile);
        Assert.True(run.Battle.Confirm());
        for (var i = 0; i < 1000 && run.Battle.Aura is null && run.Battle.Current == rheda; i++)
            run.Tick();
        if (run.Battle.Aura is null)
        {
            // The cast failed its roll: the popup never came.
            Assert.Contains(run.Battle.Log, l => l.Contains("fails to cast", StringComparison.Ordinal));
            return;
        }
        // Its name, then "Follower" or "Leader" and on with the level (Rheda's Sense Aura is level 2 or more).
        var lines = run.Battle.Aura.Split('\n');
        Assert.Equal(target.Sheet.Name, lines[0]);
        Assert.Contains(lines[1], new[] { "Follower", "Leader" });
        for (var i = 0; i < 400; i++)
            run.Tick();
        Assert.Same(rheda, run.Battle.Current);
        Assert.True(run.Battle.DismissAura());
        Assert.NotSame(rheda, run.Battle.Current);
    }

    [Fact]
    public void APlacedAreasCentreMovesWithTheStickWithinItsReachAndTakesEveryoneInIt()
    {
        var run = Start();
        var sholeh = run.Battle.Combatants.Single(c => c.Sheet.Name == "Sholeh");
        // Sholeh plays herself here, so her Dispel Naming (a placed area, level × range from her body) can be aimed.
        sholeh.Controlled = false;
        sholeh.Ai = null;
        Assert.Same(sholeh, run.TurnOf("Sholeh"));
        Choose(run.Battle, false, r => r.Text == "Dispel Naming");
        Assert.Equal(CombatState.PlaceArea, run.Battle.State);
        Assert.Equal(sholeh.Cell, run.Battle.AreaAim);
        var source = sholeh.CurrentSpell(run.Built.Game.Rules.Magic)!;
        var reach = source.Spell.Range * Aidyn.Core.Magic.Casting.Level(sholeh.Sheet, source);

        // A second of the stick pushed: the centre moves, but never past the reach from her body.
        for (var i = 0; i < 120; i++)
            run.Battle.Tick(run.Zone, false, _ => null, () => new Aidyn.Core.Trek.PlayerInput(new System.Numerics.Vector2(0, 1)));
        var body = sholeh.Body.Sphere.Position - run.Battle.Origin;
        var off = run.Battle.AreaAim - new System.Numerics.Vector2(body.X, body.Z);
        Assert.True(off.Length() > 0.5f);
        Assert.True(off.Length() <= reach + 0.001f);
        Assert.Equal(new AimMarker(run.Battle.AreaAim, Aidyn.Core.Magic.Casting.Level(sholeh.Sheet, source), Spell: true), run.Battle.Aim);

        // Everyone in it is taken, either side, an All spell's every candidate.
        Assert.Equal(run.Battle.TargetsB.Candidates, run.Battle.TargetsB.Selected);
        if (run.Battle.TargetsB.Selected == 0)
        {
            // Nobody in it: A puts the aim away.
            Assert.True(run.Battle.Confirm());
            Assert.Equal(CombatState.Turn, run.Battle.State);
            return;
        }
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(sholeh);
    }

    [Fact]
    public void AnEmptyMenuShowsItsMessageAndChoosesNothing()
    {
        var run = Start();
        var brenna = run.TurnOf("Brenna");
        Assert.True(run.Battle.OpenMenu(false));
        Assert.Equal("No spells or skills available", run.Battle.Menu!.Empty);
        Assert.True(run.Battle.CloseMenu(false));
        Assert.Equal(CombatState.Turn, run.Battle.State);
        Assert.Same(brenna, run.Battle.Current);
    }
}
