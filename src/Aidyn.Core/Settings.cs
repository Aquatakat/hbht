using System.Text.Json;
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

    /// <summary>How many lines the 3D world is drawn at, scaled up to the window, or 0 for the window's own.</summary>
    public int WorldLines { get; set; }

    /// <summary>Multisample anti-aliasing's samples: 0 (off), 2, 4 or 8.</summary>
    public int Antialiasing { get; set; }

    public bool Fullscreen { get; set; }

    public bool VSync { get; set; } = true;

    /// <summary>How far scene objects are drawn, as a multiple of the original's distances.</summary>
    public float DrawDistance { get; set; } = 1;

    /// <summary>Whether scene objects always draw their nearest, most detailed level.</summary>
    public bool NearestDetail { get; set; }

    /// <summary>How many raindrops and snowflakes fall, as a multiple of the original's (<see cref="Particles.WeatherParticles.Density"/>).</summary>
    public float PrecipitationDensity { get; set; } = 1;

    public SkyPlacement SkyPlacement { get; set; } = SkyPlacement.Projected;

    /// <summary>Whether trek runs at a faster pace than the original's, which feels slow on a PC.</summary>
    public bool BriskPace { get; set; }

    /// <summary>A setting changed.</summary>
    public event Action? Changed;

    /// <summary>Says a setting changed, for whoever carries them out.</summary>
    public void Notify() => Changed?.Invoke();

    /// <summary>The settings in a file, or the defaults if there's none or it can't be read.</summary>
    public static Settings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), PluginJson.Options) ?? new() : new();
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
}
