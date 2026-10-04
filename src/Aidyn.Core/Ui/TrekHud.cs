using System.Numerics;
using Aidyn.Core.Flags;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// Trek's HUD (<c>draw_hud_elements</c>, <c>0x80020bd4</c> in 1.0): the sundial at the bottom left, the party's portraits
/// at the bottom centre, and at the bottom right the minimap where the player's chunk has a piece, or else the compass
/// under an outdoor sky. B raises or lowers the portraits, the minimap growing and shrinking with them, and moving
/// lowers them; standing still doesn't raise them.
/// </summary>
public sealed class TrekHud
{
    private readonly Dictionary<RecordId, MinimapRecord> _minimaps;
    private int _ticks;

    /// <param name="size">An image's width and height, which centring a minimap's piece needs.</param>
    public TrekHud(GameState game, Func<RecordId, (int Width, int Height)> size)
    {
        Minimap = new Minimap(game.Minimaps, size);
        _minimaps = game.Plugins.All<MinimapRecord>(RecordTypes.Minimap).ToDictionary(m => m.MapSet);
    }

    public PartyPortraits Portraits { get; } = new();

    public Minimap Minimap { get; }

    /// <summary>
    /// Whether the sundial shows the moon and the sun (<c>Sundial::ToggleSun</c>, <c>ToggleMoon</c>). Crossing from an
    /// outdoor sky into a cave's hides them, and back shows them (<c>TeleportPlayer</c>, <c>0x8000bfd0</c> in 1.0); trek
    /// starting shows them wherever it is (<c>Sundial::Init</c>), so they show in a cave entered by loading or after a
    /// cut-scene. The Detect Moon and Sun Phase spells also show them, which aren't ported.
    /// </summary>
    public bool SunAndMoon { get; private set; } = true;

    private bool? _outdoors;

    /// <summary>
    /// Raises or lowers the portraits, and grows or shrinks the minimap with them (<c>show_portaits</c>,
    /// <c>toggle_show_minimap</c>).
    /// </summary>
    public void Raise(bool up)
    {
        Portraits.Shown = up;
        Minimap.Toggle(up);
    }

    /// <summary>Trek starts, or starts again after a cut-scene (<c>initGameTrek</c>): the portraits come up, and the sun and moon show.</summary>
    public void Start()
    {
        SunAndMoon = true;
        Raise(true);
    }

    /// <summary>B in trek, with no fade, popup or conversation up (<c>Actor::Move</c>): raises or lowers them.</summary>
    public void PressB() => Raise(!Portraits.Shown);

    /// <summary>The player has come into a chunk under an outdoor sky or a cave's, which may hide or show the sun and moon.</summary>
    public void Arrive(bool outdoors)
    {
        if (_outdoors is { } was && was != outdoors)
            SunAndMoon = outdoors;
        _outdoors = outdoors;
    }

    /// <summary>The player is in a map set: its minimap, if it has one, is loaded.</summary>
    public void Enter(MapSetRecord set) => Minimap.Load(_minimaps.GetValueOrDefault(set.Id));

    /// <summary>
    /// A tick with the HUD up, the player in <paramref name="chunk"/> at <paramref name="at"/> in its coordinates. Once a
    /// frame, the minimap chooses its piece and steps, and the portraits slide.
    /// </summary>
    public void Tick(ChunkEntry? chunk, Vector3 at, FlagNetwork flags)
    {
        if (++_ticks % Cadence.FrameTicks != 0)
            return;
        if (chunk is not null)
            Minimap.Choose(chunk.Row, chunk.Column, at, flags);
        Minimap.StepFrame();
        Portraits.StepFrame();
    }

    /// <summary>
    /// What the HUD draws on a screen of <paramref name="width"/> × <paramref name="height"/>, at <paramref name="brightness"/>
    /// (the scene's fade): the sundial, the portraits, and the minimap or, under an outdoor sky (<paramref name="outdoors"/>),
    /// the compass for the camera looking along <paramref name="look"/>. On a screen other than 320 × 240, each keeps its
    /// place from the bottom and from its side, the sundial the left and the compass or minimap the right, and the
    /// portraits stay centred.
    /// </summary>
    public IEnumerable<HudSprite> Draw(GameState game, bool outdoors, Vector2 look, float brightness, int width = 320, int height = 240)
    {
        var portraits = game.Party.Slots.Select(m => m is null ? null : game.Plugins.TryGet<EntityRecord>(m.Entity)?.Portrait).ToList();
        var corner = Minimap.Showing ? Minimap.Draw(brightness) : outdoors ? Compass.Draw(look, brightness) : [];
        var down = height - 240;
        return Shift(Sundial.Draw(game.Clock.Time, brightness, SunAndMoon), 0, down)
            .Concat(Shift(Portraits.Draw(portraits, brightness), (width - 320) / 2, down))
            .Concat(Shift(corner, width - 320, down));
    }

    private static IEnumerable<HudSprite> Shift(IEnumerable<HudSprite> sprites, int x, int y) => sprites.Select(s => s with
    {
        X = s.X + x,
        Y = s.Y + y,
        Clip = s.Clip is { } c ? new ScreenRect(c.Left + x, c.Top + y, c.Right + x, c.Bottom + y) : null,
    });
}
