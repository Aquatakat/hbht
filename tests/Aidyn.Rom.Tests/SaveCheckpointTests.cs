using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Checks what the rig reads of a checkpoint's place, clock and map objects' memory, and that the
/// save made from a checkpoint loads into a game that matches the console's: the party, the flags,
/// and a round trip through the save format. The tests skip without the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class SaveCheckpointTests(MapObjectFixture fixture)
{
    private PluginSet Set => fixture.Data.Set;

    public static TheoryData<string> Checkpoints() => CheckpointTests.Checkpoints();

    private static AresCheckpoint Load(string name)
    {
        if (name.Length == 0)
            Assert.Skip($"No checkpoints in {TestRoms.Folder}.");
        return AresCheckpoint.Read(Path.Combine(TestRoms.Folder, name));
    }

    private static bool IsDebug(string name) => name.StartsWith("AidynDebug", StringComparison.Ordinal);

    private static string LabelOf(string name) => CheckpointSave.Label(name);

    private (SaveGame Save, List<string> Problems) Convert(string name, AresCheckpoint checkpoint)
    {
        var problems = new List<string>();
        return (CheckpointSave.Convert(checkpoint, Set, LabelOf(name), problems), problems);
    }

    private GameState Game(string name, AresCheckpoint checkpoint)
    {
        var (save, problems) = Convert(name, checkpoint);
        Assert.Empty(problems);
        var game = GameState.Load(Set, save);
        Assert.Empty(game.Problems);
        return game;
    }

    // What the 1.0 states hold, measured with a RAM dump; the debug states differ only in the clocks.
    private sealed record Expected(
        int MapSet, int Row, int Column, Vector3 Position, Vector2? Facing, uint Time, uint PlayTime, uint DebugTime, uint DebugPlayTime);

    private static readonly Dictionary<string, Expected> Places = new()
    {
        ["Before final encounter"] = new(0, 13, 6, new(69.06516f, -19.99730f, 72.22858f), new(0.0208002f, 0.9997836f),
            354261576, 3905657, 354215280, 3912247),
        ["Before Rooughah"] = new(10, 2, 1, new(90.38694f, -1.6294456f, 31.970375f), new(0.9254885f, 0.3787757f),
            348605112, 3738265, 348465648, 3736835),
        ["Before Shamsuk's Tower"] = new(0, 16, 17, new(65.41911f, -26.731735f, 68.34049f), null,
            287209944, 1676013, 287202456, 1676577),
        ["Before Rooughah, in Terminor"] = new(8, 7, 1, new(50.999657f, 0.507f, 64.60998f), null,
            358130688, 4050671, 358136448, 4048355),
    };

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ThePlaceAndTheClocksAreWhereAndWhenTheyWerePlayed(string name)
    {
        var checkpoint = Load(name);
        var expected = Places[LabelOf(name)];

        var place = checkpoint.Place;
        Assert.Equal((expected.MapSet, expected.Row, expected.Column), (place.MapSet, place.Row, place.Column));
        Assert.Equal(expected.Position.X, place.Position.X, 4);
        Assert.Equal(expected.Position.Y, place.Position.Y, 4);
        Assert.Equal(expected.Position.Z, place.Position.Z, 4);
        if (expected.Facing is { } facing)
        {
            Assert.Equal(facing.X, place.Facing.X, 4);
            Assert.Equal(facing.Y, place.Facing.Y, 4);
        }
        // A facing is a direction.
        Assert.InRange(place.Facing.Length(), 0.99f, 1.01f);

        Assert.Equal(IsDebug(name) ? expected.DebugTime : expected.Time, checkpoint.Time);
        Assert.Equal(IsDebug(name) ? expected.DebugPlayTime : expected.PlayTime, checkpoint.PlayTime);
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void TheChartHoldsTheMemoriesOfTheTalksAndOpeningsBeforeTheCheckpoint(string name)
    {
        var checkpoint = Load(name);
        IReadOnlyList<CheckpointMemory> expected = LabelOf(name) switch
        {
            "Before final encounter" =>
            [
                new(2, 7, 0, 12, 8, 9, BitConverter.SingleToUInt32Bits(0.99f)),
                new(2, 7, 0, 12, 8, 74, 2),
            ],
            "Before Rooughah, in Terminor" => [new(32, 10, 8, 7, 1, 9, BitConverter.SingleToUInt32Bits(1.0f))],
            _ => [],
        };
        Assert.Equal(expected, checkpoint.Memories);
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ACheckpointConvertsWithoutProblemsAndItsMemoriesNameMonsterPartiesWithDialogues(string name)
    {
        var checkpoint = Load(name);
        var (save, problems) = Convert(name, checkpoint);
        Assert.Empty(problems);

        Assert.Equal(LabelOf(name), save.Label);
        Assert.Equal(checkpoint.Time, save.Time);
        Assert.Equal(checkpoint.PlayTime, save.PlayTime);
        Assert.Equal(Set.Plugins.Select(p => (p.Manifest.Id, p.Manifest.Version)), save.Plugins.Select(p => (p.Id, p.Version)));
        Assert.Null(save.Random);

        // The place: the chunk by name, and the heading the way the player faces, which is the opposite of the console's.
        var place = save.Place!;
        var chunk = Set.Get<MapSetRecord>(place.MapSet).Chunks.Single(c => c.Row == checkpoint.Place.Row && c.Column == checkpoint.Place.Column);
        Assert.Equal(RecordId.Decimal(RecordTypes.MapSet, checkpoint.Place.MapSet), place.MapSet);
        Assert.Equal(chunk.Name, place.Chunk);
        Assert.Equal(checkpoint.Place.Position, place.Position);
        Assert.Equal(-checkpoint.Place.Facing, place.Heading);

        // The plain flags set, 2 to 5000.
        Assert.Equal(
            Enumerable.Range(2, 4999).Where(n => checkpoint.Flags[n]).Select(n => RecordId.Decimal(RecordTypes.Flag, n)),
            save.Flags);

        var memories = save.Memories;
        Assert.Equal(checkpoint.Memories.Count, memories.Count);
        // What remembers a random line or a skill check here is a monster party's dialogue, not a dialogue object.
        foreach (var memory in memories)
            Assert.NotNull(Assert.IsType<MonsterPartyObject>(fixture.Data.Objects[memory.MapObject]).Dialogue);

        switch (LabelOf(name))
        {
            case "Before final encounter":
                var l08 = RecordId.Parse("aidyn:voxel/0x1063/2");
                Assert.Equal(l08, MapRecord.VoxelId(Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 0)).Chunks.Single(c => c.Name == "L08").Map, 2));
                Assert.Equal(
                    [
                        new SaveMemory(l08, "RandomLine", 99, 7),
                        new SaveMemory(l08, "SkillCheck", 2, 7) { Skill = 1 },
                    ],
                    memories);
                break;
            case "Before Rooughah, in Terminor":
                var map = Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, 8)).Chunks.Single(c => c.Name == "G01").Map;
                Assert.Equal(RecordId.Parse("aidyn:voxel/0x13D4/32"), MapRecord.VoxelId(map, 32));
                Assert.Equal([new SaveMemory(MapRecord.VoxelId(map, 32), "RandomLine", 100, 10)], memories);
                break;
            default:
                Assert.Empty(memories);
                break;
        }
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ALoadedCheckpointHasTheConsolesPartyGoldAndInventory(string name)
    {
        var checkpoint = Load(name);
        var game = Game(name, checkpoint);
        var members = game.Party.Members.ToList();

        Assert.Equal(checkpoint.Party.Count, members.Count);
        Assert.Equal(checkpoint.Gold, game.Party.Gold);
        var wrong = new List<string>();
        for (var slot = 0; slot < members.Count; slot++)
        {
            var sheet = checkpoint.Party[slot];
            var member = members[slot];
            var who = $"{member.Name} ({name})";
            Assert.Equal(RecordId.Hex(RecordTypes.Entity, sheet.Id), member.Entity);

            for (var i = 0; i < CharacterRules.StatCount; i++)
            {
                var stat = (Stat)i;
                Compare($"{who} {stat} modified", sheet.Modified[i], member.Stats.Modified(stat));
                Compare($"{who} {stat} base", sheet.Base[i], member.Stats.Base(stat));
                Compare($"{who} {stat} trained", sheet.Trained[i], member.Stats.Trained(stat));
                Compare($"{who} {stat} current", sheet.Current[i], member.Stats.Current(stat));
            }
            foreach (var skill in SkillId.All)
            {
                Compare($"{who} skill {skill.Slot} modified", sheet.SkillModified[skill.Slot], member.Skills.RawModified(skill));
                Compare($"{who} skill {skill.Slot} base", sheet.SkillBase[skill.Slot], member.Skills.RawBase(skill));
            }
            Compare($"{who} protection", sheet.Protection, member.Protection);
            Compare($"{who} damage", sheet.Damage, member.Damage);
            Compare($"{who} experience", (int)sheet.TotalExp, (int)member.TotalExp);
            Compare($"{who} spendable experience", (int)sheet.SpendableExp, (int)member.SpendableExp);

            Assert.InRange(member.Hp, 1, member.MaxHp);
            Assert.Equal(sheet.Base[(int)Stat.Endurance] + sheet.Base[(int)Stat.Stamina] + sheet.Base[(int)Stat.Level], member.MaxHp);

            Assert.Equal(Worn(sheet.Weapon), member.Equipment.Weapon?.Id);
            Assert.Equal(Worn(sheet.Armor), member.Equipment.Armor?.Id);
            Assert.Equal(Worn(sheet.Shield), member.Equipment.Shield?.Id);
            Assert.Equal(sheet.Gear.OfType<CheckpointItem>().Select(g => ItemId(g.Id)).Order(),
                member.Equipment.Gear.OfType<Aidyn.Core.Items.ItemInstance>().Select(g => g.Id).Order());
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} differ: {string.Join("; ", wrong)}");

        Assert.Equal(checkpoint.Inventory.Select(s => (ItemId(s.Item.Id), s.Quantity)),
            game.Party.Inventory!.Stacks.Select(s => (s.Item.Id, s.Quantity)));

        void Compare(string what, int console, int port)
        {
            if (console != port)
                wrong.Add($"{what}: console {console}, port {port}");
        }
    }

    private static RecordId? Worn(CheckpointItem? item) => item is null ? null : ItemId(item.Id);

    private static RecordId ItemId(ushort id) => (id >> 8) switch
    {
        7 => RecordId.Hex(RecordTypes.Weapon, id),
        5 or 6 => RecordId.Hex(RecordTypes.Armor, id),
        _ => RecordId.Hex(RecordTypes.Item, id),
    };

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ASpellbookLoadsInTheOrderTheConsoleHoldsIt(string name)
    {
        var checkpoint = Load(name);
        var game = Game(name, checkpoint);

        var members = game.Party.Members.ToList();
        for (var slot = 0; slot < members.Count; slot++)
        {
            Assert.Equal(
                checkpoint.Party[slot].Spells.Select(s => (RecordId.Hex(RecordTypes.Spell, s.Spell), s.Level)),
                members[slot].Spells.Select(s => (s.Spell.Id, s.Level)));
        }
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ALoadedCheckpointHasTheConsolesFlagsAndANewestJournalEntryButNoNotice(string name)
    {
        const int newJournalEntry = 3617;
        var checkpoint = Load(name);
        var network = Game(name, checkpoint).Flags;

        Assert.Equal(checkpoint.Flags.Count, network.Count);
        var differ = Enumerable.Range(0, network.Count)
            .Where(h => h != newJournalEntry && network.Peek(h) != checkpoint.Flags[h])
            .Select(h => $"{h} ({(network.IsDerived(h) ? "derived" : "plain")}, port {network.Peek(h)}, console {checkpoint.Flags[h]})")
            .ToList();
        Assert.True(differ.Count == 0, $"{differ.Count} flags differ in {name}: {string.Join(", ", differ)}");
        // The journal entries the save has turn on again from a new game's, which would set it, but the original
        // says nothing after loading.
        Assert.False(network.Peek(newJournalEntry));
        Assert.NotNull(network.NewestJournalEntry);
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ALoadedCheckpointSavesAgainAsItWasLoaded(string name)
    {
        var checkpoint = Load(name);
        var game = Game(name, checkpoint);

        var first = SaveFile.Serialize(game.Save());
        var again = GameState.Load(Set, SaveFile.Parse(first));
        Assert.Empty(again.Problems);
        Assert.Equal(first, SaveFile.Serialize(again.Save()));
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void ACopiedPartyIsTheGamesAndChangingItLeavesTheGameAlone(string name)
    {
        var game = Game(name, Load(name));
        var saved = SaveFile.Serialize(game.Save());

        var copy = game.CopyParty();
        Assert.Equal(game.Party.Members.Select(m => (m.Entity, m.Hp, m.Stats.Modified(Stat.Strength))),
            copy.Members.Select(m => (m.Entity, m.Hp, m.Stats.Modified(Stat.Strength))));
        Assert.Equal(game.Party.Gold, copy.Gold);
        Assert.Equal(game.Party.Inventory!.Stacks.Select(s => (s.Item.Id, s.Quantity)), copy.Inventory!.Stacks.Select(s => (s.Item.Id, s.Quantity)));

        // Taking every key item from the copy clears no flag, and leaves the game's party as it was.
        foreach (var stack in copy.Inventory.Stacks.Where(s => s.Item.KeyItem is not null).ToList())
            copy.Inventory.Take(stack.Item.Id, stack.Quantity);
        copy.Members.First().TakeDamage(10);
        Assert.Equal(saved, SaveFile.Serialize(game.Save()));
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void LoadingThePartyAgainPutsItBackAsSaved(string name)
    {
        var game = Game(name, Load(name));
        var saved = game.SaveParty();
        var before = SaveFile.Serialize(game.Save());

        game.Party.Remove(game.Party.Members.Last().Entity);
        game.Party.Gold = 0;
        game.Party.Members.First().TakeDamage(10);
        game.LoadParty(saved);

        Assert.Empty(game.Problems);
        Assert.Equal(before, SaveFile.Serialize(game.Save()));
    }

    /// <summary>The potions working on a member save with what's left of them, and loading applies them again.</summary>
    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void APotionWorkingOnAMemberSavesAndLoadsAgain(string name)
    {
        var game = Game(name, Load(name));
        var member = game.Party.Members.First();
        var strength = member.Stats.RawBase(Stat.Strength);
        member.ApplyPotionEffect(Aidyn.Core.Items.Potion.Strength, 0, 5000);
        member.ApplyPotionEffect(Aidyn.Core.Items.Potion.Defence, 2, 70);
        game.PassTime(1);

        var again = GameState.Load(Set, SaveFile.Parse(SaveFile.Serialize(game.Save())));
        var loaded = again.Party.Members.First();

        Assert.Empty(again.Problems);
        Assert.Equal([(Aidyn.Core.Items.Potion.Strength, 0, 5000u - 72), (Aidyn.Core.Items.Potion.Defence, 2, 0u)],
            loaded.PotionEffects.Select(p => (p.Potion, p.Power, p.Timer)));
        Assert.Equal(strength + 10, loaded.Stats.RawBase(Stat.Strength));
        Assert.Equal(member.Stats.Modified(Stat.Strength), loaded.Stats.Modified(Stat.Strength));
    }

    /// <summary>Alaron renamed in the options menu keeps his name in the save, as the original's does (<c>SaveAlaron</c>).</summary>
    [Fact]
    public void ARenamedAlaronSavesAndLoadsHisName()
    {
        var game = GameState.NewGame(Set);
        var alaron = game.Party.Members.First();
        Assert.Null(GameState.SaveCharacter(alaron).Name);
        alaron.Name = "Bob";

        var again = GameState.Load(Set, SaveFile.Parse(SaveFile.Serialize(game.Save())));

        Assert.Equal("Bob", again.Party.Members.First().Name);
        Assert.Equal(alaron.RecordName, again.Party.Members.First().RecordName);
    }
}
