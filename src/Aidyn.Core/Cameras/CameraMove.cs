using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Cameras;

/// <summary>
/// A camera object's hold on the camera while the player is in its reach (<c>processVoxelCamera</c>):
/// it moves the camera, or its aim, to a point by cutting, panning or easing, and can keep it aimed at
/// the player. Cut-scenes move the camera with the same moves (<c>AllocScriptCamera</c>), aimed at an
/// actor rather than the player. Checked against the debug ROM's machine code.
/// </summary>
/// <remarks>
/// <para>
/// The first tick in reach starts the move from wherever the camera is: a cut jumps there, a pan goes
/// at a steady speed and the eases follow their curve, each over <see cref="Duration"/> ticks. Tracking
/// chases the player at 1/24 of the way a tick, but no more than 0.25. A finished move stays done while
/// he stays in reach: the original marks it to start again, but the tick it's stepped overwrites the
/// mark. Out of reach for a tick, it starts afresh next time.
/// </para>
/// <para>
/// The original also aims straight at the player as every tracking move starts. Running, the aim
/// trails him by about 1.4, so a pan or an ease taking over jolted the view; the user saw it in the
/// original too, and chose to fix it (ADR 0018). The port snaps the aim only when
/// the move cuts, where the view jumps anyway, and otherwise carries on chasing from where it was.
/// </para>
/// <para>
/// While it moves the camera or its aim, the follow camera leaves the camera be (<see cref="FollowCamera.Hold"/>).
/// Flags 0x100 and 0x200 instead set how far behind and how high the outdoor camera follows.
/// </para>
/// </remarks>
/// <param name="point">
/// Where it moves the camera or its aim to, in the world. Without one, just above the focus: its
/// bottom, plus the follow camera's height (<see cref="FollowCamera.Height"/>), or 0.5 in a conversation.
/// </param>
/// <param name="values">Z is <see cref="Duration"/>; with flag 0x100, Y is the distance, and with 0x200, X the pitch in degrees.</param>
public sealed class CameraMove(CameraFlags flags, Vector3? point, Vector3 values)
{
    private const CameraFlags Moves = CameraFlags.Cut | CameraFlags.Pan | CameraFlags.Sine | CameraFlags.EaseIn | CameraFlags.EaseOut;
    private const float TrackRate = 24, TrackMost = 0.25f;

    /// <summary>Where it started; for a pan, how far it goes a tick (<c>vec3_b</c>).</summary>
    private Vector3 _from;

    /// <summary>Where it's going (<c>vec3_C</c>).</summary>
    private Vector3 _to;

    /// <summary>A pan's ticks left, or an ease's angle so far (<c>unk0x54</c>).</summary>
    private float _progress;

    /// <summary>The tick it must be stepped by to carry on rather than start again (<c>timestamp</c>).</summary>
    private long _due = long.MinValue;

    public CameraFlags Flags => flags;
    public Vector3? Point => point;
    public Vector3 Values => values;

    /// <summary>How long a pan or an ease takes, in ticks.</summary>
    public float Duration => values.Z;

    /// <summary>Whether it moves the camera or its aim, rather than only setting how the follow camera follows.</summary>
    public bool Moving => (flags & (CameraFlags.Aim | CameraFlags.Position)) != 0;

    /// <summary>
    /// One tick in reach, with the player's sphere, or a cut-scene's actor, at <paramref name="focus"/>.
    /// <paramref name="tick"/> counts every tick, whatever the game is doing (<see cref="GameState.Ticker"/>).
    /// </summary>
    public void Step(FollowCamera camera, Vector3 focus, float focusRadius, long tick, bool conversation = false)
    {
        var above = focus + new Vector3(0, camera.Height, 0);
        if (Moving && (flags & Moves) != 0)
        {
            camera.Hold();
            if (_due < tick)
                Start(camera, focus, focusRadius, conversation, above);
            if (Has(CameraFlags.Pan) && _progress > 0)
            {
                _progress--;
                Put(camera, Taken(camera) + _from);
            }
            if (Has(CameraFlags.Sine) && _progress < MathF.PI)
            {
                _progress = MathF.Min(_progress + MathF.PI / Duration, MathF.PI);
                Put(camera, _from + (_to - _from) * ((1 - MathF.Cos(_progress)) / 2));
            }
            if (Has(CameraFlags.EaseIn) && _progress < MathF.PI / 2)
            {
                _progress = MathF.Min(_progress + MathF.PI / 2 / Duration, MathF.PI / 2);
                Put(camera, _from + (_to - _from) * (1 - MathF.Cos(_progress)));
            }
            if (Has(CameraFlags.EaseOut) && _progress < MathF.PI / 2)
            {
                _progress = MathF.Min(_progress + MathF.PI / 2 / Duration, MathF.PI / 2);
                Put(camera, _from + (_to - _from) * MathF.Sin(_progress));
            }
        }
        if (Has(CameraFlags.Track))
        {
            camera.AimGoal = above;
            FollowCamera.Approach(ref camera.Aim, above, TrackRate, TrackMost);
        }
        if (Has(CameraFlags.Distance))
            camera.HoldDistance(values.Y);
        if (Has(CameraFlags.Pitch))
        {
            camera.Pitch = values.X;
            camera.Anchor(focus);
        }
        if (Moving)
            camera.Orient();
        _due = tick + 1;
    }

    private void Start(FollowCamera camera, Vector3 focus, float focusRadius, bool conversation, Vector3 above)
    {
        _from = Taken(camera);
        _to = point ?? focus with { Y = focus.Y - focusRadius + (conversation ? 0.5f : camera.Height) };
        _progress = 0;
        if (Has(CameraFlags.Cut))
        {
            if (Has(CameraFlags.Track))
                camera.AimAt(above);
            _from = _to;
            Put(camera, _to);
            camera.Cut();
        }
        if (Has(CameraFlags.Pan))
        {
            _from = (_to - _from) / Duration;
            _progress = Duration;
        }
    }

    private bool Has(CameraFlags flag) => (flags & flag) != 0;

    /// <summary>The camera's aim, or with <see cref="CameraFlags.Position"/> its position (<c>Camera::CopyPosAim</c>).</summary>
    private Vector3 Taken(FollowCamera camera) => Has(CameraFlags.Position) ? camera.Position : camera.Aim;

    /// <summary>Moves the camera's aim, its position or both there, not their goals (<c>Camera::CopyPosAim2</c>).</summary>
    private void Put(FollowCamera camera, Vector3 to)
    {
        if (Has(CameraFlags.Aim))
            camera.Aim = to;
        if (Has(CameraFlags.Position))
            camera.Position = to;
    }
}
