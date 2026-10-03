using System.Runtime.CompilerServices;
using System.Text.Json;
using Godot;

namespace Aidyn.Game;

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
/// multiplies and fogs there as the N64 does, and hands Godot the linear result.
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
/// With <see cref="RspLighting"/>, as in the runtime, lit display lists are lit per vertex as the
/// RSP lights them: an ambient plus the sun's colour times the cosine to it, clamped to 1, in
/// on-screen values. Without it, as in the Inspector, Godot's lights stand in.
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
internal static class N64Material
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
    /// <summary>Per instance: how its lit display lists are lit, a <see cref="Lighting"/>.</summary>
    public const string LightingParameter = "lighting";
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
        /// <summary>Untinted, lit by half the sky colour and the sun (<c>some_player_render_sub</c>): the player and other bodies.</summary>
        Body,
        /// <summary>Untinted, lit by the tint and the sun (<c>set_sun_light</c>): scene objects' actors, unless fullbright.</summary>
        SceneObject,
    }

    /// <summary>Whether lit display lists are lit as the RSP does, from the globals, rather than by Godot's lights.</summary>
    public static bool RspLighting { get; set; }

    private enum Wrap { Repeat, Clamp, Mirror }
    private enum Blend { Opaque, Mask, Blend }

    private const int Nearest = 9728, ClampToEdge = 33071, MirroredRepeat = 33648;
    private static readonly Dictionary<string, Shader> Shaders = [];
    /// <summary>Cut-out materials' blended shaders, for drawing them faded.</summary>
    private static readonly ConditionalWeakTable<Material, Shader> FadedShaders = [];
    /// <summary>Cut-out materials' blended copies, made as they're first faded.</summary>
    private static readonly ConditionalWeakTable<Material, ShaderMaterial> FadedCopies = [];
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

    /// <summary>Replaces the imported materials of <paramref name="scene"/>, loaded from <paramref name="glb"/>.</summary>
    public static void Apply(byte[] glb, GltfState state, Node scene)
    {
        DeclareGlobals();
        using var json = GlbFile.Json(glb);
        var root = json.RootElement;
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

    /// <summary>Moves a scrolling texture's image, in its material and the material's faded copy.</summary>
    public static void SetScroll(ShaderMaterial material, float scroll)
    {
        material.SetShaderParameter(ScrollParameter, scroll);
        if (FadedCopies.TryGetValue(material, out var copy))
            copy.SetShaderParameter(ScrollParameter, scroll);
    }

    /// <summary>A copy of a material for a copy of a scene, which fades as the original does.</summary>
    public static ShaderMaterial Copy(ShaderMaterial material)
    {
        var copy = (ShaderMaterial)material.Duplicate();
        if (FadedShaders.TryGetValue(material, out var shader))
            FadedShaders.Add(copy, shader);
        return copy;
    }

    /// <summary>Sets how every mesh under <paramref name="root"/> is lit.</summary>
    public static void SetLighting(Node root, Lighting lighting) => SetOnMeshes(root, LightingParameter, (int)lighting);

    /// <summary>Gives every mesh under <paramref name="root"/> a scene object's tint, which the original averages with the scenery's.</summary>
    public static void SetObjectTint(Node root, Color onScreen) =>
        SetOnMeshes(root, ObjectTintParameter, new Color(onScreen, 1));

    private static void SetOnMeshes(Node root, string parameter, Variant value)
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

        var material = new ShaderMaterial { Shader = ShaderFor(unlit, cull, nearest, blend, albedo is not null) };
        // Back to on-screen values; alpha was never gamma-encoded.
        material.SetShaderParameter("prim", new Color(factor.LinearToSrgb(), factor.A));
        if (albedo is not null)
        {
            material.SetShaderParameter("albedo_texture", albedo);
            material.SetShaderParameter("wrap", new Vector2I((int)wrap.S, (int)wrap.T));
        }
        if (m.TryGetProperty("alphaCutoff", out var cutoff))
            material.SetShaderParameter("alpha_scissor", cutoff.GetSingle());
        if (m.TryGetProperty("extras", out var extras) && extras.TryGetProperty("borg1", out var borg1))
            material.SetMeta(Borg1Meta, borg1.GetInt32());
        if (blend == Blend.Mask)
            FadedShaders.Add(material, ShaderFor(unlit, cull, nearest, Blend.Blend, albedo is not null));
        return material;

        static Wrap ToWrap(JsonElement sampler, string name) =>
            !sampler.TryGetProperty(name, out var v) ? Wrap.Repeat : v.GetInt32() switch
            {
                ClampToEdge => Wrap.Clamp,
                MirroredRepeat => Wrap.Mirror,
                _ => Wrap.Repeat,
            };
    }

    /// <summary>A shader for a material's fixed state. Each combination is compiled once.</summary>
    private static Shader ShaderFor(bool unlit, bool cull, bool nearest, Blend blend, bool textured)
    {
        var rsp = RspLighting && !unlit;
        var key = $"{unlit} {rsp} {cull} {nearest} {blend} {textured}";
        if (Shaders.TryGetValue(key, out var shader))
            return shader;

        var modes = new List<string> { cull ? "cull_back" : "cull_disabled" };
        modes.AddRange(unlit || rsp ? ["unshaded"] : ["diffuse_lambert", "specular_disabled"]);
        if (blend == Blend.Blend)
            modes.Add("depth_draw_opaque");

        var texel = textured
            ? """
                vec2 half_texel = 0.5 / vec2(textureSize(albedo_texture, 0));
                	// A scrolling texture's image wraps round within the texture, after the tile's own wrapping.
                	vec2 uv = vec2(wrap_axis(UV.x, wrap.x, half_texel.x) - scroll, wrap_axis(UV.y, wrap.y, half_texel.y));
                	// The unwrapped coordinates' gradients, so the mip level doesn't jump at a seam.
                	vec4 texel = textureGrad(albedo_texture, uv, dFdx(UV), dFdy(UV));
                """
            : "vec4 texel = vec4(1.0);";
        var shade = unlit ? "vec4(to_screen(COLOR.rgb), COLOR.a)" : rsp ? "vec4(lit_shade, 1.0)" : "vec4(1.0)";
        var light = rsp
            ? """

                	// The RSP's lighting, per vertex, in on-screen values.
                	vec3 normal = normalize(MODEL_NORMAL_MATRIX * NORMAL);
                	vec3 ambient = lighting == 1 ? n64_body_ambient : lighting == 2 ? n64_tint : vec3(1.0);
                	vec3 sun = lighting == 0 ? vec3(0.0) : n64_sun_color * max(dot(normal, n64_sun_direction), 0.0);
                	lit_shade = min(ambient + sun, vec3(1.0));
                """
            : "";
        var fog = unlit || rsp
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
                // 0 scenery, 1 a body, 2 a scene object's actor.
                instance uniform int lighting = 0;

                // On-screen colour and alpha: the mesh's tint when untextured, and its alpha.
                uniform vec4 prim = vec4(1.0);
                // Raw texels, which are on-screen values: no source_color.
                uniform sampler2D albedo_texture : {{(nearest ? "filter_nearest_mipmap" : "filter_linear_mipmap")}}, repeat_enable;
                // Per axis: 0 repeat, 1 clamp, 2 mirror.
                uniform ivec2 wrap;
                // How far a scrolling texture's image has moved toward +S, as a share of its width.
                uniform float scroll = 0.0;
                uniform float alpha_scissor = 0.5;

                varying float fog;
                varying vec3 lit_shade;

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
                	fog = far > near ? clamp((z * 500.0 + 500.0 - n64_fog.x) / (n64_fog.y - n64_fog.x), 0.0, 1.0) : 0.0;{{light}}
                }

                void fragment() {
                	{{texel}}
                	vec4 shade = {{shade}};
                	vec3 tint = lighting != 0 ? vec3(1.0) : (object_tint.a > 0.0 ? (n64_tint + object_tint.rgb) * 0.5 : n64_tint);
                	vec3 color = texel.rgb * shade.rgb * prim.rgb * tint;
                	float alpha = texel.a * shade.a * prim.a;
                	{{fog}}
                	{{alpha}}
                }
                """,
        };
        Shaders[key] = shader;
        return shader;
    }
}
