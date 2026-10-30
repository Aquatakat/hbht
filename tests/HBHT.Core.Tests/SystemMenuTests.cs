using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Tests;

/// <summary>
/// The pause menu's System tab (<c>WidgetOptionsMenu</c>): the options with the port's graphics page, the name
/// entry (<c>InputMenu</c>), and the save and load screens (<c>WidgetContPakData</c>), as 1.0's code has them.
/// </summary>
public class SystemMenuTests(BarterPlugins fixture) : IClassFixture<BarterPlugins>, IDisposable
{
    private readonly string _saves = Path.Combine(Path.GetTempPath(), "hbht-saves-" + Guid.NewGuid().ToString("N"));

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
        options.Accept();

        Assert.Equal(OptionsPage.Audio, options.Page);
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
    public void TheMainPageIsThePagesThenRenameSetOffByHalfALine()
    {
        var options = new OptionsMenu(new Settings(), inGame: true);

        Assert.Equal(["Audio", "Video", "Enhancements", "Controls", "Rename Character"], options.Rows.Rows.Select(r => r.Label));
        Assert.Equal([false, false, false, false, true], options.Rows.Rows.Select(r => r.GapBefore));
        // The title screen's has no rename, as the original's leaves out its GAME rows.
        Assert.Equal(4, new OptionsMenu(new Settings(), inGame: false).Rows.Rows.Count);
    }

    [Theory]
    [InlineData(0, OptionsPage.Audio)]
    [InlineData(1, OptionsPage.Video)]
    [InlineData(2, OptionsPage.Enhancements)]
    [InlineData(3, OptionsPage.Controls)]
    public void ALinkOpensItsPageAndBReturnsToItsRow(int row, OptionsPage page)
    {
        var options = new OptionsMenu(new Settings(), inGame: true);
        options.Point(row);

        Assert.Null(options.Accept());
        Assert.Equal(page, options.Page);
        Assert.Equal(0, options.Rows.Cursor);
        Assert.True(options.Back());
        Assert.Equal(OptionsPage.Main, options.Page);
        Assert.Equal(row, options.Rows.Cursor);
        Assert.False(options.Back());
    }

    [Fact]
    public void TheVideoAndEnhancementsPagesListTheirSettings()
    {
        var options = new OptionsMenu(new Settings(), inGame: true);
        options.Point(1);
        options.Accept();
        Assert.Equal(["Display", "Window Size", "Render Resolution", "Frame Rate Limit", "V-Sync", "Anti-Aliasing"],
            options.Rows.Rows.Select(r => r.Label));
        options.Back();
        options.Point(2);
        options.Accept();
        Assert.Equal(["Draw Distance", "Object Distance", "Model Detail", "Rain and Snow", "Menu Size", "Menu Filter", "Pace", "Movement",
                "Status Auras"],
            options.Rows.Rows.Select(r => r.Label));
        // A new player's are the original's, and the original's video out.
        Assert.Equal(["Original", "Original", "Original", "Original", "Original", "Video Out", "Original", "Original", "Original"],
            options.Rows.Rows.Select(r => r.ValueName));
    }

    [Fact]
    public void TheVideoPageListsSizesForTheScreen()
    {
        var settings = new Settings();
        // A 1080p screen whose taskbar leaves 1040 lines for a window, with the window at 1280 × 960.
        var options = new OptionsMenu(settings, inGame: true, ScreenInfo.Default);
        options.Point(1);
        options.Accept();
        OptionRow Row(string label) => options.Rows.Rows.Single(r => r.Label == label);

        Assert.Equal(["640x480", "960x720", "1280x720", "1280x960", "1600x900", "Maximized"], Row("Window Size").Values);
        Assert.Equal("1280x960", Row("Window Size").ValueName);
        // The original's line counts, then parts of the window's size.
        Assert.Equal(["240p", "480p", "50%", "75%", "100%", "150%", "200%"], Row("Render Resolution").Values);
        Assert.Equal("100%", Row("Render Resolution").ValueName);
        Assert.Equal(["Display", "30 FPS", "60 FPS", "90 FPS", "120 FPS", "144 FPS", "Unlimited"], Row("Frame Rate Limit").Values);
        Assert.Equal("Display", Row("Frame Rate Limit").ValueName);

        options.Point(1);
        options.Change(1);
        Assert.Equal((1600, 900), (settings.WindowWidth, settings.WindowHeight));
        options.Point(2);
        options.Change(1);
        Assert.Equal((0, 1.5f), (settings.WorldLines, settings.RenderScale));
        options.Change(2);
        Assert.Equal((240, 1f), (settings.WorldLines, settings.RenderScale));
        Assert.Equal("240p", Row("Render Resolution").ValueName);

        // Fullscreen is the screen's own size, which the stick can't change.
        options.Point(0);
        options.Change(1);
        Assert.True(settings.Fullscreen);
        Assert.Equal(["1920x1080"], Row("Window Size").Values);
        Assert.Equal("240p", Row("Render Resolution").ValueName);
    }

    [Fact]
    public void OlderRenderSettingsReadAsTheirScale()
    {
        using var folder = new TempPlugins();
        Directory.CreateDirectory(folder.Root);
        var path = Path.Combine(folder.Root, "settings.json");
        File.WriteAllText(path, """{ "worldLines": 0, "supersampled": true }""");
        Assert.Equal((0, 2f), (Settings.Load(path).WorldLines, Settings.Load(path).RenderScale));
        File.WriteAllText(path, """{ "worldLines": 1080, "supersampled": false }""");
        Assert.Equal((0, 1f), (Settings.Load(path).WorldLines, Settings.Load(path).RenderScale));
        File.WriteAllText(path, """{ "worldLines": 480 }""");
        Assert.Equal(480, Settings.Load(path).WorldLines);

        // And the old name isn't written again.
        Settings.Load(path).Save(path);
        Assert.DoesNotContain("supersampled", File.ReadAllText(path));
    }

    [Fact]
    public void WindowSizeIsTheWindowAsItIs()
    {
        var settings = new Settings { WindowWidth = 1280, WindowHeight = 960 };
        var options = new OptionsMenu(settings, inGame: true, ScreenInfo.Default);
        options.Point(1);
        options.Accept();
        OptionRow Row(string label) => options.Rows.Rows.Single(r => r.Label == label);

        // Maximized is the last size.
        options.Point(1);
        Assert.Equal("1280x960", Row("Window Size").ValueName);
        options.Change(1);
        options.Change(1);
        Assert.True(settings.Maximized);
        Assert.Equal("Maximized", Row("Window Size").ValueName);
        // A size restores it.
        options.Change(1);
        Assert.False(settings.Maximized);
        Assert.Equal("640x480", Row("Window Size").ValueName);

        // A size the player dragged the window to is listed among them, as the runtime sets it.
        (settings.WindowWidth, settings.WindowHeight) = (1111, 777);
        options.Refresh(ScreenInfo.Default with { WindowWidth = 1111, WindowHeight = 777 });
        Assert.Equal("1111x777", Row("Window Size").ValueName);
        Assert.Contains("1111x777", Row("Window Size").Values);
    }

    [Fact]
    public void AnEnhancementWrapsRoundItsValues()
    {
        var settings = new Settings();
        var options = new OptionsMenu(settings, inGame: true);
        options.Point(2);
        options.Accept();
        options.Point(options.Rows.Rows.ToList().FindIndex(r => r.Label == "Menu Size"));

        Assert.Equal("Original", options.Rows.Current!.ValueName);
        options.Change(1);
        Assert.Equal(360, settings.UiLines);
        options.Accept();
        Assert.Equal(480, settings.UiLines);
        options.Change(1);
        Assert.Equal(240, settings.UiLines);
        options.Change(-1);
        Assert.Equal(480, settings.UiLines);

        options.Point(options.Rows.Rows.ToList().FindIndex(r => r.Label == "Object Distance"));
        options.Change(-1);
        Assert.Equal(3, settings.ObjectDistance);
        options.Point(options.Rows.Rows.ToList().FindIndex(r => r.Label == "Menu Filter"));
        Assert.Equal("Video Out", options.Rows.Current!.ValueName);
    }

    [Fact]
    public void RenameCharacterLeadsOutOfTheOptions()
    {
        var options = new OptionsMenu(new Settings(), inGame: true);
        options.Point(4);

        Assert.Equal(OptionLink.RenameCharacter, options.Accept());
    }

    [Fact]
    public void SettingsKeepInTheirFile()
    {
        Directory.CreateDirectory(_saves);
        var path = Path.Combine(_saves, "settings.json");
        new Settings
        {
            MusicVolume = 30, UiLines = 480, UiFilter = UiFilter.Smooth, BriskPace = true, ObjectDistance = 3, ExtendedDrawDistance = true,
            Movement = Movement.MovementMode.Modern,
        }.Save(path);

        var read = Settings.Load(path);

        Assert.Equal((30, 480, UiFilter.Smooth, true, 3f, true), (read.MusicVolume, read.UiLines, read.UiFilter, read.BriskPace, read.ObjectDistance, read.ExtendedDrawDistance));
        Assert.Equal(Movement.MovementMode.Modern, read.Movement);
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
        // The letter the cursor only showed isn't kept.
        Assert.Equal("Alaron", entry.Chosen);

        var cancelled = new NameEntry("Alaron");
        cancelled.Type('x');
        cancelled.Back();
        Assert.True(cancelled.Cancelled);
        Assert.Null(cancelled.Chosen);
    }

    [Fact]
    public void TheLastThreeKeysPutTheSlotBack()
    {
        var entry = new NameEntry("Alaron");
        entry.Point(NameEntry.Keys.IndexOf('B'));
        entry.Accept();
        entry.Point(NameEntry.Keys.IndexOf(' '));
        Assert.Equal("Alaronb ", entry.Name);
        // Onto Rub Out: the slot is empty again, its underscore shown, and A rubs out the b typed.
        entry.Move(1, 0);
        Assert.Equal("Alaronb", entry.Name);
        Assert.Equal(7, entry.Slot);
        entry.Accept();
        Assert.Equal("Alaron", entry.Name);
        // Onto OK from a letter, then confirmed.
        entry.Point(NameEntry.Keys.IndexOf('Z'));
        entry.Point(NameEntry.Keys.IndexOf(NameEntry.OkKey));
        Assert.Equal("Alaron", entry.Name);
        entry.Accept();
        Assert.Equal("Alaron", entry.Chosen);

        // A full name's last slot gets its own letter back.
        var full = new NameEntry("Abcdefghij");
        full.Point(NameEntry.Keys.IndexOf('X'));
        Assert.Equal("Abcdefghix", full.Name);
        full.Point(NameEntry.Keys.IndexOf(NameEntry.CancelKey));
        Assert.Equal("Abcdefghij", full.Name);
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
        system.Options!.Point(4);
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
    public void EachPageCrossfadesWithTheOneBeforeIn5Ticks()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);
        Assert.Equal((255, null), (system.PageAlpha, system.Outgoing));

        // The new page comes in at 0 and the old stays at 255, then each tick 60 more and 60 less (0x8003f81c).
        system.Items.Point((int)SystemItem.Options);
        system.Accept();
        Assert.Equal((SystemPage.Options, SystemPage.List), (system.Page, system.Outgoing!.Page));
        List<(byte, byte)> alphas = [];
        for (var i = 0; i < 5; i++)
        {
            system.Step();
            alphas.Add((system.PageAlpha, system.OutgoingAlpha));
        }
        Assert.Equal([(60, 195), (120, 135), (180, 75), (240, 15), (255, 0)], alphas);
        Assert.Null(system.Outgoing);

        // The Enhancements page fades out as it was, though the options have gone back to their main page.
        system.Options!.Point(2);
        system.Accept();
        Assert.Equal((OptionsPage.Enhancements, OptionsPage.Main), (system.Options.Page, system.Outgoing!.Options!.Page));
        system.Back();
        Assert.Equal((OptionsPage.Main, OptionsPage.Enhancements), (system.Options.Page, system.Outgoing!.Options!.Page));
        Assert.Equal(2, system.Options.Rows.Cursor);
        // Back to the list, which the options fade out over.
        system.Back();
        Assert.Equal((SystemPage.List, SystemPage.Options, (byte)0), (system.Page, system.Outgoing!.Page, system.PageAlpha));
        // The Theatre's page as the menu reopens on it after a cinematic shows at once.
        system.ShowTheatre(fade: false);
        Assert.Equal((byte)255, system.PageAlpha);
    }

    [Fact]
    public void CreditsAndHelpCloseTheMenuForWhatTheyStart()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);

        Assert.Equal(Enum.GetValues<SystemItem>().Where(i => i is not (SystemItem.Flee or SystemItem.NewGame)), system.Items.Rows);
        Assert.Equal("L160", system.Label(SystemItem.LoadGame));
        Assert.Equal("L377", system.Label(SystemItem.SaveGame));
        Assert.Equal("Credits", system.Label(SystemItem.Credits));
        // Each ends the menu with its exit code: 5 for the credits and 3 for help (PauseMenu.Exit).
        system.Items.Point((int)SystemItem.Credits);
        Assert.Equal(PauseExit.Credits, system.Accept());
        Assert.Equal(5, (int)PauseExitCode.Credits);
        system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);
        system.Items.Point((int)SystemItem.Help);
        Assert.Equal(PauseExit.Help, system.Accept());
        Assert.Equal(3, (int)PauseExitCode.Help);
        // The other rows open their screens, and the menu stays open.
        foreach (var item in new[] { SystemItem.LoadGame, SystemItem.SaveGame, SystemItem.Options, SystemItem.Theater })
        {
            system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves);
            system.Items.Point((int)item);
            Assert.Null(system.Accept());
            Assert.False(system.OnList);
        }
    }

    [Fact]
    public void CombatsListIsOptionsAndFleeWhenThePartyCanFlee()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));
        // OptionsMenu's combat branch: Options (161) and, while the party can flee from a turn's pause, Flee (378).
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves, combat: true);
        Assert.Equal([SystemItem.Options, SystemItem.Flee, SystemItem.Quit], system.Items.Rows);
        Assert.Equal("L378", system.Label(SystemItem.Flee));
        system.Items.Point(1);
        Assert.Equal(PauseExit.Flee, system.Accept());
        Assert.Equal([SystemItem.Options, SystemItem.Quit], new SystemMenu(fixture.Plugins, new Settings(), party, _saves, combat: false).Items.Rows);
    }

    // Quit Game, the port's.

    [Fact]
    public void QuitGameIsLastInEveryList()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));

        Assert.Equal(SystemItem.Quit, new SystemMenu(fixture.Plugins, new Settings(), party, _saves).Items.Rows[^1]);
        Assert.Equal([SystemItem.NewGame, SystemItem.LoadGame, SystemItem.Options, SystemItem.Quit],
            new SystemMenu(fixture.Plugins, new Settings(), party, _saves, title: true).Items.Rows);
        Assert.Equal("Quit Game", new SystemMenu(fixture.Plugins, new Settings(), party, _saves).Label(SystemItem.Quit));
    }

    [Fact]
    public void TheTitlesQuitGameQuitsAtOnce()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves, title: true);
        system.Items.Point(3);

        Assert.Equal(PauseExit.Quit, system.Accept());
        Assert.Equal(PauseExit.Quit, system.Ended);
        Assert.Null(system.Quitting);
    }

    [Fact]
    public void ThePauseMenusQuitGameAsksWhereToGoAndWarnsOfUnsavedProgress()
    {
        var party = TradeData.PartyOf(TradeData.Catalog(), ItemData.Flags(), Characters.Create(Characters.Alaron()));
        var unsaved = true;
        var system = new SystemMenu(fixture.Plugins, new Settings(), party, _saves, unsaved: () => unsaved);
        system.Items.Point(system.Items.Rows.Count - 1);

        Assert.Null(system.Accept());
        var box = Assert.IsType<MenuChoice>(system.Quitting);
        Assert.Equal(SystemMenu.Unsaved, box.Text);
        Assert.Equal([SystemMenu.QuitToDesktop, SystemMenu.ReturnToTitle, $"L{SaveMenu.Cancel}"], box.Choices.Rows);
        // The tab can't be left while it's up, and the list's cursor stays put under it.
        Assert.False(system.OnList);
        system.Point(0);
        system.MoveCursor(1);
        Assert.Equal((system.Items.Rows.Count - 1, 1), (system.Items.Cursor, box.Choices.Cursor));
        Assert.Equal(PauseExit.ToTitle, system.Accept());
        Assert.Null(system.Quitting);

        // B takes it down, and so does Cancel; the menu stays open.
        system.Accept();
        Assert.True(system.Back());
        Assert.Null(system.Quitting);
        Assert.True(system.OnList);
        system.Accept();
        system.PointQuit(2);
        Assert.Null(system.Accept());
        Assert.True(system.OnList);

        // Saved and unchanged since, it asks only where to go.
        unsaved = false;
        system.Accept();
        Assert.Equal(("", 0), (system.Quitting!.Text, system.Quitting.Gap));
        Assert.Equal(PauseExit.Quit, system.Accept());
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

    /// <summary>Lets a slide between saves finish.</summary>
    private static void Settle(SaveMenu screen)
    {
        for (var i = 0; i <= SaveMenu.SlideFrames; i++)
            screen.Step();
        Assert.False(screen.Sliding);
    }

    [Fact]
    public void TheSavesSlideOnTheEaseOutCurveAndDropPressesMeanwhile()
    {
        Write("old", 0, minutesAgo: 10);
        Write("recent", 0);
        var screen = Screen(saving: false);

        screen.Shift(1);
        Assert.Equal((0, 1, 1), (screen.Slide!.From, screen.Slide.Direction, screen.Slot));
        Assert.Equal(0, screen.SlideChange);
        // Every press is dropped while it slides: the stick, the actions, A and B.
        screen.Shift(-1);
        screen.MoveCursor(1);
        screen.Back();
        screen.Accept();
        Assert.Equal((1, 0, false, SaveRequest.None), (screen.Slot, screen.Actions.Cursor, screen.Closed, screen.Request));
        // sin(π t / 2): quick at first, slow at the end, and done in half the original's 60 frames.
        for (var i = 0; i < SaveMenu.SlideFrames / 2; i++)
            screen.Step();
        Assert.Equal(MathF.Sin(MathF.PI / 4), screen.SlideChange, 5);
        Settle(screen);
        Assert.Equal(1, screen.SlideChange);
        screen.Shift(-1);
        Assert.Equal((1, -1), (screen.Slide!.From, screen.Slide.Direction));
        // A slot that isn't there doesn't slide.
        Settle(screen);
        screen.Shift(-1);
        Assert.Null(screen.Slide);
    }

    [Fact]
    public void ASaveSlideStepsItsEaseOutShareEachFrameAndFinishesOneFrameLater()
    {
        Write("old", 0, minutesAgo: 10);
        Write("recent", 0);
        var screen = Screen(saving: false);
        screen.Shift(1);
        for (var frame = 0; frame <= SaveMenu.SlideFrames + 2; frame++)
        {
            var sliding = frame <= SaveMenu.SlideFrames;
            Assert.Equal(sliding, screen.Slide is not null);
            Assert.Equal(sliding ? MenuTween.EaseOut(frame, SaveMenu.SlideFrames) : 1, screen.SlideChange);
            screen.Step();
        }
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
        Settle(screen);
        Assert.Equal((true, false), (screen.MoreLeft, screen.MoreRight));
        screen.Shift(1);
        Settle(screen);
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
        Settle(screen);
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
        Settle(screen);
        screen.Shift(-1);
        Settle(screen);
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
