using System.Text.Json;
using HBHT.Plugins;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Ui;

/// <summary>
/// A saved game as the save and load screens show it: its file, the save, and its screenshot beside it, if
/// it has one (a PNG of the same name).
/// </summary>
public sealed record SaveSlot(string Path, SaveGame Save, DateTime Written)
{
    /// <summary>Where a save's screenshot goes: beside it, with the same name.</summary>
    public static string ScreenshotPath(string savePath) => System.IO.Path.ChangeExtension(savePath, ".png");

    public string? Screenshot => File.Exists(ScreenshotPath(Path)) ? ScreenshotPath(Path) : null;

    /// <summary>
    /// The saves in a folder, the newest first, leaving out what isn't one or can't be read.
    /// </summary>
    public static IReadOnlyList<SaveSlot> List(string folder)
    {
        if (!Directory.Exists(folder))
            return [];
        var slots = new List<SaveSlot>();
        foreach (var path in Directory.EnumerateFiles(folder, "*" + SaveFile.Extension))
        {
            try
            {
                slots.Add(new SaveSlot(path, SaveFile.Read(path), File.GetLastWriteTimeUtc(path)));
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
            {
            }
        }
        return [.. slots.OrderByDescending(s => s.Written).ThenBy(s => s.Path, StringComparer.Ordinal)];
    }

    /// <summary>A path for a new save in a folder: <c>Save 1</c>, or the first number after that not taken.</summary>
    public static string NewPath(string folder)
    {
        for (var n = 1; ; n++)
        {
            var path = System.IO.Path.Combine(folder, $"Save {n}{SaveFile.Extension}");
            if (!File.Exists(path))
                return path;
        }
    }

    /// <summary>Deletes a save and its screenshot.</summary>
    public static void Delete(string path)
    {
        File.Delete(path);
        File.Delete(ScreenshotPath(path));
    }
}
