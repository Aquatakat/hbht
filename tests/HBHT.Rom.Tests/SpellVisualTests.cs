using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Tests;

/// <summary>
/// What each spell and potion shows in a battle (<see cref="SpellVisualTables"/>, importer 46): the five switch functions of 1.0's
/// <c>combatspellvisuals.cpp</c> and the two immediates, decoded from every ROM and compared with 1.0's own functions run in the interpreter
/// (session 10's research, <c>kind:Borg:memory</c> below), and what the import makes of them.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SpellVisualTests(Na10Import fixture)
{
    // The cases of each function, as kind:Borg:memory in hex, kinds in decimal.
    private const string Caster =
        "2:1D7A:11C00 3:1AD9:4C00 10:1AB9:11C00 12:1E50:3800 13:1B51:11C00 15:1B37:4C00 19:1C8E:11C00 20:1DA1:8C00 21:1DBD:3400 23:1B68:4000 " +
        "25:1AF8:C400 26:1EF7:1800 29:1BD8:17400 30:1CF3:5800 32:1DFD:7C00 35:1DE7:5800 37:1D43:C800 38:1E65:9000 39:1E76:6800 40:1E89:6800 " +
        "41:1E9D:3400 46:1DB6:4800 47:1B29:22800 48:1E0E:4800 51:1A48:11C00 54:1D31:C400";

    private const string Target =
        "0:1D0D:6000 1:1C9F:4800 4:1B87:5000 5:1BF3:2C00 6:1C55:4C00 7:1C66:8000 8:1CC9:5000 9:1D21:8000 11:1EEC:A400 14:1D52:2800 " +
        "15:1C9F:4800 16:1B79:4000 17:1B95:5000 21:1DC4:1C00 22:1ECD:2800 24:1AC6:5000 27:1BA3:5000 28:1BBC:C400 31:1DD6:5000 33:1E42:5400 " +
        "34:1EC0:C800 42:1C07:1FC00 43:1C18:6000 44:1C30:1CC00 45:1C44:F800 52:1CB9:2400 53:1CDB:7800 56:1E32:7800 57:1E1C:3800 58:1EDF:8400";

    private const string Aura =
        "0:1D0F:2000 3:1ACC:2000 4:1BA4:2400 5:1BE7:2800 10:1E33:3C00 13:1B47:3C00 14:1D46:2800 16:1B6B:2400 17:1BA5:2400 19:1C76:3C00 " +
        "22:1EC3:2800 23:1B56:2400 24:1AC7:2000 25:1ADD:2C00 26:1EFC:2800 27:1BA6:2400 30:1CE3:3000 31:1DC7:3800 32:1DEC:2400 34:1EA9:6400 " +
        "35:1DDB:2000 37:1D34:2000 38:1E55:3400 39:1E69:3C00 40:1E7A:3C00 41:1E8D:2400 46:1DA6:2000 47:1B07:5C00 48:1E02:2000 51:1BF4:3C00 " +
        "52:1CAD:2800 53:1CCF:3400 56:1E21:2800 57:1E02:2000";

    private const string Potion =
        "0:1A56:3000 1:1A00:6400 2:1A83:3C00 3:19CB:3C00 4:1A65:4000 5:1A91:4800 6:1A10:6400 7:19DB:4400 8:1A74:5000 9:1AB9:12000 " +
        "10:1A48:12000 15:1A20:5400";

    private const string PotionAura = "3:19C0:2000 9:1AA1:3C00 10:1A30:3C00 15:1A14:2000";

    private static readonly int[] Heads = [4, 5, 14, 16, 17, 22, 27, 28, 31, 52];

    private static Dictionary<int, VisualBorg> Parse(string table) =>
        table.Split(' ').Select(e => e.Split(':')).ToDictionary(
            p => int.Parse(p[0]), p => new VisualBorg(Convert.ToInt32(p[1], 16), Convert.ToInt32(p[2], 16)));

    private static RecordId Spell(int id) => RecordId.Hex(RecordTypes.Spell, id);

    private static RecordId PotionItem(int n) => RecordId.Hex(RecordTypes.Item, 0x1000 + n);

    private static RecordId Actor(int borg) => RecordId.Hex(RecordTypes.Actor, borg);

    private static RecordId Scene(int borg) => RecordId.Hex(RecordTypes.Scene, borg);

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheTablesReadTheSameInEveryVersion(RomVersionId id)
    {
        var tables = SpellVisualTables.Read(TestRoms.Get(id));
        Assert.Equal(Parse(Caster), tables.Caster);
        Assert.Equal(Parse(Target), tables.Target);
        Assert.Equal(Parse(Aura), tables.Aura);
        Assert.Equal(Parse(Potion), tables.Potion);
        Assert.Equal(Parse(PotionAura), tables.PotionAura);
        Assert.Equal(Heads.Order(), tables.TargetAtHead.Order());
        Assert.Equal((0x1CAA, 0x37F1), (tables.Refused, tables.Resisted));
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheSpotValuesHold(RomVersionId id)
    {
        var tables = SpellVisualTables.Read(TestRoms.Get(id));
        Assert.Equal(new VisualBorg(0x1C8E, 0x11C00), tables.Caster[19]);
        Assert.Equal(0x1CC9, tables.Target[8].Borg);
        Assert.Equal(0x1C76, tables.Aura[19].Borg);
        Assert.Equal(0x1AB9, tables.Potion[9].Borg);
        Assert.Equal(0x1A14, tables.PotionAura[15].Borg);
        // Detecting Traps, Poison, Detect Moon Phase, Detect Sun Phase and Stealth show nothing at all.
        foreach (var kind in new[] { 18, 36, 49, 50, 55 })
        {
            Assert.DoesNotContain(kind, tables.Caster.Keys);
            Assert.DoesNotContain(kind, tables.Target.Keys);
            Assert.DoesNotContain(kind, tables.Aura.Keys);
        }
        Assert.Equal((26, 30, 34, 12, 4), (tables.Caster.Count, tables.Target.Count, tables.Aura.Count, tables.Potion.Count, tables.PotionAura.Count));
    }

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void EveryBorgTheTablesNameIsOfItsType(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var tables = SpellVisualTables.Read(rom);
        var archive = BorgArchive.Open(rom);
        BorgType Type(int borg) => archive.Listings[borg].Type;
        Assert.All(tables.Caster.Values.Concat(tables.Target.Values).Concat(tables.Potion.Values), v => Assert.Equal(BorgType.Actor, Type(v.Borg)));
        Assert.All(tables.Aura.Values.Concat(tables.PotionAura.Values), v => Assert.Equal(BorgType.Scene, Type(v.Borg)));
        Assert.Equal(BorgType.Actor, Type(tables.Refused));
        Assert.Equal(BorgType.Actor, Type(tables.Resisted));
    }

    [Fact]
    public void ASpellHasItsVisuals()
    {
        var set = fixture.Data.Set;
        // Endurance (kind 19) shows an actor at the caster and a lasting aura on the target.
        Assert.Equal(new MagicVisuals(Actor(0x1C8E), null, false, Scene(0x1C76), 0x11C00, 0), set.Get<SpellRecord>(Spell(0x0310)).Visuals);
        // Fireball (kind 8) an actor at the target; Banishing (15) both, sharing Escape's; Crushing Death (28) lifts its actor to the head.
        Assert.Equal(new MagicVisuals(null, Actor(0x1CC9), false, null, 0, 0x5000), set.Get<SpellRecord>(Spell(0x0305)).Visuals);
        Assert.Equal(new MagicVisuals(Actor(0x1B37), Actor(0x1C9F), false, null, 0x4C00, 0x4800), set.Get<SpellRecord>(Spell(0x030A)).Visuals);
        Assert.Equal(new MagicVisuals(null, Actor(0x1BBC), true, null, 0, 0xC400), set.Get<SpellRecord>(Spell(0x0315)).Visuals);
    }

    [Fact]
    public void TheSpellsThatShowNothingHaveNoVisuals()
    {
        var spells = fixture.Data.Set.All<SpellRecord>(RecordTypes.Spell);
        Assert.Equal(59 - 5, spells.Count(s => s.Visuals is not null));
        Assert.Equal([18, 36, 49, 50, 55], spells.Where(s => s.Visuals is null).Select(s => s.Position!.Value).Order());
        // Every spell's visuals are the tables' by its place in the list.
        var tables = SpellVisualTables.Read(TestRoms.Get(RomVersionId.Na10));
        Assert.All(spells.Where(s => s.Visuals is not null), s =>
        {
            var kind = s.Position!.Value;
            var v = s.Visuals!;
            Assert.Equal(tables.Caster.TryGetValue(kind, out var c) ? Actor(c.Borg) : (RecordId?)null, v.Caster);
            Assert.Equal(tables.Target.TryGetValue(kind, out var t) ? Actor(t.Borg) : (RecordId?)null, v.Target);
            Assert.Equal(tables.Aura.TryGetValue(kind, out var a) ? Scene(a.Borg) : (RecordId?)null, v.Aura);
            Assert.Equal(tables.TargetAtHead.Contains(kind), v.TargetAtHead);
        });
    }

    [Fact]
    public void APotionHasItsVisuals()
    {
        var set = fixture.Data.Set;
        // The Strength Potion's actor is the Strength spell's, with its own aura; the Healing Potion has an actor alone.
        Assert.Equal(new MagicVisuals(null, Actor(0x1AB9), false, Scene(0x1AA1), 0, 0x12000), set.Get<ItemRecord>(PotionItem(9)).Visuals);
        Assert.Equal(new MagicVisuals(null, Actor(0x1A65), false, null, 0, 0x4000), set.Get<ItemRecord>(PotionItem(4)).Visuals);
        Assert.Equal(Scene(0x1A14), set.Get<ItemRecord>(PotionItem(15)).Visuals!.Aura);
        // The two placeholders, Clarity, Charisma and Stealth show nothing.
        Assert.All(new[] { 11, 12, 13, 14, 16 }, n => Assert.Null(set.Get<ItemRecord>(PotionItem(n)).Visuals));
        Assert.Equal(12, Enumerable.Range(0, 17).Count(n => set.Get<ItemRecord>(PotionItem(n)).Visuals is not null));
        // Nothing else has visuals.
        Assert.All(set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Kind != ItemKind.Potion), i => Assert.Null(i.Visuals));
    }

    [Fact]
    public void TheMagicRecordNamesTheRefusedAndResistedActors()
    {
        var magic = fixture.Data.Set.Singleton<MagicRecord>();
        Assert.Equal(Actor(0x1CAA), magic.Refused);
        Assert.Equal(Actor(0x37F1), magic.Resisted);
    }

    [Fact]
    public void SevenOfTheVisualsActorsAreParticlesAloneWhoseAnimationsHoldTheirScenesStill()
    {
        // The refused and resisted casts' actors, Dragon Flames', Banishing's and Escape's targets', vs. Star's and Wraith Touch's
        // casters' and the Fire Flask's have no models, and every Borg6 of theirs targets nothing: the importer lists them as still
        // animations, so Godot gives their scenes no animation player. Their states still run, and request 5 starts their emitters,
        // which are all they show (the runtime's ActorAnimator runs them without a player).
        var set = fixture.Data.Set;
        var magic = set.Singleton<MagicRecord>();
        var spells = set.All<SpellRecord>(RecordTypes.Spell).ToList();
        var actors = new[]
        {
            magic.Refused!.Value, magic.Resisted!.Value,
            spells.Single(s => s.Name == "Dragon Flames").Visuals!.Target!.Value,
            spells.Single(s => s.Name == "Banishing").Visuals!.Target!.Value,
            spells.Single(s => s.Name == "vs. Star").Visuals!.Caster!.Value,
            spells.Single(s => s.Name == "Wraith Touch").Visuals!.Caster!.Value,
            set.Get<ItemRecord>(PotionItem(0)).Visuals!.Target!.Value,
        };
        Assert.Equal([Actor(0x1CAA), Actor(0x37F1), Actor(0x1C55), Actor(0x1C9F), Actor(0x1E9D), Actor(0x1EF7), Actor(0x1A56)], actors);
        Assert.All(actors, id =>
        {
            var actor = set.Get<ActorRecord>(id);
            var clips = Plugins.Assets.ActorClips.For(set, actor.Scene, actor.RootTransform)!;
            Assert.False(clips.Animated);
            Assert.All(actor.States, s => Assert.True(clips.Clip(s.Animation)?.Length > 0));
            var emitters = set.Get<EmittersRecord>(RecordId.Hex(RecordTypes.Emitters, Convert.ToInt32(actor.Scene.Key, 16))).Emitters;
            Assert.Contains(emitters, e => e.Request == 5 && e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest));
        });
    }
}
