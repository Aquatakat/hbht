using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using GameDb = Aidyn.Rom.Db.GameDb;
using Aidyn.Rom.Import;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The combat record's weapons that fly (importer 42), and its boss flags, the Goblin Ambush's rules and the game over, the scripts'
/// endings and the camp ambushes (importer 43), checked against the ROM and 1.0's machine code.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CombatImportTests(Na10Import fixture)
{
    private static RecordId Weapon(int id) => RecordId.Hex(RecordTypes.Weapon, id);

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

    [Fact]
    public void SixteenWeaponsFly()
    {
        var missiles = Combat.Missiles!;
        Assert.Equal(16, missiles.Count);
        Assert.Equal(16, missiles.Select(m => m.Weapon).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(Flying))]
    public void AWeaponThatFliesIsAnArrowOrItsOwnModel(int id, string name, bool bow, float speed, float turn)
    {
        var missile = Assert.Single(Combat.Missiles!, m => m.Weapon == Weapon(id));
        Assert.Equal(name, fixture.Data.Set.Get<WeaponRecord>(missile.Weapon).Name);
        Assert.Equal(bow ? Arrow : null, missile.Scene);
        Assert.Equal(speed, missile.Speed);
        Assert.Equal(turn, missile.Turn);
        // What a thrown one flies as, its own model, is on its record.
        if (!bow)
            Assert.NotNull(fixture.Data.Set.Get<WeaponRecord>(missile.Weapon).Model);
    }

    [Fact]
    public void BowsAreTheBowsOfTheMissileClassAndThrownWeaponsTheThrownOnes()
    {
        var set = fixture.Data.Set;
        Assert.All(Combat.Missiles!.Where(m => m.Scene is not null), m => Assert.Equal(WeaponClass.Missile, set.Get<WeaponRecord>(m.Weapon).Class));
        Assert.All(Combat.Missiles!.Where(m => m.Scene is null), m => Assert.Equal(WeaponClass.Thrown, set.Get<WeaponRecord>(m.Weapon).Class));
        Assert.Equal(8, Combat.Missiles!.Count(m => m.Scene is not null));
    }

    [Fact]
    public void OnlySpikesMayStrikeInMelee()
    {
        var spikes = Assert.Single(Combat.CloseRange!);
        Assert.Equal(Weapon(0x071B), spikes);
        Assert.Equal("Spikes", fixture.Data.Set.Get<WeaponRecord>(spikes).Name);
        // It's a thrown weapon that doesn't fly: it isn't in either list.
        Assert.DoesNotContain(Combat.Missiles!, m => m.Weapon == spikes);
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
        var names = Combat.Text!.Aura.Elements;
        Assert.Equal(Enum.GetValues<Element>(), names.Keys.OrderBy(e => e));
        Assert.Equal(
            ["None", "Earth", "Solar", "Physical", "Necromancy", "Fire", "Lunar", "Naming", "Water", "Magic", "Air", "Star", "Elemental",
             "Chaos", "Cutting", "Smashing", "Holy"],
            Enum.GetValues<Element>().Select(e => names[e]));
        // The names read as the enum's own, so the order is the code's: the element byte indexes the pointer.
        Assert.All(Enum.GetValues<Element>(), e => Assert.Equal(e.ToString(), names[e]));
    }

    /// <summary>The menus' strings sit elsewhere in each version but read the same, as do the element names' Common lines.</summary>
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void TheTextIsTheSameInEveryVersion(RomVersionId id)
    {
        var rom = TestRoms.Get(id);
        var text = CombatImporter.ReadText(rom, GameDb.Read(rom));
        var na10 = Combat.Text!;
        Assert.Equal(na10.LayOnHands, text.LayOnHands);
        Assert.Equal(na10.HealWithHerbs, text.HealWithHerbs);
        Assert.Equal(na10.Perform, text.Perform);
        Assert.Equal(na10.NoSpells, text.NoSpells);
        Assert.Equal(na10.NoItems, text.NoItems);
        Assert.Equal(na10.Aura with { Elements = null! }, text.Aura with { Elements = null! });
        Assert.Equal(na10.Aura.Elements, text.Aura.Elements);
    }

    private PluginSet Set => fixture.Data.Set;

    [Fact]
    public void TheBossFlagsAreSetKillFlagsCasesByEntity()
    {
        // The jump table's cases by entity list place, read as the entities the old fact table named by ID.
        Assert.Equal(Quests.EngineFlags.BossFlags.OrderBy(b => b.Value).Select(b => (Import.DbRecordIds.For(b.Key), RecordId.Decimal(RecordTypes.Flag, b.Value))),
            Combat.Bosses!.OrderBy(b => int.Parse(b.Flag.Key)).Select(b => ((RecordId?)b.Entity, b.Flag)));
    }

    [Fact]
    public void TheGameOverStartsFromItsDialogueObjectInTheDreamscape()
    {
        var gameOver = Combat.GameOver!;
        Assert.Equal(RecordId.Hex(RecordTypes.Dialogue, 0x0512), gameOver.Script);
        var map = Set.Get<MapRecord>(gameOver.Map);
        Assert.NotNull(map.Objects.OfType<DialogueObject>().Single(d => d.Script == gameOver.Script).Stage);
        Assert.Equal(new[] { 0x0512, 0x0513, 0x0514 }.Select(n => RecordId.Hex(RecordTypes.Dialogue, n)), Set.All<DialogueRecord>(RecordTypes.Dialogue)
            .Where(d => d.Ends == DialogueEnding.GameOver).Select(d => d.Id));
    }

    [Fact]
    public void TheGoblinAmbushIsNonLethalAndEndsInOrianasHut()
    {
        var rule = Combat.Encounter(RecordId.Decimal(RecordTypes.Flag, 978))!;
        Assert.True(rule is { NonLethal: true, QuietVictory: true });
        Assert.Equal(new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, 6), 5, 3), rule.End!.Place);
        Assert.NotNull(Set.Get<MapSetRecord>(rule.End.Place.MapSet).Chunks.Single(c => c.Row == 5 && c.Column == 3));
        Assert.Null(Combat.Encounter(RecordId.Decimal(RecordTypes.Flag, 5250)));
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
        var plains = TerrainRecord.Find(Set, 0)!.Ambushes!;
        Assert.Equal(5, plains.Count);
        Assert.Equal([new PartyEnemy(Entity(0x026E), 1, 3), new PartyEnemy(Entity(0x0215), 1, 1)], plains[0].Enemies);
        Assert.Equal([new PartyEnemy(Entity(0x0210), 2, 4), new PartyEnemy(Entity(0x0211), 2, 4)], plains[4].Enemies);
        Assert.All(Enumerable.Range(0, 27), t => Assert.Equal(5, TerrainRecord.Find(Set, t)!.Ambushes!.Count));
        Assert.Null(TerrainRecord.Find(Set, 27)!.Ambushes);
        Assert.Equal([5, 10, 15, 20], Set.Singleton<CampingRecord>(RecordTypes.Camping).AmbushLevels!);
    }
}
