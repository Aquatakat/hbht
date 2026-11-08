using System.Numerics;
using HBHT.Core.Particles;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Core.Actors;

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
    private readonly SceneSkeleton? _skeleton;
    private readonly int _heldRoot;
    private Vector3 _lastRoot;
    private bool _made;
    private ActorParticles? _particles;

    /// <summary>The state and clock the views last posed its scene at: the last a moving animation played, as a still one leaves it be.</summary>
    private (int State, int Clock)? _posed;

    /// <param name="rolls">The stream its branches roll on (<see cref="ActorStateMachine"/>).</param>
    /// <param name="skeleton">Its scene's transforms, which its locators are posed by (<see cref="Locator"/>); none for no locators.</param>
    public ActorMotion(ActorRecord actor, ActorClips clips, IRandom rolls, SceneSkeleton? skeleton = null)
    {
        _actor = actor;
        _clips = clips;
        _skeleton = skeleton;
        _heldRoot = skeleton?.Find(SceneNames.Transform(actor.RootTransform)) ?? -1;
        Machine = new ActorStateMachine(actor, Length, rolls);
        _lastRoot = Root;
        Posed();
    }

    /// <summary>
    /// The actor's motion, from its scene's asset, or null if no plugin has the asset or the scene has no animations, as
    /// the runtime then draws it still.
    /// </summary>
    public static ActorMotion? For(PluginSet plugins, ActorRecord actor, IRandom rolls) =>
        ActorClips.For(plugins, actor.Scene, actor.RootTransform) is { Animated: true } clips ? new ActorMotion(actor, clips, rolls) : null;

    /// <summary>
    /// The actor's motion as the views draw it (<c>Models.InstantiateActor</c>), with its locators: for a scene with animations, moving
    /// or still, which the views give an animator; null for one without, or with no asset, which they draw still. The core owns it, and
    /// the views pose its scene where it stands (Start Early).
    /// </summary>
    public static ActorMotion? AsDrawn(PluginSet plugins, ActorRecord actor, IRandom rolls) =>
        ActorScenes.Of(plugins).Get(actor.Scene) is { Plays: true } scene
            ? new ActorMotion(actor, scene.Clips(actor.RootTransform), rolls, scene.Skeleton)
            : null;

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
        set => (_particles, _made) = (value, value is not null || _starts is not null);
    }

    /// <summary>
    /// Who hears the requests its emitters start for, where <see cref="Particles"/> would start them: a battle's recording, which the
    /// views start their own emitters from as they draw the tick (Start Early). Heard as it's made and on every state it enters, as
    /// <see cref="Particles"/> is.
    /// </summary>
    public Action<int>? Starts
    {
        get => _starts;
        set => (_starts, _made) = (value, value is not null || _particles is not null);
    }

    private Action<int>? _starts;

    /// <summary>Its emitters start for <paramref name="request"/>: its own, and whoever hears of it.</summary>
    private void Start(int request)
    {
        _particles?.Entered(request);
        _starts?.Invoke(request);
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
            Start(Machine.Request);
        }
        Entered = Machine.Tick();
        Posed();
        if (!Entered)
            return false;
        Start(Machine.Request);
        return true;
    }

    /// <summary>
    /// Starts the emitters it starts as it's made, for the request it has now (<c>Actor.Create</c>, <c>0x800960e0</c>), rather than on
    /// its first tick: for an actor asked for another request on that tick, as the spells' visuals are.
    /// </summary>
    public void Made()
    {
        if (!_made)
            return;
        _made = false;
        Start(Machine.Request);
    }

    /// <summary>Plays a state from its first tick, as a branch does.</summary>
    public void Enter(int state)
    {
        Machine.Enter(state);
        Posed();
    }

    /// <inheritdoc/>
    public void Restart() => Enter(_actor.InitialState);

    /// <summary>Enters the state a request leads to partway through, at <paramref name="clock"/> (<see cref="ActorStateMachine.Join"/>).</summary>
    public void Join(int request, int clock)
    {
        Machine.Join(request, clock);
        Posed();
    }

    /// <summary>
    /// Where its locator <paramref name="locator"/> is in its scene's space, as the views pose the scene where the motion stands
    /// (<c>SceneGetLocatorPos</c>, <c>0x8009ECD0</c>, before the body's place, turn and scale): a matrix whose translation is the
    /// locator's place. Null if its scene has no such locator, or no transforms were given.
    /// </summary>
    public Matrix4x4? Locator(int locator)
    {
        if (_skeleton is null)
            return null;
        if (_posed is not var (state, clock))
            return _skeleton.Locator(locator, null, 0);
        return _skeleton.Locator(locator, Borg6(state), clock - 1, _heldRoot, _clips.Pivot);
    }

    /// <summary>Whether its scene has locator <paramref name="locator"/>.</summary>
    public bool HasLocator(int locator) => _skeleton?.HasLocator(locator) == true;

    /// <summary>The views pose the scene where it stands now if its state's animation moves it; a still one leaves the last pose.</summary>
    private void Posed()
    {
        if (_skeleton is not null && _skeleton.Animates(Borg6(Machine.State)))
            _posed = (Machine.State, Machine.Clock);
    }

    private int Borg6(int state) => Convert.ToInt32(_actor.States[state].Animation.Key, 16);

    /// <summary>A state's length in ticks: its clip's, or 1 for an animation the scene doesn't have.</summary>
    private int Length(int state) => _clips.Clip(_actor.States[state].Animation)?.Length ?? 1;
}
