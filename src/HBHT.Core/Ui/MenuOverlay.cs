using HBHT.Plugins.Records;

namespace HBHT.Core.Ui;

/// <summary>Which of a <see cref="MenuOverlay"/>'s popups is the newest, and so takes the presses.</summary>
public enum MenuPopupLayer
{
    /// <summary>A message, over everything (<see cref="MenuMessage"/>).</summary>
    Message,

    /// <summary>The help on the parchment (<c>WidgetMenuPrompt</c>).</summary>
    Help,

    /// <summary>A question over choices (<see cref="MenuChoice"/>).</summary>
    Choice,

    /// <summary>C-down's details (<see cref="DetailsPane"/>), which take the presses and do nothing with them.</summary>
    Details,
}

/// <summary>
/// The popups a menu has open over its state: a message, the help, a question over choices and C-down's details, which
/// the Party tab, the loot and shop window, the training window, the World tab and the save screens each keep.
/// As the original's widget handler does, it gives input to the newest widget only (<c>WidgetManager.Input</c>,
/// <c>0x800ad2d4</c>; see <see cref="PopupFade"/>): the menu asks <see cref="Top"/> what takes the presses, closes that
/// one, and what was under it takes them next.
/// </summary>
/// <remarks>
/// <para>
/// The layers are in the order the menus have always handled them, newest first: <see cref="Message"/>,
/// <see cref="Help"/>, <see cref="Choice"/>, then the <see cref="Details"/>. A menu shows one at a time in practice,
/// since what opens a popup waits for <see cref="HasPopup"/> to clear; the order says which takes a press if two were up.
/// A menu's own popups, such as the loot window's drop question (<see cref="BarterConfirm"/>) or the Party tab's crafting
/// popup, stay with it, and count in its own <c>HasPopup</c>.
/// </para>
/// <para>
/// The choice's cursor pulses between a scroll menu's own colours (<see cref="Highlight"/>) a step a menu frame, from the
/// first colour when it's asked. Trek's popup pulses at a trek frame's rate, so it keeps its own
/// (<see cref="MapObjects.TrekPopup"/>).
/// </para>
/// </remarks>
public sealed class MenuOverlay
{
    private readonly MenuPulse _pulse = new();

    /// <summary>A message over everything, which a press closes.</summary>
    public MenuMessage? Message { get; private set; }

    /// <summary>The help on the parchment, which a press closes.</summary>
    public string? Help { get; private set; }

    /// <summary>A question over choices, which the menu answers.</summary>
    public MenuChoice? Choice { get; private set; }

    /// <summary>C-down's details of the row the menu points at, which fade as popups do (<see cref="PopupFade"/>).</summary>
    public DetailsPane Details { get; } = new();

    /// <summary>The popup that takes the presses, the newest, or null if none is up.</summary>
    public MenuPopupLayer? Top =>
        Message is not null ? MenuPopupLayer.Message
        : Help is not null ? MenuPopupLayer.Help
        : Choice is not null ? MenuPopupLayer.Choice
        : Details.Showing ? MenuPopupLayer.Details
        : null;

    /// <summary>Whether a popup or the details are taking the presses, fading or not.</summary>
    public bool HasPopup => Top is not null;

    /// <summary>The colour the choice's cursor pulses to now.</summary>
    public Rgba Highlight => _pulse.Now(MenuPulse.ScrollFrom, MenuPulse.ScrollTo);

    /// <summary>Puts a message up, over what is open.</summary>
    public void Say(MenuMessage message) => Message = message;

    /// <summary>Puts a message of the usual shape up.</summary>
    public void Say(string text) => Message = new MenuMessage(text);

    /// <summary>Puts the help up.</summary>
    public void ShowHelp(string text) => Help = text;

    /// <summary>Puts a question up, its cursor pulsing from the first colour.</summary>
    public void Ask(MenuChoice choice)
    {
        Choice = choice;
        ResetPulse();
    }

    /// <summary>Starts the cursor's pulse again from the first colour, for a popup of the menu's own that shares it.</summary>
    public void ResetPulse() => _pulse.Reset();

    /// <summary>Closes the message. True if there was one.</summary>
    public bool CloseMessage()
    {
        if (Message is null)
            return false;
        Message = null;
        return true;
    }

    /// <summary>Closes the help. True if there was one.</summary>
    public bool CloseHelp()
    {
        if (Help is null)
            return false;
        Help = null;
        return true;
    }

    /// <summary>Closes the message, else the help: the layers a press closes outright. True if one closed.</summary>
    public bool Dismiss() => CloseMessage() || CloseHelp();

    /// <summary>Takes the question down, answered or not.</summary>
    public void CloseChoice() => Choice = null;

    /// <summary>Moves the question's cursor, if it's the popup that takes the presses. True if it was.</summary>
    public bool MoveCursor(int by)
    {
        if (Top != MenuPopupLayer.Choice)
            return false;
        Choice!.Choices.Move(by);
        return true;
    }

    /// <summary>C-down pressed: opens the details if <paramref name="open"/> and they aren't showing already.</summary>
    public void PressDetails(bool open) => Details.Press(open);

    /// <summary>C-down let go: the details fade out once they're open.</summary>
    public void ReleaseDetails() => Details.Release();

    /// <summary>Steps the choice's pulse and the details' fade by a tick.</summary>
    public void Step()
    {
        _pulse.Step();
        Details.Advance();
    }
}
