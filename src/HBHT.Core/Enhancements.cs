using HBHT.Core.Cameras;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Core.Ui;

namespace HBHT.Core;

/// <summary>What the Enhancements page's Preset row shows: which set of its settings they match, or Custom for neither.</summary>
public enum EnhancementPreset
{
    /// <summary>The N64's look and feel, with the menus through its video out.</summary>
    Original,

    /// <summary>Every enhancement on, at its fullest, but the menus a step smaller rather than smallest.</summary>
    Enhanced,

    /// <summary>Neither: the player's own mix.</summary>
    Custom,
}

/// <summary>
/// The settings on the options' Enhancements page, which its Preset row sets together (<see cref="EnhancementPreset"/>). The preset isn't
/// kept: it's read from the settings each time, so changing a row turns it to Custom, and changing them all back turns it back.
/// </summary>
public sealed record Enhancements(bool ExtendedDrawDistance, float ObjectDistance, bool NearestDetail, float PrecipitationDensity,
    int UiLines, UiFilter UiFilter, bool BriskPace, MovementMode Movement, AuraTurns SpellAuras, CombatTurns CombatTurns, CameraStyle Camera)
{
    /// <summary>The N64's: a new player's settings.</summary>
    public static readonly Enhancements Original = new(false, 1, false, 1, 240, UiFilter.VideoOut, false, MovementMode.Original,
        AuraTurns.Battle, CombatTurns.Original, CameraStyle.Original);

    /// <summary>Every enhancement at its fullest, the user's choice, but for Menu Size at Smaller and the menus through the video out.</summary>
    public static readonly Enhancements Enhanced = new(true, 3, true, 8, 360, UiFilter.VideoOut, true, MovementMode.Modern,
        AuraTurns.EachCharacter, CombatTurns.StartEarly, CameraStyle.Enhanced);

    /// <summary>A preset's settings, or null for Custom, which has none of its own.</summary>
    public static Enhancements? Of(EnhancementPreset preset) => preset switch
    {
        EnhancementPreset.Original => Original,
        EnhancementPreset.Enhanced => Enhanced,
        _ => null,
    };

    /// <summary>The Enhancements page's settings as they are.</summary>
    public static Enhancements From(Settings s) => new(s.ExtendedDrawDistance, s.ObjectDistance, s.NearestDetail, s.PrecipitationDensity,
        s.UiLines, s.UiFilter, s.BriskPace, s.Movement, s.SpellAuras, s.CombatTurns, s.Camera);

    public void ApplyTo(Settings s)
    {
        s.ExtendedDrawDistance = ExtendedDrawDistance;
        s.ObjectDistance = ObjectDistance;
        s.NearestDetail = NearestDetail;
        s.PrecipitationDensity = PrecipitationDensity;
        s.UiLines = UiLines;
        s.UiFilter = UiFilter;
        s.BriskPace = BriskPace;
        s.Movement = Movement;
        s.SpellAuras = SpellAuras;
        s.CombatTurns = CombatTurns;
        s.Camera = Camera;
    }

    /// <summary>The preset these match, or Custom.</summary>
    public EnhancementPreset Preset =>
        SameAs(Original) ? EnhancementPreset.Original : SameAs(Enhanced) ? EnhancementPreset.Enhanced : EnhancementPreset.Custom;

    /// <summary>
    /// The camera the player gets: under Start Early always the enhanced one, whatever the Camera setting kept for when it's Original
    /// again, as the Camera row shows.
    /// </summary>
    private CameraStyle ShownCamera => CombatTurns == CombatTurns.StartEarly ? CameraStyle.Enhanced : Camera;

    /// <summary>Whether these play the same as <paramref name="other"/>: the same, with the camera as each shows it.</summary>
    public bool SameAs(Enhancements other) =>
        this with { Camera = ShownCamera } == other with { Camera = other.ShownCamera };
}
