namespace Aidyn.Core.Ui;

/// <summary>Where a <see cref="PopupFade"/> stands.</summary>
public enum PopupFadeState
{
    Opening,
    Open,
    Closing,
    Gone,
}

/// <summary>
/// How a parchment popup fades in and out: the Party tab's skill popups and C-down's details, in the tab, the loot window,
/// a shop and training (<c>MenuPopup</c>, <c>itemdetail.cpp</c>). It opens at alpha 0 (<c>MenuPopup.Open</c>,
/// <c>0x800336a0</c>), and each menu update, a tick, steps it before it's drawn: in, 60 more until it's 195 or more and
/// then 255, where it's open (<c>MenuPopup.FadeIn</c>, <c>0x80032840</c>), so it's drawn at 60, 120, 180, 240 and 255;
/// out, 60 less until it's under 61 and then gone, freed before it's drawn at 0 (<c>MenuPopup.FadeOut</c>,
/// <c>0x800328ac</c>), so from open it's drawn at 195, 135, 75 and 15. The steps are <see cref="WidgetFade"/>'s.
/// </summary>
/// <remarks>
/// The alpha is the whole popup's: <c>Widget.SetAlpha</c> (<c>0x800b2928</c>) sets it on the parchment, its shadow, the
/// title, the rows, the icons and the cursor's row, whose pulse it multiplies (<c>0x800afe88</c>). Input reaches only
/// the newest widget, and only once it's open (<c>WidgetManager.Input</c>, <c>0x800ad2d4</c>), so no press reaches the
/// popup or the tab under it while it fades either way.
/// </remarks>
public sealed class PopupFade
{
    /// <summary>The popup's alpha now, from 0 to 255.</summary>
    public int Alpha { get; private set; }

    public PopupFadeState State { get; private set; } = PopupFadeState.Opening;

    /// <summary>Whether it's fully open, and so takes the presses.</summary>
    public bool IsOpen => State == PopupFadeState.Open;

    public bool IsGone => State == PopupFadeState.Gone;

    /// <summary>Starts it fading out, from where it is.</summary>
    public void Close()
    {
        if (State is PopupFadeState.Opening or PopupFadeState.Open)
            State = PopupFadeState.Closing;
    }

    /// <summary>Steps it by a menu update.</summary>
    public void Advance()
    {
        switch (State)
        {
            case PopupFadeState.Opening:
                Alpha = WidgetFade.In(Alpha);
                if (Alpha == WidgetFade.Opaque)
                    State = PopupFadeState.Open;
                break;
            case PopupFadeState.Closing:
                Alpha = WidgetFade.Out(Alpha);
                if (Alpha == 0)
                    State = PopupFadeState.Gone;
                break;
        }
    }
}

/// <summary>
/// C-down's details as they fade (<see cref="PopupFade"/>): pressing C-down opens them, and once they're open, they fade
/// out whenever it's not held (<c>PartyMenu.CloseDetails</c>, <c>0x80034c18</c>, which tests it only then). So a quick
/// tap still fades them fully in, then straight out.
/// </summary>
public sealed class DetailsPane
{
    /// <summary>The details' fade, while they show.</summary>
    public PopupFade? Fade { get; private set; }

    /// <summary>Whether C-down is held.</summary>
    public bool Held { get; private set; }

    /// <summary>Whether the details show, fading or not.</summary>
    public bool Showing => Fade is not null;

    /// <summary>Their alpha now, 0 if they don't show.</summary>
    public int Alpha => Fade?.Alpha ?? 0;

    /// <summary>C-down pressed: opens the details if <paramref name="open"/> and they aren't showing already.</summary>
    public void Press(bool open)
    {
        Held = true;
        if (open && Fade is null)
            Fade = new PopupFade();
    }

    /// <summary>C-down let go.</summary>
    public void Release() => Held = false;

    /// <summary>Steps the fade by a menu update, and starts it out if they're open and C-down isn't held.</summary>
    public void Advance()
    {
        if (Fade is not { } fade)
            return;
        fade.Advance();
        if (fade.IsGone)
            Fade = null;
        else if (fade.IsOpen && !Held)
            fade.Close();
    }
}
