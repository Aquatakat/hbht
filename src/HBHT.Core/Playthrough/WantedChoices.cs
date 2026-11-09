using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The choices a step wants taken, keyed by the script and the node of the page that offers them, to the node of the choice; any other
/// page with choices is left to <see cref="Fallback"/>, the host's default. A wanted choice the page doesn't offer (a conversation hides
/// those taken, and a script's conditions hide others) is recorded, and the fallback takes one instead. A wanted choice is taken once:
/// the page come back to after it goes to the next choice wanted there, in order, and once they're spent, to the fallback.
/// </summary>
/// <remarks>
/// With <paramref name="ways"/>, the step's script's pages are chosen by its ways instead (<see cref="PilotStep.Ways"/>): each page takes
/// the next choice of the first way, of those whose choices so far are the ones taken, that the page offers. The planner merges ways
/// that do the same under conditions it can't tell apart (Brenna's 0x03A3 goes by node 33's menu once flag 18 is set, by node 2's
/// before), and the way it keeps may be the one the state doesn't take: the pages say which.
/// </remarks>
public sealed class WantedChoices(IEnumerable<ChoiceWanted> wanted, IConversationPolicy? fallback = null,
    (RecordId Script, IReadOnlyList<IReadOnlyList<int>> Choices)? ways = null) : IConversationPolicy
{
    private readonly Dictionary<(RecordId Script, int Node), List<ChoiceWanted>> _wanted = wanted
        .GroupBy(w => (w.Script, w.Node)).ToDictionary(g => g.Key, g => g.ToList());
    private readonly Dictionary<(RecordId Script, int Node), int> _next = [];
    private readonly List<ChoiceWanted> _taken = [];
    private readonly List<ChoiceMiss> _missed = [];
    private readonly HashSet<ChoiceWanted> _asked = [];
    private readonly List<int> _path = [];
    private bool _offWays;

    public IConversationPolicy Fallback { get; } = fallback ?? FirstChoice.Instance;

    public int ReadTicks => Fallback.ReadTicks;

    /// <summary>The wanted choices taken, in order, each as often as it was.</summary>
    public IReadOnlyList<ChoiceWanted> Taken => _taken;

    /// <summary>The pages that came up without their wanted choice.</summary>
    public IReadOnlyList<ChoiceMiss> Missed => _missed;

    /// <summary>The wanted choices whose page never came up; none once a whole way was taken.</summary>
    public IEnumerable<ChoiceWanted> NotReached => WayDone ? [] : _wanted.Values.SelectMany(w => w).Where(w => !_asked.Contains(w)).Distinct();

    /// <summary>Whether the choices taken on the script's pages are all of one of its ways.</summary>
    private bool WayDone => ways is { } w && !_offWays && w.Choices.Any(c => c.Count == _path.Count && c.SequenceEqual(_path));

    public int Choose(ChoiceContext page)
    {
        if (ways is { } w && page.Script.Id == w.Script && !_offWays)
        {
            foreach (var choices in w.Choices)
            {
                if (choices.Count <= _path.Count || !choices.Take(_path.Count).SequenceEqual(_path))
                    continue;
                for (var i = 0; i < page.Page.Choices.Count; i++)
                {
                    if (page.Page.Choices[i].Node != choices[_path.Count])
                        continue;
                    _path.Add(choices[_path.Count]);
                    var hit = new ChoiceWanted(page.Script.Id, page.Page.Line.Node, page.Page.Choices[i].Node);
                    _asked.Add(hit);
                    _taken.Add(hit);
                    return i;
                }
            }
            // No way kept to offers anything here: the keyed choices and the fallback go on.
            _offWays = true;
        }
        var key = (page.Script.Id, page.Page.Line.Node);
        var next = _next.GetValueOrDefault(key);
        // A page come back to after its wanted choices were taken hides them, as a conversation hides the choices taken: the fallback goes on.
        if (!_wanted.TryGetValue(key, out var list) || next >= list.Count)
            return Fallback.Choose(page);
        var want = list[next];
        _next[key] = next + 1;
        _asked.Add(want);
        for (var i = 0; i < page.Page.Choices.Count; i++)
        {
            if (page.Page.Choices[i].Node == want.Choice)
            {
                _taken.Add(want);
                return i;
            }
        }
        _missed.Add(new ChoiceMiss(page.Host.Clock.Ticks, want, [.. page.Page.Choices.Select(c => c.Node)]));
        return Fallback.Choose(page);
    }
}
