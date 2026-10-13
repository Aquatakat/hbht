using Aidyn.Plugins.Records;

namespace Aidyn.Core.Actors;

/// <summary>
/// An actor's state machine, as the original ticks it (<c>Borg7_TickAnimation</c>): each 60 Hz
/// tick advances the clock, and at the end of the state's animation, or when the request changes,
/// the state's branches are rolled for. The runtime and the editor both step actors with this.
/// </summary>
/// <remarks>
/// Requests include 0 idle, 2 walk, 3 run and 25 sneak (<see cref="ActorBranch.Request"/> 255 is
/// any). The rolls use their own random stream, as the original's actor animations do; nothing in
/// the rules depends on them.
/// </remarks>
public sealed class ActorStateMachine
{
    /// <summary>The request a branch takes for any request.</summary>
    public const int AnyRequest = 255;

    private readonly ActorRecord _actor;
    private readonly Func<int, int> _length;
    private readonly Random _random;
    private int _lastRequest, _settle;

    /// <param name="length">A state's animation length in ticks: one loop of it.</param>
    /// <param name="seed">The random stream's seed. Each actor has its own stream.</param>
    public ActorStateMachine(ActorRecord actor, Func<int, int> length, int seed = 0x3DBB6CD)
    {
        _actor = actor;
        _length = length;
        _random = new Random(seed);
        Enter(actor.InitialState);
    }

    public ActorRecord Actor => _actor;

    /// <summary>The state playing.</summary>
    public int State { get; private set; }

    /// <summary>The state's animation time, from 1 when it's entered.</summary>
    public int Clock { get; private set; } = 1;

    /// <summary>The animation the actor is asked for.</summary>
    public int Request { get; set; }

    /// <summary>
    /// Whether the root's motion on the last tick moves the actor. The original moves nothing on a
    /// tick that enters a state, as the root jumps back to its start, or on the two ticks after.
    /// </summary>
    public bool RootMoves { get; private set; }

    /// <summary>The state's animation length in ticks.</summary>
    public int Length(int state) => Math.Max(1, _length(state));

    /// <summary>Starts a state from its first tick, as a branch does.</summary>
    public void Enter(int state)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(state);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(state, _actor.States.Count);
        State = state;
        Clock = 1;
    }

    /// <summary>
    /// Enters the state <paramref name="request"/> leads to from this one at <paramref name="clock"/>, as if it had
    /// played up to there. It's the port's, for animations made to run on from one another, such as the pause menu's
    /// scroll's; the original only enters a state at its start.
    /// </summary>
    public void Join(int request, int clock)
    {
        var branch = _actor.States[State].Branches.FirstOrDefault(b => b.Request == request)
            ?? throw new ArgumentException($"{_actor.Id}'s state {State} has no branch for request {request}.", nameof(request));
        Enter(branch.State);
        Clock = Math.Clamp(clock, 1, Length(State));
    }

    /// <summary>Advances a tick. Returns whether it entered a state, by a branch or by restarting.</summary>
    public bool Tick()
    {
        var entered = Advance();
        RootMoves = !entered && _settle == 0;
        if (entered)
            _settle = 2;
        else if (_settle > 0)
            _settle--;
        return entered;
    }

    private bool Advance()
    {
        var changed = Request != _lastRequest;
        _lastRequest = Request;
        if (Clock >= Length(State))
        {
            Enter(Roll(b => b.Request == Request || b.Request == AnyRequest)?.State ?? State);
            return true;
        }
        // A new request can cut the animation short through a branch that interrupts. Either way,
        // the original doesn't advance the animation on the tick the request changes.
        if (changed)
        {
            if (Roll(b => b.Request == Request && b.Interrupts) is not { } branch)
                return false;
            Enter(branch.State);
            return true;
        }
        Clock++;
        return false;
    }

    /// <summary>Rolls 0–99 and walks the branches in order, subtracting each match's chance.</summary>
    private ActorBranch? Roll(Func<ActorBranch, bool> matches)
    {
        var roll = _random.Next(100);
        foreach (var branch in _actor.States[State].Branches)
        {
            if (matches(branch) && (roll -= branch.Chance) < 1)
                return branch;
        }
        return null;
    }
}
