using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;
using HBHT.Rom.Quests;
using HBHT.Rom.Text;

namespace HBHT.Rom.Import;

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

    /// <summary>
    /// The entities (ADR 0032): the entity DB's, each with its sheet, and the dialogue entity table's but the four that are an entity
    /// too (<see cref="DbRecordIds.Twins"/>), with none, the twins' references being the entity's. A twin's names must match.
    /// </summary>
    /// <param name="facts">Combat's facts about entities. Without them, none has combat traits or holds its weapons apart.</param>
    public static List<EntityRecord> BuildEntities(GameDb db, EntityModels models, EntityFacts? facts = null)
    {
        var portraits = models.Entities.ToDictionary(m => m.Entity, m => RecordId.Hex(RecordTypes.Image, m.Portrait));
        var bodies = models.Entities.ToDictionary(m => m.Entity, m => new EntityBody(RecordId.Hex(RecordTypes.Actor, m.Actor), m.Radius, m.Scale, m.Height, m.Strike, Alpha: m.Alpha));
        var alaron = db.Entities[AlaronDbPosition].Id;
        var oriana = DbRecordIds.Required(db.Entities[OrianaDbPosition].Id);
        var entities = db.Entities.Where(e => DbRecordIds.For(e.Id) is not null)
            .Select(e => Entity(e, portraits.GetValueOrDefault(e.Id), e.Id == alaron) with { Body = bodies.GetValueOrDefault(e.Id, Fallback(EntityRadius)) })
            .DistinctBy(e => e.Id)
            .Select(e => e with { TrainsFree = e.Id == oriana })
            .Select(e => facts is null ? e : e with
            {
                Combat = facts.Combat.GetValueOrDefault(e.Id),
                Body = e.Body! with
                {
                    WeaponsInModel = facts.WeaponsInModel.Contains(e.Id),
                    BowHand = facts.OtherHand.Contains(e.Id) ? BowHand.Other : BowHand.Usual,
                },
            })
            .ToList();
        foreach (var (dialogue, entity) in DbRecordIds.Twins)
        {
            var speaker = db.DialogEntities.FirstOrDefault(d => d.Id.Value == dialogue)
                ?? throw new InvalidDataException($"There's no dialogue entity 0x{dialogue:X4} to merge.");
            if (entities.FirstOrDefault(e => e.Id == RecordId.Hex(RecordTypes.Entity, entity)) is not { } twin || twin.Name != speaker.Name)
                throw new InvalidDataException($"Dialogue entity 0x{dialogue:X4}, {speaker.Name}, isn't entity 0x{entity:X4}'s twin.");
        }
        return [.. entities.Concat(BuildSpeakers(db, models)).OrderBy(e => e.Id.Key, StringComparer.Ordinal)];
    }

    /// <summary>
    /// The dialogue entity table's entities, with their portraits and bodies and no sheet, but the twins (<see cref="DbRecordIds.Twins"/>).
    /// The five without a portrait get the code's fallback at run time, and a bat for a body. Each notices the party at 10
    /// (<see cref="DialoguePerception"/>).
    /// </summary>
    internal static List<EntityRecord> BuildSpeakers(GameDb db, EntityModels models)
    {
        var portraits = models.DialogEntities.ToDictionary(m => m.Entity, m => RecordId.Hex(RecordTypes.Image, m.Portrait));
        var bodies = models.DialogEntities.ToDictionary(m => m.Entity,
            m => new EntityBody(RecordId.Hex(RecordTypes.Actor, m.Actor), DialogEntityRadius, m.Scale, m.Height));
        var fallback = Fallback(DialogEntityRadius);
        return
        [
            .. db.DialogEntities.Where(e => !DbRecordIds.Twins.ContainsKey(e.Id.Value) && DbRecordIds.For(e.Id) is not null)
                .Select(e => new EntityRecord(DbRecordIds.For(e.Id)!.Value, e.Name, portraits.TryGetValue(e.Id, out var p) ? p : null,
                    DialoguePerception, bodies.GetValueOrDefault(e.Id, fallback)))
                .DistinctBy(e => e.Id),
        ];
    }

    /// <summary>
    /// How far one from the dialogue entity table notices the party: 10, the port's (<c>Wanderers.DialoguePerception</c>), as the
    /// original's own entity check (<c>EntityDB::GetPerception</c>) reads past the entity DB for one.
    /// </summary>
    internal const int DialoguePerception = 10;

    /// <summary>
    /// The entity list positions whose sheets <c>SaveEntity::GetSlot</c> keeps in slots 4 to 8 (a jump
    /// table in 1.0 at <c>0x800CDD88</c>): Abrecan, Arturo, Becan, Godric and Keelin.
    /// </summary>
    internal static readonly int[] KeptMemberPositions = [0x98, 0x9A, 0x9C, 0xA0, 0xA1];

    /// <summary>Writes the entities (with their members' flags and sounds), the spells, the stats, skills and progression.</summary>
    public static void Import(ImportContext ctx)
    {
        var db = ctx.Db;
        ctx.Report("Characters");
        ctx.WriteRecords("entities.json", BodySoundImporter.WithSounds(db, ctx.BodySounds,
            WithMembers(ItemImporter.WithSoleWeapons(db, BuildEntities(db, ctx.Models, CombatImporter.Entities(ctx.Engine))), ctx.Engine)));
        ctx.WriteRecords("spells.json", BodySoundImporter.WithSounds(db, ctx.BodySounds,
            MagicImporter.WithVisuals(MagicImporter.WithEffects(BuildSpells(db, ctx.Items, ctx.Menus), ctx.Engine), ctx.SpellVisuals, ctx.Archive)));
        ctx.WriteRecords("stats.json", BuildStats(db, ctx.Characters));
        ctx.WriteRecords("skills.json", ItemImporter.WithIcons(ctx.Items, BuildSkills(db, ctx.Characters)));
        ctx.WriteRecords("weaponskills.json", BuildWeaponSkills(db, ctx.Characters));
        ctx.WriteRecords("progression.json", new[] { BuildProgression(ctx.Characters) });
    }

    /// <summary>Gives each recruitable member the flags the engine keeps for them (<c>Party::CheckFlags</c>).</summary>
    public static List<EntityRecord> WithMembers(List<EntityRecord> entities, EngineTables tables)
    {
        var kept = KeptMemberPositions.Select(p => tables.EntityList[p]).ToHashSet();
        var members = tables.Party.ToDictionary(p => DbRecordIds.For(p.Member)!.Value, p => new MemberFlags(
            DbRecordIds.Flag(p.Joined), p.Left == 0 ? null : DbRecordIds.Flag(p.Left), DbRecordIds.Flag(p.Died), kept.Contains(p.Member)));
        return [.. entities.Select(e => members.TryGetValue(e.Id, out var member) ? e with { Member = member } : e)];
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

    private static EntityRecord Entity(RomEntityRecord e, RecordId? portrait, bool alaron)
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
        var (skills, weaponSkills) = SkillSlots.Maps([.. e.Skills.Concat(e.WeaponSkills).Append(e.ShieldSkill).Select(Skill)]);
        RecordId? Weapon(int slot) => e.Weapons[slot].IsNone ? null : Record(e.Weapons[slot], DbType.Weapon, e.Id);
        var sheet = new EntitySheet(category, e.Aspect, Numbering.School(e.School), e.HasTrueName != 0, e.IsHeavy != 0, e.Level,
            Enumerable.Range(0, 6).ToDictionary(n => RecordId.Decimal(RecordTypes.Stat, n), n => (int)e.Stats[n]),
            skills, weaponSkills, spells,
            new EntityWeapons(Weapon(0), Weapon(1), Weapon(2)),
            e.Armor.IsNone ? null : Record(e.Armor, DbType.Armor, e.Id),
            e.Shield.IsNone ? null : Record(e.Shield, DbType.Shield, e.Id),
            e.BaseProtection, e.Unk4E, e.Morale,
            [.. e.Resistances.Where(r => r.Element != Element.None).Select(r => new Resistance(Numbering.Element(r.Element)!.Value, r.Amount * ResistanceStep))],
            e.ExpReward * ExpPerRewardPoint, e.LootCategory,
            alaron ? DbRecordIds.Flag(AlaronTrueNameFlag) : null);
        return new EntityRecord(DbRecordIds.For(e.Id)!.Value, e.Name, portrait, e.Unk55, Sheet: sheet);
    }

    private static int? Skill(byte level) => level == CantLearn ? null : level;

    private static RecordId Record(ItemId id, DbType type, ItemId owner) =>
        id.Type == type && DbRecordIds.For(id) is { } record ? record
            : throw new InvalidDataException($"Entity {owner} names {id}, which isn't a {type}.");

    /// <summary><c>unk1E</c> bit 0x10: the caster places the area.</summary>
    private const int PlacedBit = 0x10;

    /// <param name="items">Where the spell symbols' icons are. Without them or <paramref name="menu"/>, spells have no icon.</param>
    /// <param name="menu">Where each spell's symbol and description come from. Without it, spells have neither.</param>
    public static List<SpellRecord> BuildSpells(GameDb db, ItemTables? items = null, MenuTables? menu = null) =>
    [
        .. db.Spells.Select((s, position) => (s, position)).Where(p => DbRecordIds.For(p.s.Id) is not null)
            .Select(p => Spell(db, p.s, p.position, items, menu))
            .DistinctBy(s => s.Id).OrderBy(s => s.Id.Key, StringComparer.Ordinal),
    ];

    private static SpellRecord Spell(GameDb db, RomSpellRecord s, int position, ItemTables? items, MenuTables? menu)
    {
        var special = (SpellSpecial)s.Special;
        if (!Enum.IsDefined(special))
            throw new InvalidDataException($"Spell {s.Id} has special {s.Special}.");
        // unk1E's bits: 1 Lunar and 2 Solar, which Combatant.ResistRoll and SpellResistanceApplies read; 0x10 a placed area,
        // which CanCast, BeginSpellTargeting and IsResistable read. 0x20, 0x40 and 0x80 nothing in 1.0 reads (docs/formats/db.md).
        var aspect = (s.Unk1E & 3) switch
        {
            0 => Aspect.None,
            1 => Aspect.Lunar,
            2 => Aspect.Solar,
            _ => throw new InvalidDataException($"{s.Id} is both Lunar and Solar."),
        };
        var (spice, herb, gemstone) = ItemImporter.Reagents(db);
        RecordId? reagent = s.Reagent switch
        {
            Reagent.None => null,
            Reagent.Spice => spice,
            Reagent.Herb => herb,
            Reagent.Gemstone => gemstone,
            _ => throw new InvalidDataException($"Spell {s.Id} has reagent {s.Reagent}."),
        };
        return new SpellRecord(
            DbRecordIds.For(s.Id)!.Value, s.Name, Numbering.School(s.School), s.StaminaCost,
            new SpellTargeting(s.TargetType, s.TargetCount, Placed: (s.Unk1E & PlacedBit) != 0),
            s.WizardRequired, special, s.Range, reagent,
            s.ExpModifier * SpellCostPerPoint,
            items is not null && menu is not null && menu.SpellSymbols.TryGetValue(s.Id, out var symbol)
                ? RecordId.Hex(RecordTypes.Image, items.SpellSymbolIcons[symbol]) : null,
            menu is null ? null : ItemImporter.Description(db, RomStringTable.Spells, menu.SpellDescriptions, s.Id),
            position,
            Aspect: aspect);
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

    /// <summary>
    /// The eleven weapon classes, <c>aidyn:weaponskill/0</c> to <c>10</c>, each saying whether its weapons drop as loot
    /// (<see cref="CombatImporter.DroppedClasses"/>), and the shield, <c>11</c>.
    /// </summary>
    public static List<SkillRecord> BuildWeaponSkills(GameDb db, CharacterTables tables)
    {
        var common = db.Strings[RomStringTable.Common];
        var descriptions = db.Strings[RomStringTable.Skills];
        return
        [
            .. Enumerable.Range(0, CharacterTables.WeaponClassCount).Select(n => new SkillRecord(
                RecordId.Decimal(RecordTypes.WeaponSkill, n), DialogueText.Unescape(common[WeaponNameLine + n]),
                WeaponDescriptionLines.TryGetValue(n, out var line) ? DialogueText.Unescape(descriptions[line]) : null,
                tables.WeaponTrainingCosts[n], SkillBaseCap, SkillCap, Drops: CombatImporter.DroppedClasses.Contains((WeaponClass)n))),
            new SkillRecord(RecordId.Decimal(RecordTypes.WeaponSkill, CharacterTables.WeaponClassCount), ShieldName,
                DialogueText.Unescape(descriptions[ShieldDescriptionLine]), ShieldTrainingCost, SkillBaseCap, SkillCap),
        ];
    }

    /// <summary>The progression, <c>aidyn:progression/0</c>.</summary>
    public static ProgressionRecord BuildProgression(CharacterTables tables) => new(
        RecordId.Decimal(RecordTypes.Progression, 0), tables.LevelExperience, ExpAward,
        new TrainingPrices(FirstLevelGold, GoldPerLevel, TrainerExp, SpellMaxLevel));
}
