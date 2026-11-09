using System.Globalization;
using Godot;
using HBHT.Core.Cameras;
using HBHT.Core.Combat;
using HBHT.Views;
using N = System.Numerics;

namespace HBHT.Game;

/// <summary>
/// The F3 overlay's drawing of what Camera: Enhanced, or Start Early's director, is trying to do in a battle (<see cref="CameraIntent"/>).
/// On the screen: a ring round each body it tries to hold (yellow, a pack held where it can be orange, the target red), the director's
/// beats (pink playing, violet to come, with how soon), the frame the lens holds them within, where the goal and the shot shown aim, and
/// an AI's walk's rail. In the corner, a map from above turned with the view: the bodies where they're drawn, the eye shown and what it
/// sees, the goal's eye and what it would, the vantage, the way over the shoulder with the framings held and eased onto, the walk's rail
/// with where its eye starts and ends, and the beats. A debug build's alone, as the overlay is.
/// </summary>
public partial class CameraIntentView : Control
{
    private static readonly Color Held = new(1, 0.9f, 0.2f), Lesser = new(1, 0.55f, 0.1f), TargetRed = new(1, 0.25f, 0.25f),
        Shown = Colors.White, GoalCyan = new(0.3f, 0.95f, 1), VantageMagenta = new(1, 0.35f, 1), FollowGreen = new(0.4f, 1, 0.4f),
        Party = new(0.4f, 0.65f, 1), Enemy = new(1, 0.4f, 0.4f), Dead = new(0.5f, 0.5f, 0.5f), Frame = new(1, 1, 1, 0.5f),
        RailOrange = new(1, 0.6f, 0.2f), Legend = new(1, 1, 1, 0.7f), BeatPink = new(1, 0.3f, 0.75f), ComingViolet = new(0.7f, 0.55f, 1);

    /// <summary>The map's size and margin, and the legend's text, as shares of the view's height.</summary>
    private const float MapShare = 0.32f, MarginShare = 0.012f, TextShare = 0.0135f;

    private BattleDrawing? _drawing;

    /// <summary>A pixel of a 960-line view, in this one's.</summary>
    private float _px = 1;
    private Transform3D _view;
    private float _fov = 45;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>
    /// Draws the battle's camera's intent from the view drawn this frame, or nothing for no battle under Camera: Enhanced or Start Early
    /// (<see cref="BattleDrawing.Enhanced"/>).
    /// </summary>
    public void Update(BattleDrawing? drawing, Transform3D view, float fov)
    {
        _drawing = drawing?.Enhanced is not null ? drawing : null;
        (_view, _fov) = (view, fov);
        QueueRedraw();
    }

    /// <summary>Where a combatant is drawn now, or null for one not drawn.</summary>
    private N.Vector3? Drawn(Combatant? c)
    {
        if (c is null || _drawing is null)
            return null;
        foreach (var b in _drawing.Frame.Bodies)
        {
            if (b.Combatant == c)
                return b.Centre;
        }
        return null;
    }

    public override void _Draw()
    {
        if (_drawing?.Enhanced?.Intent is not { } intent || intent.Lens.FieldOfView <= 0)
            return;
        _px = MathF.Max(1, GetViewportRect().Size.Y / 960);
        try
        {
            OnScreen(intent);
            Map(intent);
        }
        catch (Exception e)
        {
            GD.PrintErr($"The camera's overlay: {e}");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // On the screen

    private void OnScreen(CameraIntent intent)
    {
        var size = GetViewportRect().Size;
        var lens = intent.Lens;
        // The lens's frame: its margin across, up and down to its floor, over the view's own width.
        var across = lens.Aspect * lens.Margin / (size.X / size.Y);
        var (top, bottom) = ((1 - lens.Margin) / 2 * size.Y, (1 + lens.Floor) / 2 * size.Y);
        var (left, right) = ((1 - across) / 2 * size.X, (1 + across) / 2 * size.X);
        DrawDashedLine(new(left, top), new(right, top), Frame, 2 * _px, 8 * _px);
        DrawDashedLine(new(right, top), new(right, bottom), Frame, 2 * _px, 8 * _px);
        DrawDashedLine(new(right, bottom), new(left, bottom), Frame, 2 * _px, 8 * _px);
        DrawDashedLine(new(left, bottom), new(left, top), Frame, 2 * _px, 8 * _px);

        var target = Drawn(intent.Target);
        for (var i = 0; i < intent.Subjects.Count; i++)
        {
            var s = intent.Subjects[i];
            if (Project(s.At) is not { } at)
                continue;
            var radius = Project(s.At + Right() * MathF.Max(s.Radius, 0.2f)) is { } edge ? MathF.Max(at.DistanceTo(edge), 4) : 8;
            var colour = target is { } t && N.Vector3.Distance(t, s.At) < 0.01f ? TargetRed : s.Weight < 1 ? Lesser : Held;
            DrawArc(at, radius, 0, Mathf.Tau, 32, colour, (i == 0 ? 3 : 2) * _px);
        }
        if (target is { } aimedAt && !intent.Subjects.Any(s => N.Vector3.Distance(s.At, aimedAt) < 0.01f) && Project(aimedAt) is { } tp)
            DrawArc(tp, 10, 0, Mathf.Tau, 24, TargetRed, _px);
        // The director's beats: pink playing, violet to come, with how many seconds off.
        var font = ThemeDB.FallbackFont;
        foreach (var (s, kind, n) in intent.Beats)
        {
            if (Project(s.At) is not { } at)
                continue;
            var radius = Project(s.At + Right() * MathF.Max(s.Radius, 0.2f)) is { } edge ? MathF.Max(at.DistanceTo(edge), 4) + 4 : 12;
            var colour = n <= 0 ? BeatPink : ComingViolet;
            if (n <= 0)
                DrawArc(at, radius, 0, Mathf.Tau, 32, colour, 3 * _px);
            else
                DrawDashedArc(at, radius, colour);
            var label = n <= 0 ? kind.ToString().ToLowerInvariant() : string.Create(CultureInfo.InvariantCulture, $"{kind.ToString().ToLowerInvariant()} in {n / 60f:F1}s");
            DrawString(font, at + new Vector2(radius + 3, -radius), label, HorizontalAlignment.Left, -1, (int)(12 * _px), colour);
        }
        if (intent.Rail.Count > 1)
        {
            for (var i = 1; i < intent.Rail.Count; i++)
            {
                if (Project(intent.Rail[i - 1]) is { } a && Project(intent.Rail[i]) is { } b)
                    DrawLine(a, b, RailOrange, 2 * _px);
            }
        }
        if (Project(intent.Goal.Aim) is { } goal)
            Cross(goal, 9 * _px, GoalCyan, 2);
        if (Project(intent.Shown.Aim) is { } shown)
            Cross(shown, 5 * _px, Shown, 1);
        if (intent.Vantage is { } v && Project(new N.Vector3(v.X, intent.Shown.Aim.Y, v.Y)) is { } vantage)
            Diamond(vantage, 7 * _px, VantageMagenta);
    }

    /// <summary>A ring of dashes, for a beat to come.</summary>
    private void DrawDashedArc(Vector2 at, float radius, Color colour)
    {
        const int dashes = 12;
        for (var i = 0; i < dashes; i++)
        {
            var a = Mathf.Tau * i / dashes;
            DrawArc(at, radius, a, a + Mathf.Tau / dashes / 2, 4, colour, 2 * _px);
        }
    }

    /// <summary>The view's right, across the world.</summary>
    private N.Vector3 Right()
    {
        var x = _view.Basis.X;
        return new N.Vector3(x.X, x.Y, x.Z);
    }

    /// <summary>Where a point is drawn this frame, or null behind the eye.</summary>
    private Vector2? Project(N.Vector3 p)
    {
        var size = GetViewportRect().Size;
        var local = _view.AffineInverse() * p.ToGodot();
        if (local.Z > -0.05f)
            return null;
        var t = Mathf.Tan(Mathf.DegToRad(_fov) / 2);
        var x = local.X / -local.Z / (t * size.X / size.Y);
        var y = local.Y / -local.Z / t;
        return new Vector2((x + 1) / 2 * size.X, (1 - y) / 2 * size.Y);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The map from above

    private void Map(CameraIntent intent)
    {
        var size = GetViewportRect().Size;
        // In the top right of the view's middle 4:3, where the battle's own screen is drawn, which a wider window's movie keeps.
        var (mapSize, margin, text) = (MapShare * size.Y, MarginShare * size.Y, Math.Max(10, (int)(TextShare * size.Y)));
        var width = MathF.Min(size.X, size.Y * 4 / 3);
        var box = new Rect2((size.X + width) / 2 - mapSize - margin, margin, mapSize, mapSize);
        DrawRect(box, new Color(0, 0, 0, 0.55f));
        DrawRect(box, Frame, false, 1);
        var middle = box.GetCenter();
        var shot = intent.Shown;
        // Turned with the view: the way it looks up the map, its right to the right.
        var (forward, right) = (new N.Vector2(-MathF.Sin(shot.Yaw), -MathF.Cos(shot.Yaw)), new N.Vector2(MathF.Cos(shot.Yaw), -MathF.Sin(shot.Yaw)));
        var centre = new N.Vector2(shot.Aim.X, shot.Aim.Z);
        var reach = 20f;
        foreach (var b in _drawing!.Frame.Bodies)
        {
            if (!b.Standing.HasFlag(BodyState.Freed))
                reach = MathF.Max(reach, N.Vector2.Distance(Flat(b.Centre), centre) + 3);
        }
        reach = MathF.Max(reach, N.Vector2.Distance(Flat(shot.Eye), centre) + 3);
        reach = MathF.Min(reach, 70);
        var scale = mapSize / 2 / reach;
        Vector2 At(N.Vector2 p)
        {
            var d = p - centre;
            return middle + new Vector2(N.Vector2.Dot(d, right), -N.Vector2.Dot(d, forward)) * scale;
        }
        Vector2 At3(N.Vector3 p) => At(Flat(p));
        bool Inside(Vector2 p) => box.Grow(-2).HasPoint(p);

        if (intent.Field is var (min, max))
        {
            Vector2[] corners = [At(min), At(new(max.X, min.Y)), At(max), At(new(min.X, max.Y)), At(min)];
            for (var i = 1; i < corners.Length; i++)
                ClippedLine(box, corners[i - 1], corners[i], Frame, 1 * _px);
        }
        // Over the shoulder: the way straight over it (grey), the framing held (white) and the one it eases onto (cyan), each the side the
        // eye stands.
        if (intent.Behind is { } behind && intent.Follow is { } from)
        {
            var origin = At3(from);
            var reachOut = mapSize / 4;
            ClippedLine(box, origin, origin + Way(behind) * reachOut, new Color(1, 1, 1, 0.35f), _px);
            if (intent.Framing is { } framing)
                ClippedLine(box, origin, origin + Way(framing) * reachOut, Shown with { A = 0.8f }, 2 * _px);
            if (intent.FramingTo is { } towards)
                ClippedLine(box, origin, origin + Way(towards) * reachOut, GoalCyan, 2 * _px);
        }
        // The walk's rail, and its eye's.
        for (var i = 1; i < intent.Rail.Count; i++)
            ClippedLine(box, At3(intent.Rail[i - 1]), At3(intent.Rail[i]), RailOrange, 2 * _px);
        if (intent.EyeRail is var (start, end))
        {
            ClippedLine(box, At3(start), At3(end), GoalCyan, 1 * _px);
            if (Inside(At3(start)))
                DrawArc(At3(start), 4, 0, Mathf.Tau, 12, GoalCyan, _px);
        }
        // The bodies where they're drawn, whose turn it is outlined, the way they face.
        foreach (var b in _drawing.Frame.Bodies)
        {
            if (b.Standing.HasFlag(BodyState.Freed))
                continue;
            var p = At3(b.Centre);
            if (!Inside(p))
                continue;
            var c = b.Combatant;
            var colour = b.Standing.HasFlag(BodyState.Dead) ? Dead : c.Hostile ? Enemy : Party;
            DrawCircle(p, MathF.Max(3, c.Body.ScaledRadius * scale), colour);
            var h = b.Heading;
            DrawLine(p, p + new Vector2(N.Vector2.Dot(h, right), -N.Vector2.Dot(h, forward)) * 8 * _px, colour, _px);
            if (c == intent.Turn)
                DrawArc(p, MathF.Max(5, c.Body.ScaledRadius * scale + 2), 0, Mathf.Tau, 16, Colors.White, _px);
        }
        foreach (var (s, _, n) in intent.Beats)
        {
            var p = At3(s.At);
            if (Inside(p))
                DrawArc(p, MathF.Max(8, s.Radius * scale + 6), 0, Mathf.Tau, 16, n <= 0 ? BeatPink : ComingViolet, 2 * _px);
        }
        var target = Drawn(intent.Target);
        foreach (var s in intent.Subjects)
        {
            var p = At3(s.At);
            if (Inside(p))
            {
                var colour = target is { } t && N.Vector3.Distance(t, s.At) < 0.01f ? TargetRed : s.Weight < 1 ? Lesser : Held;
                DrawArc(p, MathF.Max(6, s.Radius * scale + 4), 0, Mathf.Tau, 16, colour, 2 * _px);
            }
        }
        if (intent.Follow is { } follow && Inside(At3(follow)))
            DrawCircle(At3(follow), 2.5f * _px, FollowGreen);
        // The goal's eye and what it would see, and the eye shown and what it sees.
        Eye(box, intent.Goal, At3, GoalCyan, scale, 1);
        Eye(box, shot, At3, Shown, scale, 2);
        if (intent.Vantage is { } v)
        {
            var p = At(v);
            if (!Inside(p))
                p = Clip(box, middle, p);
            Diamond(p, 6 * _px, VantageMagenta);
        }

        var font = ThemeDB.FallbackFont;
        var line = text * 1.3f;
        DrawString(font, new Vector2(box.Position.X, box.End.Y + line), intent.Kind, HorizontalAlignment.Left, mapSize, text, Colors.White);
        // What the director leads with, and why it last moved off the enhanced camera's own shot, for three seconds after.
        var lines = 2;
        if (intent.Director is { } director)
            DrawString(font, new Vector2(box.Position.X, box.End.Y + line * lines++), director, HorizontalAlignment.Left, mapSize, text, BeatPink);
        if (intent.Moved is { Ago: < 180 } moved)
        {
            DrawString(font, new Vector2(box.Position.X, box.End.Y + line * lines++),
                string.Create(CultureInfo.InvariantCulture, $"moved: {moved.Why}, {moved.Ago / 60f:F1} s ago"), HorizontalAlignment.Left, mapSize, text,
                BeatPink);
        }
        // Why it last swung onto a new framing, for three seconds after.
        if (intent.Swung is { Ago: < 180 } swung)
        {
            DrawString(font, new Vector2(box.Position.X, box.End.Y + line * lines++),
                string.Create(CultureInfo.InvariantCulture, $"swung: {swung.Why}, {swung.Ago / 60f:F1} s ago"), HorizontalAlignment.Left, mapSize, text,
                RailOrange);
        }
        string[] legend = intent.Director is null
            ? ["white shown, cyan its goal", "yellow held, orange if it can be, red the target", "magenta the vantage, green the follow point"]
            : ["white shown, cyan its goal", "yellow held, orange if it can be, red the target", "magenta the vantage, green the follow point",
                "pink a beat playing, violet one to come"];
        for (var i = 0; i < legend.Length; i++)
            DrawString(font, new Vector2(box.Position.X, box.End.Y + line * (i + lines)), legend[i], HorizontalAlignment.Left, mapSize, text - 2, Legend);

        // The way across the map of a yaw, the side the eye stands.
        Vector2 Way(float yaw)
        {
            var w = new N.Vector2(MathF.Sin(yaw), MathF.Cos(yaw));
            return new Vector2(N.Vector2.Dot(w, right), -N.Vector2.Dot(w, forward));
        }
    }

    /// <summary>An eye on the map: where it stands, what it looks at, and the edges of what it sees.</summary>
    private void Eye(Rect2 box, Shot shot, Func<N.Vector3, Vector2> at, Color colour, float scale, float width)
    {
        var (eye, aim) = (at(shot.Eye), at(shot.Aim));
        ClippedLine(box, eye, aim, colour with { A = 0.6f }, 1 * _px);
        var size = GetViewportRect().Size;
        var half = MathF.Atan(MathF.Tan(Mathf.DegToRad(_fov) / 2) * size.X / size.Y);
        var look = aim - eye;
        if (look.Length() > 1e-3f)
        {
            var reach = look.Normalized() * (look.Length() + 10 * scale);
            ClippedLine(box, eye, eye + reach.Rotated(half), colour, width * _px);
            ClippedLine(box, eye, eye + reach.Rotated(-half), colour, width * _px);
        }
        if (box.HasPoint(eye))
            DrawCircle(eye, 3.5f * _px, colour);
    }

    private void Cross(Vector2 at, float size, Color colour, float width)
    {
        DrawLine(at - new Vector2(size, 0), at + new Vector2(size, 0), colour, width * _px);
        DrawLine(at - new Vector2(0, size), at + new Vector2(0, size), colour, width * _px);
    }

    private void Diamond(Vector2 at, float size, Color colour) =>
        DrawColoredPolygon([at + new Vector2(0, -size), at + new Vector2(size, 0), at + new Vector2(0, size), at + new Vector2(-size, 0)], colour);

    /// <summary>A line drawn only as far as it's inside the map.</summary>
    private void ClippedLine(Rect2 box, Vector2 a, Vector2 b, Color colour, float width)
    {
        var inside = box.HasPoint(a);
        if (!inside && box.HasPoint(b))
            (a, b, inside) = (b, a, true);
        if (!inside)
            return;
        DrawLine(a, Clip(box, a, b), colour, width);
    }

    /// <summary>Where the way from a point inside the map to another leaves it, or the other where it's inside.</summary>
    private static Vector2 Clip(Rect2 box, Vector2 from, Vector2 to)
    {
        if (box.HasPoint(to))
            return to;
        var d = to - from;
        var s = 1f;
        if (d.X > 0)
            s = MathF.Min(s, (box.End.X - from.X) / d.X);
        else if (d.X < 0)
            s = MathF.Min(s, (box.Position.X - from.X) / d.X);
        if (d.Y > 0)
            s = MathF.Min(s, (box.End.Y - from.Y) / d.Y);
        else if (d.Y < 0)
            s = MathF.Min(s, (box.Position.Y - from.Y) / d.Y);
        return from + d * MathF.Max(0, s);
    }

    private static N.Vector2 Flat(N.Vector3 v) => new(v.X, v.Z);
}
