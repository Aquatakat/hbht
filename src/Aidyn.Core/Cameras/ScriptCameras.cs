using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Cameras;

/// <summary>
/// A cut-scene's cameras (<c>ScriptCamera_struct</c>): each camera op of a script starts a move that
/// holds the camera for its duration, built on a camera object's (<see cref="CameraMove"/>), aimed at
/// a point the op chose as it ran. Checked in 1.0's code (<c>AllocScriptCamera</c> <c>0x800168f8</c>,
/// <c>ProcessScriptCamera</c> <c>0x80016a50</c>, <c>freeScriptCamera</c> <c>0x800169c0</c>).
/// </summary>
/// <remarks>
/// <para>
/// Several can run at once, each writing the camera in slot order, not by age: slots are reused last
/// freed, first taken. A move that ends leaves the camera where it put it, and nothing moves it again
/// until the next: the follow camera doesn't run in a cut-scene once the last move is freed
/// (<c>PHANDLE.cameraFocus</c> −1).
/// </para>
/// <para>
/// <see cref="Busy"/> is the original's <c>counter1</c>, the tally of the step before, so a script
/// waiting for the camera, and the player's input, carry on a little after the last move ends.
/// </para>
/// </remarks>
public sealed class ScriptCameras
{
    /// <summary>The original has eight slots and crashes as the eighth is taken.</summary>
    public const int Most = 7;

    private const CameraFlags Moves = CameraFlags.Cut | CameraFlags.Pan | CameraFlags.Sine | CameraFlags.EaseIn | CameraFlags.EaseOut;

    private readonly Running?[] _slots = new Running?[Most + 1];
    /// <summary>Free slots, taken from <see cref="_count"/> up (<c>cameraIndecies</c>).</summary>
    private readonly int[] _free = [0, 1, 2, 3, 4, 5, 6, 7];
    private int _count;
    private int _counter0, _counter1;

    /// <summary>How many moves are running.</summary>
    public int Count => _count;

    /// <summary>
    /// Whether the camera was moved lately (<c>counter1</c>): a script waiting for the camera waits, and
    /// the conversation box takes no input.
    /// </summary>
    public bool Busy => _counter1 != 0;

    /// <summary>
    /// Starts a move (<c>DialougCreateScriptCamera</c>), held for <paramref name="ticks"/>. A cut lasts
    /// one step. False, and nothing starts, when all seven are running, where the original crashes.
    /// </summary>
    /// <param name="point">Where it moves the camera or its aim: a reference point, or with none, <paramref name="focus"/>.</param>
    /// <param name="focus">The point the op chose: an actor's eyes, or for the rest, where the speaker stands.</param>
    public bool Start(CameraFlags flags, Vector3? point, Vector3 focus, int ticks)
    {
        if (_count == Most)
            return false;
        var slot = _free[_count++];
        _slots[slot] = new Running(new CameraMove(flags, point, new Vector3(0, 0, ticks)), focus, ticks);
        _counter0++;
        _counter1++;
        return true;
    }

    /// <summary>
    /// One tick: each move steps in slot order, and those whose time is up are freed. A cut is done in
    /// the step after it starts.
    /// </summary>
    /// <param name="tick">The game's ticks so far, which <see cref="CameraMove"/> keeps its place by.</param>
    public void Step(FollowCamera camera, long tick)
    {
        _counter1 = _counter0;
        _counter0 = 0;
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is not { } running)
                continue;
            // ProcessScriptCamera counts each, and processVoxelCamera a mover again.
            _counter0 += (running.Move.Flags & Moves) != 0 ? 2 : 1;
            // gPlayer is null in a cut-scene, so the goal with no reference point is the focus itself:
            // processVoxelCamera takes 0.5 off for the missing player and adds 0.5 back in a dialogue.
            running.Move.Step(camera, running.Focus, 0.5f, tick, conversation: true);
            if (--running.Ticks < 1)
                Free(i);
        }
    }

    /// <summary>Frees every move, as the cut-scene ends.</summary>
    public void Clear()
    {
        for (var i = 0; i < _slots.Length; i++)
        {
            if (_slots[i] is not null)
                Free(i);
        }
        _counter0 = _counter1 = 0;
    }

    private void Free(int slot)
    {
        _slots[slot] = null;
        _free[--_count] = slot;
    }

    private sealed class Running(CameraMove move, Vector3 focus, int ticks)
    {
        public CameraMove Move { get; } = move;
        public Vector3 Focus { get; } = focus;
        public int Ticks { get; set; } = ticks;
    }
}
