using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

public class PauseScrollTests
{
    /// <summary>
    /// The scroll's actor as the menu steps it: a request enters its state on the first frame, holds the clock on the
    /// next as the request goes back to 0, and then counts to the state's length, where it goes to the request's
    /// state, idle for 0. A join enters the request's state at its frame.
    /// </summary>
    private sealed class Scroll(Dictionary<int, int> lengths)
    {
        private int _lastRequest;
        public int State, Clock = 1;
        public int Length => lengths[State];

        public void Play(ScrollCue cue)
        {
            if (cue.Join > 0)
            {
                (State, Clock) = (cue.Request, cue.Join);
                return;
            }
            var changed = cue.Request != _lastRequest;
            _lastRequest = cue.Request;
            if (Clock >= Length)
                (State, Clock) = (cue.Request, 1);
            else if (changed && cue.Request != 0)
                (State, Clock) = (cue.Request, 1);
            else if (!changed)
                Clock++;
        }
    }

    // The actor's states by request: idle 11 frames, open on System or World 37 and on Party 53, close from Party 53
    // and from System or World 39.
    private static Scroll NewScroll() => new(new() { [0] = 11, [22] = 37, [10] = 53, [19] = 53, [11] = 39 });

    /// <summary>Steps a frame as the menu does, returning the clip it shows.</summary>
    private static (int, int)? Frame(PauseScroll menu, Scroll scroll)
    {
        var cue = menu.Step(scroll.Clock, scroll.Length);
        if (menu.StepsScroll)
            scroll.Play(cue);
        return menu.Clip;
    }

    private static (PauseScroll, Scroll) Opened(int tab, bool holds = false, bool brightens = true)
    {
        var menu = new PauseScroll(tab, brightens, holds);
        var scroll = NewScroll();
        while (menu.Phase != PauseScrollPhase.Open)
            Frame(menu, scroll);
        return (menu, scroll);
    }

    /// <summary>Closes the menu as it does, stepping the scroll into its closing at once.</summary>
    private static void Close(PauseScroll menu, Scroll scroll, int tab)
    {
        scroll.Play(menu.Close(tab));
    }

    [Fact]
    public void TheWorldDarkensTenAFrameToHalfBeforeTheMenuIsBuilt()
    {
        var menu = new PauseScroll(tab: 1);
        var tints = new List<int>();
        ScrollCue cue = default;
        while (menu.Phase == PauseScrollPhase.Darkening)
        {
            Assert.False(menu.ShowsMenu);
            cue = menu.Step(1, 11);
            tints.Add(menu.Tint);
        }
        Assert.Equal([245, 235, 225, 215, 205, 195, 185, 175, 165, 155, 145, 135, 128, 128], tints);
        Assert.True(menu.ShowsMenu);
        // The tabs show from this frame, cut as the opening starts: Party's to nothing.
        Assert.Equal((160, 160), menu.Clip);
        // The frame the tabs show from cued the scroll's opening; the frames before cued nothing.
        Assert.Equal(new ScrollCue(10), cue);
    }

    [Fact]
    public void PartyUnrollsFromTheMiddleAndTakesInputWhenItsAnimationEnds()
    {
        var menu = new PauseScroll(tab: 1);
        var scroll = NewScroll();
        while (menu.Phase == PauseScrollPhase.Darkening)
            Frame(menu, scroll);
        var clips = new List<(int, int)?>();
        var alphas = new List<float>();
        while (menu.Phase == PauseScrollPhase.Unrolling)
        {
            clips.Add(Frame(menu, scroll));
            alphas.Add(menu.Alpha);
        }
        // Nothing until frame 37 of the 53, then wider each frame to the original's last, 17 to 303, which is in the
        // overscan, so the whole screen.
        Assert.Equal(54, clips.Count);
        Assert.All(clips.Take(37), c => Assert.Equal((160, 160), c));
        Assert.Equal((152, 168), clips[37]);
        Assert.Equal((26, 294), clips[^2]);
        Assert.Null(clips[^1]);
        // It fades in over its first 12 frames as it rises into the frame (the first shown twice).
        Assert.Equal(1 / 12f, alphas[0], 4);
        Assert.Equal(11 / 12f, alphas[11], 4);
        Assert.All(alphas.Skip(12), a => Assert.Equal(1, a));
    }

    [Fact]
    public void PartyRunsOnIntoTheOtherTabsOpeningAtItsTenthFrame()
    {
        var (menu, scroll) = Opened(tab: 1);
        // Its last 9 frames are their first 9.
        Assert.Equal((22, 10), (scroll.State, scroll.Clock));
        Assert.True(menu.StepsScroll);
        var frames = 0;
        while (scroll.State == 22)
        {
            Assert.Null(Frame(menu, scroll));
            frames++;
        }
        Assert.Equal(28, frames);
        Assert.Equal(0, scroll.State);
    }

    [Fact]
    public void BarterAndTrainingRunOnAFrameAndHold()
    {
        var (menu, scroll) = Opened(tab: 1, holds: true);
        Assert.Equal((22, 10), (scroll.State, scroll.Clock));
        for (var i = 0; i < 20; i++)
            Assert.Null(Frame(menu, scroll));
        Assert.Equal((22, 11), (scroll.State, scroll.Clock));
        Assert.False(menu.StepsScroll);
        // Closing turns it back, as from the Party tab.
        Close(menu, scroll, tab: 1);
        Assert.Equal((11, 29), (scroll.State, scroll.Clock));
    }

    [Fact]
    public void SystemUnrollsFromTheRightAndWorldFromTheLeft()
    {
        foreach (var (tab, first, last) in new (int, (int, int)?, (int, int)?)[] { (0, (320, 320), null), (2, (0, -5), null) })
        {
            var menu = new PauseScroll(tab);
            var scroll = NewScroll();
            while (menu.Phase == PauseScrollPhase.Darkening)
                Frame(menu, scroll);
            var clips = new List<(int, int)?>();
            while (menu.Phase == PauseScrollPhase.Unrolling)
            {
                clips.Add(Frame(menu, scroll));
                Assert.Equal(1, menu.Alpha);
            }
            Assert.Equal(first, clips[0]);
            Assert.Equal(last, clips[^1]);
            Assert.Null(Frame(menu, scroll));
        }
    }

    [Fact]
    public void ClosingFromPartyRunsTheOtherTabsClosingIntoThePartyTabsAndThenBrightens()
    {
        var (menu, scroll) = Opened(tab: 1);
        for (var i = 0; i < 40; i++)
            Frame(menu, scroll);
        Close(menu, scroll, tab: 1);
        Assert.Equal((11, 1), (scroll.State, scroll.Clock));
        var clips = new List<(int, int)?>();
        var alphas = new List<float>();
        while (menu.Phase == PauseScrollPhase.Rolling)
        {
            clips.Add(Frame(menu, scroll));
            alphas.Add(menu.Alpha);
            // Their frames 31 to 39 are the Party tab's 1 to 9.
            if (scroll.State == 19 && scroll.Clock == 1)
                Assert.Equal(31, clips.Count);
        }
        // Their closing, its first frame shown twice, the rollers coming in to the frame's edges, with the tabs between.
        Assert.All(clips.Take(31), Assert.Null);
        // Then the Party tab's: to the middle over 13 frames, and gone after that.
        var party = clips.Skip(31).ToList();
        Assert.Null(party[0]);
        // Its first frame's 12 to 308 is in the overscan.
        Assert.Null(party[1]);
        Assert.Equal((24, 296), party[2]);
        Assert.True(party[13] is var (l, r) && l >= r);
        Assert.Equal(53, party.Count);
        // It fades out as it drops out of the frame, from its 41st frame.
        Assert.All(alphas.Take(31 + 41), a => Assert.Equal(1, a));
        Assert.Equal(6 / 12f, alphas[31 + 46], 4);
        Assert.Equal(0, alphas[^1]);
        // The scroll stops on its last frame, and the world brightens back in 13 frames, the first with the last roll.
        Assert.Equal(138, menu.Tint);
        var tints = new List<int> { menu.Tint };
        while (menu.Phase == PauseScrollPhase.Brightening)
        {
            Assert.False(menu.StepsScroll);
            Assert.False(menu.ShowsMenu);
            Frame(menu, scroll);
            tints.Add(menu.Tint);
        }
        Assert.Equal([138, 148, 158, 168, 178, 188, 198, 208, 218, 228, 238, 248, 255, 255], tints);
        Assert.Equal(PauseScrollPhase.Closed, menu.Phase);
    }

    [Fact]
    public void ClosingFromPartyWhileItRunsOnTurnsItsOpeningBack()
    {
        var (menu, scroll) = Opened(tab: 1);
        for (var i = 0; i < 5; i++)
            Frame(menu, scroll);
        Assert.Equal((22, 15), (scroll.State, scroll.Clock));
        Close(menu, scroll, tab: 1);
        Assert.Equal((11, 25), (scroll.State, scroll.Clock));
    }

    [Fact]
    public void SystemRollsUpFromTheLeftAndWorldFromTheRightFadingAsTheirRollersLeaveTheFrame()
    {
        foreach (var (tab, at10) in new[] { (0, (96, 320)), (2, (0, 235)) })
        {
            var (menu, scroll) = Opened(tab);
            Close(menu, scroll, tab);
            var clips = new List<(int, int)?>();
            var alphas = new List<float>();
            while (menu.Phase == PauseScrollPhase.Rolling)
            {
                clips.Add(Frame(menu, scroll));
                alphas.Add(menu.Alpha);
            }
            Assert.Equal(at10, clips[10]);
            // The first frame is shown twice, so frame 29 is the 30th.
            Assert.All(alphas.Take(30), a => Assert.Equal(1, a));
            Assert.Equal(0.5f, alphas[34], 4);
            Assert.Equal(0, alphas[^1]);
            Assert.Equal(40, alphas.Count);
        }
    }

    [Fact]
    public void TheLootWindowSnapsBackToFullBrightness()
    {
        var (menu, scroll) = Opened(tab: 1, holds: true, brightens: false);
        Close(menu, scroll, tab: 1);
        while (menu.Phase == PauseScrollPhase.Rolling)
            Frame(menu, scroll);
        Assert.Equal(PauseScrollPhase.Closed, menu.Phase);
        Assert.Equal(255, menu.Tint);
    }
}
