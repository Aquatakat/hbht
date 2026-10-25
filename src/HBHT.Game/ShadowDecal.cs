using HBHT.Core.Movement;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Draws a body's <see cref="BodyShadow"/> as a decal on the ground.
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
/// </remarks>
public partial class ShadowDecal : Decal
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

    public ShadowDecal(Texture2D texture)
    {
        Name = "Shadow";
        TextureAlbedo = texture;
        UpperFade = LowerFade = EndFade;
        CullMask &= ~(1u << (BodyLayer - 1));
        Visible = false;
    }

    public ShadowDecal() : this(new PlaceholderTexture2D())
    {
    }

    public BodyShadow Shadow { get; } = new();

    /// <summary>Moves the decal to where <see cref="Shadow"/> is. Call after placing it.</summary>
    public void Sync()
    {
        var appearing = !Visible;
        Visible = Shadow.Size > 0;
        if (!Visible)
            return;
        var (s, t) = Shadow.Axes();
        // The decal's X and Z run along the texture's S and T, and it projects down its −Y.
        GlobalTransform = new Transform3D(new Basis(s.ToGodot(), Shadow.Normal.ToGodot(), t.ToGodot()), Shadow.Position.ToGodot());
        if (appearing)
            ResetPhysicsInterpolation();
        var width = Shadow.HalfWidth * 2;
        Size = new Vector3(width, Depth * 2, width);
        // Black, with the texture's intensity times the vertex alpha as its alpha (G_CC_MODULATEIA).
        // The scenery is drawn in on-screen values (N64Material.OnScreenFramebuffer), so it darkens
        // them as the N64's blend does.
        Modulate = new Color(0, 0, 0, Shadow.Alpha / 255f);
    }

    /// <summary>
    /// Places the shadow under a body's <paramref name="sphere"/> with the alpha <see cref="BodyShadow.DrawAlpha"/> gave
    /// and moves the decal to it, hiding it when there is none (the shadow isn't drawn). Call after the body moves.
    /// </summary>
    public void Place(Core.Physics.CollisionWorld world, Core.Physics.CollisionSphere sphere, byte? alpha)
    {
        if (alpha is { } a)
            Shadow.Place(world, sphere.Position, sphere.Radius, a);
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
