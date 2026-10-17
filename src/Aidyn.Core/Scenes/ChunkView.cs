using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Scenes;

/// <summary>
/// Which of the 3×3 loaded chunks the original draws (<c>RenderZones</c>, <c>FUN_80010598</c>). It
/// always draws the centre chunk, the player's, and a neighbour when it's in the camera's view.
/// </summary>
/// <remarks>
/// <para>
/// The view is a kite on the ground. From the camera, three lines of 200 go along the way it looks
/// and 42.5° either side of it, and two more join the tip of the middle one to the tips of the
/// others. A neighbour is in view when one of those five lines crosses the edge of the chunk grown
/// by 20 on every side. A chunk the kite holds without crossing, which only one far along the look
/// could be, is out of view.
/// </para>
/// <para>
/// The player's chunk's map can also have neighbours drawn out of view as well
/// (<see cref="MapRecord.DrawnNeighbours"/>, Borg9 header byte 0x1B): 21 chunks of the Overworld do, none
/// of Gwernia's. <c>RenderZones</c> (<c>0x8000d574</c>) and <c>RenderTransZones</c> (<c>0x8000dd44</c>) walk
/// the neighbours in their table's order (<c>0x800CD700</c>, <see cref="Neighbours"/>) and draw one if the
/// view crosses it or its bit is set.
/// </para>
/// </remarks>
public static class ChunkView
{
    /// <summary>How far the view's lines reach.</summary>
    public const float Reach = 200;

    /// <summary>How far either side of the look the view's outer lines go, in degrees.</summary>
    public const float HalfAngle = 42.5f;

    /// <summary>How far a chunk is grown on every side for the test.</summary>
    public const float Margin = 20;

    /// <summary>Whether the original draws a chunk.</summary>
    /// <param name="camera">The camera's X and Z, from the centre chunk's corner.</param>
    /// <param name="look">The way it looks, in X and Z (<c>gCamera.rotationXZ</c>), of length 1.</param>
    /// <param name="row">The chunk's row, from the centre chunk's: −1, 0 or 1, along X.</param>
    /// <param name="column">Its column, from the centre chunk's, along Z.</param>
    /// <param name="cellSize">The map set's chunk size in X and Z.</param>
    /// <param name="forced">The neighbours the centre chunk's map has drawn whatever the view.</param>
    public static bool Drawn(Vector2 camera, Vector2 look, int row, int column, Vector2 cellSize, Neighbours forced = Neighbours.None)
    {
        if (row == 0 && column == 0 || (forced & Neighbour(row, column)) != 0)
            return true;

        var min = new Vector2(row * cellSize.X, column * cellSize.Y) - new Vector2(Margin);
        var max = new Vector2((row + 1) * cellSize.X, (column + 1) * cellSize.Y) + new Vector2(Margin);
        var ahead = look * Reach;
        var tip = camera + ahead;
        var left = camera + Rotate(ahead, -HalfAngle);
        var right = camera + Rotate(ahead, HalfAngle);
        ReadOnlySpan<(Vector2 From, Vector2 To)> lines =
            [(camera, tip), (camera, left), (tip, left), (camera, right), (tip, right)];
        foreach (var (from, to) in lines)
        {
            if (Crosses(from, to, min.Y, min.X, max.X) || Crosses(from, to, max.Y, min.X, max.X) ||
                Crosses(Swap(from), Swap(to), min.X, min.Y, max.Y) || Crosses(Swap(from), Swap(to), max.X, min.Y, max.Y))
                return true;
        }
        return false;
    }

    /// <summary>The bit of the neighbour at a row and column from the centre chunk's, or none for the centre.</summary>
    public static Neighbours Neighbour(int row, int column) => (row, column) switch
    {
        (-1, -1) => Neighbours.NorthWest,
        (1, -1) => Neighbours.NorthEast,
        (-1, 1) => Neighbours.SouthWest,
        (1, 1) => Neighbours.SouthEast,
        (0, -1) => Neighbours.North,
        (0, 1) => Neighbours.South,
        (-1, 0) => Neighbours.West,
        (1, 0) => Neighbours.East,
        _ => Neighbours.None,
    };

    /// <summary>
    /// Whether a line crosses <c>y = <paramref name="edge"/></c> between <paramref name="low"/> and
    /// <paramref name="high"/> in X (<c>FUN_80010414</c>). An end exactly on the edge counts as above it.
    /// </summary>
    private static bool Crosses(Vector2 from, Vector2 to, float edge, float low, float high)
    {
        var a = from.Y - edge;
        var b = to.Y - edge;
        if ((a < 0) == (b < 0))
            return false;
        var x = from.X - (from.X - to.X) * (MathF.Abs(a) / (MathF.Abs(a) + MathF.Abs(b)));
        return low <= x && x <= high;
    }

    private static Vector2 Swap(Vector2 v) => new(v.Y, v.X);

    private static Vector2 Rotate(Vector2 v, float degrees)
    {
        var (s, c) = MathF.SinCos(degrees * (MathF.PI / 180));
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }
}
