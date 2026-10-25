using Aidyn.Plugins.Assets;

namespace Aidyn.Views;

/// <summary>
/// A scene's glTF binary, parsed once for the views that read what Godot's importer doesn't keep: the scene's
/// <see cref="SceneExtras"/>, and the actor clips of a root transform. <see cref="Models"/> holds one per asset, so
/// instancing a scene a hundred times reads its JSON once. The owner disposes it.
/// </summary>
public sealed class GlbScene : IDisposable
{
    private readonly Dictionary<int, ActorClips> _clips = [];

    private GlbScene(byte[] bytes, GlbReader reader)
    {
        Bytes = bytes;
        Reader = reader;
        Extras = SceneExtras.Read(reader);
    }

    /// <summary>The binary, for Godot's own glTF import.</summary>
    public byte[] Bytes { get; }

    /// <summary>The binary's JSON and accessors.</summary>
    public GlbReader Reader { get; }

    /// <summary>What the importer keeps in the scene's extras.</summary>
    public SceneExtras Extras { get; }

    /// <summary>Whether the binary has geometry, which a scene with none doesn't (and Godot's reader warns about).</summary>
    public bool HasBinaryChunk => Reader.HasBinaryChunk;

    public static GlbScene Parse(byte[] glb) => new(glb, GlbReader.Parse(glb));

    /// <summary>The scene's animations for an actor rooted at <paramref name="rootTransform"/>, read once.</summary>
    public ActorClips Clips(int rootTransform)
    {
        // Under a lock, as scenery is built off the main thread too.
        lock (_clips)
        {
            if (!_clips.TryGetValue(rootTransform, out var clips))
                _clips[rootTransform] = clips = ActorClips.Read(Reader, Extras, rootTransform);
            return clips;
        }
    }

    /// <summary>What each of the scene's animations drives, with the nodes' rest values, read once (<see cref="TransformTracks"/>).</summary>
    public DrivenNodes Driven
    {
        get
        {
            lock (_clips)
                return _driven ??= DrivenNodes.Read(Reader);
        }
    }

    private DrivenNodes? _driven;

    public void Dispose() => Reader.Dispose();
}
