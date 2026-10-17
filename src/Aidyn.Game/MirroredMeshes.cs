using Godot;

namespace Aidyn.Game;

/// <summary>
/// Culls a scene's mirrored meshes as the N64 does. The RSP culls a triangle by the way it winds on screen, whatever
/// its matrix, so a model whose world matrix mirrors it shows its insides; Godot turns the culling round for a mirrored
/// instance, so it would show the outside. While a mesh's world matrix mirrors it, its one-sided surfaces are drawn
/// with copies that cull the other side (<see cref="N64Material.Mirrored"/>). Shadow Self Merge's Shadow is Alaron
/// scaled by −1 across (Borg5 0x24C9's transform 10), and seen from inside it faces the camera as the original's does.
/// Only scenes the importer marks as mirroring get one (their glTF's <c>mirrors</c>); it's updated each time the scene
/// is posed.
/// </summary>
public sealed class MirroredMeshes
{
    private readonly List<MeshInstance3D> _meshes;
    /// <summary>The materials a mirrored mesh had, to give back.</summary>
    private readonly Dictionary<MeshInstance3D, Material?[]> _mirrored = [];

    private MirroredMeshes(Node root) => _meshes = Nodes.FindAll<MeshInstance3D>(root);

    /// <summary>One for an instance of a scene, or null if its scene never mirrors a model.</summary>
    public static MirroredMeshes? For(Node root, byte[] glb)
    {
        using var json = GlbFile.Json(glb);
        return json.RootElement.TryGetProperty("scenes", out var scenes) && scenes[0].TryGetProperty("extras", out var extras) &&
            extras.TryGetProperty("mirrors", out var mirrors) && mirrors.GetBoolean() ? new MirroredMeshes(root) : null;
    }

    /// <summary>Gives each mesh the materials for whether its world matrix mirrors it now.</summary>
    public void Update()
    {
        foreach (var mesh in _meshes)
        {
            var mirrored = mesh.GlobalBasis.Determinant() < 0;
            if (mirrored == _mirrored.ContainsKey(mesh))
                continue;
            if (mirrored)
            {
                var own = Enumerable.Range(0, mesh.GetSurfaceOverrideMaterialCount()).Select(mesh.GetSurfaceOverrideMaterial).ToArray();
                _mirrored[mesh] = own;
                for (var surface = 0; surface < own.Length; surface++)
                {
                    if (own[surface] is { } material && N64Material.Mirrored(material) is { } copy)
                        mesh.SetSurfaceOverrideMaterial(surface, copy);
                }
            }
            else
            {
                var own = _mirrored[mesh];
                for (var surface = 0; surface < own.Length; surface++)
                    mesh.SetSurfaceOverrideMaterial(surface, own[surface]);
                _mirrored.Remove(mesh);
            }
        }
    }
}
