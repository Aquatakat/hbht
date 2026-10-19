using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Plugins;

namespace Aidyn.Core.Combat;

/// <summary>Where a combatant's selected spell comes from (the entity's <c>+0x3D</c>).</summary>
public enum SpellSourceKind : byte
{
    /// <summary>No spell is selected.</summary>
    None = 0,

    /// <summary>A spell the combatant knows, by its place in the spellbook.</summary>
    Known = 1,

    /// <summary>The body armor's spell.</summary>
    Armor = 2,

    /// <summary>The shield's spell.</summary>
    Shield = 3,

    /// <summary>The weapon's spell.</summary>
    Weapon = 4,

    /// <summary>A gear slot's item's spell, by its slot.</summary>
    Gear = 5,
}

/// <summary>A combatant's selected spell (the entity's <c>+0x3D</c>, the source, and <c>+0x3E</c>, the spellbook's place or gear slot).</summary>
public readonly record struct SpellChoice(SpellSourceKind Source, int Index = 0)
{
    public static readonly SpellChoice None = default;

    public bool IsNone => Source == SpellSourceKind.None;
}

public sealed partial class Combatant
{
    /// <summary>
    /// Its selected spell (the entity's <c>+0x3D</c> and <c>+0x3E</c>): the first it knows as it's made (<c>Combatant.Init</c>,
    /// <c>0x8005EAE0</c>), then what C-down or C-up chose, kept from turn to turn. Target set B is built for it at every turn's
    /// start, which the portraits' red tints show. A refused cast, an item's last charge and an equipment change put it to none.
    /// </summary>
    public SpellChoice Spell { get; internal set; }

    /// <summary>
    /// The cast's margin (the entity's <c>+0x3C</c>): −1 (<c>0xFF</c>) for no cast chosen, which a turn's end puts back; 0 as a target
    /// is confirmed; the roll's step once the first target has prepared it; −2 (<c>0xFE</c>) once refused. A cast that hasn't come to
    /// a margin casts at no more targets.
    /// </summary>
    public int CastMargin { get; internal set; } = -1;

    /// <summary>The action kept while a potion or flask is in hand (<c>+0x24</c>), which <see cref="RevertAction"/> puts back.</summary>
    internal CombatAction HeldAction { get; set; }

    /// <summary>The potion or flask in hand (<c>+0x27</c>, with <see cref="CombatAction.Flask"/>): a flask to throw, a potion to drink.</summary>
    public Potion? Potion { get; internal set; }

    /// <summary>Whether its potion in hand is a flask, thrown at the other side; otherwise it's drunk.</summary>
    public bool HoldsFlask => Action == CombatAction.Flask && Potion is { } p && Items.Potions.IsFlask(p);

    /// <summary>
    /// Its selected spell as a cast's source (<c>Entity.CurrentSpell</c>, <c>0x80070CB4</c>): the spellbook's entry, or the armor's,
    /// shield's, weapon's or gear slot's item with its spell at the item's level. Null with none, or one that's gone.
    /// </summary>
    public SpellSource? CurrentSpell(MagicRules? magic)
    {
        switch (Spell.Source)
        {
            case SpellSourceKind.Known:
                return Spell.Index >= 0 && Spell.Index < Sheet.Spells.Count ? SpellSource.From(Sheet.Spells[Spell.Index]) : null;
            case SpellSourceKind.None:
                return null;
        }
        var equipment = Sheet.Equipment;
        var item = Spell.Source switch
        {
            SpellSourceKind.Armor => equipment.Armor,
            SpellSourceKind.Shield => equipment.Shield,
            SpellSourceKind.Weapon => equipment.Weapon,
            _ => Spell.Index >= 0 && Spell.Index < equipment.Gear.Count ? equipment.Gear[Spell.Index] : null,
        };
        if (item?.Item.Bonuses.Spell is not { } spell || magic?.Spell(spell.Spell) is not { } record)
            return null;
        return new SpellSource(record, Item: item, ItemLevel: spell.Level);
    }

    /// <summary>
    /// A potion or flask in hand (<c>Combatant.SetSpellAction</c>): the action kept to come back to, the potion's action, a
    /// performance stopped and the healing modes put away.
    /// </summary>
    internal void HoldPotion(Potion potion, Battle battle)
    {
        if (Action != CombatAction.Flask)
            HeldAction = Action;
        Action = CombatAction.Flask;
        Potion = potion;
        Drawn = Items.Potions.IsFlask(potion);
        if (Performing)
            StopPerforming(battle);
        Flags &= ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs);
    }

    /// <summary>
    /// Puts its action back (<c>Combatant.RevertAction</c>, <c>0x8006679C</c>): the healing modes go; a flask stays in hand while the
    /// party has another, else the hand is emptied (action 0); a potion's goes back to the action kept.
    /// </summary>
    internal void RevertAction(PartyInventory? inventory)
    {
        Flags &= ~(CombatantFlags.LayOnHands | CombatantFlags.HealWithHerbs);
        if (Action != CombatAction.Flask || Potion is not { } potion)
            return;
        if (Items.Potions.IsFlask(potion))
        {
            if (inventory?.Has(Items.Potions.Id(potion)) == true)
                return;
            Action = CombatAction.None;
        }
        else
        {
            Action = HeldAction;
        }
        Potion = null;
        Drawn = false;
    }

    /// <summary>The spell this combatant would cast now, for the battle's rules (<see cref="CurrentSpell"/>).</summary>
    internal SpellSource? CurrentSpell(Battle battle) => CurrentSpell(battle.Game?.Rules.Magic);

    /// <summary>Its slot in the party (0–3) by its entity, or −1 (<c>CombatMenu.PartySlotOf</c>, <c>0x800888E4</c>).</summary>
    internal int PartySlot(Characters.Party party)
    {
        for (var i = 0; i < party.Slots.Count; i++)
        {
            if (party.Slots[i] is { } member && member.Entity == Entity)
                return i;
        }
        return -1;
    }
}
