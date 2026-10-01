using System.Globalization;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>Part of a line of details: text in a colour, or an image.</summary>
public abstract record DetailPiece;

public sealed record DetailText(string Text, Rgba Color) : DetailPiece;

/// <summary>An image, at its own size or squared to <paramref name="Size"/>.</summary>
public sealed record DetailImage(RecordId Image, int? Size = null) : DetailPiece;

/// <summary>
/// A line of C-down's details: pieces from the left, indented or not, and pieces against the right
/// edge. <paramref name="Wrap"/> text wraps at the details' width.
/// </summary>
public sealed record DetailLine(IReadOnlyList<DetailPiece> Left, IReadOnlyList<DetailPiece> Right, bool Indent = false, bool Wrap = false);

/// <summary>
/// What C-down's details show (<c>WidgetItemDetail</c>): a heading with an icon, lines, and lines the
/// original pins near the bottom of its parchment, which <see cref="FooterTop"/> or
/// <see cref="FooterBottom"/> place in its 320 × 240 frame.
/// </summary>
/// <param name="Icon">The heading's icon, beside <paramref name="Title"/>, both absent for a stat or skill.</param>
public sealed record MenuDetails(
    RecordId? Icon,
    string? Title,
    IReadOnlyList<DetailLine> Lines,
    IReadOnlyList<DetailLine> Footer,
    int? FooterTop = null,
    int? FooterBottom = null);

/// <summary>Builds C-down's details for the Party tab's rows, as <c>WidgetItemDetail</c>'s constructors do.</summary>
public static class MenuDetailsBuilder
{
    /// <summary>The colour of every line, as the lists' rows are (<c>COLOR_RED1</c>).</summary>
    public static readonly Rgba TextColor = new(130, 80, 80, 255);

    /// <summary>A gain and a loss (<c>(+n)</c> and <c>(n)</c>) in the stats and skills lists and the armor's totals.</summary>
    public static readonly Rgba Gain = new(0, 128, 0, 255), Loss = new(128, 0, 0, 255);

    /// <summary>
    /// Where the original pins a spell's or scroll's Wizard rank and reagent, and where armor's totals
    /// end, in its 320 × 240 frame.
    /// </summary>
    public const int WizardTop = 187, TotalsBottom = 213;

    /// <summary>
    /// An item's details, or null for one the original shows none for: key items, and other items
    /// without a description, among them the maps (<c>WidgetInvShop::CDownFunc</c>).
    /// </summary>
    /// <param name="wearer">The member shown, whose totals armor's details compare with.</param>
    public static MenuDetails? Item(ItemInstance instance, Character? wearer, MenuText text, Func<RecordId, SpellRecord?> spells, ItemCatalog? items)
    {
        var item = instance.Item;
        if (item.Weapon is { } weapon)
            return Weapon(instance, weapon, text, spells, wearer);
        if (item.Armor is { } armor)
            return Armor(instance, armor, text, spells, wearer);
        if (item.Kind is ItemKind.Key)
            return null;
        if (item.Kind is ItemKind.Potion or ItemKind.Scroll or ItemKind.Misc)
        {
            if (item.Gear?.Description is not { } description)
                return null;
            var footer = new List<DetailLine>();
            if (item.Kind == ItemKind.Scroll && item.Bonuses.Spell is { } scroll && spells(scroll.Spell) is { } spell)
                footer.Add(Line(Format(text[MenuText.MinWizard], spell.Wizard)));
            return new MenuDetails(item.Icon, item.Name, [Wrapped(description)], footer, FooterTop: footer.Count > 0 ? WizardTop : null);
        }
        return Gear(instance, text, spells);
    }

    /// <summary>A stat's description (<c>WidgetItemDetail(u16)</c>).</summary>
    public static MenuDetails Stat(StatRecord stat) => new(null, null, [Wrapped(stat.Description ?? "")], []);

    /// <summary>A skill's, weapon skill's or the shield's description.</summary>
    public static MenuDetails Skill(SkillRecord skill) => new(null, null, [Wrapped(skill.Description ?? "")], []);

    /// <summary>
    /// A spell's details: its symbol and name, what it does, and pinned below, the Wizard rank it needs
    /// and the reagent it uses (<c>WidgetItemDetail(SpellInstance*)</c>).
    /// </summary>
    public static MenuDetails Spell(SpellRecord spell, MenuText text, ItemCatalog? items)
    {
        var footer = new List<DetailLine>();
        if (spell.Wizard != 0)
            footer.Add(Line(Format(text[MenuText.MinWizard], spell.Wizard)));
        if (Reagent(spell.Reagent, items) is { } reagent)
        {
            footer.Add(new DetailLine([new DetailText(text[MenuText.Requires], TextColor), new DetailImage(reagent.Icon, Size: ReagentIcon),
                new DetailText(reagent.Name, TextColor)], []));
        }
        return new MenuDetails(spell.Icon, spell.Name, spell.Description is { } d ? [Wrapped(d)] : [], footer,
            FooterTop: footer.Count > 0 ? WizardTop : null);
    }

    /// <summary>The reagent's icon is squared to a line's height less 2.</summary>
    public const int ReagentIcon = 9;

    private static MenuDetails Weapon(ItemInstance instance, WeaponRecord weapon, MenuText text, Func<RecordId, SpellRecord?> spells,
        Character? wearer)
    {
        var lines = new List<DetailLine> { Pair(text[MenuText.Skill], SkillName(wearer, SkillId.FromRecord(WeaponSkill(weapon.Class)))) };
        AspectLine(lines, weapon.Bonuses.Aspect, text);
        if (weapon.RequiredStrength != 0)
            lines.Add(Pair(text[MenuText.MinStrength], Num(weapon.RequiredStrength)));
        lines.Add(Pair(text[MenuText.BaseHit], Num(weapon.Hit)));
        lines.Add(Pair(text[MenuText.Damage], Num(weapon.Damage)));
        Bonuses(lines, instance, text, spells, wearer);
        return new MenuDetails(instance.Item.Icon, instance.Item.Name, lines, []);
    }

    private static MenuDetails Armor(ItemInstance instance, ArmorRecord armor, MenuText text, Func<RecordId, SpellRecord?> spells,
        Character? wearer)
    {
        var lines = new List<DetailLine>();
        AspectLine(lines, armor.Bonuses.Aspect, text);
        lines.Add(Pair(text[MenuText.Protection], Num(armor.Protection)));
        if (armor.Slot == ArmorSlot.Shield)
            lines.Add(Pair(text[MenuText.Defense], Num(armor.Defense)));
        lines.Add(Pair(text[MenuText.Dexterity], Plus(armor.Dexterity)));
        // The decomp labels Stealth with the armor's name.
        lines.Add(Pair(text[MenuText.Stealth], Plus(armor.Stealth)));
        Bonuses(lines, instance, text, spells, wearer);

        var footer = new List<DetailLine>();
        if (wearer is not null)
        {
            var equipment = wearer.Equipment;
            var protection = equipment.ArmorProtection;
            var withIt = protection;
            if (armor.Slot == ArmorSlot.Body || equipment.CanCarryShield)
            {
                var replaced = armor.Slot == ArmorSlot.Body ? equipment.Armor : equipment.Shield;
                withIt = protection - (replaced?.Item.Armor?.Protection ?? 0) + armor.Protection;
            }
            var defense = equipment.ShieldDefense;
            var defenseWithIt = armor.Slot == ArmorSlot.Shield && equipment.CanCarryShield ? armor.Defense : defense;
            footer.Add(Total(text[MenuText.TotalProtection], protection, withIt));
            footer.Add(Total(text[MenuText.TotalDefense], defense, defenseWithIt));
        }
        return new MenuDetails(instance.Item.Icon, instance.Item.Name, lines, footer, FooterBottom: footer.Count > 0 ? TotalsBottom : null);
    }

    /// <summary>
    /// Worn gear (<c>WidgetItemDetail(GearInstance*)</c>). The original prints the required Strength
    /// under "Min. Intelligence" and the Intelligence under "Min. Strength"; these are the right way round.
    /// </summary>
    private static MenuDetails Gear(ItemInstance instance, MenuText text, Func<RecordId, SpellRecord?> spells)
    {
        var item = instance.Item;
        var gear = item.Gear;
        var lines = new List<DetailLine>();
        AspectLine(lines, item.Bonuses.Aspect, text);
        lines.Add(Pair(text[MenuText.MinIntelligence], Num(item.RequiredIntelligence)));
        if (item.RequiredStrength != 0)
            lines.Add(Pair(text[MenuText.MinStrength], Num(item.RequiredStrength)));
        lines.Add(Pair(text[MenuText.Damage], PlusNonzero(gear?.Damage ?? 0)));
        lines.Add(Pair(text[MenuText.Protection], PlusNonzero(gear?.Protection ?? 0)));
        Bonuses(lines, instance, text, spells, null, charges: item.Kind == ItemKind.Wand);
        return new MenuDetails(item.Icon, item.Name, lines, []);
    }

    /// <summary>What wearing it adds: a stat, a skill, an enchantment, a spell and its charges, and a resistance.</summary>
    private static void Bonuses(List<DetailLine> lines, ItemInstance instance, MenuText text, Func<RecordId, SpellRecord?> spells,
        Character? wearer, bool charges = false)
    {
        var bonuses = instance.Item.Bonuses;
        if (bonuses.Stat is { } stat)
        {
            // A Stamina bonus is a battery: what it holds now.
            var stamina = stat.Stat == Items.Item.StaminaStat;
            lines.Add(Pair(stamina ? text[MenuText.SpellBattery] : NameOf(stat.Stat, wearer), Plus(stamina ? instance.Battery ?? stat.Amount : stat.Amount)));
        }
        if (bonuses.Skill is { } skill)
            lines.Add(Pair(SkillName(wearer, SkillId.FromRecord(skill.Skill)) ?? skill.Skill.Key, Plus(skill.Amount)));
        if (bonuses.Enchantment is { } magic)
        {
            lines.Add(Line(text[MenuText.Magic]));
            lines.Add(Line(spells(magic.Spell)?.Name ?? magic.Spell.ToString(), indent: true));
        }
        if (bonuses.Spell is { } spell)
        {
            lines.Add(Line(text[MenuText.Spell]));
            lines.Add(Line(spells(spell.Spell)?.Name ?? spell.Spell.ToString(), indent: true));
            if (charges && (instance.Charges ?? spell.Charges) is var left and not 0)
                lines.Add(Pair(text[MenuText.Charges], Num(left)));
        }
        if (bonuses.Resistance is { Element: not Element.None } resistance)
        {
            lines.Add(Line(text[MenuText.Resists]));
            lines.Add(new DetailLine([new DetailText(resistance.Element.ToString(), TextColor)],
                [new DetailText(((1 - resistance.Damage) * 100).ToString("0", CultureInfo.InvariantCulture), TextColor)], Indent: true));
        }
    }

    private static void AspectLine(List<DetailLine> lines, Aspect aspect, MenuText text)
    {
        if (MenuImages.AspectIcon(aspect) is { } icon)
            lines.Add(new DetailLine([new DetailText(text[MenuText.Aspect], TextColor)], [new DetailImage(icon)]));
    }

    private static DetailLine Total(string label, int now, int withIt) =>
        new([new DetailText(label, TextColor)],
            [new DetailText(Num(now), TextColor), new DetailText($"({Num(withIt)})", withIt > now ? Gain : withIt < now ? Loss : TextColor)]);

    private static (RecordId Icon, string Name)? Reagent(Reagent reagent, ItemCatalog? items)
    {
        if (items is null)
            return null;
        RecordId? id = reagent switch
        {
            Plugins.Records.Reagent.Spice => items.Rules.Spice,
            Plugins.Records.Reagent.Herb => items.Rules.Herb,
            Plugins.Records.Reagent.Gemstone => items.Rules.Gemstone,
            _ => null,
        };
        return id is { } i && items.TryGet(i) is { } item ? (item.Icon, item.Name) : null;
    }

    private static RecordId WeaponSkill(WeaponClass weapon) => RecordId.Decimal(RecordTypes.WeaponSkill, (int)weapon);

    private static string? SkillName(Character? character, SkillId skill) => character?.Rules[skill].Name ?? skill.ToString();

    private static string NameOf(RecordId stat, Character? character) =>
        character is not null && int.TryParse(stat.Key, out var n) && n < CharacterRules.StatCount ? character.Rules[(Stat)n].Name
            : int.TryParse(stat.Key, out var m) ? ((Stat)m).ToString() : stat.Key;

    private static DetailLine Line(string text, bool indent = false) => new([new DetailText(text, TextColor)], [], indent);

    private static DetailLine Wrapped(string text) => new([new DetailText(text, TextColor)], [], Wrap: true);

    private static DetailLine Pair(string left, string? right) =>
        new([new DetailText(left, TextColor)], right is null ? [] : [new DetailText(right, TextColor)]);

    /// <summary>A bonus: "(+n)" above 0, else "(n)" (<c>PrintPlus</c>).</summary>
    private static string Plus(int value) => value > 0 ? $"(+{Num(value)})" : $"({Num(value)})";

    /// <summary>"(+n)" unless 0 (<c>PrintPlus2</c>), so a negative reads "(+-n)" as the original's does.</summary>
    private static string PlusNonzero(int value) => value != 0 ? $"(+{Num(value)})" : $"({Num(value)})";

    private static string Format(string format, int value) => format.Replace("%d", Num(value), StringComparison.Ordinal);

    private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
}
