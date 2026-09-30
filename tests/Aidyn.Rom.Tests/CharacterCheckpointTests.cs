using Aidyn.Core.Characters;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Checks the character rules against the party sheets in the user's checkpoints, played on 1.0 and
/// the debug ROM, with the records imported from 1.0.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CharacterCheckpointTests(MapObjectFixture fixture)
{
    private const int AlaronsTrueNameFlag = 590;

    private PluginSet Set => fixture.Data.Set;

    public static TheoryData<string> Checkpoints() => CheckpointTests.Checkpoints();

    private static AresCheckpoint Load(string name)
    {
        if (name.Length == 0)
            Assert.Skip($"No checkpoints in {TestRoms.Folder}.");
        return AresCheckpoint.Read(Path.Combine(TestRoms.Folder, name));
    }

    private static Character Restore(CheckpointCharacter sheet, EntityRecord entity, CharacterRules rules) => Character.Restore(entity.Id,
        entity.Name, rules, new CharacterStats(rules, sheet.Trained, sheet.Current, sheet.Base, sheet.Modified),
        new CharacterSkills(rules, sheet.SkillBase, sheet.SkillModified), []);

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void EveryMembersLevelIsTheOneTheirExperienceReaches(string name)
    {
        var checkpoint = Load(name);
        var rules = CharacterRules.From(Set);

        Assert.Equal(4, checkpoint.Party.Count);
        Assert.Equal(0x023F, checkpoint.Party[0].Id);
        foreach (var sheet in checkpoint.Party)
        {
            Assert.Equal(sheet.Trained[(int)Stat.Level], rules.LevelFor(sheet.TotalExp));
            Assert.True(sheet.SpendableExp <= sheet.TotalExp);
        }
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void TheSkillsAMemberCantLearnAreTheOnesTheirRecordLacks(string name)
    {
        var checkpoint = Load(name);

        foreach (var sheet in checkpoint.Party)
        {
            var entity = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, sheet.Id));
            var levels = Character.SkillLevels(entity);
            for (var slot = 0; slot < SkillId.Count; slot++)
                Assert.Equal(levels[slot] is null, sheet.SkillBase[slot] == CharacterSkills.CantLearn);
        }
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void AMembersTrueNameAspectAndSchoolComeFromTheirRecord(string name)
    {
        var checkpoint = Load(name);
        var rules = CharacterRules.From(Set);

        foreach (var sheet in checkpoint.Party)
        {
            var entity = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, sheet.Id));
            var character = Character.Create(entity, rules, Set.Get<SpellRecord>, flag => checkpoint.Flags[int.Parse(flag.Key)]);
            // CharExp::flags: True Name 1, heavy 2, Solar 4.
            Assert.Equal((sheet.Flags & 1) != 0, character.TrueName);
            Assert.Equal((sheet.Flags & 2) != 0, character.Heavy);
            Assert.Equal((sheet.Flags & 4) != 0, character.Aspect == Aspect.Solar);
            Assert.Equal((int)entity.School, sheet.School);
        }
        // Alaron has his True Name only once the Roog cinematic has played.
        Assert.Equal(checkpoint.Flags[AlaronsTrueNameFlag], (checkpoint.Party[0].Flags & 1) != 0);
    }

    [Theory]
    [MemberData(nameof(Checkpoints))]
    public void HitPointsReadFromTheSheetsAsTheGameCountsThem(string name)
    {
        var checkpoint = Load(name);
        var rules = CharacterRules.From(Set);

        foreach (var sheet in checkpoint.Party)
        {
            var character = Restore(sheet, Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, sheet.Id)), rules);
            Assert.InRange(character.Hp, 1, character.MaxHp);
            Assert.Equal(sheet.Base[(int)Stat.Endurance] + sheet.Base[(int)Stat.Stamina] + sheet.Base[(int)Stat.Level], character.MaxHp);
        }
    }
}
