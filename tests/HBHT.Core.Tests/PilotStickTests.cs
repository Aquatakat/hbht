using System.Numerics;
using HBHT.Core.Playthrough;

namespace HBHT.Core.Tests;

/// <summary>The playthrough pilot's stick: the way it wants him to go, through the camera's look, as the player's motion reads it back.</summary>
public sealed class PilotStickTests
{
    [Fact]
    public void TheStickForAWayIsTheOneThePlayersMotionTurnsBackToIt()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var look = Vector2.Normalize(new Vector2(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1));
            var way = Vector2.Normalize(new Vector2(random.NextSingle() * 2 - 1, random.NextSingle() * 2 - 1));
            var stick = Pilot.StickFor(way, look);
            Assert.Equal(1, stick.Length(), 4);
            // PlayerMotion.Control: up on the stick is the look, right its right (Facing.Rotate of (−x, y) by the look).
            var moved = Vector2.Normalize(Facing.Rotate(new Vector2(-stick.X, stick.Y), look.X, look.Y));
            Assert.True(Vector2.Distance(way, moved) < 1e-4f, $"{way} came back {moved} through {look}");
        }
        // Up on the stick goes the way the camera looks.
        Assert.Equal(Vector2.UnitY, Pilot.StickFor(new Vector2(0.6f, 0.8f), new Vector2(0.6f, 0.8f)));
    }
}
