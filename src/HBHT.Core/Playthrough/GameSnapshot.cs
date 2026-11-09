using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>
/// What a step can change that the plan cares about, taken before and after it: the plain flags set (derived ones follow from them),
/// the party's members, the inventory's items and the gold.
/// </summary>
public sealed class GameSnapshot
{
    private GameSnapshot(HashSet<RecordId> flags, HashSet<RecordId> members, Dictionary<RecordId, int> items, uint gold)
    {
        Flags = flags;
        Members = members;
        Items = items;
        Gold = gold;
    }

    /// <summary>The plain flags set.</summary>
    public IReadOnlySet<RecordId> Flags { get; }

    /// <summary>The party's members, by entity.</summary>
    public IReadOnlySet<RecordId> Members { get; }

    /// <summary>How many of each item the inventory holds.</summary>
    public IReadOnlyDictionary<RecordId, int> Items { get; }

    public uint Gold { get; }

    public static GameSnapshot Take(GameState game)
    {
        var network = game.Flags;
        var flags = new HashSet<RecordId>();
        // Handles 0 and 1 are the constants false and true.
        for (var handle = 2; handle < network.Count; handle++)
        {
            if (!network.IsDerived(handle) && network.Peek(handle))
                flags.Add(network.Id(handle));
        }
        var items = new Dictionary<RecordId, int>();
        foreach (var stack in game.Party.Inventory.Stacks)
            items[stack.Item.Id] = items.GetValueOrDefault(stack.Item.Id) + stack.Quantity;
        return new GameSnapshot(flags, [.. game.Party.Members.Select(m => m.Entity)], items, game.Party.Gold);
    }

    /// <summary>What changed from <paramref name="before"/> to this.</summary>
    public GameChanges Since(GameSnapshot before) => new(
        [.. Flags.Except(before.Flags).OrderBy(Key)],
        [.. before.Flags.Except(Flags).OrderBy(Key)],
        [.. Members.Except(before.Members)],
        [.. before.Members.Except(Members)],
        [.. Items.Keys.Union(before.Items.Keys)
            .Select(i => new ItemChange(i, before.Items.GetValueOrDefault(i), Items.GetValueOrDefault(i)))
            .Where(c => c.Before != c.After).OrderBy(c => c.Item.Key, StringComparer.Ordinal)],
        (long)Gold - before.Gold);

    /// <summary>Flags in number order.</summary>
    private static (int, string) Key(RecordId id) => (int.TryParse(id.Key, out var n) ? n : int.MaxValue, id.Key);
}

/// <summary>A change in how many of an item the inventory holds.</summary>
public readonly record struct ItemChange(RecordId Item, int Before, int After)
{
    public override string ToString() => $"{Item} {Before}→{After}";
}

/// <summary>What changed in a game between two snapshots.</summary>
public sealed record GameChanges(
    IReadOnlyList<RecordId> FlagsSet,
    IReadOnlyList<RecordId> FlagsCleared,
    IReadOnlyList<RecordId> Joined,
    IReadOnlyList<RecordId> Left,
    IReadOnlyList<ItemChange> Items,
    long Gold)
{
    public static readonly GameChanges None = new([], [], [], [], [], 0);

    public bool IsEmpty => FlagsSet.Count == 0 && FlagsCleared.Count == 0 && Joined.Count == 0 && Left.Count == 0 && Items.Count == 0 && Gold == 0;

    /// <summary>These changes less those the expectations account for: a flag expected set that was set, a member expected to join who joined, and so on.</summary>
    public GameChanges Except(IEnumerable<ExpectedEffect> expected)
    {
        var e = expected.ToList();
        bool Has(EffectKind kind, RecordId id, bool present) => e.Any(x => x.Kind == kind && x.Id == id && x.Present == present);
        return new GameChanges(
            [.. FlagsSet.Where(f => !Has(EffectKind.Flag, f, true))],
            [.. FlagsCleared.Where(f => !Has(EffectKind.Flag, f, false))],
            [.. Joined.Where(m => !Has(EffectKind.Member, m, true))],
            [.. Left.Where(m => !Has(EffectKind.Member, m, false))],
            [.. Items.Where(c => !Has(EffectKind.Item, c.Item, c.After > c.Before))],
            Gold);
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (FlagsSet.Count > 0)
            parts.Add($"set {string.Join(' ', FlagsSet.Select(f => f.Key))}");
        if (FlagsCleared.Count > 0)
            parts.Add($"cleared {string.Join(' ', FlagsCleared.Select(f => f.Key))}");
        if (Joined.Count > 0)
            parts.Add($"joined {string.Join(' ', Joined)}");
        if (Left.Count > 0)
            parts.Add($"left {string.Join(' ', Left)}");
        if (Items.Count > 0)
            parts.Add($"items {string.Join(' ', Items)}");
        if (Gold != 0)
            parts.Add($"gold {Gold:+0;-0}");
        return parts.Count == 0 ? "nothing" : string.Join("; ", parts);
    }
}
