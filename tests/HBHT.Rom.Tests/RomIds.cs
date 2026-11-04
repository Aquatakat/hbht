using HBHT.Plugins;
using HBHT.Rom.Db;

namespace HBHT.Rom.Tests;

/// <summary>The record IDs of the ROM's numbered kinds, by the importer's enums (the tests' static <c>Ids.Element</c> would hide the enum's name).</summary>
internal static class RomIds
{
    public static RecordId Element(Db.Element element) => Numbering.Element(element)!.Value;

    public static RecordId School(MagicSchool school) => Numbering.School(school);

    public static RecordId WeaponSkill(WeaponClass weapon) => Numbering.WeaponSkill(weapon);
}
