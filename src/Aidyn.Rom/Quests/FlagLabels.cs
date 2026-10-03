using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;

namespace Aidyn.Rom.Quests;

/// <summary>
/// What engine code means by a flag, such as "Brenna is in the party", where it says. The importer
/// writes these as the flag records' labels.
/// </summary>
public static class FlagLabels
{
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
                Label((ushort)flag, Bit(range, flag) + " (never written)");
        }
        var cinematics = Cinematics.Read(rom);
        var titles = db.CinematicTitles.ToDictionary(t => t.Flag, t => t.Text.Trim());
        for (var i = 0; i < cinematics.Count; i++)
        {
            var flag = cinematics[i].DoneFlag;
            Label(flag, titles.TryGetValue(flag, out var t) ? $"Saw the cinematic “{t}”" : $"Saw cinematic {i}");
        }
        foreach (var (boss, flag) in EngineFlags.BossFlags)
            Label(flag, $"Beat {names[boss]}");

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
            Label(region.Key, $"World map region {region.Key - firstRegion + 1}");
        foreach (var e in EngineFlags.Writes)
            Label(e.Flag, e.Description);
        foreach (var e in EngineFlags.Reads)
            Label(e.Flag, e.Description);
        return labels;
    }
}
