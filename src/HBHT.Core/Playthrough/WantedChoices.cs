using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The choices a step wants taken, keyed by the script and the node of the page that offers them, to the node of the choice; any other
/// page with choices is left to <see cref="Fallback"/>, the host's default. A wanted choice the page doesn't offer (a conversation hides
/// those taken, and a script's conditions hide others) is recorded, and the fallback takes one instead. A wanted choice is taken once:
/// the page come back to after it goes to the fallback.
/// </summary>
public sealed class WantedChoices(IEnumerable<ChoiceWanted> wanted, IConversationPolicy? fallback = null) : IConversationPolicy
{
    private readonly Dictionary<(RecordId Script, int Node), ChoiceWanted> _wanted = wanted.ToDictionary(w => (w.Script, w.Node));
    private readonly List<ChoiceWanted> _taken = [];
    private readonly List<ChoiceMiss> _missed = [];
    private readonly HashSet<ChoiceWanted> _asked = [];

    public IConversationPolicy Fallback { get; } = fallback ?? FirstChoice.Instance;

    public int ReadTicks => Fallback.ReadTicks;

    /// <summary>The wanted choices taken, in order, each as often as it was.</summary>
    public IReadOnlyList<ChoiceWanted> Taken => _taken;

    /// <summary>The pages that came up without their wanted choice.</summary>
    public IReadOnlyList<ChoiceMiss> Missed => _missed;

    /// <summary>The wanted choices whose page never came up.</summary>
    public IEnumerable<ChoiceWanted> NotReached => _wanted.Values.Where(w => !_asked.Contains(w));

    public int Choose(ChoiceContext page)
    {
        // A page come back to after its wanted choice was taken hides it, as a conversation hides the choices taken: the fallback goes on.
        if (!_wanted.TryGetValue((page.Script.Id, page.Page.Line.Node), out var want) || _taken.Contains(want))
            return Fallback.Choose(page);
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
