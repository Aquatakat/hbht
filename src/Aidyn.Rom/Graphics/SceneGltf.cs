using System.Numerics;
using System.Text.Json.Nodes;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Graphics;

/// <summary>
/// Converts a Borg5 scene, with its models, textures, camera and lights, to a glTF file in scene
/// units (roadmap 1A). See docs/formats/scenes.md for how each part maps.
/// </summary>
public static class SceneGltf
{
    private const int Nearest = 9728, Linear = 9729;
    private const int Repeat = 10497, ClampToEdge = 33071, MirroredRepeat = 33648;

    /// <summary>
    /// The texel alpha a cut-out draws from (a <c>MASK</c> material's <c>alphaCutoff</c>). The N64 cuts by coverage, with no
    /// alpha compare (<c>G_RM_AA_ZB_TEX_EDGE2</c>, <c>0x113078</c>: <c>CVG_X_ALPHA</c> and <c>ALPHA_CVG_SEL</c>): a pixel's
    /// coverage of eight is multiplied by the filtered texel alpha, and the pixel is drawn while any of it is left, so from
    /// alpha 32 of 255, an eighth (see docs/systems/rendering.md).
    /// </summary>
    public const float CutOutAlpha = 0.125f;

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
            var texture = draw.Texture;
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
        // billboard is drawn with only its scale and world position, turned to face the camera and
        // rolled by its rot.z, so its meshes go on a separate root node (see docs/formats/scenes.md).
        var first = scene.FirstNode;
        var roots = new List<int>();
        var billboards = new List<int>();
        var nodes = new Dictionary<int, int>();
        var billboardNodes = new Dictionary<int, int>();
        for (var t = first; t < scene.Transforms.Count; t++)
        {
            var transform = scene.Transforms[t];
            var node = new JsonObject { ["name"] = SceneNames.Transform(t) };
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
            node["extras"] = new JsonObject { [ExtrasKeys.Transform] = t, ["op"] = transform.Op.ToString(), [ExtrasKeys.Billboard] = billboard };
            if (transform.Pivot != Vector3.Zero)
                node["extras"]![ExtrasKeys.Pivot] = new JsonArray(transform.Pivot.X, transform.Pivot.Y, transform.Pivot.Z);
            var n = nodes[t] = gltf.AddNode(node);
            if (scene.Parent(t) is null)
                roots.Add(n);
        }
        var cards = new Dictionary<int, int>();
        foreach (var t in billboards)
        {
            var card = new JsonObject { ["name"] = SceneNames.Card(t) };
            SetTrs(card, Vector3.One, Roll(scene.Transforms[t]), Vector3.Zero);
            card["mesh"] = gltf.AddMesh(new JsonObject { ["primitives"] = primitives[t] });
            card["extras"] = new JsonObject { [ExtrasKeys.Transform] = t, ["card"] = true };
            cards[t] = gltf.AddNode(card);
            var facing = gltf.AddNode(new JsonObject
            {
                ["name"] = SceneNames.Facing(t),
                ["children"] = new JsonArray(cards[t]),
                ["extras"] = new JsonObject { [ExtrasKeys.Transform] = t, [ExtrasKeys.Facing] = true },
            });
            var node = new JsonObject { ["name"] = SceneNames.Billboard(t), ["children"] = new JsonArray(facing) };
            SetTrs(node, scene.Transforms[t].Scale, Quaternion.Identity, scene.WorldMatrix(t).Translation);
            node["extras"] = new JsonObject { [ExtrasKeys.Transform] = t, [ExtrasKeys.Billboard] = true };
            roots.Add(billboardNodes[t] = gltf.AddNode(node));
        }

        var camera = Borg3Camera.Parse(read(scene.Camera));
        roots.Add(gltf.AddNode(new JsonObject
        {
            ["name"] = SceneNames.Camera,
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
        if (ScrollingTextures(scene, textures) is { Count: > 0 } scrolling)
            extras[ExtrasKeys.ScrollingTextures] = scrolling;
        for (var i = 0; i < scene.Lights.Count; i++)
        {
            var light = Borg4Light.Parse(read(scene.Lights[i]));
            var color = new JsonArray(light.Red, light.Green, light.Blue);
            if (i == scene.Lights.Count - 1)
            {
                extras[ExtrasKeys.AmbientLight] = color;
                extras["ambientBorg4"] = scene.Lights[i];
                break;
            }
            roots.Add(nodes[1 + i] = gltf.AddNode(new JsonObject
            {
                ["name"] = SceneNames.Light(i),
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
            // The transform's scale tints its light, channel by channel (BorgAnimDrawSceneRaw, 0x80097d54).
            if (scene.Transforms[1 + i].Scale != Vector3.One)
                gltf.Node(nodes[1 + i])["scale"] = new JsonArray(scene.Transforms[1 + i].Scale.X, scene.Transforms[1 + i].Scale.Y, scene.Transforms[1 + i].Scale.Z);
        }

        // Emitters on one of the scene's own locators become child nodes of the locator's transform,
        // so they follow its animation. The others sit on scenes attached at run time.
        if (scene.ParticleEmitters.Count > 0)
        {
            var list = new JsonArray();
            for (var i = 0; i < scene.ParticleEmitters.Count; i++)
            {
                var emitter = scene.ParticleEmitters[i];
                var json = EmitterJson(emitter);
                if (emitter.Locator.Scene == 0 && Locator(emitter.Locator.Index) is var (t, position) && nodes.TryGetValue(t, out var parent))
                {
                    var node = gltf.AddNode(new JsonObject
                    {
                        ["name"] = SceneNames.Emitter(i),
                        ["translation"] = new JsonArray(position.X, position.Y, position.Z),
                        ["extras"] = new JsonObject { ["emitter"] = i },
                    });
                    var p = gltf.Node(parent);
                    if (p["children"] is not JsonArray children)
                        p["children"] = children = [];
                    children.Add(node);
                    json["node"] = node;
                }
                list.Add(json);
            }
            extras[ExtrasKeys.ParticleEmitters] = list;
        }

        // Every locator becomes a node on its transform, for what the game attaches there at run
        // time: particle emitters, items in hand, and Alaron's head (locator 3).
        for (var i = 0; i < 8; i++)
        {
            if (Locator(i) is not var (t, position) || !nodes.TryGetValue(t, out var parent))
                continue;
            // What a body holds at the locator is turned by the locator model's rotation too (SceneGetLocatorMtx, 0x8009EB10:
            // guRotateRPYF of the Borg2's rotation in degrees, then its position, then the transform), which its emitters
            // (SceneGetLocatorPos) and a head don't take: so it hangs from a node of its own under the locator's.
            var attachment = new JsonObject { ["name"] = SceneNames.Attachment(i) };
            if (LocatorRotation(i) is { } rotation && rotation != Vector3.Zero)
                attachment["rotation"] = QuaternionJson(RotateRpy(rotation));
            var node = gltf.AddNode(new JsonObject
            {
                ["name"] = SceneNames.Locator(i),
                ["translation"] = new JsonArray(position.X, position.Y, position.Z),
                ["extras"] = new JsonObject { ["locator"] = i },
                ["children"] = new JsonArray(gltf.AddNode(attachment)),
            });
            var p = gltf.Node(parent);
            if (p["children"] is not JsonArray children)
                p["children"] = children = [];
            children.Add(node);
        }

        // An animation with no targets only holds the scene still for its length. glTF can't have
        // an empty animation, so those are listed in the extras.
        var still = new JsonArray();
        // Whether a model transform's scale mirrors it, at rest or as an animation poses it.
        var mirrors = scene.Transforms.Skip(scene.FirstNode).Any(t => Mirrors(t.Scale));
        foreach (var (borg6, animation) in animations ?? [])
        {
            if (animation.Scene != index)
                throw new ArgumentException($"Borg6 {borg6} animates scene {animation.Scene}, not {index}.", nameof(animations));
            if (animation.Targets.Count == 0)
                still.Add(new JsonObject { [ExtrasKeys.Borg6] = borg6, [ExtrasKeys.Length] = animation.Length });
            else
                gltf.AddAnimation(Animation(gltf, scene, borg6, animation, nodes, billboardNodes, cards, ref mirrors));
        }
        if (still.Count > 0)
            extras[ExtrasKeys.StillAnimations] = still;
        // The N64 culls a mirrored model's front, which the runtime has to undo Godot's correction of.
        if (mirrors)
            extras[ExtrasKeys.Mirrors] = true;

        return gltf.ToGlb(roots, extras);

        // A locator is the first model marked with its number, at the model's position, drawn
        // with the transform it's last loaded with.
        (int Transform, Vector3 Position)? Locator(int index)
        {
            for (var m = 0; m < models.Length; m++)
            {
                var model = models[m].Value;
                if (model.Locator != index)
                    continue;
                return scene.ModelTransform(m) is { } t && t >= first ? (t, model.Position * model.Scale / 16) : null;
            }
            return null;
        }

        // The rotation of the model that's locator i, in radians, as Locator finds it.
        Vector3? LocatorRotation(int index)
        {
            for (var m = 0; m < models.Length; m++)
            {
                if (models[m].Value.Locator == index)
                    return models[m].Value.Rotation;
            }
            return null;
        }

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
                pbr["baseColorFactor"] = LinearColor(mesh.Tint, alpha);
            }

            var material = new JsonObject
            {
                ["pbrMetallicRoughness"] = pbr,
                ["alphaMode"] = alphaMode,
                ["doubleSided"] = !mesh.Flags.HasFlag(Borg2MaterialFlags.CullBack),
                ["extras"] = new JsonObject { [ExtrasKeys.Borg2Flags] = (uint)mesh.Flags },
            };
            if (texture is { } bound)
                material["extras"]![ExtrasKeys.Borg1] = scene.Textures[bound];
            if (alphaMode == "MASK")
                material["alphaCutoff"] = CutOutAlpha;
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
                images[texture] = image = gltf.AddPng(Png.Encode(textures[texture].Value.Decode()), SceneNames.Image(scene.Textures[texture]));

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
        Dictionary<int, int> nodes, Dictionary<int, int> billboardNodes, Dictionary<int, int> cards, ref bool mirrors)
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
                    // A light's scale tints it.
                    Samples(scales, nodes[t])[sample] = transform.Scale;
                }
                else
                {
                    if (driven.Contains(t) && transform.Trs() is { } trs)
                    {
                        Samples(translations, nodes[t])[sample] = trs.Translation;
                        SetRotation(nodes[t], sample, trs.Rotation);
                        Samples(scales, nodes[t])[sample] = trs.Scale;
                        mirrors |= Mirrors(trs.Scale);
                    }
                    if (billboardNodes.TryGetValue(t, out var billboard))
                    {
                        Samples(translations, billboard)[sample] = World(t).Translation;
                        Samples(scales, billboard)[sample] = transform.Scale;
                        if (driven.Contains(t))
                            SetRotation(cards[t], sample, Roll(transform));
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
            ["name"] = SceneNames.Animation(index),
            ["channels"] = channels,
            ["samplers"] = samplers,
            ["extras"] = new JsonObject { [ExtrasKeys.Borg6] = index, [ExtrasKeys.Length] = animation.Length },
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

    private static JsonObject EmitterJson(Borg5ParticleEmitter e)
    {
        static JsonArray V3(Vector3 v) => new(v.X, v.Y, v.Z);
        static JsonArray V4(Vector4 v) => new(v.X, v.Y, v.Z, v.W);
        return new JsonObject
        {
            ["locator"] = e.Locator.Index,
            ["locatorScene"] = e.Locator.Scene,
            ["flags"] = e.Flags.ToString(),
            ["request"] = e.Flags.HasFlag(ParticleEmitterFlags.ActorRequest) ? e.Request : null,
            ["texture"] = e.Texture,
            ["delay"] = e.Delay,
            ["lifetime"] = e.Lifetime,
            ["particleLifetime"] = e.ParticleLifetime,
            ["interval"] = e.Interval,
            ["burst"] = e.Burst,
            ["spawnJitter"] = e.SpawnJitter,
            ["velocityJitter"] = e.VelocityJitter,
            ["direction"] = V3(e.Direction),
            ["speed"] = e.Speed,
            ["acceleration"] = V4(e.Acceleration),
            ["accelerationChange"] = V4(e.AccelerationChange),
            ["target"] = e.Flags.HasFlag(ParticleEmitterFlags.Target) ? new JsonObject
            {
                ["locator"] = e.Target.Index,
                ["locatorScene"] = e.Target.Scene,
                ["jitter"] = e.TargetJitter,
            } : null,
            ["scale"] = new JsonArray(e.MinScale, e.MaxScale),
            ["endScale"] = e.EndScale,
            [ExtrasKeys.Color] = new JsonArray(V4(e.MinColor), V4(e.MaxColor)),
            ["fadeColor"] = V4(e.FadeColor),
        };
    }

    /// <summary>What the game skips: hidden models, missing or empty display lists, and fully transparent ones.</summary>
    internal static bool IsDrawn(Borg2Geometry model, int displayList) =>
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
            attributes["COLOR_0"] = gltf.AddVectors(mesh.Vertices.Select(v => ToLinear(new Vector3(v.R, v.G, v.B) / 255)).ToList());
        else
            attributes["COLOR_0"] = gltf.AddVectors(mesh.Vertices.Select(v => new Vector4(ToLinear(new Vector3(v.R, v.G, v.B) / 255), v.A / 255f)).ToList());
        if (texture is not null)
        {
            // 64 units of S or T per texel; T = 0 is the texture's first row, which is glTF's top. An environment-mapped
            // list's texture is scaled by its size (G_TEXTURE with W << 6 and H << 6, where others have 0x8000), so
            // 32768 units span it; a lit one's coordinates are made from its normals instead.
            var size = mesh.Flags.HasFlag(Borg2MaterialFlags.EnvironmentMap) ? new Vector2(32768) : new Vector2(texture.Width * 64, texture.Height * 64);
            attributes["TEXCOORD_0"] = gltf.AddVectors(mesh.Vertices.Select(v => new Vector2(v.S, v.T) / size).ToList());
        }

        return new JsonObject
        {
            ["attributes"] = attributes,
            ["indices"] = gltf.AddIndices(mesh.Indices, mesh.Vertices.Length),
        };
    }

    /// <summary>
    /// The N64 has no linear light: its vertex colours and tints are the values it puts on screen,
    /// which glTF colours aren't. Decoding them as sRGB makes a renderer that multiplies in linear
    /// light come out almost as the N64 does, because a power curve carries through a product.
    /// </summary>
    public static Vector3 ToLinear(Vector3 srgb) => new(ToLinear(srgb.X), ToLinear(srgb.Y), ToLinear(srgb.Z));

    private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    private static JsonArray LinearColor(Vector4 tint, float alpha)
    {
        var c = ToLinear(new Vector3(tint.X, tint.Y, tint.Z));
        return new JsonArray(c.X, c.Y, c.Z, alpha);
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

    /// <summary>
    /// The scene's textures that scroll, with how many times each draw of the scene loads each one,
    /// which is how many times it scrolls (see <c>Aidyn.Core.Scenes.TextureScroll</c>).
    /// </summary>
    private static JsonArray ScrollingTextures(Borg5Scene scene, Lazy<Borg1Texture>[] textures)
    {
        var loads = new List<(int Texture, int Count)>();
        foreach (var (op, value) in scene.Instructions)
        {
            if (op != Borg5Opcode.LoadTexture || value >= textures.Length || !textures[value].Value.Flags.HasFlag(Borg1Flags.Moving))
                continue;
            var i = loads.FindIndex(l => l.Texture == value);
            if (i < 0)
                loads.Add((value, 1));
            else
                loads[i] = (value, loads[i].Count + 1);
        }
        return new JsonArray(loads.Select(l => (JsonNode)new JsonObject
        {
            [ExtrasKeys.Borg1] = scene.Textures[l.Texture],
            [ExtrasKeys.Width] = textures[l.Texture].Value.Width,
            [ExtrasKeys.Move] = textures[l.Texture].Value.Move,
            [ExtrasKeys.Loads] = l.Count,
        }).ToArray());
    }

    /// <summary>
    /// How a billboard's card rolls about its own +Z before it's turned to the camera: by
    /// <c>rot.z</c>, which <c>guAlignF</c> takes as its angle.
    /// </summary>
    private static Quaternion Roll(Borg5Transform transform) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitZ, transform.Rotation.Z * (MathF.PI / 180));

    /// <summary>
    /// <c>guRotateRPYF</c> (<c>0x800C1660</c>) of a rotation in radians: roll about X, then pitch about Y, then heading about Z, as
    /// row vectors, which <see cref="Matrix4x4"/>'s are too.
    /// </summary>
    internal static Matrix4x4 RotateRpy(Vector3 rotation) =>
        Matrix4x4.CreateRotationX(rotation.X) * Matrix4x4.CreateRotationY(rotation.Y) * Matrix4x4.CreateRotationZ(rotation.Z);

    private static JsonArray QuaternionJson(Quaternion q)
    {
        q = Quaternion.Normalize(q);
        return new JsonArray(q.X, q.Y, q.Z, q.W);
    }

    private static JsonArray QuaternionJson(Matrix4x4 rotation) => QuaternionJson(Quaternion.CreateFromRotationMatrix(rotation));

    private static int Wrap(Borg2MaterialFlags flags, Borg2MaterialFlags wrap, Borg2MaterialFlags mirror) =>
        flags.HasFlag(mirror) ? MirroredRepeat : flags.HasFlag(wrap) ? Repeat : ClampToEdge;

    /// <summary>Whether a scale turns a model inside out: an odd number of its axes negative.</summary>
    private static bool Mirrors(Vector3 scale) => scale.X * scale.Y * scale.Z < 0;

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
