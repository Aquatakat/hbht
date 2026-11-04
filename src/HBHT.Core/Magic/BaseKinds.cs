using HBHT.Plugins;

namespace HBHT.Core.Magic;

/// <summary>
/// The base schools the original's rules name (ADR 0033): Chaos's spellbook keeps the Star spells' place, Alaron's keeps the three
/// schools he learns, and which school's spells a resistance meets (docs/hardcoded-content.md).
/// </summary>
public static class Schools
{
    public static readonly RecordId Chaos = Id(0), Elemental = Id(1), Naming = Id(2), Necromancy = Id(3), Star = Id(5);

    /// <summary>The unschooled, <c>aidyn:school/4</c>.</summary>
    public static readonly RecordId Unschooled = Id(4);

    private static RecordId Id(int n) => RecordId.Decimal(RecordTypes.School, n);
}

/// <summary>
/// The base elements the original's rules name (ADR 0033): which a resistance to each meets on a spell (<c>Combatant.SpellDamage</c>).
/// </summary>
public static class Elements
{
    public static readonly RecordId Earth = Id(1), Solar = Id(2), Physical = Id(3), Necromancy = Id(4), Fire = Id(5), Lunar = Id(6),
        Naming = Id(7), Water = Id(8), Magic = Id(9), Air = Id(10), Star = Id(11), Elemental = Id(12), Chaos = Id(13), Cutting = Id(14),
        Smashing = Id(15), Holy = Id(16);

    private static RecordId Id(int n) => RecordId.Decimal(RecordTypes.Element, n);
}
