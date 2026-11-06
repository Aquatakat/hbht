using System.Numerics;
using HBHT.Plugins;

namespace HBHT.Core.Scenes;

/// <summary>
/// Under Draw Distance's Extended, a port addition, how far the fog reaches in each way round the eye (<see cref="Edges"/>): to
/// the edge of the cells whose scenery is drawn, or the sphere the frame is cut at if that's nearer, so the scenery fades
/// into the sky before the edge of what's drawn, the sea's last square or a row of chunks still to come, rather than
/// showing it (docs/systems/rendering.md#draw-distance). The user's design.
/// </summary>
/// <remarks>
/// <para>
/// A cell that's newly drawn comes in out of the fog rather than at once: its share revealed grows from its near side, at
/// <see cref="RevealSpeed"/> over the speed the player moves at, so the fog's edge moves back at that speed however fast he
/// goes. A cell that's no longer drawn is gone from the edge at once. After a jump, a teleport, a load or a map set changing,
/// every cell drawn is revealed at once, and so is every cell that comes in while the scenery round the 3×3, built off the
/// main thread, arrives: until <see cref="SettleSeconds"/> pass with none coming, counting no frame as more than a tenth of a
/// second, as a load's first frames can be long.
/// </para>
/// <para>
/// The fog never comes nearer than the original's own (<c>floor</c>, the planes' far), and the edges change by no more than
/// <see cref="MaxTurn"/> a radian round the eye, nearer rather than further, so a cell's corner fogs the scenery round it by
/// degrees rather than along a line from the eye. It's only what's drawn, as <see cref="DrawDistance"/> is.
/// </para>
/// </remarks>
public sealed class FogHorizon
{
    /// <summary>How many ways round the eye the edge is measured in, evenly from +X toward +Z.</summary>
    public const int Sectors = 256;

    /// <summary>
    /// How fast a newly drawn cell's edge moves back over the player's own speed, in units a second: twice his walk
    /// (<see cref="Movement.PlayerMotion"/>, about 1.26), the user's choice.
    /// </summary>
    public const float RevealSpeed = 2 * 1.26f;

    /// <summary>How much an edge can change a radian round the eye, toward the nearer.</summary>
    public const float MaxTurn = 800;

    /// <summary>How far the player can move in a frame and still be walking, not jumping, as a share of a cell.</summary>
    public const float JumpShare = 0.5f;

    /// <summary>How long after a jump, or after the last cell to come in since, the cells that come in are revealed at once.</summary>
    public const float SettleSeconds = 3;

    private readonly Dictionary<(int Row, int Column), float> _revealed = [];
    private readonly HashSet<(int Row, int Column)> _drawn = [];
    private RecordId? _set;
    private Vector2? _focus;
    private float _speed;
    private float _sinceJump;

    /// <summary>How far the fog reaches each way round the eye, from +X toward +Z, between the floor and the radius given.</summary>
    public float[] Edges { get; } = new float[Sectors];

    /// <summary>How much of a drawn cell is revealed, 0 to 1, or 0 for one that isn't drawn.</summary>
    public float Revealed(int row, int column) => _revealed.GetValueOrDefault((row, column));

    /// <summary>Forgets what's drawn, so the next <see cref="Step"/> reveals every cell at once.</summary>
    public void Reset()
    {
        _revealed.Clear();
        _focus = null;
        _speed = 0;
    }

    /// <summary>
    /// Steps the cells' reveal by a frame and measures the edges from the eye.
    /// </summary>
    /// <param name="seconds">The frame's length.</param>
    /// <param name="set">What the cells belong to, the map set: a change reveals every cell at once.</param>
    /// <param name="drawn">The cells whose scenery is drawn, by row and column.</param>
    /// <param name="cellSize">A cell's width: cell (row, column) spans X from row × size and Z from column × size.</param>
    /// <param name="eye">The eye on the ground, X and Z.</param>
    /// <param name="focus">The player on the ground, X and Z, whose speed the edges move back over.</param>
    /// <param name="floor">How near the fog's edge can come: the original's far plane.</param>
    /// <param name="radius">How far it can go: the sphere the frame is cut at.</param>
    public void Step(float seconds, RecordId set, IEnumerable<(int Row, int Column)> drawn, float cellSize, Vector2 eye,
        Vector2 focus, float floor, float radius)
    {
        _drawn.Clear();
        _drawn.UnionWith(drawn);
        var jumped = _focus is not { } was || set != _set || Vector2.Distance(was, focus) > JumpShare * cellSize;
        if (!jumped && seconds > 0)
        {
            // Smoothed over about a fifth of a second, so the step of a frame's motion doesn't jitter the edge.
            var moved = Vector2.Distance(_focus!.Value, focus) / seconds;
            _speed += (moved - _speed) * Math.Min(seconds / 0.2f, 1);
        }
        else
        {
            _speed = 0;
        }
        _set = set;
        _focus = focus;
        _sinceJump = jumped ? 0 : _sinceJump + Math.Min(seconds, 0.1f);
        var settling = _sinceJump < SettleSeconds;
        var arrived = false;

        foreach (var cell in _revealed.Keys.Where(c => !_drawn.Contains(c)).ToList())
            _revealed.Remove(cell);
        var step = (_speed + RevealSpeed) * seconds / cellSize;
        foreach (var cell in _drawn)
        {
            if (_revealed.TryGetValue(cell, out var share))
            {
                _revealed[cell] = Math.Min(share + step, 1);
            }
            else
            {
                _revealed[cell] = settling ? 1 : 0;
                arrived |= settling;
            }
        }
        if (arrived)
            _sinceJump = 0;

        for (var i = 0; i < Sectors; i++)
        {
            var angle = i * MathF.Tau / Sectors;
            Edges[i] = Math.Clamp(Cast(eye, new Vector2(MathF.Cos(angle), MathF.Sin(angle)), cellSize, radius), floor, radius);
        }
        Ease(Edges, MaxTurn * MathF.Tau / Sectors);
    }

    /// <summary>
    /// How far a ray from the eye goes over drawn cells before it reaches one that isn't, or into one still being revealed as
    /// far as it's revealed, up to <paramref name="limit"/>.
    /// </summary>
    private float Cast(Vector2 eye, Vector2 way, float size, float limit)
    {
        var row = (int)MathF.Floor(eye.X / size);
        var column = (int)MathF.Floor(eye.Y / size);
        var stepRow = way.X > 0 ? 1 : -1;
        var stepColumn = way.Y > 0 ? 1 : -1;
        var nextRow = MathF.Abs(way.X) < 1e-6f ? float.PositiveInfinity : ((row + (stepRow > 0 ? 1 : 0)) * size - eye.X) / way.X;
        var nextColumn = MathF.Abs(way.Y) < 1e-6f ? float.PositiveInfinity : ((column + (stepColumn > 0 ? 1 : 0)) * size - eye.Y) / way.Y;
        var acrossRow = size / MathF.Abs(way.X);
        var acrossColumn = size / MathF.Abs(way.Y);
        var t = 0f;
        while (t < limit)
        {
            var exit = MathF.Min(nextRow, nextColumn);
            var share = _revealed.GetValueOrDefault((row, column));
            if (share < 1)
                return t + share * (exit - t);
            t = exit;
            if (nextRow < nextColumn)
            {
                row += stepRow;
                nextRow += acrossRow;
            }
            else
            {
                column += stepColumn;
                nextColumn += acrossColumn;
            }
        }
        return limit;
    }

    /// <summary>
    /// Brings each edge in to within <paramref name="most"/> of its neighbours', round the circle, never out: two passes each
    /// way, the second carrying what wraps past the first's start.
    /// </summary>
    internal static void Ease(float[] edges, float most)
    {
        var n = edges.Length;
        for (var pass = 0; pass < 2 * n; pass++)
        {
            var i = pass % n;
            edges[i] = MathF.Min(edges[i], edges[(i + n - 1) % n] + most);
        }
        for (var pass = 2 * n - 1; pass >= 0; pass--)
        {
            var i = pass % n;
            edges[i] = MathF.Min(edges[i], edges[(i + 1) % n] + most);
        }
    }
}
