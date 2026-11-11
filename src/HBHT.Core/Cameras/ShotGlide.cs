using System.Numerics;

namespace HBHT.Core.Cameras;

/// <summary>
/// A goal shot glided after an ideal one that may jump or change course: each of its aim, yaw, pitch and distance (its logarithm, so a zoom
/// is even at every scale) is a critically damped spring drawn after another that's drawn after the ideal. So its place, its speed and its
/// acceleration all change continuously: a new ideal bends its course rather than kicking it, and it lands on one that stops within
/// <see cref="Time"/>, never past it. The first spring follows the ideal's own steady motion as well, so an ideal that moves evenly (a
/// framing drawn back as a character nears a pack) is glided after without trailing it. What a camera is carried by (a walker's stride, an
/// orbit's turn) is carried by it too (<see cref="Carry"/>, <see cref="Turn"/>), as it is.
/// </summary>
/// <remarks>
/// Each spring <c>x'' = ω²(g − x) − 2ω(x' − g')</c>, stepped exactly over a tick for a goal moving evenly, with <c>ω = 7.75 / Time</c>:
/// the pair's step response reaches 95% at <c>ωt ≈ 7.75</c> and never overshoots, and its acceleration starts from nothing.
/// </remarks>
public sealed class ShotGlide
{
    private const float Tick = 1f / 60;

    private Vector3 _aim1, _aimSpeed1, _aim2, _aimSpeed2;
    private float _yaw1, _yawSpeed1, _yaw2, _yawSpeed2;
    private float _pitch1, _pitchSpeed1, _pitch2, _pitchSpeed2;
    private float _log1, _logSpeed1, _log2, _logSpeed2;

    /// <summary>The ideal last tick, and its own steady motion, eased over a few ticks: the first spring's goal's speed.</summary>
    private Shot? _last;

    private Vector3 _idealAim;
    private float _idealYaw, _idealPitch, _idealLog;

    /// <summary>How long it takes to land on an ideal that stops, seconds: 95% of the way.</summary>
    public float Time { get; set; } = 1;

    /// <summary>
    /// How far an ideal may move in a tick and still be followed at its own speed; further, it jumped, and its own speed is taken as none,
    /// so the jump is glided over rather than followed past.
    /// </summary>
    public (float Units, float Turn, float Zoom) Jump { get; set; } = (0.3f, 0.05f, 0.05f);

    /// <summary>The goal it's at.</summary>
    public Shot Current => new(_aim2, Shot.Wrap(_yaw2), _pitch2, MathF.Exp(_log2));

    /// <summary>Its speeds: the aim's, units a second; the yaw's and pitch's, radians a second; and the distance's logarithm's, a second.</summary>
    public (Vector3 Aim, float Yaw, float Pitch, float Distance) Speeds => (_aimSpeed2, _yawSpeed2, _pitchSpeed2, _logSpeed2);

    /// <summary>
    /// Starts it at <paramref name="at"/> moving at <paramref name="speeds"/>, as a camera already moving is: the first spring stands where
    /// the second neither speeds up nor slows down, so it carries on as it was until the ideal says otherwise.
    /// </summary>
    public void Reset(Shot at, (Vector3 Aim, float Yaw, float Pitch, float Distance) speeds)
    {
        var lead = 2 / Omega;
        (_aim2, _aimSpeed2, _aim1, _aimSpeed1) = (at.Aim, speeds.Aim, at.Aim + speeds.Aim * lead, speeds.Aim);
        (_yaw2, _yawSpeed2, _yaw1, _yawSpeed1) = (at.Yaw, speeds.Yaw, at.Yaw + speeds.Yaw * lead, speeds.Yaw);
        (_pitch2, _pitchSpeed2, _pitch1, _pitchSpeed1) = (at.Pitch, speeds.Pitch, at.Pitch + speeds.Pitch * lead, speeds.Pitch);
        var log = MathF.Log(MathF.Max(at.Distance, 1e-3f));
        (_log2, _logSpeed2, _log1, _logSpeed1) = (log, speeds.Distance, log + speeds.Distance * lead, speeds.Distance);
        _last = null;
        (_idealAim, _idealYaw, _idealPitch, _idealLog) = (Vector3.Zero, 0, 0, 0);
    }

    /// <summary>
    /// Starts it for a camera at <paramref name="at"/> moving at <paramref name="speeds"/>, which would come to rest at
    /// <paramref name="rest"/> (<see cref="CappedMotion.Rest"/>), gliding after <paramref name="ideal"/>. Each of the aim, the yaw, the
    /// pitch and the distance carries on at the camera's speed only where the ideal is far enough on along its way for the springs to
    /// land on it (their own stop from a speed <c>v</c> is some <c>4v/ω</c> long, softer than the camera's); else it starts at rest where
    /// the camera stops, which the camera slows onto under its caps, and glides on from there. Started at the camera's speed whatever
    /// the ideal, a turn's shot found just ahead of a camera still moving was glided past and come back to.
    /// </summary>
    public void Reset(Shot at, (Vector3 Aim, float Yaw, float Pitch, float Distance) speeds, Shot rest, Shot ideal)
    {
        var reach = 4 / Omega;
        var aimSpeed = speeds.Aim.Length();
        var aimOn = aimSpeed > 1e-6f && Vector3.Dot(ideal.Aim - at.Aim, speeds.Aim / aimSpeed) >= aimSpeed * reach;
        var yawOn = On(Shot.Wrap(ideal.Yaw - at.Yaw), speeds.Yaw);
        var pitchOn = On(ideal.Pitch - at.Pitch, speeds.Pitch);
        var zoomOn = On(MathF.Log(MathF.Max(ideal.Distance, 1e-3f) / MathF.Max(at.Distance, 1e-3f)), speeds.Distance);
        Reset(new Shot(aimOn ? at.Aim : rest.Aim, yawOn ? at.Yaw : rest.Yaw, pitchOn ? at.Pitch : rest.Pitch, zoomOn ? at.Distance : rest.Distance),
            (aimOn ? speeds.Aim : Vector3.Zero, yawOn ? speeds.Yaw : 0, pitchOn ? speeds.Pitch : 0, zoomOn ? speeds.Distance : 0));

        bool On(float way, float speed) => speed != 0 && way * MathF.Sign(speed) >= MathF.Abs(speed) * reach;
    }

    /// <summary>Moves it by <paramref name="by"/> as it is, its speeds kept: carried along with the body the camera follows.</summary>
    public void Carry(Vector3 by)
    {
        _aim1 += by;
        _aim2 += by;
        if (_last is { } last)
            _last = last with { Aim = last.Aim + by };
    }

    /// <summary>Turns it by <paramref name="by"/> radians as it is, its speeds kept: turned along with an orbit.</summary>
    public void Turn(float by)
    {
        _yaw1 += by;
        _yaw2 += by;
        if (_last is { } last)
            _last = last with { Yaw = Shot.Wrap(last.Yaw + by) };
    }

    /// <summary>A tick gliding after <paramref name="ideal"/>.</summary>
    public void Step(Shot ideal)
    {
        var w = Omega;
        var log = MathF.Log(MathF.Max(ideal.Distance, 1e-3f));
        // The ideal's own steady motion, eased over a few ticks, unless it jumped.
        if (_last is { } last)
        {
            var (moved, turned, tilted, zoomed) = (ideal.Aim - last.Aim, Shot.Wrap(ideal.Yaw - last.Yaw), ideal.Pitch - last.Pitch,
                log - MathF.Log(MathF.Max(last.Distance, 1e-3f)));
            const float k = 0.25f;
            _idealAim = moved.Length() > Jump.Units ? Vector3.Zero : _idealAim + (moved / Tick - _idealAim) * k;
            _idealYaw = MathF.Abs(turned) > Jump.Turn ? 0 : _idealYaw + (turned / Tick - _idealYaw) * k;
            _idealPitch = MathF.Abs(tilted) > Jump.Turn ? 0 : _idealPitch + (tilted / Tick - _idealPitch) * k;
            _idealLog = MathF.Abs(zoomed) > Jump.Zoom ? 0 : _idealLog + (zoomed / Tick - _idealLog) * k;
        }
        _last = ideal;
        // The yaw unwrapped about the first spring, so it goes the short way round.
        var yaw = _yaw1 + Shot.Wrap(ideal.Yaw - _yaw1);
        Spring(ref _aim1, ref _aimSpeed1, ideal.Aim, _idealAim, w);
        Spring(ref _aim2, ref _aimSpeed2, _aim1, Vector3.Zero, w);
        Spring(ref _yaw1, ref _yawSpeed1, yaw, _idealYaw, w);
        Spring(ref _yaw2, ref _yawSpeed2, _yaw1, 0, w);
        Spring(ref _pitch1, ref _pitchSpeed1, ideal.Pitch, _idealPitch, w);
        Spring(ref _pitch2, ref _pitchSpeed2, _pitch1, 0, w);
        Spring(ref _log1, ref _logSpeed1, log, _idealLog, w);
        Spring(ref _log2, ref _logSpeed2, _log1, 0, w);
        // Kept near the circle, the two together.
        if (MathF.Abs(_yaw2) > 4 * MathF.PI)
        {
            var whole = MathF.Round(_yaw2 / MathF.Tau) * MathF.Tau;
            (_yaw1, _yaw2) = (_yaw1 - whole, _yaw2 - whole);
        }
    }

    private float Omega => 7.75f / MathF.Max(Time, 1e-3f);

    /// <summary>
    /// A tick of <c>x'' = ω²(g − x) − 2ω(x' − g')</c> for a goal at <paramref name="goal"/> moving at <paramref name="goalSpeed"/>, stepped
    /// exactly: in the goal's own frame, the critically damped spring's closed form.
    /// </summary>
    private static void Spring(ref float x, ref float v, float goal, float goalSpeed, float w)
    {
        var (e, u) = (x - goal, v - goalSpeed);
        var decay = MathF.Exp(-w * Tick);
        var temp = (u + w * e) * Tick;
        (e, u) = ((e + temp) * decay, (u - w * temp) * decay);
        // The goal moves on through the tick.
        (x, v) = (goal + goalSpeed * Tick + e, goalSpeed + u);
    }

    private static void Spring(ref Vector3 x, ref Vector3 v, Vector3 goal, Vector3 goalSpeed, float w)
    {
        var (e, u) = (x - goal, v - goalSpeed);
        var decay = MathF.Exp(-w * Tick);
        var temp = (u + w * e) * Tick;
        (e, u) = ((e + temp) * decay, (u - w * temp) * decay);
        (x, v) = (goal + goalSpeed * Tick + e, goalSpeed + u);
    }
}
