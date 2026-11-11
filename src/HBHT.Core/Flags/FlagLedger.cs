namespace HBHT.Core.Flags;

/// <summary>
/// Every flag seen to turn on, by handle, since the ledger was made: a plain flag as it's written set, a derived one as the network's
/// evaluation turns it on (<see cref="FlagNetwork.Ledger"/>). So a flag set and cleared again between two looks still counts. It isn't
/// part of a save, and it outlives the network it watches: the playthrough bot moves it to each game its host starts again on.
/// </summary>
/// <remarks>
/// The network marks only on a change to set, which trek's calendar makes a handful of times a game hour, so the hot path is a null
/// check and a bit test. Loading a save or starting a new game sets flags without marking them: they were set, not seen to turn on.
/// </remarks>
public sealed class FlagLedger
{
    private ulong[] _seen;
    private readonly List<int> _fresh = [];

    public FlagLedger(int count = 0) => _seen = new ulong[(count + 63) / 64];

    /// <summary>How many flags have been seen.</summary>
    public int Count { get; private set; }

    /// <summary>Whether a flag has been seen to turn on.</summary>
    public bool Seen(int handle) => handle >> 6 < _seen.Length && (_seen[handle >> 6] & (1UL << (handle & 63))) != 0;

    /// <summary>Notes that a flag turned on.</summary>
    public void Mark(int handle)
    {
        var word = handle >> 6;
        var bit = 1UL << (handle & 63);
        if (word < _seen.Length && (_seen[word] & bit) != 0)
            return;
        if (word >= _seen.Length)
            Array.Resize(ref _seen, Math.Max(word + 1, _seen.Length * 2));
        _seen[word] |= bit;
        Count++;
        _fresh.Add(handle);
    }

    /// <summary>The flags first seen since the last call, in the order they were, and forgets them as fresh.</summary>
    public IReadOnlyList<int> TakeFresh()
    {
        var fresh = _fresh.ToArray();
        _fresh.Clear();
        return fresh;
    }

    /// <summary>Every flag seen, by handle.</summary>
    public IEnumerable<int> All()
    {
        for (var w = 0; w < _seen.Length; w++)
        {
            for (var bits = _seen[w]; bits != 0; bits &= bits - 1)
                yield return w * 64 + System.Numerics.BitOperations.TrailingZeroCount(bits);
        }
    }
}
