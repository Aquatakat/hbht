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
    private int _ticks, _standing, _standingBad, _standingOut, _cuts;
    private int? _cutsBefore;
    private Vector3? _p1, _p2;
    private Vector3? _lastPlayer;
    private readonly List<float> _accels = [];

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
        var p = trek.Camera.Position;
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
        }
        _lastPlayer = at;
    }

    public int Ticks => _ticks;

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
}
