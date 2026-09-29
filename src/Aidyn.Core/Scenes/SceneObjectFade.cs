using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Scenes;

/// <summary>
/// Where the original draws a scene object from this frame: the player, and the camera's place and
/// look on the ground (<c>RenderVoxelScenes</c>'s <c>pos</c>, <c>gCamera.pos</c> and <c>gCamera.aim</c>).
/// </summary>
/// <param name="Focus">What distances are measured from: the player's centre, or the camera's aim when there's no player.</param>
/// <param name="Camera">The camera's X and Z.</param>
/// <param name="Look">The way it looks in X and Z, of length 1, or zero when it looks straight up or down.</param>
/// <param name="AimDistance">How far along <paramref name="Look"/> its aim is, the player; 0 fades nothing for coming between them.</param>
/// <param name="Cone">How wide the view is taken to be.</param>
public readonly record struct SceneView(Vector3 Focus, Vector2 Camera, Vector2 Look, float AimDistance, ViewCone Cone);

/// <summary>
/// How far either side of the camera's look, on the ground, a scene object starts to fade and has
/// gone, in radians.
/// </summary>
public readonly record struct ViewCone(float FadeFrom, float Gone)
{
    /// <summary>The original's: from 37.5° to 42.5°, just outside a 4:3 screen at the follow camera's pitch.</summary>
    public static readonly ViewCone Original = new(0.6544875f, 0.7417525f);

    /// <summary>
    /// The original's cone for a wider screen: each angle's tangent grows with the width, as the
    /// screen's edge does, so the fade stays as far outside it. 4:3 and narrower keep the original's.
    /// </summary>
    public static ViewCone For(float aspect)
    {
        var widen = aspect / (4f / 3);
        return widen <= 1 ? Original : new ViewCone(
            MathF.Atan(MathF.Tan(Original.FadeFrom) * widen),
            MathF.Atan(MathF.Tan(Original.Gone) * widen));
    }
}

/// <summary>
/// How far scene objects and their levels of detail are drawn, where the original draws them as it
/// does and further isn't a choice yet. <see cref="Original"/> is the original's.
/// </summary>
/// <param name="Scale">Multiplies each object's radius and each level's distance.</param>
/// <param name="NearestDetail">Draws the nearest level of detail out to where the farthest ends, and no other.</param>
public sealed record DrawDistance(float Scale = 1, bool NearestDetail = false)
{
    public static readonly DrawDistance Original = new();
}

/// <summary>
/// Which of a scene object's levels of detail the original draws, and how faded
/// (<c>RenderVoxelScenes</c>, checked against the debug ROM's machine code). Everything is measured
/// from the player, not the camera.
/// </summary>
/// <remarks>
/// <para>
/// An object isn't drawn from its radius out. Within it, it's faded by where the camera looks
/// (<see cref="ViewAlpha"/>), and each level of detail by its distance (<see cref="LevelAlpha"/>);
/// a level is drawn at the lesser. A level with alpha under 255 is drawn after the rest, blended
/// and without writing depth.
/// </para>
/// </remarks>
public static class SceneObjectFade
{
    /// <summary>How far past the player, as a share of the way to the camera, an object between them fades.</summary>
    public const float BetweenWidth = 0.35f;

    /// <summary>The alpha an object between the camera and the player fades to, straight between them.</summary>
    public const byte BetweenAlpha = 0x60;

    /// <summary>Each level of detail's alpha, 0 where it isn't drawn.</summary>
    /// <param name="o">The object.</param>
    /// <param name="origin">Its chunk's corner.</param>
    /// <param name="alphas">One for each of the object's <see cref="SceneObject.Lods"/>.</param>
    public static void Alphas(SceneObject o, Vector3 origin, in SceneView view, Span<byte> alphas, DrawDistance? range = null)
    {
        range ??= DrawDistance.Original;
        alphas.Clear();
        var position = origin + o.Position;
        var distance = Vector3.Distance(view.Focus, position);
        var radius = o.Radius * range.Scale;
        if (distance >= radius)
            return;
        var shown = ViewAlpha(new Vector2(position.X, position.Z), distance, radius, o.Flags, view);
        if (shown == 0)
            return;

        var levels = Math.Min(Math.Min(o.Lods, o.Models.Count), alphas.Length);
        if (range.NearestDetail && levels > 0)
        {
            alphas[0] = Math.Min(shown, LevelAlpha(distance, 0, o.Models[levels - 1].DrawDistance * range.Scale));
            return;
        }
        for (var i = 0; i < levels; i++)
        {
            var near = i == 0 ? 0 : o.Models[i - 1].DrawDistance * range.Scale;
            alphas[i] = Math.Min(shown, LevelAlpha(distance, near, o.Models[i].DrawDistance * range.Scale));
        }
    }

    /// <summary>
    /// How much of an object shows for where the camera looks, before its levels of detail.
    /// </summary>
    /// <remarks>
    /// On the ground, an object from <see cref="ViewCone.FadeFrom"/> off the camera's look fades out
    /// by <see cref="ViewCone.Gone"/>, linearly in the angle's cosine. One that's
    /// <see cref="SceneObjectFlags.DrawWhenNear"/> and within half its radius doesn't. One that isn't
    /// <see cref="SceneObjectFlags.Solid"/> and is nearer the camera than the player, in a cone from
    /// the player toward the camera <see cref="BetweenWidth"/> wide, is drawn from
    /// <see cref="BetweenAlpha"/> in the middle to 255 at the edge instead, whatever the look gave.
    /// </remarks>
    /// <param name="position">The object's X and Z.</param>
    /// <param name="distance">Its distance from <see cref="SceneView.Focus"/>.</param>
    /// <param name="radius">Its radius, as far as it's drawn.</param>
    public static byte ViewAlpha(Vector2 position, float distance, float radius, SceneObjectFlags flags, in SceneView view)
    {
        byte alpha = 0xFF;
        if ((!flags.HasFlag(SceneObjectFlags.DrawWhenNear) || radius * 0.5f < distance) && view.Look != Vector2.Zero)
        {
            var toward = position - view.Camera;
            var cos = toward == Vector2.Zero ? 1 : Vector2.Dot(view.Look, Vector2.Normalize(toward));
            var fadeFrom = MathF.Cos(view.Cone.FadeFrom);
            var gone = MathF.Cos(view.Cone.Gone);
            if (cos <= fadeFrom)
            {
                if (cos <= gone)
                    return 0;
                alpha = Byte((cos - gone) / (fadeFrom - gone) * 255);
            }
        }
        if (!flags.HasFlag(SceneObjectFlags.Solid) && Between(position, view) is { } between)
            alpha = between;
        return alpha;
    }

    /// <summary>
    /// A level of detail's alpha at a distance, 0 where it isn't drawn: from 2 short of the previous
    /// level's distance, fading in over a unit, to its own distance, fading out over the last unit
    /// (<c>RenderVoxelScenes</c>). So as the player walks away, the next level fades in before this
    /// one fades out.
    /// </summary>
    /// <param name="near">The previous level's distance, 0 for the first.</param>
    /// <param name="far">This level's distance.</param>
    public static byte LevelAlpha(float distance, float near, float far)
    {
        if (distance < near - 2 || far < distance)
            return 0;
        byte alpha = 0xFF;
        if (far - 1 < distance)
            alpha = Byte((far - distance) * 255);
        if (distance < near - 1)
            alpha = Byte((1 - (near - 1 - distance)) * 255);
        return alpha;
    }

    /// <summary>
    /// The alpha of an object between the camera and the player (<c>get_scene_obj_proximity</c>), or
    /// null if it isn't between them. It's measured across from the point on the way to the player
    /// that's as far from the camera as the object is.
    /// </summary>
    private static byte? Between(Vector2 position, in SceneView view)
    {
        var along = Vector2.Distance(position, view.Camera);
        if (view.AimDistance <= 0 || along > view.AimDistance)
            return null;
        var point = view.Camera + view.Look * along;
        var across = Vector2.Distance(point, position);
        var width = (view.AimDistance - along) * BetweenWidth;
        if (!(across < width))
            return null;
        return Byte(across / width * 159 + BetweenAlpha);
    }

    /// <summary>A float truncated to a byte, as the original's conversions do.</summary>
    private static byte Byte(float value) => (byte)(int)value;
}
