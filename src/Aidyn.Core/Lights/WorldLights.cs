using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Lights;

/// <summary>
/// The point lights that light bodies and the actors a map places, beside the sky's sun and ambient: the loaded
/// chunks' light map objects and the dynamic lights. Each frame, each lit draw gathers the lights that reach it into
/// its own few directional lights (<see cref="SceneLights"/>).
/// </summary>
/// <remarks>
/// <para>
/// From 1.0's machine code. A body (<c>Bodies.SetLights</c>, 0x80013798) gets the sun while the sky is outdoor, the
/// moon while it's up (cyan by the body's tint, so black but for combat's, yet taking a light's room), the dynamic lights (<c>Lights.AddDynamic</c>, 0x8004d9e0), and then, only if
/// it's one of the party's (the player in trek), the light map objects that the grid cell under its centre lists in
/// its own chunk (<c>WorldLights.ForBody</c>, 0x8004d878). An actor a map places, unless it's fullbright, gets the sun
/// while outdoor, the dynamic lights and every light map object of its own chunk (<c>MapObjects.SetActorLights</c>,
/// 0x80013ac4, <c>Lights.AddZone</c>, 0x8004d9b0). Chunk scenery and plain scene objects get none: they keep the
/// white ambient every scene starts with.
/// </para>
/// <para>
/// Each light (<c>WorldLights.AddNear</c>, 0x8004d384), if it's active: an occludable one is skipped if a sphere of 0.25
/// swept from it to the body's centre hits a triangle facing it, see-through ones aside; then one no nearer than its
/// radius is skipped. Otherwise its falloff is (radius − distance) / radius, straight-line and in 3D. The first time
/// in a frame something reaches it, its colour moves on by the frame's ticks and is scaled by that falloff, in place;
/// every time, the colour is scaled again by the screen fade's level and added as a directional light from the body
/// toward the light, or toward the camera's eye for one that faces the camera. So what a light reaches second in a
/// frame takes the first one's falloff.
/// </para>
/// </remarks>
public sealed class WorldLights(IRandom random)
{
    private readonly Dictionary<LoadedMap, PointLight[]> _maps = [];

    /// <summary>The lights the game adds for a while.</summary>
    public DynamicLights Dynamic { get; } = new();

    /// <summary>The frame being drawn (<c>GameFrame.Counter</c>, 0x800d8090), counted from 1.</summary>
    public long Frame { get; private set; }

    /// <summary>The ticks the frame covers, which the lights' colours move on by.</summary>
    public int Ticks { get; private set; } = 1;

    /// <summary>The screen fade's level, 1 clear and 0 black (<c>ScreenFade.Alpha</c>), which scales every light.</summary>
    public float Fade { get; set; } = 1;

    /// <summary>The camera's eye, which lights that face the camera shine from.</summary>
    public Vector3 Eye { get; set; }

    /// <summary>The collision occludable lights are blocked by; with none, nothing blocks them.</summary>
    public CollisionWorld? World { get; set; }

    /// <summary>Whether the sky is outdoor, which gives bodies and actors the sun, a light no other takes the place of.</summary>
    public bool Sun { get; set; }

    /// <summary>
    /// The way to the moon while it's up and the sky is outdoor (<c>Sky.MoonDirection</c>, <c>0x800D9538</c>, with y over 0), else
    /// null: a body then gets the moon's light after the sun's, which nothing takes the place of either.
    /// </summary>
    public Vector3? Moon { get; set; }

    /// <summary>
    /// The moon's light on a body (<c>Bodies.SetLights</c>): cyan, (0, g, g) with g = 200 × the moon's height × the screen's fade
    /// × the body's tint level, truncated to a byte (the 200.0s at <c>0x800CDBB0</c> and <c>0x800CDBB8</c>). Black but in combat.
    /// </summary>
    public static Rgba MoonLight(float height, float fade, float tint)
    {
        var g = (byte)(int)(height * 200f * fade * tint);
        return new Rgba(0, g, g, 0);
    }

    /// <summary>Keeps a chunk's light map objects while it's loaded, active or not, in its objects' order.</summary>
    public void Load(LoadedMap map) =>
        _maps[map] = [.. map.Record.Objects.OfType<LightObject>().Select(l => PointLight.For(l, map.Origin))];

    public void Unload(LoadedMap map) => _maps.Remove(map);

    /// <summary>A chunk's light map objects, by their place in its objects, and whether each is active.</summary>
    public IEnumerable<(PointLight Light, bool Active)> In(LoadedMap map)
    {
        if (!_maps.TryGetValue(map, out var lights))
            yield break;
        var i = 0;
        foreach (var o in map.Record.Objects.OfType<LightObject>())
            yield return (lights[i++], map.IsActive(o.Index));
    }

    /// <summary>
    /// Starts a frame of <paramref name="ticks"/> ticks: the dynamic lights' lives count down (from the zone engine's
    /// frame), and the lights may move on again.
    /// </summary>
    public void BeginFrame(int ticks)
    {
        Frame++;
        Ticks = ticks;
        Dynamic.Step(ticks);
    }

    /// <summary>
    /// A body's lights this frame, after the sun: the dynamic lights, then, for one of the party's, the light map
    /// objects its grid cell lists.
    /// </summary>
    /// <param name="centre">Its sphere's centre, in the world.</param>
    /// <param name="map">The chunk it's in, or null outside any.</param>
    /// <param name="tint">The body's tint level (<c>+0x75C</c>), which the moon's light is scaled by: 0 but in combat.</param>
    public IReadOnlyList<DirectionalLight> ForBody(Vector3 centre, LoadedMap? map, bool party, float tint = 0)
    {
        var scene = Begin(SceneLights.BodyMax);
        if (Moon is { } moon)
        {
            scene.Add(MoonLight(moon.Y, Fade, tint), moon);
            scene.Kept++;
        }
        AddNear(scene, centre, Dynamic.Lit.Select(l => (l, true)));
        if (party && map is not null && map.Record.Grid is { } grid && _maps.ContainsKey(map))
        {
            var lights = In(map).ToArray();
            var byIndex = map.Record.Objects.OfType<LightObject>().Select((o, i) => (o.Index, i)).ToDictionary(p => p.Index, p => p.i);
            var listed = grid.At(centre - map.Origin).Where(byIndex.ContainsKey).Select(i => lights[byIndex[i]]);
            AddNear(scene, centre, listed);
        }
        // The moon's is handed on with the rest, black or not: it takes its room in the RSP's lights.
        return scene.After(Sun ? 1 : 0);
    }

    /// <summary>An actor's lights this frame, after the sun: the dynamic lights, then every light map object of its chunk.</summary>
    /// <param name="origin">Where the map places it, in the world.</param>
    public IReadOnlyList<DirectionalLight> ForActor(Vector3 origin, LoadedMap map)
    {
        var scene = Begin(SceneLights.ActorMax);
        AddNear(scene, origin, Dynamic.Lit.Select(l => (l, true)));
        AddNear(scene, origin, In(map));
        return scene.After(scene.Kept);
    }

    private SceneLights Begin(int max)
    {
        var scene = new SceneLights(max);
        if (Sun)
        {
            // Its colour doesn't matter here: nothing takes its place.
            scene.Add(default, Vector3.UnitY);
            scene.Kept = 1;
        }
        return scene;
    }

    /// <summary>Adds those of <paramref name="lights"/> that reach <paramref name="at"/> (<c>WorldLights.AddNear</c>).</summary>
    public void AddNear(SceneLights scene, Vector3 at, IEnumerable<(PointLight Light, bool Active)> lights)
    {
        foreach (var (light, active) in lights)
        {
            if (!active)
                continue;
            if (light.Occludable && World is { } world && SpherePhysics.Sweep(world, light.Position, at, 0.25f, true, out _, out _))
                continue;
            var distance = Vector3.Distance(at, light.Position);
            if (distance >= light.Radius)
                continue;
            if (light.Frame < Frame)
            {
                light.Frame = Frame;
                light.Step(Ticks, random);
                light.Working = PointLight.Scale(light.Working, (light.Radius - distance) / light.Radius);
            }
            var toward = (light.FacesCamera ? Eye : light.Position) - at;
            toward = toward.Length() < 1e-6f ? Vector3.UnitY : Vector3.Normalize(toward);
            light.Working = PointLight.Scale(light.Working, Fade);
            scene.Add(light.Working, toward);
        }
    }
}
