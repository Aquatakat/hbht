using System.Numerics;
using HBHT.Core.Movement;
using HBHT.Core.Rng;

namespace HBHT.Rom.Tests;

public sealed partial class GameSessionTests
{
    [Fact]
    public void ABodyWhoseEveryTryMissesAtACreaseIsRestedOnTheFloorUnderIt()
    {
        // The Battlefields' G11, where the Lizard King's fight is: good4a, the party leader's spawn on set 4, truncates to the cell
        // (29, 48), in a band about 0.09 wide where a sphere of Alaron's radius swept down meets neither face of a shallow crease.
        var (session, _) = StartIn(16, "G11", 20, 20);
        var chunks = session.Trek.Chunks;
        var origin = chunks.Origin(chunks.ChunkAt(session.Trek.Player.Sphere.Position)!);
        var start = origin + new Vector3(29, 1, 48);
        const float radius = 0.7f;
        Assert.False(Body.SetDown(chunks.World, start, radius, null).Grounded);

        // With the tries to the side, as combat places a body, every one misses for about one seed in 14, which 1.0 leaves a unit
        // up, eight under the floor, to fall through the world.
        var missed = Enumerable.Range(1, 2000).Select(seed => Body.SetDown(chunks.World, start, radius, new Lcg((uint)seed)))
            .Where(s => !s.Grounded).ToList();
        Assert.InRange(missed.Count, 60, 250);

        // The port rests it on the floor under where its tries left it: where a sweep beside the band rests it.
        foreach (var (left, _) in missed)
        {
            var rested = Assert.NotNull(Body.RestOnFloor(chunks.World, left, radius));
            var beside = Body.SetDown(chunks.World, left with { X = left.X + 0.2f }, radius, null);
            Assert.True(beside.Grounded);
            Assert.Equal(beside.Position.Y, rested.Y, 0.05f);
        }
    }
}
