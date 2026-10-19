using System.Numerics;
using Aidyn.Core.Combat;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>What a portrait's entry is (<c>CombatGui.Portraits</c>' <c>+0x0B</c>).</summary>
public enum PortraitState : byte
{
    /// <summary>Not in play yet: a reserve, or a combatant before its body is made.</summary>
    Waiting,

    /// <summary>Out of the fight: a party member dead, an enemy swapped out of its slot, or a reserve that fled as it came.</summary>
    Gone,

    /// <summary>A party member in play.</summary>
    Party,

    /// <summary>An enemy in play.</summary>
    Enemy,

    /// <summary>An enemy dead or fled, under its skull until a reserve takes its slot.</summary>
    Fallen,

    /// <summary>A spare with no one in it yet, for whoever is in neither list (a summons, Sholeh fighting with the party).</summary>
    Placeholder,
}

/// <summary>
/// A frame a portrait sits in (<c>CombatGui.Bars</c>, 0x18 bytes each): a large one in a column (28 × 26, image
/// <c>0x01C6</c>, its portrait 23 square) or a reserve's small one (14 × 13, <c>0x01C7</c>, its portrait 11 × 10).
/// Positions are in the original's 320 × 240 frame.
/// </summary>
public sealed class CombatBar
{
    internal CombatBar(bool party, bool large, int frameX, int frameY, bool spare)
    {
        Party = party;
        Large = large;
        FrameX = frameX;
        FrameY = frameY;
        Blocked = Hidden = spare;
    }

    /// <summary>Whether it's on the party's side, at the top right; else the enemies', at the top left.</summary>
    public bool Party { get; }

    /// <summary>Whether it's a large frame, or a reserve's small one.</summary>
    public bool Large { get; }

    public int FrameX { get; }
    public int FrameY { get; }

    /// <summary>Its frame's image.</summary>
    public RecordId Frame => Large ? CombatGui.LargeFrame : CombatGui.SmallFrame;

    public int FrameWidth => Large ? 28 : 14;
    public int FrameHeight => Large ? 26 : 13;

    /// <summary>
    /// Its portrait's place in it (<c>+0x04</c>, <c>+0x06</c>): large ones 2 down and 2 in from the side away from the HP bar's
    /// strip (the party's strip is on the left, the enemies' on the right), small ones 1 in.
    /// </summary>
    public int X => FrameX + (Large ? Party ? 4 : 2 : 1);

    public int Y => FrameY + (Large ? 2 : 1);

    /// <summary>Its portrait's size (<c>+0x08</c>, <c>+0x0A</c>).</summary>
    public int Width => Large ? 23 : 11;

    public int Height => Large ? 23 : 10;

    /// <summary>Whether no one has taken it (<c>+0x0C</c>).</summary>
    public bool Free { get; internal set; } = true;

    /// <summary>Whether it's one of the two spares under the party, kept for a placeholder (<c>+0x14</c>).</summary>
    public bool Blocked { get; internal set; }

    /// <summary>Whether its frame is hidden: the spares are, until they're used (the frame widget's <c>+0x7C</c>).</summary>
    public bool Hidden { get; internal set; }

    /// <summary>The frame's colour: black at 200 but where a tint has it (<see cref="CombatGui.Tint"/>).</summary>
    public Rgba Tint { get; internal set; } = CombatGui.FrameColor;
}

/// <summary>
/// A portrait's entry (<c>CombatGui.Portraits</c>, 0x18 bytes each): whose it is, which combatant's slot it follows, which
/// frame it's in, and the portrait's own place and size, which slide into a frame over a second, with the skull that fades
/// in over it once it's dead or fled.
/// </summary>
public sealed class CombatPortrait
{
    internal CombatPortrait(RecordId? entity, bool party, PortraitState state, int x, int y, int size)
    {
        Entity = entity;
        Party = party;
        State = state;
        (X, Y, Width, Height) = (x, y, size, size);
    }

    /// <summary>Whose portrait it is, or none for a placeholder not used yet (<c>+0x08</c>).</summary>
    public RecordId? Entity { get; internal set; }

    /// <summary>Which side it was made for (<c>+0x0C</c>): the party's members, else the enemy list's.</summary>
    public bool Party { get; }

    /// <summary>The combatant's slot it follows, once in play (<c>+0x0A</c>).</summary>
    public int? Slot { get; internal set; }

    public PortraitState State { get; internal set; }

    /// <summary>The frame it's in (<c>+0x10</c>).</summary>
    public CombatBar? Bar { get; internal set; }

    /// <summary>Whether it fled, rather than died (<c>+0x14</c>): its frame turns orange.</summary>
    public bool Fled { get; internal set; }

    /// <summary>Where it's drawn now, and how big, in the 320 × 240 frame.</summary>
    public int X { get; private set; }

    public int Y { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Whether it has the skull over it, and the skull's alpha.</summary>
    public bool Skull { get; private set; }

    public byte SkullAlpha => _skull is null ? (byte)0 : (byte)MenuTween.Value(0, 255, (float)_skull.Value / CombatGui.TweenTicks);

    private (int X, int Y, int Width, int Height, int Ticks)? _from;
    private int? _skull;

    /// <summary>Whether it's sliding to its frame.</summary>
    public bool Sliding => _from is not null;

    /// <summary>
    /// Starts it toward its frame (<c>CombatGui.PlaceInBar</c>, <c>0x800869F4</c>): four tweens of 60 ticks, x in a straight
    /// line, y easing out, and the width and height easing in and out.
    /// </summary>
    internal void SlideTo(CombatBar bar)
    {
        Bar = bar;
        _from = (X, Y, Width, Height, 0);
    }

    /// <summary>The skull over it, fading in from nothing over 60 ticks (<c>CombatGui.AddSkull</c>, <c>0x800873F0</c>).</summary>
    internal void AddSkull()
    {
        Skull = true;
        _skull = 0;
    }

    /// <summary>A tick of its tweens (<c>MenuTween.ListStep</c> with the frame's delta).</summary>
    internal void Tick()
    {
        if (_skull is < CombatGui.TweenTicks)
            _skull++;
        if (_from is not { } start || Bar is not { } bar)
            return;
        var ticks = Math.Min(start.Ticks + 1, CombatGui.TweenTicks);
        X = MenuTween.Value(start.X, bar.X, (float)ticks / CombatGui.TweenTicks);
        Y = MenuTween.Value(start.Y, bar.Y, MenuTween.EaseOut(ticks, CombatGui.TweenTicks));
        Width = MenuTween.Value(start.Width, bar.Width, MenuTween.Ease(ticks, CombatGui.TweenTicks));
        Height = MenuTween.Value(start.Height, bar.Height, MenuTween.Ease(ticks, CombatGui.TweenTicks));
        _from = ticks >= CombatGui.TweenTicks ? null : start with { Ticks = ticks };
    }
}

/// <summary>Where the radar sits: in the log box, as the original has it, or in the screen's bottom right corner.</summary>
public enum RadarAnchor
{
    /// <summary>In the log box at the bottom centre, at the original's (274, 199).</summary>
    LogBox,

    /// <summary>The same place from the screen's bottom right corner, which on a wide screen is clear of the box.</summary>
    Corner,
}

/// <summary>
/// The battle screen (<c>combatgui.cpp</c>, <c>0x80085A80</c>–<c>0x80087C60</c> in 1.0, and the radar,
/// <c>0x8008DD30</c>–<c>0x8008E3C0</c>): the enemies' portraits down the top left and the party's down the top right, each
/// in its frame with its hit points beside it and tinted by the target sets, the reserves' small ones under the enemies',
/// the log box across the foot with the current combatant's portrait and the log's line, and the radar. The battle holds
/// it (<see cref="Battle.Gui"/>) and steps it each tick; the runtime draws what <see cref="Draw"/> lays out. See
/// docs/systems/ui.md's battle screen.
/// </summary>
/// <remarks>
/// C-left toggles a flag in the original (<c>CombatGui.Shown</c>, <c>0x800E3140</c>) that nothing reads, so it hides
/// nothing, and the port has no flag.
/// </remarks>
public sealed class CombatGui
{
    /// <summary>A large frame (28 × 26) and a small one (14 × 13), opaque: a white ring, a black one and grey within.</summary>
    public static readonly RecordId LargeFrame = HudSprite.Id(0x01C6), SmallFrame = HudSprite.Id(0x01C7);

    /// <summary>The skull over the dead and fled (40 square, sized to the portrait).</summary>
    public static readonly RecordId SkullImage = HudSprite.Id(0x016D);

    /// <summary>A portrait for an entity without one (<c>Entity.PortraitImage</c>, <c>0x8006C2D4</c>).</summary>
    public static readonly RecordId NoPortrait = HudSprite.Id(0x0125);

    /// <summary>The ticks a portrait takes to slide into its frame, and the skull to fade in (<c>0x800D43E4</c>, 60.0).</summary>
    public const int TweenTicks = 60;

    /// <summary>The columns' frames: the enemies' at x 21 (and a second column of small ones at 35), the party's at 270.</summary>
    public const int EnemyColumn = 21, EnemyColumn2 = 35, PartyColumn = 270;

    /// <summary>The large frames' tops, 28 apart from 18, and the small ones', 12 apart from 130.</summary>
    public const int Top = 18, Pitch = 28, SmallTop = 130, SmallPitch = 12;

    /// <summary>Where every portrait starts, at its image's size, before it slides into its frame (<c>0x80086464</c>).</summary>
    public const int StartX = 160, StartY = 120, StartSize = 40, PlaceholderSize = 32;

    /// <summary>An HP bar's x from its frame's portrait: the party's 4 left of it, an enemy's 24 right (<c>0x800D43E8</c>).</summary>
    public const int PartyBarOffset = -4, EnemyBarOffset = 24, BarWidth = 3;

    /// <summary>The frames' colours (<c>CombatGui.TintAll</c>, <c>0x8008713C</c>), each later one over the earlier.</summary>
    public static readonly Rgba FrameColor = new(0, 0, 0, 200),
        FledColor = new(200, 164, 0, 255),
        AttackCandidateColor = new(0, 200, 0, 150),
        SpellCandidateColor = new(200, 0, 0, 150),
        AttackSelectedColor = new(0, 255, 0, 255),
        SpellSelectedColor = new(255, 0, 0, 255);

    private readonly List<CombatPortrait> _portraits = [];
    private readonly List<CombatBar> _bars = [];
    private bool _firstDraw = true;
    private readonly FrameGate _frame = new(Cadence.FrameTicks);

    /// <summary>The portraits' entries, in the original's order: the party's members, the two placeholders, then the enemy list.</summary>
    public IReadOnlyList<CombatPortrait> Portraits => _portraits;

    /// <summary>The frames: the party's (and two hidden spares), then the enemies' by the list, the first four large.</summary>
    public IReadOnlyList<CombatBar> Bars => _bars;

    /// <summary>The radar.</summary>
    public CombatRadar Radar { get; } = new();

    /// <summary>The entity the log box's portrait is for (<c>0x800E3164</c>), the current combatant's.</summary>
    public RecordId? LogPortrait { get; private set; }

    /// <summary>
    /// The state the tints are worked out for, in place of the battle's, for the runtime's debug key while nothing chooses
    /// targets; null for the battle's own.
    /// </summary>
    public CombatState? TintState { get; set; }

    /// <summary>
    /// The screen for a battle with <paramref name="party"/>'s members and <paramref name="enemies"/> (<c>CombatGui.Create</c>,
    /// <c>0x80085B80</c>): an entry for each member, two placeholders and an entry for each of the list, reserves too, all
    /// waiting at the screen's middle; a frame for each member and two hidden spares under them, and one for each of the
    /// list, the first four large and the rest small in two columns.
    /// </summary>
    public CombatGui(IReadOnlyList<RecordId> party, IReadOnlyList<RecordId> enemies)
    {
        foreach (var member in party)
            _portraits.Add(new CombatPortrait(member, true, PortraitState.Waiting, StartX, StartY, StartSize));
        for (var i = 0; i < 2; i++)
            _portraits.Add(new CombatPortrait(null, true, PortraitState.Placeholder, StartX, StartY, PlaceholderSize));
        foreach (var enemy in enemies)
            _portraits.Add(new CombatPortrait(enemy, false, PortraitState.Waiting, StartX, StartY, StartSize));

        // CombatGui.CreateBars, 0x800864F4.
        for (var i = 0; i < party.Count + 2; i++)
            _bars.Add(new CombatBar(true, true, PartyColumn, Top + Pitch * i, spare: i >= party.Count));
        for (var i = 0; i < enemies.Count; i++)
        {
            _bars.Add(i switch
            {
                < Battle.OnField => new CombatBar(false, true, EnemyColumn, Top + Pitch * i, false),
                < 2 * Battle.OnField => new CombatBar(false, false, EnemyColumn, SmallTop + SmallPitch * (i - Battle.OnField), false),
                _ => new CombatBar(false, false, EnemyColumn2, SmallTop + SmallPitch * (i - 2 * Battle.OnField), false),
            });
        }
    }

    /// <summary>
    /// The screen for <paramref name="battle"/>: its party's members (or, with no game, its party's combatants), its enemy
    /// list, and every combatant's portrait put in its frame in slot order, as each one's body is made
    /// (<c>Combat.CreateBody</c>, <c>0x8005C474</c>).
    /// </summary>
    public static CombatGui For(Battle battle)
    {
        var party = battle.Game is { } game
            ? game.Party.Slots.OfType<Characters.Character>().Select(m => m.Entity).ToList()
            : battle.Combatants.Where(c => c.Slot < battle.PartySize).Select(c => c.Entity).ToList();
        var gui = new CombatGui(party, battle.Enemies);
        foreach (var combatant in battle.Combatants)
            gui.Assign(combatant.Entity, combatant.Slot, enemy: combatant.Slot >= battle.PartySize);
        return gui;
    }

    /// <summary>
    /// A combatant's body is made, or a reserve joins (<c>CombatGui.AssignPortrait</c>, <c>0x80085FC8</c>): the first
    /// waiting entry of its entity takes its slot and slides into the first free frame of its side. With none free, a
    /// joining enemy swaps frames with the first dead or fled one, which goes out of the fight into the joiner's small
    /// frame. An entity in neither list takes a placeholder and the first spare frame under the party.
    /// </summary>
    public void Assign(RecordId entity, int slot, bool enemy)
    {
        if (_portraits.FirstOrDefault(p => p.State == PortraitState.Waiting && p.Entity == entity) is { } entry)
        {
            entry.State = enemy ? PortraitState.Enemy : PortraitState.Party;
            entry.Slot = slot;
            if (FreeBar(enemy) is { } bar)
            {
                Place(bar, entry);
            }
            else if (enemy && _portraits.FirstOrDefault(p => p.State == PortraitState.Fallen) is { } fallen && fallen.Bar is { } large)
            {
                fallen.State = PortraitState.Gone;
                var small = entry.Bar;
                if (small is not null)
                    Place(small, fallen);
                Place(large, entry);
            }
            return;
        }
        if (_portraits.FirstOrDefault(p => p.State == PortraitState.Placeholder) is { } spare)
        {
            spare.Entity = entity;
            spare.Slot = slot;
            spare.State = enemy ? PortraitState.Enemy : PortraitState.Party;
            if (_bars.FirstOrDefault(b => b.Blocked) is { } bar)
                Place(bar, spare);
        }
    }

    /// <summary>The first free frame of a side (<c>CombatGui.FindFreeBar</c>, <c>0x80086984</c>).</summary>
    private CombatBar? FreeBar(bool enemy) => _bars.FirstOrDefault(b => b.Party != enemy && b.Free && !b.Blocked);

    /// <summary>Takes a frame for an entry and slides it in (<c>CombatGui.PlaceInBar</c>, <c>0x800869F4</c>).</summary>
    private static void Place(CombatBar bar, CombatPortrait entry)
    {
        bar.Free = false;
        bar.Blocked = false;
        bar.Hidden = false;
        entry.SlideTo(bar);
    }

    /// <summary>
    /// A combatant's body has gone, dead (<c>CombatGui.MarkDead</c>, <c>0x80086208</c>, as <c>Combat.FreeBody</c> frees
    /// it) or fled (<c>MarkFled</c>, <c>0x80086230</c>, as <c>Combatant.Leave</c> ends its flight): the skull fades in over
    /// its portrait, which stays in its frame; an enemy's is fallen, and a fled one's frame turns orange, and a party
    /// member's is gone.
    /// </summary>
    public void MarkGone(int slot, bool fled = false)
    {
        // CombatGui.MarkGone, 0x80086258.
        if (_portraits.FirstOrDefault(p => p.Slot == slot && p.State is PortraitState.Party or PortraitState.Enemy) is not { } entry)
            return;
        entry.AddSkull();
        if (entry.State == PortraitState.Enemy)
        {
            entry.State = PortraitState.Fallen;
            entry.Fled = fled;
        }
        else
        {
            entry.State = PortraitState.Gone;
        }
    }

    /// <summary>
    /// A reserve fled as it came (<c>CombatGui.MarkReserveFled</c>, <c>0x80086338</c>): its small portrait gets the skull
    /// and an orange frame, out of the fight.
    /// </summary>
    public void MarkReserveFled(RecordId entity)
    {
        if (_portraits.FirstOrDefault(p => p.State == PortraitState.Waiting && p.Entity == entity) is not { } entry)
            return;
        entry.State = PortraitState.Gone;
        entry.Fled = true;
        entry.AddSkull();
    }

    /// <summary>
    /// A tick (<c>CombatGui.Draw</c>, <c>0x80085F64</c>): the first gives the reserves their small frames in the list's
    /// order; then the portraits' tweens step, the radar's pulse once a frame, the log box's portrait follows whose turn it
    /// is, and the frames are tinted.
    /// </summary>
    public void Tick(Battle battle)
    {
        if (_firstDraw)
        {
            // CombatGui.AssignBarsFirstDraw, 0x80086C84.
            _firstDraw = false;
            foreach (var entry in _portraits.Where(p => p.Bar is null && p.State != PortraitState.Placeholder))
            {
                if (FreeBar(enemy: !entry.Party) is { } bar)
                    Place(bar, entry);
            }
        }
        foreach (var entry in _portraits)
            entry.Tick();
        if (_frame.Tick())
            Radar.StepFrame();
        if (battle.Lines.Locked)
            LogPortrait = battle.Lines.LockedPortrait ?? LogPortrait;
        else if (battle.Current is { } current)
            LogPortrait = current.Entity;
        Tint(battle);
    }

    /// <summary>
    /// The frames' colours (<c>CombatGui.TintAll</c>, <c>0x8008713C</c>): all black at 200; a fled one's orange; then for
    /// each in play by its combatant (<c>CombatGui.TintByState</c>, <c>0x80086EB4</c>), each later rule over the earlier:
    /// orange while the AI has it fleeing, dim green as set A's candidate, dim red as set B's, bright green as set A's
    /// selected while an attack or a flask is aimed (states 9 and 18), and bright red as set B's selected while a spell or
    /// its area is (10 and 11). Side doesn't matter: a party member a healing spell could reach is dim red.
    /// </summary>
    public void Tint(Battle battle)
    {
        foreach (var bar in _bars)
            bar.Tint = FrameColor;
        foreach (var entry in _portraits.Where(p => p.Fled && p.Bar is not null))
            entry.Bar!.Tint = FledColor;
        var state = TintState ?? battle.State;
        foreach (var entry in _portraits)
        {
            if (entry is not { State: PortraitState.Party or PortraitState.Enemy, Bar: { } bar, Slot: { } slot })
                continue;
            var marks = battle.Marks(slot);
            if (battle.Slots[slot] is { Fleeing: true })
                bar.Tint = FledColor;
            if (marks.HasFlag(TargetMarks.AttackCandidate))
                bar.Tint = AttackCandidateColor;
            if (marks.HasFlag(TargetMarks.SpellCandidate))
                bar.Tint = SpellCandidateColor;
            if (marks.HasFlag(TargetMarks.AttackSelected | TargetMarks.AttackCandidate) && state is CombatState.TargetAttack or CombatState.TargetThrown)
                bar.Tint = AttackSelectedColor;
            if (marks.HasFlag(TargetMarks.SpellSelected | TargetMarks.SpellCandidate) && state is CombatState.TargetSpell or CombatState.PlaceArea)
                bar.Tint = SpellSelectedColor;
        }
    }

    /// <summary>
    /// An HP bar's colour (<c>CombatGui.HpColour</c>, <c>0x80085A80</c>): from green at full through (128, 127, 0) at half
    /// to red, with no steps.
    /// </summary>
    public static Rgba HpColor(int hp, int max)
    {
        var u = max <= 0 ? 0 : Math.Min(255, hp * 255 / max);
        return new Rgba((byte)(255 - u), (byte)u, 0, 255);
    }

    /// <summary>
    /// What the screen draws on a screen of <paramref name="width"/> × <paramref name="height"/>, in the order it's drawn:
    /// the log box, the radar, the frames, the portraits with their skulls, and the HP bars.
    /// </summary>
    /// <remarks>
    /// Laid out in the original's frame, then placed on the screen as the user chose: the enemies' column from the top
    /// left corner, the party's from the top right, the log box floating at the bottom centre as the conversation and
    /// caption boxes do, and the radar in the log box or in the bottom right corner (<paramref name="radar"/>).
    /// </remarks>
    public CombatScreen Draw(Battle battle, Func<RecordId, RecordId?> portrait, RadarAnchor radar = RadarAnchor.LogBox,
        int width = 320, int height = 240)
    {
        var right = (width - DialogueBox.FrameWidth, 0);
        var (centreX, foot) = ((width - DialogueBox.FrameWidth) / 2, height - DialogueBox.FrameHeight);
        (int X, int Y) Side(CombatBar bar) => bar.Party ? right : (0, 0);

        var log = CombatLogBox.Layout(battle.Lines.Text, LogPortrait is { } who ? portrait(who) ?? NoPortrait : null, width, height);
        var radarAt = radar == RadarAnchor.LogBox ? (centreX, foot) : (width - DialogueBox.FrameWidth, foot);
        var dots = Radar.Draw(battle, radarAt.Item1, radarAt.Item2, TintState);

        var frames = new List<HudSprite>();
        foreach (var bar in _bars.Where(b => !b.Hidden))
        {
            var (x, y) = Side(bar);
            frames.Add(new HudSprite(bar.Frame, bar.FrameX + x, bar.FrameY + y, 1, bar.Tint, (bar.FrameWidth, bar.FrameHeight)));
        }

        var portraits = new List<HudSprite>();
        var bars = new List<HudFill>();
        foreach (var entry in _portraits)
        {
            if (entry.Entity is not { } entity || entry.Bar is not { } bar)
                continue;
            var (x, y) = Side(bar);
            var image = portrait(entity) ?? NoPortrait;
            portraits.Add(new HudSprite(image, entry.X + x, entry.Y + y, 1, White, (entry.Width, entry.Height)));
            if (entry.Skull)
                portraits.Add(new HudSprite(SkullImage, entry.X + x, entry.Y + y, 1, White with { A = entry.SkullAlpha }, (entry.Width, entry.Height)));
        }
        // CombatGui.DrawHpBars, 0x80087168: over everything, at the frame's place at once, from the live hit points.
        foreach (var entry in _portraits)
        {
            if (entry is not { State: PortraitState.Party or PortraitState.Enemy, Bar: { } bar, Slot: { } slot } || battle.Slots[slot] is not { } combatant)
                continue;
            var (x, y) = Side(bar);
            var left = bar.X + (entry.State == PortraitState.Party ? PartyBarOffset : EnemyBarOffset) + x;
            var (hp, max) = (combatant.Sheet.Hp, combatant.Sheet.MaxHp);
            var fill = (int)(bar.Height * Math.Min(1f, max <= 0 ? 0 : (float)hp / max));
            var top = bar.Y + bar.Height - fill;
            if (top == bar.Y + bar.Height)
                top--;
            bars.Add(new HudFill(left, bar.Y + y, BarWidth, bar.Height, new Rgba(0, 0, 0, 255)));
            bars.Add(new HudFill(left, top + y, BarWidth, bar.Y + bar.Height - top, HpColor(hp, max)));
        }
        return new CombatScreen(log, Radar.Ring(radarAt.Item1, radarAt.Item2), dots, frames, portraits, bars);
    }

    private static readonly Rgba White = new(255, 255, 255, 255);
}

/// <summary>A filled rectangle on the UI screen (<c>Screen.FillRect</c>, <c>0x8009CBE8</c>), its colour blended by its alpha.</summary>
public readonly record struct HudFill(int X, int Y, int Width, int Height, Rgba Color);

/// <summary>What the battle screen draws, in order: the log box, the radar's ring and dots, the frames, the portraits and the HP bars.</summary>
public sealed record CombatScreen(
    CombatLogBoxLayout Log,
    HudSprite RadarRing,
    IReadOnlyList<HudFill> RadarDots,
    IReadOnlyList<HudSprite> Frames,
    IReadOnlyList<HudSprite> Portraits,
    IReadOnlyList<HudFill> HpBars);

/// <summary>
/// The battle's radar (<c>CombatRadar</c>, <c>0x8008DD30</c>–<c>0x8008E3C0</c>): the sundial's ring at alpha 164, and a
/// 2 × 2 dot for every body, the camera's owner yellow at the middle, the party green and the rest red, half a pixel to a
/// unit, turned so the camera looks up the radar, and those over 20 pixels out held at 20 and dimmed.
/// </summary>
public sealed class CombatRadar
{
    /// <summary>The ring, the sundial's (60 square).</summary>
    public static readonly RecordId RingImage = HudSprite.Id(0x001B);

    /// <summary>The radar's place: its dots' top left for a body where the camera's owner is (<c>CombatRadar.Create</c>).</summary>
    public const int X = 274, Y = 199;

    /// <summary>Its alpha, the ring's and the dots'.</summary>
    public const byte Alpha = 164;

    /// <summary>Pixels a unit (<c>0x800D5150</c>), and how far out a dot may be (<c>0x800D5154</c>).</summary>
    public const float Scale = 0.5f, Radius = 20;

    public static readonly Rgba Owner = new(150, 150, 0, Alpha), Party = new(0, 150, 0, Alpha), Others = new(150, 0, 0, Alpha);

    /// <summary>The pulse of a spell's targets (<c>+0x80</c>), from 0 down to −150 and back, 4 a frame.</summary>
    public int Pulse { get; private set; }

    private int _step = -4;

    /// <summary>A frame (<c>CombatRadar.Step</c>, <c>0x8008DE44</c>): the pulse moves 4, turning at −150 and 0, 76 frames a cycle.</summary>
    public void StepFrame()
    {
        Pulse += _step;
        if (Pulse < -150)
        {
            Pulse = -150;
            _step = -_step;
        }
        else if (Pulse > 0)
        {
            Pulse = 0;
            _step = -_step;
        }
    }

    /// <summary>The ring, its frame's place moved by (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public HudSprite Ring(int x, int y) => new(RingImage, X - 30 + 1 + x, Y - 30 + 1 + y, 1, new Rgba(255, 255, 255, Alpha));

    /// <summary>
    /// The dots (<c>CombatRadar.Draw</c>, <c>0x8008DF04</c>), by slot, around the camera's owner (<see cref="Battle.CameraOwner"/>):
    /// the current combatant, or the cursor's target while an attack is aimed. A body gone from the field has none. While a target is chosen (states 10, 11 and
    /// 18), the dots of set B's selected pulse and the rest are dimmed. The frame's place is moved by
    /// (<paramref name="x"/>, <paramref name="y"/>).
    /// </summary>
    public IReadOnlyList<HudFill> Draw(Battle battle, int x, int y, CombatState? state = null)
    {
        var dots = new List<HudFill>();
        if ((battle.CameraOwner is { Freed: false } o ? o : battle.Current) is not { } owner)
            return dots;
        var look = battle.CombatCamera is { } camera ? camera.Camera.LookXZ : Vector2.UnitX;
        var centre = owner.Body.Sphere.Position;
        var targeting = (state ?? battle.State) is CombatState.TargetSpell or CombatState.PlaceArea or CombatState.TargetThrown;
        foreach (var c in battle.Combatants.Where(c => !c.Freed))
        {
            var (dx, dy, far) = Place(c.Body.Sphere.Position - centre, look);
            var color = c == owner ? Owner : c.Slot < battle.PartySize ? Party : Others;
            if (far)
                color = Halve(color);
            if (targeting)
                color = battle.Marks(c.Slot).HasFlag(TargetMarks.SpellSelected) ? Throb(color) : Halve(color);
            dots.Add(new HudFill(X + dx + x, Y - dy + y, 2, 2, color));
        }
        return dots;

        static Rgba Halve(Rgba c) => c with { R = (byte)(c.R >> 1), G = (byte)(c.G >> 1), B = (byte)(c.B >> 1) };
    }

    /// <summary>
    /// Where a body <paramref name="offset"/> from the camera's owner goes on the radar, in pixels right and up from the
    /// middle, with the camera looking along <paramref name="look"/> (its X and Z) up the radar: half a pixel a unit, and
    /// one more than 20 out held at 20 (<paramref name="far"/>), each truncated toward zero.
    /// </summary>
    public static (int X, int Y, bool Far) Place(Vector3 offset, Vector2 look)
    {
        var (a, b) = (look.X, look.Y);
        var at = new Vector2((offset.X * -b + offset.Z * a) * Scale, (offset.X * a + offset.Z * b) * Scale);
        var far = at.Length() > Radius;
        if (far)
            at = Vector2.Normalize(at) * Radius;
        return ((int)at.X, (int)at.Y, far);
    }

    /// <summary>A target's dot: each channel that isn't 0 plus the pulse's low byte, as the original adds it.</summary>
    private Rgba Throb(Rgba c) => c with
    {
        R = c.R == 0 ? (byte)0 : (byte)(c.R + Pulse),
        G = c.G == 0 ? (byte)0 : (byte)(c.G + Pulse),
        B = c.B == 0 ? (byte)0 : (byte)(c.B + Pulse),
    };
}
