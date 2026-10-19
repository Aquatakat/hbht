using Aidyn.Core.Rng;

namespace Aidyn.Core.Combat;

/// <summary>
/// The AI's list of scored choices (<c>AiScores</c>, <c>0x800E0BE0</c>, <c>aiscores.cpp</c>): entries of a byte score, a cell and,
/// for a spell, its action slot, each with a combatant (the opponent, or for a cell the nearest one it can attack from there). A
/// style fills it, sorts it, and the first entry is its choice: its target and the cell it walks to.
/// </summary>
internal sealed class AiScores(int capacity)
{
    /// <summary>One choice (8 bytes: the score, x, z, the action slot + 1, the combatant).</summary>
    public sealed class Entry(int score, int x, int z, int slot, Combatant? combatant)
    {
        public int Score { get; set; } = score & 0xFF;

        public int X { get; set; } = x & 0xFF;

        public int Z { get; set; } = z & 0xFF;

        /// <summary>The spell's action slot and 1, or 0 for a weapon's.</summary>
        public int Slot { get; } = slot & 0xFF;

        public Combatant? Combatant { get; set; } = combatant;

        public override string ToString() => $"({Score}, {X}, {Z}, {Slot}, {Combatant})";
    }

    private List<Entry> _entries = [];

    /// <summary>How many it may hold (<c>0x800E0BE0</c>), the list's allocation; more are dropped.</summary>
    public int Capacity { get; } = (ushort)capacity;

    public int Count => _entries.Count;

    public IReadOnlyList<Entry> Entries => _entries;

    public Entry this[int i] => _entries[i];

    /// <summary>Adds an entry, unless the list is full (<c>AiScores.Add</c>, <c>0x8005BF20</c>).</summary>
    public void Add(int x, int z, int slot, int score, Combatant? combatant)
    {
        if (_entries.Count < Capacity)
            _entries.Add(new Entry(score, x, z, slot, combatant));
    }

    /// <summary>
    /// Sorts it by score (<c>AiScores.Sort</c>, <c>0x8005BFC4</c>): the lowest first, or the highest. It's the original's quicksort
    /// (<see cref="Quicksort"/>), so equal scores fall in an order drawn on <paramref name="random"/>.
    /// </summary>
    public void Sort(bool ascending, IRandom random)
    {
        if (_entries.Count < 2)
            return;
        Quicksort.Sort(_entries, ascending ? Ascending : Descending, random);

        static int Ascending(Entry a, Entry b) => a.Score > b.Score ? 1 : b.Score > a.Score ? -1 : 0;
        static int Descending(Entry a, Entry b) => a.Score < b.Score ? 1 : b.Score < a.Score ? -1 : 0;
    }

    /// <summary>Keeps only the entries tied with the first (<c>AiScores.KeepBest</c>, <c>0x8005C018</c>).</summary>
    public void KeepBest()
    {
        if (_entries.Count < 2)
            return;
        var keep = 1;
        if (_entries[1].Score == _entries[0].Score)
        {
            keep = 2;
            while (keep < _entries.Count && _entries[keep].Score == _entries[0].Score)
                keep++;
        }
        _entries = _entries[..keep];
    }
}

/// <summary>
/// The original's quicksort (<c>quicksort.cpp</c>, <c>Quicksort.Sort</c>, <c>0x800B3978</c>), which isn't the C library's: three
/// elements or two are put in order by comparisons, and more by a pivot drawn at random (one draw a partition,
/// <c>Quicksort.RandomPivot</c>, <c>0x800B389C</c>) and Hoare's partition against whatever is in the pivot's slot
/// (<c>Quicksort.Partition</c>, <c>0x800B3730</c>). It isn't stable: equal elements fall in an order the draws decide.
/// </summary>
public static class Quicksort
{
    /// <summary>Sorts <paramref name="list"/> by <paramref name="compare"/> (1 when the first goes after the second).</summary>
    public static void Sort<T>(IList<T> list, Comparison<T> compare, IRandom random)
    {
        if (list.Count > 0)
            Sort(list, 0, list.Count - 1, compare, random);
    }

    private static void Sort<T>(IList<T> a, int lo, int hi, Comparison<T> compare, IRandom random)
    {
        if (lo == hi - 2)
        {
            if (compare(a[lo], a[lo + 1]) == 1)
                Swap(a, lo, lo + 1);
            if (compare(a[lo + 1], a[lo + 2]) == 1)
                Swap(a, lo + 1, lo + 2);
            if (compare(a[lo], a[lo + 1]) == 1)
                Swap(a, lo, lo + 1);
            return;
        }
        if (lo == hi - 1)
        {
            if (compare(a[lo], a[hi]) == 1)
                Swap(a, lo, hi);
            return;
        }
        if (hi <= lo)
            return;
        Swap(a, random.Range(lo, hi), lo);
        var p = Partition(a, lo, hi, compare);
        Sort(a, lo, p, compare, random);
        Sort(a, p + 1, hi, compare, random);
    }

    private static int Partition<T>(IList<T> a, int lo, int hi, Comparison<T> compare)
    {
        var (i, j) = (lo - 1, hi + 1);
        while (true)
        {
            while (i != hi)
            {
                i++;
                if (compare(a[i], a[lo]) != -1)
                    break;
            }
            while (j != 0)
            {
                j--;
                if (compare(a[j], a[lo]) != 1)
                    break;
            }
            if (j <= i)
                return j;
            Swap(a, i, j);
        }
    }

    private static void Swap<T>(IList<T> a, int i, int j) => (a[i], a[j]) = (a[j], a[i]);
}
