using System.Globalization;
using System.Text;
using Aidyn.Rom;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Text;

const string Usage = """
    Usage:
      aidyn identify <rom>
      aidyn borg summary <rom>
      aidyn borg dump <rom> [--out <dir>]    default dir: out/<version>/borg
      aidyn db dump <rom> [--out <dir>]      default dir: out/<version>/db
      aidyn strings <rom> <table>
    """;

try
{
    return args switch
    {
        ["identify", var rom] => Identify(rom),
        ["borg", "summary", var rom] => BorgSummary(rom),
        ["borg", "dump", var rom] => BorgDump(rom, null),
        ["borg", "dump", var rom, "--out", var dir] => BorgDump(rom, dir),
        ["db", "dump", var rom] => DbDump(rom, null),
        ["db", "dump", var rom, "--out", var dir] => DbDump(rom, dir),
        ["strings", var rom, var table] => Strings(rom, table),
        _ => Fail(Usage),
    };
}
catch (UnsupportedRomException e)
{
    return Fail(e.Message);
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
{
    return Fail(e.Message);
}

static int Fail(string message)
{
    Console.Error.WriteLine(message);
    return 1;
}

static int Identify(string path)
{
    var rom = RomImage.Load(path);
    Console.WriteLine($"Version     {rom.Version.Name}{(rom.Version.ResearchOnly ? " (research only)" : "")}");
    Console.WriteLine($"SHA-1       {rom.Sha1}");
    Console.WriteLine($"Byte order  {rom.OriginalByteOrder}");
    Console.WriteLine($"Name        {rom.Header.Name}");
    Console.WriteLine($"Game code   {rom.Header.GameCode}");
    Console.WriteLine($"Revision    {rom.Header.Revision}");
    Console.WriteLine($"CRC         {rom.Header.Crc1:X8} {rom.Header.Crc2:X8}");
    return 0;
}

static int BorgSummary(string path)
{
    var archive = BorgArchive.Open(RomImage.Load(path));
    Console.Write(Summary(archive));
    return 0;
}

static int BorgDump(string path, string? outDir)
{
    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    outDir ??= Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), "borg");

    var index = new StringBuilder("index,type,compression,stored_size,size,offset\n");
    foreach (var listing in archive.Listings)
    {
        var typeDir = Path.Combine(outDir, TypeFolder(listing.Type));
        Directory.CreateDirectory(typeDir);
        File.WriteAllBytes(Path.Combine(typeDir, $"{listing.Index:D5}.bin"), archive.Read(listing.Index));
        index.Append(CultureInfo.InvariantCulture,
            $"{listing.Index},{listing.Type},{listing.Compression},{listing.CompressedSize},{listing.UncompressedSize},0x{listing.Offset:X}\n");

        if (listing.Index % 1000 == 999)
            Console.Error.Write($"\r{listing.Index + 1}/{archive.Count}");
    }
    Console.Error.WriteLine($"\r{archive.Count}/{archive.Count}");

    var summary = Summary(archive);
    File.WriteAllText(Path.Combine(outDir, "index.csv"), index.ToString());
    File.WriteAllText(Path.Combine(outDir, "summary.txt"), summary);
    Console.Write(summary);
    Console.WriteLine($"Wrote {archive.Count} Borgs to {Path.GetFullPath(outDir)}");
    return 0;
}

static int DbDump(string path, string? outDir)
{
    var rom = RomImage.Load(path);
    var db = GameDb.Read(rom);
    outDir ??= Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), "db");
    Directory.CreateDirectory(outDir);

    var tables = new (string Name, object Value)[]
    {
        ("entities", db.Entities), ("weapons", db.Weapons), ("armor", db.Armor), ("spells", db.Spells),
        ("gear", db.Gear), ("loot", db.Loot), ("shops", db.Shops), ("dialog_entities", db.DialogEntities),
        ("game_state_funnel", db.GameStateFunnel), ("journal", db.Journal), ("cinematic_titles", db.CinematicTitles),
        ("captions", db.Captions), ("camp_encounters", db.CampEncounters), ("strings", db.Strings),
    };
    foreach (var (name, value) in tables)
        File.WriteAllText(Path.Combine(outDir, name + ".json"), DbJson.Serialize(value));

    Console.WriteLine($"Wrote {tables.Length} tables to {Path.GetFullPath(outDir)}");
    return 0;
}

static int Strings(string path, string tableName)
{
    if (!Enum.TryParse<RomStringTable>(tableName, ignoreCase: true, out var table))
        return Fail($"Unknown string table \"{tableName}\". Tables: {string.Join(", ", Enum.GetNames<RomStringTable>())}.");

    var strings = RomStringBlock.Read(RomImage.Load(path), table);
    for (var i = 0; i < strings.Count; i++)
        Console.WriteLine($"{i,4}  {strings[i]}");
    return 0;
}

static string TypeFolder(BorgType type) => $"{(int)type:D2}-{type}";

static string Summary(BorgArchive archive)
{
    var s = new StringBuilder();
    s.AppendLine($"{"Type",-18} {"Count",6} {"Raw",8} {"Stored KB",10} {"Size KB",10} {"Smallest",9} {"Largest",9}");
    foreach (var group in archive.Listings.GroupBy(l => l.Type).OrderBy(g => g.Key))
        s.AppendLine(Row(TypeFolder(group.Key), group.ToList()));
    s.AppendLine(Row("Total", archive.Listings));
    return s.ToString();

    static string Row(string label, IReadOnlyList<BorgListing> ls) =>
        $"{label,-18} {ls.Count,6} {ls.Count(l => l.Compression == BorgCompression.None),8} " +
        $"{ls.Sum(l => (long)l.CompressedSize) / 1024,10:N0} {ls.Sum(l => (long)l.UncompressedSize) / 1024,10:N0} " +
        $"{ls.Min(l => l.UncompressedSize),9:N0} {ls.Max(l => l.UncompressedSize),9:N0}";
}
