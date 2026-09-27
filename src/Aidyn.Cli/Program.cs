using System.Globalization;
using System.Text;
using Aidyn.Rom;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Quests;
using Aidyn.Rom.Text;

const string Usage = """
    Usage:
      aidyn identify <rom>
      aidyn borg summary <rom>
      aidyn borg dump <rom> [--out <dir>]    default dir: out/<version>/borg
      aidyn db dump <rom> [--out <dir>]      default dir: out/<version>/db
      aidyn textures <rom> [--out <dir>]     Borg1 and Borg8 as PNG; default dir: out/<version>/textures
      aidyn scenes <rom> [<index>...] [--out <dir>]
                                           Borg5 scenes as glTF, with the Borg6s that animate them
                                           (all, or the given archive indices); default dir: out/<version>/scenes
      aidyn dialogue <rom> [<index>...] [--out <dir>]
                                           Borg13 scripts as text (all, or the given archive indices);
                                           default dir: out/<version>/dialogue
      aidyn atlas <rom> [--out <dir>]        the quest atlas as JSON, for tools/quest-atlas.html;
                                           default dir: out/<version>/atlas
      aidyn flag <rom> <flag>...             what sets and reads each flag
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
        ["textures", var rom] => Textures(rom, null),
        ["textures", var rom, "--out", var dir] => Textures(rom, dir),
        ["scenes", var rom, .. var rest] => Scenes(rom, rest),
        ["dialogue", var rom, .. var rest] => Dialogue(rom, rest),
        ["atlas", var rom] => Atlas(rom, null),
        ["atlas", var rom, "--out", var dir] => Atlas(rom, dir),
        ["flag", var rom, .. var flags] when flags.Length > 0 => Flags(rom, flags),
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

static int Textures(string path, string? outDir)
{
    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    outDir ??= Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), "textures");

    var index = new StringBuilder("index,type,format,width,height,lods,flags,move\n");
    foreach (var listing in archive.Listings)
    {
        string row;
        RgbaImage image;
        switch (listing.Type)
        {
            case BorgType.Texture:
                var texture = Borg1Texture.Parse(archive.Read(listing.Index));
                image = texture.Decode();
                row = $"{texture.Format},{texture.Width},{texture.Height},{texture.Lods},0x{(int)texture.Flags:X},0x{texture.Move:X2}";
                break;
            case BorgType.UiImage:
                var ui = Borg8Image.Parse(archive.Read(listing.Index));
                image = ui.Decode();
                row = $"{ui.Format},{ui.Width},{ui.Height},,,";
                break;
            default:
                continue;
        }

        var typeDir = Path.Combine(outDir, TypeFolder(listing.Type));
        Directory.CreateDirectory(typeDir);
        File.WriteAllBytes(Path.Combine(typeDir, $"{listing.Index:D5}.png"), Png.Encode(image));
        index.Append(CultureInfo.InvariantCulture, $"{listing.Index},{listing.Type},{row}\n");
    }

    File.WriteAllText(Path.Combine(outDir, "index.csv"), index.ToString());
    Console.WriteLine($"Wrote textures to {Path.GetFullPath(outDir)}");
    return 0;
}

static int Scenes(string path, string[] args)
{
    if (!IndicesAndOut(args, out var only, out var outDir))
        return Fail(Usage);

    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    outDir ??= Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), "scenes");
    Directory.CreateDirectory(outDir);

    var scenes = only.Count > 0 ? only : archive.Listings.Where(l => l.Type == BorgType.Scene).Select(l => l.Index).ToList();
    var animations = archive.Listings.Where(l => l.Type == BorgType.Animation)
        .Select(l => (l.Index, Animation: Borg6Animation.Parse(archive.Read(l.Index))))
        .ToLookup(a => a.Animation.Scene);
    foreach (var index in scenes)
    {
        if (!IsType(archive, index, BorgType.Scene))
            return Fail($"Borg {index} isn't a scene.");
        var scene = Borg5Scene.Parse(archive.Read(index));
        File.WriteAllBytes(Path.Combine(outDir, $"{index:D5}.glb"),
            SceneGltf.Export(scene, archive.Read, index, animations[index].ToList()));
    }

    Console.WriteLine($"Wrote {scenes.Count} scenes to {Path.GetFullPath(outDir)}");
    return 0;
}

static int Dialogue(string path, string[] args)
{
    if (!IndicesAndOut(args, out var only, out var outDir))
        return Fail(Usage);

    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    var db = GameDb.Read(rom);
    outDir ??= Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), "dialogue");
    Directory.CreateDirectory(outDir);

    var names = db.Entities.Select(e => (e.Id, e.Name)).Concat(db.DialogEntities.Select(e => (e.Id, e.Name)))
        .ToDictionary(e => e.Id, e => e.Name);
    var scripts = only.Count > 0 ? only : archive.Listings.Where(l => l.Type == BorgType.Dialogue).Select(l => l.Index).ToList();
    foreach (var index in scripts)
    {
        if (!IsType(archive, index, BorgType.Dialogue))
            return Fail($"Borg {index} isn't a dialogue.");
        var script = Borg13Script.Parse(archive.Read(index));
        File.WriteAllText(Path.Combine(outDir, $"{index:D5}.txt"), Borg13Text.Format(script, index, names.GetValueOrDefault));
    }

    Console.WriteLine($"Wrote {scripts.Count} dialogues to {Path.GetFullPath(outDir)}");
    return 0;
}

static int Atlas(string path, string? outDir)
{
    var rom = RomImage.Load(path);
    var atlas = QuestAtlas.Build(rom);
    outDir ??= Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), "atlas");
    Directory.CreateDirectory(outDir);
    File.WriteAllBytes(Path.Combine(outDir, "atlas.json"), AtlasJson.Serialize(atlas, rom.Version.Name));
    Console.WriteLine($"Wrote the atlas to {Path.GetFullPath(outDir)}: {atlas.References.Count} flag references, " +
        $"{atlas.Scripts.Count} scripts, {atlas.Objects.Count} map objects");
    return 0;
}

static int Flags(string path, string[] flags)
{
    var atlas = QuestAtlas.Build(RomImage.Load(path));
    foreach (var arg in flags)
    {
        if (!int.TryParse(arg, CultureInfo.InvariantCulture, out var flag) || flag < 0 || flag >= atlas.Funnel.Flags.Count)
            return Fail($"\"{arg}\" isn't a flag number (0 to {atlas.Funnel.Flags.Count - 1}).");
        Console.WriteLine(FlagReport.Describe(atlas, flag));
    }
    return 0;
}

static bool IndicesAndOut(string[] args, out List<int> indices, out string? outDir)
{
    indices = [];
    outDir = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--out" && i + 1 < args.Length)
            outDir = args[++i];
        else if (int.TryParse(args[i], CultureInfo.InvariantCulture, out var index))
            indices.Add(index);
        else
            return false;
    }
    return true;
}

static bool IsType(BorgArchive archive, int index, BorgType type) =>
    index >= 0 && index < archive.Count && archive.Listings[index].Type == type;

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
