using HBHT.Core.Combat;
using HBHT.Core.Sky;

namespace HBHT.Core;

/// <summary>
/// What the core plays by of the player's settings, for one tick (ADR 0022): the runtime makes it from <see cref="Settings"/>
/// with the command line's for the run over them, and hands it to every <see cref="GameSession.Tick"/>, which hands it on to
/// what reads a setting: the audio's volumes, the rain and snow, the sky's placement, the player's pace, a battle's spell
/// auras and the credits' screen. Nothing in the core reads <see cref="Settings"/> for itself, so a setting changed between
/// two ticks takes effect on the next, and a headless run plays by the one it's given, <see cref="Original"/> by default.
/// What only the runtime draws, the window, the 3D view, the draw distance and the UI screens' filter, isn't here.
/// </summary>
public sealed record SessionSettings
{
    /// <summary>The original's: its volumes and its look and feel, the settings an enhancement leaves at their default.</summary>
    public static readonly SessionSettings Original = new();

    /// <summary>The player's pace under the Pace setting's Brisk, a multiple of the original's.</summary>
    public const float BriskPace = 1.3f;

    /// <summary>The sound effects' volume, in hundredths (<see cref="Settings.SoundVolume"/>).</summary>
    public int SoundVolume { get; init; } = Settings.MaxVolume;

    /// <summary>The music's volume, in hundredths (<see cref="Settings.MusicVolume"/>).</summary>
    public int MusicVolume { get; init; } = 65;

    /// <summary>How many raindrops and snowflakes fall, as a multiple of the original's (<see cref="Particles.WeatherParticles.Density"/>).</summary>
    public float PrecipitationDensity { get; init; } = 1;

    /// <summary>How the sky places the clouds, sun, moon and stars (<see cref="SkyDirector.Placement"/>).</summary>
    public SkyPlacement SkyPlacement { get; init; } = SkyPlacement.Original;

    /// <summary>
    /// How many animation ticks the player plays per tick, 1 being the original's: trek's (<see cref="Movement.PlayerMotion.Pace"/>), and
    /// the body he steers in a battle's turn (<see cref="Battle.Pace"/>).
    /// </summary>
    public float Pace { get; init; } = 1;

    /// <summary>How a battle's lasting spells' and potions' scenes over heads take their turns (<see cref="SpellFx.Turns"/>).</summary>
    public AuraTurns SpellAuras { get; init; } = AuraTurns.Battle;

    /// <summary>How many lines tall the UI screen is, which the credits lay their cards out on: the console's 240.</summary>
    public int UiLines { get; init; } = 240;

    /// <summary>What the core plays by of these settings: the pace is Brisk's or the original's.</summary>
    public static SessionSettings From(Settings settings) => new()
    {
        SoundVolume = settings.SoundVolume,
        MusicVolume = settings.MusicVolume,
        PrecipitationDensity = settings.PrecipitationDensity,
        SkyPlacement = settings.SkyPlacement,
        Pace = settings.BriskPace ? BriskPace : 1,
        SpellAuras = settings.SpellAuras,
        UiLines = Math.Max(settings.UiLines, 1),
    };
}
