using System.Text.Json;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Plays an actor's animations as the original's state machine does (<c>Borg7_TickAnimation</c>):
/// each 60 Hz tick advances the clock, and at the end of an animation, or when the request
/// changes, the state's branches are rolled for. The root transform is drawn with its position
/// zeroed, since the original moves the actor by that motion instead.
/// </summary>
/// <remarks>
/// Requests include 0 idle, 2 walk, 3 run and 25 sneak. The rolls use their own random stream, as
/// the original's actor animations do; nothing in the rules depends on them.
/// </remarks>
public sealed class ActorAnimator
{
    private readonly ActorRecord _actor;
    private readonly AnimationPlayer _player;
    private readonly Node3D? _root;
    private readonly Vector3 _pivot;
    private readonly Random _random = new(0x3DBB6CD);
    private int _state, _clock = 1, _lastRequest;
    private double _fraction;

    private ActorAnimator(ActorRecord actor, AnimationPlayer player, Node3D? root, Vector3 pivot)
    {
        _actor = actor;
        _player = player;
        _root = root;
        _pivot = pivot;
    }

    /// <summary>The animation the actor is asked for.</summary>
    public int Request { get; set; }

    /// <summary>
    /// Adds an animator to an instance of the actor's scene. Returns null if the scene has no
    /// animations.
    /// </summary>
    public static ActorAnimator? Attach(Node3D instance, ActorRecord actor, byte[] glb)
    {
        if (Find<AnimationPlayer>(instance) is not { } player)
            return null;
        var rootName = $"transform {actor.RootTransform}";
        var root = FindNode(instance, n => n is Node3D && n.Name == rootName) as Node3D;
        var animator = new ActorAnimator(actor, player, root, Pivot(glb, actor.RootTransform));
        instance.AddChild(new FrameCallback(animator.Update) { Name = "ActorAnimator" });
        animator.Enter(actor.InitialState);
        return animator;
    }

    private void Update(double delta)
    {
        _fraction += delta * 60;
        for (; _fraction >= 1; _fraction--)
            Tick();
        Show(Math.Min(_clock - 1 + _fraction, Length(_state) - 1) / 60);
    }

    private void Tick()
    {
        var changed = Request != _lastRequest;
        _lastRequest = Request;
        if (_clock >= Length(_state))
        {
            Enter(Roll(b => b.Request == Request || b.Request == 255)?.State ?? _state);
            return;
        }
        // A new request can cut the animation short through a branch that interrupts. Either way,
        // the original doesn't advance the animation on the tick the request changes.
        if (changed)
        {
            if (Roll(b => b.Request == Request && b.Interrupts) is { } branch)
                Enter(branch.State);
            return;
        }
        _clock++;
    }

    /// <summary>Rolls 0–99 and walks the branches in order, subtracting each match's chance.</summary>
    private ActorBranch? Roll(Func<ActorBranch, bool> matches)
    {
        var roll = _random.Next(100);
        foreach (var branch in _actor.States[_state].Branches)
        {
            if (matches(branch) && (roll -= branch.Chance) < 1)
                return branch;
        }
        return null;
    }

    private void Enter(int state)
    {
        _state = state;
        _clock = 1;
        _fraction = 0;
        var name = AnimationName(state);
        if (!_player.HasAnimation(name))
        {
            GD.PushWarning($"{_actor.Id} has no animation {name}.");
            return;
        }
        _player.Play(name);
        _player.Pause();
        Show(0);
    }

    private void Show(double seconds)
    {
        if (!_player.HasAnimation(AnimationName(_state)))
            return;
        _player.Seek(seconds, update: true);
        // With its position zeroed, a pivot-rotate transform's translation is −(pivot × scale) rotated.
        if (_root is not null)
            _root.Position = -(_root.Quaternion * (_pivot * _root.Scale));
    }

    private string AnimationName(int state) => $"borg6 {Convert.ToInt32(_actor.States[state].Animation.Key, 16)}";

    /// <summary>In ticks. A clip lasts one loop of its animation.</summary>
    private int Length(int state) =>
        _player.HasAnimation(AnimationName(state)) ? (int)Math.Round(_player.GetAnimation(AnimationName(state)).Length * 60) : 1;

    /// <summary>The transform's pivot, which the exporter keeps in the node's extras.</summary>
    private static Vector3 Pivot(byte[] glb, int transform)
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

/// <summary>Calls a function every frame while it's in the tree, for classes that aren't nodes.</summary>
public partial class FrameCallback : Node
{
    private readonly Action<double>? _update;

    public FrameCallback()
    {
    }

    public FrameCallback(Action<double> update) => _update = update;

    public override void _Process(double delta) => _update?.Invoke(delta);
}
