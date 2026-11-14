using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Actions the story's order closes for good, which the relaxation, keeping no order, can't see (docs/systems/playthrough.md, "What's
/// left uncovered"). An action that needs a flag set that nothing clears can only be done after that flag's setter, and the player stands
/// at the setter as it's set; from there every gate that wants the flag clear is shut for good. So if no setter's place leads to the
/// action's place with those gates shut, and every other gate as it may come to stand, the action is never done. Shamsuk's Tower is the
/// one the base game has: the Chaos party at EhudUnder O03 is there only once Shamsuk is defeated (458), which only his battle at
/// Overworld I08/13 sets (<c>0x0494</c>), and the one way into the tower, the climb at Overworld P17/4, is enabled only while 458 is
/// clear; the portal into the tower from the cave on Cradawgh's Isle (MiscInteriors A05/5) is enabled by 4300, which nothing sets.
/// </summary>
/// <remarks>
/// Sound as far as the world graph is: only teleporters move the player in trek, and a script never does. A flag counts only if
/// it's plain, clear in the game, cleared by no action, and set only by actions at map objects that don't wander (an npc's or
/// encounter's battle is wherever its body was met). A gate counts as shut after the flag only for wanting it clear, or for wanting a
/// plain flag no action writes that value of and the game doesn't hold (4300); every other gate is taken as open. An action whose
/// place the setters' floods don't reach even with every gate open is left alone: that's the graph's gap, not the order's.
/// </remarks>
public sealed class Closings
{
    private Closings(Dictionary<int, RecordId> closed) => Why = closed;

    /// <summary>The actions closed for good, each with the flag whose setting closes its place.</summary>
    public IReadOnlyDictionary<int, RecordId> Why { get; }

    /// <summary>Whether an action is closed for good.</summary>
    public bool Closed(int action) => Why.ContainsKey(action);

    /// <summary>The actions closed for good from <paramref name="game"/>, by the world graph's floods.</summary>
    public static Closings Find(QuestModel model, WorldGraph graph, GameState game)
    {
        var plugins = model.Plugins;
        var flags = model.Flags;
        var n = flags.Count;
        var setters = new List<int>?[n];
        var cleared = new bool[n];
        var written = new bool[2 * n];
        foreach (var action in model.Actions)
        {
            foreach (var f in model.Adds(action.Index))
            {
                if (f >= 2 * n)
                    continue;
                written[f] = true;
                if (f % 2 == 0)
                    (setters[f / 2] ??= []).Add(action.Index);
                else
                    cleared[f / 2] = true;
            }
        }

        bool Wanders(RecordId place) => place.Type != RecordTypes.Chunk
            && plugins.TryGet<ChunkRecord>(ChunkRecord.ChunkOf(place))?.Object(ChunkRecord.IndexOf(place)) is PartyObject;
        bool Counts(int h) => flags.Kind(h) == FlagKind.Plain && !cleared[h] && !game.Flags.Get(h) && setters[h] is { Count: > 0 } by
            && by.All(a => model.Actions[a].Place is { } p && !Wanders(p));
        // A gate wanting a plain flag's value that no action writes and the game doesn't hold never opens.
        bool Never(RecordId flag, bool value) => flags.TryHandle(flag, out var h) && flags.Kind(h) == FlagKind.Plain
            && !written[model.FlagFact(h, value)] && game.Flags.Get(h) != value;

        // The actions that need each such flag set, by the flag.
        var needing = new Dictionary<int, List<PlayerAction>>();
        foreach (var action in model.Actions)
        {
            if (action.Place is null)
                continue;
            foreach (var literal in action.Presence.Concat(action.Requires).OfType<FlagIs>())
            {
                if (literal.Set && flags.TryHandle(literal.Flag, out var h) && Counts(h))
                    (needing.TryGetValue(h, out var list) ? list : needing[h] = []).Add(action);
            }
        }

        // For each flag, its setters' floods: with the gates wanting it clear shut, and with every gate open. A setter whose flood doesn't
        // start (its place is off the graphs) leaves the flag alone. The floods are given up flag by flag: the Overworld's are 8 MB each.
        var closed = new Dictionary<int, RecordId>();
        foreach (var (h, actions) in needing.OrderBy(p => p.Key))
        {
            var id = flags.Id(h);
            int? Toll(IGatedWay way)
            {
                foreach (var gate in way.Gates)
                {
                    if (!gate.Blocking)
                        continue;
                    if (gate.Flag is not { } flag)
                    {
                        if (gate.Level >= 10)
                            return null;
                        continue;
                    }
                    if (flag == id && gate.Invert || Never(flag, !gate.Invert))
                        return null;
                }
                return 0;
            }
            var floods = new List<(GateFlood Shut, GateFlood Open)>();
            var started = true;
            foreach (var place in setters[h]!.Select(a => model.Actions[a].Place!.Value).Distinct())
            {
                if (Playthrough.PositionOf(plugins, place) is not { } at || graph.Flood(at, Toll) is not { Started: true } shut)
                {
                    started = false;
                    break;
                }
                floods.Add((shut, graph.Flood(at, _ => 0)));
            }
            if (started)
            {
                foreach (var action in actions)
                {
                    var place = action.Place!.Value;
                    if (!closed.ContainsKey(action.Index) && !floods.Any(f => Reached(f.Shut, place)) && floods.Any(f => Reached(f.Open, place)))
                        closed[action.Index] = id;
                }
            }
            foreach (var (shut, open) in floods)
            {
                shut.Release();
                open.Release();
            }
        }
        return new Closings(closed);

        static bool Reached(GateFlood flood, RecordId place) =>
            (place.Type == RecordTypes.Chunk ? flood.ToChunk(place) : flood.To(place)) is not null;
    }
}
