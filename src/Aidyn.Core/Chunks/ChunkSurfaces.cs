using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Chunks;

/// <summary>
/// The surfaces a loaded chunk draws, which rain and snow land on (<see cref="VisibleWorld"/>), the user's choice in
/// place of the original's collision: every draw of its scene and of each scene object it places, and of its
/// see-through scene. They splash on solid draws whose texture doesn't scroll; everything else absorbs them without
/// a splash (<see cref="DrawnTriangle.Absorbs"/>): cut-outs such as the trees' canopies, translucent draws, and
/// scrolling textures such as water, lava and fire. The see-through scenes are nearly all scrolling water; B22's also
/// holds a strip of beach, which the rain splashes on. Scene objects are taken at their
/// nearest level of detail, as their runtime node places it, whatever level is drawn, and as their scene stands before
/// anything animates it. Billboards and bodies aren't in it, so rain passes through them. A scene's triangles are read once.
/// </summary>
public sealed class ChunkSurfaces(PluginSet plugins)
{
    private readonly Dictionary<RecordId, DrawnTriangle[]> _scenes = [];

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

    private DrawnTriangle[] Scene(RecordId id)
    {
        if (!_scenes.TryGetValue(id, out var triangles))
            _scenes[id] = triangles = [.. SceneMesh.For(plugins, id).Select(t => new DrawnTriangle(t.A, t.B, t.C, Absorbs(t)))];
        return triangles;
    }
}
