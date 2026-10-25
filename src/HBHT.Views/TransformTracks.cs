using System.Runtime.CompilerServices;
using HBHT.Plugins.Assets;
using Godot;

namespace HBHT.Views;

/// <summary>
/// Poses the nodes Godot's player can't pose alone, by setting each one's whole transform, as the original's matrix is built
/// (scale, then rotation, then translation). Called after the player has posed the rest.
/// </summary>
/// <remarks>
/// <para>
/// Two kinds. A node the animation both turns and scales: Godot's player sets a node's rotation and scale apart, each rebuilt
/// from what the node holds, which loses one scaled unevenly or to nothing. The opening's spell (Borg6 0x2292's transform 2),
/// scaled from nothing to (5, 0.01, 5) as it spins, came out with its rotation and scale on the wrong axes, a disc seen edge-on
/// where the original shows it face-on.
/// </para>
/// <para>
/// And a node the animation holds at rest in a channel: Godot's glTF loader leaves out a track whose every key is the node's
/// rest value, so the node kept what the animation before left it, where the original sets every channel its Borg6 drives each
/// tick (docs/formats/animation.md). Phelan's lying states in the final cut-scene (<c>aidyn:actor/0x2918</c>'s 9 to 13) hold
/// his hips turned and lowered and a leg scaled to nothing at their rest values: played after his idle, which stands him on
/// both legs, he stood oddly posed at his hips' height, his death's pool of blood about his knee. Those channels are posed
/// at rest from the glTF (<see cref="DrivenNodes"/>).
/// </para>
/// </remarks>
public static class TransformTracks
{
    /// <summary>A node to pose: each channel's track, or its rest value where Godot left the track out, or neither to keep it.</summary>
    private sealed record Posed(NodePath Path, int Position, int Rotation, int Scale, Vector3? RestPosition, Quaternion? RestRotation, Vector3? RestScale);

    private static readonly ConditionalWeakTable<Animation, List<Posed>> Posing = [];

    /// <summary>Poses the player's animation's turned and scaled nodes, and those it holds at rest, where the player stands in it.</summary>
    /// <param name="driven">What the scene's glTF animations drive (<see cref="GlbScene.Driven"/>).</param>
    public static void Apply(AnimationPlayer player, DrivenNodes driven)
    {
        var name = player.AssignedAnimation;
        if (string.IsNullOrEmpty(name) || !player.HasAnimation(name) || player.GetNodeOrNull(player.RootNode) is not { } root)
            return;
        var animation = player.GetAnimation(name);
        var time = player.CurrentAnimationPosition;
        foreach (var n in Posing.GetValue(animation, a => Find(a, root, driven.Of(name))))
        {
            if (root.GetNodeOrNull<Node3D>(n.Path) is not { } node)
                continue;
            var position = n.Position >= 0 ? animation.PositionTrackInterpolate(n.Position, time) : n.RestPosition ?? node.Position;
            var rotation = n.Rotation >= 0 ? animation.RotationTrackInterpolate(n.Rotation, time) : n.RestRotation ?? node.Quaternion;
            var scale = n.Scale >= 0 ? animation.ScaleTrackInterpolate(n.Scale, time) : n.RestScale ?? node.Scale;
            node.Transform = new Transform3D(new Basis(rotation) * Basis.FromScale(scale), position);
        }
    }

    /// <summary>
    /// The nodes with both a rotation and a scale track, and those the glTF animation drives in a channel Godot has no track
    /// for, with their tracks and rest values.
    /// </summary>
    private static List<Posed> Find(Animation animation, Node root, IReadOnlyList<DrivenNode> driven)
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
        var posed = new Dictionary<string, Posed>();
        foreach (var (path, t) in tracks)
        {
            if (t.Rotation >= 0 && t.Scale >= 0)
                posed[path] = new Posed(path, t.Position, t.Rotation, t.Scale, null, null, null);
        }
        if (driven.Count == 0)
            return [.. posed.Values];

        // A track's path ends in its node's name; a node with every track left out is found by its name.
        var byName = tracks.Keys.GroupBy(p => p[(p.LastIndexOf('/') + 1)..]).ToDictionary(g => g.Key, g => g.MinBy(p => p.Length)!);
        foreach (var d in driven)
        {
            if (!byName.TryGetValue(d.Node, out var path))
            {
                if (Nodes.FindAll<Node3D>(root).Where(n => n.Name == d.Node).MinBy(n => root.GetPathTo(n).GetNameCount()) is not { } node)
                    continue;
                path = root.GetPathTo(node).ToString();
            }
            var t = tracks.TryGetValue(path, out var found) ? found : (Position: -1, Rotation: -1, Scale: -1);
            var missing = (d.Translation is not null && t.Position < 0) || (d.Rotation is not null && t.Rotation < 0)
                || (d.Scale is not null && t.Scale < 0);
            if (!missing)
                continue;
            posed[path] = new Posed(path, t.Position, t.Rotation, t.Scale,
                t.Position < 0 ? d.Translation?.ToGodot() : null,
                t.Rotation < 0 && d.Rotation is { } q ? new Quaternion(q.X, q.Y, q.Z, q.W) : null,
                t.Scale < 0 ? d.Scale?.ToGodot() : null);
        }
        return [.. posed.Values];
    }
}
