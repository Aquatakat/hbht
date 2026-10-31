using System.Numerics;
using HBHT.Core;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The cut-scene comparison (<see cref="MovementCompare"/>, docs/systems/movement.md#the-cut-scene-comparison): every cut-scene
/// played under the original's movement and Modern's, and what it found, pinned so a change to either movement shows here.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class MovementCompareTests(Na10Import fixture, ITestOutputHelper output)
{
    private static IReadOnlyList<MovementCompare.Difference>? _differences;

    private IReadOnlyList<MovementCompare.Difference> Differences()
    {
        if (_differences is not null)
            return _differences;
        var plugins = PluginSet.Load([fixture.Data.Dir]);
        var differences = MovementCompare.Run(plugins, MovementCompare.Launches(plugins), new MovementCompare.Options(), out var skipped);
        Assert.Empty(skipped);
        return _differences = differences;
    }

    private static RecordId Script(int index) => RecordId.Hex(RecordTypes.Dialogue, index);

    private IEnumerable<MovementCompare.Difference> Of(MovementCompare.Severity severity) => Differences().Where(d => d.Severity == severity);

    [Fact]
    public void Every_cut_scene_ends_under_both_and_most_alike()
    {
        var differences = Differences();
        foreach (var group in differences.GroupBy(d => d.Severity))
            output.WriteLine($"{group.Key}: {group.Count()}");
        Assert.Equal(262, differences.Count);
        Assert.All(differences, d => Assert.True(d.SameCourse, $"{d.Launch.Script} went another way"));
        Assert.All(differences, d => Assert.True(d.Original.Ended && d.Modern.Ended));
        Assert.InRange(Of(MovementCompare.Severity.Same).Count(), 185, 205);
        Assert.InRange(Of(MovementCompare.Severity.Jump).Count(), 6, 14);
        Assert.InRange(Of(MovementCompare.Severity.Place).Count(), 18, 32);
        Assert.InRange(Of(MovementCompare.Severity.Timing).Count(), 20, 34);
    }

    /// <summary>
    /// Alaron, spawned at the Overworld A22's point where the set-down from on high finds no ground, is left with his centre 0.07
    /// behind the slope's face: the original's collision pushes him up onto it, and Modern's, which pushes a sphere only out of faces
    /// its centre is in front of, lets him fall through the world till he's too far from the camera to step.
    /// </summary>
    [Fact]
    public void Modern_drops_Alaron_through_A22()
    {
        var fell = Of(MovementCompare.Severity.FellOut).ToList();
        Assert.Equal(new HashSet<RecordId> { Script(0x042C), Script(0x042F) }, fell.Select(d => d.Launch.Script).ToHashSet());
        foreach (var d in fell)
        {
            var alaron = d.Actors.Single(a => a.Severity == MovementCompare.Severity.FellOut);
            Assert.Equal(GameState.Alaron, alaron.Original.Entity);
            Assert.False(alaron.Original.FellOut);
            Assert.True(alaron.Modern.FellOut);
            Assert.True(alaron.Modern.Position.Y < alaron.Original.Position.Y - 25);
        }
    }

    /// <summary>
    /// Where Modern gives up a walk the original doesn't, or more often, and pops the actor to its goal: the furthest pop under
    /// either. G12's gap between a wall's end and a post narrower than a sphere of radius 1 (0x03FC), the point behind H08's plain
    /// wall (0x0364, 0x0365), and C05's gap between two invisible posts narrower than Alaron (0x04EB).
    /// </summary>
    [Fact]
    public void Modern_gives_up_four_walks_the_original_makes()
    {
        var pops = Of(MovementCompare.Severity.GiveUp).ToDictionary(d => d.Launch.Script, d => d.Magnitude);
        Assert.Equal(new HashSet<RecordId> { Script(0x03FC), Script(0x0364), Script(0x0365), Script(0x04EB) }, pops.Keys.ToHashSet());
        Assert.InRange(pops[Script(0x03FC)], 8.5f, 9.5f);
        Assert.InRange(pops[Script(0x0364)], 5.3f, 6.3f);
        Assert.InRange(pops[Script(0x04EB)], 2f, 2.5f);
        Assert.InRange(pops[Script(0x0365)], 1.4f, 2f);
        foreach (var d in Of(MovementCompare.Severity.GiveUp))
            Assert.Contains(d.Actors, a => a.Modern.PutAtGoal > a.Original.PutAtGoal);
    }

    /// <summary>
    /// The Overworld I13's two actors, set down from on high onto a surface a floor covers, as the original's grid order has them:
    /// the original's collision lifts them 1.05 onto the floor, and Modern leaves them in it.
    /// </summary>
    [Fact]
    public void Modern_leaves_I13s_actors_a_floor_below()
    {
        var d = Differences().Single(d => d.Launch.Script == Script(0x0527));
        Assert.Equal(2, d.Original.Actors.Count);
        foreach (var a in d.Actors)
        {
            var (o, m) = (a.Original.Position, a.Modern.Position);
            Assert.True(Vector2.Distance(new(o.X, o.Z), new(m.X, m.Z)) < 0.01f);
            Assert.InRange(o.Y - m.Y, 1f, 1.1f);
        }
    }

    /// <summary>
    /// Modern's early stop: E22's runner in 0x0425 stops on its point under Modern, and the original's run carries it 3 past.
    /// Without the early stop, most of the places and timings that differ are the same.
    /// </summary>
    [Fact]
    public void The_early_stop_ends_a_run_short_of_where_the_original_carries_it()
    {
        var d = Differences().Single(d => d.Launch.Script == Script(0x0425));
        var runner = d.Actors.MaxBy(a => a.Distance)!;
        Assert.InRange(runner.Distance, 3, 3.6f);
        var walks = Differences().Where(d => d.SameCourse).SelectMany(d => d.Walks).Select(w => w.Delta).ToList();
        output.WriteLine($"{walks.Count} walks: {walks.Count(w => w < -MovementCompare.TimeTolerance)} shorter under Modern, " +
            $"{walks.Count(w => w > MovementCompare.TimeTolerance)} longer");
        Assert.InRange(walks.Count(w => w < -MovementCompare.TimeTolerance), 35, 50);
        Assert.InRange(walks.Count(w => w > MovementCompare.TimeTolerance), 10, 22);
        Assert.InRange(walks.Min(), -260, -200);
    }
}
