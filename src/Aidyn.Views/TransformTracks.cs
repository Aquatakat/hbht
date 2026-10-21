using System.Runtime.CompilerServices;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// Poses the nodes an animation both turns and scales by setting each one's whole transform from its tracks, as the
/// original's matrix is built (scale, then rotation, then translation). Godot's player sets a node's rotation and
/// scale apart, each rebuilt from what the node holds, which loses one scaled unevenly or to nothing: the opening's spell (Borg6
/// 0x2292's transform 2), scaled from nothing to (5, 0.01, 5) as it spins, came out with its rotation and scale on the
/// wrong axes, a disc seen edge-on where the original shows it face-on. Called after the player has posed the rest.
/// </summary>
public static class TransformTracks
{
    private sealed record Posed(string Path, int Position, int Rotation, int Scale);

    private static readonly ConditionalWeakTable<Animation, List<Posed>> Posing = [];

    /// <summary>Poses the player's animation's turned and scaled nodes where the player stands in it.</summary>
    public static void Apply(AnimationPlayer player)
    {
        var name = player.AssignedAnimation;
        if (string.IsNullOrEmpty(name) || !player.HasAnimation(name) || player.GetNodeOrNull(player.RootNode) is not { } root)
            return;
        var animation = player.GetAnimation(name);
        var time = player.CurrentAnimationPosition;
        foreach (var n in Posing.GetValue(animation, Find))
        {
            if (root.GetNodeOrNull<Node3D>(n.Path) is not { } node)
                continue;
            var position = n.Position >= 0 ? animation.PositionTrackInterpolate(n.Position, time) : node.Position;
            var rotation = animation.RotationTrackInterpolate(n.Rotation, time);
            var scale = animation.ScaleTrackInterpolate(n.Scale, time);
            node.Transform = new Transform3D(new Basis(rotation) * Basis.FromScale(scale), position);
        }
    }

    /// <summary>The nodes with both a rotation and a scale track, and their tracks.</summary>
    private static List<Posed> Find(Animation animation)
    {
        var tracks = new Dictionary<string, (int Position, int Rotation, int Scale)>();
        for (var i = 0; i < animation.GetTrackCount(); i++)
        {
            var path = animation.TrackGetPath(i).ToString();
            (int Position, int Rotation, int Scale) t = tracks.TryGetValue(path, out var found) ? found : (-1, -1, -1);
            tracks[path] = animation.TrackGetType(i) switch
            {
                Animation.TrackType.Position3D => t with { Position = i },
                Animation.TrackType.Rotation3D => t with { Rotation = i },
                Animation.TrackType.Scale3D => t with { Scale = i },
                _ => t,
            };
        }
        return [.. tracks.Where(t => t.Value.Rotation >= 0 && t.Value.Scale >= 0)
            .Select(t => new Posed(t.Key, t.Value.Position, t.Value.Rotation, t.Value.Scale))];
    }
}
