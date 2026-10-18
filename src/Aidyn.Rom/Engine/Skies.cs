using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Engine;

/// <summary>
/// The sky each terrain type has (<c>BorgMaps::GetMapTerrain</c>), which the chunk the player is in
/// chooses. A sky is a Borg8 gradient, 12 × 37 RGBA32: rows 0–31 are the sky from top to bottom and
/// rows 32–36 the scenery's tint and the sky, fog, cloud and sun colours (<c>Sky::SetColors</c>),
/// with a column every two hours from midnight.
/// </summary>
public static class Skies
{
    /// <summary>The gradients' Borg8 archive indices by sky number, read from the code segment. Number 0 is unused.</summary>
    public static IReadOnlyList<int> ReadGradients(RomImage rom)
    {
        return Be.S32s(rom.Table(CodeTable.SkyGradients));
    }

    /// <summary>
    /// The sun, moon and stars (<c>skyobjectStructs</c>), through the entry of type 3 that ends the
    /// list. In the ROM the stars come first, then the sun and the moon.
    /// </summary>
    public static IReadOnlyList<SkyObjectEntry> ReadSkyObjects(RomImage rom)
    {
        var data = rom.Table(CodeTable.SkyObjects);
        var objects = new List<SkyObjectEntry>();
        for (var at = 0; at + SkyObjectSize <= data.Length; at += SkyObjectSize)
        {
            var kind = (SkyObjectKind)data[at];
            if (kind == SkyObjectKind.End)
                return objects;
            if (!Enum.IsDefined(kind))
                throw new InvalidDataException($"Sky object at 0x{at:X} has type {(int)kind}.");
            objects.Add(new SkyObjectEntry(kind, Be.F32(data, at + 4), Be.F32(data, at + 8), Be.F32(data, at + 12),
                CodeSegment.Colour(data, at + 16)));
        }
        throw new InvalidDataException("The sky objects don't end.");
    }

    /// <summary>The clouds (<c>gCloudData</c>), up to the entry with image 3 that ends the list.</summary>
    public static IReadOnlyList<CloudEntry> ReadClouds(RomImage rom)
    {
        var data = rom.Table(CodeTable.Clouds);
        var clouds = new List<CloudEntry>();
        for (var at = 0; at + CloudSize <= data.Length; at += CloudSize)
        {
            var image = data[at];
            if (image == CloudImages.Count)
                return clouds;
            if (image > CloudImages.Count)
                throw new InvalidDataException($"Cloud at 0x{at:X} has image {image}.");
            // Bytes 8–11 are y, always 10 and unused, and 16–19 are unused; only the colour's alpha is read.
            clouds.Add(new CloudEntry(image, Be.F32(data, at + 4), Be.F32(data, at + 12), data[at + 23], Be.F32(data, at + 24)));
        }
        throw new InvalidDataException("The clouds don't end.");
    }

    /// <summary>The lens flare's pieces (<c>gLensFlareData</c>): the whole table, eight of them.</summary>
    public static IReadOnlyList<FlareEntry> ReadLensFlare(RomImage rom)
    {
        var data = rom.Table(CodeTable.LensFlare);
        var pieces = new List<FlareEntry>();
        for (var at = 0; at + FlareSize <= data.Length; at += FlareSize)
        {
            if (data[at] >= FlareImages.Count)
                throw new InvalidDataException($"Lens flare piece at 0x{at:X} has image {data[at]}.");
            pieces.Add(new FlareEntry(data[at], Be.F32(data, at + 4), Be.F32(data, at + 8), CodeSegment.Colour(data, at + 12)));
        }
        return pieces;
    }

    private const int SkyObjectSize = 20, CloudSize = 28, FlareSize = 16;

    // Engine code holds the images the sky draws as Borg8 literals. They're carried here as facts,
    // with the decomp's names, and the importer checks each is a Borg8 (UI image).

    /// <summary>The sun's image (<c>BORG8_SkyObjSun</c>).</summary>
    public const int SunImage = 0x1A2;

    /// <summary>A star's image (<c>BORG8_SkyObjStar</c>).</summary>
    public const int StarImage = 0x1A1;

    /// <summary>
    /// The scale <c>Skyobjects::Init</c> gives every star, in place of the table's 0.2. The code sets
    /// it as a literal after the table is read.
    /// </summary>
    public const float StarScale = 0.19f;

    /// <summary>The moon's images by phase (<c>Skyobjects::Init</c>, <c>World::set_moonPhase</c>).</summary>
    public static (int New, int WaxingCrescent, int WaningCrescent, int FirstQuarter, int ThirdQuarter, int Full) MoonImages { get; } =
        (New: 0x194, WaxingCrescent: 0x379F, WaningCrescent: 0x195, FirstQuarter: 0x37A0, ThirdQuarter: 0x196, Full: 0x198);

    /// <summary>The cloud images by <see cref="CloudEntry.Image"/> (<c>BORG8_CloudA</c>, <c>B</c> and <c>C</c>).</summary>
    public static IReadOnlyList<int> CloudImages { get; } = [0x18F, 0x190, 0x191];

    /// <summary>
    /// The lens flare's images by <see cref="FlareEntry.Image"/>: filled, ring and filled again, so
    /// images 0 and 2 are the same Borg8.
    /// </summary>
    public static IReadOnlyList<int> FlareImages { get; } = [0x192, 0x193, 0x192];

    /// <summary>
    /// A terrain type's sky number, and whether the gradient is drawn behind the scenery. Where it
    /// isn't (the original's sky type 4), the background is black and weather is off, but the
    /// colours still come from the gradient. The code holds this as a switch, which this carries as
    /// a fact, checked against the debug ROM's jump table.
    /// </summary>
    public static (int Sky, bool Drawn) ForTerrain(int terrain) => terrain switch
    {
        3 or 26 => (3, true),
        4 => (5, true),
        6 or 11 or 13 or 14 or 15 or 16 => (6, false),
        8 => (7, false),
        9 => (8, false),
        10 => (4, true),
        18 => (2, true),
        _ => (1, true),
    };

    /// <summary>The terrain types, which <c>World::SetTerrain</c> checks.</summary>
    public const int TerrainCount = 28;
}

/// <summary>What a <see cref="SkyObjectEntry"/> is.</summary>
public enum SkyObjectKind : byte
{
    Sun = 0,
    Moon = 1,
    Star = 2,
    /// <summary>Ends the list; it isn't an object.</summary>
    End = 3,
}

/// <summary>
/// The sun, moon or a star, as the table holds it. The angles are in degrees: <see cref="Height"/>
/// is the decomp's <c>pos.x</c> and <see cref="Azimuth"/> its <c>pos.y</c>.
/// </summary>
public readonly record struct SkyObjectEntry(SkyObjectKind Kind, float Scale, float Height, float Azimuth, Rgba Color);

/// <summary>A cloud (<c>cloudStruct</c>): an index into <see cref="Skies.CloudImages"/>, where it starts on the plane (x, z), its alpha and its cover threshold (<c>f3</c>).</summary>
public readonly record struct CloudEntry(int Image, float X, float Z, byte Alpha, float Cover);

/// <summary>A lens flare piece: an index into <see cref="Skies.FlareImages"/>, its distance along the line, its scale and its colour.</summary>
public readonly record struct FlareEntry(int Image, float Distance, float Scale, Rgba Color);
