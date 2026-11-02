using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

/// <summary>Steps a menu through its message's fade, which holds the presses until it's open and while it fades out.</summary>
internal static class MenuFades
{
    /// <summary>Steps until the message, if one is fading, has faded in or gone.</summary>
    public static void Settle(MenuOverlay overlay, Action step)
    {
        for (var i = 0; i < 10 && overlay.MessageFade is { State: PopupFadeState.Opening or PopupFadeState.Closing }; i++)
            step();
    }
}
