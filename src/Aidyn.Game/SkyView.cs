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
/// parameters.
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
    private (int Row, int Column)? _chunk;

    public SkyView(PluginSet plugins)
    {
        _plugins = plugins;
        Name = "Sky";
    }

    public GameClock Clock { get; } = GameClock.NewGame();
    public SkyState State { get; } = new();
    public SkyColors Colors { get; private set; }

    /// <summary>How thick the weather's fog is, 0 to 1 (<c>weather.fogTime</c>). There's no weather yet.</summary>
    public float FogTime { get; set; }

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

    /// <summary>One tick: the clock and any change of sky move on, and the colours follow.</summary>
    public void Step()
    {
        Clock.Step();
        State.Step();
        Apply();
    }

    /// <summary>Sets the colours for the current time and sky.</summary>
    public void Apply()
    {
        if (State.Current is null)
            return;
        Colors = State.Colors(Clock.TimeOfDay);
        RenderingServer.GlobalShaderParameterSet(N64Material.TintParameter, Colors.Tint.ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.FogColorParameter, Colors.Fog.ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.FogParameter, new Vector4(996 - (int)(FogTime * 40), 1000, Near, Far));
        RenderingServer.GlobalShaderParameterSet(N64Material.BodyAmbientParameter, SkyLight.BodyAmbient(Colors).ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.SunColorParameter, SkyLight.SunColor(Colors, State.Drawn).ToGodot());
        RenderingServer.GlobalShaderParameterSet(N64Material.SunDirectionParameter, SkyLight.SunDirection(Clock.TimeOfDay).ToGodot());
    }

    private SkyGradient? Gradient(RecordId image)
    {
        if (_gradients.TryGetValue(image, out var gradient))
            return gradient;
        if (_plugins.FindAsset(image, "png") is { } png)
        {
            var data = new Image();
            data.LoadPngFromBuffer(File.ReadAllBytes(png));
            data.Convert(Image.Format.Rgba8);
            if (data.GetWidth() == SkyGradient.Columns && data.GetHeight() == SkyGradient.Rows)
                gradient = new SkyGradient(data.GetData());
        }
        return _gradients[image] = gradient;
    }
}
