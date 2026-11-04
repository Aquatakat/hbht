using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// What a combatant's body holds (<c>Combatant.EquipVisuals</c>, <c>0x80066AEC</c>): the weapon in the hand at locator 0, the shield on
/// the arm at locator 2, a bow in the arm with its arrow in the hand, a flask, and the Troubadour's notes (<c>Combatant.StartTroubadour</c>).
/// </summary>
public sealed class HeldModelTests
{
    private static RecordId Scene(int n) => RecordId.Hex(RecordTypes.Scene, n);

    private static readonly RecordId Arrow = Scene(0x348F);
    private static readonly ItemRecord Sword = ItemData.NewWeapon(1) with { Model = Scene(0x34DF) };
    private static readonly ItemRecord Bite = ItemData.NewWeapon(2, WeaponClass.Bite);
    private static readonly ItemRecord Bow = NewBow();

    private static ItemRecord NewBow()
    {
        var bow = ItemData.NewWeapon(3, WeaponClass.Missile);
        return bow with { Model = Scene(0x34D2), Weapon = bow.Weapon! with { Range = 10, Missile = new Missile(Arrow, 8) } };
    }
    private static readonly ItemRecord Shield = ItemData.NewArmor(4, ItemKind.Shield, defense: 2) with { Model = Scene(0x3534) };

    private static CombatRecord Rules() => Combats.Rules(Ids.Entity(1));

    private static Combatant Holding(ItemRecord? weapon, ItemRecord? shield = null, bool shieldSkill = true, BowHand bowHand = BowHand.Usual,
        bool weaponsInModel = false)
    {
        var fighter = BattleFixtures.Fighter(0, 20);
        var look = fighter.Look with { BowHand = bowHand, WeaponsInModel = weaponsInModel };
        var record = shieldSkill ? fighter.Sheet.Record : fighter.Sheet.Record.WithShieldSkill(null);
        fighter = new Combatant(0, Characters.Create(record), look, fighter.Body, fighter.Flags);
        if (weapon is not null)
            fighter.Sheet.Equipment.Restore(ItemData.Inst(weapon));
        if (shield is not null)
            fighter.Sheet.Equipment.Restore(ItemData.Inst(shield));
        return fighter;
    }

    [Fact]
    public void AWeaponIsInTheHandAndAShieldOnTheArm()
    {
        var c = Holding(Sword, Shield);
        c.SetDefaultAction(Rules());

        Assert.Equal(new HeldModel(Sword.Model!.Value, 0), c.Held[Combatant.HandSlot]);
        Assert.Equal(new HeldModel(Shield.Model!.Value, 2), c.Held[Combatant.ArmSlot]);
        Assert.Null(c.Held[Combatant.NotesSlot]);
    }

    [Fact]
    public void ABiteHasNoModelAndAnEntityThatCantLearnTheShieldSkillShowsNone()
    {
        var c = Holding(Bite, Shield, shieldSkill: false);
        c.SetDefaultAction(Rules());

        Assert.Null(c.Held[Combatant.HandSlot]);
        Assert.Null(c.Held[Combatant.ArmSlot]);
    }

    [Fact]
    public void ABowIsOnTheArmInPlaceOfTheShieldWithItsArrowInTheHand()
    {
        var c = Holding(Bow, Shield);
        c.SetDefaultAction(Rules());

        Assert.Equal(new HeldModel(Arrow, 0), c.Held[Combatant.HandSlot]);
        Assert.Equal(new HeldModel(Bow.Model!.Value, 1), c.Held[Combatant.ArmSlot]);

        // EntityBody.Hand's entities hold it the other way round.
        c = Holding(Bow, Shield, bowHand: BowHand.Other);
        c.SetDefaultAction(Rules());
        Assert.Equal(new HeldModel(Arrow, 1), c.Held[Combatant.HandSlot]);
        Assert.Equal(new HeldModel(Bow.Model!.Value, 0), c.Held[Combatant.ArmSlot]);
    }

    [Fact]
    public void AnEntityWhoseModelHasItsWeaponHoldsOnlyItsShieldOrBow()
    {
        var c = Holding(Bow, weaponsInModel: true);
        c.SetDefaultAction(Rules());

        Assert.Null(c.Held[Combatant.HandSlot]);
        Assert.Equal(new HeldModel(Bow.Model!.Value, 1), c.Held[Combatant.ArmSlot]);
    }

    [Fact]
    public void WithNoActionTheHandIsEmptyAndAFlaskOrPotionInHandIsTheFlask()
    {
        var c = Holding(Sword, Shield);
        c.Action = CombatAction.None;
        c.EquipVisuals(Rules());
        Assert.Null(c.Held[Combatant.HandSlot]);
        Assert.Equal(new HeldModel(Shield.Model!.Value, 2), c.Held[Combatant.ArmSlot]);

        c.Action = CombatAction.Flask;
        c.EquipVisuals(Rules());
        Assert.Equal(new HeldModel(Battle.FlaskScene, 0), c.Held[Combatant.HandSlot]);
    }

    [Fact]
    public void TheNotesAreOnTheHeadsLocatorRaisedAndGoWhenThePerformanceStops()
    {
        var c = Holding(Sword);
        c.SetDefaultAction(Rules());
        c.HoldNotes(true);

        Assert.Equal(new HeldModel(Combatant.NotesScene, 3, 1), c.Held[Combatant.NotesSlot]);
        // EquipVisuals leaves the notes be.
        c.EquipVisuals(Rules());
        Assert.NotNull(c.Held[Combatant.NotesSlot]);

        c.StopPerforming(null);
        Assert.Null(c.Held[Combatant.NotesSlot]);
    }
}
