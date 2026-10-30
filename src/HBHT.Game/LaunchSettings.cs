using HBHT.Core;
using HBHT.Core.Combat;
using HBHT.Core.Sky;

namespace HBHT.Game;

/// <summary>
/// The settings the command line sets for one run (<c>--sky</c>, <c>--object-distance</c>, <c>--draw-distance</c>, <c>--detail</c>,
/// <c>--precipitation</c>, <c>--ui-lines</c>, <c>--spell-auras</c>, <c>--pace</c> and <c>--movement</c>): a layer over the player's settings,
/// which the runtime draws by (<see cref="Over"/>) and makes each tick's <see cref="SessionSettings"/> from
/// (<see cref="Session"/>), never written into them, so <c>settings.json</c> only ever holds the player's own. Each holds for
/// the run until the player changes that setting, in the options menu or with a debug key (<see cref="Changed"/>), and then the
/// player's holds: <c>--pace</c> gives way to the Pace setting.
/// </summary>
public sealed class LaunchSettings
{
    private SkyPlacement? _sky;
    private float? _objectDistance;
    private bool? _extendedDrawDistance;
    private bool? _nearestDetail;
    private float? _precipitation;
    private int? _uiLines;
    private AuraTurns? _spellAuras;
    private float? _pace;
    private Core.Movement.MovementMode? _movement;

    /// <summary>The player's settings as last seen, which a change is told from.</summary>
    private Settings _seen;

    /// <param name="player">The player's settings, as the run starts.</param>
    public LaunchSettings(LaunchOptions options, Settings player)
    {
        _sky = options.SkyPlacement;
        _objectDistance = options.ObjectDistance;
        _extendedDrawDistance = options.ExtendedDrawDistance;
        _nearestDetail = options.NearestDetail;
        _precipitation = options.PrecipitationDensity;
        _uiLines = options.UiLines;
        _spellAuras = options.SpellAuras;
        _pace = options.Pace;
        _movement = options.Movement;
        _seen = player.Copy();
    }

    /// <summary>The player's settings with this run's over them: what the runtime draws by.</summary>
    public Settings Over(Settings player)
    {
        var settings = player.Copy();
        if (_sky is { } sky)
            settings.SkyPlacement = sky;
        if (_objectDistance is { } objectDistance)
            settings.ObjectDistance = objectDistance;
        if (_extendedDrawDistance is { } extended)
            settings.ExtendedDrawDistance = extended;
        if (_nearestDetail is { } nearest)
            settings.NearestDetail = nearest;
        if (_precipitation is { } precipitation)
            settings.PrecipitationDensity = precipitation;
        if (_uiLines is { } lines)
            settings.UiLines = lines;
        if (_spellAuras is { } auras)
            settings.SpellAuras = auras;
        if (_movement is { } movement)
            settings.Movement = movement;
        return settings;
    }

    /// <summary>What the core plays by: the player's settings with this run's over them, and <c>--pace</c>'s pace.</summary>
    public SessionSettings Session(Settings player)
    {
        var settings = SessionSettings.From(Over(player));
        return _pace is { } pace ? settings with { Pace = pace } : settings;
    }

    /// <summary>The player's settings changed: this run's setting for each one that did gives way to the player's.</summary>
    public void Changed(Settings player)
    {
        if (player.SkyPlacement != _seen.SkyPlacement)
            _sky = null;
        if (player.ObjectDistance != _seen.ObjectDistance)
            _objectDistance = null;
        if (player.ExtendedDrawDistance != _seen.ExtendedDrawDistance)
            _extendedDrawDistance = null;
        if (player.NearestDetail != _seen.NearestDetail)
            _nearestDetail = null;
        if (player.PrecipitationDensity != _seen.PrecipitationDensity)
            _precipitation = null;
        if (player.UiLines != _seen.UiLines)
            _uiLines = null;
        if (player.SpellAuras != _seen.SpellAuras)
            _spellAuras = null;
        if (player.BriskPace != _seen.BriskPace)
            _pace = null;
        if (player.Movement != _seen.Movement)
            _movement = null;
        _seen = player.Copy();
    }
}
