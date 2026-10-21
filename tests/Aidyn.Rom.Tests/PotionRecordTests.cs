using System.Text.Json;
using System.Text.Json.Nodes;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

using static Aidyn.Rom.Tests.CombatCheckpoints;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Potions and areas as data (ADR 0021): the imported potions say how they're used as the enum the core keyed them by did, the
/// imported spells say which is an area as the combat record's list did, and a hand-written plugin's spell that heals and potion
/// that dispels work, as the instant effects land in one place for any spell, potion or flask.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class PotionRecordTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static readonly RecordId Fireball = RecordId.Hex(RecordTypes.Spell, 0x0305);

    private static readonly RecordId AirShield = RecordId.Hex(RecordTypes.Spell, 0x0300);

    [Fact]
    public void TheImportedPotionsAreUsedAsTheEnumHadThem()
    {
        var potions = Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Kind == ItemKind.Potion).ToList();
        Assert.Equal(Enum.GetValues<BasePotion>().Select(PotionId), potions.Select(p => p.Id));
        // Only potions say how they're used.
        Assert.All(Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Kind != ItemKind.Potion), i => Assert.Null(i.Use));
        foreach (var potion in Enum.GetValues<BasePotion>())
        {
            var record = Set.Get<ItemRecord>(PotionId(potion));
            // The enum's flasks were the first four; C-up's menu took every kind but those and Stealth; trek drank all but the flasks.
            var flask = potion < BasePotion.Healing;
            Assert.Equal(flask, Potions.IsFlask(record));
            Assert.Equal(!flask && potion != BasePotion.Stealth, record.Use!.Value.HasFlag(PotionUse.DrinkInBattle));
            Assert.Equal(!flask, record.Use!.Value.HasFlag(PotionUse.DrinkInTrek));
            // The flasks' effects are their blasts, and only theirs.
            Assert.Equal(flask, record.Effects?.Any(e => e is BlastEffect) == true);
        }
    }

    [Fact]
    public void FireballAloneIsAnAreaAsTheCombatRecordsListHadIt()
    {
        var areas = Set.All<SpellRecord>(RecordTypes.Spell).Where(s => s.Area).ToList();
        var fireball = Assert.Single(areas);
        Assert.Equal(Fireball, fireball.Id);
        // The code keys the area by spell kind 8 (EnemyAI.AreaCentre, 0x80058008).
        Assert.Equal(8, fireball.Position);
        Assert.True(fireball.Placed);
    }

    /// <summary>The base plugin with a hand-written one after it: a spell that heals and a potion that dispels the Elemental school.</summary>
    private (PluginSet Set, TempPlugins Temp) Modded()
    {
        var shield = Set.Get<SpellRecord>(AirShield);
        var mending = shield with
        {
            Id = RecordId.Parse("testmod:spell/mending"),
            Name = "Mending",
            School = MagicSchool.None,
            Effects = [new HealEffect(50, 50)],
            Duration = null,
            Position = null,
            AiValue = null,
            Visuals = null,
        };
        var healing = Set.Get<ItemRecord>(PotionId(BasePotion.Healing));
        var draught = healing with
        {
            Id = RecordId.Parse("testmod:item/clearing_draught"),
            Name = "Clearing Draught",
            Description = null,
            Effects = [new DispelEffect(MagicSchool.Elemental)],
            Duration = null,
            Use = PotionUse.DrinkInTrek | PotionUse.DrinkInBattle,
        };
        var temp = new TempPlugins();
        var dir = temp.Add("testmod", ["aidyn"],
            ("spells.json", new JsonArray(JsonSerializer.SerializeToNode(mending, PluginJson.Options)).ToJsonString()),
            ("items.json", new JsonArray(JsonSerializer.SerializeToNode(draught, PluginJson.Options)).ToJsonString()));
        return (PluginSet.Load([fixture.Data.Dir, dir]), temp);
    }

    [Fact]
    public void AModdedSpellThatHealsHealsAndAModdedPotionThatDispelsDispels()
    {
        var (set, temp) = Modded();
        using var _ = temp;
        var built = Plain(set, 1);
        var battle = built.Battle;
        var game = built.Game;
        var alaron = battle.Combatants.First(c => c.Sheet.Name == "Alaron");
        var rheda = battle.Combatants.First(c => c.Sheet.Name == "Rheda");
        var magic = game.Rules.Magic!;
        var catalog = game.Party.Inventory.Catalog;
        var wand = new ItemInstance(catalog[PotionId(BasePotion.Healing)]) { Charges = 9 };

        // The spell heals half of Alaron's maximum, cast from an item so its margin needs no roll.
        alaron.Sheet.TakeDamage(20, alaron);
        var hurt = alaron.Sheet.Hp;
        var mending = magic.Spell(RecordId.Parse("testmod:spell/mending"))!;
        var cast = Assert.Single(battle.Cast(rheda, new SpellSource(mending, Item: wand, ItemLevel: 1), [alaron]));
        Assert.Equal(CastResult.Effect, cast.Result);
        Assert.Equal(Math.Min(hurt + alaron.Sheet.MaxHp / 2, alaron.Sheet.MaxHp), alaron.Sheet.Hp);
        Assert.Equal(alaron.Sheet.Hp - hurt, alaron.Healed);

        // Air Shield on Rheda, an Elemental effect for the draught to take off.
        var protection = rheda.Sheet.Protection;
        var shield = magic.Spell(AirShield)!;
        Assert.Equal(MagicSchool.Elemental, shield.School);
        Assert.Equal(CastResult.Effect, Assert.Single(battle.Cast(rheda, new SpellSource(shield, Item: wand, ItemLevel: 2), [rheda])).Result);
        Assert.True(rheda.Sheet.HasEffect(AirShield));
        Assert.Equal(protection + 4, rheda.Sheet.Protection);

        // The draught is in C-up's menu while there's something to dispel, and drinking it takes the shield off.
        var draught = catalog[RecordId.Parse("testmod:item/clearing_draught")];
        game.Party.Inventory.Add(draught.Id, 2);
        Assert.Contains(battle.ItemRows(rheda), r => r.Potion == draught);
        Assert.DoesNotContain(battle.ItemRows(alaron), r => r.Potion == draught);
        Assert.Equal(DrinkResult.Drunk, Potions.Drink(game.Party, rheda.Sheet, draught, game.Random.Combat, rheda, battle));
        Assert.False(rheda.Sheet.HasEffect(AirShield));
        Assert.Equal(protection, rheda.Sheet.Protection);
        Assert.Equal(1, game.Party.Inventory.Count(draught.Id));
        Assert.Equal(DrinkResult.NotNow, Potions.CanDrink(rheda.Sheet, draught, inBattle: true));
    }
}
