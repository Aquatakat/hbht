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
    private readonly Dictionary<RecordId, HashSet<int>> _seen = [];

    public bool IsSeen(MinimapRecord minimap, int section) => Pieces(minimap).Contains(section);

    public void See(MinimapRecord minimap, int section) => Pieces(minimap).Add(section);

    private HashSet<int> Pieces(MinimapRecord minimap)
    {
        if (!_seen.TryGetValue(minimap.Id, out var pieces))
            _seen[minimap.Id] = pieces = [.. minimap.Sections.Select((s, i) => (s, i)).Where(p => p.s.Seen).Select(p => p.i)];
        return pieces;
    }

    /// <summary>Each minimap's seen pieces, where they're not as it starts.</summary>
    public IReadOnlyList<SaveMinimap> Save() =>
    [
        .. _seen.Where(e => plugins.TryGet<MinimapRecord>(e.Key) is not { } m
                || !e.Value.SetEquals(m.Sections.Select((s, i) => (s, i)).Where(p => p.s.Seen).Select(p => p.i)))
            .OrderBy(e => e.Key.ToString(), StringComparer.Ordinal)
            .Select(e => new SaveMinimap(e.Key, [.. e.Value.Order()])),
    ];

    /// <summary>Takes a save's seen pieces, listing in <paramref name="problems"/> what no plugin has any more.</summary>
    public void Load(IReadOnlyList<SaveMinimap> saved, List<string> problems)
    {
        foreach (var minimap in saved)
        {
            if (plugins.TryGet<MinimapRecord>(minimap.Minimap) is not { } record)
            {
                problems.Add($"{minimap.Minimap} isn't a minimap any plugin has.");
                continue;
            }
            var pieces = _seen[minimap.Minimap] = [];
            foreach (var section in minimap.Seen)
            {
                if (section >= 0 && section < record.Sections.Count)
                    pieces.Add(section);
                else
                    problems.Add($"{minimap.Minimap} has no piece {section}.");
            }
        }
    }
}
