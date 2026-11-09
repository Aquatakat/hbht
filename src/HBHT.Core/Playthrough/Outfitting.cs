using HBHT.Core.Characters;
using HBHT.Core.Items;
using HBHT.Core.MapObjects;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>Something a member put on from the inventory, and what it was worth to them over what it replaced.</summary>
public sealed record Outfit(long Tick, RecordId Member, string Item, string? Replaced, float Gain)
{
    public override string ToString() => $"{Tick} {Member}: {Item}{(Replaced is { } r ? $" for {r}" : "")} (+{Gain:0.#})";
}

/// <summary>
/// The menu policy that dresses the party (docs/systems/playthrough.md, "Gear"): the loot is taken into the inventory, and a player puts
/// on what's better than what each member wears. Once the inventory holds something better for someone, the pilot opens the Party tab
/// (<see cref="TryDress"/>), and this presses as a player does: C-left to the member, the stick to the inventory list, the row, A. The
/// game's own rules equip it, and refuse what the member can't use. Everything else goes to <see cref="Inner"/>.
/// </summary>
/// <remarks>
/// What's better is weighed by the rules' own terms (<see cref="Worth"/>): a melee weapon by its chance to hit times its damage with the
/// member's Strength, Endurance, Level, Warrior and weapon skill (<c>Combatant.MeleeChance</c>, <c>MeleeDamage</c>); armor and gear by
/// their protection, which comes off every blow, and the attributes they add, Dexterity twice since it comes off the foe's chance; a
/// shield by its defense times the shield skill, and its protection. Weapons that fly, rings, scrolls and wands are left as they are.
/// </remarks>
public sealed class Outfitting(IMenuPolicy? inner = null) : IMenuPolicy
{
    private readonly List<Outfit> _worn = [];
    private readonly HashSet<(RecordId Member, RecordId Item)> _refused = [];
    private bool _wanted;
    private long _looked = -1;
    private (int Slot, ItemInstance Item, float Gain)? _target;
    private (Character Member, ItemInstance Item, ItemInstance? Replaced, float Gain)? _asked;
    private int _presses;

    /// <summary>What the menu does otherwise.</summary>
    public IMenuPolicy Inner { get; } = inner ?? new TakeLootLeaveTheRest();

    /// <summary>The least gain worth opening the menu for.</summary>
    public float Least { get; init; } = 0.5f;

    /// <summary>The ticks between looks at the inventory.</summary>
    public int Every { get; init; } = 300;

    /// <summary>Everything put on, in order.</summary>
    public IReadOnlyList<Outfit> Worn => _worn;

    /// <summary>
    /// Opens the Party tab to dress the party, if the player has the controls in trek and the inventory holds something better for a
    /// member than what they wear: true if it did.
    /// </summary>
    public bool TryDress(PlaythroughHost host)
    {
        var ticks = host.Clock.Ticks;
        if (_looked >= 0 && ticks - _looked < Every)
            return false;
        _looked = ticks;
        if (!host.PlayerHasControl || host.Session is not { Mode: GameMode.Trek } session || Best(host.Game.Party) is null)
            return false;
        _wanted = true;
        _target = null;
        _asked = null;
        _presses = 0;
        session.OpenMenu(PauseTab.Party);
        return true;
    }

    public MenuAction Press(PlaythroughHost host, PauseMenu menu)
    {
        if (!_wanted || menu.Tab != PauseTab.Party || menu.Window is not PartyMenu party)
            return Inner.Press(host, menu);
        if (++_presses > 2000)
        {
            _wanted = false;
            return MenuAction.Back;
        }
        if (party.Overlay.HasPopup || party.HasPopup)
        {
            // A refusal: the game's rules say this member can't use it.
            if (_asked is var (who, what, _, _))
                _refused.Add((who.Entity, what.Id));
            _asked = null;
            _target = null;
            return MenuAction.Accept;
        }
        if (party.Changing)
            return MenuAction.None;
        if (_asked is var (member, item, replaced, gain))
        {
            _asked = null;
            _target = null;
            if (member.Equipment.All.Contains(item) || member.Equipment.All.Any(i => i.Id == item.Id))
                _worn.Add(new Outfit(host.Clock.Ticks, member.Entity, item.Item.Name, replaced?.Item.Name, gain));
            else
                _refused.Add((member.Entity, item.Id));
        }
        _target ??= Best(party.Party);
        if (_target is not var (slot, wanted, worth))
        {
            _wanted = false;
            return MenuAction.Back;
        }
        if (party.Member != slot)
            return MenuAction.MemberLeft;
        if (party.List != PartyList.Inventory)
            return MenuAction.Right;
        var rows = party.Inventory.Rows;
        var row = -1;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Wearer is null && rows[i].Item.Id == wanted.Id)
            {
                row = i;
                break;
            }
        }
        if (row < 0)
        {
            _refused.Add((party.Party.Slots[slot]!.Entity, wanted.Id));
            _target = null;
            return MenuAction.None;
        }
        if (party.Inventory.Cursor != row)
            return party.Inventory.Cursor < row ? MenuAction.Down : MenuAction.Up;
        var wearer = party.Party.Slots[slot]!;
        _asked = (wearer, wanted, Replaces(wearer, wanted.Item), worth);
        return MenuAction.Accept;
    }

    public int? Popup(PlaythroughHost host, TrekPopup popup) => Inner.Popup(host, popup);

    /// <summary>
    /// The best thing to put on: the member and the carried item that gains most over what it replaces, by <see cref="Worth"/>, of what
    /// the rules let them use and the game hasn't refused. Null if nothing gains <see cref="Least"/>.
    /// </summary>
    public (int Slot, ItemInstance Item, float Gain)? Best(Party party)
    {
        (int, ItemInstance, float)? best = null;
        var bestGain = Least;
        for (var slot = 0; slot < party.Slots.Count; slot++)
        {
            if (party.Slots[slot] is not { IsDead: false } member)
                continue;
            foreach (var stack in party.Inventory.Stacks)
            {
                var item = stack.Item;
                if (_refused.Contains((member.Entity, item.Id)) || !CanUse(member, item))
                    continue;
                if (Worth(member, item) is not { } worth)
                    continue;
                var gain = worth - (Replaces(member, item) is { } old ? Worth(member, old.Item) ?? 0 : 0);
                if (gain > bestGain)
                    (best, bestGain) = ((slot, stack.Instance, gain), gain);
            }
        }
        return best;
    }

    /// <summary>What putting an item on would take off: the weapon, armor or shield worn, or the gear of its kind.</summary>
    private static ItemInstance? Replaces(Character member, Item item) => item.Kind switch
    {
        ItemKind.Weapon => member.Equipment.Weapon,
        ItemKind.Body => member.Equipment.Armor,
        ItemKind.Shield => member.Equipment.Shield,
        _ => member.Equipment.Gear.FirstOrDefault(g => g?.Item.Kind == item.Kind),
    };

    /// <summary>Whether the rules let a member put an item on, as <c>Party.Equip</c> checks (its aspect, Strength, skill, a free slot).</summary>
    private static bool CanUse(Character member, Item item)
    {
        if (item.Bonuses.Aspect != Aspect.None && item.Bonuses.Aspect != member.Aspect)
            return false;
        var equipment = member.Equipment;
        return item.Kind switch
        {
            ItemKind.Weapon => equipment.CanWield(item) == EquipRefusal.None && equipment.Weapon?.Item.Record.Fixed != true,
            ItemKind.Body => true,
            ItemKind.Shield => equipment.CanCarryShield,
            _ when item.IsGear && item.Kind is not (ItemKind.Ring or ItemKind.Scroll or ItemKind.Wand) =>
                equipment.Gear.Any(g => g?.Item.Kind == item.Kind) || equipment.CanWear(item) == EquipRefusal.None,
            _ => false,
        };
    }

    // Combatant.StrengthTier (0x80062008): the first of these the modified Strength is at or under, else 8.
    private static readonly int[] StrengthTiers = [4, 9, 17, 24, 28, 32, 36, 39];

    /// <summary>What an item is worth to a member, in the rules' terms; null for what isn't weighed (a weapon that flies, a ring).</summary>
    public static float? Worth(Character member, Item item)
    {
        float Stat(Stat s) => member.Stats.Modified(s);
        var bonuses = 0f;
        if (item.Bonuses.Stats is { } stats)
        {
            foreach (var (stat, by) in stats)
            {
                if (StatId.TryFromRecord(stat) is not { } s || s == Characters.Stat.Stamina)
                    continue;
                bonuses += s == Characters.Stat.Dexterity ? 2 * by : by;
            }
        }
        switch (item.Kind)
        {
            case ItemKind.Weapon when item.Weapon is { Missile: null } weapon:
            {
                if (SkillId.TryFromRecord(weapon.Skill) is not { } skill)
                    return null;
                var level = Stat(Characters.Stat.Level);
                var chance = Stat(Characters.Stat.Strength) + Stat(Characters.Stat.Endurance) + level + weapon.Hit
                    + 6 * member.Skills.Modified(skill) + 2 * member.Skills.Modified(Skill.Warrior) - 50;
                var strength = (int)Stat(Characters.Stat.Strength);
                var tier = Array.FindIndex(StrengthTiers, t => strength <= t) is var i && i >= 0 ? i : StrengthTiers.Length;
                var damage = 3.5f * (weapon.Damage + tier) + level;
                return Math.Clamp(chance, 5, 95) / 100f * damage + bonuses;
            }
            case ItemKind.Body:
                return 4 * item.Protection + bonuses;
            case ItemKind.Shield:
                return item.Record.Defense * member.Skills.Modified(SkillId.Shield) + 4 * item.Protection + bonuses;
            case var _ when item.IsGear && item.Kind is not (ItemKind.Ring or ItemKind.Scroll or ItemKind.Wand):
                return 4 * item.Protection + bonuses;
            default:
                return null;
        }
    }
}
