using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>What the save and load screens ask the runtime to do.</summary>
public enum SaveRequest
{
    None,

    /// <summary>Write the game to <see cref="SaveMenu.Target"/>, then call <see cref="SaveMenu.Saved"/>.</summary>
    Save,

    /// <summary>Load the save at <see cref="SaveMenu.Target"/>, closing the menu.</summary>
    Load,
}

/// <summary>
/// The System tab's Save Game and Load Game screens (<c>WidgetContPakData</c>, <c>ControllerPakSliders</c>): one
/// saved game at a time, with its screenshot, the party's portraits, Alaron's name and the time played, which the
/// stick's left and right step through, and a list of what to do with it.
/// </summary>
/// <remarks>
/// <para>
/// The original's saves are notes on the Controller Pak, up to 16, in the pak's order; the port's are the files
/// in the saves folder (<see cref="SaveSlot.List"/>), the newest first. Saving offers "Create New Save" too, which
/// the original puts last and the port first, so a new save is a press away whatever's saved.
/// </para>
/// <para>
/// The list is Save, Delete and Exit, or Load, Delete and Exit, with no Delete on "Create New Save". Saving over a
/// game asks first, and so does deleting one; saving a new one and loading don't. After saving, "Game
/// successfully saved." with Continue leads back to the System tab's list, as the original's does, and deleting
/// starts the list again from the first save. B, like Exit, goes back to the list.
/// </para>
/// </remarks>
public sealed class SaveMenu
{
    /// <summary>The list's colour, and its cursor's pulse, which is the System tab's (<c>COLOR_RED1</c> to <c>COLOR_PURPLE1</c>).</summary>
    public static readonly Rgba TextColor = PartyMenu.TextColor;

    /// <summary>"Create New Save" and the blank slot's text (<c>COLOR_DARKBROWN</c>).</summary>
    public static readonly Rgba BlankColor = new(103, 70, 60, 255);

    /// <summary>The common strings: the actions, the questions and their answers, the time played and the new slot.</summary>
    public const int LoadLine = 423, SaveLine = 424, DeleteLine = 425, ExitLine = 426, DeleteQuestion = 372, DeleteAnswer = 373,
        Cancel = 374, OverwriteQuestion = 410, OverwriteAnswer = 411, SavedLine = 422, ContinueLine = 176, HoursLine = 481,
        CreateNewSaveLine = 553;

    /// <summary>The port's own line for an empty folder, in place of the original's about the Controller Pak.</summary>
    public const string NoSaves = "There are no saved games.";

    private readonly Func<string, IReadOnlyList<SaveSlot>> _list;
    private readonly string _folder;
    private readonly MenuText _text;
    private readonly Func<RecordId, string?> _names;
    private readonly Func<RecordId, RecordId?> _portraits;

    /// <param name="saving">The Save Game screen, or else Load Game.</param>
    /// <param name="folder">Where the saves are.</param>
    /// <param name="text">The common strings.</param>
    /// <param name="names">An entity's name, for a save whose Alaron has his record's.</param>
    /// <param name="portraits">An entity's portrait.</param>
    /// <param name="list">Lists the saves in the folder; <see cref="SaveSlot.List"/> by default.</param>
    public SaveMenu(bool saving, string folder, MenuText text, Func<RecordId, string?> names, Func<RecordId, RecordId?> portraits,
        Func<string, IReadOnlyList<SaveSlot>>? list = null)
    {
        Saving = saving;
        _folder = folder;
        _text = text;
        _names = names;
        _portraits = portraits;
        _list = list ?? SaveSlot.List;
        Actions = new MenuList<string>([]);
        Refresh(0);
    }

    public static SaveMenu From(PluginSet plugins, bool saving, string folder, Func<string, IReadOnlyList<SaveSlot>>? list = null) =>
        new(saving, folder, MenuText.From(plugins),
            id => plugins.TryGet<EntityRecord>(id)?.Name, id => plugins.TryGet<EntityRecord>(id)?.Portrait, list);

    public bool Saving { get; }

    /// <summary>The slots the stick steps through: each save, and when saving, null first for "Create New Save".</summary>
    public IReadOnlyList<SaveSlot?> Slots { get; private set; } = [];

    /// <summary>The slot shown.</summary>
    public int Slot { get; private set; }

    public SaveSlot? Current => Slots.ElementAtOrDefault(Slot);

    /// <summary>Whether there are slots to the left, or right, which the arrow over the name shows.</summary>
    public bool MoreLeft => Slot > 0;

    public bool MoreRight => Slot < Slots.Count - 1;

    /// <summary>What to do with the slot: Save or Load, Delete and Exit.</summary>
    public MenuList<string> Actions { get; }

    /// <summary>A question over the screen, which takes every press until it's answered.</summary>
    public MenuChoice? Choice { get; private set; }

    public MenuPulse Pulse { get; } = new();

    public MenuPulse ChoicePulse { get; } = new();

    /// <summary>What the runtime is asked to do, and with which file.</summary>
    public SaveRequest Request { get; private set; }

    public string? Target { get; private set; }

    /// <summary>Whether the screen is done, back to the System tab's list.</summary>
    public bool Closed { get; private set; }

    /// <summary>Common line <paramref name="line"/>.</summary>
    public string Text(int line) => _text[line];

    /// <summary>What the slot shows in place of a save: "Create New Save", or that there are none.</summary>
    public string? Blank => Current is not null ? null : Saving ? Text(CreateNewSaveLine) : NoSaves;

    /// <summary>
    /// The name a slot shows: the save's label, such as the checkpoint it was made from, or else Alaron's name,
    /// as the original shows it, which a rename changes.
    /// </summary>
    public string Name(SaveSlot slot)
    {
        if (slot.Save.Label is { Length: > 0 } label)
            return label;
        var alaron = slot.Save.Party.Members.FirstOrDefault(m => m.Entity == GameState.Alaron);
        return alaron?.Name ?? _names(GameState.Alaron) ?? "";
    }

    /// <summary>"Hours: h:m:s", unpadded, from the time played in 60 Hz ticks.</summary>
    public string Hours(SaveSlot slot)
    {
        var t = slot.Save.PlayTime;
        return _text.Format(HoursLine, t / 216000, t % 216000 / 3600, t % 3600 / 60);
    }

    /// <summary>The save's party's portraits, in slot order.</summary>
    public IReadOnlyList<RecordId> Portraits(SaveSlot slot) =>
        [.. slot.Save.Party.Members.Select(m => _portraits(m.Entity)).OfType<RecordId>()];

    public void Step()
    {
        Pulse.Step();
        ChoicePulse.Step();
    }

    /// <summary>The stick's left and right: the slot that way, if there is one.</summary>
    public void Shift(int by)
    {
        if (Choice is not null)
            return;
        var slot = Math.Clamp(Slot + by, 0, Math.Max(0, Slots.Count - 1));
        if (slot == Slot)
            return;
        Slot = slot;
        Build();
    }

    /// <summary>The stick's up and down: the action, or the question's answer.</summary>
    public void MoveCursor(int by)
    {
        if (Choice is { } choice)
            choice.Choices.Move(by);
        else
            Actions.Move(by);
    }

    public void Point(int row)
    {
        if (Choice is null)
            Actions.Point(row);
    }

    /// <summary>A: the action, or the question's answer.</summary>
    public void Accept()
    {
        if (Choice is { } choice)
        {
            Answer(choice.Choices.Cursor);
            return;
        }
        var action = Actions.Cursor;
        var line = action < _actionLines.Count ? _actionLines[action] : ExitLine;
        switch (line)
        {
            case SaveLine when Current is not null:
                Ask(OverwriteQuestion, [Text(OverwriteAnswer), Text(Cancel)]);
                _asking = OverwriteQuestion;
                break;
            case SaveLine:
                (Request, Target) = (SaveRequest.Save, SaveSlot.NewPath(_folder));
                break;
            case LoadLine when Current is { } slot:
                (Request, Target) = (SaveRequest.Load, slot.Path);
                break;
            case DeleteLine when Current is not null:
                Ask(DeleteQuestion, [Text(DeleteAnswer), Text(Cancel)]);
                _asking = DeleteQuestion;
                break;
            case ExitLine:
                Closed = true;
                break;
        }
    }

    /// <summary>B: back to the System tab's list, but a question has no B.</summary>
    public void Back()
    {
        if (Choice is null)
            Closed = true;
    }

    /// <summary>The runtime has written the save it was asked to: says so, with Continue back to the list.</summary>
    public void Saved()
    {
        var target = Target;
        (Request, Target) = (SaveRequest.None, null);
        Refresh(0);
        // The slot shown is the one just written.
        if (Slots.ToList().FindIndex(s => s?.Path == target) is var written and >= 0)
        {
            Slot = written;
            Build();
        }
        Ask(SavedLine, [Text(ContinueLine)]);
        _asking = SavedLine;
    }

    /// <summary>The runtime couldn't do what it was asked: says why.</summary>
    public void Failed(string why)
    {
        (Request, Target) = (SaveRequest.None, null);
        Ask(why, [Text(ContinueLine)]);
        _asking = -1;
    }

    private int _asking;
    private IReadOnlyList<int> _actionLines = [];

    private void Answer(int choice)
    {
        Choice = null;
        switch (_asking)
        {
            case OverwriteQuestion when choice == 0:
                (Request, Target) = (SaveRequest.Save, Current!.Path);
                break;
            case DeleteQuestion when choice == 0:
                SaveSlot.Delete(Current!.Path);
                Refresh(0);
                break;
            case SavedLine:
                Closed = true;
                break;
        }
    }

    private void Ask(int line, IReadOnlyList<string> answers) => Ask(Text(line), answers);

    // WidgetChoiceDia, 150 wide in the menu, in off-white.
    private void Ask(string question, IReadOnlyList<string> answers)
    {
        Choice = new MenuChoice(question, MenuMessage.MaxWidth, 0, MenuMessage.Color, MenuMessage.Color, new MenuList<string>(answers));
        ChoicePulse.Reset();
    }

    /// <summary>Lists the saves again and shows the slot.</summary>
    private void Refresh(int slot)
    {
        var saves = _list(_folder);
        Slots = Saving ? [null, .. saves] : [.. saves];
        Slot = Math.Clamp(slot, 0, Math.Max(0, Slots.Count - 1));
        Build();
    }

    /// <summary>The slot's actions: no Delete on the new slot, and only Exit with nothing to load.</summary>
    private void Build()
    {
        _actionLines = Current is null
            ? Saving ? [SaveLine, ExitLine] : [ExitLine]
            : [Saving ? SaveLine : LoadLine, DeleteLine, ExitLine];
        Actions.Rows = [.. _actionLines.Select(Text)];
        Actions.Point(0);
        Pulse.Reset();
    }
}
