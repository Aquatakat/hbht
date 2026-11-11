using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// A member's turn played through the menus and the states, headless, in the rats' battle the port sets up from checkpoint 01: a spell
/// from C-down, a potion from C-up, a performance, healing and a thrown flask, each ending the turn as an attack does.
/// </summary>
public sealed class BattleActionTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private sealed record Run(Built Built, ZoneEngine Zone)
    {
        public Battle Battle => Built.Battle;

        public void Tick() => Battle.Tick(Zone);

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
        var stamina = rheda.Sheet.Stats.Modified(HBHT.Core.Characters.Stat.Stamina);
        Choose(run.Battle, false, r => r.Text == "Endurance");
        Assert.Equal(CombatState.TargetSpell, run.Battle.State);
        Assert.Equal(2, run.Battle.TargetsB.Selected);
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(rheda);

        var cast = run.Battle.Combatants.Count(c => c.Sheet.Effects.Any(e => e.Source == RecordId.Hex(RecordTypes.Spell, 0x0310)));
        Assert.True(cast is 2 || run.Battle.Log.Any(l => l.Contains("fails to cast", StringComparison.Ordinal)));
        Assert.True(spice - run.Built.Game.Party.Inventory.Count(RecordId.Hex(RecordTypes.Item, 0x0111)) <= 1);
        Assert.True(rheda.Sheet.Stats.Modified(HBHT.Core.Characters.Stat.Stamina) < stamina);
        Assert.Equal(-1, rheda.CastMargin);
    }

    [Fact]
    public void AStrengthPotionFromCUpIsDrunkByTheMemberAndTheActionComesBack()
    {
        var run = Start();
        var alaron = run.TurnOf("Alaron");
        var action = alaron.Action;
        var strength = alaron.Sheet.Stats.Modified(HBHT.Core.Characters.Stat.Strength);
        var potions = run.Built.Game.Party.Inventory.Count(PotionId(BasePotion.Strength));
        Choose(run.Battle, true, r => r.Potion?.Id == PotionId(BasePotion.Strength));

        // State 9 with the drinker alone in set A, selected; A drinks.
        Assert.Equal(CombatState.TargetAttack, run.Battle.State);
        Assert.Equal([alaron.Slot], run.Battle.TargetsA.SelectedSlots);
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(alaron);
        Assert.Equal(strength + 10, alaron.Sheet.Stats.Modified(HBHT.Core.Characters.Stat.Strength));
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
    public void ABowStillAimsFreelyAndLoosesItsArrowAfterAPotionIsPutAwayOrDrunk()
    {
        // The user's Sholeh (Nov 11): her bow's free cursor (state 18) gave way to a blow's aim (state 9) for the rest of a battle, and
        // her shots' results came with no arrow flown. A potion in hand had put her action back with the drawn flag cleared, where
        // the original works the flag out from the weapon each time the hands are filled (Combatant.UpdateDrawnFlag, 0x80066A2C).
        var run = Start();
        var sholeh = run.TurnOf("Sholeh");
        // Checkpoint 01's Sholeh carries no bow: the first she can wield, in hand as the battle would have made her with it.
        var catalog = run.Built.Game.Party.Inventory.Catalog;
        var held = sholeh.Sheet.Equipment.RemoveWeapon();
        var bow = Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Weapon?.Missile?.Scene is not null).Select(i => new HBHT.Core.Items.ItemInstance(catalog[i.Id]))
            .First(i => sholeh.Sheet.Equipment.EquipWeapon(i) == HBHT.Core.Items.EquipRefusal.None);
        sholeh.SetDefaultAction(run.Battle.Rules);
        TestContext.Current.TestOutputHelper?.WriteLine($"Sholeh takes {bow.Item.Record.Name} for {held?.Item.Record.Name}");
        Assert.True(sholeh.Drawn);
        var action = sholeh.Action;
        run.Built.Game.Party.Inventory.Add(PotionId(BasePotion.Strength), 2);

        // A potion in hand isn't aimed freely; put away with B, the bow is again.
        Choose(run.Battle, true, r => r.Potion?.Id == PotionId(BasePotion.Strength));
        Assert.Equal((CombatAction.Flask, false), (sholeh.Action, sholeh.Drawn));
        Assert.True(run.Battle.Cancel());
        Assert.Equal((action, true), (sholeh.Action, sholeh.Drawn));

        // And drunk: her next turn's A aims freely, and her arrow flies before the blow lands.
        Choose(run.Battle, true, r => r.Potion?.Id == PotionId(BasePotion.Strength));
        Assert.True(run.Battle.Confirm());
        run.UntilTurnEnds(sholeh);
        Assert.Equal((action, true), (sholeh.Action, sholeh.Drawn));
        for (var turn = 0; turn < 12; turn++)
        {
            run.TurnOf("Sholeh");
            if (run.Battle.Attack())
                break;
            Assert.True(run.Battle.Skip());
            run.UntilTurnEnds(sholeh);
        }
        Assert.Equal(CombatState.TargetThrown, run.Battle.State);
        Assert.True(run.Battle.Confirm());
        var flew = false;
        for (var i = 0; i < 2000 && run.Battle.Current == sholeh; i++)
        {
            run.Tick();
            flew |= run.Battle.Flight is not null;
        }
        Assert.True(flew, "no arrow flew");
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
        Assert.Equal(alaron.Performing, alaron.Held[Combatant.NotesSlot] is not null);
        run.UntilTurnEnds(alaron);
    }

    [Fact]
    public void ThePerformersNotesShowOverItsHeadUntilThePerformanceEnds()
    {
        var run = Start();
        var alaron = run.TurnOf("Alaron");
        // (Troubadour × 5 + Intelligence) × 2 of at least 100 can't fail the d100.
        alaron.Sheet.Skills.Modify(HBHT.Core.Characters.Skill.Troubadour, 10);
        Choose(run.Battle, false, r => r.Choice == CombatMenuChoice.Perform);
        Assert.Null(alaron.Held[Combatant.NotesSlot]);
        Assert.True(run.Battle.Confirm());
        Assert.True(alaron.Performing);
        // StartTroubadour's AttachItemToPlayer(body, 2, 0x1ABB): the head's locator, raised 1 in the world's Y.
        Assert.Equal(new HeldModel(Combatant.NotesScene, 3, 1), alaron.Held[Combatant.NotesSlot]);
        run.UntilTurnEnds(alaron);
        Assert.NotNull(alaron.Held[Combatant.NotesSlot]);

        // With no Stamina at the round's end the performance stops (if a blow or an engagement hasn't stopped it first), and the notes go with it.
        var stats = alaron.Sheet.Stats;
        alaron.Sheet.DrainStamina(stats.Modified(HBHT.Core.Characters.Stat.Stamina));
        Assert.Equal(0, stats.Modified(HBHT.Core.Characters.Stat.Stamina));
        // A turn of his own in the same round (a skip) keeps it going.
        for (var turn = 0; turn < 4 && alaron.Performing; turn++)
        {
            Assert.NotNull(alaron.Held[Combatant.NotesSlot]);
            run.TurnOf("Alaron");
            if (!alaron.Performing)
                break;
            Assert.True(run.Battle.Skip());
            run.UntilTurnEnds(alaron);
        }
        Assert.False(alaron.Performing);
        Assert.Null(alaron.Held[Combatant.NotesSlot]);
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
    public void AFlaskLeavesTheThrowersHandForTheTargetsCentre()
    {
        // Combat.StartCastCamera: from the hand's locator (+0x88) as the body is drawn, to the target body's centre. The core poses the
        // hand itself (Start Early), at the throw's strike.
        var run = Start();
        var brenna = run.TurnOf("Brenna");
        run.Built.Game.Party.Inventory.Add(PotionId(BasePotion.FireFlask), 2);
        brenna.HoldPotion(run.Battle.Game.Party.Inventory.Catalog[PotionId(BasePotion.FireFlask)], run.Battle);
        Assert.True(run.Battle.Attack());
        Assert.True(run.Battle.Confirm());
        MissileFlight? flight = null;
        for (var i = 0; i < 2000 && run.Battle.Current == brenna && flight is null; i++)
        {
            run.Tick();
            flight = run.Battle.Flight;
        }
        if (flight is null)
        {
            // A miss throws nothing that flies.
            Assert.Contains(run.Battle.Log, l => l.Contains("misses", StringComparison.Ordinal));
            return;
        }
        var target = run.Battle.Slots[run.Battle.Target]!;
        Assert.Equal(0, brenna.HandLocator);
        var hand = brenna.LocatorAt(brenna.HandLocator)!.Value.Translation;
        Assert.Equal(hand, flight.From);
        // Her hand, not her middle: off her body's axis and below her head.
        var feet = brenna.Body.Sphere.Position.Y - brenna.Body.Sphere.Radius;
        Assert.InRange(hand.Y - feet, 0.3f, brenna.Look.Height * brenna.Look.Scale);
        Assert.True(System.Numerics.Vector2.Distance(new(hand.X, hand.Z), new(brenna.Body.Sphere.Position.X, brenna.Body.Sphere.Position.Z)) > 0.05f);
        Assert.True(System.Numerics.Vector3.Distance(target.Body.Sphere.Position, flight.From + flight.Way * flight.Length) < 1e-3f);
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
        rheda.Sheet.Skills.Train(HBHT.Core.Characters.Skill.Wizard, 10);
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
    public void TeleportationPutsItsCasterAtThePlacedPointBeforeItsVisualPlays()
    {
        var run = Start();
        var rheda = run.TurnOf("Rheda");
        var teleportation = run.Built.Game.Rules.Magic!.Spell(RecordId.Hex(RecordTypes.Spell, 0x033A))!;
        rheda.Sheet.LearnSpell(teleportation);
        rheda.Sheet.Spells[^1].Level = 10;
        rheda.Sheet.Skills.Train(HBHT.Core.Characters.Skill.Wizard, 10);
        run.Built.Game.Party.Inventory.Add(run.Built.Game.Party.Inventory.Catalog.Rules.Gemstone);
        Choose(run.Battle, false, r => r.Text == "Teleportation");
        Assert.Equal(CombatState.PlaceArea, run.Battle.State);

        // The stick pushed a way at a time until the point is a few units off on a free cell (a cell with no floor counts as taken,
        // where Entity.Teleport spends the cast and moves no one), and clear of bodies: A does nothing until it is.
        var start = rheda.Body.Sphere.Position;
        System.Numerics.Vector2[] ways = [new(0, 1), new(1, 0), new(0, -1), new(-1, 0)];
        var confirmed = false;
        var point = run.Battle.AreaAim;
        foreach (var way in ways)
        {
            for (var step = 0; step < 8 && !confirmed; step++)
            {
                for (var i = 0; i < 6; i++)
                    run.Battle.Tick(run.Zone, () => new HBHT.Core.Trek.PlayerInput(way));
                point = run.Battle.AreaAim;
                confirmed = System.Numerics.Vector2.Distance(point, rheda.Cell) > 3
                    && !run.Battle.Grid.IsOccupied((int)point.X, (int)point.Y, rheda.Footprint) && run.Battle.Confirm();
            }
            if (confirmed)
                break;
        }
        Assert.True(confirmed);
        for (var i = 0; i < 400 && !run.Battle.Fx.Casts.Any(); i++)
            run.Tick();
        if (run.Battle.Log.Any(l => l.Contains("fails to cast", StringComparison.Ordinal)))
            return;

        // Entity.Teleport: the body at the point plus its scaled radius across, set down, as the visual starts.
        var at = rheda.Body.Sphere.Position - run.Battle.Origin;
        Assert.Equal(point.X + rheda.Body.ScaledRadius, at.X, 3);
        Assert.Equal(point.Y + rheda.Body.ScaledRadius, at.Z, 3);
        Assert.True(System.Numerics.Vector2.Distance(new(start.X, start.Z), new(rheda.Body.Sphere.Position.X, rheda.Body.Sphere.Position.Z)) > 1);
        run.UntilTurnEnds(rheda);
        // She stays there, but for what her cast's animation's root carries her, as any body's does.
        Assert.True(System.Numerics.Vector2.Distance(new System.Numerics.Vector2(at.X, at.Z), new System.Numerics.Vector2(rheda.Body.Sphere.Position.X,
            rheda.Body.Sphere.Position.Z) - new System.Numerics.Vector2(run.Battle.Origin.X, run.Battle.Origin.Z)) < 1);
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(0.5f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(0.1f, 0.25f)]
    public void ASpellLandsAtItsStrikeInItsEffectWhichStartsAtTheCastersCastStrike(float strike, float castStrike)
    {
        // The port's, the user's choice: 1.0 starts every visual with the cast and lands it as the caster's cast animation ends.
        // Rheda's vs. Naming at herself, whose effect is its caster's actor (0x1E4F's cast state, 159 ticks); her cast runs 239.
        var run = Start();
        var rheda = run.TurnOf("Rheda");
        rheda.Look = rheda.Look with { Strikes = new Dictionary<RecordId, float> { [RecordIds.Request(EntityBody.CastRequest)] = castStrike } };
        var spell = run.Built.Game.Rules.Magic!.Spell(RecordId.Hex(RecordTypes.Spell, 0x031E))! with { Strike = strike };
        rheda.Sheet.LearnSpell(spell);
        rheda.Sheet.Skills.Train(HBHT.Core.Characters.Skill.Wizard, 10);
        run.Built.Game.Party.Inventory.Add(run.Built.Game.Party.Inventory.Catalog.Rules.Gemstone);
        Choose(run.Battle, false, r => r.Text == spell.Name);
        for (var i = 0; i < Battle.SlotCount && run.Battle.TargetsB.Cursor != rheda.Slot; i++)
            run.Battle.StepTarget(1);
        Assert.True(run.Battle.Confirm());

        // The core's own actors: the combatants' and the visuals' (Start Early).
        void Tick() => run.Battle.Tick(run.Zone);
        for (var i = 0; i < 2000 && run.Battle.State != CombatState.Action; i++)
            Tick();

        // The ticks of state 2 until the visual starts and until the cast lands, and the cast's and the effect's lengths.
        var (ticks, started, cast, effect) = (0, -1, 0, 0);
        while (run.Battle.State == CombatState.Action && ticks < 2000)
        {
            Tick();
            ticks++;
            if (rheda.Body.Playing == Battle.CastRequest && rheda.Actor is { } casting)
                cast = casting.StateLength;
            if (started < 0 && run.Battle.Fx.Visuals.FirstOrDefault(v => v.Owner == rheda && v.IsActor) is { } visual)
            {
                started = ticks;
                effect = visual.Actor!.StateLength;
            }
        }
        Assert.InRange(cast, 200, 300);
        Assert.InRange(effect, 120, 200);
        var startAt = castStrike == 0 ? 0 : (int)(cast * castStrike);
        Assert.InRange(started, startAt - 1, startAt + 3);
        var landAt = Math.Max(Battle.SwingTicks, started + (int)(effect * strike));
        Assert.InRange(ticks, landAt - 1, landAt + 3);

        // State 4 waits for her reaction, her own cast playing on, no longer than SpellReleaseTicks.
        var result = 0;
        while (run.Battle.State == CombatState.Result && run.Battle.Current == rheda && result < 2000)
        {
            Tick();
            result++;
        }
        Assert.InRange(result, Battle.ResultTicks, Battle.SpellReleaseTicks + 2);
    }

    [Fact]
    public void TeleportationsANothingWhereItCouldntLand()
    {
        // The port's fix, the user's choice: 1.0 casts at a point Entity.Teleport refuses, spending the turn and moving no one.
        var run = Start();
        var rheda = run.TurnOf("Rheda");
        var teleportation = run.Built.Game.Rules.Magic!.Spell(RecordId.Hex(RecordTypes.Spell, 0x033A))!;
        rheda.Sheet.LearnSpell(teleportation);
        rheda.Sheet.Spells[^1].Level = 10;
        rheda.Sheet.Skills.Train(HBHT.Core.Characters.Skill.Wizard, 10);
        var gemstone = run.Built.Game.Party.Inventory.Catalog.Rules.Gemstone;
        run.Built.Game.Party.Inventory.Add(gemstone);
        var gemstones = run.Built.Game.Party.Inventory.Count(gemstone);
        Choose(run.Battle, false, r => r.Text == "Teleportation");

        // Pushed off the arena's floor, where every cell is taken.
        var taken = false;
        for (var i = 0; i < 30 && !taken; i++)
        {
            for (var t = 0; t < 6; t++)
                run.Battle.Tick(run.Zone, () => new HBHT.Core.Trek.PlayerInput(new System.Numerics.Vector2(0, 1)));
            taken = !run.Battle.TeleportLands(rheda, run.Battle.AreaAim);
        }
        Assert.True(taken);
        Assert.False(run.Battle.Confirm());
        Assert.Equal(CombatState.PlaceArea, run.Battle.State);
        for (var i = 0; i < 60; i++)
            run.Tick();
        Assert.Same(rheda, run.Battle.Current);
        Assert.Equal(CombatState.PlaceArea, run.Battle.State);
        Assert.Equal(gemstones, run.Built.Game.Party.Inventory.Count(gemstone));
        Assert.False(rheda.Flags.HasFlag(CombatantFlags.Acted));
    }

    [Fact]
    public void AHealerHealsHerselfWithAHerbFromCDown()
    {
        var run = Start();
        var brenna = run.TurnOf("Brenna");
        brenna.Sheet.Skills.Train(HBHT.Core.Characters.Skill.Healer, 3);
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
        var reach = source.Spell.Range * HBHT.Core.Magic.Casting.Level(sholeh.Sheet, source);

        // A second of the stick pushed: the centre moves, but never past the reach from her body.
        for (var i = 0; i < 120; i++)
            run.Battle.Tick(run.Zone, () => new HBHT.Core.Trek.PlayerInput(new System.Numerics.Vector2(0, 1)));
        var body = sholeh.Body.Sphere.Position - run.Battle.Origin;
        var off = run.Battle.AreaAim - new System.Numerics.Vector2(body.X, body.Z);
        Assert.True(off.Length() > 0.5f);
        Assert.True(off.Length() <= reach + 0.001f);
        Assert.Equal(new AimMarker(run.Battle.AreaAim, HBHT.Core.Magic.Casting.Level(sholeh.Sheet, source), Spell: true), run.Battle.Aim);

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
