namespace Aidyn.Rom.Tests;

/// <summary>
/// Finds the user's ROMs for tests that need real data. They're read from <c>roms/</c> at the
/// repository root, or from <c>AIDYN_ROMS</c> if set, and identified by hash rather than name.
/// Tests that need a missing ROM are skipped: ROM-derived fixtures can't be committed (ADR 0001).
/// </summary>
internal static class TestRoms
{
    private static readonly Lazy<(string Folder, Dictionary<RomVersionId, RomImage> Images)> Found = new(Scan);

    public static string Folder => Found.Value.Folder;

    public static RomImage Get(RomVersionId id)
    {
        if (!Found.Value.Images.TryGetValue(id, out var image))
            Assert.Skip($"No {RomVersions.Get(id).Name} ROM in {Folder}.");
        return image;
    }

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
