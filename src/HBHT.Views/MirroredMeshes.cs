using HBHT.Plugins.Assets;
using Godot;

namespace HBHT.Views;

/// <summary>
/// Culls a scene's mirrored meshes as the N64 does. The RSP culls a triangle by the way it winds on screen, whatever
/// its matrix, so a model whose world matrix mirrors it shows its insides; Godot turns the culling round for a mirrored
/// instance, so it would show the outside. While a mesh's world matrix mirrors it, its one-sided surfaces are drawn
/// with copies that cull the other side (<see cref="N64Material.Mirrored"/>). Shadow Self Merge's Shadow is Alaron
/// scaled by −1 across (Borg5 0x24C9's transform 10), and seen from inside it faces the camera as the original's does.
/// Only scenes the importer marks as mirroring get one (<see cref="SceneExtras.Mirrored"/>); it's updated each time the scene
/// is posed.
/// </summary>
public sealed class MirroredMeshes
{
    private readonly List<MeshInstance3D> _meshes;
    /// <summary>The materials a mirrored mesh had, to give back.</summary>
    private readonly HashSet<MeshInstance3D> _mirrored = [];

    private MirroredMeshes(Node root) => _meshes = Nodes.FindAll<MeshInstance3D>(root);

    /// <summary>One for an instance of a scene, or null if its scene never mirrors a model.</summary>
    public static MirroredMeshes? For(Node root, SceneExtras extras) => extras.Mirrored ? new MirroredMeshes(root) : null;

    /// <summary>
    /// Gives each mesh the materials for whether its world matrix mirrors it now. A mesh not yet in the tree has no
    /// world matrix, as when an actor is first posed before its view is added, so it waits for the next pose.
    /// </summary>
    public void Update()
    {
        foreach (var mesh in _meshes)
        {
            if (!mesh.IsInsideTree())
                continue;
            var mirrored = mesh.GlobalBasis.Determinant() < 0;
            if (mirrored == _mirrored.Contains(mesh))
                continue;
            if (mirrored)
                _mirrored.Add(mesh);
            else
                _mirrored.Remove(mesh);
            // Faded or not as its model is (N64Material.SetState).
            N64Material.SetMirrored(mesh, mirrored);
        }
    }
}
