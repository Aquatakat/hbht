using System.Numerics;

namespace Aidyn.Core.Sky;

/// <summary>
/// A terrain's sky image (<c>Sky::loadDay</c>): 12 columns, one every two hours from midnight, of
/// 37 rows. Rows 0–31 are the sky from top to bottom, and rows 32–36 the scenery's tint and the
/// sky, fog, cloud and sun colours. Colours are the values the N64 puts on screen, 0 to 1.
/// </summary>
public sealed class SkyGradient
{
    public const int Columns = 12, Rows = 37, SkyRows = 32;
    public const int TintRow = 32, SkyRow = 33, FogRow = 34, CloudRow = 35, SunRow = 36;

    private readonly Vector3[] _texels;

    /// <param name="rgba">The image's pixels, 8-bit RGBA, row by row.</param>
    public SkyGradient(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != Columns * Rows * 4)
            throw new ArgumentException($"A sky gradient is {Columns} × {Rows} RGBA, not {rgba.Length} bytes.", nameof(rgba));
        _texels = new Vector3[Columns * Rows];
        for (var i = 0; i < _texels.Length; i++)
            _texels[i] = new Vector3(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2]) / 255;
    }

    public Vector3 this[int column, int row] => _texels[row * Columns + column];

    /// <summary>
    /// A row at a time of day, between the columns either side of it, and wrapping from 22:00 to
    /// midnight (<c>Sky::GenerateSky</c>).
    /// </summary>
    public Vector3 Sample(int row, float timeOfDay)
    {
        var x = timeOfDay * Columns;
        var column = (int)x % Columns;
        return Vector3.Lerp(this[column, row], this[(column + 1) % Columns, row], x - (int)x);
    }
}
