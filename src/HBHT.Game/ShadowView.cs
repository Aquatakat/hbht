using HBHT.Core.Movement;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws a body's <see cref="BodyShadow"/> on the ground: as a decal, or under Compatibility, which draws no decals, as the
/// original's flat square.
/// </summary>
/// <remarks>
/// The original draws a flat square without testing depth, before the bodies and the nearby scene
/// objects, which draw over it. So it never sinks into ground that's uneven or that isn't quite
/// where the collision is. A decal of the same square, projected onto the ground, looks the same
/// without that trouble. It leaves out <see cref="BodyLayer"/>, so bodies stay on top.
/// <para>
/// Where the ground bends, the flat square would carry on through it. The decal instead fades out
/// toward the top and bottom of its box, so no edge of the box shows, and a bank or wall beside
/// the body takes only a little. Godot can also fade a decal off surfaces angled away from it,
/// but that hid the shadow even on level ground: unlit meshes carry no normals, and the ones
/// Godot makes for them don't face up.
/// </para>
/// <para>
/// Under Compatibility (<see cref="Renderers.Compatibility"/>) it's the square itself, lifted <see cref="Lift"/> off the ground and
/// tested against the depth, so it can sink where the ground rises within it, as the decal can't. Drawn without the depth test, as
/// the original's, it would show through the scenery in front of it, which the original drew before it and Godot draws after.
/// </para>
/// </remarks>
public partial class ShadowView : Node3D
{
    /// <summary>The visual layer bodies are drawn on, which shadows leave out.</summary>
    public const int BodyLayer = 2;

    /// <summary>How far above and below the collision's ground it projects, to reach the visible ground.</summary>
    private const float Depth = 0.75f;

    /// <summary>
    /// How it fades toward the top and bottom of its box, as an exponent: 95% left a tenth of the
    /// way out, 71% halfway.
    /// </summary>
    private const float EndFade = 0.5f;

    /// <summary>How far the square is lifted off the ground under Compatibility, so the ground it lies on doesn't hide it.</summary>
    private const float Lift = 0.03f;

    private static readonly Shader SquareShader = new()
    {
        Code = """
            shader_type spatial;
            render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, fog_disabled;

            uniform sampler2D shadow : filter_linear, repeat_disable;
            uniform float alpha = 1.0;

            void fragment() {
            	// Black, with the texture's intensity times the vertex alpha as its alpha (G_CC_MODULATEIA).
            	ALBEDO = vec3(0.0);
            	ALPHA = texture(shadow, UV).a * alpha;
            }
            """,
    };

    private static readonly StringName AlphaParameter = "alpha";

    private static readonly PlaneMesh Square = new() { Size = Vector2.One };

    private readonly Decal? _decal;
    private readonly MeshInstance3D? _square;
    private readonly ShaderMaterial? _material;
    private float _alpha = -1;

    public ShadowView(Texture2D texture)
    {
        Name = "Shadow";
        if (Renderers.Compatibility)
        {
            _material = new ShaderMaterial { Shader = SquareShader };
            _material.SetShaderParameter("shadow", texture);
            _square = new MeshInstance3D
            {
                Name = "Square",
                Mesh = Square,
                MaterialOverride = _material,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_square);
        }
        else
        {
            _decal = new Decal { Name = "Decal", TextureAlbedo = texture, UpperFade = EndFade, LowerFade = EndFade };
            _decal.CullMask &= ~(1u << (BodyLayer - 1));
            AddChild(_decal);
        }
        Visible = false;
    }

    public ShadowView() : this(new PlaceholderTexture2D())
    {
    }

    public BodyShadow Shadow { get; } = new();

    /// <summary>Moves the shadow to where <see cref="Shadow"/> is. Call after placing it.</summary>
    public void Sync()
    {
        var appearing = !Visible;
        Visible = Shadow.Size > 0;
        if (!Visible)
            return;
        var (s, t) = Shadow.Axes();
        // Its X and Z run along the texture's S and T, and the decal projects down its −Y.
        GlobalTransform = new Transform3D(new Basis(s.ToGodot(), Shadow.Normal.ToGodot(), t.ToGodot()), Shadow.Position.ToGodot());
        if (appearing)
            ResetPhysicsInterpolation();
        var width = Shadow.HalfWidth * 2;
        // The scenery is drawn in on-screen values (N64Material.OnScreenFramebuffer), so it darkens
        // them as the N64's blend does.
        var alpha = Shadow.Alpha / 255f;
        if (_decal is not null)
        {
            _decal.Size = new Vector3(width, Depth * 2, width);
            // Black, with the texture's intensity times the vertex alpha as its alpha (G_CC_MODULATEIA).
            _decal.Modulate = new Color(0, 0, 0, alpha);
        }
        else if (_square is not null)
        {
            _square.Transform = new Transform3D(Basis.FromScale(new Vector3(width, 1, width)), new Vector3(0, Lift, 0));
            if (alpha != _alpha)
            {
                _alpha = alpha;
                _material!.SetShaderParameter(AlphaParameter, alpha);
            }
        }
    }

    /// <summary>
    /// Places the shadow under a body's <paramref name="sphere"/> with the alpha <see cref="BodyShadow.DrawAlpha"/> gave
    /// and moves it there, hiding it when there is none (the shadow isn't drawn). Call after the body moves.
    /// </summary>
    public void Place(Core.Physics.CollisionWorld world, Core.Physics.CollisionSphere sphere, byte? alpha) =>
        Place(world, sphere.Position, sphere.Radius, alpha);

    /// <summary>Places it under a sphere at <paramref name="centre"/> of <paramref name="radius"/>, with <paramref name="alpha"/>, or hides it for none.</summary>
    public void Place(Core.Physics.CollisionWorld world, System.Numerics.Vector3 centre, float radius, byte? alpha)
    {
        if (alpha is { } a)
            Shadow.Place(world, centre, radius, a);
        Sync();
        Visible &= alpha is not null;
    }

    /// <summary>Puts a body's meshes on <see cref="BodyLayer"/>, so shadows leave them out.</summary>
    public static void MarkBody(Node node)
    {
        if (node is VisualInstance3D visual)
            visual.Layers = 1u << (BodyLayer - 1);
        foreach (var child in node.GetChildren())
            MarkBody(child);
    }
}
