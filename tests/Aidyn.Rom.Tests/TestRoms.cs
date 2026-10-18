using Aidyn.Plugins;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Finds the user's ROMs for tests that need real data. They're read from <c>roms/</c> at the
/// repository root, or from <c>AIDYN_ROMS</c> if set, and identified by hash rather than name.
/// Tests that need a missing ROM are skipped: ROM-derived fixtures can't be committed (ADR 0001).
/// It also holds what the ROM tests share: the versions to run over, and the checkpoints next to the ROMs.
/// </summary>
public static class TestRoms
{
    private static readonly Lazy<(string Folder, Dictionary<RomVersionId, RomImage> Images)> Found = new(Scan);

    public static string Folder => Found.Value.Folder;

    public static RomImage Get(RomVersionId id)
    {
        if (!Found.Value.Images.TryGetValue(id, out var image))
            Assert.Skip($"No {RomVersions.Get(id).Name} ROM in {Folder}.");
        return image;
    }

    /// <summary>Every ROM version, for <c>[MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]</c>.</summary>
    public static TheoryData<RomVersionId> Versions() => [.. RomVersions.All.Select(v => v.Id)];

    /// <summary>
    /// The names of the ares save states next to the ROMs (<c>… - label.bs1</c> to <c>.bs4</c>), for a theory over each.
    /// A theory with no checkpoints gets one empty name, which <see cref="Checkpoint"/> skips.
    /// </summary>
    public static TheoryData<string> Checkpoints()
    {
        var data = new TheoryData<string>();
        var names = Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder).Select(Path.GetFileName).OfType<string>()
                .Where(n => n.Contains(" - ") && n.Length > 4 && n[^4..^1] == ".bs" && n[^1] is >= '1' and <= '4')
                .Order(StringComparer.Ordinal)
            : Enumerable.Empty<string>();
        foreach (var name in names)
            data.Add(name);
        if (data.Count == 0)
            data.Add("");
        return data;
    }

    /// <summary>The checkpoint with this file name, or a skip if there is none: the empty name <see cref="Checkpoints"/> gives, or a file that isn't there.</summary>
    public static AresCheckpoint Checkpoint(string name)
    {
        if (name.Length == 0)
            Assert.Skip($"No checkpoints in {Folder}.");
        var path = Path.Combine(Folder, name);
        if (!File.Exists(path))
            Assert.Skip($"No {name} in {Folder}.");
        return AresCheckpoint.Read(path);
    }

    /// <summary>A line as the user reads it: without the button glyphs, and trimmed.</summary>
    public static string Plain(string line) => new string([.. line.Where(c => c is < '\u0080' or > '\u009F')]).Trim();

    /// <summary>The record of the item a checkpoint's 16-bit item ID names: a weapon, armor or other item, by its high byte.</summary>
    public static RecordId ItemId(ushort id) => (id >> 8) switch
    {
        7 => RecordId.Hex(RecordTypes.Weapon, id),
        5 or 6 => RecordId.Hex(RecordTypes.Armor, id),
        _ => RecordId.Hex(RecordTypes.Item, id),
    };

    private static (string, Dictionary<RomVersionId, RomImage>) Scan()
    {
        var folder = Environment.GetEnvironmentVariable("AIDYN_ROMS") ?? Path.Combine(RepoRoot(), "roms");
        var images = new Dictionary<RomVersionId, RomImage>();
        if (!Directory.Exists(folder))
            return (folder, images);

        foreach (var path in Directory.EnumerateFiles(folder))
        {
            if (Path.GetExtension(path).ToLowerInvariant() is not (".z64" or ".v64" or ".n64"))
                continue;
            try
            {
                var image = RomImage.Load(path);
                images.TryAdd(image.Version.Id, image);
            }
            catch (Exception e) when (e is UnsupportedRomException or InvalidDataException)
            {
            }
        }
        return (folder, images);
    }

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Aidyn.slnx")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Can't find the repository root (Aidyn.slnx).");
    }
}
