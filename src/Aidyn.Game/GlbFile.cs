using Godot;

namespace Aidyn.Game;

/// <summary>
/// Loading the importer's glTF binaries (<c>.glb</c>) into a Godot node tree with the N64's materials.
/// The runtime and the editor both load scenes through here.
/// </summary>
internal static class GlbFile
{
    /// <summary>
    /// Loads the binary as Godot's glTF import does, then replaces its materials with
    /// <see cref="N64Material"/>'s. Null, with the reason in <paramref name="error"/>, if Godot can't.
    /// </summary>
    public static Node3D? Load(GlbScene glb, out Error error)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        error = document.AppendFromBuffer(glb.Bytes, "", state);
        if (error != Error.Ok)
            return null;
        if (document.GenerateScene(state) is not Node3D scene)
        {
            error = Error.Failed;
            return null;
        }
        N64Material.Apply(glb.Reader.Root, state, scene);
        return scene;
    }
}
