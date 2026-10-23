using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Tests;

/// <summary>
/// What a combatant's body holds (<c>Combatant.EquipVisuals</c>, <c>0x80066AEC</c>): the weapon in the hand at locator 0, the shield on
/// the arm at locator 2, a bow in the arm with its arrow in the hand, a flask, and the Troubadour's notes (<c>Combatant.StartTroubadour</c>).
/// </summary>
public sealed class HeldModelTests
{
    private static RecordId Scene(int n) => RecordId.Hex(RecordTypes.Scene, n);

    private static readonly RecordId Arrow = Scene(0x348F);
    private static readonly WeaponRecord Sword = ItemData.NewWeapon(1) with { Model = Scene(0x34DF) };
    private static readonly WeaponRecord Bite = ItemData.NewWeapon(2, WeaponClass.Bite);
    private static readonly WeaponRecord Bow = ItemData.NewWeapon(3, WeaponClass.Missile) with { Range = 10, Model = Scene(0x34D2) };
    private static readonly ArmorRecord Shield = ItemData.NewArmor(4, ArmorSlot.Shield, defense: 2) with { Model = Scene(0x3534) };

    private static CombatRecord Rules(IReadOnlyList<RecordId>? undrawn = null, IReadOnlyList<RecordId>? otherHand = null) =>
        new(RecordId.Decimal(RecordTypes.Combat, 0), Ids.Entity(1), [], Ids.Entity(2), [], [], [],
            Missiles: [new Missile(Bow.Id, Arrow, 8)], UndrawnWeapons: undrawn ?? [], OtherHand: otherHand ?? []);

    private static Combatant Holding(WeaponRecord? weapon, ArmorRecord? shield = null, bool shieldSkill = true)
    {
        var fighter = BattleFixtures.Fighter(0, 20);
        if (!shieldSkill)
        {
            var record = fighter.Sheet.Record with { ShieldSkill = null };
            fighter = new Combatant(0, Characters.Create(record), fighter.Look, fighter.Body, fighter.Flags);
        }
        if (weapon is not null)
            fighter.Sheet.Equipment.Restore(ItemData.Inst(weapon));
        if (shield is not null)
            fighter.Sheet.Equipment.Restore(new ItemInstance(Item.From(shield)));
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
        c.SetDefaultAction(Rules(otherHand: [c.Entity]));
        Assert.Equal(new HeldModel(Arrow, 1), c.Held[Combatant.HandSlot]);
        Assert.Equal(new HeldModel(Bow.Model!.Value, 0), c.Held[Combatant.ArmSlot]);
    }

    [Fact]
    public void AnEntityWhoseModelHasItsWeaponHoldsOnlyItsShieldOrBow()
    {
        var c = Holding(Bow);
        c.SetDefaultAction(Rules(undrawn: [c.Entity]));

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
