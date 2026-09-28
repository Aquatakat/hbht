using Aidyn.Core.Movement;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Draws a body's <see cref="BodyShadow"/> as a decal on the ground.
/// </summary>
/// <remarks>
/// The original draws a flat square without testing depth, before the bodies and the nearby scene
/// objects, which draw over it. So it never sinks into ground that's uneven or that isn't quite
/// where the collision is. A decal of the same square, projected onto the ground, looks the same
/// without that trouble. It leaves out <see cref="BodyLayer"/>, so bodies stay on top.
/// </remarks>
public partial class ShadowDecal : Decal
{
    /// <summary>The visual layer bodies are drawn on, which shadows leave out.</summary>
    public const int BodyLayer = 2;

    /// <summary>How far above and below the collision's ground it projects, to reach the visible ground.</summary>
    private const float Depth = 0.5f;

    public ShadowDecal(Texture2D texture)
    {
        Name = "Shadow";
        TextureAlbedo = texture;
        UpperFade = LowerFade = 0;
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
        // The N64 blends what's on screen, where Godot blends in linear light, in which the same
        // alpha darkens the screen about half as much. Darkening by 1 − a on screen is darkening
        // by (1 − a)^2.2 in linear light. That's exact where the texture is opaque, and a shade
        // light toward its edges.
        Modulate = new Color(0, 0, 0, 1 - Mathf.Pow(1 - Shadow.Alpha / 255f, 2.2f));
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
