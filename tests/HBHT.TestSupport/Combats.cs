using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.TestSupport;

/// <summary>A combat record for tests that need one but not the base game's.</summary>
public static class Combats
{
    /// <summary>
    /// A combat record that names nothing: <paramref name="ally"/> and <paramref name="fallback"/> as given, every list empty, the menus'
    /// words blank, gold doubled as 1.0's is. Tests set what they need with <c>with</c>.
    /// </summary>
    public static CombatRecord Rules(RecordId ally, RecordId fallback) => new(
        RecordId.Decimal(RecordTypes.Combat, 0), ally, [], fallback, [], [], [],
        LootGoldMultiplier: 2, Rooted: [], Missiles: [], CloseRange: [], Bosses: [], BossStyles: [], DroppedClasses: [], Encounters: [],
        Text: new CombatText("", "", "", "", "", new SenseAuraText("", "", "", "", "", "", "", "", 2, new Dictionary<Element, string>())),
        FlaskDanger: [], UndrawnWeapons: [], OtherHand: [], Untargetable: []);
}
