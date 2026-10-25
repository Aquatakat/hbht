using HBHT.Core.Actors;
using HBHT.Core.Particles;
using HBHT.Core.Rng;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using Godot;

namespace HBHT.Views;

/// <summary>
/// Poses an instance of an actor's scene as the core's <see cref="ActorMotion"/> plays it: the state's animation at
/// the motion's clock. The root transform is drawn with its position zeroed, since the original moves the actor by that
/// motion instead, which the core works out from the scene's asset.
/// </summary>
/// <remarks>
/// An actor that moves by its animations, such as the player, is <see cref="Driven"/>: its owner ticks it through
/// <see cref="Tick(int)"/>, which ticks the core's motion, poses the scene at once and returns the root's motion.
/// Otherwise it ticks itself with the frame (<see cref="Update"/>), 60 times a second, and is posed between ticks. The
/// editor draws with it too (HBHT.Views), so it holds no nodes of its own. An actor whose animations all hold its scene still
/// (the importer's still animations, which glTF can't hold) has no animation player: its state machine still runs, for its
/// states' lengths and the emitters its requests start, which is all the refused and resisted casts' actors are.
/// </remarks>
public sealed class ActorAnimator : IActorMotion
{
    private readonly AnimationPlayer? _player;
    private readonly Node3D? _root;
    private readonly Vector3 _pivot;
    private readonly MirroredMeshes? _mirroring;
    private readonly HashSet<int> _still;
    private readonly DrivenNodes _driven;
    private double _fraction;
    private int _warned = -1;

    private ActorAnimator(ActorMotion motion, AnimationPlayer? player, Node3D? root, MirroredMeshes? mirroring, HashSet<int> still, DrivenNodes driven)
    {
        Motion = motion;
        _driven = driven;
        _player = player;
        _still = still;
        _root = root;
        _pivot = motion.Clips.Pivot.ToGodot();
        _mirroring = mirroring;
        Show(0);
    }

    /// <summary>The core's actor, which this draws.</summary>
    public ActorMotion Motion { get; }

    public ActorStateMachine Machine => Motion.Machine;

    /// <inheritdoc cref="ActorMotion.StateLength"/>
    public int StateLength => Motion.StateLength;

    public int Frame => Motion.Frame;

    public bool Entered => Motion.Entered;

    /// <summary>The animation the actor is asked for.</summary>
    public int Request
    {
        get => Motion.Request;
        set => Motion.Request = value;
    }

    /// <inheritdoc cref="ActorMotion.Particles"/>
    public ActorParticles? Particles
    {
        get => Motion.Particles;
        set => Motion.Particles = value;
    }

    /// <summary>Set when the owner ticks it with <see cref="Tick(int)"/> rather than it ticking with the frame.</summary>
    public bool Driven { get; set; }

    /// <summary>
    /// An animator for an instance of the actor's scene, starting in its initial state, whose branches roll on
    /// <paramref name="rolls"/>. Returns null if the scene has no animations, still or moving.
    /// </summary>
    public static ActorAnimator? Create(Node3D instance, ActorRecord actor, GlbScene glb, IRandom rolls) =>
        Find<AnimationPlayer>(instance) is null && glb.Extras.StillAnimations.Count == 0 ? null : Create(instance, MotionFor(actor, glb, rolls), glb);

    /// <summary>
    /// An animator for an instance of the actor's scene that poses it as <paramref name="motion"/> plays, a motion made
    /// before the instance was (<see cref="MotionFor"/>), as a wanderer's is while its body's scene is built off the main thread.
    /// </summary>
    public static ActorAnimator Create(Node3D instance, ActorMotion motion, GlbScene glb)
    {
        var rootName = SceneNames.Transform(motion.Machine.Actor.RootTransform);
        var root = FindNode(instance, n => n is Node3D && n.Name == rootName) as Node3D;
        return new ActorAnimator(motion, Find<AnimationPlayer>(instance), root, MirroredMeshes.For(instance, glb.Extras),
            [.. glb.Extras.StillAnimations.Select(a => a.Borg6)], glb.Driven);
    }

    /// <summary>
    /// Whether an actor's scene has an animator (<see cref="Create(Node3D, ActorRecord, GlbScene, IRandom)"/>), from its glTF
    /// alone, before Godot has read it: it has animations, which Godot gives an animation player, or still animations.
    /// </summary>
    public static bool Animated(GlbScene glb) =>
        glb.Extras.StillAnimations.Count > 0 || glb.Reader.Root.TryGetProperty("animations", out var animations) && animations.GetArrayLength() > 0;

    /// <summary>The core's motion for an actor, starting in its initial state, whose branches roll on <paramref name="rolls"/>.</summary>
    public static ActorMotion MotionFor(ActorRecord actor, GlbScene glb, IRandom rolls) => new(actor, glb.Clips(actor.RootTransform), rolls);

    /// <summary>Ticks at 60 Hz with the frame, unless <see cref="Driven"/>, and poses the scene between ticks.</summary>
    public void Update(double delta)
    {
        if (Driven)
            return;
        _fraction += delta * 60;
        for (; _fraction >= 1; _fraction--)
        {
            // Entering a state starts its time over, so the frame's time left is lost, as it always has been here.
            if (Motion.Step())
                _fraction = 0;
        }
        Show(Math.Min(Machine.Clock - 1 + _fraction, Machine.Length(Machine.State) - 1) / 60);
    }

    /// <summary>
    /// One tick toward <paramref name="request"/>, returning how far the root moved forward, along
    /// the model's +Z (<c>Borg7Header.unk2c</c>).
    /// </summary>
    public float Tick(int request) => TickMotion(request).Z;

    /// <summary>One tick toward <paramref name="request"/>, returning how far the root moved in the model's space.</summary>
    public System.Numerics.Vector3 TickMotion(int request)
    {
        var moved = Motion.TickMotion(request);
        Show((Machine.Clock - 1) / 60.0);
        return moved;
    }

    /// <summary>Plays a state from its first tick, as a branch does.</summary>
    public void Enter(int state)
    {
        Motion.Enter(state);
        _fraction = 0;
        Show(0);
    }

    /// <summary>Enters the state a request leads to partway through, at <paramref name="clock"/> (<see cref="ActorStateMachine.Join"/>).</summary>
    public void Join(int request, int clock)
    {
        Motion.Join(request, clock);
        _fraction = 0;
        Show((Machine.Clock - 1) / 60.0);
    }

    /// <summary>Poses the scene <paramref name="seconds"/> into the state's animation.</summary>
    private void Show(double seconds)
    {
        var state = Machine.Actor.States[Machine.State];
        var name = AnimationName(state);
        if (_player is null || !_player.HasAnimation(name))
        {
            // A still animation leaves the scene as it is.
            if (_warned != Machine.State && !_still.Contains(Convert.ToInt32(state.Animation.Key, 16)))
                GD.PushWarning($"{Machine.Actor.Id} has no animation {name}.");
            _warned = Machine.State;
            return;
        }
        if (_player.AssignedAnimation != name)
        {
            _player.Play(name);
            _player.Pause();
        }
        _player.Seek(seconds, update: true);
        TransformTracks.Apply(_player, _driven);
        if (_root is not null)
            HoldRoot(_root, _pivot);
        _mirroring?.Update();
    }

    /// <summary>
    /// Zeroes a root transform's position, as the original draws it, and returns the position it had.
    /// With its position zeroed, a pivot-rotate transform's translation is −(pivot × scale) rotated.
    /// </summary>
    public static Vector3 HoldRoot(Node3D root, Vector3 pivot)
    {
        var turned = root.Quaternion * (pivot * root.Scale);
        var position = root.Position + turned;
        root.Position = -turned;
        return position;
    }

    /// <summary>The name the importer gives a state's animation in the scene's glTF.</summary>
    public static string AnimationName(ActorState state) => SceneNames.Animation(Convert.ToInt32(state.Animation.Key, 16));

    private static T? Find<T>(Node root) where T : Node => FindNode(root, n => n is T) as T;

    private static Node? FindNode(Node root, Func<Node, bool> match)
    {
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (match(node))
                return node;
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        return null;
    }
}
