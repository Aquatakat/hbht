using HBHT.Core.Ui;

namespace HBHT.Core.Tests;

/// <summary>Held directions repeat on the original's ticks: 30, then every 5 (<c>BaseWidget::Control</c>).</summary>
public sealed class StickRepeatTests
{
    private static List<(int Tick, MenuAction Direction)> Hold(StickRepeat stick, int ticks, Action<int>? between = null)
    {
        var fired = new List<(int, MenuAction)>();
        for (var tick = 1; tick <= ticks; tick++)
        {
            between?.Invoke(tick);
            stick.Tick(d => fired.Add((tick, d)));
        }
        return fired;
    }

    [Fact]
    public void AHeldDirectionRepeatsAfterThirtyTicksAndThenEveryFive()
    {
        var stick = new StickRepeat();
        stick.Press(MenuAction.Down);
        Assert.True(stick.Held(MenuAction.Down));
        var fired = Hold(stick, 45);
        Assert.Equal([(30, MenuAction.Down), (35, MenuAction.Down), (40, MenuAction.Down), (45, MenuAction.Down)], fired);
    }

    [Fact]
    public void LettingGoStopsItAndPressingAgainWaitsTheWholeDelay()
    {
        var stick = new StickRepeat();
        stick.Press(MenuAction.Up);
        Hold(stick, 32);
        stick.Release(MenuAction.Up);
        Assert.False(stick.Held(MenuAction.Up));
        Assert.Empty(Hold(stick, 10));
        stick.Press(MenuAction.Up);
        Assert.Equal([(30, MenuAction.Up)], Hold(stick, 30));
    }

    [Fact]
    public void AKeyIsHeldButRepeatsOnlyAsTheSystemRepeatsIt()
    {
        var stick = new StickRepeat();
        stick.Press(MenuAction.Right, repeats: false);
        Assert.True(stick.Held(MenuAction.Right));
        Assert.Empty(Hold(stick, 60));
    }

    [Fact]
    public void TwoDirectionsRepeatEachOnTheirOwnTicksInUpDownLeftRightOrder()
    {
        var stick = new StickRepeat();
        stick.Press(MenuAction.Left);
        var fired = Hold(stick, 32, tick =>
        {
            if (tick == 3)
                stick.Press(MenuAction.Up);
        });
        // Left was pressed 2 ticks before up, so it fires 2 ticks earlier; on a shared tick up would come first.
        Assert.Equal([(30, MenuAction.Left), (32, MenuAction.Up)], fired);
        stick.Press(MenuAction.Down);
        stick.Press(MenuAction.Right);
        var together = new List<MenuAction>();
        for (var i = 0; i < 30; i++)
            stick.Tick(together.Add);
        Assert.Equal([MenuAction.Left, MenuAction.Up, MenuAction.Left, MenuAction.Up, MenuAction.Left, MenuAction.Up, MenuAction.Left, MenuAction.Up, MenuAction.Left, MenuAction.Up, MenuAction.Left, MenuAction.Up, MenuAction.Down, MenuAction.Right],
            together);
    }

    [Fact]
    public void ClearForgetsEverythingAndOnlyDirectionsAreHeld()
    {
        var stick = new StickRepeat();
        stick.Press(MenuAction.Accept);
        Assert.False(stick.Held(MenuAction.Accept));
        stick.Press(MenuAction.Down);
        stick.Press(MenuAction.Left);
        stick.Clear();
        Assert.False(stick.Held(MenuAction.Down) || stick.Held(MenuAction.Left));
        Assert.Empty(Hold(stick, 40));
    }

    [Fact]
    public void ARepeatThatClearsTheStickDoesNotBringTheDirectionBack()
    {
        var stick = new StickRepeat();
        stick.Press(MenuAction.Down);
        for (var i = 0; i < 30; i++)
            stick.Tick(_ => stick.Clear());
        Assert.False(stick.Held(MenuAction.Down));
    }
}
