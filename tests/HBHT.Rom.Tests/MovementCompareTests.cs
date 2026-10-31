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
        Assert.InRange(Of(MovementCompare.Severity.Same).Count(), 195, 210);
        Assert.InRange(Of(MovementCompare.Severity.Jump).Count(), 6, 14);
        Assert.InRange(Of(MovementCompare.Severity.Place).Count(), 18, 30);
        Assert.InRange(Of(MovementCompare.Severity.Timing).Count(), 20, 34);
        Assert.Empty(Of(MovementCompare.Severity.FellOut));
        Assert.Empty(Of(MovementCompare.Severity.GiveUp));
    }

    /// <summary>
    /// Alaron, spawned at the Overworld A22's point where the set-down from on high finds no ground, is left with his centre 0.07
    /// behind the slope's face: the original's collision pushes him up onto it, and so does Modern's, as he was put there, where he
    /// fell through the world when it pushed a sphere only out of faces its centre is in front of.
    /// </summary>
    [Fact]
    public void Modern_sets_Alaron_down_on_A22_as_the_original_does()
    {
        var a22 = Differences().Where(d => d.Launch.Script == Script(0x042C) || d.Launch.Script == Script(0x042F))
            .Where(d => d.Launch.Stage is { Row: 1, Column: 22 }).ToList();
        // 0x042C's two launches there and 0x042F's one.
        Assert.Equal(3, a22.Count);
        foreach (var d in a22)
        {
            var alaron = d.Actors.First(a => a.Original.Entity == GameState.Alaron);
            Assert.False(alaron.Modern.FellOut);
            Assert.True(alaron.Distance < 0.05f, $"{d.Launch.Script}: {alaron.Distance}");
        }
    }

    /// <summary>
    /// The walks Modern gave up and the original makes: G12's gap between a wall's end and a post narrower than a sphere of radius 1
    /// (0x03FC) and C05's between two invisible posts narrower than Alaron (0x04EB), which it now slips through, the point behind
    /// H08's plain wall (0x0365), which a body pressed against the wall now reaches as the original's does, and the walk on from
    /// inside that wall where 0x0364's give-up put entity 0x0226, which it now comes out of. Every actor gives up as often under
    /// both.
    /// </summary>
    [Fact]
    public void Modern_makes_the_walks_the_original_makes()
    {
        var those = Differences().Where(d => d.Launch.Script == Script(0x03FC) || d.Launch.Script == Script(0x0364)
            || d.Launch.Script == Script(0x0365) || d.Launch.Script == Script(0x04EB)).ToList();
        Assert.True(those.Count >= 4);
        foreach (var d in Differences())
            Assert.All(d.Actors, a => Assert.True(a.Modern.PutAtGoal == a.Original.PutAtGoal, $"{d.Launch.Script} actor {a.Index}"));
    }

    /// <summary>
    /// The Overworld I13's two actors, set down from on high onto a surface a floor covers, as the original's grid order has them:
    /// the original's collision lifts them 1.05 onto the floor, and Modern's does too, where it left them in it.
    /// </summary>
    [Fact]
    public void Modern_lifts_I13s_actors_onto_the_floor_as_the_original_does()
    {
        var d = Differences().Single(d => d.Launch.Script == Script(0x0527));
        Assert.Equal(2, d.Original.Actors.Count);
        foreach (var a in d.Actors)
            Assert.True(a.Distance < 0.05f, $"actor {a.Index}: {a.Distance}");
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
