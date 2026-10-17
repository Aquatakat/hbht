using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Chunks;

/// <summary>
/// The surfaces a loaded chunk draws that rain and snow land on (<see cref="Physics.VisibleWorld"/>), the user's
/// choice in place of the original's collision: the solid draws of its scene and of each scene object it places,
/// and the whole of its see-through scene, its water. Cut-outs, such as the trees' canopies, and translucent
/// draws elsewhere let them through. Scene objects are taken at their nearest level of detail, as their runtime
/// node places it, whatever level is drawn, and as their scene stands before anything animates it; bodies aren't
/// in it. A scene's triangles are read once.
/// </summary>
public sealed class ChunkSurfaces(PluginSet plugins)
{
    private readonly Dictionary<(RecordId, bool), (Vector3 A, Vector3 B, Vector3 C)[]> _scenes = [];

    /// <summary>A chunk's surfaces, in its own coordinates, with the objects its map places now.</summary>
    public IEnumerable<(Vector3 A, Vector3 B, Vector3 C)> For(ChunkEntry chunk, LoadedMap map)
    {
        if (chunk.Scene is { } scene)
        {
            foreach (var t in Scene(scene, all: false))
                yield return t;
        }
        if (chunk.AltScene is { } water)
        {
            foreach (var t in Scene(water, all: true))
                yield return t;
        }
        foreach (var o in map.Record.Objects.OfType<SceneObject>())
        {
            if (!map.IsActive(o.Index) || Math.Min(o.Lods, o.Models.Count) == 0)
                continue;
            // The nearest level shows a container's opened or destroyed model in place of its own (ChunkScenery.Place).
            var look = (int)map.Look(o.Index);
            var model = (look < o.Models.Count ? o.Models[look] : o.Models[0]).Model;
            if (model.Type == RecordTypes.Actor)
            {
                if (plugins.TryGet<ActorRecord>(model) is not { } actor)
                    continue;
                model = actor.Scene;
            }
            var place = Matrix4x4.CreateScale(o.Scale) * Matrix4x4.CreateFromQuaternion(o.Orientation()) * Matrix4x4.CreateTranslation(o.Position);
            foreach (var (a, b, c) in Scene(model, all: false))
                yield return (Vector3.Transform(a, place), Vector3.Transform(b, place), Vector3.Transform(c, place));
        }
    }

    private (Vector3 A, Vector3 B, Vector3 C)[] Scene(RecordId id, bool all)
    {
        if (!_scenes.TryGetValue((id, all), out var triangles))
        {
            _scenes[(id, all)] = triangles = [.. SceneMesh.For(plugins, id)
                .Where(t => all || t.Blend == SurfaceBlend.Opaque)
                .Select(t => (t.A, t.B, t.C))];
        }
        return triangles;
    }
}
