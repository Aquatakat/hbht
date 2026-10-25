using Godot;

namespace HBHT.Game;

/// <summary>
/// Lets the 3D image hold on-screen values: Godot's output encodes it for the screen as if it were
/// linear light, and this colour correction decodes it again.
/// </summary>
/// <remarks>
/// The N64 blends the values it puts on screen; Godot blends in linear light, where 10% of a bright
/// surface over a dark one comes out far brighter (see docs/systems/rendering.md). With
/// <see cref="N64Material.OnScreenFramebuffer"/>, the N64 material writes on-screen values, so
/// Godot's blender mixes them as the N64's does. Godot applies an environment's colour correction
/// after encoding, so a correction that decodes sRGB hands the screen the values as they were.
/// </remarks>
internal static class OnScreenOutput
{
    private const int Entries = 4096;

    private static readonly Shader EncodeShader = new()
    {
        Code = """
            shader_type canvas_item;

            uniform sampler2D screen : hint_screen_texture, filter_nearest;

            void fragment() {
            	vec3 c = texture(screen, SCREEN_UV).rgb;
            	COLOR = vec4(mix(1.055 * pow(c, vec3(1.0 / 2.4)) - 0.055, c * 12.92, lessThan(c, vec3(0.0031308))), 1.0);
            }
            """,
    };

    /// <summary>Sets <paramref name="environment"/> to show on-screen values as they are.</summary>
    public static void Apply(Godot.Environment environment)
    {
        var image = Image.CreateEmpty(Entries, 1, false, Image.Format.Rgbf);
        for (var i = 0; i < Entries; i++)
        {
            var v = (float)i / (Entries - 1);
            var linear = new Color(v, v, v).SrgbToLinear();
            image.SetPixel(i, 0, linear);
        }
        environment.AdjustmentEnabled = true;
        environment.AdjustmentColorCorrection = ImageTexture.CreateFromImage(image);
    }

    /// <summary>
    /// A rectangle that goes over a canvas the environment shows behind the scene, last.
    /// </summary>
    /// <remarks>
    /// Godot takes a background canvas into the 3D image as linear light, decoding its on-screen
    /// values, and the colour correction would decode them again. This encodes the canvas first,
    /// so it reaches the 3D image as the on-screen values it drew. The canvas is 8 bits a channel,
    /// so near white, where encoding squeezes the values together, neighbouring shades can merge.
    /// </remarks>
    public static Control BackgroundEncoder()
    {
        var rect = new ColorRect { Name = "Encode", Material = new ShaderMaterial { Shader = EncodeShader }, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        return rect;
    }
}
