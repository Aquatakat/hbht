using Aidyn.Core.Ui;

namespace Aidyn.Core.Tests;

public class PauseScrollTests
{
    /// <summary>
    /// The scroll's actor as the menu steps it: a request enters its state on the first frame, holds the clock on the
    /// next as the request goes back to 0, and then counts to the state's length, where it goes back to idle.
    /// </summary>
    private sealed class Scroll(Dictionary<int, int> lengths)
    {
        private int _lastRequest;
        public int State, Clock = 1;
        public int Length => lengths[State];

        public void Step(int request)
        {
            var changed = request != _lastRequest;
            _lastRequest = request;
            if (Clock >= Length)
                (State, Clock) = (0, 1);
            else if (changed && request != 0)
                (State, Clock) = (request, 1);
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
        menu.Step(scroll.Clock, scroll.Length);
        if (menu.StepsScroll)
            scroll.Step(menu.TakeRequest());
        return menu.Clip;
    }

    [Fact]
    public void TheWorldDarkensTenAFrameToHalfBeforeTheMenuIsBuilt()
    {
        var menu = new PauseScroll(tab: 1);
        var tints = new List<int>();
        while (menu.Phase == PauseScrollPhase.Darkening)
        {
            Assert.False(menu.ShowsMenu);
            menu.Step(1, 11);
            tints.Add(menu.Tint);
        }
        Assert.Equal([245, 235, 225, 215, 205, 195, 185, 175, 165, 155, 145, 135, 128, 128], tints);
        Assert.True(menu.ShowsMenu);
        Assert.Equal(10, menu.TakeRequest());
        Assert.Equal(0, menu.TakeRequest());
    }

    [Fact]
    public void PartyUnrollsFromTheMiddleAndTakesInputWhenTheAnimationEnds()
    {
        var menu = new PauseScroll(tab: 1);
        var scroll = NewScroll();
        while (menu.Phase == PauseScrollPhase.Darkening)
            Frame(menu, scroll);
        var clips = new List<(int, int)?>();
        while (menu.Phase == PauseScrollPhase.Unrolling)
            clips.Add(Frame(menu, scroll));
        // Nothing until frame 37 of the 53, then wider each frame to the original's last, 17 to 303.
        Assert.Equal(54, clips.Count);
        Assert.All(clips.Take(37), c => Assert.Equal((160, 160), c));
        Assert.Equal((152, 168), clips[37]);
        Assert.Equal((17, 303), clips[^1]);
        // The port carries on to the frame's edges, and then shows the whole screen.
        Assert.Equal((9, 311), Frame(menu, scroll));
        Assert.Equal((0, 320), Frame(menu, scroll));
        Assert.Null(Frame(menu, scroll));
        Assert.Equal(0, scroll.State);
    }

    [Fact]
    public void SystemUnrollsFromTheRightAndWorldFromTheLeft()
    {
        foreach (var (tab, first, last) in new[] { (0, (320, 320), (-11, 320)), (2, (0, -5), (0, 342)) })
        {
            var menu = new PauseScroll(tab);
            var scroll = NewScroll();
            while (menu.Phase == PauseScrollPhase.Darkening)
                Frame(menu, scroll);
            var clips = new List<(int, int)?>();
            while (menu.Phase == PauseScrollPhase.Unrolling)
                clips.Add(Frame(menu, scroll));
            Assert.Equal(first, clips[0]);
            Assert.Equal(last, clips[^1]);
            Assert.Null(Frame(menu, scroll));
        }
    }

    [Fact]
    public void ClosingRollsUpThenBrightensTenAFrame()
    {
        var menu = new PauseScroll(tab: 1);
        var scroll = NewScroll();
        while (menu.Phase != PauseScrollPhase.Open)
            Frame(menu, scroll);
        for (var i = 0; i < 20; i++)
            Frame(menu, scroll);
        menu.Close(tab: 1);
        scroll.Step(menu.TakeRequest());
        var clips = new List<(int, int)?>();
        while (menu.Phase == PauseScrollPhase.Rolling)
            clips.Add(Frame(menu, scroll));
        // To the middle over 13 frames, and gone after that.
        Assert.Null(clips[0]);
        Assert.Equal((12, 308), clips[2]);
        Assert.True(clips[14] is var (l, r) && l >= r);
        // The scroll stops on its last frame, and the world brightens back in 13 frames, the first with the last roll.
        Assert.Equal(54, clips.Count);
        Assert.Equal(138, menu.Tint);
        var tints = new List<int> { menu.Tint };
        while (menu.Phase == PauseScrollPhase.Brightening)
        {
            Assert.False(menu.StepsScroll);
            Frame(menu, scroll);
            tints.Add(menu.Tint);
        }
        Assert.Equal([138, 148, 158, 168, 178, 188, 198, 208, 218, 228, 238, 248, 255, 255], tints);
        Assert.Equal(PauseScrollPhase.Closed, menu.Phase);
    }

    [Fact]
    public void SystemRollsUpFromTheLeftAndWorldFromTheRight()
    {
        foreach (var (tab, at10) in new[] { (0, (96, 320)), (2, (0, 235)) })
        {
            var menu = new PauseScroll(tab);
            var scroll = NewScroll();
            while (menu.Phase != PauseScrollPhase.Open)
                Frame(menu, scroll);
            menu.Close(tab);
            scroll.Step(menu.TakeRequest());
            var clips = new List<(int, int)?>();
            while (menu.Phase == PauseScrollPhase.Rolling)
                clips.Add(Frame(menu, scroll));
            Assert.Equal(at10, clips[10]);
        }
    }

    [Fact]
    public void TheLootWindowSnapsBackToFullBrightness()
    {
        var menu = new PauseScroll(tab: 1, brightens: false);
        var scroll = NewScroll();
        while (menu.Phase != PauseScrollPhase.Open)
            Frame(menu, scroll);
        menu.Close(tab: 1);
        scroll.Step(menu.TakeRequest());
        while (menu.Phase == PauseScrollPhase.Rolling)
            Frame(menu, scroll);
        Assert.Equal(PauseScrollPhase.Closed, menu.Phase);
        Assert.Equal(255, menu.Tint);
    }
}
