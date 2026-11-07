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
        Show(Machine.State, 0);
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

    /// <summary>
    /// Its scene's emitters: the core's motion's (<see cref="ActorMotion.Particles"/>), which start as it enters a state; or, for an animator
    /// that draws from a battle's recording (<see cref="Recorded"/>), its own, which the drawing starts as the recording says
    /// (<see cref="StartEmitters"/>), so they start as the tick is drawn rather than as the core steps it (ADR 0040).
    /// </summary>
    public ActorParticles? Particles
    {
        get => Recorded ? _own : Motion.Particles;
        set
        {
            if (Recorded)
                _own = value;
            else
                Motion.Particles = value;
        }
    }

    private ActorParticles? _own;

    /// <summary>
    /// Whether it draws from a battle's recording (ADR 0040): it's posed at a recorded state and clock (<see cref="Follow(int, int)"/>), and
    /// its emitters are its own. Set before <see cref="Particles"/>.
    /// </summary>
    public bool Recorded { get; set; }

    /// <summary>Starts its own emitters for <paramref name="request"/>, as a battle's recording says its actor did (<see cref="Recorded"/>).</summary>
    public void StartEmitters(int request) => _own?.Entered(request);

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
        Show(Machine.State, Math.Min(Machine.Clock - 1 + _fraction, Machine.Length(Machine.State) - 1) / 60);
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
        ShowSeen((Machine.Clock - 1) / 60.0);
        return moved;
    }

    /// <summary>
    /// Poses the scene where its motion stands, for a motion the core ticks itself, as a combatant's and a spell's visual's are
    /// (ADR 0040): the core steps it, and the view only draws it. Call after each tick.
    /// </summary>
    public void Follow() => ShowSeen((Machine.Clock - 1) / 60.0);

    /// <summary>
    /// Poses the scene at <paramref name="state"/> and <paramref name="clock"/>, as a battle's recording has its actor on the tick drawn
    /// (ADR 0040). Call after each tick.
    /// </summary>
    public void Follow(int state, int clock)
    {
        ShowSeen(state, (clock - 1) / 60.0);
        Blend();
    }

    /// <summary>
    /// Blends the next <paramref name="ticks"/> poses from the one it has now, as a body drawn from a battle's recording meets its next
    /// action from an idle frame other than the one before it (ADR 0040): each posed node eases from where it was to where the animation
    /// has it, so the jump doesn't show. Call before the <see cref="Follow(int, int)"/> that jumps.
    /// </summary>
    public void BlendFrom(int ticks)
    {
        if (_player?.GetNodeOrNull(_player.RootNode) is not { } root || ticks <= 0)
            return;
        var nodes = new List<(Node3D, Transform3D)>();
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is Node3D n3 && node != root)
                nodes.Add((n3, n3.Transform));
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        (_blend, _blendTicks, _blended) = (nodes, ticks, 0);
    }

    private List<(Node3D Node, Transform3D From)>? _blend;
    private int _blendTicks, _blended;

    /// <summary>Eases the posed nodes from where a blend began, a tick further each call, till it's done.</summary>
    private void Blend()
    {
        if (_blend is not { } nodes)
            return;
        _blended++;
        if (_blended >= _blendTicks || _unseen)
        {
            _blend = null;
            return;
        }
        var w = (float)_blended / _blendTicks;
        foreach (var (node, from) in nodes)
        {
            if (!GodotObject.IsInstanceValid(node))
                continue;
            var to = node.Transform;
            // A node scaled to nothing, or turned through a mirror, has no rotation to ease through: it takes its new pose at once.
            var (a, b) = (from.Basis.Determinant(), to.Basis.Determinant());
            if (a < 1e-6f || b < 1e-6f)
                continue;
            var (fromScale, toScale) = (from.Basis.Scale, to.Basis.Scale);
            var rotation = from.Basis.Orthonormalized().GetRotationQuaternion().Slerp(to.Basis.Orthonormalized().GetRotationQuaternion(), w);
            var mixed = new Transform3D(new Basis(rotation) * Basis.FromScale(fromScale.Lerp(toScale, w)), from.Origin.Lerp(to.Origin, w));
            if (mixed.Basis.Determinant() is var d && float.IsFinite(d) && Mathf.Abs(d) > 1e-6f && mixed.Origin.IsFinite())
                node.Transform = mixed;
        }
    }

    /// <summary>
    /// Whether its scene isn't seen, as a wanderer's isn't while it's faded out: the core's motion plays on, but the scene isn't
    /// posed, a call into Godot a node, till it's seen again, when it's posed at once where the motion stands.
    /// </summary>
    public bool Unseen
    {
        get => _unseen;
        set
        {
            _unseen = value;
            if (!value && _stale)
            {
                _stale = false;
                Show(_staleState, _staleSeconds);
            }
        }
    }

    private bool _unseen;

    /// <summary>Whether a pose was left out while it was <see cref="Unseen"/>.</summary>
    private bool _stale;

    /// <summary>Where it was left unposed while <see cref="Unseen"/>.</summary>
    private int _staleState;

    private double _staleSeconds;

    /// <summary>Poses the scene at its motion's state, unless it's <see cref="Unseen"/>.</summary>
    private void ShowSeen(double seconds) => ShowSeen(Machine.State, seconds);

    /// <summary>Poses the scene, unless it's <see cref="Unseen"/>.</summary>
    private void ShowSeen(int state, double seconds)
    {
        if (_unseen)
            (_stale, _staleState, _staleSeconds) = (true, state, seconds);
        else
            Show(state, seconds);
    }

    /// <summary>Plays a state from its first tick, as a branch does.</summary>
    public void Enter(int state)
    {
        Motion.Enter(state);
        _fraction = 0;
        ShowSeen(0);
    }

    /// <inheritdoc/>
    public void Restart() => Enter(Machine.Actor.InitialState);

    /// <summary>Enters the state a request leads to partway through, at <paramref name="clock"/> (<see cref="ActorStateMachine.Join"/>).</summary>
    public void Join(int request, int clock)
    {
        Motion.Join(request, clock);
        _fraction = 0;
        ShowSeen((Machine.Clock - 1) / 60.0);
    }

    /// <summary>
    /// A state's animation as the player knows it, found once: its name, whether the player has it, and what
    /// <see cref="TransformTracks"/> poses in it, found the first time it plays.
    /// </summary>
    private sealed class StateAnimation(string name, StringName playerName, bool has)
    {
        public string Name { get; } = name;
        public StringName PlayerName { get; } = playerName;
        public bool Has { get; } = has;
        public TransformTracks.Pose? Pose { get; set; }
        public bool PoseFound { get; set; }
    }

    private readonly Dictionary<int, StateAnimation> _animations = [];

    /// <summary>The animation the player was last given (<see cref="AnimationPlayer.AssignedAnimation"/>), by this.</summary>
    private StateAnimation? _assigned;

    /// <summary>Poses the scene <paramref name="seconds"/> into the animation of the state numbered <paramref name="index"/>.</summary>
    private void Show(int index, double seconds)
    {
        var state = Machine.Actor.States[index];
        if (!_animations.TryGetValue(index, out var animation))
        {
            var name = AnimationName(state);
            var playerName = new StringName(name);
            _animations[index] = animation = new StateAnimation(name, playerName, _player?.HasAnimation(playerName) == true);
        }
        if (_player is null || !animation.Has)
        {
            // A still animation leaves the scene as it is.
            if (_warned != index && !_still.Contains(Convert.ToInt32(state.Animation.Key, 16)))
                GD.PushWarning($"{Machine.Actor.Id} has no animation {animation.Name}.");
            _warned = index;
            return;
        }
        if (_assigned?.Name != animation.Name)
        {
            _player.Play(animation.PlayerName);
            _player.Pause();
            _assigned = animation;
        }
        _player.Seek(seconds, update: true);
        if (!animation.PoseFound)
        {
            animation.Pose = TransformTracks.Pose.For(_player, animation.PlayerName, _driven);
            animation.PoseFound = true;
        }
        animation.Pose?.Apply(_player.CurrentAnimationPosition);
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
