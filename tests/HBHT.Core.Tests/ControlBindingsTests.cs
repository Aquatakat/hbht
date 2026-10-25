using HBHT.Core.Controls;
using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

/// <summary>The controls and their bindings (<see cref="ControlBindings"/>), and the options' Controls page over them (ADR 0026).</summary>
public class ControlBindingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "hbht-controls-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private const ControlDevice Keys = ControlDevice.Keyboard, Pad = ControlDevice.Controller;

    /// <summary>
    /// The runtime's input actions before the Controls page (<c>Game.AddInputs</c>), as the controls that took each over in the
    /// situations that read it: the defaults are those bindings, so nothing changes for a player who never opens the page.
    /// </summary>
    [Theory]
    // move_forward W/Up/LeftY-, move_back S/Down/LeftY+, move_left A/Left/LeftX-, move_right D/Right/LeftX+.
    [InlineData("move", "Source:WASD Source:Arrows", "Source:LeftStick")]
    // camera_left Q and RightX-, camera_right E and RightX+, camera_up and down RightY; orbit (RB) with the left stick.
    [InlineData("camera", "", "Source:LeftStick Source:RightStick")]
    [InlineData("camera_hold", "Mouse:Right", "Joy:RB")]
    [InlineData("camera_left", "Key:Q", "")]
    [InlineData("camera_right", "Key:E", "")]
    [InlineData("walk", "Key:Shift", "")]
    [InlineData("sneak", "Key:Ctrl", "Axis:LeftTrigger+")]
    // pause: Enter, Escape, Start; menu_accept Space, A; menu_back Backspace, B.
    [InlineData("use", "Key:Space", "Joy:A")]
    [InlineData("portraits", "Key:Backspace", "Joy:B")]
    [InlineData("pause", "Key:Enter Key:Escape", "Joy:Start")]
    // The menus' directions: the movement keys, the left stick, and the D-pad (menu_up and so on).
    [InlineData("menu_cursor", "Source:WASD Source:Arrows", "Source:LeftStick Source:DPad")]
    [InlineData("menu_accept", "Key:Space", "Joy:A")]
    [InlineData("menu_back", "Key:Backspace", "Joy:B")]
    [InlineData("menu_close", "Key:Enter Key:Escape", "Joy:Start")]
    [InlineData("menu_details", "Key:Tab", "Joy:Y")]
    [InlineData("menu_appraise", "Key:R", "Joy:X")]
    [InlineData("menu_member_left", "Key:Q", "Joy:LB")]
    [InlineData("menu_member_right", "Key:E", "Joy:RB")]
    [InlineData("menu_tab_previous", "Key:Z", "Axis:LeftTrigger+")]
    [InlineData("menu_tab_next", "Key:X", "Axis:RightTrigger+")]
    // Combat read the same actions as the menus.
    [InlineData("battle_target", "Source:WASD Source:Arrows", "Source:LeftStick Source:DPad")]
    [InlineData("battle_attack", "Key:Space", "Joy:A")]
    [InlineData("battle_cancel", "Key:Backspace", "Joy:B")]
    [InlineData("battle_skip", "Key:Z", "Axis:LeftTrigger+")]
    [InlineData("battle_spells", "Key:Tab", "Joy:Y")]
    [InlineData("battle_items", "Key:R", "Joy:X")]
    [InlineData("battle_ranged_left", "Key:Q", "Joy:LB")]
    [InlineData("battle_ranged_right", "Key:E", "Joy:RB")]
    [InlineData("battle_pause", "Key:Enter Key:Escape", "Joy:Start")]
    public void TheDefaultsAreTheBindingsTheRuntimeAlwaysHad(string control, string keyboard, string controller)
    {
        var bindings = new ControlBindings();

        Assert.Equal(Split(keyboard), bindings.Get(control, Keys));
        Assert.Equal(Split(controller), bindings.Get(control, Pad));
        Assert.True(bindings.IsDefault);
    }

    [Fact]
    public void EveryControlIsListedOnceAndTheDefaultsDontConflict()
    {
        Assert.Equal(29, GameControls.All.Count);
        Assert.Equal(GameControls.All.Count, GameControls.All.Select(c => c.Id).Distinct().Count());
        Assert.Empty(new ControlBindings().Conflicts());
        Assert.All(GameControls.All.SelectMany(c => c.Keyboard.Concat(c.Controller)), b => Assert.True(Binding.IsValid(b), b));
    }

    [Fact]
    public void TheSourcesAreTheDirectionsTheyAlwaysWere()
    {
        Assert.Equal(["Key:W", "Key:S", "Key:A", "Key:D"], InputSource.Find(InputSource.Wasd)!.Directions);
        Assert.Equal(["Axis:LeftY-", "Axis:LeftY+", "Axis:LeftX-", "Axis:LeftX+"], InputSource.Find(InputSource.LeftStick)!.Directions);
        Assert.Equal(["Joy:DPadUp", "Joy:DPadDown", "Joy:DPadLeft", "Joy:DPadRight"], InputSource.Find(InputSource.DPad)!.Directions);
    }

    [Theory]
    [InlineData("Key:W", "W")]
    [InlineData("Key:Space", "Space")]
    [InlineData("Key:Up", "Up Arrow")]
    [InlineData("Key:Key5", "5")]
    [InlineData("Key:Pageup", "Page Up")]
    [InlineData("Mouse:Left", "Left Click")]
    [InlineData("Mouse:X1", "Mouse 4")]
    [InlineData("Mouse:WheelDown", "Wheel Down")]
    [InlineData("Joy:LB", "LB")]
    [InlineData("Joy:View", "View")]
    [InlineData("Joy:DPadUp", "D-Pad Up")]
    [InlineData("Axis:LeftTrigger+", "LT")]
    [InlineData("Axis:RightX-", "Right Stick Left")]
    [InlineData("Source:DPad", "D-Pad")]
    public void BindingsAreNamedAsThePlayerKnowsThem(string binding, string name) => Assert.Equal(name, Binding.Name(binding));

    [Fact]
    public void ACellIsDescribedWithBothSticksAndNone()
    {
        Assert.Equal("None", Binding.Describe([]));
        Assert.Equal("Enter, Escape", Binding.Describe(["Key:Enter", "Key:Escape"]));
        Assert.Equal("Both Sticks, D-Pad", Binding.Describe([InputSource.LeftStick, InputSource.RightStick, InputSource.DPad]));
    }

    [Fact]
    public void ARebindReplacesTheCellOnItsDeviceAlone()
    {
        var bindings = new ControlBindings();
        var version = bindings.Version;

        Assert.True(bindings.Rebind(GameControls.Pause, Keys, "Key:P").Done);
        Assert.Equal(["Key:P"], bindings.Get(GameControls.Pause, Keys));
        Assert.Equal(["Joy:Start"], bindings.Get(GameControls.Pause, Pad));
        Assert.NotEqual(version, bindings.Version);
        Assert.False(bindings.IsDefault);
        // A mouse button is the keyboard's; a controller's input isn't, nor a source, nor a debug key.
        Assert.True(bindings.Rebind(GameControls.Use, Keys, "Mouse:Left").Done);
        Assert.False(bindings.Rebind(GameControls.Use, Keys, "Joy:A").Done);
        Assert.False(bindings.Rebind(GameControls.Use, Keys, InputSource.Wasd).Done);
        Assert.Equal("Kept for Debugging", bindings.Rebind(GameControls.Use, Keys, "Key:F8").Refusal);
        Assert.Equal(["Mouse:Left"], bindings.Get(GameControls.Use, Keys));
    }

    [Fact]
    public void ConflictsAreOnlyWithinASituation()
    {
        var bindings = new ControlBindings();

        // Q is the menus' Next Member and the camera's turn; in trek it's free of the menus.
        var result = bindings.Rebind(GameControls.Use, Keys, "Key:Q");
        Assert.True(result.Done);
        Assert.Equal(["Camera Left"], result.TakenFrom);
        Assert.Empty(bindings.Get(GameControls.CameraLeft, Keys));
        Assert.Equal(["Key:Q"], bindings.Get(GameControls.MenuMemberLeft, Keys));
        Assert.Equal(["Key:Q"], bindings.Get(GameControls.BattleRangedLeft, Keys));
        Assert.Empty(bindings.Conflicts());

        // A key in a source takes the source off the direction that had it.
        result = bindings.Rebind(GameControls.Sneak, Keys, "Key:W");
        Assert.Equal(["Move"], result.TakenFrom);
        Assert.Equal([InputSource.Arrows], bindings.Get(GameControls.Move, Keys));
        // Aiming is apart from the turn: the right shoulder can be Ranged Right and Camera Hold at once, as by default.
        Assert.True(bindings.Rebind(GameControls.BattleRangedRight, Pad, "Joy:RB").Done);
        Assert.Equal(["Joy:RB"], bindings.Get(GameControls.CameraHold, Pad));
    }

    [Fact]
    public void SourcesShareButTakeFromButtons()
    {
        var bindings = new ControlBindings();

        // Move and Camera may share a source: Camera Hold switches it between them.
        Assert.True(bindings.SetSources(GameControls.Camera, Keys, [InputSource.Arrows, InputSource.Mouse]).Done);
        Assert.Equal([InputSource.Arrows, InputSource.Mouse], bindings.Get(GameControls.Camera, Keys));
        Assert.Equal([InputSource.Wasd, InputSource.Arrows], bindings.Get(GameControls.Move, Keys));
        // The D-pad on Move takes nothing from the menus'; the mouse can't move him.
        Assert.False(bindings.SetSources(GameControls.Move, Keys, [InputSource.Mouse]).Done);
        Assert.False(bindings.SetSources(GameControls.Move, Keys, [InputSource.DPad]).Done);
        Assert.True(bindings.SetSources(GameControls.Move, Pad, [InputSource.DPad]).Done);
        Assert.Equal([InputSource.LeftStick, InputSource.DPad], bindings.Get(GameControls.MenuCursor, Pad));
        // A button on an input a source now covers loses it.
        bindings.Rebind(GameControls.Walk, Keys, "Key:Left");
        Assert.Equal(["Walk"], bindings.SetSources(GameControls.Move, Keys, [InputSource.Arrows]).TakenFrom);
        Assert.Empty(bindings.Get(GameControls.Walk, Keys));
        Assert.Empty(bindings.Conflicts());
    }

    [Fact]
    public void TheEssentialControlsAlwaysKeepABindingOnEachDevice()
    {
        var bindings = new ControlBindings();

        Assert.Equal("Accept Needs a Binding", bindings.Clear(GameControls.MenuAccept, Pad).Refusal);
        Assert.False(bindings.Clear(GameControls.Pause, Keys).Done);
        Assert.False(bindings.SetSources(GameControls.MenuCursor, Pad, []).Done);
        // Taking Accept's only key refuses the change.
        Assert.Equal("Accept Needs It", bindings.Rebind(GameControls.MenuBack, Keys, "Key:Space").Refusal);
        Assert.Equal(["Key:Backspace"], bindings.Get(GameControls.MenuBack, Keys));
        // The rest can be left with nothing.
        Assert.True(bindings.Clear(GameControls.MenuDetails, Keys).Done);
        Assert.Empty(bindings.Get(GameControls.MenuDetails, Keys));
        Assert.True(bindings.SetSources(GameControls.Move, Pad, []).Done);
        // Pause had Enter and Escape: one taken still leaves the other.
        Assert.True(bindings.Rebind(GameControls.Use, Keys, "Key:Enter").Done);
        Assert.Equal(["Key:Escape"], bindings.Get(GameControls.Pause, Keys));
        Assert.False(bindings.Rebind(GameControls.Use, Keys, "Key:Escape").Done);
    }

    [Fact]
    public void ResetPutsEveryControlBack()
    {
        var bindings = new ControlBindings();
        bindings.Rebind(GameControls.MenuAccept, Keys, "Key:Enter");
        bindings.SetSources(GameControls.Camera, Pad, []);
        Assert.False(bindings.IsDefault);

        bindings.Reset();

        Assert.True(bindings.IsDefault);
        Assert.Equal(["Key:Enter", "Key:Escape"], bindings.Get(GameControls.MenuClose, Keys));
    }

    [Fact]
    public void TheBindingsKeepInTheSettingsFile()
    {
        var path = Path.Combine(_folder, "settings.json");
        var settings = new Settings { MouseSensitivity = 2 };
        settings.Controls.Rebind(GameControls.Use, Keys, "Key:F");
        settings.Controls.SetSources(GameControls.Camera, Keys, [InputSource.Mouse]);
        settings.Controls.Clear(GameControls.Walk, Keys);
        settings.Save(path);

        // Only what changed is written, so a control added later takes its default.
        var text = File.ReadAllText(path);
        Assert.Contains("\"use\"", text);
        Assert.DoesNotContain("\"menu_accept\"", text);
        var read = Settings.Load(path);
        Assert.Equal(2, read.MouseSensitivity);
        Assert.Equal(["Key:F"], read.Controls.Get(GameControls.Use, Keys));
        Assert.Equal(["Joy:A"], read.Controls.Get(GameControls.Use, Pad));
        Assert.Equal([InputSource.Mouse], read.Controls.Get(GameControls.Camera, Keys));
        Assert.Empty(read.Controls.Get(GameControls.Walk));
        Assert.Equal(["Key:Space", "Joy:A"], read.Controls.Get(GameControls.MenuAccept));
        // A copy's changes are its own.
        var copy = read.Copy();
        copy.Controls.Reset();
        Assert.False(read.Controls.IsDefault);
    }

    [Fact]
    public void AFileCantLockThePlayerOut()
    {
        var path = Path.Combine(_folder, "settings.json");
        Directory.CreateDirectory(_folder);
        File.WriteAllText(path, """
            { "musicVolume": 20, "controls": { "menu_accept": [], "use": ["Key:Q", "Joy:Nonsense", "Source:WASD", "Key:F5"], "nothing": ["Key:A"] } }
            """);

        var read = Settings.Load(path);

        Assert.Equal(20, read.MusicVolume);
        Assert.Equal(["Key:Space", "Joy:A"], read.Controls.Get(GameControls.MenuAccept));
        Assert.Equal(["Key:Q"], read.Controls.Get(GameControls.Use));
        Assert.True(new Settings().Controls.IsDefault);
    }

    [Theory]
    [InlineData(GameMode.Trek, Situation.Trek)]
    [InlineData(GameMode.Menu, Situation.Menus)]
    [InlineData(GameMode.Combat, Situation.Combat | Situation.Aiming)]
    [InlineData(GameMode.Conversation, Situation.Talking)]
    [InlineData(GameMode.CutScene, Situation.Talking)]
    [InlineData(GameMode.Popup, Situation.Talking)]
    [InlineData(GameMode.Cinematic, Situation.Talking)]
    public void EachModeIsASituation(GameMode mode, Situation situation) => Assert.Equal(situation, Situations.Of(mode));

    [Fact]
    public void ConversationsTakeTheMenusCursorAcceptBackAndClose()
    {
        var talking = GameControls.In(Situation.Talking).Select(c => c.Id);
        Assert.Equal([GameControls.MenuCursor, GameControls.MenuAccept, GameControls.MenuBack, GameControls.MenuClose], talking);

        // So a rebound Accept is a conversation's too, and is checked against the rest of them.
        var bindings = new ControlBindings();
        Assert.True(bindings.Rebind(GameControls.MenuAccept, Keys, "Key:F").Done);
        Assert.Empty(bindings.Conflicts());
    }

    // The page.

    /// <summary>The options' Controls page, opened from the main page.</summary>
    private static OptionsMenu ControlsPage(Settings settings)
    {
        var options = new OptionsMenu(settings, inGame: true);
        options.Point(3);
        options.Accept();
        Assert.Equal(OptionsPage.Controls, options.Page);
        return options;
    }

    private static int RowOf(OptionsMenu options, string control) => options.Rows.Rows.ToList().FindIndex(r => r.Control == control);

    [Fact]
    public void ThePageListsTheDeviceThenEachSituationsControls()
    {
        var options = ControlsPage(new Settings());
        var rows = options.Rows.Rows;

        Assert.Equal("Device", rows[0].Label);
        Assert.Equal("Keyboard", rows[0].ValueName);
        Assert.Equal(["Trek", "Menus", "Combat"], rows.Select(r => r.Heading).OfType<string>());
        Assert.Equal(["Mouse Sensitivity", "Reset to Defaults"], rows.TakeLast(2).Select(r => r.Label));
        Assert.Equal("WASD, Arrows", rows[RowOf(options, "move")].ValueName);
        Assert.Equal("Enter, Escape", rows[RowOf(options, "pause")].ValueName);
        Assert.Equal("None", rows[RowOf(options, "camera")].ValueName);

        // The Device row switches the page to the controller's bindings, which have no mouse.
        options.Point(0);
        options.Change(1);
        Assert.Equal(ControlDevice.Controller, options.Device);
        Assert.Equal("Left Stick", options.Rows.Rows[RowOf(options, "move")].ValueName);
        Assert.Equal("Both Sticks", options.Rows.Rows[RowOf(options, "camera")].ValueName);
        Assert.Equal("LT", options.Rows.Rows[RowOf(options, "sneak")].ValueName);
        Assert.DoesNotContain(options.Rows.Rows, r => r.Label == "Mouse Sensitivity");
    }

    [Fact]
    public void ThePromptBindsTheNextInputOfItsDevice()
    {
        var settings = new Settings();
        var changes = 0;
        settings.Changed += () => changes++;
        var options = ControlsPage(settings);
        options.Point(RowOf(options, GameControls.Use));

        Assert.Null(options.Accept());
        Assert.Equal(new ControlPrompt(GameControls.Use, Keys, OptionsMenu.PromptTicks), options.Prompt);
        Assert.Equal("Press a Key... 5", options.PromptText);
        // The prompt takes everything: the stick, B and A do nothing, and a controller's button or a debug key isn't bound.
        options.MoveCursor(1);
        Assert.True(options.Back());
        Assert.Null(options.Accept());
        Assert.False(options.Bind("Joy:Y"));
        Assert.False(options.Bind("Key:F7"));
        Assert.Equal(GameControls.Use, options.Rows.Current!.Control);
        Assert.NotNull(options.Prompt);
        // Backspace, Back's own key, is bound rather than cancelling, and takes it from Portraits.
        Assert.True(options.Bind("Key:Backspace"));
        Assert.Null(options.Prompt);
        Assert.Equal(["Key:Backspace"], settings.Controls.Get(GameControls.Use, Keys));
        Assert.Equal("Taken From Portraits", options.Message);
        Assert.Equal(1, changes);
        Assert.True(options.Changed);
    }

    [Fact]
    public void ThePromptGivesUpAfterFiveSeconds()
    {
        var settings = new Settings();
        var options = ControlsPage(settings);
        options.Point(RowOf(options, GameControls.Use));
        options.Accept();

        for (var tick = 0; tick < OptionsMenu.PromptTicks - 1; tick++)
            options.Step();
        Assert.Equal("Press a Key... 1", options.PromptText);
        options.Step();

        Assert.Null(options.Prompt);
        Assert.True(settings.Controls.IsDefault);
        Assert.False(options.Bind("Key:P"));
    }

    [Fact]
    public void AppraiseClearsAndResetPutsBack()
    {
        var settings = new Settings();
        var options = ControlsPage(settings);
        Assert.Equal("R: Clear", options.ClearHint);

        options.Point(RowOf(options, GameControls.Walk));
        Assert.True(options.Clear());
        Assert.Equal("None", options.Rows.Current!.ValueName);
        options.Point(RowOf(options, GameControls.MenuAccept));
        Assert.True(options.Clear());
        Assert.Equal("Accept Needs a Binding", options.Message);
        Assert.Equal("Space", options.Rows.Current!.ValueName);
        // A direction steps through its sources' combinations, as a setting does.
        options.Point(RowOf(options, GameControls.Move));
        Assert.False(options.Clear());
        options.Change(1);
        Assert.Equal("None", options.Rows.Current!.ValueName);
        options.Change(1);
        Assert.Equal("WASD", options.Rows.Current!.ValueName);

        options.Point(options.Rows.Rows.Count - 1);
        options.Accept();
        Assert.True(settings.Controls.IsDefault);
        Assert.Equal("Controls Reset", options.Message);
    }

    [Fact]
    public void TheMenuDirectionCantBeLeftWithNone()
    {
        var options = ControlsPage(new Settings());
        options.Point(RowOf(options, GameControls.MenuCursor));

        Assert.DoesNotContain("None", options.Rows.Current!.Values);
        Assert.Equal(3, options.Rows.Current.Steps);
    }

    [Fact]
    public void TheFadingCopyShowsTheSameDevice()
    {
        var settings = new Settings();
        var options = ControlsPage(settings);
        options.Point(RowOf(options, GameControls.Sneak));
        options.Clear();

        Assert.Empty(settings.Controls.Get(GameControls.Sneak, Keys));
        // The copy the page fades out with shows the same device.
        options.Point(0);
        options.Change(1);
        Assert.Equal(ControlDevice.Controller, options.Copy().Device);
    }

    private static string[] Split(string list) => list.Split(' ', StringSplitOptions.RemoveEmptyEntries);
}
