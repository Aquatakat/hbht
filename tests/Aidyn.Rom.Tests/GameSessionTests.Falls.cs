using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Movement;
using Aidyn.Core.Trek;

namespace Aidyn.Rom.Tests;

/// <summary>Falling out of the world, on NA 1.0's Overworld: the original's set-down, then the port's breadcrumbs, then the original's lift.</summary>
public sealed partial class GameSessionTests
{
    /// <summary>Ticks until trek has put the player back, and says how.</summary>
    private static FallRescue Fall(GameSession session, Vector3 to)
    {
        var trek = session.Trek;
        trek.Player.Sphere.Position = to;
        trek.Player.Sphere.Velocity = Vector3.Zero;
        var before = trek.LastRescue;
        var count = 0;
        for (var i = 0; i < 600 && !(trek.Player.FellOut && count > 0); i++)
        {
            Tick(session);
            if (trek.Player.FellOut)
                count++;
        }
        Assert.Equal(1, count);
        return trek.LastRescue!.Value;
    }

    [Fact]
    public void FallingOutIsSetDownThenBackAtABreadcrumbThenLiftedAndAnotherKeptStartsItOver()
    {
        var (session, host) = Start();
        var trek = session.Trek;
        var ground = trek.Player.Sphere.Position;
        Assert.All(trek.Breadcrumbs.All, c => Assert.True(c.Grounded));

        // A long drop, faster than 0.3 a tick: set down where he'd have landed, as the original does.
        Assert.Equal(FallRescue.SetDown, Fall(session, ground + new Vector3(0, 40, 0)));
        Assert.Equal(ground.Y, trek.Player.Sphere.Position.Y, 0.05f);
        Assert.Equal(new Vector2(ground.X, ground.Z), new Vector2(trek.Player.Sphere.Position.X, trek.Player.Sphere.Position.Z));

        // Out again with no breadcrumb kept since: he may have fallen through where it put him, so the newest breadcrumb.
        host.Transitions.Clear();
        Assert.Equal(FallRescue.Breadcrumb, Fall(session, ground with { Y = -60 }));
        Assert.Equal(ground, trek.Player.Sphere.Position);
        Assert.IsType<Transition.Say>(Assert.Single(host.Transitions));

        // All 15 are that one place, now tried: the original's lift, which finds the ground here.
        Assert.Equal(FallRescue.Lifted, Fall(session, ground with { Y = -60 }));
        Assert.Equal(ground.Y, trek.Player.Sphere.Position.Y, 0.05f);

        // A breadcrumb kept since starts it over with a set-down.
        var kept = trek.Breadcrumbs.Kept;
        for (var i = 0; i < 2 * Breadcrumbs.Ticks && trek.Breadcrumbs.Kept == kept; i++)
        {
            trek.Player.Sphere.Position = ground + new Vector3(0, 12, 0);
            trek.Player.Sphere.Velocity = Vector3.Zero;
            Tick(session);
        }
        Assert.NotEqual(kept, trek.Breadcrumbs.Kept);
        Assert.Equal(FallRescue.SetDown, Fall(session, ground with { Y = -60 }));
        Assert.Equal(ground.Y, trek.Player.Sphere.Position.Y, 0.05f);
        Assert.True(PlayerMotion.Floor < trek.Player.Sphere.Position.Y);
    }
}
