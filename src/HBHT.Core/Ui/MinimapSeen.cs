using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Ui;

/// <summary>
/// Which minimaps' pieces the party has seen (each section's <c>checked</c>), which a save keeps. A minimap no save has
/// touched starts with the pieces its record marks seen.
/// </summary>
public sealed class MinimapSeen(PluginSet plugins)
{
    // By map set, the minimap on it.
    private readonly Dictionary<RecordId, HashSet<int>> _seen = [];

    public bool IsSeen(MapSetRecord set, int section) => Pieces(set).Contains(section);

    public void See(MapSetRecord set, int section) => Pieces(set).Add(section);

    private HashSet<int> Pieces(MapSetRecord set)
    {
        if (!_seen.TryGetValue(set.Id, out var pieces))
            _seen[set.Id] = pieces = [.. StartsSeen(set.Minimap)];
        return pieces;
    }

    private static IEnumerable<int> StartsSeen(MinimapPlan? minimap) =>
        (minimap?.Sections ?? []).Select((s, i) => (s, i)).Where(p => p.s.Seen).Select(p => p.i);

    /// <summary>Each minimap's seen pieces, by its map set, where they're not as it starts.</summary>
    public IReadOnlyList<SaveMinimap> Save() =>
    [
        .. _seen.Where(e => plugins.TryGet<MapSetRecord>(e.Key)?.Minimap is not { } m || !e.Value.SetEquals(StartsSeen(m)))
            .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal)
            .Select(e => new SaveMinimap(e.Key, [.. e.Value.Order()])),
    ];

    /// <summary>Takes a save's seen pieces, listing in <paramref name="problems"/> what no plugin has any more.</summary>
    public void Load(IReadOnlyList<SaveMinimap> saved, List<string> problems)
    {
        foreach (var minimap in saved)
        {
            if (plugins.TryGet<MapSetRecord>(minimap.MapSet)?.Minimap is not { } record)
            {
                problems.Add($"{minimap.MapSet} isn't a map set with a minimap any plugin has.");
                continue;
            }
            var pieces = _seen[minimap.MapSet] = [];
            foreach (var section in minimap.Seen)
            {
                if (section >= 0 && section < record.Sections.Count)
                    pieces.Add(section);
                else
                    problems.Add($"{minimap.MapSet}'s minimap has no piece {section}.");
            }
        }
    }
}
