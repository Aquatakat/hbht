using Aidyn.Core.Calendar;
using Aidyn.Core.Sky;
using Aidyn.Inspector;
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
/// the far plane.
/// </remarks>
public partial class SkyView : Node
{
    /// <summary>Trek's clip planes (<c>Scene::SetNearFarPlanes</c> on the centre chunk).</summary>
    public const float Near = 1, Far = 180;

    private readonly PluginSet _plugins;
    private readonly Dictionary<RecordId, SkyGradient?> _gradients = [];
    private readonly Dictionary<RecordId, Texture2D?> _images = [];
    private (int Row, int Column)? _chunk;

    /// <param name="clock">The game's clock, which the game runs; the sky reads the time of day from it.</param>
    public SkyView(PluginSet plugins, GameClock clock)
    {
        _plugins = plugins;
        Clock = clock;
        Name = "Sky";
        if (plugins.TryGet<SkyRecord>(RecordId.Decimal(RecordTypes.Sky, 0)) is { } sky)
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

    /// <summary>How thick the weather's fog is, 0 to 1 (<c>weather.fogTime</c>). There's no weather yet.</summary>
    public float FogTime { get; set; }

    /// <summary>
    /// Whether the sky shows at a point on the original's 320 × 240 screen, with nothing drawn over
    /// it, which the lens flare needs. The original reads the depth buffer there.
    /// </summary>
    public Func<System.Numerics.Vector2, bool>? SkyShows { get; set; }

    /// <summary>
    /// Chooses the sky of the chunk the player is in, when that changes. Every new chunk chooses
    /// again, which restarts a change of sky in progress, as in the original.
    /// </summary>
    public void Enter(ChunkEntry? chunk)
    {
        if (chunk is null || (chunk.Row, chunk.Column) == _chunk)
            return;
        var first = _chunk is null;
        _chunk = (chunk.Row, chunk.Column);
        if (_plugins.TryGet<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, chunk.Terrain)) is not { } terrain ||
            Gradient(terrain.Sky) is not { } gradient)
        {
            GD.PushWarning($"No sky for terrain {chunk.Terrain}.");
            return;
        }
        State.Choose(gradient, terrain.SkyDrawn, gradual: !first);
    }

    /// <summary>
    /// One tick, after the game's clock has moved on: any change of sky moves on, the clouds blow
    /// along, the lens flare fades, and the colours follow.
    /// </summary>
    public void Step()
    {
        State.Step();
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
        RenderingServer.GlobalShaderParameterSet(N64Material.TintParameter, Colors.Tint.ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.FogColorParameter, Colors.Fog.ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.FogParameter, new Vector4(996 - (int)(FogTime * 40), 1000, Near, Far));
        RenderingServer.GlobalShaderParameterSet(N64Material.BodyAmbientParameter, SkyLight.BodyAmbient(Colors).ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.SunColorParameter, SkyLight.SunColor(Colors, State.Drawn).ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.SunDirectionParameter, SkyLight.SunDirection(Clock.TimeOfDay).ToGodot());
    }

    /// <summary>How much the lens flare darkens the scenery and lifts the sky, from the last frame.</summary>
    public float LensFlareValue => Flare?.Value ?? 0;

    /// <summary>A UI image, as a texture, or null if no plugin has it.</summary>
    public Texture2D? Image(RecordId image)
    {
        if (_images.TryGetValue(image, out var texture))
            return texture;
        if (_plugins.FindAsset(image, "png") is { } png)
        {
            var data = new Image();
            if (data.LoadPngFromBuffer(File.ReadAllBytes(png)) == Error.Ok)
                texture = ImageTexture.CreateFromImage(data);
        }
        else
        {
            GD.PushWarning($"No asset for {image}.");
        }
        return _images[image] = texture;
    }

    /// <summary>A UI image's size in pixels, or nothing if it's missing.</summary>
    public System.Numerics.Vector2 ImageSize(RecordId image) =>
        Image(image) is { } texture ? texture.GetSize().ToNumerics() : default;

    private SkyGradient? Gradient(RecordId image)
    {
        if (_gradients.TryGetValue(image, out var gradient))
            return gradient;
        if (_plugins.FindAsset(image, "png") is { } png)
        {
            var data = new Image();
            data.LoadPngFromBuffer(File.ReadAllBytes(png));
            data.Convert(Godot.Image.Format.Rgba8);
            if (data.GetWidth() == SkyGradient.Columns && data.GetHeight() == SkyGradient.Rows)
                gradient = new SkyGradient(data.GetData());
        }
        return _gradients[image] = gradient;
    }
}
