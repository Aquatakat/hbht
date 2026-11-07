using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Import;

namespace HBHT.Rom.Tests;

/// <summary>
/// The combat record's weapons that fly (importer 42), and its boss flags, the Goblin Ambush's rules and the game over, the scripts'
/// endings and the camp ambushes (importer 43), checked against the ROM and 1.0's machine code.
/// </summary>
public sealed class CombatImportTests(Na10Import fixture)
{
    private static RecordId Weapon(int id) => RecordId.Hex(RecordTypes.Item, id);

    private CombatRecord Combat => fixture.Data.Set.Get<CombatRecord>(RecordId.Decimal(RecordTypes.Combat, 0));

    private static readonly RecordId Arrow = RecordId.Hex(RecordTypes.Scene, 0x348F);

    /// <summary>Each weapon that flies by its record, the weapon DB's name, the arrow or null, <c>WeaponMuzzle</c>'s speed and turn.</summary>
    public static TheoryData<int, string, bool, float, float> Flying => new()
    {
        { 0x0760, "Bow of Accuracy", true, 9.9f, 0 },
        { 0x075F, "Bow of Shielding", true, 8, 0 },
        { 0x075E, "Bow of Thunder", true, 9, 0 },
        { 0x0755, "Heartseeker Bow", true, 9, 0 },
        { 0x0718, "Great Bow", true, 9, 0 },
        { 0x0719, "Hunter's Bow", true, 8, 0 },
        { 0x071A, "Long Bow", true, 8, 0 },
        { 0x071C, "Short Bow", true, 5, 0 },
        { 0x075A, "Dragon Fang", false, 8, 0 },
        { 0x071D, "Throwing Iron", false, 8, 0 },
        { 0x0765, "Throwing Knife", false, 5, 0 },
        { 0x074D, "Cyclops Hurlstar", false, 5, 0 },
        { 0x0763, "Dart of Distance", false, 8, 0 },
        { 0x075D, "Poison Dart", false, 5, 0 },
        { 0x072F, "Hatchet", false, 5, 0 },
        { 0x0730, "Javelin", false, 5, 90 },
    };

    private IReadOnlyList<ItemRecord> Flyers => [.. fixture.Data.Set.All<ItemRecord>(RecordTypes.Item).Where(i => i.Weapon?.Missile is not null)];

    private IReadOnlyList<EntityRecord> Entities => [.. fixture.Data.Set.All<EntityRecord>(RecordTypes.Entity)];

    [Fact]
    public void SixteenWeaponsFly()
    {
        Assert.Equal(16, Flyers.Count);
        Assert.Equal(16, Flyers.Select(m => m.Id).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Flying))]
    public void AWeaponThatFliesIsAnArrowOrItsOwnModel(int id, string name, bool bow, float speed, float turn)
    {
        var item = Assert.Single(Flyers, m => m.Id == Weapon(id));
        var missile = item.Weapon!.Missile!;
        Assert.Equal(name, item.Name);
        Assert.Equal(bow ? Arrow : (RecordId?)null, missile.Scene);
        Assert.Equal(speed, missile.Speed);
        Assert.Equal(turn, missile.Turn);
        // What a thrown one flies as, its own model, is on its record.
        if (!bow)
            Assert.NotNull(item.Model);
    }

    [Fact]
    public void BowsAreTheBowsOfTheMissileClassAndThrownWeaponsTheThrownOnes()
    {
        Assert.All(Flyers.Where(m => m.Weapon!.Missile!.Scene is not null), m => Assert.Equal(RomIds.WeaponSkill(WeaponClass.Missile), m.Weapon!.Skill));
        Assert.All(Flyers.Where(m => m.Weapon!.Missile!.Scene is null), m => Assert.Equal(RomIds.WeaponSkill(WeaponClass.Thrown), m.Weapon!.Skill));
        Assert.Equal(8, Flyers.Count(m => m.Weapon!.Missile!.Scene is not null));
    }

    [Fact]
    public void OnlySpikesMayStrikeInMelee()
    {
        var spikes = Assert.Single(fixture.Data.Set.All<ItemRecord>(RecordTypes.Item), i => i.Weapon?.StrikesInMelee == true);
        Assert.Equal(Weapon(0x071B), spikes.Id);
        Assert.Equal("Spikes", spikes.Name);
        // It is a thrown weapon that does not fly: it is in neither list.
        Assert.Null(spikes.Weapon!.Missile);
    }

    /// <summary>The lists are the same set in every version, which <c>CodeTable.BowKindWeapons</c> and <c>ThrownKindWeapons</c> find.</summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheListsOfWeaponsThatFlyAreTheSameInEveryVersion(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        Assert.Equal([53, 55, 59, 54, 58, 56, 57, 60, 0xFF], rom.Table(CodeTable.BowKindWeapons).ToArray());
        Assert.Equal([99, 98, 97, 101, 102, 96, 94, 95, 0xFF], rom.Table(CodeTable.ThrownKindWeapons).ToArray());
    }
    /// <summary>The combat menus' and Sense Aura's strings with their 1.0 addresses (<c>0x800D…</c>), as the plugin should have them.</summary>
    public static TheoryData<int, string> TextAt => new()
    {
        { 0x4880, "Lay On Hands" },
        { 0x4890, "Heal With Herbs" },
        { 0x48A0, "Perform" },
        { 0x4858, "No spells or skills available" },
        { 0x4834, "No potions or item spells available" },
        { 0x3250, "Follower" },
        { 0x325C, "Leader" },
        { 0x3264, "%d Total Hitpoints" },
        { 0x3278, "Level %lu" },
        { 0x3284, "Total Armor Protection %d" },
        { 0x3214, "Immune to %s" },
        { 0x3224, "Resistant to %s" },
        { 0x3234, "Vulnerable to %s" },
    };

    /// <summary>1.0 loads the code segment at <c>0x80000400</c> from ROM <c>0x1000</c>; <paramref name="address"/> is <c>0x800D…</c> less its top.</summary>
    private static int RomOffset(int address) => (int)(0x800D0000L + address - 0x80000400L + 0x1000);

    private static string CString(RomImage rom, int address)
    {
        var data = rom.Data[RomOffset(address)..];
        return System.Text.Encoding.ASCII.GetString(data[..data.IndexOf((byte)0)]);
    }

    [Theory]
    [MemberData(nameof(TextAt))]
    public void TheTextIsTheRomsCStringAtItsAddress(int address, string text)
    {
        Assert.Equal(text, CString(TestRoms.Get(RomVersionId.Na10), address));
        var c = Combat.Text!;
        var aura = c.Aura;
        Assert.Equal(text, address switch
        {
            0x4880 => c.LayOnHands, 0x4890 => c.HealWithHerbs, 0x48A0 => c.Perform, 0x4858 => c.NoSpells, 0x4834 => c.NoItems,
            0x3250 => aura.Follower, 0x325C => aura.Leader, 0x3264 => aura.Hitpoints, 0x3278 => aura.Level, 0x3284 => aura.Protection,
            0x3214 => aura.Immune, 0x3224 => aura.Resistant, 0x3234 => aura.Vulnerable,
            _ => throw new ArgumentOutOfRangeException(nameof(address)),
        });
    }

    /// <summary><c>Entity.AppendResistanceLine</c> (<c>0x8006FFB0</c>) compares with the float at <c>0x800D3248</c>: 2.0.</summary>
    [Fact]
    public void AMultiplierBelowTwoIsResistantAndTwoOrMoreVulnerable()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        Assert.Equal(2.0f, Be.F32(rom.Data, RomOffset(0x3248)));
        Assert.Equal(2.0f, Combat.Text!.Aura.ResistantBelow);
    }

    /// <summary>The element names are <c>String/Common</c>'s lines 241 to 257, which <c>0x800E0C00</c> points at, one per element in its order.</summary>
    [Fact]
    public void TheElementNamesAreOneForEachElementInOrder()
    {
        // The names are the element records' now (None has no record).
        var elements = Enum.GetValues<Db.Element>().Where(e => e != Db.Element.None).ToList();
        var names = elements.ToDictionary(e => e, e => fixture.Data.Set.Get<ElementRecord>(RomIds.Element(e)).Name);
        Assert.Equal(
            ["Earth", "Solar", "Physical", "Necromancy", "Fire", "Lunar", "Naming", "Water", "Magic", "Air", "Star", "Elemental",
             "Chaos", "Cutting", "Smashing", "Holy"],
            elements.Select(e => names[e]));
        // The names read as the enum's own, so the order is the code's: the element byte indexes the pointer.
        Assert.All(elements, e => Assert.Equal(e.ToString(), names[e]));
        Assert.Equal(16, Set.All<ElementRecord>(RecordTypes.Element).Count());
    }

    /// <summary>The menus' strings sit elsewhere in each version but read the same, as do the element names' Common lines.</summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheTextIsTheSameInEveryVersion(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var text = CombatImporter.ReadText(rom);
        var na10 = Combat.Text!;
        Assert.Equal(na10.LayOnHands, text.LayOnHands);
        Assert.Equal(na10.HealWithHerbs, text.HealWithHerbs);
        Assert.Equal(na10.Perform, text.Perform);
        Assert.Equal(na10.NoSpells, text.NoSpells);
        Assert.Equal(na10.NoItems, text.NoItems);
        Assert.Equal(na10.Aura, text.Aura);
    }

    private PluginSet Set => fixture.Data.Set;

    [Fact]
    public void TheBossFlagsAreSetKillFlagsCasesByEntity()
    {
        // The jump table's cases by entity list place, read as the entities the old fact table named by ID.
        Assert.Equal(Quests.EngineFlags.BossFlags.OrderBy(b => b.Value).Select(b => (Import.DbRecordIds.For(b.Key), RecordId.Decimal(RecordTypes.Flag, b.Value))),
            Entities.Where(e => e.Combat?.BossFlag is not null).OrderBy(e => int.Parse(e.Combat!.BossFlag!.Value.Key)).Select(e => ((RecordId?)e.Id, e.Combat!.BossFlag!.Value)));
    }

    [Fact]
    public void TheGameOverStartsFromItsDialogueObjectInTheDreamscape()
    {
        var gameOver = Combat.GameOver!;
        var dreamscape = (ScriptObject)ChunkRecord.Find(Set, gameOver.From)!;
        Assert.Equal(RecordId.Hex(RecordTypes.Script, 0x0512), dreamscape.Plays);
        Assert.Equal(Ids.MapObject(6, "C03", 2), gameOver.From);
        Assert.NotNull(dreamscape.Stage);
        Assert.Equal(new[] { 0x0512, 0x0513, 0x0514 }.Select(n => RecordId.Hex(RecordTypes.Script, n)), Set.All<TriggerRecord>(RecordTypes.Trigger)
            .Where(t => t.Do is [ScriptOp.GameOver]).Select(t => t.Subject!.Value));
    }

    [Fact]
    public void TheGoblinAmbushIsNonLethalAndEndsInOrianasHut()
    {
        // The rules are on what starts the battle (ADR 0035): script 0x0365 and the three npcs that fight under its flag, 978.
        var script = Set.Get<ScriptRecord>(RecordId.Hex(RecordTypes.Script, 0x0365));
        var rule = script.BattleRules!;
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 978), script.BattleFlag);
        Assert.True(rule is { Flee: false, NonLethal: true, QuietVictory: true });
        Assert.Equal(Ids.Chunk(6, "E03"), rule.End!.Place);
        Assert.NotNull(Set.TryGet<ChunkRecord>(rule.End.Place));
        foreach (var (chunk, index) in new[] { ("H08", 8), ("H08", 13), ("I08", 26) })
            Assert.Equal(rule, Assert.IsType<NpcObject>(Set.Get<ChunkRecord>(Ids.Chunk(0, chunk)).Object(index)).BattleRules);
        // Lugash's fight (flag 5250) may be fled and has no more rules, and the Marquis's (3749) the same.
        Assert.Equal(new BattleRules(Flee: true), Set.Get<ScriptRecord>(RecordId.Hex(RecordTypes.Script, 0x04D1)).BattleRules);
        Assert.Equal(new BattleRules(Flee: true), Assert.IsType<NpcObject>(Set.Get<ChunkRecord>(Ids.Chunk(0, "E23")).Object(2)).BattleRules);
        Assert.All(Set.All<ChunkRecord>().SelectMany(c => c.Objects).OfType<EncounterObject>(), e => Assert.Null(e.BattleRules));
        // Combat.Init heals the first member; Combatant.ShadowFightTrick holds Alaron to 1 a hit, and the second keels him over.
        Assert.True(rule.HealsLeader);
        Assert.Equal(new KeelOver(RecordId.Hex(RecordTypes.Entity, 0x023F), 2), rule.KeelOver);
    }

    [Fact]
    public void TheShadowsRulesNameItTheHornAlaronAndItsCinematic()
    {
        var shadow = Combat.Shadow!;
        Assert.Equal("Shadow", Set.Get<EntityRecord>(shadow.Entity).Name);
        Assert.Equal("Horn of Kynon", Set.Get<ItemRecord>(shadow.Item).Name);
        Assert.Equal(RecordId.Hex(RecordTypes.Entity, 0x023F), shadow.Merges);
        Assert.Equal((10f, 4, 0.5f, 30), (shadow.Radius, shadow.VanishRounds, shadow.MergeReach, shadow.Dexterity));
        Assert.Equal(RecordId.Decimal(RecordTypes.Flag, 569), Set.Get<CinematicRecord>(shadow.Cinematic).DoneFlag);
        Assert.Equal((5, 2, 10, RecordId.Decimal(RecordTypes.Flag, 566)), (shadow.Boost.Protection, shadow.Boost.Least, shadow.Boost.SpellCap, shadow.Boost.Flag));
        Assert.Equal([("4", 6), ("0", 3), ("1", 3), ("3", 3), ("5", 10)], shadow.Boost.Stats.Select(g => (g.Stat.Key, g.Amount)));
    }

    [Fact]
    public void TerrainsHaveTheCampEncountersFiveTiersAndTerrain27None()
    {
        static RecordId Entity(int id) => RecordId.Hex(RecordTypes.Entity, id);
        var plains = Set.Get<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, 0)).Ambushes!;
        Assert.Equal(5, plains.Count);
        Assert.Equal([new PartyEnemy(Entity(0x026E), 1, 3), new PartyEnemy(Entity(0x0215), 1, 1)], plains[0].Enemies);
        Assert.Equal([new PartyEnemy(Entity(0x0210), 2, 4), new PartyEnemy(Entity(0x0211), 2, 4)], plains[4].Enemies);
        Assert.All(Enumerable.Range(0, 27), t => Assert.Equal(5, Set.Get<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, t)).Ambushes!.Count));
        Assert.Empty(Set.Get<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, 27)).Ambushes);
        Assert.Equal([5, 10, 15, 20], Set.Singleton<CampingRecord>().AmbushLevels!);
    }

    /// <summary>
    /// The entities whose weapon isn't drawn in the hand (<c>Combatant.HasNoWeaponAttachment</c>, the list at <c>0x800D2A0C</c>, read here
    /// from a checkpoint's RAM), and those who hold a bow the other way round (<c>EntityBody.Hand</c>'s switch), as the importer
    /// carries them (importer 51).
    /// </summary>
    [Fact]
    public void TheWeaponsNotDrawnAndTheOtherHandsAreTheCodesEntities()
    {
        // They are flags on the entities' bodies now, so by ID rather than in the code's order.
        Assert.Equal(new[] { "Shamsuk", "Gorgon", "Chaos Lieutenant", "Harpy", "Marquis", "Minotaur", "Minotaur Lord", "Niesen" }.Order(StringComparer.Ordinal),
            Entities.Where(e => e.Body!.WeaponsInModel).Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "Bandit Boss", "Bandit Boss", "Bandit Boss", "Bandit Woodsman", "Bandit Woodsman", "Bandit Boss", "Bandit Boss",
            "Bandit Woodsman", "Bandit Boss", "Becan", "Keelin" }.Order(StringComparer.Ordinal),
            Entities.Where(e => e.Body!.BowHand == BowHand.Other).Select(e => e.Name).Order(StringComparer.Ordinal));

        var cp = CombatCheckpoints.Combat(1);
        Assert.Equal([.. CombatImporter.UndrawnWeaponPlaces, 0xFF],
            Enumerable.Range(0, CombatImporter.UndrawnWeaponPlaces.Count + 1).Select(i => (int)cp.ReadByte(0x800D2A0Cu + (uint)i)));
    }

    /// <summary>
    /// Who decides fleeing and who can't be targeted, read from 1.0's machine code: two rules that name one entity, the Shadow (the
    /// entity list's place <c>0xAC</c>), each in its own code, which the record keeps as two lists. <c>Combat.ForbidFleeForBosses</c>
    /// (<c>0x8005CCCC</c>) lets the party flee for the place it compares with at <c>0x8005CCE8</c> and forbids it for the places at
    /// <c>0x800D2688</c>, up to <c>0xFF</c>; <c>Combatant.CanAttack</c> (<c>0x80060590</c>) refuses a target whose entity is the entity
    /// list's byte at <c>0x800E28EC</c> plus <c>0x200</c> (<c>0x800605A8</c> and <c>0x800605C8</c>); and <c>Combat.CreateBody</c> sets
    /// <c>Combat.ShadowSlot</c>, whose caster or target <c>Combatant.CanCast</c> refuses, for the place it compares with at
    /// <c>0x8005C528</c>.
    /// </summary>
    [Fact]
    public void FleeingAndTheUntargetableAreTheCodesTwoRules()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        uint Word(uint address) => Be.U32(rom.Data, (int)(address - 0x80000400u + 0x1000));
        var list = rom.Table(CodeTable.EntityList).ToArray();
        RecordId At(int place) => RecordId.Hex(RecordTypes.Entity, 0x200 + list[place]);

        // addiu v0, zero, 0xAC; addiu v1, zero, 0xAC.
        Assert.Equal(0x240200ACu, Word(0x8005CCE8));
        Assert.Equal(0x240300ACu, Word(0x8005C528));
        // lui v1, 0x800E; lbu v0, 0x28EC(v1): the entity list (CodeTable.EntityList, 0x800E2840) at 0xAC.
        Assert.Equal(0x3C03800Eu, Word(0x800605A8));
        Assert.Equal(0x906228ECu, Word(0x800605C8));
        Assert.Equal(list[0xAC], rom.Data[(int)(0x800E28ECu - 0x80000400u + 0x1000)]);

        var noFlee = new List<int>();
        for (var a = 0x800D2688u; rom.Data[(int)(a - 0x80000400u + 0x1000)] is var b && b != 0xFF; a++)
            noFlee.Add(b);
        Assert.Equal(12, noFlee.Count);
        Assert.Equal(noFlee.Select(At).OrderBy(i => i.Key, StringComparer.Ordinal),
            Entities.Where(e => e.Combat?.Flee == FleeSay.Forbids).Select(e => e.Id).OrderBy(i => i.Key, StringComparer.Ordinal));

        Assert.Equal([At(0xAC)], Entities.Where(e => e.Combat?.Flee == FleeSay.Allows).Select(e => e.Id));
        Assert.Equal([At(0xAC)], Entities.Where(e => e.Combat?.Untargetable == true).Select(e => e.Id));
        Assert.Equal(Combat.Shadow!.Entity, At(0xAC));
        Assert.Equal("Shadow", Set.Get<EntityRecord>(At(0xAC)).Name);
    }

    /// <summary>The combat traits that one entity alone has, now carried by the entities themselves (ADR 0032).</summary>
    [Fact]
    public void SholehAloneFightsForThePartyTheShadowAloneIsUntargetableAndPochanargatAloneIsRooted()
    {
        Assert.Equal(["Sholeh"], Entities.Where(e => e.Combat?.FightsForParty == true).Select(e => e.Name));
        Assert.Equal(["Shadow"], Entities.Where(e => e.Combat?.Untargetable == true).Select(e => e.Name));
        Assert.Equal(["Shadow"], Entities.Where(e => e.Combat?.Flee == FleeSay.Allows).Select(e => e.Name));
        Assert.Equal(["Pochanargat"], Entities.Where(e => e.Combat?.Rooted == true).Select(e => e.Name));
        Assert.Equal(["Oriana"], Entities.Where(e => e.TrainsFree).Select(e => e.Name));
        Assert.Single(fixture.Data.Set.All<ItemRecord>(RecordTypes.Item), i => i.Weapon?.StrikesInMelee == true);
        Assert.Equal("Spikes", fixture.Data.Set.All<ItemRecord>(RecordTypes.Item).Single(i => i.Weapon?.StrikesInMelee == true).Name);
    }
}
