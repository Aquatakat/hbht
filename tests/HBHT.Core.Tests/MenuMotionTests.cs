using HBHT.Core.Scripts;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// The menus' widget fades, pulses and tweens, tick by tick, against the arithmetic each was written with: the fade's
/// 60 a step (<c>MenuPopup.FadeIn</c>, <c>0x80032840</c>, <c>FadeOut</c>, <c>0x800328ac</c>), the dialogue box's 24-step
/// pulse and the frame counter every tween shares.
/// </summary>
public class MenuMotionTests
{
    // The fade as every owner first wrote it.
    private static int In(int alpha) => alpha < 195 ? alpha + 60 : 255;

    private static int Out(int alpha) => alpha < 61 ? 0 : alpha - 60;

    /// <summary>How many steps a fade can have opened for before it closes.</summary>
    private static IEnumerable<int> OpenedFor() => Enumerable.Range(0, 7);

    private static List<int> Expected(int openSteps, int closeSteps)
    {
        List<int> alphas = [];
        var alpha = 0;
        for (var i = 0; i < openSteps; i++)
            alphas.Add(alpha = In(alpha));
        for (var i = 0; i < closeSteps; i++)
            alphas.Add(alpha = Out(alpha));
        return alphas;
    }

    [Fact]
    public void TheJournalsBookFadesAsTheWidgetsDoFromWhereverItCloses()
    {
        var entries = new List<(string, bool)> { ("a", false) };
        foreach (var open in OpenedFor())
        {
            var journal = new JournalMenu(new TextLayout(TextLayoutTests.Font()), entries, "none");
            List<int> alphas = [];
            for (var i = 0; i < open; i++)
            {
                journal.Step();
                alphas.Add(journal.Alpha);
            }
            journal.Close();
            for (var i = 0; i < 8; i++)
            {
                journal.Step();
                alphas.Add(journal.Alpha);
            }
            Assert.Equal(Expected(open, 8), alphas);
            Assert.True(journal.Closed);
        }
    }

    [Fact]
    public void ABackdropPageFadesAsTheWidgetsDoFromWhereverItCloses()
    {
        foreach (var open in OpenedFor())
        {
            var page = new BackdropPage(new TextLayout(TextLayoutTests.Font()), BackdropStyle.For(0));
            List<int> alphas = [];
            List<bool> closed = [];
            for (var i = 0; i < open; i++)
            {
                page.Step();
                alphas.Add(page.Alpha);
            }
            page.Close();
            for (var i = 0; i < 8; i++)
            {
                page.Step();
                alphas.Add(page.Alpha);
                closed.Add(page.Closed);
            }
            Assert.Equal(Expected(open, 8), alphas);
            Assert.Equal(alphas.Skip(open).Select(a => a == 0), closed);
        }
    }

    [Fact]
    public void APopupFadesAsTheWidgetsDoFromWhereverItCloses()
    {
        foreach (var open in OpenedFor())
        {
            var fade = new PopupFade();
            List<int> alphas = [];
            for (var i = 0; i < open; i++)
            {
                fade.Advance();
                alphas.Add(fade.Alpha);
            }
            fade.Close();
            for (var i = 0; i < 8; i++)
            {
                fade.Advance();
                alphas.Add(fade.Alpha);
            }
            Assert.Equal(Expected(open, 8), alphas);
            Assert.True(fade.IsGone);
        }
    }

    // The dialogue box's pulse as it was written: a step every FrameTicks ticks, 0 up to 23 and back, which a new page
    // restarts but not the ticks counting toward the next step.
    [Fact]
    public void TheDialogueBoxsPulseRestartsOnANewPageWithoutRestartingTheFrame()
    {
        const int frames = 24;
        var box = new DialogueBox(new TextLayout(TextLayoutTests.Font()), 52, 52, RecordId.Hex(RecordTypes.Entity, 0x0240));
        var page = new ScriptPage(new ScriptLine(0, null, "a"), [new ScriptLine(1, null, "b"), new ScriptLine(2, null, "c")]);
        box.Show(page);
        int pulse = 0, sign = 1, ticks = 0;
        for (var tick = 0; tick < 5 * frames * Cadence.FrameTicks; tick++)
        {
            if (tick % 37 == 36)
            {
                box.Show(page);
                (pulse, sign) = (0, 1);
            }
            box.Step();
            if (++ticks >= Cadence.FrameTicks)
            {
                ticks = 0;
                pulse += sign;
                if (pulse == 0 || pulse >= frames - 1)
                    sign = -sign;
            }
            var (from, to) = (DialogueBox.ChoiceColor, DialogueBox.HighlightColor);
            byte Blend(byte f, byte t) => (byte)(f + (t - f) / frames * pulse);
            Assert.Equal(new Rgba(Blend(from.R, to.R), Blend(from.G, to.G), Blend(from.B, to.B), Blend(from.A, to.A)), box.HighlightNow);
        }
    }
}
