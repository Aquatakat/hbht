using System.Numerics;
using Aidyn.Core.Particles;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Actors;

/// <summary>
/// An actor playing its animations: its <see cref="ActorStateMachine"/>, timed by its clips' lengths, and the motion of
/// its root transform, which moves its body (<c>Borg7_TickAnimation</c>). The original draws the root with its position
/// zeroed (<c>Borg7_Render</c>) and moves the body by how far that position moved each tick instead. The runtime's
/// <c>ActorAnimator</c> poses the actor's scene at the state and clock this says.
/// </summary>
public sealed class ActorMotion : IActorMotion
{
    private readonly ActorRecord _actor;
    private readonly ActorClips _clips;
    private Vector3 _lastRoot;
    private bool _made;
    private ActorParticles? _particles;

    /// <param name="rolls">The stream its branches roll on (<see cref="ActorStateMachine"/>).</param>
    public ActorMotion(ActorRecord actor, ActorClips clips, IRandom rolls)
    {
        _actor = actor;
        _clips = clips;
        Machine = new ActorStateMachine(actor, Length, rolls);
        _lastRoot = Root;
    }

    /// <summary>
    /// The actor's motion, from its scene's asset, or null if no plugin has the asset or the scene has no animations, as
    /// the runtime then draws it still.
    /// </summary>
    public static ActorMotion? For(PluginSet plugins, ActorRecord actor, IRandom rolls) =>
        ActorClips.For(plugins, actor.Scene, actor.RootTransform) is { Animated: true } clips ? new ActorMotion(actor, clips, rolls) : null;

    public ActorStateMachine Machine { get; }

    public ActorClips Clips => _clips;

    /// <summary>The animation the actor is asked for.</summary>
    public int Request
    {
        get => Machine.Request;
        set => Machine.Request = value;
    }

    /// <summary>The length in ticks of the state playing, which a cut-scene's animation waits for.</summary>
    public int StateLength => Machine.Length(Machine.State);

    public int Frame => Machine.Clock;

    public bool Entered { get; private set; }

    /// <summary>
    /// Its scene's emitters, which it starts for its request on every state it enters, as <c>ActorStateMachine.Tick</c>
    /// does (<c>ActorParticles.Entered</c>), and once as it's made (<c>0x800960e0</c>), which the port does on its first
    /// tick, once it's in the world. Null when its scene has none.
    /// </summary>
    public ActorParticles? Particles
    {
        get => _particles;
        set => (_particles, _made) = (value, value is not null);
    }

    /// <summary>
    /// Where the root transform stands at the state's clock, in the scene's space. An animation that doesn't drive the
    /// root, or that the scene doesn't have, leaves it at the origin, which moves nothing.
    /// </summary>
    public Vector3 Root =>
        _clips.Clip(_actor.States[Machine.State].Animation)?.Root is { } root ? root[Math.Min(Machine.Clock - 1, root.Count - 1)] : Vector3.Zero;

    /// <summary>
    /// One tick toward <paramref name="request"/>, returning how far the root moved forward, along the model's +Z
    /// (<c>Borg7Header.unk2c</c>).
    /// </summary>
    public float Tick(int request) => TickMotion(request).Z;

    /// <summary>
    /// One tick toward <paramref name="request"/>, returning how far the root moved in the scene's space: nothing on a
    /// tick that enters a state or the two after (<see cref="ActorStateMachine.RootMoves"/>). The first tick moves it
    /// from where its initial state starts.
    /// </summary>
    public Vector3 TickMotion(int request)
    {
        Request = request;
        Step();
        var root = Root;
        var moved = Machine.RootMoves ? root - _lastRoot : Vector3.Zero;
        _lastRoot = root;
        return moved;
    }

    /// <summary>
    /// One tick toward the request, starting the emitters on making it and on each state it enters. Returns whether it
    /// entered a state, by a branch or by restarting.
    /// </summary>
    public bool Step()
    {
        if (_made)
        {
            _made = false;
            _particles?.Entered(Machine.Request);
        }
        Entered = Machine.Tick();
        if (!Entered)
            return false;
        _particles?.Entered(Machine.Request);
        return true;
    }

    /// <summary>Plays a state from its first tick, as a branch does.</summary>
    public void Enter(int state) => Machine.Enter(state);

    /// <summary>Enters the state a request leads to partway through, at <paramref name="clock"/> (<see cref="ActorStateMachine.Join"/>).</summary>
    public void Join(int request, int clock) => Machine.Join(request, clock);

    /// <summary>A state's length in ticks: its clip's, or 1 for an animation the scene doesn't have.</summary>
    private int Length(int state) => _clips.Clip(_actor.States[state].Animation)?.Length ?? 1;
}
