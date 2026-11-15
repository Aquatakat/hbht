using System.Numerics;
using HBHT.Core.Physics;

namespace HBHT.Core.Trek;

/// <summary>
/// A tour of the walkable space about where the player stands, for the camera's rig (<see cref="CameraTrace"/>): the floor
/// sampled on a grid, the cells he can walk between found from where he is, each cell's clearance from the nearest wall, and
/// a route along the middle of the passages to the furthest place, on to the place furthest from both, and back, stopping
/// at each for a moment as a player exploring would, turning round to go on.
/// </summary>
public static class CameraTour
{
    /// <summary>A waypoint in the world's X and Z, and how many ticks he stands there once he reaches it.</summary>
    public readonly record struct Stop(Vector2 At, int Ticks);

    /// <summary>What the last plan found, for the rig's report.</summary>
    public static string Note { get; private set; } = "";

    /// <summary>
    /// Laps round a piece of furniture whose collision is a hollow ring of walls (GwerniaCastle G01's table), the way a player
    /// runs round a table: its middle found as the mean of the ring's hits from <paramref name="guess"/> at hip height, its
    /// edge by rays out from the middle, and a waypoint every 15° <paramref name="hug"/> off the edge; <paramref name="laps"/>
    /// one way (anticlockwise seen from above with <paramref name="clockwise"/> false), a short stop, then as many the other.
    /// </summary>
    public static IReadOnlyList<Stop> Around(TrekLoop trek, Vector2 guess, float hug, int laps, int pause = 45)
    {
        var world = trek.Chunks.World;
        var y = trek.Player.Sphere.Position.Y;
        var centre = guess;
        float Edge(Vector2 from, float angle)
        {
            var dir = new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle));
            var start = new Vector3(from.X, y, from.Y);
            return SpherePhysics.SweepEitherSide(world, start, start + dir * 6, 0.01f, skipSeeThrough: true) is { } share ? share * 6 : 6;
        }
        // The middle: twice, the mean of the edge's points round the guess.
        for (var pass = 0; pass < 2; pass++)
        {
            var sum = Vector2.Zero;
            for (var k = 0; k < 24; k++)
            {
                var angle = k * MathF.PI / 12;
                sum += centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * Edge(centre, angle);
            }
            centre = sum / 24;
        }
        var ring = new List<Vector2>();
        for (var k = 0; k < 24; k++)
        {
            var angle = k * MathF.PI / 12;
            ring.Add(centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * (Edge(centre, angle) + hug));
        }
        var stops = new List<Stop>();
        foreach (var sense in new[] { 1, -1 })
        {
            for (var lap = 0; lap < laps; lap++)
                for (var k = 0; k < 24; k++)
                    stops.Add(new Stop(ring[((sense * k) % 24 + 24) % 24], 0));
            stops.Add(new Stop(ring[0], pause));
        }
        Note = $"table middle {centre}";
        return stops;
    }

    public static IReadOnlyList<Stop> Plan(TrekLoop trek, float reach = 30, float cell = 0.5f, int pause = 90)
    {
        // The body's width between cells: his own half a unit, or where that can't leave the door he stands at, a little less.
        var plan = Plan(trek, reach, cell, pause, 0.5f);
        return plan.Count > 4 ? plan : Plan(trek, reach, cell, pause, 0.35f);
    }

    private static IReadOnlyList<Stop> Plan(TrekLoop trek, float reach, float cell, int pause, float width)
    {
        var world = trek.Chunks.World;
        var start = trek.Player.Sphere.Position;
        var n = (int)(2 * reach / cell);
        var floor = new float?[n, n];
        Vector3 At(int i, int j, float y) => new(start.X - reach + (i + 0.5f) * cell, y, start.Z - reach + (j + 0.5f) * cell);
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                var top = At(i, j, start.Y + 2.5f);
                if (SpherePhysics.SweepNearest(world, top, top - new Vector3(0, 7, 0), 0.25f, skipSeeThrough: true, minUp: 0.5f, out var point, out _))
                    floor[i, j] = point.Y;
            }
        var si = (int)((start.X - (start.X - reach)) / cell);
        var sj = (int)((start.Z - (start.Z - reach)) / cell);
        // Walkable from where he stands: a step under half a unit up or down, and nothing in the way at knee height.
        var walk = new bool[n, n];
        var queue = new Queue<(int, int)>();
        if (floor[si, sj] is null)
        {
            Note = "no floor under him";
            return [];
        }
        walk[si, sj] = true;
        queue.Enqueue((si, sj));
        (int, int)[] ways = [(1, 0), (-1, 0), (0, 1), (0, -1)];
        while (queue.Count > 0)
        {
            var (i, j) = queue.Dequeue();
            foreach (var (di, dj) in ways)
            {
                var (a, b) = (i + di, j + dj);
                if (a < 0 || b < 0 || a >= n || b >= n || walk[a, b] || floor[a, b] is not { } y || MathF.Abs(y - floor[i, j]!.Value) > 0.45f)
                    continue;
                var from = At(i, j, floor[i, j]!.Value + 0.9f);
                var to = At(a, b, y + 0.9f);
                if (SpherePhysics.Blocked(world, from, to, width, skipSeeThrough: true) || SpherePhysics.Blocked(world, to, from, width, skipSeeThrough: true))
                    continue;
                walk[a, b] = true;
                queue.Enqueue((a, b));
            }
        }
        var walkable = 0;
        foreach (var w in walk)
            walkable += w ? 1 : 0;
        Note = $"{walkable} cells walkable";
        // Clearance: cells to the nearest one he can't walk on.
        var clear = new int[n, n];
        var near = new Queue<(int, int)>();
        for (var i = 0; i < n; i++)
            for (var j = 0; j < n; j++)
            {
                clear[i, j] = walk[i, j] ? int.MaxValue : 0;
                if (!walk[i, j])
                    near.Enqueue((i, j));
            }
        while (near.Count > 0)
        {
            var (i, j) = near.Dequeue();
            foreach (var (di, dj) in ways)
            {
                var (a, b) = (i + di, j + dj);
                if (a < 0 || b < 0 || a >= n || b >= n || clear[a, b] <= clear[i, j] + 1)
                    continue;
                clear[a, b] = clear[i, j] + 1;
                near.Enqueue((a, b));
            }
        }
        // Paths along the middle: a step costs more the nearer a wall.
        (float[,] Cost, (int, int)[,] From) Paths((int, int) source)
        {
            var cost = new float[n, n];
            var back = new (int, int)[n, n];
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                    cost[i, j] = float.MaxValue;
            cost[source.Item1, source.Item2] = 0;
            var open = new PriorityQueue<(int, int), float>();
            open.Enqueue(source, 0);
            while (open.TryDequeue(out var c, out var d))
            {
                if (d > cost[c.Item1, c.Item2])
                    continue;
                foreach (var (di, dj) in ways)
                {
                    var (a, b) = (c.Item1 + di, c.Item2 + dj);
                    if (a < 0 || b < 0 || a >= n || b >= n || !walk[a, b])
                        continue;
                    // Kept off the walls: a cell nearer than three-quarters of a unit to one is walked through only where it must be.
                    var step = d + 1 + 6f / clear[a, b] + (clear[a, b] * cell < 0.75f ? 30 : 0);
                    if (step < cost[a, b])
                    {
                        cost[a, b] = step;
                        back[a, b] = c;
                        open.Enqueue((a, b), step);
                    }
                }
            }
            return (cost, back);
        }
        // The furthest place with room to stand (a unit from any wall), by the path's length.
        (int, int) Furthest(params float[][,] costs)
        {
            (int, int) best = (si, sj);
            var most = -1f;
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                {
                    if (!walk[i, j] || clear[i, j] * cell < 1)
                        continue;
                    var d = costs.Max(c => c[i, j]) == float.MaxValue ? float.MaxValue : costs.Min(c => c[i, j]);
                    if (d < float.MaxValue && d > most)
                        (most, best) = (d, (i, j));
                }
            return best;
        }
        var fromStart = Paths((si, sj));
        var first = Furthest(fromStart.Cost);
        var fromFirst = Paths(first);
        var second = Furthest(fromStart.Cost, fromFirst.Cost);
        var fromSecond = Paths(second);
        var stops = new List<Stop>();
        void Leg((int, int)[,] back, (int, int) source, (int, int) target, int stopTicks)
        {
            var path = new List<(int, int)>();
            for (var c = target; c != source && path.Count < n * n; c = back[c.Item1, c.Item2])
                path.Add(c);
            path.Reverse();
            for (var k = 0; k < path.Count; k++)
                if (k % 3 == 2 || k == path.Count - 1)
                {
                    var p = At(path[k].Item1, path[k].Item2, 0);
                    stops.Add(new Stop(new Vector2(p.X, p.Z), k == path.Count - 1 ? stopTicks : 0));
                }
        }
        Leg(fromStart.From, (si, sj), first, pause);
        Leg(fromFirst.From, first, second, pause);
        Leg(fromSecond.From, second, (si, sj), pause);
        return stops;
    }
}
