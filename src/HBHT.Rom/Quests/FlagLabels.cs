using HBHT.Rom.Db;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Quests;

/// <summary>
/// The flags' labels, which the importer writes into the flag records (docs/systems/flags.md#labels). The ROM names no
/// flags: those whose meaning a table in the code segment gives, such as "Brenna is in the party", are made from it,
/// and the rest are written by hand in <c>FlagNames.tsv</c>.
/// </summary>
public static class FlagLabels
{
    /// <summary>The names <c>FlagNames.tsv</c> gives, by flag: every used flag no table names.</summary>
    public static IReadOnlyDictionary<ushort, string> Written { get; } = ReadWritten();

    private static Dictionary<ushort, string> ReadWritten()
    {
        using var stream = typeof(FlagLabels).Assembly.GetManifestResourceStream("HBHT.Rom.Quests.FlagNames.tsv")
            ?? throw new InvalidOperationException("FlagNames.tsv isn't embedded.");
        using var reader = new StreamReader(stream);
        var names = new Dictionary<ushort, string>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var tab = line.IndexOf('\t');
            if (tab < 0 || !ushort.TryParse(line.AsSpan(0, tab), out var flag) || tab == line.Length - 1 || !names.TryAdd(flag, line[(tab + 1)..]))
                throw new InvalidDataException($"FlagNames.tsv: \"{line}\" isn't a flag's number, a tab and its name, or names a flag again.");
        }
        return names;
    }

    public static IReadOnlyDictionary<ushort, string> Read(RomImage rom, GameDb db)
    {
        var tables = EngineTables.Read(rom);
        var names = db.Entities.Select(e => (e.Id, e.Name))
            .Concat(db.DialogEntities.Select(e => (e.Id, e.Name)))
            .Concat(db.Weapons.Select(e => (e.Id, e.Name)))
            .Concat(db.Armor.Select(e => (e.Id, e.Name)))
            .Concat(db.Gear.Select(e => (e.Id, e.Name)))
            .Concat(db.Spells.Select(e => (e.Id, e.Name)))
            .Concat(db.Loot.Select(e => (e.Id, e.Name)))
            .DistinctBy(e => e.Id)
            .ToDictionary(e => e.Id, e => e.Name);

        // The first label a flag gets stays. Flags 0 and 1 are constants and get none.
        var labels = new Dictionary<ushort, string>();
        void Label(ushort flag, string label)
        {
            if (flag >= 2)
                labels.TryAdd(flag, label);
        }

        static string Bit(EngineFlagRange range, int flag) =>
            range.Bits == 1 ? range.Description : $"{range.Description}, bit {flag - range.First} of {range.Bits}";
        foreach (var range in EngineFlags.Ranges)
        {
            for (var flag = range.First; flag <= range.Last; flag++)
                Label((ushort)flag, Bit(range, flag));
        }
        foreach (var range in EngineFlags.NeverWritten)
        {
            for (var flag = range.First; flag <= range.Last; flag++)
                Label((ushort)flag, Bit(range, flag));
        }
        var cinematics = Cinematics.Read(rom);
        var titles = db.CinematicTitles.ToDictionary(t => t.Flag, t => t.Text.Trim());
        for (var i = 0; i < cinematics.Count; i++)
        {
            var flag = cinematics[i].DoneFlag;
            Label(flag, titles.TryGetValue(flag, out var t) ? $"Saw the cinematic “{t}”" : $"Saw cinematic {i}");
        }
        foreach (var (boss, flag) in EngineFlags.BossFlags)
            Label(flag, $"Defeated {names[boss]}");

        foreach (var m in tables.Party)
        {
            var who = names[m.Member];
            Label(m.Joined, $"{who} is in the party");
            if (m.Left != 0)
                Label(m.Left, $"{who} left the party");
            Label(m.Died, $"{who} is dead");
        }
        foreach (var k in tables.KeyItems)
            Label(k.Held, $"Has {names.GetValueOrDefault(k.Item, k.Item.ToString())}");
        var firstRegion = tables.Regions.Min(r => r.Flag);
        foreach (var region in tables.Regions.GroupBy(r => r.Flag))
            Label(region.Key, $"Revealed world map region {region.Key - firstRegion + 1}");
        foreach (var e in EngineFlags.Writes)
            Label(e.Flag, e.Description);
        foreach (var e in EngineFlags.Reads)
            Label(e.Flag, e.Description);
        foreach (var (flag, name) in Written)
        {
            if (flag < 2 || !labels.TryAdd(flag, name))
                throw new InvalidDataException($"FlagNames.tsv names flag {flag}, which {(flag < 2 ? "is a constant" : $"a table names: {labels[flag]}")}.");
        }
        return labels;
    }
}
