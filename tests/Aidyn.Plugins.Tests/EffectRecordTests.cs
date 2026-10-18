using System.Text.Json;
using Aidyn.Plugins.Records;

namespace Aidyn.Plugins.Tests;

public sealed class EffectRecordTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private PluginSet Load(string file, string json) => PluginSet.Load([_temp.Add("aidyn", null, (file, json))]);

    [Fact]
    public void ASpellWithSeveralEffectTypesRoundTripsThroughThePluginJson()
    {
        var shadow = RecordId.Parse("aidyn:entity/0x0299");
        var original = new SpellRecord(
            RecordId.Parse("aidyn:spell/0x0330"), "Test Web", MagicSchool.Star, 2, 10, SpellTargetCount.One, SpellTargetType.Enemy, 6,
            SpellSpecial.Heavy, 1, Reagent.Herb, 400, 0x41, Position: 57,
            Effects:
            [
                new ImmobiliseEffect(Escape: 2, Immune: [shadow]),
                new DamagePerRoundEffect(new Dice(2)),
                new StatEffect(RecordId.Parse("aidyn:stat/3"), Magnitude.Per(-2)),
                new DamageEffect(Magnitude.Fixed(2), [Element.Air]) { For = EffectFor.Opponents },
                new StaminaEffect(Magnitude.Per(2), RaisesMaximum: true) { For = EffectFor.Allies },
                new CureEffect([RecordId.Parse("aidyn:spell/0x0332")], BeatsMargin: true),
                new CharmEffect(),
                new BlastEffect(new Dice(0, 6, 15), Stamina: true),
            ],
            Duration: new EffectDuration(PerLevel: 1800),
            Aspect: Aspect.Lunar, Placed: true, Resisted: Resisted.Always,
            NotResistedBy: [shadow], Filter: new TargetFilter(NotCategories: [EntityCategory.Chaos]), AiValue: 43);

        var json = JsonSerializer.Serialize(new[] { original }, PluginJson.Options);
        Assert.Contains("\"type\": \"immobilise\"", json, StringComparison.Ordinal);
        Assert.Contains("\"type\": \"damagePerRound\"", json, StringComparison.Ordinal);

        var read = Load("spells.json", json).Get<SpellRecord>(original.Id);
        Assert.Equal(original.Effects!.Count, read.Effects!.Count);
        for (var i = 0; i < original.Effects.Count; i++)
        {
            Assert.Equal(original.Effects[i].GetType(), read.Effects[i].GetType());
            Assert.Equal(original.Effects[i].For, read.Effects[i].For);
        }
        Assert.Equal(new ImmobiliseEffect(2, [shadow]).Escape, ((ImmobiliseEffect)read.Effects[0]).Escape);
        Assert.Equal([shadow], ((ImmobiliseEffect)read.Effects[0]).Immune);
        Assert.Equal(new Dice(2), ((DamagePerRoundEffect)read.Effects[1]).Dice);
        Assert.Equal((RecordId.Parse("aidyn:stat/3"), Magnitude.Per(-2)), (((StatEffect)read.Effects[2]).Stat, ((StatEffect)read.Effects[2]).Amount));
        Assert.Equal(EffectFor.Opponents, read.Effects[3].For);
        Assert.Equal([Element.Air], ((DamageEffect)read.Effects[3]).Elements);
        Assert.True(((StaminaEffect)read.Effects[4]).RaisesMaximum);
        Assert.True(((CureEffect)read.Effects[5]).BeatsMargin);
        Assert.Equal(new Dice(0, 6, 15), ((BlastEffect)read.Effects[7]).Dice);
        Assert.True(((BlastEffect)read.Effects[7]).Stamina);
        Assert.Equal(original.Duration, read.Duration);
        Assert.Equal((Aspect.Lunar, true, Resisted.Always, 43), (read.Aspect, read.Placed, read.Resisted, read.AiValue));
        Assert.Equal(original.NotResistedBy, read.NotResistedBy);
        Assert.Equal([EntityCategory.Chaos], read.Filter!.NotCategories);
    }

    [Fact]
    public void ASpellWithoutEffectsKeepsTheDefaults()
    {
        var read = Load("spells.json", """
            [{ "id": "aidyn:spell/0x0311", "name": "Opening", "school": "Naming", "damage": 0, "stamina": 5, "targetCount": "Self",
               "targetType": "Field", "wizard": 4, "special": "None", "range": 1, "reagent": "Spice", "trainingCost": 200, "unk1E": 32 }]
            """).Get<SpellRecord>(RecordId.Parse("aidyn:spell/0x0311"));
        Assert.Null(read.Effects);
        Assert.Null(read.Duration);
        Assert.Equal((Aspect.None, false, Resisted.Never), (read.Aspect, read.Placed, read.Resisted));
    }

    [Fact]
    public void AnEffectTypeNoBuildKnowsLoadsAsThePlainEffectBase()
    {
        var read = Load("spells.json", """
            [{ "id": "aidyn:spell/0x0311", "name": "Opening", "school": "Naming", "damage": 0, "stamina": 5, "targetCount": "Self",
               "targetType": "Field", "wizard": 4, "special": "None", "range": 1, "reagent": "Spice", "trainingCost": 200, "unk1E": 32,
               "effects": [ { "type": "fromTheFuture", "for": "allies", "power": 9 }, { "type": "charm" } ] }]
            """).Get<SpellRecord>(RecordId.Parse("aidyn:spell/0x0311"));
        Assert.Equal(2, read.Effects!.Count);
        Assert.Equal(typeof(Effect), read.Effects[0].GetType());
        Assert.Equal(EffectFor.Allies, read.Effects[0].For);
        Assert.IsType<CharmEffect>(read.Effects[1]);
    }

    [Fact]
    public void APotionAndTheMagicRecordRoundTrip()
    {
        var potion = new ItemRecord(RecordId.Parse("aidyn:item/0x1009"), "Potion of Strength", RecordId.Parse("aidyn:image/0x0001"), 100, ItemKind.Potion,
            Effects: [new StatEffect(RecordId.Parse("aidyn:stat/4"), Magnitude.Fixed(10))], Duration: new EffectDuration(Flat: 648000));
        var magic = new MagicRecord(RecordId.Parse("aidyn:magic/0"), new CastChance(4, 6, 1, 5, [0.75f, 1.25f, 1.25f, 0.75f, 0.75f], [0.5f, 0.75f, 1.25f, 1.5f]),
            new ResistRoll(3, 1, 1, 0.75f, 1.25f), 600, 2, 0.1f, 10, [RecordId.Parse("aidyn:item/0x1010")]);

        var set = Load("a.json", JsonSerializer.Serialize(new object[] { potion, magic }, PluginJson.Options));
        var readPotion = set.Get<ItemRecord>(potion.Id);
        Assert.Equal(648000u, readPotion.Duration!.At(0));
        Assert.Equal(10, ((StatEffect)Assert.Single(readPotion.Effects!)).Amount.At(0));
        var readMagic = set.Get<MagicRecord>(magic.Id);
        Assert.Equal(magic.Chance.ByMoon, readMagic.Chance.ByMoon);
        Assert.Equal(magic.Chance.ByPartOfDay, readMagic.Chance.ByPartOfDay);
        Assert.Equal((600, 2, 0.1f, 10, 15, 7), (readMagic.RoundTicks, readMagic.MostDisabled, readMagic.Improvement, readMagic.MaxLevel, readMagic.SpellEffects, readMagic.PotionEffects));
        Assert.Equal(magic.KeptAfterBattle, readMagic.KeptAfterBattle);
    }
}
