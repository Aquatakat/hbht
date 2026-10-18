using System.Globalization;
using Aidyn.Core;
using Aidyn.Core.Cameras;
using Aidyn.Plugins;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The runtime's command line: the options after <c>--</c> (docs/systems/runtime.md), read where they're asked for, so
/// one that's badly written fails where it would apply. <see cref="Game"/> reads the ones that say where and how a game
/// starts; <c>Game.RunLaunchActions</c> carries out the ones that drive it.
/// </summary>
/// <remarks>
/// <c>--save &lt;path&gt;</c> starts from a save (see docs/systems/saves.md), where it stands unless <c>--chunk</c> or
/// <c>--at</c> say otherwise; <c>--set &lt;n&gt;</c> (default the new game record's, the overworld), <c>--chunk &lt;name&gt;</c> and
/// <c>--at &lt;x&gt;,&lt;z&gt;</c> in the chunk's coordinates (default where a new game starts, I08 at 16, 62), <c>--yaw</c>
/// in degrees for the way the player and camera face (0 looks along −Z), <c>--pitch</c> (negative looks down, default −15)
/// and <c>--distance</c> for the camera, <c>--pace</c> for the player's speed (1 is the original's), <c>--fly
/// &lt;x&gt;,&lt;y&gt;,&lt;z&gt;</c> to start with the free camera there in world coordinates, looking along <c>--yaw</c>
/// and <c>--pitch</c>, <c>--time &lt;hours&gt;</c> for the time of day (a new game starts at 12), <c>--flags &lt;n&gt;,…</c>
/// to set plain flags, <c>--weather rain|snow|fog|clear[,intensity[,fog]]</c> for the weather at once,
/// <c>--precipitation &lt;scale&gt;</c> for that many times the original's raindrops and snowflakes, <c>--draw-distance
/// &lt;scale&gt;</c>, <c>--detail nearest</c>, <c>--lights off</c>, <c>--camera outdoor|interior</c> for the camera's mode
/// (by default, what going through a door into the chunk gives), and <c>--hold &lt;action&gt;,&lt;seconds&gt;</c>, which
/// holds an input such as <c>move_forward</c> (join several with <c>+</c>, as in <c>move_forward+walk</c>), and
/// <c>--screenshot &lt;png&gt;</c>, which saves the window after that and quits; with <c>--frames &lt;n&gt;</c>, the next n
/// frames as rendered, in real time, as <c>&lt;png&gt;-000.png</c> on. <c>--sky original</c> places the sky's sprites as
/// the original does rather than projected. <c>--menu 1</c> opens the pause menu on the tab it last showed, <c>--menu
/// system</c>, <c>party</c> or <c>world</c> on that tab, and <c>--menu loot</c> an empty loot window; <c>--ui-lines
/// &lt;n&gt;</c> sets the UI screen's height, and <c>--press &lt;action&gt;,…</c> presses each action in turn, such as
/// <c>pause,menu_member_left</c>. <c>--cutscene &lt;id&gt;</c> plays a cut-scene at once, as what starts it would, and
/// <c>--cinematic &lt;n&gt;</c> a cinematic.
/// </remarks>
public sealed class LaunchOptions(Dictionary<string, string> values)
{
    /// <summary>The options in the arguments after <c>--</c>.</summary>
    public static LaunchOptions Parse(string[] args) => new(CommandLine.Parse(args));

    /// <summary>These options without any but the screenshot: a save loaded from the menu starts where it stands, and the presses aren't made again.</summary>
    public LaunchOptions OnlyScreenshot() => new(values.Where(a => a.Key == "screenshot").ToDictionary());

    private string? Text(string name) => values.GetValueOrDefault(name);

    private float? Number(string name) => values.TryGetValue(name, out var v) ? float.Parse(v, CultureInfo.InvariantCulture) : null;

    private int? Integer(string name) => values.TryGetValue(name, out var v) ? int.Parse(v, CultureInfo.InvariantCulture) : null;

    // Where and how the game starts.

    /// <summary>A save to start from.</summary>
    public string? Save => Text("save");

    /// <summary>The map set, by number.</summary>
    public int? Set => Integer("set");

    /// <summary>Whether the start says where the player stands, in place of a save's.</summary>
    public bool PlacesPlayer => values.ContainsKey("chunk") || values.ContainsKey("at");

    /// <summary>The chunk to start in, or null for the save's or the new game's.</summary>
    public string? Chunk => Text("chunk");

    /// <summary>Where to start in the chunk: x, then z, in its coordinates, or null for where a new game starts.</summary>
    public float[]? At => Text("at") is { } at ? CommandLine.Floats(at) : null;

    /// <summary>The way the player and camera face, in degrees, or null for the save's or along −Z.</summary>
    public float? Yaw => Number("yaw");

    /// <summary>The camera's pitch in degrees, negative looking down.</summary>
    public float Pitch => Number("pitch") ?? -15;

    public float? Distance => Number("distance");

    /// <summary>The camera's mode, or null for what going through a door into the chunk gives.</summary>
    public CameraMode? Camera => Text("camera") is { } mode ? Enum.Parse<CameraMode>(mode, ignoreCase: true) : null;

    /// <summary>The player's speed, 1 being the original's.</summary>
    public float? Pace => Number("pace");

    /// <summary>The time of day in hours.</summary>
    public float? Time => Number("time");

    /// <summary>Plain flags to set before anything loads, such as 5,11 for after the Kendall and ambush cut-scenes.</summary>
    public IEnumerable<int> Flags =>
        (Text("flags") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => int.Parse(f, CultureInfo.InvariantCulture));

    /// <summary>Whether bodies and actors are lit by point lights; <c>--lights off</c> leaves them lit by the sky alone, as a baseline.</summary>
    public bool PointLights => Text("lights") != "off";

    /// <summary>Sets the time of day <c>--time</c> names, before it's written into the flags.</summary>
    public void SetTimeOfDay(GameState game)
    {
        if (Time is { } time)
            game.Clock.SetTimeOfDay(time);
    }

    /// <summary>Sets the plain flags <c>--flags</c> names.</summary>
    public void SetFlags(GameState game)
    {
        foreach (var flag in Flags)
            game.Flags.Set(flag, true);
    }

    /// <summary>
    /// Carries out the options that are settings for this run, which aren't kept unless the options menu changes
    /// something.
    /// </summary>
    public void ApplyTo(Settings settings)
    {
        if (Text("sky") is { } sky)
            settings.SkyPlacement = sky == "original" ? Core.Sky.SkyPlacement.Original : Core.Sky.SkyPlacement.Projected;
        if (Number("draw-distance") is { } drawDistance)
            settings.DrawDistance = drawDistance;
        if (Text("detail") is { } detail)
            settings.NearestDetail = detail == "nearest";
        if (Number("precipitation") is { } precipitation)
            settings.PrecipitationDensity = precipitation;
        if (Integer("ui-lines") is { } lines)
            settings.UiLines = lines;
    }

    // What drives the run.

    public string? Weather => Text("weather");

    /// <summary>Where to put the free camera in world coordinates, which also turns it on.</summary>
    public float[]? Fly => Text("fly") is { } fly ? CommandLine.Floats(fly) : null;

    /// <summary>The pause menu's tab to open, <c>1</c> for the last, or <c>loot</c>.</summary>
    public string? Menu => Text("menu");

    /// <summary>An input to hold and for how long, as <c>action+action,seconds</c>.</summary>
    public string? Hold => Text("hold");

    /// <summary>Actions to press in turn, joined with commas.</summary>
    public string? Press => Text("press");

    /// <summary>A cut-scene to play at once.</summary>
    public RecordId? CutScene => Text("cutscene") is { } id ? RecordId.Parse(id) : null;

    /// <summary>A cinematic to play, as <c>n[,shot[,time[,hold]]]</c>.</summary>
    public string? Cinematic => Text("cinematic");

    /// <summary>Where to save the window once the rest is done.</summary>
    public string? Screenshot => Text("screenshot");

    /// <summary>How many frames to save, as rendered, from the screenshot on, or none for just the one.</summary>
    public int Frames => Integer("frames") ?? 0;

    /// <summary>
    /// <c>--weather rain|snow|fog|clear[,intensity[,fog]]</c>: the weather at once, already set in, for screenshots. Rain
    /// and snow default to 0.5 with fog 0.2, and fog to 0.5.
    /// </summary>
    public static void ForceWeather(GameState game, string text)
    {
        if (game.Weather is not { } weather)
        {
            GD.PushWarning("No weather record: --weather does nothing.");
            return;
        }
        // The part of the day as it is, so the next frame doesn't roll.
        weather.PartOfDay = game.Clock.Date.PartOfDay;
        var parts = text.Split(',');
        float Part(int i, float fallback) => parts.Length > i ? float.Parse(parts[i], CultureInfo.InvariantCulture) : fallback;
        switch (parts[0])
        {
            case "rain":
                weather.Force(Core.Calendar.WeatherKind.Precipitation, Core.Calendar.Precipitation.Rain, Part(1, 0.5f), Part(2, 0.2f));
                break;
            case "snow":
                weather.Force(Core.Calendar.WeatherKind.Precipitation, Core.Calendar.Precipitation.Snow, Part(1, 0.5f), Part(2, 0.2f));
                break;
            case "fog":
                weather.Force(Core.Calendar.WeatherKind.Fog, fog: Part(1, 0.5f));
                break;
            case "clear":
                weather.Force(Core.Calendar.WeatherKind.Clear);
                break;
            default:
                GD.PushWarning($"--weather {text}: give rain, snow, fog or clear.");
                break;
        }
    }
}
