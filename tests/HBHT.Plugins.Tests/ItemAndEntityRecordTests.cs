using HBHT.Plugins.Records;

namespace HBHT.Plugins.Tests;

/// <summary>The item and entity records of ADRs 0031–0033: what validation asks of them, and how a mod patches their keyed maps.</summary>
public sealed class ItemAndEntityRecordTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private const string Items = """
        [
          { "id": "aidyn:item/0x0712", "name": "Club", "icon": "aidyn:image/0x0001", "price": 5, "kind": "Weapon",
            "weapon": { "skill": "aidyn:weaponskill/3", "damage": 3, "range": 0, "hit": 0, "animation": "aidyn:request/12" } },
          { "id": "aidyn:item/0x0C01", "name": "Ring", "icon": "aidyn:image/0x0001", "price": 5, "kind": "Ring",
            "weapon": { "skill": "aidyn:weaponskill/3", "damage": 3, "range": 0, "hit": 0, "animation": "aidyn:request/12" } },
          { "id": "aidyn:item/0x0713", "name": "Mace", "icon": "aidyn:image/0x0001", "price": 5, "kind": "Weapon" },
        ]
        """;

    [Fact]
    public void AWeaponNeedsItsWeaponFieldsAndNoOtherKindHasThem()
    {
        var problems = _temp.Aidyn(("items.json", Items)).Validate().Problems;

        Assert.Equal(["aidyn:item/0x0713 weapon", "aidyn:item/0x0C01 weapon"],
            problems.Select(p => $"{p.Record} {p.Path}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnEntityWithoutASheetHasNoCombatTraitsTrainingOrMembershipAndOnlyItTrainsAsAnother()
    {
        var problems = _temp.Aidyn(("entities.json", """
            [
              { "id": "aidyn:entity/0x1401", "name": "Sign", "perception": 10, "trainsAs": "aidyn:entity/0x023F" },
              { "id": "aidyn:entity/0x1402", "name": "Odd", "combat": { "rooted": true }, "trainsFree": true },
              { "id": "aidyn:entity/0x023F", "name": "Alaron", "trainsAs": "aidyn:entity/0x02C4",
                "sheet": { "category": "Party", "aspect": "Solar", "school": "aidyn:school/0", "trueName": false, "heavy": true, "level": 1,
                  "attributes": {}, "skills": {}, "weaponSkills": {}, "spells": [], "weapons": {},
                  "armor": null, "shield": null, "protection": 0, "damage": 2, "morale": 0, "resistances": [], "experience": 0, "lootCategory": 0 } },
            ]
            """)).Validate().Problems;

        Assert.Equal(["aidyn:entity/0x023F trainsAs", "aidyn:entity/0x1402 combat", "aidyn:entity/0x1402 trainsFree"],
            problems.Select(p => $"{p.Record} {p.Path}").Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AModPatchesASheetsSkillsKeyByKeyAndAddsItsOwn()
    {
        var set = PluginSet.Load([
            _temp.Add("aidyn", null, ("entities.json", """
                { "id": "aidyn:entity/0x023F", "name": "Alaron", "perception": 10,
                  "sheet": { "category": "Party", "aspect": "Solar", "school": "aidyn:school/0", "trueName": false, "heavy": true, "level": 1,
                    "attributes": { "aidyn:stat/0": 18 }, "skills": { "aidyn:skill/7": 1, "aidyn:skill/10": 3 },
                    "weaponSkills": { "aidyn:weaponskill/8": 3, "aidyn:weaponskill/11": 3 }, "spells": [], "weapons": { "joins": "aidyn:item/0x0712" },
                    "armor": null, "shield": null, "protection": 0, "damage": 2, "morale": 0, "resistances": [], "experience": 0, "lootCategory": 0 } }
                """)),
            _temp.Add("mymod", ["aidyn"], ("alaron.json", """
                { "patch": "aidyn:entity/0x023F", "sheet": { "skills": { "aidyn:skill/7": 4, "mymod:skill/herbalism": 2 } } }
                """)),
        ]);

        var sheet = set.Get<EntityRecord>(RecordId.Parse("aidyn:entity/0x023F")).Sheet!;
        Assert.Equal(4, sheet.SkillLevel(RecordId.Parse("aidyn:skill/7")));
        Assert.Equal(3, sheet.SkillLevel(RecordId.Parse("aidyn:skill/10")));
        Assert.Equal(2, sheet.SkillLevel(RecordId.Parse("mymod:skill/herbalism")));
        Assert.Null(sheet.SkillLevel(RecordId.Parse("aidyn:skill/0")));
        Assert.Equal(3, sheet.SkillLevel(RecordId.Parse("aidyn:weaponskill/11")));
        Assert.Equal(RecordId.Parse("aidyn:item/0x0712"), sheet.Weapons.AsEnemy);
        Assert.Empty(set.Validate().Problems);
    }

    [Fact]
    public void ARequestIsPlayedByItsBaseNumberAndAModsByAHandlePastThem()
    {
        Assert.Equal(16, Requests.Number(RecordId.Parse("aidyn:request/16")));
        var mod = Requests.Number(RecordId.Parse("mymod:request/cartwheel"));
        Assert.True(mod >= Requests.FirstModHandle);
        Assert.Equal(mod, Requests.Number(RecordId.Parse("mymod:request/cartwheel")));
        Assert.Equal(RecordId.Parse("mymod:request/cartwheel"), Requests.Id(mod));
        Assert.Equal(Requests.Any, new ActorBranch(null, 100, false, 0).Number);
    }
}
