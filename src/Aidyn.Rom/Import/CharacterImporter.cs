using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Quests;
using PluginAspect = Aidyn.Plugins.Records.Aspect;
using PluginElement = Aidyn.Plugins.Records.Element;
using PluginEntity = Aidyn.Plugins.Records.EntityRecord;
using PluginReagent = Aidyn.Plugins.Records.Reagent;
using PluginResistance = Aidyn.Plugins.Records.Resistance;
using PluginSchool = Aidyn.Plugins.Records.MagicSchool;
using PluginSpell = Aidyn.Plugins.Records.SpellRecord;
using PluginTargetCount = Aidyn.Plugins.Records.SpellTargetCount;
using PluginTargetType = Aidyn.Plugins.Records.SpellTargetType;
using RomElement = Aidyn.Rom.Db.Element;
using RomEntity = Aidyn.Rom.Db.EntityRecord;
using RomSpell = Aidyn.Rom.Db.SpellRecord;
using WeaponClass = Aidyn.Rom.Db.WeaponClass;

namespace Aidyn.Rom.Import;

/// <summary>
/// Entities, spells, stats, skills and progression (roadmap 2.2): the records of the original's
/// character tables, with the enums and numbers they hold carried as the plugin's own types.
/// </summary>
internal static class CharacterImporter
{
    /// <summary>
    /// Alaron's position in the entity DB, 153 (<c>CharExp::Init</c> compares the DB index with
    /// <c>0x99</c> and reads flag <c>0x24E</c>). He's the one character whose True Name comes from a
    /// flag, which the Roog cinematic sets.
    /// </summary>
    internal const int AlaronDbPosition = 0x99;

    /// <summary>The flag Alaron's True Name is, <c>0x24E</c> (<c>CharExp::Init</c>).</summary>
    internal const int AlaronTrueNameFlag = 0x24E;

    /// <summary>
    /// Oriana's position in the entity DB, 144: <c>shopkeepNotOriana</c> reads <c>entityList[144]</c>
    /// and adds <c>0x200</c> to get her ID. Her training costs no gold.
    /// </summary>
    internal const int OrianaDbPosition = 144;

    /// <summary>
    /// What a trainer charges in gold for a skill or spell the character is at level 0 in, or
    /// doesn't know (<c>WidgetSkillTrain</c>, <c>WidgetSpellTrain</c>, checked in 1.0 and the debug build).
    /// </summary>
    internal const int FirstLevelGold = 200;

    /// <summary>A trainer's price for raising level <c>n</c> is (n + 1) times this (<c>WidgetSkillTrain</c>).</summary>
    internal const int GoldPerLevel = 40;

    /// <summary>A trainer's experience price is the full price times this (<c>WidgetSkillTrain</c>).</summary>
    internal const float TrainerExp = 0.8f;

    /// <summary>A spell can't be raised past this (<c>TempSpell::IsMaxRank</c>).</summary>
    internal const int SpellMaxLevel = 10;

    /// <summary>Set the first time a character learns a spell at a trainer (<c>WidgetSpellTrain::Purchase</c>, flag <c>0xE1B</c>).</summary>
    internal const int FirstSpellFlag = 3611;

    /// <summary>What every award of experience is multiplied by (<c>Entity::AddExp</c>).</summary>
    internal const float ExpAward = 1.5f;

    /// <summary><c>SKILLMAXBASE</c>: the most a skill's base level reads as.</summary>
    internal const int SkillBaseCap = 10;

    /// <summary><c>SKILLMAXMOD</c>: the most a skill reads as with gear and spells.</summary>
    internal const int SkillCap = 15;

    /// <summary>The shield's price multiplier (<c>CharSkills::GetShieldXpPrice</c>).</summary>
    internal const int ShieldTrainingCost = 500;

    /// <summary>What the skill screen calls the shield skill: a literal in <c>WidgetSkillInfo</c>.</summary>
    internal const string ShieldName = "Shield";

    /// <summary>
    /// Where the names are in the Common table: the skills' are lines 271 to 282, the weapon classes'
    /// 283 to 293 and the stats' 303 to 309.
    /// </summary>
    internal const int SkillNameLine = 271, WeaponNameLine = 283, StatNameLine = 303;

    /// <summary>
    /// The lines of the Skills table that describe the weapon classes: the skills' descriptions
    /// are lines 0 to 11, then these five, by weapon class, and the shield's line 17. The monsters'
    /// classes have none.
    /// </summary>
    private static readonly Dictionary<int, int> WeaponDescriptionLines = new()
    {
        [(int)WeaponClass.Hafted] = 12,
        [(int)WeaponClass.Missile] = 13,
        [(int)WeaponClass.Pole] = 14,
        [(int)WeaponClass.Sword] = 15,
        [(int)WeaponClass.Thrown] = 16,
    };

    private const int ShieldDescriptionLine = 17;

    /// <summary>A skill that can't be learnt (<c>0xFF</c>) is null.</summary>
    private const byte CantLearn = 0xFF;

    /// <summary>A resistance's byte is a damage multiplier in quarters (<c>EntityDB::Load</c>).</summary>
    private const float ResistanceStep = 0.25f;

    /// <summary>The experience a record's reward byte is worth before <see cref="ExpAward"/> (<c>EntityDB::Load</c>).</summary>
    private const int ExpPerRewardPoint = 50;

    /// <summary>A spell's training cost per point of its record's byte (<c>TempSpell::Init</c>).</summary>
    private const int SpellCostPerPoint = 50;

    public static List<PluginEntity> BuildEntities(GameDb db, EntityModels models)
    {
        var portraits = models.Entities.ToDictionary(m => m.Entity, m => RecordId.Hex(RecordTypes.Image, m.Portrait));
        var bodies = models.Entities.ToDictionary(m => m.Entity, m => new EntityBody(RecordId.Hex(RecordTypes.Actor, m.Actor), m.Radius, m.Scale, m.Height));
        var alaron = db.Entities[AlaronDbPosition].Id;
        return
        [
            .. db.Entities.Where(e => DbRecordIds.For(e.Id) is not null)
                .Select(e => Entity(e, portraits.GetValueOrDefault(e.Id), e.Id == alaron) with { Body = bodies.GetValueOrDefault(e.Id, Fallback(EntityRadius)) })
                .DistinctBy(e => e.Id).OrderBy(e => e.Id.Key, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The entity list positions whose sheets <c>SaveEntity::GetSlot</c> keeps in slots 4 to 8 (a jump
    /// table in 1.0 at <c>0x800CDD88</c>): Abrecan, Arturo, Becan, Godric and Keelin.
    /// </summary>
    internal static readonly int[] KeptMemberPositions = [0x98, 0x9A, 0x9C, 0xA0, 0xA1];

    /// <summary>Gives each recruitable member the flags the engine keeps for them (<c>Party::CheckFlags</c>).</summary>
    public static List<PluginEntity> WithMembers(List<PluginEntity> entities, EngineTables tables)
    {
        var kept = KeptMemberPositions.Select(p => tables.EntityList[p]).ToHashSet();
        var members = tables.Party.ToDictionary(p => DbRecordIds.For(p.Member)!.Value, p => new MemberFlags(
            Flag(p.Joined), p.Left == 0 ? null : Flag(p.Left), Flag(p.Died), kept.Contains(p.Member)));
        return [.. entities.Select(e => members.TryGetValue(e.Id, out var member) ? e with { Member = member } : e)];

        static RecordId Flag(ushort flag) => RecordId.Decimal(RecordTypes.Flag, flag);
    }

    /// <summary>
    /// The body the code gives a record its tables leave out: a bat (<c>BORG7_Bat</c>) of scale and
    /// height 1, with a radius of 0.75 for an entity and 0.55 for a dialogue entity
    /// (<c>EntityDB::GetBorg7</c>, <c>GetCollideRadius</c>, <c>GetScale</c>, <c>GetHeight</c>).
    /// Every entity has an entry; five dialogue entities don't.
    /// </summary>
    public static EntityBody Fallback(float radius) => new(RecordId.Hex(RecordTypes.Actor, BatActor), radius, 1, 1);

    /// <summary>Every dialogue entity's collision radius (<c>EntityDB::GetCollideRadius</c>).</summary>
    public const float DialogEntityRadius = 0.55f;

    private const float EntityRadius = 0.75f;
    private const int BatActor = 0x2D4A;

    private static PluginEntity Entity(RomEntity e, RecordId? portrait, bool alaron)
    {
        var category = (EntityCategory)e.Category;
        if (!Enum.IsDefined(category))
            throw new InvalidDataException($"Entity {e.Id} is in category {e.Category}.");
        var spells = new List<KnownSpell>();
        for (var i = 0; i < e.Spells.Count; i++)
        {
            // Empty slots keep their level byte, usually 10, but name no spell.
            if (!e.Spells[i].IsNone)
                spells.Add(new KnownSpell(Record(e.Spells[i], DbType.Spell, e.Id), e.SpellLevels[i]));
        }
        if (e.Weapons.Count != 3)
            throw new InvalidDataException($"Entity {e.Id} has {e.Weapons.Count} weapon slots.");
        var s = e.Skills;
        var w = e.WeaponSkills;
        var a = e.Stats;
        return new PluginEntity(
            DbRecordIds.For(e.Id)!.Value, e.Name, portrait, category, (PluginAspect)(int)e.Aspect, (PluginSchool)(int)e.School,
            e.HasTrueName != 0, e.IsHeavy != 0, e.Level,
            new Attributes(a[0], a[1], a[2], a[3], a[4], a[5]),
            new SkillLevels(Skill(s[0]), Skill(s[1]), Skill(s[2]), Skill(s[3]), Skill(s[4]), Skill(s[5]), Skill(s[6]), Skill(s[7]), Skill(s[8]),
                Skill(s[9]), Skill(s[10]), Skill(s[11])),
            new WeaponSkillLevels(Skill(w[0]), Skill(w[1]), Skill(w[2]), Skill(w[3]), Skill(w[4]), Skill(w[5]), Skill(w[6]), Skill(w[7]),
                Skill(w[8]), Skill(w[9]), Skill(w[10])),
            Skill(e.ShieldSkill), spells,
            [.. e.Weapons.Select(i => i.IsNone ? (RecordId?)null : Record(i, DbType.Weapon, e.Id))],
            e.Armor.IsNone ? null : Record(e.Armor, DbType.Armor, e.Id),
            e.Shield.IsNone ? null : Record(e.Shield, DbType.Shield, e.Id),
            e.BaseProtection, e.Unk4E, e.Morale,
            [.. e.Resistances.Where(r => r.Element != RomElement.None).Select(r => new PluginResistance((PluginElement)(int)r.Element, r.Amount * ResistanceStep))],
            e.ExpReward * ExpPerRewardPoint, e.LootCategory, e.Unk55, e.Unk56,
            alaron ? RecordId.Decimal(RecordTypes.Flag, AlaronTrueNameFlag) : null);
    }

    private static int? Skill(byte level) => level == CantLearn ? null : level;

    private static RecordId Record(ItemId id, DbType type, ItemId owner) =>
        id.Type == type && DbRecordIds.For(id) is { } record ? record
            : throw new InvalidDataException($"Entity {owner} names {id}, which isn't a {type}.");

    /// <param name="items">Where the spell symbols' icons are. Without them or <paramref name="menu"/>, spells have no icon.</param>
    /// <param name="menu">Where each spell's symbol and description come from. Without it, spells have neither.</param>
    public static List<PluginSpell> BuildSpells(GameDb db, ItemTables? items = null, MenuTables? menu = null) =>
    [
        .. db.Spells.Select((s, position) => (s, position)).Where(p => DbRecordIds.For(p.s.Id) is not null)
            .Select(p => Spell(db, p.s, p.position, items, menu))
            .DistinctBy(s => s.Id).OrderBy(s => s.Id.Key, StringComparer.Ordinal),
    ];

    private static PluginSpell Spell(GameDb db, RomSpell s, int position, ItemTables? items, MenuTables? menu)
    {
        var special = (SpellSpecial)s.Special;
        if (!Enum.IsDefined(special))
            throw new InvalidDataException($"Spell {s.Id} has special {s.Special}.");
        return new PluginSpell(
            DbRecordIds.For(s.Id)!.Value, s.Name, (PluginSchool)(int)s.School, s.Damage, s.StaminaCost, (PluginTargetCount)(int)s.TargetCount,
            (PluginTargetType)(int)s.TargetType, s.WizardRequired, special, s.Range, (PluginReagent)(int)s.Reagent,
            s.ExpModifier * SpellCostPerPoint, s.Unk1E,
            items is not null && menu is not null && menu.SpellSymbols.TryGetValue(s.Id, out var symbol)
                ? RecordId.Hex(RecordTypes.Image, items.SpellSymbolIcons[symbol]) : null,
            menu is null ? null : ItemImporter.Description(db, RomStringTable.Spells, menu.SpellDescriptions, s.Id),
            position);
    }

    /// <summary>The seven stats, <c>aidyn:stat/0</c> to <c>6</c>.</summary>
    public static List<StatRecord> BuildStats(GameDb db, CharacterTables tables)
    {
        var common = db.Strings[RomStringTable.Common];
        var descriptions = db.Strings[RomStringTable.Stats];
        var level = CharacterTables.StatCount - 1;
        return
        [
            .. Enumerable.Range(0, CharacterTables.StatCount).Select(n => new StatRecord(
                RecordId.Decimal(RecordTypes.Stat, n), DialogueText.Unescape(common[StatNameLine + n]),
                // The level has no description, and the training screen skips it.
                n == level ? null : DialogueText.Unescape(descriptions[n]),
                tables.BaseStatCaps[n], tables.ModdedStatCaps[n], n == level ? null : tables.StatTrainingCosts[n])),
        ];
    }

    /// <summary>The twelve skills, <c>aidyn:skill/0</c> to <c>11</c>.</summary>
    public static List<SkillRecord> BuildSkills(GameDb db, CharacterTables tables)
    {
        var common = db.Strings[RomStringTable.Common];
        var descriptions = db.Strings[RomStringTable.Skills];
        return
        [
            .. Enumerable.Range(0, CharacterTables.SkillCount).Select(n => new SkillRecord(
                RecordId.Decimal(RecordTypes.Skill, n), DialogueText.Unescape(common[SkillNameLine + n]),
                DialogueText.Unescape(descriptions[n]), tables.SkillTrainingCosts[n], SkillBaseCap, SkillCap)),
        ];
    }

    /// <summary>The eleven weapon classes, <c>aidyn:weaponskill/0</c> to <c>10</c>, and the shield, <c>11</c>.</summary>
    public static List<SkillRecord> BuildWeaponSkills(GameDb db, CharacterTables tables)
    {
        var common = db.Strings[RomStringTable.Common];
        var descriptions = db.Strings[RomStringTable.Skills];
        return
        [
            .. Enumerable.Range(0, CharacterTables.WeaponClassCount).Select(n => new SkillRecord(
                RecordId.Decimal(RecordTypes.WeaponSkill, n), DialogueText.Unescape(common[WeaponNameLine + n]),
                WeaponDescriptionLines.TryGetValue(n, out var line) ? DialogueText.Unescape(descriptions[line]) : null,
                tables.WeaponTrainingCosts[n], SkillBaseCap, SkillCap)),
            new SkillRecord(RecordId.Decimal(RecordTypes.WeaponSkill, CharacterTables.WeaponClassCount), ShieldName,
                DialogueText.Unescape(descriptions[ShieldDescriptionLine]), ShieldTrainingCost, SkillBaseCap, SkillCap),
        ];
    }

    /// <summary>The progression, <c>aidyn:progression/0</c>. Oriana is the free trainer.</summary>
    public static ProgressionRecord BuildProgression(GameDb db, CharacterTables tables) => new(
        RecordId.Decimal(RecordTypes.Progression, 0), tables.LevelExperience, ExpAward,
        new TrainingPrices(FirstLevelGold, GoldPerLevel, TrainerExp, [DbRecordIds.For(db.Entities[OrianaDbPosition].Id)!.Value], SpellMaxLevel,
            RecordId.Decimal(RecordTypes.Flag, FirstSpellFlag)));
}
