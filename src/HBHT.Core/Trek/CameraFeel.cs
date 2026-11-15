using System.Numerics;
using HBHT.Core.Cameras;

namespace HBHT.Core.Trek;

/// <summary>
/// What a player would feel of trek's camera over a run, fed a tick at a time (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced):
/// the enhanced camera's backstop cuts (ticks its arm was pulled in at once by more than 0.1, a wall it didn't see coming) a
/// minute, the camera's acceleration through space (its peak and 95th percentile, units a second², cuts left out), and the
/// share of the ticks he stands still that he's out of the frame or the arm is under the close floor.
/// </summary>
public sealed class CameraFeel
{
    private const float TicksPerSecond = 60;
    private int _ticks, _standing, _standingBad, _standingOut, _standingOutDrawn, _cuts;
    private int? _cutsBefore;
    private Vector3? _p1, _p2;
    private Vector3? _lastPlayer;
    private readonly List<float> _accels = [];
    private readonly List<float> _arms = [];
    private readonly Queue<float> _recent = new();
    private bool _dropped;
    private int _excursions, _urgent, _pastDrawn, _offFloor, _lensIn;

    /// <summary>Where the camera was each tick.</summary>
    public List<Vector3> Positions { get; } = [];

    public void Tick(TrekLoop trek)
    {
        _ticks++;
        var enhanced = trek.CameraStyle == CameraStyle.Enhanced ? trek.EnhancedCamera : null;
        if (enhanced is not null)
        {
            if (_cutsBefore is { } before)
                _cuts += enhanced.Cuts - before;
            _cutsBefore = enhanced.Cuts;
        }
        if (enhanced is not null && !trek.Camera.Held)
        {
            // An excursion: the arm falls by more than 35% of the longest it was in the last half second, by the backstop or
            // its eased approach alike; counted once until it's back within 10% of that.
            var arm = enhanced.Arm;
            _arms.Add(arm);
            _recent.Enqueue(arm);
            if (_recent.Count > 30)
                _recent.Dequeue();
            var most = _recent.Max();
            if (!_dropped && arm < 0.65f * most && most > 0.5f)
                (_dropped, _excursions) = (true, _excursions + 1);
            else if (_dropped && arm > 0.9f * most)
                _dropped = false;
            if (enhanced.Urgency > 0.05f)
                _urgent++;
        }
        var p = trek.Camera.Position;
        Positions.Add(p);
        if (enhanced is not null && !trek.Camera.Held && trek.Chunks.Visible is { } visible)
        {
            // Safety under either limits: past a drawn wall (the boom from the pivot crosses a solid drawn surface), not over
            // floor (nothing under the eye within 6), the lens within 0.15 of drawn geometry.
            if (visible.RaycastSolid(enhanced.Pivot, p, scrollingSolid: true) is not null)
                _pastDrawn++;
            if (!Physics.SpherePhysics.SweepNearest(trek.Chunks.World, p, p - new Vector3(0, 6, 0), 0.05f, false, 0.5f, out _, out _))
                _offFloor++;
            foreach (var way in new[] { Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ })
                if (visible.Raycast(p, p + way * 0.15f, out _, out _, out _))
                {
                    _lensIn++;
                    break;
                }
        }
        if (_p1 is { } p1 && _p2 is { } p2 && Vector3.Distance(p, p1) < 1 && Vector3.Distance(p1, p2) < 1)
            _accels.Add((p - 2 * p1 + p2).Length() * TicksPerSecond * TicksPerSecond);
        (_p2, _p1) = (_p1, p);
        var at = trek.Player.Sphere.Position;
        if (_lastPlayer is { } last && new Vector2(at.X - last.X, at.Z - last.Z).Length() < 0.002f)
        {
            _standing++;
            var seen = CameraTrace.InFrame(trek, at + new Vector3(0, 0.5f, 0));
            if (!seen || (enhanced is not null && enhanced.Arm < enhanced.Tuning.CloseFloor - 0.05f))
                _standingBad++;
            if (!seen)
                _standingOut++;
            if (!CameraTrace.InFrame(trek, at + new Vector3(0, 0.5f, 0), drawnOnly: true))
                _standingOutDrawn++;
        }
        _lastPlayer = at;
    }

    public int Ticks => _ticks;

    public int PastDrawn => _pastDrawn;

    public int OffFloor => _offFloor;

    public int LensInside => _lensIn;

    /// <summary>The arm's falls of more than 35% within half a second.</summary>
    public int Excursions => _excursions;

    public float ExcursionsPerMinute => _ticks == 0 ? 0 : _excursions * 3600f / _ticks;

    /// <summary>The ticks the enhanced camera's follow was quickened to save the boom.</summary>
    public int Urgent => _urgent;

    public float UrgentShare => _arms.Count == 0 ? 0 : (float)_urgent / _arms.Count;

    public float ArmMedian => CameraCensus.Percentile(_arms, 0.5f);

    public float Arm10 => CameraCensus.Percentile(_arms, 0.1f);

    public int Cuts => _cuts;

    public float CutsPerMinute => _ticks == 0 ? 0 : _cuts * 3600f / _ticks;

    public float AccelPeak => _accels.Count == 0 ? 0 : _accels.Max();

    public float Accel95 => CameraCensus.Percentile(_accels, 0.95f);

    public int Standing => _standing;

    public int StandingBad => _standingBad;

    public float StandingBadShare => _standing == 0 ? 0 : (float)_standingBad / _standing;

    /// <summary>Of the standing ticks, the share he's out of the frame or hidden, whatever the arm.</summary>
    public float StandingOutShare => _standing == 0 ? 0 : (float)_standingOut / _standing;

    public int StandingOut => _standingOut;

    /// <summary>Standing ticks he's out of the frame or hidden by the drawn world alone.</summary>
    public int StandingOutDrawn => _standingOutDrawn;
}
