using System.Numerics;

namespace HBHT.Rom.Graphics;

/// <summary>
/// Meets the water the cut leaves at a cell's edge with the water on the other side, point for point (docs/systems/rendering.md#draw-distance).
/// A square of sea takes, along each edge where it meets a neighbour's own water or fill, every point of that water on the edge and
/// its height there, and eases back to its own height over <see cref="Ease"/> inside; a fill's sea takes a real chunk's points and
/// heights where it meets that chunk's own water. So the two share their edge and nothing shows between them: the steps of a few
/// hundredths to 0.77 (H20's) opened cracks with the sky behind them, and the skirts that hid those showed through the shallows.
/// </summary>
internal static class SeaRims
{
    /// <summary>How far into a square its edge's heights reach, fading to its own height.</summary>
    internal const float Ease = 20;

    /// <summary>How near a point must be to a cell's edge to lie on it.</summary>
    private const float OnLine = 0.01f;

    /// <summary>A cell's four sides: 0 and 1 the lines across X at its first and last row's edge, 2 and 3 across Z at its columns'.</summary>
    internal static readonly (int Row, int Column)[] Toward = [(-1, 0), (1, 0), (0, -1), (0, 1)];

    /// <summary>A piece of water's edge on a cell's side: from <paramref name="T0"/> to <paramref name="T1"/> along it, at those heights.</summary>
    internal readonly record struct Span(float T0, float Y0, float T1, float Y1)
    {
        public float At(float t) => T1 - T0 < 1e-6f ? Math.Max(Y0, Y1) : Y0 + (Y1 - Y0) * (t - T0) / (T1 - T0);
    }

    /// <summary>The water on one side of a cell: its spans along the side, the topmost where they overlap.</summary>
    internal sealed class Profile(IReadOnlyList<Span> spans)
    {
        public IReadOnlyList<Span> Spans { get; } = spans;

        /// <summary>Its height at <paramref name="t"/> along the side, or null where no water lies.</summary>
        public float? At(float t)
        {
            float? best = null;
            foreach (var s in Spans)
            {
                if (t >= s.T0 - OnLine && t <= s.T1 + OnLine)
                    best = best is { } b ? Math.Max(b, s.At(Math.Clamp(t, s.T0, s.T1))) : s.At(Math.Clamp(t, s.T0, s.T1));
            }
            return best;
        }

        /// <summary>Where along the side its points lie, in order, each once.</summary>
        public IReadOnlyList<float> Points =>
            [.. Spans.SelectMany(s => new[] { s.T0, s.T1 }).Order().Aggregate(new List<float>(), (list, t) =>
            {
                if (list.Count == 0 || t - list[^1] > OnLine)
                    list.Add(t);
                return list;
            })];
    }

    /// <summary>Where a point of a cell lies along a side, and how far off its line, in the cell's coordinates.</summary>
    internal static (float Along, float Off) OnSide(Vector3 local, int side, float cellSize) => side switch
    {
        0 => (local.Z, local.X),
        1 => (local.Z, local.X - cellSize),
        2 => (local.X, local.Z),
        _ => (local.X, local.Z - cellSize),
    };

    /// <summary>
    /// The water of polygons, in the cell's coordinates, whose edges lie on <paramref name="side"/> of it, within <paramref name="reach"/> of
    /// <paramref name="height"/> up or down, or null with none.
    /// </summary>
    internal static Profile? Of(IEnumerable<Vector3[]> polygons, int side, float cellSize, float height, float reach)
    {
        var spans = new List<Span>();
        foreach (var polygon in polygons)
        {
            for (var i = 0; i < polygon.Length; i++)
            {
                var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Length]);
                var (ta, oa) = OnSide(a, side, cellSize);
                var (tb, ob) = OnSide(b, side, cellSize);
                if (MathF.Abs(oa) > OnLine || MathF.Abs(ob) > OnLine || MathF.Abs(ta - tb) < OnLine
                    || MathF.Abs(a.Y - height) > reach || MathF.Abs(b.Y - height) > reach)
                    continue;
                var span = ta < tb ? new Span(ta, a.Y, tb, b.Y) : new Span(tb, b.Y, ta, a.Y);
                // On the cell's side alone.
                if (span.T1 <= 0 || span.T0 >= cellSize)
                    continue;
                var (t0, t1) = (Math.Max(0, span.T0), Math.Min(cellSize, span.T1));
                spans.Add(new Span(t0, span.At(t0), t1, span.At(t1)));
            }
        }
        return spans.Count == 0 ? null : new Profile(spans);
    }

    /// <summary>
    /// Polygons in a cell, their points on a side with a profile at that profile's height, and its points put into their edges along
    /// the side; each polygon so changed turned to start at a corner off those edges, as the export fans it from its first. Points
    /// elsewhere are as <paramref name="height"/> has them, or as they were.
    /// </summary>
    internal static List<CutVertex[]> Meet(IEnumerable<CutVertex[]> polygons, IReadOnlyList<Profile?> sides, float cellSize,
        Func<Vector3, float?>? height = null)
    {
        var result = new List<CutVertex[]>();
        foreach (var polygon in polygons)
        {
            var corners = new List<CutVertex>();
            var added = new List<bool>();
            var onSplit = new HashSet<int>();
            for (var i = 0; i < polygon.Length; i++)
            {
                var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Length]);
                corners.Add(a);
                added.Add(false);
                for (var side = 0; side < 4; side++)
                {
                    if (sides[side] is not { } profile)
                        continue;
                    var (ta, oa) = OnSide(a.Position, side, cellSize);
                    var (tb, ob) = OnSide(b.Position, side, cellSize);
                    if (MathF.Abs(oa) > OnLine || MathF.Abs(ob) > OnLine)
                        continue;
                    var between = profile.Points.Where(t => t > Math.Min(ta, tb) + OnLine && t < Math.Max(ta, tb) - OnLine);
                    var inserted = (ta < tb ? between : between.Reverse()).ToList();
                    if (inserted.Count == 0)
                        continue;
                    onSplit.Add(corners.Count - 1);
                    foreach (var t in inserted)
                    {
                        var f = (t - ta) / (tb - ta);
                        corners.Add(new CutVertex(Vector3.Lerp(a.Position, b.Position, f), a.Normal, Vector4.Lerp(a.Color, b.Color, f),
                            Vector2.Lerp(a.St, b.St, f), a.Second));
                        added.Add(true);
                    }
                    onSplit.Add(corners.Count);
                }
            }
            // The heights: a side's where a point lies on it and its water, else the field's.
            for (var i = 0; i < corners.Count; i++)
            {
                var p = corners[i].Position;
                float? y = null;
                for (var side = 0; side < 4 && y is null; side++)
                {
                    var (t, off) = OnSide(p, side, cellSize);
                    if (MathF.Abs(off) <= OnLine && sides[side]?.At(t) is { } at)
                        y = at;
                }
                y ??= height?.Invoke(p);
                if (y is { } h)
                    corners[i] = corners[i] with { Position = p with { Y = h } };
            }
            if (onSplit.Count > 0)
            {
                // Start the fan at a corner that's no end of an edge points went into.
                var ends = onSplit.Select(i => i % corners.Count).ToHashSet();
                var start = Enumerable.Range(0, corners.Count).FirstOrDefault(i => !ends.Contains(i) && !added[i]);
                corners = [.. corners.Skip(start), .. corners.Take(start)];
            }
            result.Add([.. corners]);
        }
        return result;
    }

    /// <summary>
    /// The heights across the squares: each square's own, eased toward its edges' water within <see cref="Ease"/>, by how near each
    /// piece of that water is (exact on it, half its difference at half <see cref="Ease"/> from it). Read at a point in the world, so two
    /// squares sharing an edge put its points at the same heights.
    /// </summary>
    internal sealed class Field(IReadOnlyList<(Vector2 A, Vector2 B, float DA, float DB)> rims)
    {
        public float Offset(Vector2 p)
        {
            var sum = 0f;
            var weights = 1 / (Ease * Ease);
            foreach (var (a, b, da, db) in rims)
            {
                var ab = b - a;
                var f = Math.Clamp(Vector2.Dot(p - a, ab) / Math.Max(ab.LengthSquared(), 1e-9f), 0, 1);
                var d = da + (db - da) * f;
                var distance = Vector2.Distance(p, a + ab * f);
                if (distance < 1e-4f)
                    return d;
                if (distance >= Ease)
                    continue;
                var w = (Ease - distance) / (Ease * distance);
                w *= w;
                sum += w * d;
                weights += w;
            }
            return sum / weights;
        }
    }
}
