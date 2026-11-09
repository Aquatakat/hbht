using HBHT.Core.Characters;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// A planner's action as the run plays it: the pilot's step (none for an event, which the host plays by running until the player has
/// the controls), whether its route may pick locks, and its code gates sorted by whether the party meets them now.
/// </summary>
/// <param name="Step">The pilot's step; null for an event, or where no map object stands for the action's place.</param>
/// <param name="PickLocks">Whether the route may take locked doors, which the party's thief picks (<see cref="RouteOptions.PickLocks"/>).</param>
/// <param name="Unmet">Code gates the party doesn't meet as it stands: gold, stats, a lock its thief can't pick, a shop's purchase.</param>
/// <param name="Rolls">Code gates left to a roll or the battle: skill checks, traps, loot's chances, winning.</param>
/// <param name="Notes">What the translation couldn't carry over, such as a choice no menu of the script offers.</param>
public sealed record TranslatedStep(PlayerAction Action, PilotStep? Step, bool PickLocks, IReadOnlyList<CodeGate> Unmet, IReadOnlyList<CodeGate> Rolls,
    IReadOnlyList<string> Notes)
{
    public bool IsEvent => Action.Kind == ActionKind.Event;
}

/// <summary>
/// Turns the planner's actions into the pilot's steps (docs/systems/playthrough.md, "The run"): the action's place becomes the map object
/// to use (for a chunk, one to walk up to in it), its kind how it's used, its choices the pages' choices wanted, and its effects what the
/// step should leave so.
/// </summary>
public static class StepTranslation
{
    /// <summary>How the pilot uses an action's object.</summary>
    public static UseKind UseOf(ActionKind kind) => kind switch
    {
        ActionKind.Talk or ActionKind.Fight => UseKind.Talk,
        ActionKind.Open or ActionKind.FindSecret or ActionKind.Disarm or ActionKind.PickLock => UseKind.Press,
        ActionKind.Enter => UseKind.Arrive,
        _ => UseKind.Auto,
    };

    public static TranslatedStep Translate(PlayerAction action, QuestModel model, GameState game, WorldGraph graph, WorldReach? reach = null)
    {
        var notes = new List<string>();
        var gates = action.Gates.Distinct().ToList();
        var unmet = gates.Where(g => CodeGates.Meets(g, game) == false).ToList();
        var rolls = gates.Where(g => CodeGates.Meets(g, game) is null).ToList();
        if (action.Kind == ActionKind.Event || action.Place is not { } place)
            return new TranslatedStep(action, null, false, unmet, rolls, notes);

        RecordId target;
        if (place.Type == RecordTypes.Chunk)
        {
            if (InChunk(model.Plugins, graph, game, place) is not { } inside)
            {
                notes.Add($"{place} has no map object the pilot can walk up to");
                return new TranslatedStep(action, null, false, unmet, rolls, notes);
            }
            target = inside;
        }
        else
        {
            target = place;
        }
        var choices = Choices(action, model.Plugins, notes);
        var expect = Expect(action, model, game);
        var picks = reach?.Picks(place) == true || PicksOnTheWay(graph, game, target);
        return new TranslatedStep(action, new PilotStep(target, UseOf(action.Kind), choices, expect), picks, unmet, rolls, notes);
    }

    /// <summary>
    /// Whether the way to a map object is shut only by locks the party's thief picks, so the route must take them: the route without
    /// picking is refused for closed gates that are all such locks.
    /// </summary>
    private static bool PicksOnTheWay(WorldGraph graph, GameState game, RecordId target)
    {
        if (!graph.Portals.Any(p => p.Gates.Any(g => g.Kind == GateKind.Locked && g.Closed(game.Flags))))
            return false;
        var plain = graph.Route(game, target);
        return plain.Refusal is { Kind: RefusalKind.Gated } refusal
            && refusal.Blockers.All(g => g.Kind == GateKind.Locked && WorldReach.CanPick(game.Party, g.Level));
    }

    /// <summary>
    /// A map object to walk up to in a chunk, to enter it: the nearest its middle of those the player uses by standing near that walking
    /// up to sets off nothing (no tangible object, no npc or encounter), and whose own gates are open.
    /// </summary>
    private static RecordId? InChunk(PluginSet plugins, WorldGraph graph, GameState game, RecordId chunkId)
    {
        if (plugins.TryGet<ChunkRecord>(chunkId) is not { } chunk)
            return null;
        var size = plugins.Get<MapSetRecord>(chunk.MapSet).CellSize;
        var middle = new System.Numerics.Vector3(size / 2, 0, size / 2);
        return chunk.Objects
            .Where(o => !o.Tangible && o is not PartyObject && WorldGraph.UseRange(o) is not null
                && WorldGraph.ObjectGates(chunk.ObjectId(o.Index), o).All(g => !g.Closed(game.Flags)))
            .OrderBy(o => System.Numerics.Vector3.Distance(o.Position with { Y = 0 }, middle))
            .Select(o => (RecordId?)chunk.ObjectId(o.Index))
            .FirstOrDefault();
    }

    /// <summary>
    /// The choices an action takes, as the pages that offer them: each choice's node is found among the links of the script's menus
    /// and cases, the first that offers it. One no menu offers is noted and left out.
    /// </summary>
    public static IReadOnlyList<ChoiceWanted> Choices(PlayerAction action, PluginSet plugins, List<string> notes)
    {
        if (action.Choices.Count == 0)
            return [];
        if (action.Script is not { } scriptId || plugins.TryGet<ScriptRecord>(scriptId) is not { } script)
        {
            notes.Add($"choices {string.Join(" ", action.Choices)} with no script to offer them");
            return [];
        }
        var wanted = new List<ChoiceWanted>();
        foreach (var choice in action.Choices)
        {
            var menu = script.Nodes.FirstOrDefault(n => n.Kind is ScriptNodeKind.Menu or ScriptNodeKind.Case && (n.Links ?? []).Contains(choice));
            if (menu is null)
                notes.Add($"choice {choice}: no menu of {scriptId} offers it");
            else
                wanted.Add(new ChoiceWanted(scriptId, menu.Index, choice));
        }
        return wanted;
    }

    /// <summary>
    /// What an action should leave so, by its effects' last word on each flag, member and item: the plain flags it sets and clears,
    /// who joins (while the party has room) and leaves, and the items it gives and takes, less those a roll or a shop decides.
    /// </summary>
    public static IReadOnlyList<ExpectedEffect> Expect(PlayerAction action, QuestModel model, GameState game)
    {
        var chancy = action.Gates.Where(g => g.Check is { Kind: CodeCheckKind.Chance or CodeCheckKind.Buy }).Select(g => g.Check!.A).ToHashSet();
        var expect = new Dictionary<(EffectKind, RecordId), bool>();
        var room = Party.MaxSize - game.Party.Members.Count(m => !m.IsDead);
        foreach (var effect in action.Effects)
        {
            switch (effect)
            {
                case SetFlag set when model.Flags.TryHandle(set.Flag, out var h) && model.Flags.Kind(h) == FlagKind.Plain:
                    expect[(EffectKind.Flag, set.Flag)] = set.Value;
                    break;
                case Join join when !game.Party.Contains(join.Entity):
                    if (room-- > 0)
                        expect[(EffectKind.Member, join.Entity)] = true;
                    break;
                case Leave leave:
                    expect[(EffectKind.Member, leave.Entity)] = false;
                    break;
                case Gain gain when !chancy.Contains(gain.Item):
                    expect[(EffectKind.Item, gain.Item)] = true;
                    break;
                case Lose lose:
                    expect[(EffectKind.Item, lose.Item)] = false;
                    break;
            }
        }
        return [.. expect.Select(e => new ExpectedEffect(e.Key.Item1, e.Key.Item2, e.Value))];
    }
}

/// <summary>What the run makes of the planner's code gates, weighed against the party as it stands.</summary>
public static class CodeGates
{
    /// <summary>
    /// Whether the party meets a code gate now: gold, stats and locks are read off the game; a skill check, a trap, a drop's chance, a
    /// battle and the merge are a roll or the battle's (null); a shop's purchase the run doesn't make (false).
    /// </summary>
    public static bool? Meets(CodeGate gate, GameState game)
    {
        if (gate.Check is not { } check)
            return null;
        bool In(int value) => value >= check.Lo && value <= check.Hi;
        int? Stat(RecordId? stat) => stat is { } s && StatId.TryFromRecord(s) is { } id ? (int)id : null;
        return check.Kind switch
        {
            CodeCheckKind.Gold => In((int)Math.Min(game.Party.Gold, 0xFFFF)),
            CodeCheckKind.BestStat when Stat(check.A) is { } s => In(((IScriptWorld)game).BestStat(s) & 0xFFFF),
            CodeCheckKind.WorstStat when Stat(check.A) is { } s => In(((IScriptWorld)game).WorstStat(s) & 0xFFFF),
            CodeCheckKind.MemberStat when Stat(check.B) is { } s => In(((IScriptWorld)game).MemberStat(check.A, s) & 0xFFFF),
            CodeCheckKind.Lock => WorldReach.CanPick(game.Party, check.Lo),
            CodeCheckKind.Buy => false,
            _ => null,
        };
    }
}
