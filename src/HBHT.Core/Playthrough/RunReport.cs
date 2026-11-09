using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HBHT.Core.Playthrough;

/// <summary>What a report entry says went differently from the plan (docs/systems/playthrough.md, "The report").</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EntryKind>))]
public enum EntryKind
{
    /// <summary>A step was played, but what it was expected to do didn't all happen: a flag not set, a member who didn't join.</summary>
    StepMissed,
    /// <summary>The world graph found no route to the step's object, or no object stands for its place.</summary>
    NoRoute,
    /// <summary>The pilot stalled or replanned more than its budget allows.</summary>
    Stuck,
    /// <summary>The pilot stood by the object but couldn't use it.</summary>
    NotUsed,
    /// <summary>The step's ticks ran out.</summary>
    OutOfTicks,
    /// <summary>A wanted choice that the page didn't offer, or whose page never came, in a step that missed.</summary>
    ChoiceMissed,
    /// <summary>A code gate the party doesn't meet (a lock it can't pick, the gold it lacks, a purchase the run doesn't make), or a roll that came out against the plan.</summary>
    CodeGate,
    /// <summary>The planner has no step towards a goal from where the run stands.</summary>
    Unreachable,
    /// <summary>A battle the party lost, played again and won outright.</summary>
    LostFirst,
    /// <summary>A battle neither side could end, won outright.</summary>
    Stalemate,
    /// <summary>A member died.</summary>
    Death,
    /// <summary>The host's warning: a script stopped where the original would crash, or a battle the pilot couldn't play again.</summary>
    Warning,
    /// <summary>An exception: the run stopped.</summary>
    Exception,
}

/// <summary>What a milestone is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MilestoneKind>))]
public enum MilestoneKind
{
    /// <summary>Where the run began.</summary>
    Start,
    /// <summary>A member joined.</summary>
    Joined,
    /// <summary>A member died.</summary>
    Died,
    /// <summary>A journal entry was added: a flag that lists one turned on.</summary>
    Journal,
    /// <summary>A cinematic played.</summary>
    Cinematic,
    /// <summary>A map set was entered for the first time.</summary>
    MapSet,
    /// <summary>The run's goal was reached.</summary>
    Goal,
}

/// <summary>Where the player stood: a map set and his feet.</summary>
public sealed record Spot(string MapSet, float X, float Y, float Z)
{
    public static Spot? Of(Place? place) => place is { } p ? new Spot(p.MapSet.Key, p.Feet.X, p.Feet.Y, p.Feet.Z) : null;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"set {MapSet} ({X:0.0}, {Y:0.0}, {Z:0.0})");
}

/// <summary>One thing that went differently from the plan.</summary>
/// <param name="Step">The step it came in, from 1; 0 outside one.</param>
/// <param name="Action">The planner's action, described.</param>
/// <param name="Expected">What the plan expected.</param>
/// <param name="Happened">What the game did.</param>
/// <param name="Tick">The host's tick when it was seen.</param>
/// <param name="Where">Where the player stood then.</param>
/// <param name="Save">The save made before the step, to play it again from.</param>
public sealed record ReportEntry(EntryKind Kind, int Step, string? Action, string Expected, string Happened, long Tick, Spot? Where, string? Save)
{
    public override string ToString() =>
        $"[{Kind}] step {Step}, tick {Tick}{(Where is { } w ? $", at {w}" : "")}: {(Action is { } a ? a + ": " : "")}expected {Expected}; happened {Happened}" +
        (Save is { } s ? $" (save before: {Path.GetFileName(s)})" : "");
}

/// <summary>A member's level as a milestone came.</summary>
public sealed record MemberLevel(string Entity, string Name, int Level, uint SpendableExp);

/// <summary>A milestone, with the save made at it and the party's levels.</summary>
public sealed record Milestone(int Number, MilestoneKind Kind, string What, int Step, long Tick, Spot? Where, string? Save, IReadOnlyList<MemberLevel> Party)
{
    public override string ToString() =>
        $"#{Number} {Kind}: {What} (step {Step}, tick {Tick}); party {string.Join(", ", Party.Select(m => $"{m.Name} {m.Level}"))}" +
        (Save is { } s ? $"; saved {Path.GetFileName(s)}" : "");
}

/// <summary>A step the run played: the goal it was for, the action, how it went, and how long it took.</summary>
public sealed record StepLine(int Step, string Goal, string Action, string Outcome, long Ticks, double Seconds, long AtTick)
{
    public override string ToString() => $"{Step,4}. [{Goal}] {Action}: {Outcome} ({Ticks} ticks, {Seconds:0.0} s)";
}

/// <summary>How the run ended, and what it came to.</summary>
public sealed record RunSummary(
    string Goal,
    bool Reached,
    string? Why,
    int Steps,
    long Ticks,
    double WallSeconds,
    int Battles,
    int LostFirst,
    int Stalemates,
    int Deaths,
    int Trainings,
    IReadOnlyDictionary<string, int> EntriesByKind);

/// <summary>
/// The run's report (docs/systems/playthrough.md, "The report"): every place what was planned and what the game did part ways, the
/// milestones with their saves, every step played, and a summary. Written as text and as JSON.
/// </summary>
public sealed class RunReport
{
    public List<ReportEntry> Entries { get; } = [];

    public List<Milestone> Milestones { get; } = [];

    public List<StepLine> Steps { get; } = [];

    /// <summary>What was bought with experience and gold, in order.</summary>
    public List<string> Purchases { get; } = [];

    public RunSummary? Summary { get; set; }

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string ToJson() => JsonSerializer.Serialize(new { Summary, Milestones, Entries, Steps, Purchases }, Json);

    public string ToText()
    {
        var text = new StringBuilder();
        if (Summary is { } s)
        {
            text.AppendLine($"Goal: {s.Goal}: {(s.Reached ? "reached" : $"not reached: {s.Why}")}");
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"{s.Steps} steps, {s.Ticks} ticks of game time ({s.Ticks / 60.0 / 60:0.0} minutes), {s.WallSeconds:0} s of wall time"));
            text.AppendLine($"{s.Battles} battles: {s.LostFirst} lost first, {s.Stalemates} stalemates; {s.Deaths} deaths; {s.Trainings} visits to trainers");
            text.AppendLine($"Entries: {(s.EntriesByKind.Count == 0 ? "none" : string.Join(", ", s.EntriesByKind.Select(e => $"{e.Key} {e.Value}")))}");
            text.AppendLine();
        }
        text.AppendLine("Milestones:");
        foreach (var m in Milestones)
            text.AppendLine($"  {m}");
        text.AppendLine();
        text.AppendLine("Entries:");
        foreach (var e in Entries)
            text.AppendLine($"  {e}");
        text.AppendLine();
        text.AppendLine("Steps:");
        foreach (var step in Steps)
            text.AppendLine($"  {step}");
        if (Purchases.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("Bought:");
            foreach (var p in Purchases)
                text.AppendLine($"  {p}");
        }
        return text.ToString();
    }

    /// <summary>Writes the report as JSON and as text: <paramref name="path"/> with <c>.json</c> and with <c>.txt</c>.</summary>
    public void Write(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (dir is not null)
            Directory.CreateDirectory(dir);
        File.WriteAllText(Path.ChangeExtension(path, ".json"), ToJson());
        File.WriteAllText(Path.ChangeExtension(path, ".txt"), ToText());
    }
}
