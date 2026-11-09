using System.Diagnostics;
using System.Globalization;
using HBHT.Core;
using HBHT.Core.Playthrough;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

/// <summary>
/// <c>hbht playthrough</c>: the playthrough bot's planner over the installed plugins (docs/systems/playthrough.md): what can ever come
/// true from a new game (the static pass), and the plan from a new game or a save to a goal.
/// </summary>
internal static class PlaythroughReport
{
    public const string Usage = """
          hbht playthrough reachable [--save <save>] [--all]
                                               what can ever come true from a new game (or the save): the flags that can
                                               be set, who can join, which scripts can run, whether the ending can roll,
                                               and each plain flag that can't, by why (--all lists every one, not the
                                               first 20 of each reason; flags nothing uses at all are counted, not listed)
          hbht playthrough plan --goal flag:<n>|member|ending [--save <save>] [--budget <states>]
                                               the planner's steps from a new game (or the save) to the goal, each with
                                               the choices it takes and what code decides, or why there are none
        """;

    public static int Run(PluginSet plugins, string[] args)
    {
        try
        {
            return args switch
            {
                ["reachable", .. var rest] => Reachable(plugins, rest),
                ["plan", .. var rest] => Plan(plugins, rest),
                _ => throw new ArgumentException(Usage),
            };
        }
        catch (ArgumentException e)
        {
            Console.Error.WriteLine(e.Message);
            return 1;
        }
    }

    private static (QuestModel Model, PlanState State) Load(PluginSet plugins, string? save)
    {
        var clock = Stopwatch.StartNew();
        var model = QuestModel.Build(plugins);
        var game = save is null ? GameState.NewGame(plugins) : GameState.Load(plugins, SaveFile.Read(save, plugins));
        Console.WriteLine($"{model.Actions.Count} actions over {model.Ways.Count} scripts' {model.Ways.Values.Sum(w => w.Ways.Count)} ways, " +
            $"in {clock.Elapsed.TotalSeconds:F1} s, from {(save is null ? "a new game" : Path.GetFileName(save))}");
        foreach (var note in model.Notes)
            Console.WriteLine($"  {note}");
        return (model, PlanState.From(model, game));
    }

    private static int Reachable(PluginSet plugins, string[] args)
    {
        string? save = null;
        var all = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--save" when i + 1 < args.Length:
                    save = args[++i];
                    break;
                case "--all":
                    all = true;
                    break;
                default:
                    throw new ArgumentException(Usage);
            }
        }
        var (model, state) = Load(plugins, save);
        var pass = StaticPass.Run(model, state, QuestIndex.Build(plugins));
        Console.WriteLine();
        Console.WriteLine($"Plain flags that can be set:   {pass.PlainSettable} of {pass.PlainFlags}");
        Console.WriteLine($"Derived flags that can be set: {pass.DerivedSettable} of {pass.DerivedFlags}");
        Console.WriteLine($"Members who can join:          {pass.Joinable.Count}: {string.Join(", ", pass.Joinable.Select(m => Name(plugins, m)))}");
        Console.WriteLine($"Scripts that can run:          {pass.ScriptsRun.Count} of the {pass.ScriptsStarted} something starts");
        Console.WriteLine($"The ending:                    {(pass.ReachesEnding ? "can roll" : "can't roll")}");
        Console.WriteLine();
        Console.WriteLine($"Plain flags that can never be set: {pass.Unreachable.Count}");
        foreach (var group in pass.Unreachable.GroupBy(u => u.Why).OrderBy(g => g.Key))
        {
            var read = group.Count(u => u.Read);
            Console.WriteLine($"  {Describe(group.Key)}: {group.Count()} ({read} read by something)");
            // Flags nothing uses at all are many, and say nothing.
            var listed = group.Where(u => group.Key != NeverSet.NothingSetsIt || u.Read).ToList();
            foreach (var u in all ? listed : listed.Take(20))
            {
                var label = plugins.TryGet<FlagRecord>(u.Flag)?.Label;
                Console.WriteLine($"    {u.Flag.Key,5}{(label is null ? "" : $" ({label})")}: {u.Detail}");
            }
            if (!all && listed.Count > 20)
                Console.WriteLine($"    … {listed.Count - 20} more (--all)");
        }
        return 0;
    }

    private static string Describe(NeverSet why) => why switch
    {
        NeverSet.NothingSetsIt => "Nothing sets it",
        NeverSet.OnlyTheEngine => "Only the engine sets it (a member's death, or flags of a member who never joins)",
        NeverSet.ScriptNeverStarted => "Only scripts nothing starts set it",
        NeverSet.NoWayThroughItsScript => "No way through its scripts reaches what sets it",
        NeverSet.SetterNeverPossible => "Its setters need something that never comes true",
        NeverSet.ObjectNeverThere => "Its setters are map objects gated on flag 0 or 1, never there",
        NeverSet.OnlyATrapGoingOff => "Only a container's trap going off sets it (never planned)",
        _ => "Set by what the planner doesn't model",
    };

    private static int Plan(PluginSet plugins, string[] args)
    {
        string? save = null;
        Goal? goal = null;
        var budget = 20_000;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--save" when i + 1 < args.Length:
                    save = args[++i];
                    break;
                case "--goal" when i + 1 < args.Length:
                    goal = Goal.Parse(args[++i]);
                    break;
                case "--budget" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var b):
                    budget = b;
                    i++;
                    break;
                default:
                    throw new ArgumentException(Usage);
            }
        }
        if (goal is null)
            throw new ArgumentException(Usage);
        var (model, state) = Load(plugins, save);
        var clock = Stopwatch.StartNew();
        var plan = new QuestPlanner(model).Plan(state, goal, budget);
        Console.WriteLine($"Goal: {goal}. {(plan.Found ? $"{plan.Steps.Count} steps" : "No plan")}, from {plan.Relevant} relevant actions, " +
            $"{plan.Expanded} states looked at, in {clock.Elapsed.TotalSeconds:F1} s.");
        Console.WriteLine("Places are taken as reachable until the world graph answers (IPlaceReach).");
        var step = 0;
        foreach (var action in plan.Steps)
        {
            Console.WriteLine();
            Console.WriteLine($"{++step,3}. {action.Describe()}");
            if (action.Script is { } script && plugins.TryGet<ScriptRecord>(script)?.Label is { } label)
                Console.WriteLine($"     script: {label}");
            var gates = action.Gates.Select(g => g.What).Distinct().ToList();
            if (gates.Count > 0)
                Console.WriteLine($"     code decides: {string.Join("; ", gates)}");
            var needs = action.Presence.Concat(action.Requires).Where(l => l is not (CodeGate or Reaches)).ToList();
            if (needs.Count > 0)
                Console.WriteLine($"     needs: {string.Join(", ", needs)}");
            Console.WriteLine($"     does: {string.Join(", ", action.Effects)}");
            if (action.Fault is { } fault)
                Console.WriteLine($"     its script stops: {fault}");
        }
        if (plan.Why is { } why)
        {
            Console.WriteLine();
            Console.WriteLine(why);
        }
        return plan.Found ? 0 : 2;
    }

    private static string Name(PluginSet plugins, RecordId entity) => plugins.TryGet<EntityRecord>(entity)?.Name ?? entity.ToString();
}
