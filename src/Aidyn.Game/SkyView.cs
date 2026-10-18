using Aidyn.Core.Calendar;
using Aidyn.Core.Rng;
using Aidyn.Core.Sky;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The time of day and the sky, and the colours they give the scenery: its tint and fog, and the
/// ambient and sun that light bodies, which the <see cref="N64Material"/> reads as global shader
/// parameters. It also keeps the sky's sun, moon, stars, clouds and lens flare, which
/// <see cref="SkyBackdropView"/> and <see cref="LensFlareView"/> draw.
/// </summary>
/// <remarks>
/// The chunk the player is in chooses the sky by its terrain. With no fog in the weather, as at a
/// new game, the original fogs from 996 to 1000 on the depth buffer's scale (<c>SetSceneColors</c>),
/// which with trek's planes of 1 and 180 (<c>ZoneEngine</c>'s draw) is from about 105 units out to
/// the far plane. The weather's fog moves the start in, to 996 − trunc(<c>fogTime</c> × 40)
/// (<c>Zones.SetSceneColors</c>, <c>0x8000c298</c> in 1.0), and its wind blows the clouds.
/// </remarks>
public partial class SkyView : Node
{
    /// <summary>Trek's clip planes (<c>Scene::SetNearFarPlanes</c> on the centre chunk).</summary>
    public const float Near = 1, Far = 180;

    private readonly PluginSet _plugins;
    private readonly Dictionary<RecordId, SkyGradient?> _gradients = [];
    private readonly UiImages _images;
    private (int Row, int Column)? _chunk;
    private ChunkEntry? _entered;
    private Weather? _weather;
    /// <summary>The sky of the terrain last entered (<c>Weather.Data</c> + 0x10), and the rain sky.</summary>
    private SkyGradient? _terrain, _rain;

    /// <param name="clock">The game's clock, which the game runs; the sky reads the time of day from it.</param>
    public SkyView(PluginSet plugins, GameClock clock)
    {
        _plugins = plugins;
        _images = new UiImages(plugins);
        Clock = clock;
        Name = "Sky";
        if (plugins.TrySingleton<SkyRecord>(RecordTypes.Sky) is { } sky)
        {
            Objects = new SkyObjects(sky);
            Clouds = new Clouds(sky);
            Flare = new LensFlare(sky);
        }
        else
        {
            GD.PushWarning("No sky record: no sun, moon, stars, clouds or lens flare.");
        }
    }

    public GameClock Clock { get; }
    /// <summary>
    /// The stars' twinkle's random stream: its own, from the boot seed, and not the game's cosmetic stream (ADR 0006),
    /// because the backdrop draws from it once per rendered frame, and the game's streams must not depend on the frame rate.
    /// </summary>
    public IRandom Twinkle { get; } = new Lcg();
    public SkyState State { get; } = new();
    public SkyColors Colors { get; private set; }
    public SkyObjects? Objects { get; }
    public Clouds? Clouds { get; }
    public LensFlare? Flare { get; }

    /// <summary>How the sky places the clouds, sun, moon and stars across the screen (F6).</summary>
    public SkyPlacement Placement
    {
        get => Objects?.Placement ?? SkyPlacement.Projected;
        set
        {
            Objects?.Placement = value;
            Clouds?.Placement = value;
        }
    }

    /// <summary>
    /// The game's weather, which sets the fog, blows the clouds, brings the rain sky and flashes lightning; none leaves
    /// them as a new game has them.
    /// </summary>
    public Weather? Weather
    {
        get => _weather;
        set
        {
            _weather?.SkyChange -= ChangeSky;
            _weather = value;
            _weather?.SkyChange += ChangeSky;
            _rain = value?.Rules.RainSky is { } rain ? Gradient(rain) : null;
        }
    }

    /// <summary>How thick the weather's fog is, 0 to 1 (<c>fogTime</c>), as the weather has it.</summary>
    public float FogTime => Weather?.FogTime ?? 0;

    /// <summary>
    /// Whether a cut-scene plays, when the scenery's fog ignores the weather's (<c>Zones.SetSceneColors</c> reads
    /// <c>0x800dfe14</c>, which the cut-scene's mode sets, <c>0x8005039c</c>). The sky's fog doesn't.
    /// </summary>
    public bool CutScene { get; set; }

    /// <summary>Whether lightning flashes this frame (<see cref="Weather.Flash"/>): the backdrop is white.</summary>
    public bool Flash => Weather?.Flash == true && State.Drawn;

    /// <summary>
    /// Whether the sky shows at a point on the original's 320 × 240 screen, with nothing drawn over
    /// it, which the lens flare needs. The original reads the depth buffer there.
    /// </summary>
    public Func<System.Numerics.Vector2, bool>? SkyShows { get; set; }

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
        if (TerrainRecord.Find(_plugins, chunk.Terrain) is not { } terrain ||
            Gradient(terrain.Sky) is not { } gradient)
        {
            GD.PushWarning($"No sky for terrain {chunk.Terrain}.");
            return;
        }
        // GetMapTerrain keeps the terrain's sky for the weather to come back to, whatever's chosen.
        _terrain = gradient;
        State.Enter(gradient, terrain.SkyDrawn, _rain, Weather, atOnce: first);
    }

    /// <summary>
    /// Trek's world goes (<c>FreeZoneEngine</c>, which leaves no sky up, <c>SetBackgroundType(2, 0, 0)</c>): the
    /// next chunk entered chooses its sky at once, as trek's world or a cut-scene's stage is built.
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
    /// One tick, after the game's clock has moved on: any change of sky moves on, the clouds blow
    /// along, the lens flare fades, and the colours follow.
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

    /// <summary>Sets the colours for the current time and sky.</summary>
    public void Apply()
    {
        if (State.Current is null)
            return;
        Colors = SkyColors.At(State.Current, State.Next, State.Change, Clock.TimeOfDay, LensFlareValue);
        // RenderSky's flash: the tint and the sky colour, half of which is bodies' ambient, white for the frame.
        if (Flash)
            Colors = Colors.Flashed();
        RenderingServer.GlobalShaderParameterSet(N64Material.TintParameter, Colors.Tint.ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.FogColorParameter, Colors.Fog.ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.FogParameter, new Vector4(SkyColors.FogStart(CutScene ? 0 : FogTime), 1000, Near, Far));
        RenderingServer.GlobalShaderParameterSet(N64Material.BodyAmbientParameter, SkyLight.BodyAmbient(Colors).ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.SunColorParameter, SkyLight.SunColor(Colors, State.Drawn).ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.SunDirectionParameter, SkyLight.SunDirection(Clock.TimeOfDay).ToGodot());
    }

    /// <summary>How much the lens flare darkens the scenery and lifts the sky, from the last frame.</summary>
    public float LensFlareValue => Flare?.Value ?? 0;

    /// <summary>A UI image, as a texture, or null if no plugin has it.</summary>
    public Texture2D? Image(RecordId image) => _images.Get(image);

    /// <summary>A UI image's size in pixels, or nothing if it's missing.</summary>
    public System.Numerics.Vector2 ImageSize(RecordId image) =>
        Image(image) is { } texture ? texture.GetSize().ToNumerics() : default;

    private SkyGradient? Gradient(RecordId image)
    {
        if (_gradients.TryGetValue(image, out var gradient))
            return gradient;
        if (_plugins.FindAsset(image, "png") is { } png && Png.Load(png) is { } data)
        {
            data.Convert(Godot.Image.Format.Rgba8);
            if (data.GetWidth() == SkyGradient.Columns && data.GetHeight() == SkyGradient.Rows)
                gradient = new SkyGradient(data.GetData());
        }
        return _gradients[image] = gradient;
    }
}
