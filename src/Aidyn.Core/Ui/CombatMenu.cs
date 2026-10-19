using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>What a row of combat's held menus does as it's chosen.</summary>
public enum CombatMenuChoice
{
    /// <summary>The Healer's Lay On Hands: an ally in reach to heal (state 9).</summary>
    LayOnHands,

    /// <summary>The Healer's Heal With Herbs: the healer or an ally in reach (state 9).</summary>
    HealWithHerbs,

    /// <summary>The Troubadour's performance (state 25).</summary>
    Perform,

    /// <summary>A spell the combatant knows (state 10 or 11).</summary>
    Spell,

    /// <summary>A potion to drink (state 9, the drinker alone).</summary>
    Potion,

    /// <summary>A worn item's spell (state 10 or 11).</summary>
    ItemSpell,
}

/// <summary>
/// A row of a held menu (<c>CombatMenu.MakeEntry</c>, <c>0x80088714</c>): its icon at its left, its text after, in its colour, and what
/// choosing it does.
/// </summary>
/// <param name="Spell">The spell it selects, for <see cref="CombatMenuChoice.Spell"/> and <see cref="CombatMenuChoice.ItemSpell"/>.</param>
/// <param name="Potion">The potion it holds, for <see cref="CombatMenuChoice.Potion"/>.</param>
public sealed record CombatMenuRow(CombatMenuChoice Choice, string Text, RecordId? Icon, Rgba Color, SpellChoice Spell = default, Potion? Potion = null);

/// <summary>
/// Combat's held menus (<c>CombatMenu</c>, <c>0x80087C60</c>–<c>0x80089780</c>, states 22 and 23): C-down's spells and skills, C-up's
/// potions and item spells. They open on the button's press, the stick or the D-pad moves the cursor, wrapping, and letting the
/// button go chooses the row under it; nothing else does anything while one is open. The list is a <c>ChoicePopup</c>: a feathered
/// black panel round a list 120 wide centred on the screen, as tall as its rows up to <see cref="MostHeight"/>, the cursor's row
/// pulsing from grey toward its own colour, with arrows while it scrolls. With nothing to list, a message says so instead.
/// </summary>
public sealed class CombatMenu
{
    /// <summary>The rows' colours: the skills' blue, the spells' red, the potions' green and the item spells' yellow.</summary>
    public static readonly Rgba SkillColor = new(0, 128, 225, 225), SpellColor = new(225, 80, 80, 225), PotionColor = new(0, 200, 0, 225),
        ItemSpellColor = new(200, 200, 0, 225);

    /// <summary>What the cursor's row pulses from (the list's default, <c>WidgetScrollMenu.Create</c>).</summary>
    public static readonly Rgba PulseFrom = new(100, 100, 100, 100);

    /// <summary>The Healer's icon (a red cross), the Troubadour's (a lute), and a potion's (a flask).</summary>
    public static readonly RecordId HealerIcon = HudSprite.Id(0x0185), PerformIcon = HudSprite.Id(0x018C), PotionIcon = HudSprite.Id(0x35E5);

    /// <summary>The scroll arrows, up and down, 5 × 8.</summary>
    public static readonly RecordId UpArrow = HudSprite.Id(0x38D0), DownArrow = HudSprite.Id(0x38CF);

    /// <summary>The schools' icons (<c>Spell.SchoolIcons</c>, <c>0x800DEFC0</c>): none for the unschooled.</summary>
    public static RecordId? SchoolIcon(MagicSchool school) => school switch
    {
        MagicSchool.Chaos => HudSprite.Id(0x01A9),
        MagicSchool.Elemental => HudSprite.Id(0x01A5),
        MagicSchool.Naming => HudSprite.Id(0x01A6),
        MagicSchool.Necromancy => HudSprite.Id(0x01A7),
        MagicSchool.Star => HudSprite.Id(0x01A8),
        _ => null,
    };

    /// <summary>The list's width and centre, its tallest (with the Expansion Pak; 55 without), and the screen's middle it's centred on.</summary>
    public const int Width = 120, CentreX = 160, MostHeight = 95, CentreY = 120;

    /// <summary>The icons' widths the rows' text starts after: the spells' menu's (the lute's) and the items' (the flask's).</summary>
    public const int SpellIconWidth = 10, ItemIconWidth = 16;

    /// <summary>The pulse's length, up and back (<c>WidgetPulseMenu.Step</c>), and how fast the list scrolls to the cursor.</summary>
    public const int PulseSteps = 20, ScrollSpeed = 10;

    /// <summary>The message's width.</summary>
    public const int MessageWidth = 150;

    public CombatMenu(bool items, IReadOnlyList<CombatMenuRow> rows, string? empty, int cursor = -1)
    {
        Items = items;
        Rows = rows;
        Empty = rows.Count == 0 ? empty ?? "" : null;
        if (cursor >= 0 && rows.Count > 0)
            Cursor = Math.Min(cursor, rows.Count - 1);
        _jump = true;
    }

    /// <summary>Whether it's C-up's potions and item spells, rather than C-down's spells and skills.</summary>
    public bool Items { get; }

    public IReadOnlyList<CombatMenuRow> Rows { get; }

    /// <summary>The message shown in its place when there's nothing to list (<c>CombatMenu.MakeEmptyMessage</c>), or null.</summary>
    public string? Empty { get; }

    /// <summary>The row the cursor is on.</summary>
    public int Cursor { get; private set; }

    /// <summary>The row the cursor is on, or null for an empty menu.</summary>
    public CombatMenuRow? Current => Rows.Count == 0 ? null : Rows[Cursor];

    /// <summary>The cursor's pulse, 0 to <see cref="PulseSteps"/> − 1, up then back.</summary>
    public int Pulse { get; private set; }

    private int _pulseWay = 1;

    /// <summary>How far the list has scrolled, and where it's scrolling to, in pixels.</summary>
    public int Scroll { get; private set; }

    private int _scrollGoal;

    // The remembered row is put on the cursor with the scroll at its goal at once.
    private bool _jump;

    /// <summary>The icon width the rows' text starts after.</summary>
    public int IconWidth => Items ? ItemIconWidth : SpellIconWidth;

    /// <summary>Moves the cursor a row up (−1) or down (+1), round from the end to the start (<c>FUN_80027BE0</c>, <c>FUN_80027C10</c>).</summary>
    public void Move(int by)
    {
        if (Rows.Count == 0 || by == 0)
            return;
        Cursor = ((Cursor + Math.Sign(by)) % Rows.Count + Rows.Count) % Rows.Count;
    }

    /// <summary>A tick (<c>CombatMenu.Step</c>, <c>WidgetPulseMenu.Step</c>): the pulse steps, and the scroll follows its goal.</summary>
    public void Tick()
    {
        Pulse += _pulseWay;
        if (Pulse >= PulseSteps - 1 || Pulse <= 0)
            _pulseWay = -_pulseWay;
        if (Scroll < _scrollGoal)
            Scroll = Math.Min(_scrollGoal, Scroll + ScrollSpeed);
        else if (Scroll > _scrollGoal)
            Scroll = Math.Max(_scrollGoal, Scroll - ScrollSpeed);
    }

    /// <summary>The cursor's row's colour this tick: from grey toward its own (<c>Color.Mix</c>, <c>0x80027F1C</c>).</summary>
    public Rgba CursorColor(Rgba own)
    {
        var t = (float)Pulse / PulseSteps;
        return new Rgba(Mix(PulseFrom.R, own.R), Mix(PulseFrom.G, own.G), Mix(PulseFrom.B, own.B), Mix(PulseFrom.A, own.A));

        byte Mix(byte from, byte to) => (byte)(from + (int)((to - from) * t));
    }

    /// <summary>
    /// Where it draws in the original's 320 × 240 frame, its rows as tall as <paramref name="heights"/> says, each its icon's or its
    /// text's, stacked with no gap (<c>ChoicePopup.Layout</c>, <c>0x80046488</c>): the list centred on the screen, its scroll kept so the
    /// cursor's row shows.
    /// </summary>
    public CombatMenuLayout Layout(IReadOnlyList<int> heights)
    {
        var tops = new int[Rows.Count];
        var content = 0;
        for (var i = 0; i < Rows.Count; i++)
        {
            tops[i] = content;
            content += i < heights.Count ? heights[i] : 0;
        }
        var height = Math.Min(content, MostHeight);
        var top = CentreY - height / 2;
        if (Rows.Count > 0)
        {
            var cursorTop = tops[Cursor];
            var cursorBottom = cursorTop + heights[Cursor];
            if (cursorTop < _scrollGoal)
                _scrollGoal = cursorTop;
            else if (cursorBottom > _scrollGoal + height)
                _scrollGoal = cursorBottom - height;
        }
        if (_jump)
        {
            Scroll = _scrollGoal;
            _jump = false;
        }
        var left = CentreX - Width / 2;
        return new CombatMenuLayout(new ScreenRect(left, top, left + Width, top + height), tops, Scroll,
            UpArrow: Scroll > 0, DownArrow: Scroll + height < content);
    }

    /// <summary>
    /// A row's height (<c>CombatMenu.MakeEntry</c>): its icon's, or its text's below the text's start, half the menu's icon width less
    /// half the text's height.
    /// </summary>
    public int RowHeight(int iconHeight, int textHeight) => Math.Max(iconHeight, IconWidth / 2 - textHeight / 2 + textHeight);

    /// <summary>Where a row's text starts below its top (<c>CombatMenu.MakeEntry</c>).</summary>
    public int TextTop(int textHeight) => IconWidth / 2 - textHeight / 2;
}

/// <summary>Where a held menu draws: its list's clip, each row's top in the list, the scroll, and which arrows show.</summary>
public sealed record CombatMenuLayout(ScreenRect List, IReadOnlyList<int> RowTops, int Scroll, bool UpArrow, bool DownArrow);
