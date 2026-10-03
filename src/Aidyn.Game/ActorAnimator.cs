using System.Text.Json;
using Aidyn.Core.Actors;
using Aidyn.Core.Movement;
using Aidyn.Core.Scripts;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Plays an actor's animations on an instance of its scene, stepped by the core's
/// <see cref="ActorStateMachine"/>. The root transform is drawn with its position zeroed, since the
/// original moves the actor by that motion instead.
/// </summary>
/// <remarks>
/// An actor that moves by its animations, such as the player, is <see cref="Driven"/>: its owner
/// ticks it and gets the root's motion back. Otherwise something calls <see cref="Update"/> each
/// frame. The editor shares this file, so it holds no nodes of its own.
/// </remarks>
public sealed class ActorAnimator : IActorMotion
{
    private readonly AnimationPlayer _player;
    private readonly Node3D? _root;
    private readonly Vector3 _pivot;
    private double _fraction;
    private Vector3 _rootPosition, _lastRoot;

    private ActorAnimator(ActorRecord actor, AnimationPlayer player, Node3D? root, Vector3 pivot)
    {
        _player = player;
        _root = root;
        _pivot = pivot;
        Machine = new ActorStateMachine(actor, Length);
        Enter(actor.InitialState);
    }

    public ActorStateMachine Machine { get; }

    /// <summary>The length in ticks of the state playing, which a cut-scene's animation waits for.</summary>
    public int StateLength => Machine.Length(Machine.State);

    /// <summary>The animation the actor is asked for.</summary>
    public int Request
    {
        get => Machine.Request;
        set => Machine.Request = value;
    }

    /// <summary>Set when the owner ticks it with <see cref="Tick(int)"/> rather than it ticking with the frame.</summary>
    public bool Driven { get; set; }

    /// <summary>
    /// An animator for an instance of the actor's scene, starting in its initial state. Returns null
    /// if the scene has no animations.
    /// </summary>
    public static ActorAnimator? Create(Node3D instance, ActorRecord actor, byte[] glb)
    {
        if (Find<AnimationPlayer>(instance) is not { } player)
            return null;
        var rootName = $"transform {actor.RootTransform}";
        var root = FindNode(instance, n => n is Node3D && n.Name == rootName) as Node3D;
        return new ActorAnimator(actor, player, root, Pivot(glb, actor.RootTransform));
    }

    /// <summary>Ticks at 60 Hz with the frame, unless <see cref="Driven"/>.</summary>
    public void Update(double delta)
    {
        if (Driven)
            return;
        _fraction += delta * 60;
        for (; _fraction >= 1; _fraction--)
            Step();
        Show(Math.Min(Machine.Clock - 1 + _fraction, Machine.Length(Machine.State) - 1) / 60);
    }

    /// <summary>
    /// One tick toward <paramref name="request"/>, returning how far the root moved forward, along
    /// the model's +Z (<c>Borg7Header.unk2c</c>).
    /// </summary>
    public float Tick(int request) => TickMotion(request).Z;

    /// <summary>One tick toward <paramref name="request"/>, returning how far the root moved in the model's space.</summary>
    public Vector3 TickMotion(int request)
    {
        Request = request;
        Step();
        Show((Machine.Clock - 1) / 60.0);
        var moved = Machine.RootMoves ? _rootPosition - _lastRoot : Vector3.Zero;
        _lastRoot = _rootPosition;
        return moved;
    }

    /// <summary>Plays a state from its first tick, as a branch does.</summary>
    public void Enter(int state)
    {
        Machine.Enter(state);
        Play();
    }

    private void Step()
    {
        if (Machine.Tick())
            Play();
    }

    private void Play()
    {
        _fraction = 0;
        var name = AnimationName(Machine.State);
        if (!_player.HasAnimation(name))
        {
            GD.PushWarning($"{Machine.Actor.Id} has no animation {name}.");
            return;
        }
        _player.Play(name);
        _player.Pause();
        Show(0);
    }

    private void Show(double seconds)
    {
        if (!_player.HasAnimation(AnimationName(Machine.State)))
            return;
        _player.Seek(seconds, update: true);
        if (_root is not null)
            _rootPosition = HoldRoot(_root, _pivot);
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

    private string AnimationName(int state) => AnimationName(Machine.Actor.States[state]);

    /// <summary>In ticks. A clip lasts one loop of its animation.</summary>
    private int Length(int state) =>
        _player.HasAnimation(AnimationName(state)) ? (int)Math.Round(_player.GetAnimation(AnimationName(state)).Length * 60) : 1;

    /// <summary>The transform's pivot, which the exporter keeps in the node's extras.</summary>
    public static Vector3 Pivot(byte[] glb, int transform)
    {
        var length = BitConverter.ToInt32(glb, 12);
        using var json = JsonDocument.Parse(glb.AsMemory(20, length));
        foreach (var node in json.RootElement.GetProperty("nodes").EnumerateArray())
        {
            if (node.TryGetProperty("extras", out var extras) && extras.TryGetProperty("transform", out var t) &&
                t.GetInt32() == transform && extras.TryGetProperty("pivot", out var p) &&
                !(extras.TryGetProperty("billboard", out var b) && b.GetBoolean()))
            {
                return new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle());
            }
        }
        return Vector3.Zero;
    }

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
