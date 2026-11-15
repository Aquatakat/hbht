using System.Collections.Concurrent;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// Trek's camera after a conversation ends under both enhanced cameras (<see cref="ConversationEndTrace"/>,
/// docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced): standing 3 s, then walking off 3 s.
/// </summary>
public sealed class ConversationEndCameraTests(Na10Import fixture, ITestOutputHelper output)
{
    /// <summary>
    /// Over a sample of talkers across the map sets, Port Saiid's piers among them: after the end the view turns, all told,
    /// under 230° (the half turn of walking straight off, and a little) and turns back at most once. On a pier a camera object that moves only its aim, left by the last shot a unit over
    /// his shoulder, spun the view more than a full turn round him as he walked off.
    /// </summary>
    [Fact]
    public void AfterAConversationTheViewNeverSpins()
    {
        var plugins = fixture.Data.Set;
        var sample = plugins.All<MapSetRecord>(RecordTypes.MapSet)
            .Select(s => int.Parse(s.Id.ToString().Split('/')[^1], System.Globalization.CultureInfo.InvariantCulture))
            .SelectMany(set => ConversationEndTrace.Talkers(plugins, set).Take(set == 2 ? 12 : 5).Select(t => (Set: set, t.Chunk, t.Npc)))
            .ToList();
        var results = new ConcurrentBag<ConversationEndResult>();
        Parallel.ForEach(sample, x =>
        {
            if (ConversationEndTrace.Run(plugins, x.Set, x.Chunk, x.Npc) is { } r)
                results.Add(r with { Npc = $"{x.Set} {r.Npc}" });
        });
        foreach (var r in results.OrderBy(r => r.Npc, StringComparer.Ordinal))
            output.WriteLine(r.ToString());
        Assert.True(results.Count >= 25, $"only {results.Count} talks traced");
        // Walking straight off from a view behind him, the swing is a half turn from the start, and he can bump his way a
        // little further round; a turn back the other way, once, follows a camera object or his travel along a wall.
        Assert.All(results, r => Assert.True(r.TotalTurn < 230 && r.Reversals <= 1 && r.TotalTurn - MathF.Abs(r.NetTurn) < 100, r.ToString()));
    }
}
