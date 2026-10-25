using System.Text.Json;
using System.Text.Json.Serialization;
using Aidyn.Core.Sky;
using Aidyn.Core.Ui;
using Aidyn.Plugins;

namespace Aidyn.Core;

/// <summary>
/// The player's settings, which the options menu changes and the runtime carries out: the original's
/// volumes, and the port's graphics and fidelity settings, which live in the same menu rather than in a
/// launcher. They're the player's, not the game's, so a save doesn't hold them: they're kept in
/// <c>settings.json</c> in the data folder (<see cref="DataPaths.Settings"/>). The original writes its volumes and
/// resolution into every save (<c>QuestData::Save</c>), so loading a game set them back.
/// </summary>
/// <remarks>
/// Only the options menu (<see cref="Ui.OptionsMenu"/>) and the runtime read these. The core plays by what the runtime makes
/// of them for each tick, with the command line's for the run over them (<see cref="SessionSettings"/>, ADR 0023), and the
/// runtime draws by the rest.
/// </remarks>
public sealed class Settings
{
    /// <summary>
    /// The volumes' steps, from silent to full: the original's are floats from 0 to 1 that the stick steps by
    /// 0.01 (<c>WidgetOptionsConfig</c>), which these are in hundredths.
    /// </summary>
    public const int MaxVolume = 100;

    /// <summary>The sound effects' volume (<c>VolSFX</c>): full in a new game (<c>app.cpp</c>).</summary>
    public int SoundVolume { get; set; } = MaxVolume;

    /// <summary>The music's volume (<c>VolBGM</c>): 0.65 in a new game.</summary>
    public int MusicVolume { get; set; } = 65;

    /// <summary>How many lines tall the UI screen is: the original's 240, or more for smaller UI with room for more.</summary>
    public int UiLines { get; set; } = 240;

    /// <summary>How the UI screen is scaled up to the window.</summary>
    public UiFilter UiFilter { get; set; } = UiFilter.VideoOut;

    /// <summary>
    /// How many lines the 3D world is drawn at, scaled up to the window: the original's 240 or its Expansion Pak's 480, or 0 to draw
    /// it at <see cref="RenderScale"/>.
    /// </summary>
    public int WorldLines { get; set; }

    /// <summary>
    /// The 3D world's size as a part of the window's (or the screen's, in fullscreen), when <see cref="WorldLines"/> is 0: under 1
    /// scaled up, over 1 scaled down, supersampling.
    /// </summary>
    public float RenderScale { get; set; } = 1;

    /// <summary>Older settings' supersampling, at twice the window's resolution: read as a render scale of 2, and never written.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Supersampled
    {
        get => false;
        set
        {
            if (value)
                RenderScale = 2;
        }
    }

    /// <summary>Multisample anti-aliasing's samples: 0 (off), 2, 4 or 8.</summary>
    public int Antialiasing { get; set; }

    /// <summary>Borderless fullscreen at the screen's own resolution, or a window.</summary>
    public bool Fullscreen { get; set; }

    /// <summary>
    /// The window's size while it's windowed and not maximized, or 0 × 0 for whatever size it opens at. The runtime keeps it as the
    /// player resizes the window by hand, too.
    /// </summary>
    public int WindowWidth { get; set; }

    public int WindowHeight { get; set; }

    /// <summary>Whether the window is maximized, by the options or by hand: <see cref="WindowWidth"/> × <see cref="WindowHeight"/> is then its size restored.</summary>
    public bool Maximized { get; set; }

    public bool VSync { get; set; } = true;

    /// <summary><see cref="FrameRateLimit"/>'s value for the screen's own refresh rate.</summary>
    public const int DisplayRate = -1;

    /// <summary>The most frames drawn a second: a number, <see cref="DisplayRate"/> for the screen's refresh rate, or 0 for no limit.</summary>
    public int FrameRateLimit { get; set; } = DisplayRate;

    /// <summary>Whether the Overworld's terrain is drawn out to the 5 × 5 chunks around the player rather than the original's 3 × 3.</summary>
    public bool ExtendedDrawDistance { get; set; }

    /// <summary>How far scene objects are drawn, as a multiple of the original's distances.</summary>
    public float ObjectDistance { get; set; } = 1;

    /// <summary>Whether scene objects always draw their nearest, most detailed level.</summary>
    public bool NearestDetail { get; set; }

    /// <summary>How many raindrops and snowflakes fall, as a multiple of the original's (<see cref="Particles.WeatherParticles.Density"/>).</summary>
    public float PrecipitationDensity { get; set; } = 1;

    /// <summary>How the sky places its objects: no longer on the options menu, the user's choice, but kept for F6 and <c>--sky</c>.</summary>
    public SkyPlacement SkyPlacement { get; set; } = SkyPlacement.Projected;

    /// <summary>Whether trek runs at a faster pace than the original's, which feels slow on a PC.</summary>
    public bool BriskPace { get; set; }

    /// <summary>
    /// How the lasting spells' and potions' scenes over heads take their turns in a battle: one at a time across the battle, as the
    /// original's, or each character's at once (<see cref="Combat.SpellFx.Turns"/>).
    /// </summary>
    public Combat.AuraTurns SpellAuras { get; set; } = Combat.AuraTurns.Battle;

    /// <summary>A setting changed.</summary>
    public event Action? Changed;

    /// <summary>Says a setting changed, for whoever carries them out.</summary>
    public void Notify() => Changed?.Invoke();

    /// <summary>A copy of these settings, without whoever hears of this one's changes.</summary>
    public Settings Copy()
    {
        var copy = (Settings)MemberwiseClone();
        copy.Changed = null;
        copy.Controls = Controls.Copy();
        return copy;
    }

    /// <summary>The settings in a file, or the defaults if there's none or it can't be read.</summary>
    public static Settings Load(string path)
    {
        try
        {
            var settings = File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), PluginJson.Options) ?? new() : new();
            // Older settings' line counts past 480 (720, 1080 and so on) were the window's own or under it: the window's own now.
            if (settings.WorldLines is not (0 or 240 or 480))
                settings.WorldLines = 0;
            return settings;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        if (Path.GetDirectoryName(Path.GetFullPath(path)) is { } folder)
            Directory.CreateDirectory(folder);
        File.WriteAllText(path, JsonSerializer.Serialize(this, PluginJson.Options) + "\n");
    }

    /// <summary>The player's bindings, on the keyboard and mouse and on a controller (the options' Controls page, ADR 0026).</summary>
    public Aidyn.Core.Controls.ControlBindings Controls { get; set; } = new();

    /// <summary>How far the mouse turns the camera, as a multiple of the drag the runtime always had.</summary>
    public float MouseSensitivity { get; set; } = 1;
}
