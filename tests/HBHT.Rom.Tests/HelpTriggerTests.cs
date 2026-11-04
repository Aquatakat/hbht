using HBHT.Core;
using HBHT.Core.Events;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The base plugin's triggers on the events fired where nothing can take over (2.10): the first spell's and first potion's
/// help, each once while its flag is clear, and the new journal entry's notice, over a new game on NA 1.0's records; and what
/// such an event does with an op that would take over.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class HelpTriggerTests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>Fires the event on the game's triggers, returning what they showed; nothing's to be warned of.</summary>
    private static List<string> Fire(GameState game, string name, RecordId? subject)
    {
        var shown = new List<string>();
        var warnings = new List<string>();
        game.Triggers.InPlace(game, warnings.Add)(name, subject, shown.Add);
        Assert.Empty(warnings);
        return [.. shown.Select(TestRoms.Plain)];
    }

    [Fact]
    public void TheFirstSpellTrainedShowsItsHelpAndSetsItsFlagOnce()
    {
        var game = GameState.NewGame(Set);
        var spell = Set.All<SpellRecord>(RecordTypes.Spell).First().Id;
        Assert.False(game.Flags.IsSet(Flag(3611)));

        var help = Assert.Single(Fire(game, GameEvents.SpellTrained, spell));

        Assert.StartsWith("You've learned a new spell! To select a spell in combat, press and hold the", help, StringComparison.Ordinal);
        Assert.True(game.Flags.IsSet(Flag(3611)));
        Assert.Empty(Fire(game, GameEvents.SpellTrained, spell));
    }

    [Fact]
    public void OnlyAPotionGainedShowsTheFirstPotionsHelpAndOnlyOnce()
    {
        var game = GameState.NewGame(Set);
        var items = Set.All<ItemRecord>(RecordTypes.Item).ToList();
        var reagent = items.First(i => i.Kind == ItemKind.Misc).Id;
        var potion = items.First(i => i.Kind == ItemKind.Potion).Id;

        Assert.Empty(Fire(game, GameEvents.ItemGained, reagent));
        Assert.False(game.Flags.IsSet(Flag(3612)));

        var help = Assert.Single(Fire(game, GameEvents.ItemGained, potion));
        Assert.StartsWith("You've acquired a potion! To select a potion in combat, press and hold the", help, StringComparison.Ordinal);
        Assert.True(game.Flags.IsSet(Flag(3612)));
        Assert.Empty(Fire(game, GameEvents.ItemGained, potion));
    }

    [Fact]
    public void ANewJournalEntryIsToldEveryTime()
    {
        var game = GameState.NewGame(Set);
        Assert.Equal([TestRoms.JournalNotice], Fire(game, GameEvents.JournalAdded, null));
        Assert.Equal([TestRoms.JournalNotice], Fire(game, GameEvents.JournalAdded, null));
    }

    [Fact]
    public void AnOpThatWouldTakeOverIsPassedOverWhereNothingCanAndTheRestGoOn()
    {
        var game = GameState.NewGame(Set);
        var trigger = new TriggerRecord(RecordId.Decimal(RecordTypes.Trigger, 9000), GameEvents.JournalAdded, null, null,
            [new ScriptOp.GameOver(), new ScriptOp.FlagSet(Flag(3442)), new ScriptOp.MessageShow("after")]);
        var triggers = new Triggers([trigger]);
        Assert.Contains(triggers.Problems, p => p.Contains("game.over on journal.added", StringComparison.Ordinal));
        var warnings = new List<string>();
        var shown = new List<string>();

        Assert.Null(triggers.Fire(game, GameEvents.JournalAdded, null, warnings.Add, show: shown.Add));

        Assert.Single(warnings);
        Assert.True(game.Flags.IsSet(Flag(3442)));
        Assert.Equal(["after"], shown);
    }

    [Fact]
    public void AMessageWithNowhereToShowIsWarnedOf()
    {
        var game = GameState.NewGame(Set);
        var trigger = new TriggerRecord(RecordId.Decimal(RecordTypes.Trigger, 9000), GameEvents.ScriptEnded, null, null,
            [new ScriptOp.MessageShow("unseen")]);
        var warnings = new List<string>();

        Assert.Null(new Triggers([trigger]).Fire(game, GameEvents.ScriptEnded, null, warnings.Add));

        Assert.Contains("nowhere to show", Assert.Single(warnings), StringComparison.Ordinal);
    }
}
