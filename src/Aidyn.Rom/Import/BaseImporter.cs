using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Import;

/// <summary>
/// Turns a ROM into <c>aidyn.base</c> (ADR 0001): records under <c>records/</c> and converted assets
/// under <c>assets/aidyn/</c>, keyed by record ID. The output is deterministic: the same ROM and
/// <see cref="BasePlugin.ImporterVersion"/> always give the same bytes.
/// </summary>
/// <remarks>
/// The families of records each have an importer, which builds them from what an <see cref="ImportContext"/>
/// reads of the ROM and writes them through it: <see cref="WorldImporter"/>, <see cref="FlagImporter"/>,
/// <see cref="CharacterImporter"/>, <see cref="ItemImporter"/>, <see cref="TextImporter"/>,
/// <see cref="DialogueImporter"/>, <see cref="CinematicImporter"/>, <see cref="MapImporter"/>,
/// <see cref="GraphicsImporter"/> and <see cref="AudioImporter"/>.
/// </remarks>
public static class BaseImporter
{
    /// <summary>
    /// Imports <paramref name="rom"/> into <paramref name="directory"/>, replacing what's there. It
    /// writes to a sibling folder first, so a failed import leaves the old one in place.
    /// </summary>
    public static void Import(RomImage rom, string directory, IProgress<string>? progress = null)
    {
        if (rom.Version.ResearchOnly)
            throw new UnsupportedRomException(rom.Sha1, rom.Header);

        directory = Path.GetFullPath(directory);
        var staging = directory + ".importing";
        if (Directory.Exists(staging))
            Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(staging);

        var context = new ImportContext(rom, staging, progress);
        context.WriteJson(PluginManifest.FileName, new PluginManifest(
            RecordId.BaseNamespace, "Aidyn Chronicles: The First Mage", "1.0.0",
            Import: new ImportInfo(BasePlugin.ImporterVersion, rom.Sha1, rom.Version.Name)));

        WorldImporter.Import(context);
        FlagImporter.Import(context);
        CharacterImporter.Import(context);
        ItemImporter.Import(context);
        TextImporter.Import(context);
        DialogueImporter.Import(context);
        CinematicImporter.Import(context);
        MapImporter.Import(context);
        GraphicsImporter.Import(context);
        AudioImporter.Import(context);

        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
        Directory.Move(staging, directory);
    }
}
