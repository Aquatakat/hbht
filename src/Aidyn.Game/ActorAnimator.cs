using Aidyn.Core.Actors;
using Aidyn.Core.Particles;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Poses an instance of an actor's scene as the core's <see cref="ActorMotion"/> plays it: the state's animation at
/// the motion's clock. The root transform is drawn with its position zeroed, since the original moves the actor by that
/// motion instead, which the core works out from the scene's asset.
/// </summary>
/// <remarks>
/// An actor that moves by its animations, such as the player, is <see cref="Driven"/>: its owner ticks it through
/// <see cref="Tick(int)"/>, which ticks the core's motion, poses the scene at once and returns the root's motion.
/// Otherwise it ticks itself with the frame (<see cref="Update"/>), 60 times a second, and is posed between ticks. The
/// editor shares this file, so it holds no nodes of its own.
/// </remarks>
public sealed class ActorAnimator : IActorMotion
{
    private readonly AnimationPlayer _player;
    private readonly Node3D? _root;
    private readonly Vector3 _pivot;
    private readonly MirroredMeshes? _mirroring;
    private double _fraction;
    private int _warned = -1;

    private ActorAnimator(ActorMotion motion, AnimationPlayer player, Node3D? root, MirroredMeshes? mirroring)
    {
        Motion = motion;
        _player = player;
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
    /// <paramref name="rolls"/>. Returns null if the scene has no animations.
    /// </summary>
    public static ActorAnimator? Create(Node3D instance, ActorRecord actor, byte[] glb, IRandom rolls)
    {
        if (Find<AnimationPlayer>(instance) is not { } player)
            return null;
        var rootName = $"transform {actor.RootTransform}";
        var root = FindNode(instance, n => n is Node3D && n.Name == rootName) as Node3D;
        var motion = new ActorMotion(actor, ActorClips.Read(glb, actor.RootTransform), rolls);
        return new ActorAnimator(motion, player, root, MirroredMeshes.For(instance, glb));
    }

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
        var name = AnimationName(Machine.Actor.States[Machine.State]);
        if (!_player.HasAnimation(name))
        {
            if (_warned != Machine.State)
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
        TransformTracks.Apply(_player);
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
    public static string AnimationName(ActorState state) => $"borg6 {Convert.ToInt32(state.Animation.Key, 16)}";

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
