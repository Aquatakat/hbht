using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Import;

namespace Aidyn.Rom.Tests;

public class CharacterTablesTests
{
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    [Theory]
    [MemberData(nameof(Versions))]
    public void LevelsStartAtTheExperienceTheTableHolds(RomVersionId id)
    {
        var tables = CharacterTables.Read(TestRoms.Get(id));

        Assert.Equal(
            [0, 500, 2500, 7000, 15000, 27500, 45500, 70000, 102000, 142500, 192500, 253000, 325000, 409500, 507500, 620000, 748000, 892500,
                1054500, 1235000, 1435000, 1655500, 1897500, 2162000, 2450000, 2762500, 3100500, 3465000, 3857000, 4277500, 4727500, 5208000,
                5720000, 6264500, 6842500, 7455000, 8103000, 8787500, 9509500, 10270000, 11070000],
            tables.LevelExperience);
        Assert.Equal(CharacterTables.MaxLevel + 1, tables.LevelExperience.Count);
    }

    [Theory]
    [MemberData(nameof(Versions))]
    public void StatCapsAreByStatWithTheLevelLast(RomVersionId id)
    {
        var tables = CharacterTables.Read(TestRoms.Get(id));

        Assert.Equal([30, 30, 30, 40, 30, 90, 40], tables.BaseStatCaps);
        Assert.Equal([40, 40, 40, 50, 40, 120, 40], tables.ModdedStatCaps);
    }

    /// <summary>The decomp's source has Merchant 7500 and Ranger 750. The ROMs, and the debug build's machine code, have 750 and 1000.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void TrainingCostsAreTheRomsNotTheDecompsSource(RomVersionId id)
    {
        var tables = CharacterTables.Read(TestRoms.Get(id));

        Assert.Equal([12, 12, 24, 12, 12, 4, 500], tables.StatTrainingCosts);
        Assert.Equal([1500, 500, 1000, 750, 500, 750, 1000, 500, 1000, 1000, 1000, 1500], tables.SkillTrainingCosts);
        Assert.Equal(750, tables.SkillTrainingCosts[5]);
        Assert.Equal(1000, tables.SkillTrainingCosts[6]);
        Assert.Equal([0, 0, 0, 400, 600, 400, 0, 0, 600, 400, 0], tables.WeaponTrainingCosts);
    }

    /// <summary>Common's line numbers for the names are the same in 1.0, 1.1 and the debug build, though 1.1's table is longer.</summary>
    [Theory]
    [MemberData(nameof(Versions))]
    public void StatsSkillsAndWeaponSkillsAreNamedByTheSameCommonLinesInEveryVersion(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var db = GameDb.Read(rom);
        var tables = CharacterTables.Read(rom);

        Assert.Equal(["Intelligence", "Willpower", "Dexterity", "Endurance", "Strength", "Stamina", "Level"],
            CharacterImporter.BuildStats(db, tables).Select(s => s.Name));
        Assert.Equal(["Alchemist", "Diplomat", "Healer", "Loremaster", "Mechanic", "Merchant", "Ranger", "Stealth", "Thief", "Troubadour", "Warrior", "Wizard"],
            CharacterImporter.BuildSkills(db, tables).Select(s => s.Name));
        Assert.Equal(["Bite", "Breath", "Claw", "Hafted", "Missile", "Pole", "Spit", "Sting", "Sword", "Thrown", "Tusk", "Shield"],
            CharacterImporter.BuildWeaponSkills(db, tables).Select(s => s.Name));
    }
}

[Collection(ImportedPluginCollection.Name)]
public sealed class CharacterImportTests(MapObjectFixture fixture)
{
    private static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);

    private PluginSet Set => fixture.Data.Set;

    private T Get<T>(string id) where T : class => Set.Get<T>(RecordId.Parse(id));

    [Fact]
    public void AlaronStartsAtLevelOneWithHisTrueNameFromTheRoogCinematic()
    {
        var alaron = Set.Get<Plugins.Records.EntityRecord>(Alaron);

        Assert.Equal("Alaron", alaron.Name);
        Assert.Equal(1, alaron.Level);
        Assert.Equal(new Attributes(18, 12, 15, 15, 15, 30), alaron.Attributes);
        Assert.False(alaron.TrueName);
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 590), alaron.TrueNameFlag);
        Assert.True(alaron.Heavy);
        Assert.Equal((Plugins.Records.Aspect.Solar, Plugins.Records.MagicSchool.Chaos, EntityCategory.Party),
            (alaron.Aspect, alaron.School, alaron.Category));
        Assert.Equal(2, alaron.Damage);
    }

    [Fact]
    public void AlaronIsTheEntityTheCodeNamesByDbPosition()
    {
        var db = GameDb.Read(TestRoms.Get(RomVersionId.Na10));
        Assert.Equal(new ItemId(0x023F), db.Entities[CharacterImporter.AlaronDbPosition].Id);
        Assert.Equal("Alaron", db.Entities[CharacterImporter.AlaronDbPosition].Name);
        Assert.Equal(0x24E, CharacterImporter.AlaronTrueNameFlag);
    }

    [Fact]
    public void OnlyAlaronHasATrueNameFlag()
    {
        var entities = Set.All<Plugins.Records.EntityRecord>(RecordTypes.Entity).ToList();

        Assert.Equal(221, entities.Count);
        Assert.Equal([Alaron], entities.Where(e => e.TrueNameFlag is not null).Select(e => e.Id));
    }

    [Fact]
    public void AlaronsSkillsAndGearAreThoseOfTheRecord()
    {
        var alaron = Set.Get<Plugins.Records.EntityRecord>(Alaron);

        Assert.Equal((3, 1, 1, 1), (alaron.Skills.Warrior, alaron.Skills.Wizard, alaron.Skills.Stealth, alaron.Skills.Troubadour));
        // No natural weapons: null is a skill that can never be learnt, as a zero is one that hasn't been yet.
        Assert.Equal(new WeaponSkillLevels(Hafted: 1, Missile: 3, Pole: 0, Sword: 3, Thrown: 0), alaron.WeaponSkills);
        Assert.Equal(3, alaron.ShieldSkill);

        Assert.Equal([RecordId.Hex(RecordTypes.Weapon, 0x072C), RecordId.Hex(RecordTypes.Weapon, 0x0711), null], alaron.Weapons);
        Assert.Equal(RecordId.Hex(RecordTypes.Armor, 0x0501), alaron.Armor);
        Assert.Equal(RecordId.Hex(RecordTypes.Armor, 0x0612), alaron.Shield);
    }

    [Fact]
    public void ACharacterWhoCantLearnASkillHasNoLevelInIt()
    {
        var brenna = Get<Plugins.Records.EntityRecord>("aidyn:entity/0x0243");
        var farris = Get<Plugins.Records.EntityRecord>("aidyn:entity/0x0299");

        Assert.Equal("Brenna", brenna.Name);
        Assert.Null(brenna.ShieldSkill);
        Assert.Equal("Farris", farris.Name);
        Assert.Null(farris.Skills.Wizard);
    }

    [Fact]
    public void EmptySpellSlotsAreDroppedAndKnownOnesKeepTheirOrder()
    {
        var db = GameDb.Read(TestRoms.Get(RomVersionId.Na10));
        var entities = Set.All<Plugins.Records.EntityRecord>(RecordTypes.Entity).ToDictionary(e => e.Id);

        foreach (var rom in db.Entities)
        {
            var known = Enumerable.Range(0, 5).Where(i => !rom.Spells[i].IsNone).ToList();
            var spells = entities[DbRecordIds.For(rom.Id)!.Value].Spells;
            Assert.Equal(known.Select(i => DbRecordIds.For(rom.Spells[i])!.Value), spells.Select(s => s.Spell));
            Assert.Equal(known.Select(i => (int)rom.SpellLevels[i]), spells.Select(s => s.Level));
            Assert.All(spells, s => Assert.Equal(RecordTypes.Spell, s.Spell.Type));
        }
        Assert.Contains(entities.Values, e => e.Spells.Count > 0);
        Assert.Contains(entities.Values, e => e.Spells.Count == 0);
    }

    [Fact]
    public void EveryEntitysWeaponSlotsKeepTheirPositions()
    {
        var entities = Set.All<Plugins.Records.EntityRecord>(RecordTypes.Entity).ToList();

        Assert.All(entities, e => Assert.Equal(3, e.Weapons.Count));
        Assert.All(entities, e => Assert.All(e.Weapons, w => Assert.True(w is null || w.Value.Type == RecordTypes.Weapon)));
        Assert.Contains(entities, e => e.Weapons[0] is null && e.Weapons[2] is not null || e.Weapons[0] is not null && e.Weapons[1] is null);
    }

    [Fact]
    public void ResistancesNameAnElementAndMultiplyDamageInQuarters()
    {
        var entities = Set.All<Plugins.Records.EntityRecord>(RecordTypes.Entity).ToList();

        Assert.All(entities, e =>
        {
            Assert.InRange(e.Resistances.Count, 0, 2);
            Assert.All(e.Resistances, r =>
            {
                Assert.NotEqual(Plugins.Records.Element.None, r.Element);
                Assert.InRange(r.Damage, 0f, 2.5f);
                Assert.Equal(0f, r.Damage * 4 % 1f);
            });
        });
        Assert.Contains(entities, e => e.Resistances.Count > 0);
    }

    [Fact]
    public void ExperienceIsFiftyPerPointOfTheRewardByte()
    {
        var db = GameDb.Read(TestRoms.Get(RomVersionId.Na10));
        var entities = Set.All<Plugins.Records.EntityRecord>(RecordTypes.Entity).ToDictionary(e => e.Id);

        Assert.All(db.Entities, rom =>
        {
            var entity = entities[DbRecordIds.For(rom.Id)!.Value];
            Assert.Equal(rom.ExpReward * 50, entity.Experience);
            Assert.Equal((rom.Unk55, rom.Unk56, (int)rom.LootCategory), (entity.Perception, entity.Unk21, entity.LootCategory));
        });
    }

    [Fact]
    public void EntitiesAreStillReadableAsDbRecordsByTheirNamesAndPortraits()
    {
        var alaron = Set.Get<DbRecord>(Alaron);
        var entity = Set.Get<Plugins.Records.EntityRecord>(Alaron);

        Assert.Equal(("Alaron", RecordId.Hex(RecordTypes.Image, 0x0108)), (alaron.Name, alaron.Portrait));
        Assert.Equal((entity.Name, entity.Portrait), (alaron.Name, alaron.Portrait));
    }

    [Fact]
    public void ASpellsTrainingCostIsItsExperienceByteTimesFifty()
    {
        var immolation = Get<Plugins.Records.SpellRecord>("aidyn:spell/0x033D");

        Assert.Equal("Immolation", immolation.Name);
        Assert.Equal((Plugins.Records.MagicSchool.Elemental, 4, 8, 200), (immolation.School, immolation.Wizard, immolation.Stamina, immolation.TrainingCost));

        var db = GameDb.Read(TestRoms.Get(RomVersionId.Na10));
        var spells = Set.All<Plugins.Records.SpellRecord>(RecordTypes.Spell).ToDictionary(s => s.Id);
        Assert.Equal(db.Spells.Count, spells.Count);
        Assert.All(db.Spells, rom =>
        {
            var spell = spells[DbRecordIds.For(rom.Id)!.Value];
            Assert.Equal(rom.ExpModifier * 50, spell.TrainingCost);
            Assert.Equal((int)rom.Special, (int)spell.Special);
            Assert.Equal((int)rom.TargetCount, (int)spell.TargetCount);
        });
    }

    [Fact]
    public void TheSevenStatsHaveNamesCapsAndCosts()
    {
        var stats = Enumerable.Range(0, 7).Select(n => Get<StatRecord>($"aidyn:stat/{n}")).ToList();

        Assert.Equal(["Intelligence", "Willpower", "Dexterity", "Endurance", "Strength", "Stamina", "Level"], stats.Select(s => s.Name));
        Assert.Equal([30, 30, 30, 40, 30, 90, 40], stats.Select(s => s.BaseCap));
        Assert.Equal([40, 40, 40, 50, 40, 120, 40], stats.Select(s => s.Cap));
        Assert.Equal([12, 12, 24, 12, 12, 4, null], stats.Select(s => s.TrainingCost));
        Assert.All(stats.Take(6), s => Assert.False(string.IsNullOrWhiteSpace(s.Description)));
        Assert.Null(stats[6].Description);
        Assert.Equal(Set.Get<StringsRecord>(RecordId.Decimal(RecordTypes.Strings, (int)RomStringTable.Stats)).Lines, stats.Take(6).Select(s => s.Description));
    }

    [Fact]
    public void TheTwelveSkillsHaveNamesDescriptionsAndCosts()
    {
        var skills = Enumerable.Range(0, 12).Select(n => Get<SkillRecord>($"aidyn:skill/{n}")).ToList();

        Assert.Equal(["Alchemist", "Diplomat", "Healer", "Loremaster", "Mechanic", "Merchant", "Ranger", "Stealth", "Thief", "Troubadour", "Warrior", "Wizard"],
            skills.Select(s => s.Name));
        Assert.Equal([1500, 500, 1000, 750, 500, 750, 1000, 500, 1000, 1000, 1000, 1500], skills.Select(s => s.TrainingCost));
        Assert.Equal(750, skills[5].TrainingCost);
        Assert.Equal(1000, skills[6].TrainingCost);
        Assert.All(skills, s => Assert.Equal((10, 15), (s.BaseCap, s.Cap)));
        var lines = Set.Get<StringsRecord>(RecordId.Decimal(RecordTypes.Strings, (int)RomStringTable.Skills)).Lines;
        Assert.Equal(lines.Take(12), skills.Select(s => s.Description));
    }

    [Fact]
    public void TheWeaponSkillsAreTheClassesAndTheShield()
    {
        var skills = Enumerable.Range(0, 12).Select(n => Get<SkillRecord>($"aidyn:weaponskill/{n}")).ToList();

        Assert.Equal(["Bite", "Breath", "Claw", "Hafted", "Missile", "Pole", "Spit", "Sting", "Sword", "Thrown", "Tusk", "Shield"], skills.Select(s => s.Name));
        Assert.Equal([0, 0, 0, 400, 600, 400, 0, 0, 600, 400, 0, 500], skills.Select(s => s.TrainingCost));
        Assert.All(skills, s => Assert.Equal((10, 15), (s.BaseCap, s.Cap)));

        var lines = Set.Get<StringsRecord>(RecordId.Decimal(RecordTypes.Strings, (int)RomStringTable.Skills)).Lines;
        Assert.Equal([null, null, null, lines[12], lines[13], lines[14], null, null, lines[15], lines[16], null, lines[17]], skills.Select(s => s.Description));
    }

    [Fact]
    public void ProgressionHoldsTheLevelsTheAwardAndTheTrainingPrices()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var progression = Get<ProgressionRecord>("aidyn:progression/0");
        var db = GameDb.Read(rom);

        Assert.Equal(CharacterTables.Read(rom).LevelExperience, progression.Levels);
        Assert.Equal(41, progression.Levels.Count);
        Assert.Equal(1.5f, progression.ExpAward);
        Assert.Equal((200, 40, 0.8f, 10), (progression.Training.FirstLevelGold, progression.Training.GoldPerLevel, progression.Training.TrainerExp, progression.Training.SpellMaxLevel));

        var oriana = Assert.Single(progression.Training.FreeTrainers);
        Assert.Equal(RecordId.Hex(RecordTypes.Entity, 0x0236), oriana);
        Assert.Equal("Oriana", Get<Plugins.Records.EntityRecord>(oriana.ToString()).Name);
        // shopkeepNotOriana adds 0x200 to entityList[144].
        Assert.Equal(new ItemId((ushort)(0x200 + CodeSegment.Table(rom, CodeTable.EntityList)[CharacterImporter.OrianaDbPosition])), db.Entities[CharacterImporter.OrianaDbPosition].Id);
    }

    [Fact]
    public void TheCatalogSummarisesTheNewRecordsAndFindsWhatTheEntitiesName()
    {
        var catalog = PluginCatalog.Build(Set);

        Assert.Equal("Alaron", catalog.Find(Alaron)!.Summary);
        Assert.Equal("Immolation", catalog.Find(RecordId.Hex(RecordTypes.Spell, 0x033D))!.Summary);
        Assert.Contains("Intelligence", catalog.Find(RecordId.Decimal(RecordTypes.Stat, 0))!.Summary);
        Assert.Contains("Merchant", catalog.Find(RecordId.Decimal(RecordTypes.Skill, 5))!.Summary);
        Assert.Contains("41 levels", catalog.Find(RecordId.Decimal(RecordTypes.Progression, 0))!.Summary);

        Assert.Contains(catalog.ReferencesTo(RecordId.Hex(RecordTypes.Weapon, 0x072C)), r => r.From == Alaron && r.Path == "weapons[0]");
        Assert.Contains(catalog.ReferencesTo(RecordId.Hex(RecordTypes.Armor, 0x0612)), r => r.From == Alaron && r.Path == "shield");
        Assert.Contains(catalog.ReferencesTo(RecordId.Decimal(RecordTypes.Flag, 590)), r => r.From == Alaron && r.Path == "trueNameFlag");
        Assert.Contains(catalog.ReferencesTo(RecordId.Hex(RecordTypes.Entity, 0x0236)), r => r.From == RecordId.Decimal(RecordTypes.Progression, 0));
    }

    [Fact]
    public void EveryEntityHasTheBodyTheRomsModelTableGivesIt()
    {
        var models = EntityModels.Read(TestRoms.Get(RomVersionId.Na10)).Entities.ToDictionary(m => DbRecordIds.For(m.Entity)!.Value);
        var entities = Set.All<Plugins.Records.EntityRecord>(RecordTypes.Entity).ToList();

        Assert.Equal(221, entities.Count);
        Assert.Equal(entities.Count, models.Count);
        Assert.All(entities, e =>
        {
            var model = models[e.Id];
            Assert.Equal(new EntityBody(RecordId.Hex(RecordTypes.Actor, model.Actor), model.Radius, model.Scale, model.Height), e.Body);
            Assert.NotNull(Set.TryGet<ActorRecord>(e.Body!.Actor));
            Assert.Equal(e.Body, Set.Get<DbRecord>(e.Id).Body);
        });
    }

    [Fact]
    public void DialogueEntitiesHaveTheirTableBodiesWithTheCodesRadiusAndTheBatWhereTheTableLacksThem()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var models = EntityModels.Read(rom).DialogEntities.ToDictionary(m => DbRecordIds.For(m.Entity)!.Value);
        var records = GameDb.Read(rom).DialogEntities.Select(e => DbRecordIds.For(e.Id)).Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();

        Assert.Equal(37, records.Count);
        var fallback = new EntityBody(RecordId.Hex(RecordTypes.Actor, 0x2D4A), 0.55f, 1, 1);
        var without = new List<RecordId>();
        foreach (var id in records)
        {
            var body = Set.Get<DbRecord>(id).Body;
            if (models.TryGetValue(id, out var model))
                Assert.Equal(new EntityBody(RecordId.Hex(RecordTypes.Actor, model.Actor), 0.55f, model.Scale, model.Height), body);
            else
            {
                without.Add(id);
                Assert.Equal(fallback, body);
            }
            Assert.NotNull(Set.TryGet<ActorRecord>(body!.Actor));
        }
        Assert.Equal(5, without.Count);
        Assert.Equal(32, records.Count - without.Count);
    }
}
