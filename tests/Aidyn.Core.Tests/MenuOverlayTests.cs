using Aidyn.Core.MapObjects;
using Aidyn.Core.Ui;

namespace Aidyn.Core.Tests;

public class MenuOverlayTests
{
    private static MenuChoice YesNo() => MenuChoice.Question("Sure?", ["Yes", "No"]);

    [Fact]
    public void AnEmptyOverlayTakesNoPresses()
    {
        var overlay = new MenuOverlay();
        Assert.Null(overlay.Top);
        Assert.False(overlay.HasPopup);
        Assert.False(overlay.Dismiss());
        Assert.False(overlay.MoveCursor(1));
    }

    /// <summary>The newest popup takes the press, and closing it reveals the one under it.</summary>
    [Fact]
    public void TheNewestPopupTakesThePressesAndClosingItRevealsTheOneBelow()
    {
        var overlay = new MenuOverlay();
        overlay.Ask(YesNo());
        overlay.ShowHelp("help");
        overlay.Say("message");
        Assert.Equal(MenuPopupLayer.Message, overlay.Top);

        // The stick doesn't reach the question while the message is over it.
        Assert.False(overlay.MoveCursor(1));
        Assert.Equal(0, overlay.Choice!.Choices.Cursor);

        Assert.True(overlay.Dismiss());
        Assert.Null(overlay.Message);
        Assert.Equal(MenuPopupLayer.Help, overlay.Top);

        Assert.True(overlay.Dismiss());
        Assert.Null(overlay.Help);
        Assert.Equal(MenuPopupLayer.Choice, overlay.Top);

        // Neither is Dismiss's to close: the question is answered by its menu.
        Assert.False(overlay.Dismiss());
        Assert.True(overlay.MoveCursor(1));
        Assert.Equal(1, overlay.Choice!.Choices.Cursor);

        overlay.CloseChoice();
        Assert.Null(overlay.Top);
        Assert.False(overlay.HasPopup);
    }

    [Fact]
    public void TheDetailsAreTheLayerUnderTheOthers()
    {
        var overlay = new MenuOverlay();
        overlay.PressDetails(open: true);
        Assert.Equal(MenuPopupLayer.Details, overlay.Top);
        Assert.True(overlay.HasPopup);

        overlay.Say("message");
        Assert.Equal(MenuPopupLayer.Message, overlay.Top);
        overlay.CloseMessage();
        Assert.Equal(MenuPopupLayer.Details, overlay.Top);

        // A press that isn't to open them opens nothing.
        var other = new MenuOverlay();
        other.PressDetails(open: false);
        Assert.False(other.HasPopup);
    }

    /// <summary>The details fade in and, once open and not held, out, on the overlay's own steps.</summary>
    [Fact]
    public void StepFadesTheDetailsAsADetailsPaneDoes()
    {
        var overlay = new MenuOverlay();
        overlay.PressDetails(open: true);
        var alphas = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            overlay.Step();
            alphas.Add(overlay.Details.Alpha);
        }
        Assert.Equal([60, 120, 180, 240, 255], alphas);

        // Held, they stay open; let go, they fade out and are gone.
        overlay.Step();
        Assert.Equal(255, overlay.Details.Alpha);
        overlay.ReleaseDetails();
        overlay.Step();
        Assert.Equal(255, overlay.Details.Alpha);
        alphas.Clear();
        do
        {
            overlay.Step();
            alphas.Add(overlay.Details.Alpha);
        }
        while (overlay.Details.Showing);
        Assert.Equal([195, 135, 75, 15, 0], alphas);
        Assert.False(overlay.HasPopup);
    }

    /// <summary>The question's cursor starts from the first colour when it's asked, and steps a tick at a time.</summary>
    [Fact]
    public void TheQuestionsCursorPulsesFromTheFirstColourWhenAsked()
    {
        var overlay = new MenuOverlay();
        Assert.Equal(MenuPulse.ScrollFrom, overlay.Highlight);
        overlay.Ask(YesNo());
        for (var i = 0; i < 10; i++)
            overlay.Step();
        Assert.NotEqual(MenuPulse.ScrollFrom, overlay.Highlight);

        overlay.Ask(YesNo());
        Assert.Equal(MenuPulse.ScrollFrom, overlay.Highlight);

        var reference = new MenuPulse();
        overlay.Step();
        reference.Step();
        Assert.Equal(reference.Now(MenuPulse.ScrollFrom, MenuPulse.ScrollTo), overlay.Highlight);
    }

    [Fact]
    public void TheQuestionShapeIsWhiteOverTanAnswers()
    {
        var choice = MenuChoice.Question("Train?", ["Yes", "No"]);
        Assert.Equal((MenuMessage.MaxWidth, 0, MenuMessage.White, MenuChoice.Tan), (choice.Width, choice.Gap, choice.Color, choice.ChoiceColor));
        Assert.Equal(["Yes", "No"], choice.Choices.Rows);
    }

    /// <summary>A trek popup is the menus' own message and choice models, with trek's own pulse and wrap.</summary>
    [Fact]
    public void ATrekPopupWrapsTheMenusMessageAndChoice()
    {
        var message = TrekPopup.Message("Hello", 120);
        Assert.Equal(new MenuMessage("Hello", Wrap: 120), message.Body);
        Assert.Equal((120, MenuMessage.Color, 0), (message.Wrap, message.Color, message.Gap));
        Assert.Empty(message.Choices.Rows);

        var damage = TrekPopup.Damage("Ouch");
        Assert.Equal(TrekPopup.White, damage.Color);
        Assert.Equal("Ouch", damage.Text);

        var choice = TrekPopup.Choice("Which?", 180, 10, TrekPopup.PaleGreen, ["A", "B"]);
        Assert.Equal((180, 10, TrekPopup.PaleGreen, TrekPopup.PaleGreen), (choice.Question!.Width, choice.Question.Gap, choice.Question.Color, choice.Question.ChoiceColor));
        Assert.Same(choice.Question.Choices, choice.Choices);

        // Its cursor steps a trek frame, two ticks, where a menu's steps every tick.
        var trek = new MenuPulse(Cadence.FrameTicks);
        for (var i = 0; i < 20; i++)
        {
            choice.Step();
            trek.Step();
            Assert.Equal(trek.Now(MenuPulse.ScrollFrom, MenuPulse.ScrollTo), choice.Highlight);
        }
    }
}
