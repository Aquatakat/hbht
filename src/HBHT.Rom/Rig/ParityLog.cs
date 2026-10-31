using System.Text.Json;

namespace HBHT.Rom.Rig;

/// <summary>
/// A battle the original fought under <c>tools/parity.ps1</c> (combat parity, roadmap 2.9): its folder's <c>log.jsonl</c>,
/// every step of the stream and every rules entry point in order, and the RAM dumped beside it.
/// </summary>
/// <remarks>The format is <c>tools/gdb/parity.py</c>'s, which says what each field holds.</remarks>
public sealed class ParityLog
{
    private ParityLog(string folder, IReadOnlyList<ParityRecord> records)
    {
        Folder = folder;
        Records = records;
    }

    public string Folder { get; }

    /// <summary>The draws and entry points, in the order the original made them.</summary>
    public IReadOnlyList<ParityRecord> Records { get; }

    public IEnumerable<ParityDraw> Draws => Records.OfType<ParityDraw>();

    public IEnumerable<ParityEntry> Entries => Records.OfType<ParityEntry>();

    /// <summary>
    /// Whether the logger watched an entry point, by the list it gives at <c>Combat.Setup</c>; false for a log made before it gave
    /// one, whatever it watched.
    /// </summary>
    public bool Watches(string name) => Entries.FirstOrDefault(e => e.Name == "Combat.Setup")?.Watched?.Contains(name) == true;

    /// <summary>The RAM an entry point dumped.</summary>
    public AresCheckpoint Ram(ParityEntry entry) =>
        AresCheckpoint.FromRdram(Path.Combine(Folder, entry.Dump ?? throw new ArgumentException($"{entry.Name} dumped no RAM.")));

    /// <summary>
    /// A log's whole lines, read alongside the logger: a gdb that hasn't noticed ares close still holds it open for writing, and its
    /// last line may not be written out yet.
    /// </summary>
    public static IEnumerable<string> Lines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        var text = reader.ReadToEnd();
        var end = text.LastIndexOf('\n');
        return end < 0 ? [] : text[..end].Split('\n').Select(l => l.TrimEnd('\r')).ToList();
    }

    public static ParityLog Read(string folder)
    {
        var records = new List<ParityRecord>();
        foreach (var line in Lines(Path.Combine(folder, "log.jsonl")))
        {
            if (line.Length == 0)
                continue;
            var r = JsonDocument.Parse(line).RootElement;
            var at = r.GetProperty("pc").GetUInt32();
            records.Add(r.GetProperty("t").GetString() switch
            {
                "draw" => new ParityDraw(r.GetProperty("old").GetUInt32(), r.GetProperty("new").GetUInt32(), at,
                    [.. r.GetProperty("chain").EnumerateArray().Select(a => a.GetUInt32())], r.GetProperty("state").GetInt32()),
                "enter" => new ParityEntry(r.GetProperty("name").GetString()!, at, r.GetProperty("ra").GetUInt32(),
                    [.. r.GetProperty("args").EnumerateArray().Select(a => a.GetUInt32())], r.GetProperty("seed").GetUInt32())
                {
                    State = Hex(r, "state"),
                    Delta = Hex(r, "delta"),
                    Frame = r.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetUInt32() : null,
                    Current = r.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetInt32() : null,
                    Combatant = Hex(r, "combatant"),
                    Dump = r.TryGetProperty("dump", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null,
                    Watched = r.TryGetProperty("watched", out var w) && w.ValueKind == JsonValueKind.Array
                        ? [.. w.EnumerateArray().Select(n => n.GetString()!)]
                        : null,
                },
                "leave" => new ParityLeave(r.GetProperty("name").GetString()!, at, r.GetProperty("seed").GetUInt32()),
                var t => throw new InvalidDataException($"A record of type {t}."),
            });
        }
        return new ParityLog(folder, records);

        static byte[]? Hex(JsonElement r, string name) =>
            r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? Convert.FromHexString(v.GetString()!) : null;
    }
}

public abstract record ParityRecord(uint At);

/// <summary>A step of the stream: the seed before and after, where it stopped (in the function that drew), and above it.</summary>
/// <param name="Chain">The stop's address, then up to three return addresses above it.</param>
/// <param name="State">The combat state (<c>Combat.State</c>) as it drew.</param>
public sealed record ParityDraw(uint Old, uint New, uint At, IReadOnlyList<uint> Chain, int State) : ParityRecord(At);

/// <summary>A rules entry point, or the entry of a function that fills the setup block.</summary>
/// <param name="Args">a0 to a3.</param>
/// <param name="Seed">The stream's state as it was entered.</param>
public sealed record ParityEntry(string Name, uint At, uint Return, IReadOnlyList<uint> Args, uint Seed) : ParityRecord(At)
{
    /// <summary><c>Combat.State</c> and <c>NextState</c>.</summary>
    public byte[]? State { get; init; }

    /// <summary>The frame's delta's word (<c>Frame.Delta</c>, <c>0x800D8094</c>).</summary>
    public byte[]? Delta { get; init; }

    /// <summary>The combat struct's frame counter (<c>+0x5448</c>).</summary>
    public uint? Frame { get; init; }

    /// <summary>The current combatant's slot, and its 0x90 bytes.</summary>
    public int? Current { get; init; }

    public byte[]? Combatant { get; init; }

    /// <summary>The RAM dumped here, by its file's name in the battle's folder.</summary>
    public string? Dump { get; init; }

    /// <summary><c>Combat.Setup</c>'s: the entry points the logger watched, or null from a logger that didn't say.</summary>
    public IReadOnlyList<string>? Watched { get; init; }
}

/// <summary>A function that fills the setup block returned.</summary>
public sealed record ParityLeave(string Name, uint At, uint Seed) : ParityRecord(At);
