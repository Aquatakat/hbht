using Aidyn.Core.Sky;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Core.Tests;

/// <summary>
/// The pause menu's System tab (<c>WidgetOptionsMenu</c>): the options with the port's graphics page, the name
/// entry (<c>InputMenu</c>), and the save and load screens (<c>WidgetContPakData</c>), as 1.0's code has them.
/// </summary>
public class SystemMenuTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>, IDisposable
{
    private readonly string _saves = Path.Combine(Path.GetTempPath(), "aidyn-saves-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_saves))
            Directory.Delete(_saves, recursive: true);
    }

    // Options.

    [Fact]
    public void TheVolumesStepByAHundredthAndStopAtTheirEnds()
    {
        var settings = new Settings();
        var changes = 0;
        settings.Changed += () => changes++;
        var options = new OptionsMenu(settings, inGame: true);

        Assert.Equal((100, 65), (settings.SoundVolume, settings.MusicVolume));
        options.Change(1);
        Assert.Equal(100, settings.SoundVolume);
        Assert.Equal(0, changes);
        options.Change(-1);
        options.MoveCursor(1);
        options.Change(1);
        Assert.Equal((99, 66), (settings.SoundVolume, settings.MusicVolume));
        Assert.Equal(2, changes);
        Assert.True(options.Changed);
    }

    [Fact]
    public void TheMainPageIsTheVolumesThenGraphicsAndRenameSetOffByHalfALine()
    {
        var options = new OptionsMenu(new Settings(), inGame: true);

        Assert.Equal(["Sound volume:", "Music volume:", "Graphics", "Rename Character"], options.Rows.Rows.Select(r => r.Label));
        Assert.Equal([false, false, true, true], options.Rows.Rows.Select(r => r.GapBefore));
        // The title screen's has no rename, as the original's leaves out its GAME rows.
        Assert.Equal(3, new OptionsMenu(new Settings(), inGame: false).Rows.Rows.Count);
    }

    [Fact]
    public void GraphicsOpensItsPageAndBReturnsToItsRow()
    {
        var settings = new Settings();
        var options = new OptionsMenu(settings, inGame: true);
        options.Point(2);

        Assert.Null(options.Accept());
        Assert.Equal(OptionsPage.Graphics, options.Page);
        Assert.Equal(0, options.Rows.Cursor);
        Assert.True(options.Back());
        Assert.Equal(OptionsPage.Main, options.Page);
        Assert.Equal(2, options.Rows.Cursor);
        Assert.False(options.Back());
    }

    [Fact]
    public void AGraphicsSettingWrapsRoundItsValues()
    {
        var settings = new Settings();
        var options = new OptionsMenu(settings, inGame: true);
        options.Point(2);
        options.Accept();
        options.Point(options.Rows.Rows.ToList().FindIndex(r => r.Label == "Menu size"));

        Assert.Equal("Original", options.Rows.Current!.ValueName);
        options.Change(1);
        Assert.Equal(360, settings.UiLines);
        options.Accept();
        Assert.Equal(480, settings.UiLines);
        options.Change(1);
        Assert.Equal(240, settings.UiLines);
        options.Change(-1);
        Assert.Equal(480, settings.UiLines);

        options.Point(options.Rows.Rows.ToList().FindIndex(r => r.Label == "Sky"));
        options.Change(1);
        Assert.Equal(SkyPlacement.Original, settings.SkyPlacement);
    }

    [Fact]
    public void RenameCharacterLeadsOutOfTheOptions()
    {
        var options = new OptionsMenu(new Settings(), inGame: true);
        options.Point(3);

        Assert.Equal(OptionLink.RenameCharacter, options.Accept());
    }

    [Fact]
    public void SettingsKeepInTheirFile()
    {
        Directory.CreateDirectory(_saves);
        var path = Path.Combine(_saves, "settings.json");
        new Settings { MusicVolume = 30, UiLines = 480, UiFilter = UiFilter.Smooth, BriskPace = true, DrawDistance = 1.4f }.Save(path);

        var read = Settings.Load(path);

        Assert.Equal((30, 480, UiFilter.Smooth, true, 1.4f), (read.MusicVolume, read.UiLines, read.UiFilter, read.BriskPace, read.DrawDistance));
        Assert.Equal(100, Settings.Load(Path.Combine(_saves, "none.json")).SoundVolume);
    }

    // The name entry.

    [Fact]
    public void TheFirstLetterAndAnyAfterASpaceAreCapitalsTheRestSmall()
    {
        var entry = new NameEntry("");
        foreach (var c in "BOB the")
            entry.Type(c);

        Assert.Equal("Bob The", entry.Name);
        entry.Confirm();
        Assert.Equal("Bob The", entry.Chosen);
    }

    [Fact]
    public void ANameIsAtMostTenLettersAndTheLastSlotTakesEachInTurn()
    {
        var entry = new NameEntry("");
        foreach (var c in "abcdefghijkl")
            entry.Type(c);

        Assert.Equal("Abcdefghil", entry.Name);
        Assert.Equal(9, entry.Slot);
    }

    [Fact]
    public void MovingOverTheKeysWritesIntoTheSlotAndATypesIt()
    {
        var entry = new NameEntry("Al");

        // The cursor starts on OK, which writes nothing.
        Assert.Equal(NameEntry.OkKey, NameEntry.Keys[entry.Key]);
        entry.Point(NameEntry.Keys.IndexOf('X'));
        Assert.Equal("Alx", entry.Name);
        entry.Move(1, 0);
        Assert.Equal("Aly", entry.Name);
        entry.Accept();
        Assert.Equal(3, entry.Slot);
        // Down a row, 13 keys on.
        entry.Move(0, 1);
        Assert.Equal('>', NameEntry.Keys[entry.Key]);
        Assert.Equal("Aly>", entry.Name);
    }

    [Fact]
    public void StartGoesToOkThenConfirmsAndBCancels()
    {
        var entry = new NameEntry("Alaron");
        entry.Point(0);
        entry.Start();
        Assert.Equal(NameEntry.OkKey, NameEntry.Keys[entry.Key]);
        Assert.Null(entry.Chosen);
        entry.Start();
        Assert.Equal("Alarona", entry.Chosen);

        var cancelled = new NameEntry("Alaron");
        cancelled.Type('x');
        cancelled.Back();
        Assert.True(cancelled.Cancelled);
        Assert.Null(cancelled.Chosen);
    }

    [Fact]
    public void RubbingOutTakesTheLastLetter()
    {
        var entry = new NameEntry("Ann");
        entry.RubOut();
        entry.RubOut();

        Assert.Equal("A", entry.Name);
        Assert.Equal(1, entry.Slot);
    }

    [Fact]
    public void TheSystemTabRenamesAlaronFromTheOptions()
    {
        var alaron = Characters.Create(Characters.Alaron());
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), alaron);
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);

        Assert.True(system.OnList);
        system.Items.Point((int)SystemItem.Options);
        system.Accept();
        Assert.Equal(SystemPage.Options, system.Page);
        Assert.False(system.OnList);
        system.Options!.Point(3);
        system.Accept();
        Assert.Equal(SystemPage.Rename, system.Page);
        system.Rename!.Type('o');
        Assert.True(system.Start());
        Assert.True(system.Start());

        Assert.Equal(SystemPage.Options, system.Page);
        Assert.Equal(alaron.RecordName + "o", alaron.Name);
        Assert.True(system.Back());
        Assert.True(system.OnList);
        Assert.False(system.Back());
    }

    [Fact]
    public void CreditsAndHelpCloseTheMenuForWhatTheyStart()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);

        Assert.Equal(Enum.GetValues<SystemItem>(), system.Items.Rows);
        Assert.Equal("L160", system.Label(SystemItem.LoadGame));
        Assert.Equal("L377", system.Label(SystemItem.SaveGame));
        Assert.Equal("Credits", system.Label(SystemItem.Credits));
        system.Items.Point((int)SystemItem.Credits);
        system.Accept();
        Assert.True(system.CreditsChosen);
        Assert.False(system.HelpChosen);
        system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);
        system.Items.Point((int)SystemItem.Help);
        system.Accept();
        Assert.True(system.HelpChosen);
    }

    // The save and load screens.

    private static readonly IReadOnlyList<string> Lines =
        [.. Enumerable.Range(0, 560).Select(i => i == SaveMenu.HoursLine ? "Hours: %u:%u:%u" : $"L{i}")];

    private SaveMenu Screen(bool saving) =>
        new(saving, _saves, new MenuText(Lines), _ => "Alaron", id => RecordId.Hex(RecordTypes.Image, 0x100 + int.Parse(id.Key[2..], System.Globalization.NumberStyles.HexNumber) % 0x100));

    private string Write(string name, uint playTime, string? label = null, string? alaronName = null, int minutesAgo = 0)
    {
        Directory.CreateDirectory(_saves);
        var path = Path.Combine(_saves, name + SaveFile.Extension);
        var save = new SaveGame
        {
            Label = label,
            PlayTime = playTime,
            Party = new SaveParty
            {
                Members =
                [
                    new SaveCharacter(GameState.Alaron) { Name = alaronName, Trained = Stats(), Current = Stats() },
                    new SaveCharacter(RecordId.Hex(RecordTypes.Entity, 0x0240)) { Trained = Stats(), Current = Stats() },
                ],
            },
        };
        SaveFile.Write(path, save);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-minutesAgo));
        return path;

        static SaveStats Stats() => new(10, 10, 10, 10, 10, 10, 1);
    }

    [Fact]
    public void LoadingListsTheSavesNewestFirstWithTheirArrows()
    {
        var old = Write("old", 3905657, minutesAgo: 10);
        var recent = Write("recent", 100);
        var screen = Screen(saving: false);

        Assert.Equal([recent, old], screen.Slots.Select(s => s!.Path));
        Assert.Equal((false, true), (screen.MoreLeft, screen.MoreRight));
        Assert.Equal(["L423", "L425", "L426"], screen.Actions.Rows);
        screen.Shift(1);
        Assert.Equal((true, false), (screen.MoreLeft, screen.MoreRight));
        screen.Shift(1);
        Assert.Equal(1, screen.Slot);
        // 3,905,657 ticks is 18 hours, 4 minutes and 54 seconds, which the original prints unpadded and the port pads.
        Assert.Equal("Hours: 18:04:54", screen.Hours(screen.Current!));
        Assert.Equal("Alaron", screen.Name(screen.Current!));
        Assert.Equal(2, screen.Portraits(screen.Current!).Count);
    }

    [Fact]
    public void ASlotShowsItsLabelOrAlaronsName()
    {
        Write("labelled", 0, label: "Before final encounter", minutesAgo: 5);
        Write("renamed", 0, alaronName: "Bob");
        var screen = Screen(saving: false);

        Assert.Equal("Bob", screen.Name(screen.Slots[0]!));
        Assert.Equal("Before final encounter", screen.Name(screen.Slots[1]!));
    }

    [Fact]
    public void LoadingAsksTheRuntimeAtOnce()
    {
        var path = Write("one", 0);
        var screen = Screen(saving: false);
        screen.Accept();

        Assert.Equal((SaveRequest.Load, path), (screen.Request, screen.Target));
        Assert.Null(screen.Choice);
    }

    [Fact]
    public void WithNothingSavedLoadingOffersOnlyExit()
    {
        var screen = Screen(saving: false);

        Assert.Empty(screen.Slots);
        Assert.Equal(SaveMenu.NoSaves, screen.Blank);
        Assert.Equal(["L426"], screen.Actions.Rows);
        screen.Accept();
        Assert.True(screen.Closed);
    }

    [Fact]
    public void SavingOffersANewSaveFirstWhichWritesWithoutAsking()
    {
        Write("Save 1", 0);
        var screen = Screen(saving: true);

        Assert.Null(screen.Slots[0]);
        Assert.Equal("L553", screen.Blank);
        Assert.Equal(["L424", "L426"], screen.Actions.Rows);
        screen.Accept();
        Assert.Equal((SaveRequest.Save, Path.Combine(_saves, "Save 2.json")), (screen.Request, screen.Target));

        // The runtime writes it; the screen says so, on the slot just written, and Continue goes back to the list.
        Write("Save 2", 0);
        screen.Saved();
        Assert.Equal(SaveRequest.None, screen.Request);
        Assert.Equal(Path.Combine(_saves, "Save 2.json"), screen.Current!.Path);
        Assert.Equal("L422", screen.Choice!.Text);
        Assert.Equal(["L176"], screen.Choice.Choices.Rows);
        screen.Accept();
        Assert.True(screen.Closed);
    }

    [Fact]
    public void SavingOverAGameAsksFirst()
    {
        var path = Write("one", 0);
        var screen = Screen(saving: true);
        screen.Shift(1);
        screen.Accept();

        Assert.Equal("L410", screen.Choice!.Text);
        Assert.Equal(["L411", "L374"], screen.Choice.Choices.Rows);
        Assert.Equal(SaveRequest.None, screen.Request);
        // A question has no B.
        screen.Back();
        Assert.False(screen.Closed);
        screen.MoveCursor(1);
        screen.Accept();
        Assert.Equal(SaveRequest.None, screen.Request);
        screen.Accept();
        screen.Accept();
        Assert.Equal((SaveRequest.Save, path), (screen.Request, screen.Target));
    }

    [Fact]
    public void DeletingAsksAndStartsAgainFromTheFirstSlot()
    {
        Write("a", 0, minutesAgo: 5);
        var b = Write("b", 0);
        File.WriteAllBytes(SaveSlot.ScreenshotPath(b), [1, 2, 3]);
        var screen = Screen(saving: false);
        screen.Shift(1);
        screen.Shift(-1);
        screen.Point(1);
        screen.Accept();

        Assert.Equal("L372", screen.Choice!.Text);
        Assert.Equal(["L373", "L374"], screen.Choice.Choices.Rows);
        screen.Accept();
        Assert.False(File.Exists(b));
        Assert.False(File.Exists(SaveSlot.ScreenshotPath(b)));
        Assert.Single(screen.Slots);
        Assert.Equal(0, screen.Slot);
    }

    [Fact]
    public void TheNewSlotHasNoDelete()
    {
        var screen = Screen(saving: true);

        Assert.DoesNotContain("L425", screen.Actions.Rows);
        Assert.True((screen.MoreLeft, screen.MoreRight) == (false, false));
    }
}
