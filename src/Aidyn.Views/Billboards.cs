using Aidyn.Plugins.Assets;
using Aidyn.Core.Scenes;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// Turns scenes' billboards to face the camera, as the original draws them (<see cref="Billboard"/>).
/// </summary>
/// <remarks>
/// The export gives each billboard three nodes: one at its place in the scene, which animations
/// move and scale; a facing node under it; and the card, with the meshes, which is rolled by the
/// transform's <c>rot.z</c> and animations may roll further. The facing node is top-level, and each
/// frame it's put at the billboard's world position, turned to the camera and scaled, so the card's
/// roll comes first, then the turn, then the scale along the world's axes, as in the original.
/// </remarks>
public static class Billboards
{
    /// <summary>The facing nodes under <paramref name="root"/>.</summary>
    public static List<Node3D> Facings(Node root) =>
        [.. Nodes.FindAll<Node3D>(root).Where(n => SceneNames.IsFacing(n.Name.ToString()))];

    /// <summary>Turns each billboard to face <paramref name="camera"/>.</summary>
    public static void Face(IEnumerable<Node3D> facings, Vector3 camera)
    {
        foreach (var facing in facings)
        {
            // GetParentNode3D gives null for a top-level node.
            if (facing.GetParent() is not Node3D billboard)
                continue;
            // The transform's own scale, times the scene object's (SceneData.scalar).
            var scale = billboard.Scale * (billboard.GetParentNode3D()?.GlobalBasis.Scale ?? Vector3.One);
            var position = billboard.GlobalPosition;
            var m = Billboard.World(position.ToNumerics(), scale.ToNumerics(), 0, camera.ToNumerics());
            facing.GlobalTransform = new Transform3D(
                new Basis(new Vector3(m.M11, m.M12, m.M13), new Vector3(m.M21, m.M22, m.M23), new Vector3(m.M31, m.M32, m.M33)),
                position);
        }
    }
}
