using System.Text.Json;
using Godot;

namespace Aidyn.Inspector;

/// <summary>
/// Restores glTF texture wrap modes that Godot's importer loses. A <see cref="BaseMaterial3D"/> has
/// one repeat switch for both axes and no mirroring, but Borg2 materials clamp, repeat or mirror S
/// and T separately (faces are often half a texture, mirrored). Materials whose sampler isn't plain
/// repeat are swapped for a shader that wraps each axis itself and otherwise matches the imported
/// material.
/// </summary>
internal static class WrapMaterials
{
    private enum Wrap { Repeat, Clamp, Mirror }

    private const int ClampToEdge = 33071, MirroredRepeat = 33648;
    private static readonly Dictionary<string, Shader> Shaders = [];

    /// <summary>Replaces the imported materials of <paramref name="scene"/> that need per-axis wrapping. Returns how many.</summary>
    public static int Apply(byte[] glb, GltfState state, Node scene)
    {
        var wraps = Read(glb);
        if (wraps.Count == 0)
            return 0;

        var imported = state.GetMaterials();
        var replacements = new Dictionary<Material, Material>();
        foreach (var (index, (s, t)) in wraps)
        {
            if (index < imported.Count && imported[index] is BaseMaterial3D material)
                replacements[material] = Create(material, s, t);
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
        return replacements.Count;
    }

    /// <summary>The glTF materials whose base colour texture doesn't repeat on both axes, by index.</summary>
    private static Dictionary<int, (Wrap S, Wrap T)> Read(byte[] glb)
    {
        var jsonLength = BitConverter.ToInt32(glb, 12);
        using var json = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        var root = json.RootElement;
        var result = new Dictionary<int, (Wrap, Wrap)>();
        if (!root.TryGetProperty("materials", out var materials) || !root.TryGetProperty("samplers", out var samplers))
            return result;

        var textures = root.GetProperty("textures");
        var i = 0;
        foreach (var material in materials.EnumerateArray())
        {
            if (material.TryGetProperty("pbrMetallicRoughness", out var pbr) &&
                pbr.TryGetProperty("baseColorTexture", out var info) &&
                textures[info.GetProperty("index").GetInt32()].TryGetProperty("sampler", out var samplerIndex))
            {
                var sampler = samplers[samplerIndex.GetInt32()];
                var s = ToWrap(sampler, "wrapS");
                var t = ToWrap(sampler, "wrapT");
                if (s != Wrap.Repeat || t != Wrap.Repeat)
                    result[i] = (s, t);
            }
            i++;
        }
        return result;

        static Wrap ToWrap(JsonElement sampler, string name) =>
            !sampler.TryGetProperty(name, out var v) ? Wrap.Repeat : v.GetInt32() switch
            {
                ClampToEdge => Wrap.Clamp,
                MirroredRepeat => Wrap.Mirror,
                _ => Wrap.Repeat,
            };
    }

    private static ShaderMaterial Create(BaseMaterial3D m, Wrap s, Wrap t)
    {
        var material = new ShaderMaterial { Shader = ShaderFor(m), RenderPriority = m.RenderPriority };
        material.SetShaderParameter("albedo", m.AlbedoColor);
        material.SetShaderParameter("albedo_texture", m.AlbedoTexture);
        material.SetShaderParameter("wrap", new Vector2I((int)s, (int)t));
        material.SetShaderParameter("alpha_scissor", m.AlphaScissorThreshold);
        return material;
    }

    /// <summary>
    /// A shader for the imported material's fixed state: lighting, culling, transparency, filtering
    /// and vertex colour. Each combination is compiled once.
    /// </summary>
    private static Shader ShaderFor(BaseMaterial3D m)
    {
        var unshaded = m.ShadingMode == BaseMaterial3D.ShadingModeEnum.Unshaded;
        var cull = m.CullMode == BaseMaterial3D.CullModeEnum.Disabled ? "cull_disabled" : "cull_back";
        var nearest = m.TextureFilter is BaseMaterial3D.TextureFilterEnum.Nearest or BaseMaterial3D.TextureFilterEnum.NearestWithMipmaps;
        var transparency = m.Transparency;
        var vertexColor = m.VertexColorUseAsAlbedo;
        var srgbColor = m.VertexColorIsSrgb;

        var key = $"{unshaded} {cull} {nearest} {transparency} {vertexColor} {srgbColor}";
        if (Shaders.TryGetValue(key, out var shader))
            return shader;

        var modes = new List<string> { cull, "diffuse_burley", "specular_schlick_ggx" };
        if (unshaded)
            modes.Add("unshaded");
        if (transparency == BaseMaterial3D.TransparencyEnum.Alpha)
            modes.Add("depth_draw_opaque");

        var filter = nearest ? "filter_nearest_mipmap" : "filter_linear_mipmap";
        var alpha = transparency switch
        {
            BaseMaterial3D.TransparencyEnum.Alpha => "ALPHA = color.a;",
            BaseMaterial3D.TransparencyEnum.AlphaScissor => "ALPHA = color.a;\n\tALPHA_SCISSOR_THRESHOLD = alpha_scissor;",
            _ => "",
        };
        // As StandardMaterial3D does, when the vertex colours are sRGB.
        var toLinear = srgbColor
            ? "COLOR.rgb = mix(pow((COLOR.rgb + vec3(0.055)) * (1.0 / 1.055), vec3(2.4)), COLOR.rgb / 12.92, lessThan(COLOR.rgb, vec3(0.04045)));"
            : "";
        var shade = vertexColor ? "color *= COLOR;" : "";

        shader = new Shader
        {
            Code = $$"""
                shader_type spatial;
                render_mode {{string.Join(", ", modes)}};

                uniform vec4 albedo : source_color = vec4(1.0);
                uniform sampler2D albedo_texture : source_color, {{filter}}, repeat_enable;
                // Per axis: 0 repeat, 1 clamp, 2 mirror.
                uniform ivec2 wrap;
                uniform float alpha_scissor = 0.5;

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
                	{{toLinear}}
                }

                void fragment() {
                	vec2 half_texel = 0.5 / vec2(textureSize(albedo_texture, 0));
                	vec2 uv = vec2(wrap_axis(UV.x, wrap.x, half_texel.x), wrap_axis(UV.y, wrap.y, half_texel.y));
                	// The unwrapped coordinates' gradients, so the mip level doesn't jump at a seam.
                	vec4 color = textureGrad(albedo_texture, uv, dFdx(UV), dFdy(UV)) * albedo;
                	{{shade}}
                	ALBEDO = color.rgb;
                	ROUGHNESS = 1.0;
                	{{alpha}}
                }
                """,
        };
        Shaders[key] = shader;
        return shader;
    }
}
