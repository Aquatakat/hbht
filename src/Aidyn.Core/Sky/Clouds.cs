using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Sky;

/// <summary>
/// The clouds (<c>Clouds::Render</c>, <c>Clouds::CalcWind</c>): sprites on a plane around the
/// camera, drawn over the sun, moon and stars and under the scenery, in an outdoor sky.
/// </summary>
/// <remarks>
/// A cloud is a point within 750 of the camera, which the wind blows along; one that goes past 750
/// comes back in on the other side. It's drawn in the way from the point to the camera, turned by
/// the camera's heading, and the nearer it is, the higher above the horizon line, the larger and the
/// more opaque: at a nearness of 1 − distance / 750, 240 × nearness pixels up, at nearness + 0.2
/// times its size, and with alpha × √nearness. The horizon line is where a level look would put it,
/// moved as the camera looks up or down, 300 pixels to the unit of the look's height.
/// <para>
/// The game measures the way to the camera with the plane's X and Z swapped against the sky
/// objects' azimuths, which only mirrors the layout. The alpha fades in and out as the cloud cover
/// (<c>gGlobals.cloudsFloat</c>) passes each cloud's <see cref="Cloud.Cover"/>, but nothing changes
/// the cover from 1, so all show and that isn't ported.
/// </para>
/// </remarks>
public sealed class Clouds
{
    /// <summary>How far from the camera clouds go.</summary>
    public const float Reach = 750;

    /// <summary>
    /// A new game's wind (<c>World::init</c>): (1, −0.1, 1) normalised, at 15/648 a tick. It changes
    /// with the weather (<c>World::ChangeWind</c>), which isn't ported, so it's constant for now.
    /// </summary>
    public static readonly Vector3 NewGameWind = Vector3.Normalize(new Vector3(1, -0.1f, 1)) * (15f / 648);

    /// <summary>How far the wind blows clouds in a frame, in its units: they move 4 × the wind each frame.</summary>
    private const float WindScale = 4;

    private readonly SkyRecord _sky;
    private readonly Vector2[] _at;

    public Clouds(SkyRecord sky)
    {
        _sky = sky;
        _at = [.. sky.Clouds.Select(c => new Vector2(c.X, c.Z))];
    }

    public Vector3 Wind { get; set; } = NewGameWind;

    /// <summary>How the clouds are placed across the screen: the original's way, or projected.</summary>
    public SkyPlacement Placement { get; set; }

    /// <summary>Where each cloud is on its plane, around the camera, as (X, Z).</summary>
    public IReadOnlyList<Vector2> Positions => _at;

    /// <summary>
    /// Blows the clouds along for <paramref name="ticks"/>. The original moves them once a rendered
    /// frame, whatever its length; this moves them at 30 fps's pace (<see cref="Cadence"/>).
    /// </summary>
    public void Step(int ticks = 1)
    {
        var blow = new Vector2(Wind.X, Wind.Z) * Cadence.PerTick(WindScale) * ticks;
        for (var i = 0; i < _at.Length; i++)
        {
            _at[i] += blow;
            if (_at[i].Length() > Reach)
                _at[i] = blow - _at[i];
        }
    }

    /// <summary>Adds this frame's cloud sprites to <paramref name="sprites"/>, in the order they're drawn.</summary>
    /// <param name="look">The way the camera looks, normalised.</param>
    /// <param name="color">The sky's cloud colour (<see cref="SkyColors.Cloud"/>).</param>
    /// <param name="fogTime">How thick the weather's fog is: the clouds are gone from two thirds.</param>
    public void Draw(List<SkySprite> sprites, Vector3 look, Vector3 color, ImageSize size, float fogTime = 0)
    {
        var across = MathF.Sqrt(look.X * look.X + look.Z * look.Z);
        if (across == 0)
            return;
        var (lx, lz) = (look.X / across, look.Z / across);
        var horizon = look.Y * SkyScreen.Spread + SkyScreen.Middle.Y;
        for (var i = 0; i < _at.Length; i++)
        {
            var distance = _at[i].Length();
            if (distance == 0)
                continue;
            var toCamera = -_at[i] / distance;
            if (toCamera.X * lz + toCamera.Y * lx <= 0)
                continue;
            var near = 1 - distance / Reach;
            float a = _sky.Clouds[i].Alpha;
            var fog = a * fogTime * 1.5f;
            var faded = fog <= a ? MathF.Max(fog, 0) : a;
            var alpha = (byte)((a - faded) * MathF.Sqrt(near));

            var image = _sky.CloudImages[_sky.Clouds[i].Image];
            var extent = size(image);
            var x = SkyScreen.Across(toCamera.X * lx - toCamera.Y * lz, Placement);
            var at = new Vector2(x - extent.X * near * 0.5f, horizon - near * SkyScreen.Height - extent.Y * near * 0.5f);
            sprites.Add(new SkySprite(image, at, near + 0.2f, color, alpha / 255f));
        }
    }
}
