using System.Numerics;

namespace HBHT.Rom.Graphics;

/// <summary>
/// Plays a <see cref="Borg6Animation"/> the way the game does (<c>anim.cpp</c>), one 60 Hz tick at
/// a time, in single precision, so every value matches the game's bit for bit.
/// </summary>
/// <remarks>
/// <para>
/// Each track holds a value and its first three forward differences, and each tick adds each
/// difference to the one before it, so the value follows a polynomial of up to third degree. Only
/// as many are stepped as the current key's mask reaches: a key of just a value (mask 1) holds it,
/// mask 2 or 3 steps linearly, 6 or 7 quadratically, and 14 or 15 cubically. A later key usually
/// loads only the differences (mask 14), so the curve carries on from its value without a jump.
/// </para>
/// <para>
/// A track's counter starts at 1. At each tick, a key whose tick equals the counter loads (from its
/// first stored value on, the ones it leaves out become 0), then the state steps and the counter
/// goes up. When the counter reaches the length, the track instead goes back to its first key
/// without stepping. So after <c>n</c> ticks, <see cref="Time"/> is <c>1 + n</c> and each value is
/// its curve at that time, and the tracks loop every <see cref="Borg6Animation.Length"/> ticks.
/// </para>
/// <para>
/// A driven channel is set to its track's value: the game zeroes it, then adds the value times the
/// animation's weight, which is always 1. The differences are also written to fields nothing reads.
/// </para>
/// </remarks>
public sealed class Borg6Player
{
    private readonly Borg6Track[] _tracks;
    private readonly float[] _state;
    private readonly int[] _key, _counter, _firstTrack;
    private readonly int _length;

    public Borg6Player(Borg6Animation animation)
    {
        Animation = animation;
        _tracks = animation.Targets.SelectMany(t => t.Tracks).ToArray();
        _state = new float[_tracks.Length * 4];
        _key = new int[_tracks.Length];
        _counter = new int[_tracks.Length];
        _length = animation.Length;
        _firstTrack = new int[animation.Targets.Count];
        for (var t = 1; t < _firstTrack.Length; t++)
            _firstTrack[t] = _firstTrack[t - 1] + animation.Targets[t - 1].Tracks.Length;

        // Loading the Borg6 fills the whole state from the first key, zeroing what it leaves out.
        for (var i = 0; i < _tracks.Length; i++)
        {
            Load(i, 0, fill: Fill.Zero);
            _counter[i] = 1;
        }
        Time = 1;
    }

    public Borg6Animation Animation { get; }

    /// <summary>The scene's animation clock (<c>SceneData.aniTime</c>): 1 at the start, then one more per tick.</summary>
    public int Time { get; private set; }

    /// <summary>
    /// Goes back to the start, as an actor does when it takes a branch or its animation ends. Unlike
    /// loading, this keeps the state the first key leaves out, but no track steps that state before
    /// a later key overwrites it.
    /// </summary>
    public void Restart()
    {
        for (var i = 0; i < _tracks.Length; i++)
        {
            Load(i, 0, fill: Fill.Keep);
            _counter[i] = 1;
        }
        Time = 1;
    }

    /// <summary>Advances every track by one tick (<c>FUN_800a0a74</c>).</summary>
    public void Tick()
    {
        for (var i = 0; i < _tracks.Length; i++)
        {
            if (_counter[i] == _length)
            {
                Load(i, 0, fill: Fill.Keep);
                _counter[i] = 1;
                continue;
            }

            var keys = _tracks[i].Keys;
            if (_key[i] + 1 < keys.Length && keys[_key[i] + 1].Tick == _counter[i])
                Load(i, _key[i] + 1, fill: Fill.ZeroAfterFirst);

            var s = _state.AsSpan(i * 4, 4);
            var mask = keys[_key[i]].Mask;
            if ((mask & 2) != 0)
            {
                s[0] += s[1];
                if ((mask & 4) != 0)
                {
                    s[1] += s[2];
                    if ((mask & 8) != 0)
                        s[2] += s[3];
                }
            }
            _counter[i]++;
        }
        Time++;
    }

    /// <summary>The value of track <paramref name="track"/> of <see cref="Borg6Animation.Targets"/>[<paramref name="target"/>].</summary>
    public float Value(int target, int track) => _state[(_firstTrack[target] + track) * 4];

    /// <summary>A transform with the channels this animation drives set to their current values.</summary>
    public Borg5Transform Pose(int target, Borg5Transform rest)
    {
        var t = Animation.Targets[target];
        Span<float> fields = [rest.Rotation.X, rest.Rotation.Y, rest.Rotation.Z, rest.Position.X, rest.Position.Y,
            rest.Position.Z, rest.Scale.X, rest.Scale.Y, rest.Scale.Z];
        for (var j = 0; j < t.Tracks.Length; j++)
            fields[BitOperations.TrailingZeroCount((uint)t.Tracks[j].Channel)] = Value(target, j);
        return rest with
        {
            Rotation = new Vector3(fields[0], fields[1], fields[2]),
            Position = new Vector3(fields[3], fields[4], fields[5]),
            Scale = new Vector3(fields[6], fields[7], fields[8]),
        };
    }

    private enum Fill
    {
        /// <summary>Loading the Borg6 (<c>borg6_func_b</c>): what the key leaves out becomes 0.</summary>
        Zero,
        /// <summary>Restarting (<c>FUN_800a0714</c>): what the key leaves out is kept.</summary>
        Keep,
        /// <summary>
        /// Reaching a key (<c>FUN_800a0a74</c>): what the key leaves out before its first value is
        /// kept, and what it leaves out after it becomes 0.
        /// </summary>
        ZeroAfterFirst,
    }

    private void Load(int track, int key, Fill fill)
    {
        _key[track] = key;
        var k = _tracks[track].Keys[key];
        var s = _state.AsSpan(track * 4, 4);
        var v = 0;
        for (var bit = 0; bit < 4; bit++)
        {
            if ((k.Mask & (1 << bit)) != 0)
                s[bit] = k.Values[v++];
            else if (fill == Fill.Zero || (fill == Fill.ZeroAfterFirst && v > 0))
                s[bit] = 0;
        }
    }
}
