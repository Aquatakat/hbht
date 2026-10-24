using Aidyn.Plugins.Assets;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// Loading the importer's glTF binaries (<c>.glb</c>) into a Godot node tree with the N64's materials.
/// The runtime and the editor both load scenes through here.
/// </summary>
public static class GlbFile
{
    /// <summary>
    /// The rate Godot bakes the glTF's animations at as it loads them: the importer's, a sample a tick. Godot
    /// resamples every translation, rotation and scale track at the rate <c>GenerateScene</c> is given (which overrides
    /// the state's), 30 a second by default, and the views pose a scene every tick, so on every other tick a node stood
    /// halfway between two samples, its translation and rotation interpolated apart. A node turning fast about a pivot
    /// away from its origin then left the pivot: the purple cube on Ardra's sceptre in the ending's fourth shot (scene
    /// 0x2097's transform 145, turning 100° a tick) flew up to 1.5 units off the sceptre on the even ticks.
    /// </summary>
    public const float BakeFps = SceneNames.AnimationSamplesPerSecond;

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
        if (document.GenerateScene(state, BakeFps) is not Node3D scene)
        {
            error = Error.Failed;
            return null;
        }
        N64Material.Apply(glb.Reader.Root, state, scene);
        return scene;
    }
}
