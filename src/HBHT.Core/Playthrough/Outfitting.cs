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
    private readonly Dictionary<(RecordId Member, RecordId Item), int> _putOn = [];

    /// <summary>
    /// How often one member may put the same item on before it's left off for good: a guard, so that a slip in the weighing that makes
    /// two items each look better than the other can't swap them for ever and eat the steps' ticks.
    /// </summary>
    public int MostPutOn { get; init; } = 3;
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

    /// <summary>Whether the next shop opened is shopped in: what's better than the party has is bought, then the shop left.</summary>
    public bool Shopping
    {
        get => _shopping;
        set => (_shopping, _presses, _buying) = (value, 0, null);
    }

    private bool _shopping;

    /// <summary>The gold kept back from shopping.</summary>
    public uint GoldReserve { get; set; } = 1000;

    /// <summary>Everything bought at shops, in order.</summary>
    public List<string> Bought { get; } = [];

    private RecordId? _buying;
    private readonly HashSet<RecordId> _unbought = [];

    public MenuAction Press(PlaythroughHost host, PauseMenu menu)
    {
        if (Shopping && menu.Tab == PauseTab.Party && menu.Window is BarterMenu { Shop: { } shop } barter)
            return Shop(host, barter, shop);
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
            {
                _worn.Add(new Outfit(host.Clock.Ticks, member.Entity, item.Item.Name, replaced?.Item.Name, gain));
                var key = (member.Entity, item.Id);
                if ((_putOn[key] = _putOn.GetValueOrDefault(key) + 1) >= MostPutOn && replaced is not null)
                    _refused.Add((member.Entity, replaced.Id));
            }
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
    /// In a shop: the stock's row that gains the party most over the best it has of its kind (<see cref="Gain"/>), within the gold less
    /// <see cref="GoldReserve"/>, is bought by the stick to the stock, its row and A, one at a time; then B leaves. Outfitting puts it on
    /// after.
    /// </summary>
    private MenuAction Shop(PlaythroughHost host, BarterMenu barter, Shop shop)
    {
        if (++_presses > 2000)
        {
            Shopping = false;
            return MenuAction.Back;
        }
        if (barter.HasPopup)
        {
            // Refused (the gold, a full inventory): not asked again.
            if (_buying is { } refused)
                _unbought.Add(refused);
            _buying = null;
            return MenuAction.Accept;
        }
        var party = barter.Party.Party;
        var gold = party.Gold > GoldReserve ? party.Gold - GoldReserve : 0;
        var rows = barter.Pile.Rows;
        var (row, best) = (-1, Least);
        for (var i = 0; i < rows.Count; i++)
        {
            var item = rows[i].Item;
            if (_unbought.Contains(item.Id) || shop.BuyPrice(rows[i].Instance) > gold)
                continue;
            if (Gain(party, item) is var gain && gain > best)
                (row, best) = (i, gain);
        }
        _buying = null;
        if (row < 0)
        {
            Shopping = false;
            return MenuAction.Back;
        }
        if (!barter.OnPile)
            return MenuAction.Left;
        if (barter.Pile.Cursor != row)
            return barter.Pile.Cursor < row ? MenuAction.Down : MenuAction.Up;
        _buying = rows[row].Item.Id;
        Bought.Add($"{host.Clock.Ticks} {rows[row].Item.Name} for {shop.BuyPrice(rows[row].Instance)} gold (+{best:0.#})");
        return MenuAction.Accept;
    }

    /// <summary>
    /// What an item would gain the party: the most it's worth to a living member who can use it, over the best of its kind they wear or
    /// the inventory holds for them.
    /// </summary>
    public float Gain(Party party, Item item)
    {
        var best = 0f;
        foreach (var member in party.Members.Where(m => !m.IsDead))
        {
            var worn = Replaces(member, item)?.Item;
            if (!CanUse(member, item) || Worth(member, item, worn) is not { } worth)
                continue;
            var have = worn is not null ? Worth(member, worn, worn) ?? 0 : 0;
            foreach (var stack in party.Inventory.Stacks)
            {
                if (stack.Item.Kind == item.Kind && CanUse(member, stack.Item) && Worth(member, stack.Item, worn) is { } carried)
                    have = Math.Max(have, carried);
            }
            best = Math.Max(best, worth - have);
        }
        return best;
    }

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
                var old = Replaces(member, item)?.Item;
                if (Worth(member, item, old) is not { } worth)
                    continue;
                var gain = worth - (old is not null ? Worth(member, old, old) ?? 0 : 0);
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

    /// <summary>
    /// What an item is worth to a member wearing it in place of <paramref name="replaced"/> (what they wear of its kind, if anything), in
    /// the rules' terms; null for what isn't weighed (a weapon that flies, a ring). The member is weighed as they'd be wearing it: their
    /// stats and skills less what the replaced item adds, and with what this one adds. Weighed on the member as they stand, a sword whose
    /// Warrior bonus raised every weapon's chance made the sword it was swapped for look better, and that one the first again, for ever
    /// (Abrecan's Warfang and Chaos Sword).
    /// </summary>
    public static float? Worth(Character member, Item item, Item? replaced = null)
    {
        var stats = new Dictionary<Stat, int>();
        var skills = new Dictionary<SkillId, int>();
        void Wear(Item? worn, int sign)
        {
            if (worn is null)
                return;
            foreach (var (stat, by) in worn.Bonuses.Stats ?? new Dictionary<RecordId, int>())
                if (StatId.TryFromRecord(stat) is { } s)
                    stats[s] = stats.GetValueOrDefault(s) + sign * by;
            foreach (var (skill, by) in worn.Bonuses.Skills ?? new Dictionary<RecordId, int>())
                if (SkillId.TryFromRecord(skill) is { } k)
                    skills[k] = skills.GetValueOrDefault(k) + sign * by;
        }
        Wear(replaced, -1);
        Wear(item, 1);
        float Stat(Stat s) => member.Stats.Modified(s) + stats.GetValueOrDefault(s);
        float SkillOf(SkillId k) => member.Skills.Modified(k) + skills.GetValueOrDefault(k);

        // What the item adds to stats the formula below doesn't read, flat; Dexterity twice, as defence.
        var weapon = item.Kind == ItemKind.Weapon ? item.Weapon : null;
        var bonuses = 0f;
        foreach (var (stat, by) in item.Bonuses.Stats ?? new Dictionary<RecordId, int>())
        {
            if (StatId.TryFromRecord(stat) is not { } s || s == Characters.Stat.Stamina
                || weapon is not null && s is Characters.Stat.Strength or Characters.Stat.Endurance or Characters.Stat.Level)
                continue;
            bonuses += s == Characters.Stat.Dexterity ? 2 * by : by;
        }
        switch (item.Kind)
        {
            case ItemKind.Weapon when weapon is { Missile: null }:
            {
                if (SkillId.TryFromRecord(weapon.Skill) is not { } skill)
                    return null;
                var level = Stat(Characters.Stat.Level);
                var chance = Stat(Characters.Stat.Strength) + Stat(Characters.Stat.Endurance) + level + weapon.Hit
                    + 6 * SkillOf(skill) + 2 * SkillOf(Skill.Warrior) - 50;
                var strength = (int)Stat(Characters.Stat.Strength);
                var tier = Array.FindIndex(StrengthTiers, t => strength <= t) is var i && i >= 0 ? i : StrengthTiers.Length;
                var damage = 3.5f * (weapon.Damage + tier) + level;
                return Math.Clamp(chance, 5, 95) / 100f * damage + bonuses;
            }
            case ItemKind.Body:
                return 4 * item.Protection + bonuses;
            case ItemKind.Shield:
                return item.Record.Defense * SkillOf(SkillId.Shield) + 4 * item.Protection + bonuses;
            case var _ when item.IsGear && item.Kind is not (ItemKind.Ring or ItemKind.Scroll or ItemKind.Wand):
                return 4 * item.Protection + bonuses;
            default:
                return null;
        }
    }
}
