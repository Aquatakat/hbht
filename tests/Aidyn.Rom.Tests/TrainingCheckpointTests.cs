using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The training window against NA 1.0's imported data: the common strings it reads, and what the user's
/// captures of STAI01's training show (Oct 3, 2026), from the "Before final encounter" checkpoint.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class TrainingCheckpointTests(MapObjectFixture fixture)
{
    private const string Checkpoint = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Before final encounter.bs1";

    private PluginSet Set => fixture.Data.Set;

    /// <summary>A line as the user reads it: without the button glyphs, and trimmed.</summary>
    private static string Plain(string line) => new string([.. line.Where(c => c is < '\u0080' or > '\u009F')]).Trim();

    [Fact]
    public void TheTrainingLinesOfTheCommonStringsReadAsTheWindowNeedsThem()
    {
        var text = MenuText.From(Set);

        Assert.Equal(
            [
                "XP Cost: MAX", "XP Cost: %lu", "XP Remaining: %lu", "Gold Cost: %lu", "You cannot learn this skill.",
                "I cannot teach you further in that skill.", "You cannot train this skill any further.",
                "You need %ld XP and %ld gold to train this skill.", "You need %ld XP to train this skill.",
                "Only wizards can learn spells.", "You cannot train this spell any further.", "Yes", "No",
            ],
            new[]
            {
                MenuText.XpCostMax, MenuText.XpCost, MenuText.XpRemainingLine, MenuText.GoldCost, MenuText.SkillCantLearn, MenuText.SkillCantTeach,
                MenuText.SkillAtMost, MenuText.SkillNeeds, MenuText.SkillNeedsXp, MenuText.OnlyWizardsTrain, MenuText.SpellAtMost,
                TrainingMenu.YesLine, TrainingMenu.NoLine,
            }.Select(line => Plain(text[line])));
        Assert.StartsWith("Are you sure you wish to train this for %ld XP and %ld gold?", Plain(text[MenuText.TrainSkillFor]));
        Assert.StartsWith("You've learned a new spell!", Plain(text[MenuText.FirstSpell]));
    }

    [Fact]
    public void STAI01TeachesAlaronWhatTheCapturesShow()
    {
        var path = Path.Combine(TestRoms.Folder, Checkpoint);
        if (!File.Exists(path))
            Assert.Skip($"No {Checkpoint} in {TestRoms.Folder}.");
        var game = GameState.Load(Set, CheckpointSave.Convert(AresCheckpoint.Read(path), Set, CheckpointSave.Label(path)));
        var keeper = Set.Get<EntityRecord>(RecordId.Hex(RecordTypes.Entity, 0x0280));
        var menu = TrainingMenu.For(Set, game.Party, Trainer.From(keeper, game.Rules, Set.Get<SpellRecord>), game.Flags);

        Assert.Equal(18, menu.Skills.Rows.Count);
        Assert.Equal(["Pole", "Sword", "Thrown", "Shield"], menu.Skills.Rows.Skip(14).Select(r => r.Name));
        Assert.Equal(["Thief", "Troubadour", "Warrior", "Wizard", "Hafted", "Missile"], menu.Skills.Rows.Skip(8).Take(6).Select(r => r.Name));
        Assert.All(menu.Skills.Rows.Skip(8).Take(6), r => Assert.Equal(1, r.Value));
        menu.Point(menu.Skills.Rows.ToList().FindIndex(r => r.Name == "Wizard"));
        Assert.True(menu.Followed);
        Assert.Equal(["XP Cost: MAX", "XP Remaining: 659576", "Gold Cost: 440"], menu.PriceLines);

        // The trainer has no spells: only the experience shows.
        menu.Switch();
        Assert.Empty(menu.Spells.Rows);
        Assert.Equal([" ", "XP Remaining: 659576", " "], menu.PriceLines);
        Assert.Equal(["vs. Elemental", "vs. Naming", "vs. Necromancy", "vs. Star", "Air Shield", "Wind", "Spirit Shield"],
            menu.Party.Spells.Rows.Select(r => r.Name));
    }
}
