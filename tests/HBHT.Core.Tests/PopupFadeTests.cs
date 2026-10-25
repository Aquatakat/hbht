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
}
