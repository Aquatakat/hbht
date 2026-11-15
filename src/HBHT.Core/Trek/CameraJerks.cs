using System.Globalization;
using System.Numerics;
using System.Text;
using HBHT.Core.Cameras;

namespace HBHT.Core.Trek;

/// <summary>
/// The enhanced trek camera's jerks over a run, each put down to what caused it (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced).
/// Fed a tick at a time: the camera's acceleration through space and its view's angular acceleration; a run of ticks over
/// <see cref="PlaceThreshold"/> or <see cref="ViewThreshold"/> is one event, sized by its peak, and put down to whichever of the
/// boom's parts (the pivot, the arm, the yaw, the pitch) moved the camera most at the peak, then within that by what drove it.
/// Cuts (a teleport, a camera object's) are left out.
/// </summary>
public sealed class CameraJerks
{
    /// <summary>The camera's acceleration through space that counts as a jerk, units a second².</summary>
    public const float PlaceThreshold = 150;

    /// <summary>The view's angular acceleration that counts as a jerk, degrees a second².</summary>
    public const float ViewThreshold = 400;

    private const float Rate = 60;
    private readonly List<(TrekCameraTick Tick, Vector3 Position, Vector3 Look)> _ticks = [];
    private readonly List<(string Kind, string Cause, float Size)> _events = [];
    private (string Kind, string Cause, float Size)? _place, _view;

    public int Ticks => _ticks.Count;

    public IReadOnlyList<(string Kind, string Cause, float Size)> Events => _events;

    public void Tick(TrekLoop trek)
    {
        if (trek.CameraStyle != CameraStyle.Enhanced || trek.Camera.Held)
        {
            _ticks.Clear();
            Close(ref _place);
            Close(ref _view);
            return;
        }
        var camera = trek.Camera;
        _ticks.Add((trek.EnhancedCamera.Last, camera.Position, Vector3.Normalize(camera.Aim - camera.Position)));
        if (_ticks.Count < 4)
            return;
        var (c, b, a, z) = (_ticks[^1], _ticks[^2], _ticks[^3], _ticks[^4]);
        if (Vector3.Distance(c.Position, b.Position) > 1 || Vector3.Distance(b.Position, a.Position) > 1)
            return;
        var place = (c.Position - 2 * b.Position + a.Position).Length() * Rate * Rate;
        var view = (c.Look - 2 * b.Look + a.Look).Length() * Rate * Rate * 180 / MathF.PI;
        Step(ref _place, place, PlaceThreshold, "place", () => Cause(c, b, a, z, place: true));
        Step(ref _view, view, ViewThreshold, "view", () => Cause(c, b, a, z, place: false));
    }

    /// <summary>The events found, closed at the end of the run.</summary>
    public void Finish()
    {
        Close(ref _place);
        Close(ref _view);
    }

    private void Step(ref (string Kind, string Cause, float Size)? open, float size, float threshold, string kind, Func<string> cause)
    {
        if (size < threshold)
        {
            Close(ref open);
            return;
        }
        if (open is not { } o || size > o.Size)
            open = (kind, cause(), size);
    }

    private void Close(ref (string Kind, string Cause, float Size)? open)
    {
        if (open is { } o)
            _events.Add(o);
        open = null;
    }

    private static string Cause((TrekCameraTick T, Vector3 P, Vector3 L) c, (TrekCameraTick T, Vector3 P, Vector3 L) b,
        (TrekCameraTick T, Vector3 P, Vector3 L) a, (TrekCameraTick T, Vector3 P, Vector3 L) z, bool place)
    {
        float Second(Func<TrekCameraTick, float> f) => MathF.Abs(f(c.T) - 2 * f(b.T) + f(a.T)) * Rate * Rate;
        float Change(Func<TrekCameraTick, float> f) => MathF.Abs(f(c.T) - f(a.T));
        var arm = c.T.Arm;
        var cos = MathF.Cos(c.T.Pitch * MathF.PI / 180);
        var pivot = (c.T.Pivot - 2 * b.T.Pivot + a.T.Pivot).Length() * Rate * Rate;
        var armPart = Second(t => t.Arm);
        var yawPart = Second(t => t.Yaw) * MathF.PI / 180 * (place ? arm * cos : 1);
        var pitchPart = Second(t => t.Pitch) * MathF.PI / 180 * (place ? arm : 1);
        if (!place)
        {
            // The view: the yaw's and pitch's turn directly, the pivot and arm only through where the look is aimed.
            pivot /= MathF.Max(arm, 1);
            armPart = arm < 1 ? armPart : 0;
        }
        var lift = Change(t => t.Shortened) > 0.01f;
        var most = new[] { pivot, armPart, yawPart, pitchPart }.Max();
        if (most == armPart)
            return c.T.Cut || b.T.Cut || a.T.Cut ? "backstop cut" : "arm's approach";
        if (most == pivot)
            return lift ? "lift (pivot)" : "pivot following";
        if (most == pitchPart)
            return c.T.SightCapped != a.T.SightCapped ? "sight cap on/off" : c.T.SightCapped ? "sight cap" : lift ? "lift (pitch)" : "pitch";
        var whisker = Change(t => t.Whisker);
        var lean = Change(t => t.Lean);
        if (whisker > 0.05f && whisker >= lean)
            return "whisker push";
        if (lean > 0.05f)
            return "lean";
        return c.T.Swinging ? "swing" : "yaw's approach";
    }

    /// <summary>The table: for each kind and cause, the events a minute and their median and largest size.</summary>
    public static string Table(IEnumerable<CameraJerks> runs)
    {
        var list = runs.ToList();
        var minutes = list.Sum(r => r.Ticks) / 3600f;
        var text = new StringBuilder();
        foreach (var g in list.SelectMany(r => r.Events).GroupBy(e => (e.Kind, e.Cause)).OrderBy(g => g.Key.Kind).ThenByDescending(g => g.Count()))
        {
            var sizes = g.Select(e => e.Size).Order().ToList();
            text.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"  {g.Key.Kind,-5} {g.Key.Cause,-18} {g.Count() / MathF.Max(minutes, 1e-3f),6:F1}/min  median {sizes[sizes.Count / 2],6:F0}  largest {sizes[^1],6:F0}"));
        }
        return text.ToString();
    }
}
