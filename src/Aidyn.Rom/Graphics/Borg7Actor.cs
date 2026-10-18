using System.Buffers.Binary;

namespace Aidyn.Rom.Graphics;

/// <summary>A way out of a <see cref="Borg7State"/>.</summary>
/// <param name="Request">
/// The animation request it answers (<c>Borg7_SetAnimation</c>), or <see cref="AnyRequest"/>. The
/// game asks for one each frame: 0 when idle, then values such as 2 to walk and 3 to run.
/// </param>
/// <param name="Chance">Out of 100. Branches that match share one roll, in order.</param>
/// <param name="Interrupts">
/// Can be taken as soon as the request changes, rather than only when the animation ends.
/// </param>
/// <param name="State">An index into <see cref="Borg7Actor.States"/>.</param>
public sealed record Borg7Branch(byte Request, byte Chance, bool Interrupts, int State)
{
    public const byte AnyRequest = 0xFF;
}

/// <param name="Animation">The Borg6's archive index.</param>
/// <param name="Initial">
/// Set on the state the actor starts in, in all but two actors. The game doesn't read it: it keeps
/// its own "animation is linked to the scene" bit (0x2) in the same field.
/// </param>
public sealed record Borg7State(int Animation, bool Initial, Borg7Branch[] Branches);

/// <summary>
/// A Borg7: an actor, which switches between Borg6 animations of one Borg5 scene by answering
/// requests, as a state machine (roadmap 1B).
/// </summary>
/// <remarks>
/// <para>
/// The file is a 0xC-byte header (the root transform, the state count and the initial state), then
/// 0xC-byte states (the Borg6, a u16 of flags, the branch count and the branches' offset), then the
/// 4-byte branches (request, chance, flags and target state). Offsets are from the start of the
/// data. Files are padded to 8 bytes, sometimes with leftover text.
/// </para>
/// <para>
/// <b>Playback</b> (<c>Borg7_TickAnimation</c>). The current state's animation plays with a clock
/// from 1. When the clock reaches the animation's length, the game rolls 0–99 and walks the state's
/// branches in order: for each whose request is the current one or <see cref="Borg7Branch.AnyRequest"/>,
/// it subtracts the chance, and it takes the first branch at which the roll drops below 1. With no
/// branch taken, the animation restarts. When the request changes during an animation, branches
/// that match it and <see cref="Borg7Branch.Interrupts"/> are rolled for the same way, and one taken
/// cuts the animation short. Each branch taken restarts the new state's animation.
/// </para>
/// <para>
/// <b>Motion.</b> The actor is drawn with the root transform's position zeroed (<c>Borg7_Render</c>).
/// Instead, each tick the game moves the actor by how far the root's position moved, except on the
/// tick a branch is taken or an animation restarts and the two ticks after it.
/// </para>
/// </remarks>
public sealed class Borg7Actor
{
    private const int HeaderSize = 0xC, StateSize = 0xC, BranchSize = 4;
    private const ushort InitialFlag = 0x1;
    private const byte InterruptFlag = 0x1;

    private Borg7Actor(int rootTransform, int initialState, Borg7State[] states)
    {
        RootTransform = rootTransform;
        InitialState = initialState;
        States = states;
    }

    /// <summary>The transform whose motion moves the actor. It's 2 in every actor.</summary>
    public int RootTransform { get; }

    /// <summary>The state the actor starts in. It's 0 in every actor.</summary>
    public int InitialState { get; }

    public IReadOnlyList<Borg7State> States { get; }

    public static Borg7Actor Parse(byte[] data)
    {
        int Word(int offset) => Be.S32(Be.Slice(data, offset, 4, "Borg7"), 0);

        int root = Word(0), count = Word(4), initial = Word(8);
        if (count < 1 || initial < 0 || initial >= count)
            throw new InvalidDataException($"The Borg7 starts in state {initial} of {count}.");

        var end = HeaderSize + count * StateSize;
        var states = new Borg7State[count];
        for (var i = 0; i < count; i++)
        {
            var s = Be.Slice(data, HeaderSize + i * StateSize, StateSize, "Borg7");
            var animation = BinaryPrimitives.ReadInt32BigEndian(s);
            var flags = BinaryPrimitives.ReadUInt16BigEndian(s[4..]);
            var branchCount = BinaryPrimitives.ReadUInt16BigEndian(s[6..]);
            var branches = BinaryPrimitives.ReadInt32BigEndian(s[8..]);
            if ((flags & ~InitialFlag) != 0)
                throw new InvalidDataException($"State {i} has unknown flags 0x{flags:X}.");

            var list = new Borg7Branch[branchCount];
            var b = Be.Slice(data, branches, branchCount * BranchSize, "Borg7");
            for (var j = 0; j < branchCount; j++)
            {
                byte request = b[j * 4], chance = b[j * 4 + 1], branchFlags = b[j * 4 + 2], target = b[j * 4 + 3];
                if ((branchFlags & ~InterruptFlag) != 0)
                    throw new InvalidDataException($"Branch {j} of state {i} has unknown flags 0x{branchFlags:X}.");
                if (target >= count)
                    throw new InvalidDataException($"Branch {j} of state {i} goes to state {target} of {count}.");
                if (chance is 0 or > 100)
                    throw new InvalidDataException($"Branch {j} of state {i} has a chance of {chance}.");
                list[j] = new Borg7Branch(request, chance, (branchFlags & InterruptFlag) != 0, target);
            }
            end = Math.Max(end, branches + branchCount * BranchSize);
            states[i] = new Borg7State(animation, (flags & InitialFlag) != 0, list);
        }

        if (data.Length - end is < 0 or >= 8)
            throw new InvalidDataException($"The actor's data ends at 0x{end:X}, but the Borg7 is 0x{data.Length:X} bytes.");
        return new Borg7Actor(root, initial, states);
    }
}
