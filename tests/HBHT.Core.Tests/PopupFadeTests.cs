using HBHT.Core.MapObjects;
using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

public class PopupFadeTests
{
    /// <summary>
    /// A popup's alpha as each update steps it before it's drawn (<c>MenuPopup.FadeIn</c>, <c>0x80032840</c>, and
    /// <c>FadeOut</c>, <c>0x800328ac</c>): in, 60, 120, 180, 240 and 255, where it's open; out, 195, 135, 75, 15, and gone.
    /// </summary>
    [Fact]
    public void APopupFadesInAndOutSixtyAStep()
    {
        var fade = new PopupFade();
        var drawn = new List<(int, PopupFadeState)>();
        for (var i = 0; i < 5; i++)
        {
            fade.Advance();
            drawn.Add((fade.Alpha, fade.State));
        }
        Assert.Equal(
        [
            (60, PopupFadeState.Opening), (120, PopupFadeState.Opening), (180, PopupFadeState.Opening),
            (240, PopupFadeState.Opening), (255, PopupFadeState.Open),
        ], drawn);

        fade.Advance();
        Assert.Equal((255, PopupFadeState.Open), (fade.Alpha, fade.State));

        fade.Close();
        drawn.Clear();
        while (!fade.IsGone)
        {
            fade.Advance();
            drawn.Add((fade.Alpha, fade.State));
        }
        Assert.Equal(
        [
            (195, PopupFadeState.Closing), (135, PopupFadeState.Closing), (75, PopupFadeState.Closing),
            (15, PopupFadeState.Closing), (0, PopupFadeState.Gone),
        ], drawn);
    }

    /// <summary>
    /// A trek message fades as the pause menu's do, 90 a step, but a trek frame a step: drawn at 90 as it's made, then
    /// 180 and 255, where it's open; closed, 165 at once, then 75, and gone.
    /// </summary>
    [Fact]
    public void ATrekMessageFadesNinetyATrekFrame()
    {
        var popup = TrekPopup.Message("a");
        var drawn = new List<int> { popup.Alpha };
        for (var frame = 0; frame < 2; frame++)
        {
            for (var tick = 0; tick < Cadence.FrameTicks; tick++)
                popup.Step();
            drawn.Add(popup.Alpha);
        }
        Assert.Equal([90, 180, 255], drawn);
        Assert.True(popup.IsOpen);

        popup.Close();
        drawn = [popup.Alpha];
        for (var tick = 0; tick < 2 * Cadence.FrameTicks; tick++)
        {
            popup.Step();
            if (tick % Cadence.FrameTicks == Cadence.FrameTicks - 1)
                drawn.Add(popup.Alpha);
        }
        Assert.Equal([165, 75, 0], drawn);
        Assert.True(popup.IsGone);
    }

    /// <summary>
    /// A menu's question fades as a message does, 90 an update, where 1.0's has no fade, the user's choice. Answered, it's
    /// gone from the overlay at once, and fades out from under it.
    /// </summary>
    [Fact]
    public void AMenusQuestionFadesInAndOutNinetyAnUpdate()
    {
        var overlay = new MenuOverlay();
        var choice = MenuChoice.Question("a", ["b", "c"]);
        overlay.Ask(choice);
        var drawn = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            overlay.Step();
            drawn.Add(overlay.Question.Alpha);
        }
        Assert.Equal([90, 180, 255], drawn);
        Assert.False(overlay.Question.Fading);

        overlay.CloseChoice();
        Assert.Null(overlay.Choice);
        Assert.False(overlay.HasPopup);
        Assert.Same(choice, overlay.Question.Leaving);
        drawn = [];
        for (var i = 0; i < 3; i++)
        {
            overlay.Step();
            drawn.Add(overlay.Question.LeavingAlpha);
        }
        Assert.Equal([165, 75, 0], drawn);
        Assert.Null(overlay.Question.Leaving);
    }

    /// <summary>The trap's question fades as a message does, where 1.0's has no fade, the user's choice.</summary>
    [Fact]
    public void ATrekQuestionFadesAsAMessageDoes()
    {
        var popup = TrekPopup.Choice("a", 180, 10, TrekPopup.PaleGreen, ["b", "c"]);
        var drawn = new List<int> { popup.Alpha };
        for (var tick = 0; tick < 2 * Cadence.FrameTicks; tick++)
        {
            popup.Step();
            if (tick % Cadence.FrameTicks == Cadence.FrameTicks - 1)
                drawn.Add(popup.Alpha);
        }
        Assert.Equal([90, 180, 255], drawn);
        Assert.True(popup.IsOpen);
    }
}
