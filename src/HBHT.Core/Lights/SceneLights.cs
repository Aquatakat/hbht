using System.Numerics;
using HBHT.Plugins.Records;

namespace HBHT.Core.Lights;

/// <summary>A directional light as the RSP gets it: a colour in on-screen values, 0 to 1, and the way to it in the world.</summary>
public readonly record struct DirectionalLight(Vector3 Colour, Vector3 Toward);

/// <summary>
/// The directional lights a body or actor is drawn with, beside its ambient, as the original's scene keeps them for
/// one draw (<c>SceneLights.Add</c>, 0x8009e838 in 1.0).
/// </summary>
/// <remarks>
/// A scene has room for 7, which <c>SceneSetMaxDynamicDirLights</c> (0x8009e7d0) lowers to 4 for bodies
/// (<c>RenderPlayers</c>) and 2 for actors a map places (<c>RenderVoxelScenes</c>). Once they're full, a new light
/// takes the place of the dimmest after the sun and moon, by r² + g² + b² (<c>Scene.LightBrightness</c>, 0x8009e800),
/// if that's dimmer than it, and is dropped otherwise. The draw then hands the RSP the lights and the ambient, and
/// empties the list for the next.
/// </remarks>
public sealed class SceneLights(int max)
{
    /// <summary>A body's room: the sun, the moon and the rest share 4.</summary>
    public const int BodyMax = 4;

    /// <summary>An actor's that a map places: the sun and the rest share 2.</summary>
    public const int ActorMax = 2;

    private readonly List<(Rgba Colour, Vector3 Toward)> _lights = [];

    public int Max { get; } = max;

    /// <summary>The lights added first, the sun and the moon, which a later light never takes the place of.</summary>
    public int Kept { get; set; }

    public int Count => _lights.Count;

    /// <summary>Adds a light, or takes the place of a dimmer one, or is dropped.</summary>
    public void Add(Rgba colour, Vector3 toward)
    {
        if (_lights.Count < Max)
        {
            _lights.Add((colour, toward));
            return;
        }
        // The dimmest of the new one and those that can be replaced, the earliest on a tie with an old one.
        var dimmest = -1;
        var least = Brightness(colour);
        for (var i = Kept; i < _lights.Count; i++)
        {
            var b = Brightness(_lights[i].Colour);
            if (b < least)
            {
                least = b;
                dimmest = i;
            }
        }
        if (dimmest >= 0)
            _lights[dimmest] = (colour, toward);
    }

    /// <summary>The lights after the first <paramref name="skip"/>, as the RSP gets them.</summary>
    public IReadOnlyList<DirectionalLight> After(int skip) =>
        [.. _lights.Skip(skip).Select(l => new DirectionalLight(new Vector3(l.Colour.R, l.Colour.G, l.Colour.B) / 255, l.Toward))];

    public void Clear()
    {
        _lights.Clear();
        Kept = 0;
    }

    private static int Brightness(Rgba c) => c.R * c.R + c.G * c.G + c.B * c.B;
}
