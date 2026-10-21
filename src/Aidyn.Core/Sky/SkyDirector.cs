using System.Numerics;
using Aidyn.Core.Calendar;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Sky;

/// <summary>
/// The sky as the zone engine draws it (<c>Sky::RenderSky</c> and what it calls): which sky is up and the one it's changing
/// to (<see cref="State"/>), chosen by the terrain of the chunk the player is in and by the weather; the colours they give
/// the scenery and bodies (<see cref="Colors"/>); the sun, moon and stars with the stars' twinkle, the clouds blowing on the
/// weather's wind, and the lens flare. The session steps it (<see cref="Step"/>, <see cref="StepTwinkle"/>) and the runtime
/// draws what it holds.
/// </summary>
/// <remarks>
/// One sky goes on from trek's world to a cut-scene's stage or a battle's arena and back (<see cref="Chunks.ZoneEngine.Sky"/>):
/// each is entered as it's built, choosing its sky at once (<see cref="ClearSky"/>). What only the drawing knows comes back
/// from it: where the sun was last drawn (<see cref="SkyObjects.SunMiddle"/>, which drawing the sky's objects sets) and whether
/// the sky shows there (<see cref="SkyShows"/>), for the lens flare.
/// </remarks>
public sealed class SkyDirector
{
    private readonly PluginSet _plugins;
    private readonly IRandom _twinkle;
    private readonly FrameGate _twinkleFrame = new(Cadence.FrameTicks);
    private readonly Dictionary<RecordId, SkyGradient?> _gradients = [];
    private (int Row, int Column)? _chunk;
    private ChunkEntry? _entered;
    /// <summary>The sky of the terrain last entered (<c>Weather.Data</c> + 0x10), and the rain sky.</summary>
    private SkyGradient? _terrain, _rain;
    private bool _toldNoSky;

    /// <param name="clock">The game's clock: the sky shows its time of day, and the moon its date.</param>
    /// <param name="weather">
    /// The game's weather, which sets the fog, blows the clouds, brings the rain sky and flashes lightning; none leaves them as
    /// a new game has them.
    /// </param>
    /// <param name="twinkle">The stars' own stream (<see cref="RandomStreams.Twinkle"/>).</param>
    public SkyDirector(PluginSet plugins, GameClock clock, Weather? weather, IRandom twinkle)
    {
        _plugins = plugins;
        _twinkle = twinkle;
        Clock = clock;
        Weather = weather;
        if (plugins.TrySingleton<SkyRecord>() is { } sky)
        {
            Objects = new SkyObjects(sky);
            Clouds = new Clouds(sky);
            Flare = new LensFlare(sky);
        }
        if (weather is not null)
        {
            weather.SkyChange += ChangeSky;
            _rain = weather.Rules.RainSky is { } rain ? Gradient(rain) : null;
        }
    }

    public GameClock Clock { get; }

    public Weather? Weather { get; }

    /// <summary>Which sky is up, and the one it's changing to.</summary>
    public SkyState State { get; } = new();

    /// <summary>Whether the sky is drawn behind the scenery, an outdoor sky's: the original's sky type 3, not 4.</summary>
    public bool Drawn => State.Drawn;

    /// <summary>The sun, moon and stars, or none without a sky record.</summary>
    public SkyObjects? Objects { get; }

    /// <summary>The clouds, or none without a sky record.</summary>
    public Clouds? Clouds { get; }

    /// <summary>The lens flare, or none without a sky record.</summary>
    public LensFlare? Flare { get; }

    /// <summary>Where a problem goes, such as a terrain with no sky: whoever's watching the log. The session sets it.</summary>
    public Action<string> Warn { get; set; } = _ => { };

    /// <summary>
    /// Whether the sky shows at a point on the original's 320 × 240 screen, with nothing drawn over it, which the lens flare
    /// needs. The original reads the depth buffer there at the end of each frame; whoever draws the scene says. None never
    /// shows it, as headless.
    /// </summary>
    public Func<Vector2, bool>? SkyShows { get; set; }

    /// <summary>The time of day the sky shows: the clock's, moved by Darkness and Light (<see cref="GameClock.ShownTimeOfDay"/>).</summary>
    public float TimeOfDay => Clock.ShownTimeOfDay;

    /// <summary>The sky's slower fade, which multiplies its colours and its backdrop: a cast in a battle dims it (<see cref="SkyBrightness"/>).</summary>
    public float Dim { get; set; } = 1;

    /// <summary>
    /// Whether a cut-scene plays, when the scenery's fog ignores the weather's (<c>Zones.SetSceneColors</c> reads
    /// <c>0x800dfe14</c>, which the cut-scene's mode sets, <c>0x8005039c</c>). The sky's fog doesn't.
    /// </summary>
    public bool CutScene { get; set; }

    /// <summary>How thick the weather's fog is, 0 to 1 (<c>fogTime</c>), as the weather has it.</summary>
    public float FogTime => Weather?.FogTime ?? 0;

    /// <summary>Whether lightning flashes this frame (<see cref="Weather.Flash"/>): the backdrop is white.</summary>
    public bool Flash => Weather?.Flash == true && State.Drawn;

    /// <summary>How much the lens flare darkens the scenery and lifts the sky, from the last frame.</summary>
    public float LensFlareValue => Flare?.Value ?? 0;

    /// <summary>The way to the moon while it's up, which lights bodies (<see cref="SkyLight.MoonDirection"/>), else null.</summary>
    public Vector3? Moon => SkyLight.MoonDirection(TimeOfDay, Objects?.MoonAzimuth ?? 0) is { Y: > 0 } moon ? moon : null;

    /// <summary>How the sky places the clouds, sun, moon and stars across the screen: the settings' (F6).</summary>
    public SkyPlacement Placement
    {
        get => Objects?.Placement ?? SkyPlacement.Projected;
        set
        {
            Objects?.Placement = value;
            Clouds?.Placement = value;
        }
    }

    // What the colours came to as they were last worked out (Apply), which the scenery and bodies are drawn with until the next.

    /// <summary>The colours for the time and the sky, as last worked out (<see cref="Apply"/>).</summary>
    public SkyColors Colors { get; private set; }

    /// <summary>Whether the colours have been worked out yet: not until a sky has been chosen.</summary>
    public bool Applied { get; private set; }

    /// <summary>Where the scenery's fog starts on the depth buffer's scale (<see cref="SkyColors.FogStart"/>), as last worked out.</summary>
    public float SceneryFogStart { get; private set; }

    /// <summary>The sun's light on bodies (<see cref="SkyLight.SunColor"/>), black under a sky that isn't drawn, as last worked out.</summary>
    public Vector3 SunColor { get; private set; }

    /// <summary>The way to the sun (<see cref="SkyLight.SunDirection"/>), as last worked out.</summary>
    public Vector3 SunDirection { get; private set; }

    /// <summary>Bodies' ambient (<see cref="SkyLight.BodyAmbient"/>): half the sky colour.</summary>
    public Vector3 BodyAmbient => SkyLight.BodyAmbient(Colors);

    /// <summary>
    /// Chooses the sky of the chunk the player is in, when that changes (<see cref="SkyState.Enter"/>). Every new
    /// chunk chooses again, which restarts a change of sky in progress, as in the original, unless the weather holds
    /// the sky. The first chunk after <see cref="ClearSky"/> chooses at once.
    /// </summary>
    public void Enter(ChunkEntry? chunk)
    {
        if (chunk is null || (chunk.Row, chunk.Column) == _chunk)
            return;
        var first = _chunk is null;
        _chunk = (chunk.Row, chunk.Column);
        _entered = chunk;
        if (Objects is null && !_toldNoSky)
        {
            _toldNoSky = true;
            Warn("No sky record: no sun, moon, stars, clouds or lens flare.");
        }
        if (TerrainRecord.Find(_plugins, chunk.Terrain) is not { } terrain ||
            Gradient(terrain.Sky) is not { } gradient)
        {
            Warn($"No sky for terrain {chunk.Terrain}.");
            return;
        }
        // GetMapTerrain keeps the terrain's sky for the weather to come back to, whatever's chosen.
        _terrain = gradient;
        State.Enter(gradient, terrain.SkyDrawn, _rain, Weather, atOnce: first);
    }

    /// <summary>
    /// The world goes (<c>FreeZoneEngine</c>, which leaves no sky up, <c>SetBackgroundType(2, 0, 0)</c>): the next chunk
    /// entered chooses its sky at once, as trek's world or a cut-scene's stage is built.
    /// </summary>
    public void ClearSky() => _chunk = null;

    /// <summary>Chooses the sky of the chunk the player is in again at once, as the weather has it now (<c>--weather</c>).</summary>
    public void ChooseAgain()
    {
        ClearSky();
        Enter(_entered);
    }

    /// <summary>The weather changes the sky over 600 ticks: to the rain sky, or back to the terrain's.</summary>
    private void ChangeSky(bool rain)
    {
        if ((rain ? _rain : _terrain) is { } gradient)
            State.Choose(gradient, State.Drawn);
    }

    /// <summary>
    /// One tick, after the game's clock has moved on, in trek, a cut-scene or a battle: any change of sky moves on, the
    /// clouds blow along on the weather's wind, the lens flare fades by where the sun was last drawn, and the colours follow.
    /// </summary>
    public void Step()
    {
        State.Step();
        if (Clouds is not null && Weather is not null)
            Clouds.Wind = Weather.Wind;
        Clouds?.Step();
        if (Flare is not null && Objects is not null)
        {
            var sun = Objects.SunMiddle;
            Flare.Step(sun, SkyShows?.Invoke(sun) ?? false, Colors.Tint.X, State.Drawn);
        }
        Apply();
    }

    /// <summary>
    /// The stars twinkle (<see cref="SkyObjects.Twinkle"/>) while they're drawn, in an outdoor sky: once a 30 fps frame in
    /// every mode (<see cref="Cadence.FrameTicks"/>), as the original draws the sky once a frame behind whatever holds the world.
    /// </summary>
    public void StepTwinkle()
    {
        if (!_twinkleFrame.Tick())
            return;
        if (Objects is not null && State is { Current: not null, Drawn: true })
            Objects.Twinkle(_twinkle, TimeOfDay);
    }

    /// <summary>
    /// Works the colours out for the time and the sky as they are now (<c>Sky::SetColors</c>), with the lens flare's last
    /// value, the sky's fade and lightning's flash, and the scenery's fog and the sun's light from them. Nothing changes until
    /// a sky has been chosen.
    /// </summary>
    public void Apply()
    {
        if (State.Current is null)
            return;
        Colors = SkyColors.At(State.Current, State.Next, State.Change, TimeOfDay, LensFlareValue, dim: Dim);
        // RenderSky's flash: the tint and the sky colour, half of which is bodies' ambient, white for the frame.
        if (Flash)
            Colors = Colors.Flashed();
        SceneryFogStart = SkyColors.FogStart(CutScene ? 0 : FogTime);
        SunColor = SkyLight.SunColor(Colors, State.Drawn);
        SunDirection = SkyLight.SunDirection(TimeOfDay);
        Applied = true;
    }

    /// <summary>The gradient an image holds (<see cref="SkyGradient"/>), decoded once, or null if no plugin has it as one.</summary>
    private SkyGradient? Gradient(RecordId image)
    {
        if (_gradients.TryGetValue(image, out var gradient))
            return gradient;
        if (_plugins.FindAsset(image, "png") is { } png && PngImage.Read(png) is { Width: SkyGradient.Columns, Height: SkyGradient.Rows } data)
            gradient = new SkyGradient(data.Rgba);
        return _gradients[image] = gradient;
    }
}
