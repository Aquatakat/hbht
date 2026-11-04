using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// Edits of an entity's character sheet in the shapes the tests used to write as <c>with { Level = 3 }</c> on the entity,
/// before the sheet became an optional part of it (ADR 0032).
/// </summary>
internal static class EntityEdits
{
    public static EntityRecord Sheeted(this EntityRecord entity, Func<EntitySheet, EntitySheet> edit) => entity with { Sheet = edit(entity.CharacterSheet) };

    public static EntityRecord WithLevel(this EntityRecord entity, int level) => entity.Sheeted(s => s with { Level = level });

    public static EntityRecord WithAttributes(this EntityRecord entity, Attributes a) => entity.Sheeted(s => s with
    {
        Attributes = new Dictionary<RecordId, int>
        {
            [RecordIds.Stat(0)] = a.Intelligence, [RecordIds.Stat(1)] = a.Willpower, [RecordIds.Stat(2)] = a.Dexterity, [RecordIds.Stat(3)] = a.Endurance,
            [RecordIds.Stat(4)] = a.Strength, [RecordIds.Stat(5)] = a.Stamina,
        },
    });

    public static EntityRecord WithSkills(this EntityRecord entity, SkillLevels skills) => entity.Sheeted(s =>
    {
        var map = SkillSlots.Maps(SkillSlots.ToArray(skills, new WeaponSkillLevels(), null)).Skills;
        return s with { Skills = map };
    });

    public static EntityRecord WithWeaponSkills(this EntityRecord entity, WeaponSkillLevels weapons) => entity.Sheeted(s =>
    {
        var shield = s.WeaponSkills.TryGetValue(RecordIds.WeaponSkill(11), out var level) ? level : (int?)null;
        return s with { WeaponSkills = SkillSlots.Maps(SkillSlots.ToArray(new SkillLevels(), weapons, shield)).WeaponSkills };
    });

    public static EntityRecord WithShieldSkill(this EntityRecord entity, int? shield) => entity.Sheeted(s =>
    {
        var map = new Dictionary<RecordId, int>(s.WeaponSkills);
        if (shield is { } level)
            map[RecordIds.WeaponSkill(11)] = level;
        else
            map.Remove(RecordIds.WeaponSkill(11));
        return s with { WeaponSkills = map };
    });

    public static EntityRecord WithSchool(this EntityRecord entity, RecordId school) => entity.Sheeted(s => s with { School = school });

    public static EntityRecord WithSpells(this EntityRecord entity, IReadOnlyList<KnownSpell> spells) => entity.Sheeted(s => s with { Spells = spells });

    public static EntityRecord WithWeapons(this EntityRecord entity, RecordId? joins = null, RecordId? spare = null, RecordId? fights = null) =>
        entity.Sheeted(s => s with { Weapons = new EntityWeapons(joins, spare, fights) });

    public static EntityRecord WithArmor(this EntityRecord entity, RecordId? armor, RecordId? shield) => entity.Sheeted(s => s with { Armor = armor, Shield = shield });
}
