namespace HBHT.Plugins.Records;

/// <summary>
/// What the outdoor sky draws in front of its gradient, and the lens flare over the scene
/// (<c>aidyn:sky/0</c>, the only one): the sun, moon and stars (<c>skyobjectStructs</c>), the
/// clouds (<c>gCloudData</c>) and the lens flare's pieces (<c>gLensFlareData</c>), with the images
/// engine code draws them with.
/// </summary>
/// <remarks>
/// They're drawn on the original's 320 × 240 screen, by angles from the way the camera looks rather
/// than projected. The sun and moon cross the sky by the time of day, and the stars show between
/// 18:00 and 06:00.
/// </remarks>
public sealed record SkyRecord(
    RecordId Id,
    SkyBody Sun,
    SkyBody Moon,
    MoonImages MoonPhases,
    IReadOnlyList<SkyBody> Stars,
    IReadOnlyList<RecordId> CloudImages,
    IReadOnlyList<Cloud> Clouds,
    IReadOnlyList<RecordId> FlareImages,
    IReadOnlyList<FlarePiece> LensFlare);

/// <summary>
/// The sun, the moon or a star (<c>SkyobjectStruct</c>). Where the sky puts it comes from two
/// angles in degrees: <see cref="Height"/>, which the game replaces with the time of day for the sun
/// and moon, and <see cref="Azimuth"/>.
/// </summary>
/// <param name="Image">What it's drawn with. The moon's is its full phase; <see cref="SkyRecord.MoonPhases"/> has the rest.</param>
/// <param name="Scale">The image's scale on the 320 × 240 screen. Stars are 0.19 (<c>Skyobjects::Init</c> overrides the table's 0.2).</param>
/// <param name="Color">Its colour and alpha. The sun is drawn in the sky's sun colour instead, and stars twinkle in greys.</param>
public sealed record SkyBody(RecordId Image, float Scale, float Height, float Azimuth, Rgba Color);

/// <summary>The moon's images by phase (<c>Skyobjects::Init</c>, <c>World::set_moonPhase</c>).</summary>
public sealed record MoonImages(RecordId New, RecordId WaxingCrescent, RecordId WaningCrescent, RecordId FirstQuarter, RecordId ThirdQuarter, RecordId Full);

/// <summary>
/// A cloud (<c>cloudStruct</c>): an image of <see cref="SkyRecord.CloudImages"/>, placed on a plane
/// around the camera, which the wind moves. The nearer the middle, the higher and larger it's drawn.
/// </summary>
/// <param name="Image">Which of <see cref="SkyRecord.CloudImages"/>.</param>
/// <param name="X">Where it starts, within 750 of the camera.</param>
/// <param name="Alpha">Its alpha before its distance fades it (its colour is the sky's cloud colour).</param>
/// <param name="Cover">
/// The cloud cover from which it shows, 0.2 to 1 (<c>cloudStruct.f3</c>, against <c>gGlobals.cloudsFloat</c>,
/// which nothing sets from 1, so all show).
/// </param>
public sealed record Cloud(int Image, float X, float Z, byte Alpha, float Cover);

/// <summary>
/// One of the lens flare's images (<c>lensflare_data</c>), drawn along the line from the middle of
/// the screen through the sun.
/// </summary>
/// <param name="Image">Which of <see cref="SkyRecord.FlareImages"/>.</param>
/// <param name="Distance">How far along the line, as a multiple of the sun's distance from the middle.</param>
/// <param name="Scale">Its scale when the sun is 80 or more from the middle; nearer, it shrinks, and it isn't drawn at 0.2 or less.</param>
public sealed record FlarePiece(int Image, float Distance, float Scale, Rgba Color);
