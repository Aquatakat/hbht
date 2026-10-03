using System.Text.Json;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The importer's glTF binaries (<c>.glb</c>): their JSON chunk, and loading one into a Godot node
/// tree with the N64's materials. The runtime and the editor both load scenes through here.
/// </summary>
internal static class GlbFile
{
    // A glb is a 12-byte header, then chunks of (length, type, data): the JSON chunk's length is at 12, its data at 20.
    private const int JsonLengthOffset = 12, JsonOffset = 20;

    /// <summary>The binary's JSON chunk. The caller disposes it.</summary>
    public static JsonDocument Json(byte[] glb) => JsonDocument.Parse(glb.AsMemory(JsonOffset, BitConverter.ToInt32(glb, JsonLengthOffset)));

    /// <summary>Whether the binary has no binary chunk after its JSON, which a scene with no geometry doesn't (and Godot's reader warns about).</summary>
    public static bool HasNoBinaryChunk(byte[] glb) => glb.Length == JsonOffset + BitConverter.ToInt32(glb, JsonLengthOffset);

    /// <summary>
    /// Loads the binary as Godot's glTF import does, then replaces its materials with
    /// <see cref="N64Material"/>'s. Null, with the reason in <paramref name="error"/>, if Godot can't.
    /// </summary>
    public static Node3D? Load(byte[] glb, out Error error)
    {
        var document = new GltfDocument();
        var state = new GltfState();
        error = document.AppendFromBuffer(glb, "", state);
        if (error != Error.Ok)
            return null;
        if (document.GenerateScene(state) is not Node3D scene)
        {
            error = Error.Failed;
            return null;
        }
        N64Material.Apply(glb, state, scene);
        return scene;
    }
}
