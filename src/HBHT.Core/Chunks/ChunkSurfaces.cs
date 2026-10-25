using System.Collections.Concurrent;
using System.Numerics;
using HBHT.Core.MapObjects;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;

namespace HBHT.Core.Chunks;

/// <summary>
/// The surfaces a loaded chunk draws, which rain and snow land on (<see cref="VisibleWorld"/>), the user's choice in
/// place of the original's collision: every draw of its scene and of each scene object it places, and of its
/// see-through scene. They splash on solid draws whose texture doesn't scroll; everything else absorbs them without
/// a splash (<see cref="DrawnTriangle.Absorbs"/>): cut-outs such as the trees' canopies, translucent draws, and
/// scrolling textures such as water, lava and fire. The see-through scenes are nearly all scrolling water; B22's also
/// holds a strip of beach, which the rain splashes on. Scene objects are taken at their
/// nearest level of detail, as their runtime node places it, whatever level is drawn, and as their scene stands before
/// anything animates it. Billboards and bodies aren't in it, so rain passes through them. A scene's triangles are read once,
/// and can be read ahead of time on any thread (<see cref="Warm"/>), which changes nothing but when.
/// </summary>
public sealed class ChunkSurfaces(PluginSet plugins)
{
    private readonly ConcurrentDictionary<RecordId, DrawnTriangle[]> _scenes = new();

    /// <summary>A chunk's surfaces, in its own coordinates, with the objects its map places now.</summary>
    public IEnumerable<DrawnTriangle> For(ChunkEntry chunk, LoadedMap map)
    {
        if (chunk.Scene is { } scene)
        {
            foreach (var t in Scene(scene))
                yield return t;
        }
        if (chunk.AltScene is { } water)
        {
            foreach (var t in Scene(water))
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
            foreach (var t in Scene(model))
                yield return t with { A = Vector3.Transform(t.A, place), B = Vector3.Transform(t.B, place), C = Vector3.Transform(t.C, place) };
        }
    }

    /// <summary>Whether a drawn triangle absorbs rain rather than splashing it: all but solid draws that don't scroll.</summary>
    public static bool Absorbs(SceneTriangle t) => t.Blend != SurfaceBlend.Opaque || t.Scrolls;

    /// <summary>
    /// Reads the triangles of every scene a chunk could draw ahead of its loading, from any thread: its own and each of its
    /// scene objects' models, actors' scenes as they are.
    /// </summary>
    public void Warm(ChunkEntry chunk)
    {
        foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
            Scene(scene);
        if (plugins.TryGet<MapRecord>(chunk.Map) is not { } map)
            return;
        foreach (var o in map.Objects.OfType<SceneObject>())
        {
            foreach (var model in o.Models.Select(m => m.Model))
            {
                if (model.Type != RecordTypes.Actor)
                    Scene(model);
                else if (plugins.TryGet<ActorRecord>(model) is { } actor)
                    Scene(actor.Scene);
            }
        }
    }

    // Whichever thread reads a scene first keeps its triangles; any other reading it at once reads the same.
    private DrawnTriangle[] Scene(RecordId id) =>
        _scenes.GetOrAdd(id, id => [.. SceneMesh.For(plugins, id).Select(t => new DrawnTriangle(t.A, t.B, t.C, Absorbs(t)))]);
}
