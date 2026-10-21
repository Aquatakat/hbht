using System.Text.Json;
using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Who joins and leaves the party and with what sheet (<c>Party::AddMember</c>, <c>removeAliveMemberByID</c>, <c>SaveEntity</c>),
/// in a new game over NA 1.0's real records: the flags, the inventory, the slots and the sheets kept for rejoining.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class PartyMemberTests(Na10Import fixture)
{
    private static readonly RecordId Abrecan = Entity(0x023E), Brenna = Entity(0x0243), Godric = Entity(0x0248), Keelin = Entity(0x0249),
        Niesen = Entity(0x024A), Rheda = Entity(0x024C), Sholeh = Entity(0x024D), Trahern = Entity(0x0239);

    /// <summary>Every recruitable member by name, as entities.json names them.</summary>
    private static readonly Dictionary<string, RecordId> Recruitable = new()
    {
        ["Farris"] = Entity(0x0299), ["Abrecan"] = Abrecan, ["Arturo"] = Entity(0x0240), ["Baird"] = Entity(0x0241), ["Becan"] = Entity(0x0242),
        ["Brenna"] = Brenna, ["Donovan"] = Entity(0x0246), ["Dougal"] = Entity(0x0247), ["Godric"] = Godric, ["Keelin"] = Keelin,
        ["Niesen"] = Niesen, ["Rheda"] = Rheda, ["Sholeh"] = Sholeh,
    };

    public static TheoryData<string> Names() => [.. Recruitable.Keys];

    private PluginSet Set => fixture.Data.Set;

    private static string Text(RecordId id) => id.ToString();

    private static bool IsSet(GameState game, int flag) => game.Flags.Get(Flag(flag));

    private static void SetFlag(GameState game, int flag, bool value) => game.Flags.Set(Flag(flag), value);

    private GameState NewGame() => GameState.NewGame(Set);

    /// <summary>A new game with these members joined after Alaron.</summary>
    private GameState PartyOf(params RecordId[] members)
    {
        var game = NewGame();
        foreach (var member in members)
            Assert.True(game.AddMember(member), $"{member} didn't join.");
        return game;
    }

    private static List<(RecordId Item, int Quantity)> Stacks(GameState game) =>
        [.. game.Party.Inventory.Stacks.Select(s => (s.Item.Id, s.Quantity))];

    private static Dictionary<RecordId, int> Count(IEnumerable<RecordId> items) =>
        items.GroupBy(i => i).ToDictionary(g => g.Key, g => g.Count());

    private static List<RecordId> Worn(Character member) => [.. member.Equipment.All.Select(i => i.Id)];

    private static void AssertSameSheet(SaveCharacter expected, SaveCharacter actual) =>
        Assert.Equal(JsonSerializer.Serialize(expected, PluginJson.Options), JsonSerializer.Serialize(actual, PluginJson.Options));

    /// <summary>Puts on the first ring that member can wear, and says which.</summary>
    private static RecordId WearARing(GameState game, Character member)
    {
        foreach (var ring in game.Items.All.Where(i => i.Kind == ItemKind.Ring && i.KeyItem is null).OrderBy(i => i.Id.Key, StringComparer.Ordinal))
        {
            game.Party.Inventory.Add(ring.Id);
            if (game.Party.Equip(member, ring.Id) == EquipRefusal.None)
                return ring.Id;
            game.Party.Inventory.Take(ring.Id);
        }
        throw new InvalidOperationException($"No ring {member.Name} can wear.");
    }

    // Joining.

    [Fact]
    public void AMemberJoinsTheNextSlotWithTheirJoinedFlagSetAndTheirLeftFlagClear()
    {
        var game = NewGame();
        SetFlag(game, 686, true);
        Assert.False(IsSet(game, 90));

        Assert.True(game.AddMember(Godric));

        Assert.Equal([GameState.Alaron, Godric, null, null], game.Party.Slots.Select(m => m?.Entity));
        Assert.True(IsSet(game, 90));
        Assert.False(IsSet(game, 686));
        // Their died flag is the battle's.
        Assert.False(IsSet(game, 616));
        Assert.Empty(game.Party.Kept);
    }

    [Theory]
    [InlineData("Godric", false)]
    [InlineData("Keelin", true)]
    [InlineData("Sholeh", true)]
    [InlineData("Rheda", true)]
    public void AMemberBringsTheWeaponsTheyDontWieldToTheInventory(string name, bool hasSpares)
    {
        var id = Recruitable[name];
        var game = NewGame();
        var spares = game.Recruit(id)!.Spares.Select(s => s.Id).ToList();
        Assert.Equal(hasSpares, spares.Count > 0);
        var before = Count(game.Party.Inventory.Stacks.Select(s => s.Item.Id));

        Assert.True(game.AddMember(id));

        var after = game.Party.Inventory.Stacks.ToDictionary(s => s.Item.Id, s => s.Quantity);
        foreach (var (item, n) in Count(spares))
            Assert.Equal(before.GetValueOrDefault(item) + n, after.GetValueOrDefault(item));
        // What they wield stays in their hands, and no spare is also worn.
        Assert.NotNull(game.Party.Get(id)!.Equipment.Weapon);
        Assert.Empty(game.Party.Get(id)!.Spares);
    }

    [Fact]
    public void AFullPartyTakesNoOneAndChangesNothing()
    {
        var game = PartyOf(Rheda, Brenna, Sholeh);
        SetFlag(game, 686, true);
        var members = game.Party.Slots.Select(m => m?.Entity).ToList();
        var stacks = Stacks(game);
        var flags = game.Save().Flags;

        Assert.False(game.AddMember(Godric));

        Assert.Equal(4, game.Party.Count);
        Assert.Equal(members, game.Party.Slots.Select(m => m?.Entity));
        Assert.Equal(stacks, Stacks(game));
        Assert.Equal(flags, game.Save().Flags);
        Assert.False(IsSet(game, 90));
        Assert.True(IsSet(game, 686));
    }

    [Fact]
    public void SomeoneAlreadyInThePartyIsntAddedAgainAndTheirJoinedFlagStaysSet()
    {
        var game = PartyOf(Godric);
        var godric = game.Party.Get(Godric);
        var stacks = Stacks(game);

        Assert.True(game.AddMember(Godric));

        Assert.Equal(2, game.Party.Count);
        Assert.Same(godric, game.Party.Get(Godric));
        Assert.True(IsSet(game, 90));
        Assert.False(IsSet(game, 686));
        Assert.Equal(stacks, Stacks(game));
    }

    [Fact]
    public void WithAFullPartyAMemberAlreadyInItIsRefusedTooForTheFullCheckComesFirst()
    {
        var game = PartyOf(Godric, Rheda, Brenna);

        Assert.False(game.AddMember(Godric));

        Assert.Equal(4, game.Party.Count);
        Assert.True(IsSet(game, 90));
    }

    [Fact]
    public void ACharacterWithNoMemberFlagsJoinsWithoutAnyAndNoPluginsRecordMeansNoMember()
    {
        var game = NewGame();
        Assert.Null(Set.Get<EntityRecord>(Trahern).Member);
        var flags = game.Save().Flags;

        Assert.True(game.AddMember(Trahern));

        Assert.True(game.Party.Contains(Trahern));
        Assert.Equal(flags, game.Save().Flags);

        // An entity no plugin has: no one joins.
        Assert.False(game.AddMember(Entity(0x0FFF)));
        Assert.Equal(2, game.Party.Count);
    }

    // Leaving.

    [Theory]
    [MemberData(nameof(Names))]
    public void EachMemberJoinsAndLeavesWithTheFlagsOfTheirRecordAndKeepsASheetIfItSaysSo(string name)
    {
        var id = Recruitable[name];
        var record = Set.Get<EntityRecord>(id).Member!;
        var game = NewGame();
        var left = record.Left is { } l ? int.Parse(l.Key) : (int?)null;
        if (left is { } leftFlag)
            SetFlag(game, leftFlag, true);

        Assert.True(game.AddMember(id));
        Assert.True(IsSet(game, int.Parse(record.Joined.Key)));
        if (left is { } cleared)
            Assert.False(IsSet(game, cleared));

        game.RemoveMember(id);

        Assert.False(game.Party.Contains(id));
        Assert.False(IsSet(game, int.Parse(record.Joined.Key)));
        if (left is { } set)
            Assert.True(IsSet(game, set));
        Assert.False(IsSet(game, int.Parse(record.Died.Key)));
        Assert.Equal(record.Kept, game.Party.Kept.ContainsKey(id));
        Assert.Equal(record.Kept ? [id] : [], game.Party.Kept.Keys);
    }

    [Fact]
    public void LeavingPutsWhatIsWornInTheInventoryRemovesTheMemberAndPacksTheSlots()
    {
        var game = PartyOf(Abrecan, Rheda, Brenna);
        var abrecan = game.Party.Get(Abrecan)!;
        WearARing(game, abrecan);
        Assert.NotNull(abrecan.Equipment.Weapon);
        Assert.NotNull(abrecan.Equipment.Armor);
        Assert.NotNull(abrecan.Equipment.Shield);
        var worn = Worn(abrecan);
        Assert.Equal(4, worn.Count);
        var before = game.Party.Inventory.Stacks.ToDictionary(s => s.Item.Id, s => s.Quantity);

        game.RemoveMember(Abrecan);

        var after = game.Party.Inventory.Stacks.ToDictionary(s => s.Item.Id, s => s.Quantity);
        foreach (var (item, n) in Count(worn))
            Assert.Equal(before.GetValueOrDefault(item) + n, after.GetValueOrDefault(item));
        Assert.Empty(Worn(abrecan));
        Assert.Equal([GameState.Alaron, Rheda, Brenna, null], game.Party.Slots.Select(m => m?.Entity));
        Assert.False(IsSet(game, 89));
        Assert.True(IsSet(game, 685));
    }

    [Fact]
    public void BrennaHasNoLeftFlagSoLeavingOnlyClearsHerJoinedFlag()
    {
        var game = PartyOf(Brenna);
        var flags = game.Save().Flags;
        Assert.Contains(Flag(92), flags);

        game.RemoveMember(Brenna);

        Assert.False(game.Party.Contains(Brenna));
        Assert.Equal(flags.Where(f => f != Flag(92)), game.Save().Flags);
        Assert.Empty(game.Party.Kept);
    }

    [Fact]
    public void AKeptMembersSheetIsKeptWithTheirExperienceAndTrainingAndNothingWornOrWorkingOnThem()
    {
        var game = PartyOf(Godric, Rheda);
        var godric = game.Party.Get(Godric)!;
        godric.AddExp(3000);
        godric.Stats.Train(Stat.Strength, 2);
        godric.Skills.Train(Skill.Alchemist, 1);
        godric.ApplyPotionEffect(PotionId(BasePotion.Strength), 0, 5000);
        var before = GameState.SaveCharacter(godric);
        Assert.NotNull(before.Weapon);
        Assert.NotNull(before.Armor);
        Assert.NotEmpty(before.Potions);
        Assert.True(before.TotalExp > 0);

        game.RemoveMember(Godric);

        var kept = Assert.Single(game.Party.Kept).Value;
        Assert.Equal(Godric, kept.Entity);
        Assert.Equal(before.TotalExp, kept.TotalExp);
        Assert.Equal(before.SpendableExp, kept.SpendableExp);
        Assert.Equal(before.Trained, kept.Trained);
        Assert.Equal(before.Skills, kept.Skills);
        Assert.Equal(before.Spells, kept.Spells);
        Assert.Null(kept.Weapon);
        Assert.Null(kept.Armor);
        Assert.Null(kept.Shield);
        Assert.Empty(kept.Gear);
        Assert.Empty(kept.Potions);
        AssertSameSheet(before with { Weapon = null, Armor = null, Shield = null, Gear = [], Potions = [] }, kept);
        // What they wore is in the inventory.
        Assert.True(game.Party.Inventory.Has(before.Weapon!.Item));
        Assert.True(game.Party.Inventory.Has(before.Armor!.Item));
    }

    [Fact]
    public void AMemberWhoIsntKeptLeavesNoSheet()
    {
        var game = PartyOf(Rheda);
        game.Party.Get(Rheda)!.AddExp(3000);

        game.RemoveMember(Rheda);

        Assert.Empty(game.Party.Kept);
        Assert.False(game.Party.Contains(Rheda));
    }

    [Fact]
    public void RemovingSomeoneWhoIsntInThePartyDoesNothing()
    {
        var game = PartyOf(Rheda);
        SetFlag(game, 686, false);
        SetFlag(game, 90, false);
        var stacks = Stacks(game);
        var flags = game.Save().Flags;
        var members = game.Party.Slots.Select(m => m?.Entity).ToList();

        game.RemoveMember(Godric);
        game.RemoveMember(Entity(0x0FFF));

        Assert.Equal(flags, game.Save().Flags);
        Assert.False(IsSet(game, 686));
        Assert.Equal(stacks, Stacks(game));
        Assert.Equal(members, game.Party.Slots.Select(m => m?.Entity));
        Assert.Empty(game.Party.Kept);
    }

    [Fact]
    public void WhatTheInventoryHasNoRoomForStaysOnTheSheetThatIsKept()
    {
        var game = PartyOf(Godric);
        var godric = game.Party.Get(Godric)!;
        var worn = Worn(godric);
        Assert.Equal(2, worn.Count);
        var slots = game.Items.Rules.GeneralSlots;
        foreach (var item in game.Items.All.Where(i => !i.Traits.OwnSlot && i.KeyItem is null && !worn.Contains(i.Id)))
        {
            if (game.Party.Inventory.GeneralSlotsUsed >= slots)
                break;
            game.Party.Inventory.Add(item.Id);
        }
        Assert.Equal(slots, game.Party.Inventory.GeneralSlotsUsed);
        Assert.All(worn, id => Assert.False(game.Party.Inventory.Has(id)));

        game.RemoveMember(Godric);

        Assert.False(game.Party.Contains(Godric));
        Assert.All(worn, id => Assert.False(game.Party.Inventory.Has(id)));
        var kept = game.Party.Kept[Godric];
        Assert.Equal(worn.OrderBy(Text), new[] { kept.Weapon?.Item, kept.Armor?.Item }.OfType<RecordId>().OrderBy(Text));
        Assert.True(IsSet(game, 686));
        Assert.False(IsSet(game, 90));
    }

    [Fact]
    public void TheArchmageStaffStaysWithNiesenAndTheRestOfWhatHeWearsGoesToTheInventory()
    {
        var staff = RecordId.Hex(RecordTypes.Weapon, 0x0735);
        Assert.True(Set.Get<WeaponRecord>(staff).Fixed);
        var game = PartyOf(Niesen);
        var niesen = game.Party.Get(Niesen)!;
        Assert.Equal(staff, niesen.Equipment.Weapon?.Id);
        var armor = niesen.Equipment.Armor!.Id;
        var armors = game.Party.Inventory.Count(armor);

        game.RemoveMember(Niesen);

        Assert.False(game.Party.Contains(Niesen));
        Assert.Empty(game.Party.Kept);
        Assert.False(game.Party.Inventory.Has(staff));
        Assert.Equal(staff, niesen.Equipment.Weapon?.Id);
        Assert.Null(niesen.Equipment.Armor);
        Assert.Equal(armors + 1, game.Party.Inventory.Count(armor));
        Assert.False(IsSet(game, 412));
        Assert.True(IsSet(game, 692));
    }

    // Rejoining.

    [Theory]
    [InlineData("Godric")]
    [InlineData("Keelin")]
    public void AKeptMemberComesBackAsTheyWereWithNothingWornAndTheirSpareWeaponsBackInTheInventory(string name)
    {
        var id = Recruitable[name];
        var game = PartyOf(id);
        var member = game.Party.Get(id)!;
        member.AddExp(3000);
        member.Stats.Train(Stat.Strength, 2);
        member.Skills.Train(Skill.Alchemist, 1);
        member.TakeDamage(3);
        WearARing(game, member);
        var before = GameState.SaveCharacter(member);
        game.RemoveMember(id);
        var kept = game.Party.Kept[id];
        var spares = game.Recruit(id)!.Spares.Select(s => s.Id).ToList();
        var inventory = game.Party.Inventory.Stacks.ToDictionary(s => s.Item.Id, s => s.Quantity);

        Assert.True(game.AddMember(id));

        var back = game.Party.Get(id)!;
        Assert.NotSame(member, back);
        Assert.Empty(game.Party.Kept);
        var flags = Set.Get<EntityRecord>(id).Member!;
        Assert.True(game.Flags.Get(flags.Joined));
        Assert.False(game.Flags.Get(flags.Left!.Value));
        // The kept experience, stats, skills and spells, and the damage taken, with nothing worn but what the kept sheet wore.
        Assert.Equal(before.TotalExp, back.TotalExp);
        Assert.Equal(before.Trained, GameState.SaveCharacter(back).Trained);
        Assert.Equal(before.Current, GameState.SaveCharacter(back).Current);
        AssertSameSheet(kept, GameState.SaveCharacter(back));
        Assert.Empty(Worn(back));
        // Entity::Init's spare weapons go to the inventory again.
        var after = game.Party.Inventory.Stacks.ToDictionary(s => s.Item.Id, s => s.Quantity);
        foreach (var (item, n) in Count(spares))
            Assert.Equal(inventory.GetValueOrDefault(item) + n, after.GetValueOrDefault(item));
        if (name == "Keelin")
            Assert.NotEmpty(spares);
    }

    [Fact]
    public void AMemberWhoIsntKeptComesBackNewAsTheirRecordStartsThem()
    {
        var game = PartyOf(Rheda);
        var rheda = game.Party.Get(Rheda)!;
        rheda.AddExp(3000);
        rheda.Stats.Train(Stat.Strength, 2);
        rheda.Skills.Train(Skill.Wizard, 1);
        rheda.TakeDamage(3);
        game.RemoveMember(Rheda);

        Assert.True(game.AddMember(Rheda));

        var back = game.Party.Get(Rheda)!;
        Assert.Empty(game.Party.Kept);
        Assert.NotEqual(rheda.TotalExp, back.TotalExp);
        AssertSameSheet(GameState.SaveCharacter(game.Recruit(Rheda)!), GameState.SaveCharacter(back));
        Assert.NotNull(back.Equipment.Weapon);
        Assert.NotNull(back.Equipment.Armor);
    }

    // Saves.

    [Fact]
    public void AKeptSheetSurvivesASaveAndALoad()
    {
        var game = PartyOf(Godric, Keelin);
        game.Party.Get(Godric)!.AddExp(3000);
        game.Party.Get(Keelin)!.Stats.Train(Stat.Dexterity, 2);
        game.RemoveMember(Godric);
        game.RemoveMember(Keelin);
        Assert.Equal(2, game.Party.Kept.Count);

        var save = game.Save();
        Assert.Equal([Godric, Keelin], save.Party.Kept.Select(k => k.Entity).OrderBy(Text));
        Assert.All(save.Party.Members, m => Assert.DoesNotContain(m.Entity, save.Party.Kept.Select(k => k.Entity)));

        var json = SaveFile.Serialize(save);
        var loaded = GameState.Load(Set, SaveFile.Parse(json));

        Assert.Empty(loaded.Problems);
        Assert.Equal(game.Party.Kept.Keys.OrderBy(Text), loaded.Party.Kept.Keys.OrderBy(Text));
        foreach (var (entity, sheet) in game.Party.Kept)
            AssertSameSheet(sheet, loaded.Party.Kept[entity]);
        Assert.Equal(json, SaveFile.Serialize(loaded.Save()));

        // Rejoining after the load brings Godric back with the experience he left with.
        Assert.True(loaded.AddMember(Godric));
        Assert.Equal(game.Party.Kept[Godric].TotalExp, loaded.Party.Get(Godric)!.TotalExp);
        Assert.DoesNotContain(Godric, loaded.Party.Kept.Keys);
    }

    [Fact]
    public void ASaveWhoseKeptSheetNamesAPartyMemberOrAnEntityNoPluginHasListsProblemsAndKeepsNeither()
    {
        var game = PartyOf(Godric);
        game.RemoveMember(Godric);
        var godric = game.Party.Kept[Godric];
        var save = game.Save();
        var bad = save with
        {
            Party = save.Party with
            {
                Kept = [godric with { Entity = GameState.Alaron }, godric with { Entity = Entity(0x0FFF) }],
            },
        };

        var loaded = GameState.Load(Set, bad);

        Assert.Empty(loaded.Party.Kept);
        Assert.Equal(2, loaded.Problems.Count);
        Assert.Contains(loaded.Problems, p => p.StartsWith(GameState.Alaron.ToString(), StringComparison.Ordinal) && p.Contains("in the party"));
        Assert.Contains(loaded.Problems, p => p.StartsWith(Entity(0x0FFF).ToString(), StringComparison.Ordinal) && p.Contains("isn't a character any plugin has"));
    }

    [Fact]
    public void LoadingThePartyAgainReplacesTheSheetsItKept()
    {
        var game = PartyOf(Godric);
        var withoutKept = game.SaveParty();
        game.RemoveMember(Godric);
        Assert.Single(game.Party.Kept);

        game.LoadParty(withoutKept);

        Assert.Empty(game.Party.Kept);
        Assert.True(game.Party.Contains(Godric));
        Assert.Empty(game.Problems);
    }
}
