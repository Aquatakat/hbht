using System.Globalization;
using System.Text;
using Aidyn.Core;
using Aidyn.Plugins;
using Aidyn.Plugins.Saves;
using Aidyn.Rom;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Graphics;
using Aidyn.Rom.Import;
using Aidyn.Rom.Rig;
using Aidyn.Rom.Text;

const string Usage = """
    Usage:
      aidyn identify <rom>
      aidyn import <rom> [--out <dir>]       the ROM as the base plugin the game loads (ADR 0001);
                                           default dir: aidyn.base in the data folder (AIDYN_DATA)
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
      aidyn audio <rom> [<index>...] [--plays <n>] [--out <dir>]
                                           Borg11 samples as WAV, and Borg12 sequences rendered to WAV
                                           (all, or the given archive indices), each played n times
                                           (default 1); default dir: out/<version>/audio
      aidyn flag <flag>...                   what sets, clears and reads each flag, in the installed plugins
      aidyn strings <rom> <table>            one string table, numbered by line
      aidyn strings <rom> [--out <dir>]      every string table as text, a file each;
                                           default dir: out/<version>/strings
      aidyn save new [<save>]                a new game as a save, with the installed plugins;
                                           default: new-game.json in the data folder's saves
      aidyn save from <checkpoint> [<save>]  an ares checkpoint as a save; default: <its label>.json
                                           in the data folder's saves
      aidyn save show <save>                 what the game makes of a save, loaded with the installed plugins
    """;

try
{
    return args switch
    {
        ["identify", var rom] => Identify(rom),
        ["import", var rom, .. var rest] when OutOnly(rest, out var dir) => Import(rom, dir),
        ["borg", "summary", var rom] => BorgSummary(rom),
        ["borg", "dump", var rom, .. var rest] when OutOnly(rest, out var dir) => BorgDump(rom, dir),
        ["db", "dump", var rom, .. var rest] when OutOnly(rest, out var dir) => DbDump(rom, dir),
        ["textures", var rom, .. var rest] when OutOnly(rest, out var dir) => Textures(rom, dir),
        ["scenes", var rom, .. var rest] => Scenes(rom, rest),
        ["dialogue", var rom, .. var rest] => Dialogue(rom, rest),
        ["audio", var rom, .. var rest] => Audio(rom, rest),
        ["flag", .. var flags] when flags.Length > 0 => Flags(flags),
        ["strings", var rom, .. var rest] when OutOnly(rest, out var dir) => AllStrings(rom, dir),
        ["strings", var rom, var table] => Strings(rom, table),
        ["save", "new"] => SaveNew(null),
        ["save", "new", var save] => SaveNew(save),
        ["save", "from", var checkpoint] => SaveFrom(checkpoint, null),
        ["save", "from", var checkpoint, var save] => SaveFrom(checkpoint, save),
        ["save", "show", var save] => SaveShow(save),
        _ => Fail(Usage),
    };
}
catch (UnsupportedRomException e)
{
    return Fail(e.Message);
}
catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException)
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

static int Import(string path, string? outDir)
{
    var rom = RomImage.Load(path);
    outDir ??= DataPaths.Base;
    var watch = System.Diagnostics.Stopwatch.StartNew();
    BaseImporter.Import(rom, outDir, new Progress<string>(step => Console.Error.WriteLine(step)));
    Console.WriteLine($"Imported {rom.Version.Name} to {Path.GetFullPath(outDir)} in {watch.Elapsed.TotalSeconds:F0} s");
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
    outDir ??= OutDir(rom, "borg");

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
    outDir ??= OutDir(rom, "db");
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
    outDir ??= OutDir(rom, "textures");

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

static int Audio(string path, string[] args)
{
    if (!IndicesAndOut(args, out var indices, out var outDir, out var plays, allowPlays: true))
        return Fail(Usage);

    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    outDir ??= OutDir(rom, "audio");
    var all = indices.Count == 0;

    var samples = new Dictionary<int, short[]>();
    short[] Sample(int index) => samples.TryGetValue(index, out var pcm) ? pcm : samples[index] = Aidyn.Rom.Audio.Borg11Sample.Parse(archive.Read(index)).Samples;

    Directory.CreateDirectory(Path.Combine(outDir, "samples"));
    foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Sample && (all || indices.Contains(l.Index))))
        File.WriteAllBytes(Path.Combine(outDir, "samples", $"0x{listing.Index:X4}.wav"), Aidyn.Plugins.Assets.Wav.Encode(Sample(listing.Index), 22050));

    // One sequence at a time, at 44.1 kHz, as the game would play it at full volume in the middle.
    const int rate = 44100;
    var index = new StringBuilder("index,tracks,instruments,looping instruments,seconds,samples\n");
    Directory.CreateDirectory(Path.Combine(outDir, "sequences"));
    foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Sequence && (all || indices.Contains(l.Index))))
    {
        var borg = Aidyn.Rom.Audio.Borg12Sequence.Parse(archive.Read(listing.Index));
        var sequence = new Aidyn.Core.Audio.Sequence(borg.Tracks,
            [.. borg.Instruments.Select(i => new Aidyn.Core.Audio.Instrument(Sample(i.Sample),
                i is { LoopStart: { } start, LoopEnd: { } end } ? new Aidyn.Plugins.Records.SampleLoop(start, end) : null))],
            borg.Stream, borg.LoopPosition);
        var ms = Aidyn.Core.Audio.AudioEngine.Milliseconds(sequence);
        var engine = new Aidyn.Core.Audio.AudioEngine(rate);
        var handle = engine.Play(sequence, loop: plays > 1, durationMs: plays > 1 ? ms * plays : -1);
        var pcm = new List<short>();
        var block = new short[2048];
        while (engine.IsPlaying(handle))
        {
            engine.Render(block);
            pcm.AddRange(block);
        }
        File.WriteAllBytes(Path.Combine(outDir, "sequences", $"0x{listing.Index:X4}.wav"), Aidyn.Plugins.Assets.Wav.EncodeStereo([.. pcm], rate));
        index.Append(CultureInfo.InvariantCulture,
            $"0x{listing.Index:X4},{borg.Tracks},{borg.Instruments.Count},{borg.Instruments.Count(i => i.LoopStart is not null)},{ms / 1000.0:0.00},{string.Join(' ', borg.Instruments.Select(i => $"0x{i.Sample:X4}"))}\n");
    }

    File.WriteAllText(Path.Combine(outDir, "sequences.csv"), index.ToString());
    Console.WriteLine($"Wrote audio to {Path.GetFullPath(outDir)}");
    return 0;
}

static int Scenes(string path, string[] args)
{
    if (!IndicesAndOut(args, out var only, out var outDir, out _, allowPlays: false))
        return Fail(Usage);

    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    outDir ??= OutDir(rom, "scenes");
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
    if (!IndicesAndOut(args, out var only, out var outDir, out _, allowPlays: false))
        return Fail(Usage);

    var rom = RomImage.Load(path);
    var archive = BorgArchive.Open(rom);
    var db = GameDb.Read(rom);
    outDir ??= OutDir(rom, "dialogue");
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

static int Flags(string[] flags)
{
    var plugins = InstalledPlugins();
    var report = new FlagReport(plugins, QuestIndex.Build(plugins));
    foreach (var arg in flags)
    {
        if (!int.TryParse(arg, CultureInfo.InvariantCulture, out var flag) || flag < 0 || flag >= report.Flags.Count)
            return Fail($"\"{arg}\" isn't a flag number (0 to {report.Flags.Count - 1}).");
        Console.WriteLine(report.Describe(flag));
    }
    return 0;
}

/// <summary>The default output folder of a dump: out/&lt;version&gt;/&lt;name&gt;.</summary>
static string OutDir(RomImage rom, string name) => Path.Combine("out", rom.Version.Id.ToString().ToLowerInvariant(), name);

/// <summary>Whether the arguments after the ROM are none, or <c>--out &lt;dir&gt;</c>.</summary>
static bool OutOnly(string[] rest, out string? outDir)
{
    outDir = null;
    switch (rest)
    {
        case []:
            return true;
        case ["--out", var dir]:
            outDir = dir;
            return true;
        default:
            return false;
    }
}

/// <summary>
/// Reads the arguments after the ROM of the commands that take archive indices: the indices (decimal or
/// 0x hex), <c>--out &lt;dir&gt;</c>, and <c>--plays &lt;n&gt;</c> (a positive count, the first one given, split
/// off before the rest are read, and only if <paramref name="allowPlays"/>; <paramref name="plays"/> is 1 without it).
/// </summary>
static bool IndicesAndOut(string[] args, out List<int> indices, out string? outDir, out int plays, bool allowPlays)
{
    indices = [];
    outDir = null;
    plays = 1;
    if (allowPlays && Array.IndexOf(args, "--plays") is var at and >= 0)
    {
        if (at + 1 >= args.Length || !int.TryParse(args[at + 1], CultureInfo.InvariantCulture, out plays) || plays < 1)
            return false;
        args = [.. args[..at], .. args[(at + 2)..]];
    }
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--out" && i + 1 < args.Length)
            outDir = args[++i];
        else if (int.TryParse(args[i], CultureInfo.InvariantCulture, out var index)
            || args[i].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(args[i].AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out index))
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

static int AllStrings(string path, string? outDir)
{
    var rom = RomImage.Load(path);
    outDir ??= OutDir(rom, "strings");
    Directory.CreateDirectory(outDir);
    var lines = 0;
    foreach (var table in Enum.GetValues<RomStringTable>())
    {
        var strings = RomStringBlock.Read(rom, table);
        File.WriteAllLines(Path.Combine(outDir, $"{(int)table:D2}-{table}.txt"), strings.Select((s, i) => $"{i,4}  {s}"));
        lines += strings.Count;
    }
    Console.WriteLine($"Wrote {Enum.GetValues<RomStringTable>().Length} string tables, {lines} lines, to {Path.GetFullPath(outDir)}");
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

static PluginSet InstalledPlugins()
{
    if (!File.Exists(Path.Combine(DataPaths.Base, PluginManifest.FileName)))
        throw new IOException($"The game hasn't been imported. Run: aidyn import <rom>\n(looked in {DataPaths.Base})");
    return PluginSet.LoadInstalled(DataPaths.Plugins);
}

static string SavePath(string? path, string label) => path ?? Path.Combine(DataPaths.Saves, label + SaveFile.Extension);

static int SaveNew(string? path)
{
    path = SavePath(path, "new-game");
    SaveFile.Write(path, GameState.NewGame(InstalledPlugins()).Save());
    Console.WriteLine($"Saved a new game to {Path.GetFullPath(path)}");
    return 0;
}

static int SaveFrom(string checkpoint, string? path)
{
    var label = CheckpointSave.Label(checkpoint);
    path = SavePath(path, label);
    var problems = new List<string>();
    var save = CheckpointSave.Convert(AresCheckpoint.Read(checkpoint), InstalledPlugins(), label, problems);
    SaveFile.Write(path, save);
    foreach (var problem in problems)
        Console.Error.WriteLine(problem);
    Console.WriteLine($"Saved {label} to {Path.GetFullPath(path)}");
    return 0;
}

static int SaveShow(string path)
{
    var game = GameState.Load(InstalledPlugins(), SaveFile.Read(path));
    var played = TimeSpan.FromSeconds(game.PlayTime / 60.0);
    var date = game.Clock.Date;
    Console.WriteLine(game.Label ?? Path.GetFileNameWithoutExtension(path));
    Console.WriteLine($"Played      {(int)played.TotalHours}:{played.Minutes:D2}:{played.Seconds:D2}");
    Console.WriteLine($"Date        season {date.Season}, week {date.Week}, day {date.DayOfWeek}, {date.Hour:D2}:00 ({date.PartOfDay})");
    Console.WriteLine(game.Place is { } place
        ? $"Place       {game.Plugins.TryGet<Aidyn.Plugins.Records.MapSetRecord>(place.MapSet)?.Label ?? place.MapSet.ToString()} {place.Chunk} " +
            $"at ({place.Position.X:F2}, {place.Position.Y:F2}, {place.Position.Z:F2})"
        : "Place       where a new game starts");
    var plain = Enumerable.Range(0, game.Flags.Count).Count(h => !game.Flags.IsDerived(h) && game.Flags.Peek(h));
    var derived = Enumerable.Range(0, game.Flags.Count).Count(h => game.Flags.IsDerived(h) && game.Flags.Get(h));
    Console.WriteLine($"Flags       {plain:N0} plain and {derived:N0} derived set");
    Console.WriteLine($"Gold        {game.Party.Gold:N0}");
    foreach (var member in game.Party.Members)
    {
        var stats = string.Join(" ", Enum.GetValues<Aidyn.Core.Characters.Stat>().Take(6).Select(s => $"{s.ToString()[..3]} {member.Stats.Modified(s)}"));
        Console.WriteLine($"  {member.Name,-10} level {member.Level,2}, {member.Hp}/{member.MaxHp} HP, {member.TotalExp:N0} XP; {stats}");
        var worn = member.Equipment.All.Select(i => i.Item.Name).ToList();
        if (worn.Count > 0)
            Console.WriteLine($"  {"",-10} wears {string.Join(", ", worn)}");
        if (member.Spells.Count > 0)
            Console.WriteLine($"  {"",-10} knows {string.Join(", ", member.Spells.Select(s => $"{s.Spell.Name} {s.Level}"))}");
    }
    var stacks = game.Party.Inventory.Stacks;
    Console.WriteLine($"Inventory   {stacks.Count} {(stacks.Count == 1 ? "stack" : "stacks")}: " +
        string.Join(", ", stacks.Select(s => s.Quantity == 1 ? s.Item.Name : $"{s.Item.Name} × {s.Quantity}")));
    foreach (var memory in game.Memory.Entries)
    {
        var (kind, skill) = (memory.Key.Kind, memory.Key.Skill);
        var what = kind switch
        {
            Aidyn.Core.MapObjects.MemoryKind.RandomLine => $"a random line's roll of {memory.Value}",
            Aidyn.Core.MapObjects.MemoryKind.SkillCheck =>
                $"a {game.Plugins.TryGet<Aidyn.Plugins.Records.SkillRecord>(RecordId.Decimal(RecordTypes.Skill, skill))?.Name ?? $"skill {skill}"} check's step of {memory.Value}",
            Aidyn.Core.MapObjects.MemoryKind.Opened => "being emptied",
            Aidyn.Core.MapObjects.MemoryKind.PutOut => "being put out",
            _ => "being beaten",
        };
        Console.WriteLine($"Remembers   {memory.Key.MapObject}: {what}, for {memory.Lifetime} more map loads");
    }
    foreach (var problem in game.Problems)
        Console.Error.WriteLine(problem);
    return game.Problems.Count == 0 ? 0 : 1;
}
