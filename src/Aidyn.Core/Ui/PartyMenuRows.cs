using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>An item in the inventory list (<c>SMIItem</c>).</summary>
/// <param name="Quantity">The count shown before the name: 1 for what's worn, the stack's for what's carried.</param>
/// <param name="Wearer">The party slot of the member wearing it, whose portrait shows first, or null if it's carried.</param>
public sealed record InventoryRow(ItemInstance Instance, int Quantity, int? Wearer)
{
    public Item Item => Instance.Item;
}

/// <summary>An attribute in the stats list (<c>SMStatItem</c>).</summary>
/// <param name="Value">The base value, capped as it reads.</param>
/// <param name="Change">What gear and spells add, shown in green or red when it isn't 0.</param>
public sealed record StatRow(Stat Stat, string Name, int Value, int Change);

/// <summary>A skill, weapon skill or the shield in the skills list (<c>WidgetSkillInfo</c>).</summary>
/// <param name="Icon">A skill's icon; weapon skills and the shield have none.</param>
public sealed record SkillRow(SkillId Skill, string Name, RecordId? Icon, int Value, int Change);

/// <summary>A spell in the spells list (<c>WidgetSpellEntry</c>): its aspect's, school's and own icons, its rank and name.</summary>
public sealed record SpellRow(LearnedSpell Spell, RecordId? Aspect, RecordId? School, RecordId? Symbol)
{
    public int Rank => Spell.Level;

    public string Name => Spell.Spell.Name;
}

/// <summary>What's under the stats list: experience to spend, the total and what the next level needs.</summary>
/// <param name="NextLevel">The experience the next level needs, or the common strings' "MAX" at the top level.</param>
public sealed record ExperienceLines(string RemainingLabel, string Remaining, string TotalLabel, string Total, string NextLevelLabel, string NextLevel);

/// <summary>Builds the Party tab's rows from a party, as the original's lists do.</summary>
public static class PartyMenuRows
{
    /// <summary>
    /// The inventory list (<c>WidgetInvShop::InitMenu</c>): each member's weapon, armor, shield and
    /// gear, then what the party carries, sorted by <see cref="InventoryOrder"/> with the members in
    /// party order starting from <paramref name="shown"/>.
    /// </summary>
    public static IReadOnlyList<InventoryRow> Inventory(Party party, int shown)
    {
        var worn = new List<InventoryRow>();
        foreach (var (member, slot) in party.Slots.Select((m, i) => (m, i)))
        {
            if (member is not null)
                worn.AddRange(member.Equipment.All.Select(item => new InventoryRow(item, 1, slot)));
        }
        var carried = party.Inventory?.Stacks.Select(s => new InventoryRow(s.Instance, s.Quantity, null)) ?? [];
        return
        [
            .. worn.OrderBy(r => (r.Wearer!.Value - shown + Party.MaxSize) % Party.MaxSize).ThenBy(r => r.Item, ItemComparer.Instance),
            .. carried.OrderBy(r => r.Item, ItemComparer.Instance),
        ];
    }

    /// <summary>The six attributes, without the level (<c>WidgetStatTrain::InitMenu</c>).</summary>
    public static IReadOnlyList<StatRow> Stats(Character character) =>
    [
        .. Enum.GetValues<Stat>().Where(s => s != Stat.Level).Select(s => new StatRow(s, character.Rules[s].Name,
            character.Stats.Base(s), character.Stats.RawModified(s) - character.Stats.RawBase(s))),
    ];

    /// <summary>
    /// The skills the character has above 0, then weapon skills, then the shield
    /// (<c>WidgetSkillTrain::InitMenu</c>).
    /// </summary>
    public static IReadOnlyList<SkillRow> Skills(Character character) =>
    [
        .. SkillId.All.Where(s => character.Skills.Base(s) > 0).Select(s => new SkillRow(s, character.Rules[s].Name,
            s.IsSkill ? character.Rules[s].Icon : null, character.Skills.Base(s), character.Skills.Modified(s) - character.Skills.Base(s))),
    ];

    /// <summary>The spells the character knows, in the order learnt (<c>WidgetSpellTrain::InitMenu</c>).</summary>
    public static IReadOnlyList<SpellRow> Spells(Character character) =>
    [
        .. character.Spells.Select(s => new SpellRow(s, MenuImages.AspectIcon(SpellAspect(s.Spell)), MenuImages.SchoolIcon(s.Spell.School),
            s.Spell.Icon)),
    ];

    /// <summary>A spell's aspect, which only its special gives (<c>GetSpellIcons</c>).</summary>
    public static Aspect SpellAspect(SpellRecord spell) => spell.Special switch
    {
        SpellSpecial.SolarAspect => Aspect.Solar,
        SpellSpecial.LunarAspect => Aspect.Lunar,
        _ => Aspect.None,
    };

    /// <summary>The lines under the stats list, from the common strings (<c>WidgetStatTrain</c>).</summary>
    public static ExperienceLines Experience(Character character, MenuText text) => new(
        text[MenuText.XpRemaining], Num(character.SpendableExp), text[MenuText.TotalXp], Num(character.TotalExp), text[MenuText.NextLevel],
        character.NextLevelExp is { } next ? Num(next - character.TotalExp) : text[MenuText.Max]);

    private static string Num(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private sealed class ItemComparer : IComparer<Item>
    {
        public static readonly ItemComparer Instance = new();

        public int Compare(Item? a, Item? b) => InventoryOrder.Compare(a!, b!);
    }
}

/// <summary>The common strings the menus read (<c>aidyn:strings/5</c>), by their lines.</summary>
public sealed class MenuText(IReadOnlyList<string> common)
{
    public const int XpRemaining = 0x1FA, TotalXp = 0x1FB, NextLevel = 0x1FC, Max = 0x1FD, MinWizard = 0x215, Requires = 0x216,
        Skill = 0x217, Aspect = 0x218, MinStrength = 0x219, BaseHit = 0x21A, Damage = 0x21B, SpellBattery = 0x21C, Magic = 0x21D,
        Spell = 0x21E, Resists = 0x21F, Protection = 0x220, Defense = 0x221, Dexterity = 0x222, Stealth = 0x223, TotalDefense = 0x224,
        TotalProtection = 0x225, MinIntelligence = 0x226, Charges = 0x227;

    public string this[int line] => line < common.Count ? common[line] : "";

    /// <summary>The installed plugins' common strings.</summary>
    public static MenuText From(PluginSet plugins) =>
        new(plugins.All<StringsRecord>(RecordTypes.Strings).FirstOrDefault(s => s.Name == "Common")?.Lines ?? []);
}
