using System.Runtime.CompilerServices;
using System.Text.Json;
using Aidyn.Plugins.Assets;
using Godot;

namespace Aidyn.Views;

/// <summary>
/// Draws scene glTFs as the original's combiner and blender do (roadmap M1). Every material Godot's
/// glTF import makes is replaced with one built from the glTF itself, because the import's
/// conversions of colours and samplers aren't the N64's.
/// </summary>
/// <remarks>
/// <para>
/// The N64 has no linear light. Texels, vertex colours and tints are the values it puts on screen
/// (the game turns the VI's gamma off), and the combiner multiplies them as they are. The export
/// stores colours as linear, as glTF requires, so the shader turns them back into on-screen values,
/// multiplies and fogs there as the N64 does, and hands Godot the linear result, or with
/// <see cref="OnScreenFramebuffer"/> the on-screen values themselves, so that Godot blends them as
/// the N64 does.
/// </para>
/// <para>
/// Textured draws are texel × shade × prim, and untextured ones shade × prim (<c>setStaticMode</c>'s
/// combine modes, cycle 2 multiplying by <c>PRIMITIVE</c>). Shade is the vertex colour on unlit
/// display lists, and the RSP's lighting on lit ones (see <see cref="Lighting"/>). Prim is the
/// scene's tint (<c>SceneData.colorFloats</c>, <c>FUN_800a1184</c>): <see cref="TintParameter"/> for
/// scenery, averaged with a scene object's own tint where it has one, and white for what the sun
/// lights instead. Untextured draws also take the mesh's tint colour.
/// </para>
/// <para>
/// The shade and the fog are worked out per vertex, in on-screen values, and interpolated as the RDP does: in on-screen
/// values, straight across the screen, without perspective. Interpolating the glTF's linear vertex colours instead made
/// every blend between a bright vertex and a dark one brighter and less tinted (Oriana's hut's floor, whose vertices run
/// from white to a dim teal, came out warm where the original's is green).
/// </para>
/// <para>
/// With <see cref="RspLighting"/>, as in the runtime and the Inspector, lit display lists are lit per vertex as the
/// RSP lights them: an ambient plus the sun's colour times the cosine to it, clamped to 1, in
/// on-screen values, from the sky's globals (<see cref="SetSky"/>, <see cref="SetSkyLight"/>). Without it, Godot's lights
/// stand in.
/// </para>
/// <para>
/// Fog is the RSP's: a per-vertex amount from the depth buffer's <c>z / w</c>, between the fog
/// start and end on the 0–1000 scale <c>gSPFogPosition</c> takes, blended toward the fog colour
/// in on-screen values before the pixel is drawn (<c>G_RM_FOG_SHADE_A</c>). Draws that Godot's
/// lights light fog through its <c>FOG</c> instead, after its lighting, in linear light.
/// </para>
/// <para>
/// Borg2 materials clamp, repeat or mirror S and T separately (faces are often half a texture,
/// mirrored), which a <see cref="BaseMaterial3D"/> can't, so the shader wraps each axis itself.
/// A scrolling texture's image then wraps round within the texture by <see cref="ScrollParameter"/>,
/// which the runtime sets; each material keeps its Borg1 in <see cref="Borg1Meta"/> for that.
/// </para>
/// </remarks>
public static class N64Material
{
    /// <summary>Global: the scenery's tint, <c>gMainColor</c>, in on-screen values.</summary>
    public const string TintParameter = "n64_tint";
    /// <summary>Global: the fog colour, <c>gFogColor</c>, in on-screen values.</summary>
    public const string FogColorParameter = "n64_fog_color";
    /// <summary>
    /// Global: the fog's start and end on <c>gSPFogPosition</c>'s 0–1000 scale, then the near and far
    /// planes it's measured against. No fog when far isn't beyond near.
    /// </summary>
    public const string FogParameter = "n64_fog";
    /// <summary>Per instance: a scene object's tint, in on-screen values, with alpha 1, or 0 for none.</summary>
    public const string ObjectTintParameter = "object_tint";
    /// <summary>Per instance: the scene's own tint, in on-screen values, with alpha 1, or 0 for none (<see cref="SetSceneTint"/>).</summary>
    public const string SceneTintParameter = "scene_tint";
    /// <summary>Per instance: a body's own ambient, in on-screen values, with alpha 1, or 0 for half the sky's.</summary>
    public const string BodyAmbientInstanceParameter = "body_ambient";
    /// <summary>Per instance: 1 where the scene's fog is off.</summary>
    public const string UnfoggedParameter = "unfogged";
    /// <summary>Per instance: how its lit display lists are lit, a <see cref="Lighting"/>.</summary>
    public const string LightingParameter = "lighting";
    /// <summary>
    /// Per instance: the scene's eye, which environment-mapped lists look from (see <see cref="SetEye"/>); the world's
    /// origin unless set.
    /// </summary>
    public const string EyeParameter = "eye";
    /// <summary>Global: the way to the sun, <c>gGlobals.SunPos</c>, in world space.</summary>
    public const string SunDirectionParameter = "n64_sun_direction";
    /// <summary>Global: the sun's light, <c>gSunColor</c>, in on-screen values; black where there's no sun.</summary>
    public const string SunColorParameter = "n64_sun_color";
    /// <summary>Global: bodies' ambient light, half <c>gSkyColor</c>, in on-screen values.</summary>
    public const string BodyAmbientParameter = "n64_body_ambient";
    /// <summary>Per material: how far a scrolling texture has moved toward +S, as a share of its width.</summary>
    public const string ScrollParameter = "scroll";
    /// <summary>A textured material's metadata: its texture's Borg1 archive index.</summary>
    public const string Borg1Meta = "borg1";

    /// <summary>How a model's lit display lists are lit, and whether the scenery's tint colours it.</summary>
    public enum Lighting
    {
        /// <summary>Tinted, and lit by a white ambient alone (<c>Scene::SetLightData</c>'s): scenery and fullbright scene objects.</summary>
        Scenery,
        /// <summary>Untinted, lit by half the sky colour, the sun and their point lights (<c>some_player_render_sub</c>, <see cref="SetLights"/>): the player and other bodies.</summary>
        Body,
        /// <summary>Untinted, lit by the tint, the sun and their point lights (<c>set_sun_light</c>, <see cref="SetLights"/>): scene objects' actors, unless fullbright.</summary>
        SceneObject,
        /// <summary>
        /// Untinted and unfogged, lit by an ambient and two lights of its own (<see cref="SetOwnLights"/>), apart from the
        /// world's: the pause menu's scroll, which its scene's lights light.
        /// </summary>
        Own,
    }

    /// <summary>Whether lit display lists are lit as the RSP does, from the globals, rather than by Godot's lights.</summary>
    public static bool RspLighting { get; set; }

    /// <summary>
    /// Whether draws write on-screen values rather than linear light, so that Godot blends them as
    /// the N64 does. Godot's output then needs to show them as they are, as the runtime's
    /// <c>OnScreenOutput</c> has it. Draws that Godot's lights light still write linear light, so it's for use with
    /// <see cref="RspLighting"/>.
    /// </summary>
    public static bool OnScreenFramebuffer { get; set; }

    private enum Wrap { Repeat, Clamp, Mirror }
    private enum Blend { Opaque, Mask, Blend }

    private const int Nearest = 9728, ClampToEdge = 33071, MirroredRepeat = 33648;
    private static readonly Dictionary<string, Shader> Shaders = [];
    /// <summary>Cut-out materials' blended shaders, for drawing them faded.</summary>
    private static readonly ConditionalWeakTable<Material, Shader> FadedShaders = [];
    /// <summary>Cut-out materials' blended copies, made as they're first faded.</summary>
    private static readonly ConditionalWeakTable<Material, ShaderMaterial> FadedCopies = [];
    /// <summary>One-sided materials' shaders that cull the other side, for drawing them mirrored.</summary>
    private static readonly ConditionalWeakTable<Material, Shader> MirroredShaders = [];
    /// <summary>One-sided materials' copies that cull the other side, made as they're first mirrored.</summary>
    private static readonly ConditionalWeakTable<Material, ShaderMaterial> MirroredCopies = [];
    private static bool _declared;

    /// <summary>
    /// Declares the global parameters, untinted and unfogged, if nothing has yet. Shaders that use
    /// them won't compile without them.
    /// </summary>
    public static void DeclareGlobals()
    {
        // Listing what's declared is editor-only in Godot, so this remembers.
        if (_declared)
            return;
        _declared = true;
        RenderingServer.GlobalShaderParameterAdd(TintParameter, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(1, 1, 1));
        RenderingServer.GlobalShaderParameterAdd(FogColorParameter, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(1, 1, 1));
        RenderingServer.GlobalShaderParameterAdd(FogParameter, RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(0, 1000, 0, 0));
        RenderingServer.GlobalShaderParameterAdd(SunDirectionParameter, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0, 1, 0));
        RenderingServer.GlobalShaderParameterAdd(SunColorParameter, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(0, 0, 0));
        RenderingServer.GlobalShaderParameterAdd(BodyAmbientParameter, RenderingServer.GlobalShaderParameterType.Vec3, new Vector3(1, 1, 1));
    }

    /// <summary>
    /// Sets every global from the core's sky as it last worked its colours out (<see cref="Core.Sky.SkyDirector.Apply"/>): the
    /// scenery's tint, its fog for a camera with these clip planes, and the light (<see cref="SetSkyLight"/>). The runtime's
    /// sky does, each frame. Nothing until a sky has been chosen.
    /// </summary>
    public static void SetSky(Core.Sky.SkyDirector sky, float near, float far)
    {
        if (!sky.Applied)
            return;
        DeclareGlobals();
        RenderingServer.GlobalShaderParameterSet(TintParameter, sky.Colors.Tint.ToGodot());
        RenderingServer.GlobalShaderParameterSet(FogColorParameter, sky.Colors.Fog.ToGodot());
        RenderingServer.GlobalShaderParameterSet(FogParameter, new Vector4(sky.SceneryFogStart, 1000, near, far));
        SetSkyLight(sky);
    }

    /// <summary>
    /// Sets the globals that light lit display lists as the RSP does (<see cref="RspLighting"/>) from the core's sky: bodies'
    /// ambient, half the sky colour, and the sun's light and its way. The editor sets these alone, from its game's sky, and
    /// leaves the scenery untinted and unfogged. Nothing until a sky has been chosen.
    /// </summary>
    public static void SetSkyLight(Core.Sky.SkyDirector sky)
    {
        if (!sky.Applied)
            return;
        DeclareGlobals();
        RenderingServer.GlobalShaderParameterSet(BodyAmbientParameter, sky.BodyAmbient.ToGodot());
        RenderingServer.GlobalShaderParameterSet(SunColorParameter, sky.SunColor.ToGodot());
        RenderingServer.GlobalShaderParameterSet(SunDirectionParameter, sky.SunDirection.ToGodot());
    }

    /// <summary>Replaces the imported materials of <paramref name="scene"/>, loaded from the glTF whose JSON is <paramref name="root"/>.</summary>
    public static void Apply(JsonElement root, GltfState state, Node scene)
    {
        DeclareGlobals();
        if (!root.TryGetProperty("materials", out var materials))
            return;

        var images = state.GetImages();
        var textures = new Dictionary<int, Texture2D>();
        var imported = state.GetMaterials();
        var replacements = new Dictionary<Material, Material>();
        var i = 0;
        foreach (var material in materials.EnumerateArray())
        {
            if (i < imported.Count && imported[i] is { } godot)
                replacements[godot] = Create(root, material, Texture);
            i++;
        }

        var stack = new Stack<Node>([scene]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.GetChildren())
                stack.Push(child);
            if (node is not MeshInstance3D { Mesh: { } mesh } instance)
                continue;
            for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                if (instance.GetActiveMaterial(surface) is { } active && replacements.TryGetValue(active, out var replacement))
                    instance.SetSurfaceOverrideMaterial(surface, replacement);
            }
        }

        // Godot's runtime glTF import leaves textures without mipmaps.
        Texture2D Texture(int image)
        {
            if (textures.TryGetValue(image, out var texture))
                return texture;
            texture = images[image];
            if (texture.GetImage() is { } data && !data.HasMipmaps())
            {
                data.GenerateMipmaps();
                texture = ImageTexture.CreateFromImage(data);
            }
            return textures[image] = texture;
        }
    }

    /// <summary>
    /// The material to draw a surface with while its model is faded, or null if it's the same one.
    /// </summary>
    /// <remarks>
    /// The original draws a faded scene blended, texel alpha times the fade, with no alpha test
    /// (<c>FUN_800a1184</c>'s <c>0x1049D8</c>), so its cut-outs blend too. Godot draws an alpha-scissor
    /// material at full alpha wherever it passes the test, whatever the instance's transparency, so
    /// a cut-out gets a copy that blends instead. Blended and opaque materials fade as they are.
    /// </remarks>
    public static ShaderMaterial? Faded(Material material)
    {
        if (!FadedShaders.TryGetValue(material, out var shader))
            return null;
        if (!FadedCopies.TryGetValue(material, out var copy))
        {
            copy = (ShaderMaterial)material.Duplicate();
            copy.Shader = shader;
            FadedCopies.Add(material, copy);
        }
        return copy;
    }

    /// <summary>
    /// The material to draw a surface with while its world matrix mirrors it, or null if it's the same one: a
    /// one-sided material's copy that culls its front instead of its back (see <see cref="MirroredMeshes"/>).
    /// </summary>
    public static ShaderMaterial? Mirrored(Material material)
    {
        if (!MirroredShaders.TryGetValue(material, out var shader))
            return null;
        if (!MirroredCopies.TryGetValue(material, out var copy))
        {
            copy = (ShaderMaterial)material.Duplicate();
            copy.Shader = shader;
            MirroredCopies.Add(material, copy);
        }
        return copy;
    }

    /// <summary>Moves a scrolling texture's image, in its material and the material's faded and mirrored copies.</summary>
    public static void SetScroll(ShaderMaterial material, float scroll)
    {
        material.SetShaderParameter(ScrollParameter, scroll);
        if (FadedCopies.TryGetValue(material, out var copy))
            copy.SetShaderParameter(ScrollParameter, scroll);
        if (MirroredCopies.TryGetValue(material, out var mirrored))
            mirrored.SetShaderParameter(ScrollParameter, scroll);
    }

    /// <summary>A copy of a material for a copy of a scene, which fades and mirrors as the original does.</summary>
    public static ShaderMaterial Copy(ShaderMaterial material)
    {
        var copy = (ShaderMaterial)material.Duplicate();
        if (FadedShaders.TryGetValue(material, out var shader))
            FadedShaders.Add(copy, shader);
        if (MirroredShaders.TryGetValue(material, out var mirrored))
            MirroredShaders.Add(copy, mirrored);
        return copy;
    }

    /// <summary>Sets how every mesh under <paramref name="root"/> is lit.</summary>
    public static void SetLighting(Node root, Lighting lighting) => SetOnMeshes(root, LightingParameter, (int)lighting);

    /// <summary>
    /// Sets the eye that every environment-mapped list under <paramref name="root"/> looks from, in world space
    /// (scene <c>+0xF8</c>): a cinematic's scene has its own camera's position, and a scene given none keeps the
    /// world's origin.
    /// </summary>
    public static void SetEye(Node root, Vector3 eye) => SetOnMeshes(root, EyeParameter, eye);

    /// <summary>
    /// Lights every mesh under <paramref name="root"/> by an ambient and two lights, each a colour and the way to it in
    /// world space, in on-screen values, for <see cref="Lighting.Own"/>.
    /// </summary>
    public static void SetOwnLights(Node root, Color ambient, (Color Color, Vector3 Toward) first, (Color Color, Vector3 Toward) second) =>
        SetOwnLights(root, ambient, [first, second]);

    /// <summary>
    /// Lights every mesh under <paramref name="root"/> by an ambient and up to <see cref="MaxLights"/> lights, each a
    /// colour and the way to it in world space, in on-screen values, for <see cref="Lighting.Own"/>; the rest are black.
    /// </summary>
    public static void SetOwnLights(Node root, Color ambient, IReadOnlyList<(Color Color, Vector3 Toward)> lights)
    {
        SetOnMeshes(root, "own_ambient", new Vector3(ambient.R, ambient.G, ambient.B));
        for (var i = 0; i < MaxLights; i++)
        {
            var (color, toward) = i < lights.Count ? lights[i] : (Colors.Black, Vector3.Up);
            SetOnMeshes(root, LightColor[i], new Vector3(color.R, color.G, color.B));
            SetOnMeshes(root, LightDirection[i], toward.Normalized());
        }
    }

    /// <summary>The most directional lights a mesh takes besides the sun.</summary>
    public const int MaxLights = 4;

    /// <summary>
    /// Lights the given meshes by up to <see cref="MaxLights"/> directional lights besides the sun and ambient: a body's or
    /// actor's point lights this tick (<c>Aidyn.Core.Lights.WorldLights</c>), in on-screen values, the rest black.
    /// </summary>
    public static void SetLights(IEnumerable<GeometryInstance3D> meshes, IReadOnlyList<Core.Lights.DirectionalLight> lights)
    {
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < MaxLights; i++)
            {
                var (colour, toward) = i < lights.Count ? (lights[i].Colour.ToGodot(), lights[i].Toward.ToGodot()) : (Vector3.Zero, Vector3.Up);
                mesh.SetInstanceShaderParameter(LightColor[i], colour);
                mesh.SetInstanceShaderParameter(LightDirection[i], toward);
            }
        }
    }

    private static readonly StringName[] LightColor = [.. Enumerable.Range(0, MaxLights).Select(i => new StringName($"light{i}_color"))];
    private static readonly StringName[] LightDirection = [.. Enumerable.Range(0, MaxLights).Select(i => new StringName($"light{i}_direction"))];

    /// <summary>
    /// Gives every mesh under <paramref name="root"/> a scene object's tint, which the original averages with the
    /// scenery's. It's set as a <see cref="Vector4"/>: Godot turns a <see cref="Color"/> given to an instance
    /// uniform linear, which made Oriana's path lights' pale cyan (153, 236, 255) come out as (81, 214, 255), so
    /// their red was a fifth short and they looked bluer than the N64's.
    /// </summary>
    public static void SetObjectTint(Node root, Color onScreen) =>
        SetOnMeshes(root, ObjectTintParameter, new Vector4(onScreen.R, onScreen.G, onScreen.B, 1));

    /// <summary>
    /// Gives every mesh under <paramref name="root"/> its scene's own tint (<c>Scene.SetTint</c>, <c>SceneData.colorFloats</c>), in
    /// on-screen values: the prim colour of every draw, in place of the scenery's tint and a lit draw's white. The aim's cylinder is
    /// red or green so, and the spells' visuals are tinted by the screen's fade.
    /// </summary>
    public static void SetSceneTint(Node root, Color onScreen) =>
        SetOnMeshes(root, SceneTintParameter, new Vector4(onScreen.R, onScreen.G, onScreen.B, 1));

    /// <summary>A body's own ambient light, in on-screen values, in place of half the sky's, or null for the sky's.</summary>
    public static void SetBodyAmbient(IEnumerable<GeometryInstance3D> meshes, Vector3? onScreen)
    {
        var value = onScreen is { } c ? new Vector4(c.X, c.Y, c.Z, 1) : Vector4.Zero;
        foreach (var mesh in meshes)
            mesh.SetInstanceShaderParameter(BodyAmbientInstanceParameter, value);
    }

    /// <summary>Turns a body's fog off or back on (its scene's flag <c>0x100</c>), as a cast's glow does.</summary>
    public static void SetUnfogged(IEnumerable<GeometryInstance3D> meshes, bool unfogged)
    {
        foreach (var mesh in meshes)
            mesh.SetInstanceShaderParameter(UnfoggedParameter, unfogged ? 1f : 0f);
    }

    private static void SetOnMeshes(Node root, StringName parameter, Variant value)
    {
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            foreach (var child in node.GetChildren())
                stack.Push(child);
            if (node is GeometryInstance3D geometry)
                geometry.SetInstanceShaderParameter(parameter, value);
        }
    }

    private static ShaderMaterial Create(JsonElement root, JsonElement m, Func<int, Texture2D> texture)
    {
        var unlit = m.TryGetProperty("extensions", out var extensions) && extensions.TryGetProperty("KHR_materials_unlit", out _);
        var cull = !(m.TryGetProperty("doubleSided", out var doubleSided) && doubleSided.GetBoolean());
        var blend = m.TryGetProperty("alphaMode", out var mode) ? mode.GetString() switch
        {
            "MASK" => Blend.Mask,
            "BLEND" => Blend.Blend,
            _ => Blend.Opaque,
        } : Blend.Opaque;

        var factor = new Color(1, 1, 1);
        Texture2D? albedo = null;
        (Wrap S, Wrap T) wrap = (Wrap.Repeat, Wrap.Repeat);
        var nearest = false;
        if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
        {
            if (pbr.TryGetProperty("baseColorFactor", out var f))
                factor = new Color(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle(), f[3].GetSingle());
            if (pbr.TryGetProperty("baseColorTexture", out var info))
            {
                var t = root.GetProperty("textures")[info.GetProperty("index").GetInt32()];
                albedo = texture(t.GetProperty("source").GetInt32());
                if (t.TryGetProperty("sampler", out var samplerIndex))
                {
                    var sampler = root.GetProperty("samplers")[samplerIndex.GetInt32()];
                    wrap = (ToWrap(sampler, "wrapS"), ToWrap(sampler, "wrapT"));
                    nearest = sampler.TryGetProperty("magFilter", out var mag) && mag.GetInt32() == Nearest;
                }
            }
        }

        // A lit display list with G_TEXTURE_GEN (Borg2 flag 0x1) takes its texture coordinates from its normals.
        var texgen = albedo is not null && !unlit && m.TryGetProperty("extras", out var flagExtras) &&
            flagExtras.TryGetProperty(ExtrasKeys.Borg2Flags, out var borg2Flags) && (borg2Flags.GetUInt32() & EnvironmentMapFlag) != 0;
        var material = new ShaderMaterial { Shader = ShaderFor(unlit, cull, nearest, blend, albedo is not null, texgen) };
        // Back to on-screen values; alpha was never gamma-encoded.
        var onScreen = factor.LinearToSrgb();
        // A Vector4, not a Color: Duplicate() resets a Color set on a vec4 uniform to the default,
        // which turned scrolling textures' copies opaque.
        // The game truncates the alpha to a byte (Scene.SetRenderMode, 0x80097618): 0.1 is 25.
        var alpha = Aidyn.Core.ColorBytes.Single(factor.A * 255f) / 255f;
        material.SetShaderParameter("prim", new Vector4(onScreen.R, onScreen.G, onScreen.B, alpha));
        if (albedo is not null)
        {
            material.SetShaderParameter("albedo_texture", albedo);
            material.SetShaderParameter("wrap", new Vector2I((int)wrap.S, (int)wrap.T));
        }
        if (m.TryGetProperty("alphaCutoff", out var cutoff))
            material.SetShaderParameter("alpha_scissor", cutoff.GetSingle());
        if (m.TryGetProperty("extras", out var extras) && extras.TryGetProperty(ExtrasKeys.Borg1, out var borg1))
            material.SetMeta(Borg1Meta, borg1.GetInt32());
        if (blend == Blend.Mask)
            FadedShaders.Add(material, ShaderFor(unlit, cull, nearest, Blend.Blend, albedo is not null, texgen));
        if (cull)
            MirroredShaders.Add(material, ShaderFor(unlit, cull, nearest, blend, albedo is not null, texgen, mirrored: true));
        return material;

        static Wrap ToWrap(JsonElement sampler, string name) =>
            !sampler.TryGetProperty(name, out var v) ? Wrap.Repeat : v.GetInt32() switch
            {
                ClampToEdge => Wrap.Clamp,
                MirroredRepeat => Wrap.Mirror,
                _ => Wrap.Repeat,
            };
    }

    /// <summary>Borg2's <c>EnvironmentMap</c> flag: the display list's texture coordinates come from its normals.</summary>
    private const uint EnvironmentMapFlag = 0x1;

    /// <summary>
    /// <c>G_TEXTURE_GEN</c>'s texture coordinates (<c>BorgAnimDrawSceneRaw</c>, <c>0x80097a90</c>, and F3DEX2's texgen):
    /// before each such list the game looks from the scene's eye at the node's world position, world up being up
    /// (<c>guLookAtReflect</c>), and each vertex's normal after the model matrix takes (1 + n · right) / 2 across the
    /// texture and (1 + n · up) / 2 down it, so a normal facing up samples the bottom. A cinematic's scene's eye is
    /// its camera; trek's centre chunk's is trek's camera, and the rest's is the world's origin (<see cref="SetEye"/>).
    /// </summary>
    private const string TexGen = """

        	// G_TEXTURE_GEN: the texture is looked up by the way the normal faces, seen from the scene's eye.
        	vec3 world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
        	vec3 look = normalize(eye - MODEL_MATRIX[3].xyz);
        	vec3 look_right = normalize(cross(vec3(0.0, 1.0, 0.0), look));
        	vec3 look_up = cross(look, look_right);
        	UV = (1.0 + vec2(dot(world_normal, look_right), dot(world_normal, look_up))) * 0.5;
        """;

    /// <summary>A shader for a material's fixed state. Each combination is compiled once.</summary>
    private static Shader ShaderFor(bool unlit, bool cull, bool nearest, Blend blend, bool textured, bool texgen, bool mirrored = false)
    {
        var rsp = RspLighting && !unlit;
        var onScreen = OnScreenFramebuffer && (unlit || rsp);
        var key = $"{unlit} {rsp} {onScreen} {cull} {nearest} {blend} {textured} {texgen} {mirrored}";
        if (Shaders.TryGetValue(key, out var shader))
            return shader;

        // Godot turns the culling round for a mirrored instance; a mirrored copy turns it back (see MirroredMeshes).
        var modes = new List<string> { !cull ? "cull_disabled" : mirrored ? "cull_front" : "cull_back" };
        modes.AddRange(unlit || rsp ? ["unshaded"] : ["diffuse_lambert", "specular_disabled"]);
        if (blend == Blend.Blend)
            modes.Add("depth_draw_opaque");

        var texel = textured
            ? """
                vec2 half_texel = 0.5 / vec2(textureSize(albedo_texture, 0));
                	// The RDP centres texel k on coordinate k, where the GPU centres it on k + 0.5, so the coordinates move
                	// half a texel: at a shape's edge the filter blends the texel beside it, not the far side's wrapped round.
                	vec2 at = UV + half_texel;
                	// A scrolling texture's image wraps round within the texture, after the tile's own wrapping.
                	vec2 uv = vec2(wrap_axis(at.x, wrap.x, half_texel.x) - scroll, wrap_axis(at.y, wrap.y, half_texel.y));
                	// The unwrapped coordinates' gradients, so the mip level doesn't jump at a seam.
                	vec4 texel = textureGrad(albedo_texture, uv, dFdx(UV), dFdy(UV));
                """
            : "vec4 texel = vec4(1.0);";
        // The shade at each vertex, in on-screen values: the vertex colour on unlit lists, the RSP's lighting on lit ones.
        var vertexShade = unlit ? "vec4(to_screen(COLOR.rgb), COLOR.a)" : rsp ? "vec4(lit_shade, 1.0)" : "vec4(1.0)";
        var light = rsp
            ? """

                	// The RSP's lighting, per vertex, in on-screen values.
                	vec3 normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
                	vec3 ambient = lighting == 1 ? (body_ambient.a > 0.0 ? body_ambient.rgb : n64_body_ambient) : lighting == 2 ? n64_tint
                		: lighting == 3 ? own_ambient : vec3(1.0);
                	vec3 lights = lighting == 0 ? vec3(0.0)
                		: (lighting == 3 ? vec3(0.0) : n64_sun_color * max(dot(normal, n64_sun_direction), 0.0))
                		+ light0_color * max(dot(normal, light0_direction), 0.0) + light1_color * max(dot(normal, light1_direction), 0.0)
                		+ light2_color * max(dot(normal, light2_direction), 0.0) + light3_color * max(dot(normal, light3_direction), 0.0);
                	vec3 lit_shade = min(ambient + lights, vec3(1.0));
                """
            : "";
        var fog = onScreen
            ? "ALBEDO = mix(color, n64_fog_color, fog);"
            : unlit || rsp
            ? "ALBEDO = to_linear(mix(color, n64_fog_color, fog));"
            : "ALBEDO = to_linear(color);\n\tFOG = vec4(to_linear(n64_fog_color), fog);";
        // ALPHA starts as 1 − the instance's transparency, which fades scene objects, so it's
        // multiplied rather than set, and a cut-out's threshold fades with it to keep its shape.
        var alpha = blend switch
        {
            Blend.Blend => "ALPHA *= alpha;",
            Blend.Mask => "ALPHA_SCISSOR_THRESHOLD = alpha_scissor * ALPHA;\n\tALPHA *= alpha;",
            _ => "",
        };

        shader = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode {{string.Join(", ", modes)}};

                global uniform vec3 n64_tint;
                global uniform vec3 n64_fog_color;
                // Start and end on the 0–1000 scale, then the near and far planes.
                global uniform vec4 n64_fog;
                global uniform vec3 n64_sun_direction;
                global uniform vec3 n64_sun_color;
                global uniform vec3 n64_body_ambient;
                instance uniform vec4 object_tint = vec4(0.0);
                // A scene's own tint (Scene.SetTint), with alpha 1, which replaces the scenery's and a lit draw's white; or 0 for none.
                instance uniform vec4 scene_tint = vec4(0.0);
                // A body's own ambient, with alpha 1, in place of half the sky's (its tint, in combat); or 0 for none.
                instance uniform vec4 body_ambient = vec4(0.0);
                // 1 where the scene's fog is off (a body a cast glows).
                instance uniform float unfogged = 0.0;
                // 0 scenery, 1 a body, 2 a scene object's actor, 3 its own lights.
                instance uniform int lighting = 0;
                instance uniform vec3 own_ambient = vec3(1.0);
                // The scene's eye, which environment-mapped lists look from.
                instance uniform vec3 eye = vec3(0.0);
                // Up to four directional lights besides the sun: a body's or actor's point lights, or a scene's own.
                instance uniform vec3 light0_color = vec3(0.0);
                instance uniform vec3 light0_direction = vec3(0.0, 1.0, 0.0);
                instance uniform vec3 light1_color = vec3(0.0);
                instance uniform vec3 light1_direction = vec3(0.0, 1.0, 0.0);
                instance uniform vec3 light2_color = vec3(0.0);
                instance uniform vec3 light2_direction = vec3(0.0, 1.0, 0.0);
                instance uniform vec3 light3_color = vec3(0.0);
                instance uniform vec3 light3_direction = vec3(0.0, 1.0, 0.0);

                // On-screen colour and alpha: the mesh's tint when untextured, and its alpha.
                uniform vec4 prim = vec4(1.0);
                // Raw texels, which are on-screen values: no source_color.
                uniform sampler2D albedo_texture : {{(nearest ? "filter_nearest_mipmap" : "filter_linear_mipmap")}}, repeat_enable;
                // Per axis: 0 repeat, 1 clamp, 2 mirror.
                uniform ivec2 wrap;
                // How far a scrolling texture's image has moved toward +S, as a share of its width.
                uniform float scroll = 0.0;
                uniform float alpha_scissor = 0.5;

                // The shade and the fog times the vertex's clip-space w, and that w. Dividing them again undoes Godot's
                // perspective-correct interpolation, so they're interpolated straight across the screen as the RDP's are.
                varying vec4 shade_w;
                varying float fog_w;
                varying float clip_w;

                vec3 to_screen(vec3 c) {
                	return mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, c * 12.92, lessThan(c, vec3(0.0031308)));
                }

                vec3 to_linear(vec3 c) {
                	return mix(pow((c + 0.055) / 1.055, vec3(2.4)), c / 12.92, lessThan(c, vec3(0.04045)));
                }

                float wrap_axis(float x, int mode, float half_texel) {
                	if (mode == 1) {
                		return clamp(x, half_texel, 1.0 - half_texel);
                	}
                	if (mode == 2) {
                		float m = mod(x, 2.0);
                		return clamp(m > 1.0 ? 2.0 - m : m, half_texel, 1.0 - half_texel);
                	}
                	return x;
                }

                void vertex() {
                	// The depth buffer's z / w for the original's planes, on the 0–1000 scale.
                	float near = n64_fog.z;
                	float far = n64_fog.w;
                	float depth = max(-(MODELVIEW_MATRIX * vec4(VERTEX, 1.0)).z, 0.0001);
                	float z = (far + near) / (far - near) - 2.0 * far * near / ((far - near) * depth);
                	float fog = far > near && lighting != 3 && unfogged == 0.0 ? clamp((z * 500.0 + 500.0 - n64_fog.x) / (n64_fog.y - n64_fog.x), 0.0, 1.0) : 0.0;{{light}}{{(texgen ? TexGen : "")}}
                	clip_w = max((PROJECTION_MATRIX * (MODELVIEW_MATRIX * vec4(VERTEX, 1.0))).w, 0.0001);
                	shade_w = {{vertexShade}} * clip_w;
                	fog_w = fog * clip_w;
                }

                void fragment() {
                	{{texel}}
                	vec4 shade = shade_w / clip_w;
                	float fog = fog_w / clip_w;
                	vec3 tint = scene_tint.a > 0.0 ? scene_tint.rgb
                		: lighting != 0 ? vec3(1.0) : (object_tint.a > 0.0 ? (n64_tint + object_tint.rgb) * 0.5 : n64_tint);
                	vec3 color = texel.rgb * shade.rgb * prim.rgb * tint;
                	float alpha = {{(textured ? "texel.a" : "shade.a")}} * prim.a;
                	{{fog}}
                	{{alpha}}
                }
                """,
        };
        Shaders[key] = shader;
        return shader;
    }
}
