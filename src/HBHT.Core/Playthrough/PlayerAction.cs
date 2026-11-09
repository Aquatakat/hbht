using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>What the player does in a step.</summary>
public enum ActionKind
{
    /// <summary>Talk to an npc, which plays its script.</summary>
    Talk,
    /// <summary>Use or walk into a script object (a sign, a book, a conversation or a cut-scene).</summary>
    Play,
    /// <summary>Open a container.</summary>
    Open,
    /// <summary>Use or walk into a tripwire.</summary>
    Trip,
    /// <summary>Use a secret door until it's found.</summary>
    FindSecret,
    /// <summary>Disarm a door's trap.</summary>
    Disarm,
    /// <summary>Pick a door's lock.</summary>
    PickLock,
    /// <summary>Fight an encounter and win.</summary>
    Fight,
    /// <summary>Walk into a world map region, which sets its flag.</summary>
    Enter,
    /// <summary>Let an event the game fires play out: a new game's opening.</summary>
    Event,
}

/// <summary>
/// One thing the player can do, one way: use a map object, take a script's choices along one of its ways, and win the battle it
/// ends in. Its preconditions are what the state must be before it (<see cref="Presence"/> to find the object there at all,
/// <see cref="Requires"/> for its script to go this way); its effects are what it does, in order.
/// </summary>
/// <param name="Index">Its place in <see cref="QuestModel.Actions"/>.</param>
/// <param name="Place">The map object it uses, or for <see cref="ActionKind.Enter"/> the chunk, or for an event none.</param>
/// <param name="Script">The script it plays, if any.</param>
/// <param name="Choices">The nodes of the choices to take, in order, on the pages that offer choices.</param>
/// <param name="Presence">The object's gates, and that the player can reach it.</param>
/// <param name="Requires">What its script's conditions need for it to go this way, and what code decides (<see cref="CodeGate"/>).</param>
/// <param name="Effects">What it does, in order.</param>
/// <param name="Wins">It ends in a battle, which the party must win.</param>
/// <param name="Buys">A key item it buys at the shop its script opens.</param>
/// <param name="Fault">Where its script stops where the original would crash.</param>
public sealed record PlayerAction(
    int Index,
    ActionKind Kind,
    RecordId? Place,
    RecordId? Script,
    IReadOnlyList<int> Choices,
    IReadOnlyList<Literal> Presence,
    IReadOnlyList<Literal> Requires,
    IReadOnlyList<Effect> Effects,
    bool Wins = false,
    RecordId? Buys = null,
    string? Fault = null)
{
    /// <summary>
    /// Other choices that go the same way: where ways through the script were one across a condition that changed nothing, each
    /// branch's. The pilot takes whichever the pages offer.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> OtherChoices { get; init; } = [];

    /// <summary>The way through its script it takes, for an action that plays one.</summary>
    public ScriptWay? Way { get; init; }

    /// <summary>Its gates the game decides in code, which the planner takes to hold.</summary>
    public IEnumerable<CodeGate> Gates => Presence.Concat(Requires).OfType<CodeGate>();

    /// <summary>A line for reports: what it is, where, and the choices it takes.</summary>
    public string Describe()
    {
        var what = Kind switch
        {
            ActionKind.Talk => $"talk to {Place}",
            ActionKind.Play => $"use {Place}",
            ActionKind.Open => $"open {Place}",
            ActionKind.Trip => $"walk into {Place}",
            ActionKind.FindSecret => $"find the secret door {Place}",
            ActionKind.Disarm => $"disarm {Place}",
            ActionKind.PickLock => $"pick the lock of {Place}",
            ActionKind.Fight => $"fight {Place}",
            ActionKind.Enter => $"enter {Place}",
            _ => "the opening plays",
        };
        if (Script is { } script)
            what += $" ({script.Key})";
        if (Choices.Count > 0)
            what += $", choosing {string.Join(" ", Choices)}";
        if (Buys is { } item)
            what += $", buying {item}";
        if (Wins)
            what += ", winning the battle";
        return what;
    }

    public override string ToString() => Describe();
}
