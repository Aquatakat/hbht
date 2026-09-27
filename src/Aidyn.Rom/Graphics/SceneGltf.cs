using System.Numerics;
using System.Text.Json.Nodes;

namespace Aidyn.Rom.Graphics;

/// <summary>
/// Converts a Borg5 scene, with its models, textures, camera and lights, to a glTF file in scene
/// units (roadmap 1A). See docs/reference-rig.md for how each part maps.
/// </summary>
public static class SceneGltf
{
    private const int Nearest = 9728, Linear = 9729;
    private const int Repeat = 10497, ClampToEdge = 33071, MirroredRepeat = 33648;

    /// <param name="read">Reads a Borg by archive index.</param>
    /// <param name="index">The scene's archive index, recorded in the file.</param>
    /// <param name="animations">Borg6s of this scene, by archive index, to include as glTF animations.</param>
    public static byte[] Export(Borg5Scene scene, Func<int, byte[]> read, int index,
        IReadOnlyList<(int Index, Borg6Animation Animation)>? animations = null)
    {
        var gltf = new GltfBuilder();
        var models = scene.Models.Select(m => new Lazy<Borg2Geometry>(() => Borg2Geometry.Parse(read(m)))).ToArray();
        var textures = scene.Textures.Select(t => new Lazy<Borg1Texture>(() => Borg1Texture.Parse(read(t)))).ToArray();
        var images = new Dictionary<int, int>();
        var samplers = new Dictionary<(int, int, int, int), int>();
        var gltfTextures = new Dictionary<(int, int), int>();
        var materials = new Dictionary<(int?, Borg2MaterialFlags, Vector4, float), int>();

        // Each transform's draws become one mesh with a primitive per draw.
        var primitives = new Dictionary<int, JsonArray>();
        foreach (var draw in scene.Draws)
        {
            var model = models[draw.Model].Value;
            if (!IsDrawn(model, draw.DisplayList))
                continue;
            var mesh = model.Meshes[draw.DisplayList];
            var texture = draw.Texture is { } t && !mesh.Flags.HasFlag(Borg2MaterialFlags.EnvironmentMap) ? t : (int?)null;
            var primitive = Primitive(gltf, model, mesh, texture is { } ti ? textures[ti].Value : null);
            primitive["material"] = Material(texture, mesh, model.Alpha);
            primitive["extras"] = new JsonObject
            {
                ["model"] = scene.Models[draw.Model],
                ["displayList"] = draw.DisplayList,
                ["texture"] = draw.Texture is { } dt ? scene.Textures[dt] : null,
            };
            if (!primitives.TryGetValue(draw.Transform, out var list))
                primitives[draw.Transform] = list = [];
            list.Add(primitive);
        }

        // Model nodes come first, in transform order, so a transform's node is at t - FirstNode. A
        // billboard is drawn with only its scale and world position, turned to face the camera, so
        // its meshes go on a separate root node that faces the way it was modelled.
        var first = scene.FirstNode;
        var roots = new List<int>();
        var billboards = new List<int>();
        var nodes = new Dictionary<int, int>();
        var billboardNodes = new Dictionary<int, int>();
        for (var t = first; t < scene.Transforms.Count; t++)
        {
            var transform = scene.Transforms[t];
            var node = new JsonObject { ["name"] = $"transform {t}" };
            if (transform.Trs() is { } trs)
                SetTrs(node, trs.Scale, trs.Rotation, trs.Translation);
            if (transform.Children.Length > 0)
                node["children"] = new JsonArray(transform.Children.Select(c => (JsonNode)(c - first)).ToArray());
            var billboard = transform.Flags.HasFlag(Borg5TransformFlags.Billboard);
            // The game flattens a model drawn with any other op onto a line, so it isn't drawn here.
            if (primitives.TryGetValue(t, out var list) && transform.Op == Borg5TransformOp.PivotRotate)
            {
                if (billboard)
                    billboards.Add(t);
                else
                    node["mesh"] = gltf.AddMesh(new JsonObject { ["primitives"] = list });
            }
            node["extras"] = new JsonObject { ["transform"] = t, ["op"] = transform.Op.ToString(), ["billboard"] = billboard };
            if (transform.Pivot != Vector3.Zero)
                node["extras"]!["pivot"] = new JsonArray(transform.Pivot.X, transform.Pivot.Y, transform.Pivot.Z);
            var n = nodes[t] = gltf.AddNode(node);
            if (scene.Parent(t) is null)
                roots.Add(n);
        }
        foreach (var t in billboards)
        {
            var node = new JsonObject { ["name"] = $"transform {t} billboard" };
            SetTrs(node, scene.Transforms[t].Scale, Quaternion.Identity, scene.WorldMatrix(t).Translation);
            node["mesh"] = gltf.AddMesh(new JsonObject { ["primitives"] = primitives[t] });
            node["extras"] = new JsonObject { ["transform"] = t, ["billboard"] = true };
            roots.Add(billboardNodes[t] = gltf.AddNode(node));
        }

        var camera = Borg3Camera.Parse(read(scene.Camera));
        roots.Add(gltf.AddNode(new JsonObject
        {
            ["name"] = "camera",
            ["camera"] = gltf.AddCamera(new JsonObject
            {
                ["type"] = "perspective",
                ["perspective"] = new JsonObject
                {
                    ["yfov"] = camera.FieldOfViewY * MathF.PI / 180,
                    ["aspectRatio"] = camera.Aspect,
                    ["znear"] = camera.Near,
                    ["zfar"] = camera.Far,
                },
            }),
        }));
        var view = scene.Transforms[0].LocalMatrix();
        SetTrs(gltf, roots[^1], Vector3.One, Quaternion.CreateFromRotationMatrix(view), view.Translation);
        nodes[0] = roots[^1];

        var extras = new JsonObject { ["borg5"] = index, ["borg3"] = scene.Camera };
        for (var i = 0; i < scene.Lights.Count; i++)
        {
            var light = Borg4Light.Parse(read(scene.Lights[i]));
            var color = new JsonArray(light.Red, light.Green, light.Blue);
            if (i == scene.Lights.Count - 1)
            {
                extras["ambientLight"] = color;
                extras["ambientBorg4"] = scene.Lights[i];
                break;
            }
            roots.Add(nodes[1 + i] = gltf.AddNode(new JsonObject
            {
                ["name"] = $"light {i}",
                ["rotation"] = QuaternionJson(Facing(scene.Transforms[1 + i].Direction)),
                ["extensions"] = new JsonObject
                {
                    ["KHR_lights_punctual"] = new JsonObject
                    {
                        ["light"] = gltf.AddLight(new JsonObject { ["type"] = "directional", ["color"] = color }),
                    },
                },
                ["extras"] = new JsonObject { ["borg4"] = scene.Lights[i] },
            }));
        }

        // An animation with no targets only holds the scene still for its length. glTF can't have
        // an empty animation, so those are listed in the extras.
        var still = new JsonArray();
        foreach (var (borg6, animation) in animations ?? [])
        {
            if (animation.Scene != index)
                throw new ArgumentException($"Borg6 {borg6} animates scene {animation.Scene}, not {index}.", nameof(animations));
            if (animation.Targets.Count == 0)
                still.Add(new JsonObject { ["borg6"] = borg6, ["length"] = animation.Length });
            else
                gltf.AddAnimation(Animation(gltf, scene, borg6, animation, nodes, billboardNodes));
        }
        if (still.Count > 0)
            extras["stillAnimations"] = still;

        return gltf.ToGlb(roots, extras);

        int Material(int? texture, Borg2Mesh mesh, float modelAlpha)
        {
            var key = (texture, mesh.Flags, mesh.Tint, modelAlpha);
            if (materials.TryGetValue(key, out var existing))
                return existing;

            var alpha = (1 - mesh.Tint.W) * (1 - modelAlpha);
            var pbr = new JsonObject { ["metallicFactor"] = 0f, ["roughnessFactor"] = 1f };
            var alphaMode = alpha < 1 ? "BLEND" : "OPAQUE";
            if (texture is { } t)
            {
                // The combiner multiplies the texture by the shade (vertex colour or lighting); the
                // tint's colour is unused.
                pbr["baseColorFactor"] = new JsonArray(1f, 1f, 1f, alpha);
                pbr["baseColorTexture"] = new JsonObject { ["index"] = GltfTexture(t, mesh.Flags) };
                var borg1 = textures[t].Value;
                if (alpha >= 1 && borg1.Flags.HasFlag(Borg1Flags.HasTransparency))
                    alphaMode = borg1.Format is Borg1Format.Ia16 or Borg1Format.Ia8 or Borg1Format.Rgba32 ? "BLEND" : "MASK";
            }
            else
            {
                pbr["baseColorFactor"] = new JsonArray(mesh.Tint.X, mesh.Tint.Y, mesh.Tint.Z, alpha);
            }

            var material = new JsonObject
            {
                ["pbrMetallicRoughness"] = pbr,
                ["alphaMode"] = alphaMode,
                ["doubleSided"] = !mesh.Flags.HasFlag(Borg2MaterialFlags.CullBack),
                ["extras"] = new JsonObject { ["borg2Flags"] = (uint)mesh.Flags },
            };
            if (alphaMode == "MASK")
                material["alphaCutoff"] = 0.5f;
            if (mesh.Flags.HasFlag(Borg2MaterialFlags.Unlit))
            {
                gltf.UseExtension("KHR_materials_unlit");
                material["extensions"] = new JsonObject { ["KHR_materials_unlit"] = new JsonObject() };
            }
            return materials[key] = gltf.AddMaterial(material);
        }

        int GltfTexture(int texture, Borg2MaterialFlags flags)
        {
            if (!images.TryGetValue(texture, out var image))
                images[texture] = image = gltf.AddPng(Png.Encode(textures[texture].Value.Decode()), $"borg1 {scene.Textures[texture]}");

            var filter = flags.HasFlag(Borg2MaterialFlags.PointFilter) ? Nearest : Linear;
            var samplerKey = (filter, filter, Wrap(flags, Borg2MaterialFlags.WrapS, Borg2MaterialFlags.MirrorS),
                Wrap(flags, Borg2MaterialFlags.WrapT, Borg2MaterialFlags.MirrorT));
            if (!samplers.TryGetValue(samplerKey, out var sampler))
                samplers[samplerKey] = sampler = gltf.AddSampler(samplerKey.Item1, samplerKey.Item2, samplerKey.Item3, samplerKey.Item4);

            if (!gltfTextures.TryGetValue((image, sampler), out var result))
                gltfTextures[(image, sampler)] = result = gltf.AddTexture(image, sampler);
            return result;
        }
    }

    /// <summary>
    /// Bakes a Borg6 into one sample per tick, from time 1 to time <c>Length + 1</c>, which is time 1
    /// again, so the clip lasts one loop. Linear interpolation then matches the game at every tick.
    /// </summary>
    private static JsonObject Animation(GltfBuilder gltf, Borg5Scene scene, int index, Borg6Animation animation,
        Dictionary<int, int> nodes, Dictionary<int, int> billboardNodes)
    {
        var count = animation.Length + 1;
        var driven = animation.Targets.Select(t => t.Transform).ToHashSet();
        var moved = Enumerable.Range(0, scene.Transforms.Count).Where(Moves).ToList();

        // Each path of each node the animation moves, at each sample.
        var translations = new SortedDictionary<int, Vector3[]>();
        var rotations = new SortedDictionary<int, Quaternion[]>();
        var scales = new SortedDictionary<int, Vector3[]>();

        var player = new Borg6Player(animation);
        var posed = scene.Transforms.ToArray();
        for (var sample = 0; sample < count; sample++)
        {
            if (sample > 0)
                player.Tick();
            for (var i = 0; i < animation.Targets.Count; i++)
                posed[animation.Targets[i].Transform] = player.Pose(i, scene.Transforms[animation.Targets[i].Transform]);

            foreach (var t in moved)
            {
                var transform = posed[t];
                if (t == 0)
                {
                    var view = transform.LocalMatrix();
                    Samples(translations, nodes[0])[sample] = view.Translation;
                    SetRotation(nodes[0], sample, Quaternion.CreateFromRotationMatrix(view));
                }
                else if (t < scene.FirstNode)
                {
                    SetRotation(nodes[t], sample, Quaternion.CreateFromRotationMatrix(Facing(transform.Direction)));
                }
                else
                {
                    if (driven.Contains(t) && transform.Trs() is { } trs)
                    {
                        Samples(translations, nodes[t])[sample] = trs.Translation;
                        SetRotation(nodes[t], sample, trs.Rotation);
                        Samples(scales, nodes[t])[sample] = trs.Scale;
                    }
                    if (billboardNodes.TryGetValue(t, out var billboard))
                    {
                        Samples(translations, billboard)[sample] = World(t).Translation;
                        Samples(scales, billboard)[sample] = transform.Scale;
                    }
                }
            }
        }

        var times = gltf.AddTimes(Enumerable.Range(0, count).Select(i => i / 60f).ToList());
        var samplers = new JsonArray();
        var channels = new JsonArray();
        void Channel(int node, string path, int output)
        {
            channels.Add(new JsonObject
            {
                ["sampler"] = samplers.Count,
                ["target"] = new JsonObject { ["node"] = node, ["path"] = path },
            });
            samplers.Add(new JsonObject { ["input"] = times, ["output"] = output, ["interpolation"] = "LINEAR" });
        }
        foreach (var (node, values) in translations)
            Channel(node, "translation", gltf.AddKeyValues(values));
        foreach (var (node, values) in rotations)
            Channel(node, "rotation", gltf.AddKeyValues(values));
        foreach (var (node, values) in scales)
            Channel(node, "scale", gltf.AddKeyValues(values));

        return new JsonObject
        {
            ["name"] = $"borg6 {index}",
            ["channels"] = channels,
            ["samplers"] = samplers,
            ["extras"] = new JsonObject { ["borg6"] = index, ["length"] = animation.Length },
        };

        T[] Samples<T>(SortedDictionary<int, T[]> paths, int node) =>
            paths.TryGetValue(node, out var values) ? values : paths[node] = new T[count];

        // Neighbouring samples stay in the same hemisphere, so interpolation takes the short way.
        void SetRotation(int node, int sample, Quaternion rotation)
        {
            var q = Samples(rotations, node);
            rotation = Quaternion.Normalize(rotation);
            q[sample] = sample > 0 && Quaternion.Dot(q[sample - 1], rotation) < 0 ? -rotation : rotation;
        }

        // A transform moves if it's driven, or if it's a billboard below one that is: billboards
        // are drawn at their world position, on a separate node. The game flattens model nodes with
        // other ops, so they aren't exported.
        bool Moves(int t)
        {
            if (driven.Contains(t))
                return t < scene.FirstNode || scene.Transforms[t].Op == Borg5TransformOp.PivotRotate;
            if (!billboardNodes.ContainsKey(t))
                return false;
            for (var p = scene.Parent(t); p is { } parent; p = scene.Parent(parent))
                if (driven.Contains(parent))
                    return true;
            return false;
        }

        Matrix4x4 World(int t)
        {
            var m = posed[t].LocalMatrix();
            for (var p = scene.Parent(t); p is { } parent; p = scene.Parent(parent))
                m *= posed[parent].LocalMatrix();
            return m;
        }
    }

    /// <summary>What the game skips: hidden models, missing or empty display lists, and fully transparent ones.</summary>
    private static bool IsDrawn(Borg2Geometry model, int displayList) =>
        (model.Unknown28 & 1) == 0 && model.Alpha < 1 && displayList < model.Meshes.Count &&
        model.Meshes[displayList].Indices.Length > 0 && model.Meshes[displayList].Tint.W < 1;

    private static JsonObject Primitive(GltfBuilder gltf, Borg2Geometry model, Borg2Mesh mesh, Borg1Texture? texture)
    {
        var scale = model.Scale / 16;
        var attributes = new JsonObject
        {
            ["POSITION"] = gltf.AddPositions(mesh.Vertices.Select(v => new Vector3(v.X, v.Y, v.Z) * scale).ToList()),
        };
        if (!mesh.Flags.HasFlag(Borg2MaterialFlags.Unlit))
            attributes["NORMAL"] = gltf.AddVectors(mesh.Vertices.Select(Normal).ToList());
        else if (texture is not null)
            attributes["COLOR_0"] = gltf.AddVectors(mesh.Vertices.Select(v => new Vector3(v.R, v.G, v.B) / 255).ToList());
        else
            attributes["COLOR_0"] = gltf.AddVectors(mesh.Vertices.Select(v => new Vector4(v.R, v.G, v.B, v.A) / 255).ToList());
        if (texture is not null)
        {
            // 64 units of S or T per texel; T = 0 is the texture's first row, which is glTF's top.
            var size = new Vector2(texture.Width * 64, texture.Height * 64);
            attributes["TEXCOORD_0"] = gltf.AddVectors(mesh.Vertices.Select(v => new Vector2(v.S, v.T) / size).ToList());
        }

        return new JsonObject
        {
            ["attributes"] = attributes,
            ["indices"] = gltf.AddIndices(mesh.Indices, mesh.Vertices.Length),
        };
    }

    private static Vector3 Normal(N64Vertex v)
    {
        var (x, y, z) = v.Normal;
        var n = new Vector3(x, y, z);
        return n == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(n);
    }

    /// <summary>Sets a node's translation, rotation and scale, leaving out the defaults.</summary>
    private static void SetTrs(JsonObject node, Vector3 scale, Quaternion rotation, Vector3 translation)
    {
        if (translation != Vector3.Zero)
            node["translation"] = new JsonArray(translation.X, translation.Y, translation.Z);
        if (rotation != Quaternion.Identity)
            node["rotation"] = QuaternionJson(rotation);
        if (scale != Vector3.One)
            node["scale"] = new JsonArray(scale.X, scale.Y, scale.Z);
    }

    private static void SetTrs(GltfBuilder gltf, int node, Vector3 scale, Quaternion rotation, Vector3 translation) =>
        SetTrs(gltf.Node(node), scale, rotation, translation);

    private static JsonArray QuaternionJson(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        return new JsonArray(q.X, q.Y, q.Z, q.W);
    }

    private static JsonArray QuaternionJson(Matrix4x4 rotation) => QuaternionJson(Quaternion.CreateFromRotationMatrix(rotation));

    private static int Wrap(Borg2MaterialFlags flags, Borg2MaterialFlags wrap, Borg2MaterialFlags mirror) =>
        flags.HasFlag(mirror) ? MirroredRepeat : flags.HasFlag(wrap) ? Repeat : ClampToEdge;

    /// <summary>A rotation whose +Z is <paramref name="direction"/>, so a glTF light (which shines down −Z) shines away from it.</summary>
    private static Matrix4x4 Facing(Vector3 direction)
    {
        var z = Vector3.Normalize(direction);
        var up = MathF.Abs(z.Y) > 0.99f ? Vector3.UnitX : Vector3.UnitY;
        var x = Vector3.Normalize(Vector3.Cross(up, z));
        var y = Vector3.Cross(z, x);
        return new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1);
    }
}
