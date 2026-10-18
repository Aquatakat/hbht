using System.Numerics;
using Aidyn.Core.Flags;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The minimap at the trek HUD's bottom right, in the map sets that have one (<c>MiniMap</c>, <c>minimap.cpp</c>): a dark
/// box holding the player's piece of the plan, bright, and the pieces of its group seen so far, dim. It's small while
/// the party's portraits are down, with the player's piece centred, and twice the size with the whole plan while
/// they're up, growing and shrinking over 14 frames.
/// </summary>
/// <remarks>
/// The original chooses the piece again only when the player changes chunk or crosses one of its rules' bounds
/// (<c>SpecialSections</c>, <c>UpdateSection</c>); choosing every frame comes to the same.
/// </remarks>
/// <param name="size">An image's width and height, which centring a piece needs.</param>
public sealed class Minimap(MinimapSeen seen, Func<RecordId, (int Width, int Height)> size)
{
    /// <summary>The dark box, stretched around the plan (<c>WhiteBorderGraySquare</c>), and the dark vignette over it (<c>WhiteBorderAlphaSquare</c>).</summary>
    public static readonly RecordId Box = HudSprite.Id(0x00F5), Vignette = HudSprite.Id(0x00F3);

    /// <summary>Where the plan shows, small and large (<c>MiniMap::LoadData</c>, <c>toggle_show_minimap</c>): its top-left and size.</summary>
    public static readonly (int Left, int Top, int Size) SmallBox = (237, 163, 60), LargeBox = (177, 52, 120);

    /// <summary>Animations move a 14th of the way they started from, a frame.</summary>
    public const float Frames = 14;

    /// <summary>The box's border is 8 pixels at the small size, scaled with it.</summary>
    public const int Border = 8;

    private readonly Animated _left = new(), _top = new(), _right = new(), _bottom = new(), _scale = new(), _offsetX = new(), _offsetY = new();
    /// <summary>The group's seen pieces, in the order they're drawn.</summary>
    private List<Piece> _pieces = [];
    /// <summary>The group before, fading out where it was.</summary>
    private List<Piece> _fading = [];
    private Piece? _current;
    private int? _group;

    /// <summary>The minimap loaded, if the map set has one.</summary>
    public MinimapRecord? Record { get; private set; }

    /// <summary>Whether it's large (<c>B_buttonToggle</c>), kept whether a minimap is loaded or not.</summary>
    public bool Large { get; private set; } = true;

    /// <summary>Whether the player's chunk has a piece, so the minimap shows in place of the compass (<c>ShowMinimap</c>).</summary>
    public bool Showing => Record is not null && _current is not null;

    /// <summary>The piece the player's in, by its place in the record.</summary>
    public int? Section => _current?.Section;

    /// <summary>
    /// The map set's minimap is loaded, or none (<c>MiniMap::LoadData</c>, <c>MiniMap::Free</c>). The same one loaded
    /// again keeps where it was; another starts afresh, at its size at once.
    /// </summary>
    public void Load(MinimapRecord? record)
    {
        if (record == Record)
            return;
        Record = record;
        _pieces = [];
        _fading = [];
        _current = null;
        _group = null;
        var box = Large ? LargeBox : SmallBox;
        foreach (var (value, at) in new[] { (_left, box.Left), (_top, box.Top), (_right, box.Left + box.Size), (_bottom, box.Top + box.Size),
                     (_offsetX, box.Left), (_offsetY, box.Top) })
            value.Snap(at);
        _scale.Snap(Large ? 2 : 1);
    }

    /// <summary>
    /// Grows or shrinks to a size, over 14 frames (<c>toggle_show_minimap</c>), doing nothing if it's that size already.
    /// The plan moves with it: whole from the box's top-left, or the player's piece centred.
    /// </summary>
    public void Toggle(bool large)
    {
        if (large == Large)
            return;
        Large = large;
        var box = large ? LargeBox : SmallBox;
        _left.To(box.Left);
        _top.To(box.Top);
        _right.To(box.Left + box.Size);
        _bottom.To(box.Top + box.Size);
        _scale.To(large ? 2 : 1);
        var (x, y) = Offset();
        _offsetX.To(x);
        _offsetY.To(y);
    }

    /// <summary>Where the plan's origin goes: the box's top-left when large, or so the player's piece is centred when small.</summary>
    private (float X, float Y) Offset()
    {
        var box = Large ? LargeBox : SmallBox;
        if (Large || _current is null || Record is null)
            return (box.Left, box.Top);
        var section = Record.Sections[_current.Section];
        var (width, height) = size(section.Image);
        return (box.Left + box.Size / 2 - section.X - (width >> 1), box.Top + box.Size / 2 - section.Y - (height >> 1));
    }

    /// <summary>
    /// The piece for where the player stands (<c>UpdateSection</c>): in the chunk at <paramref name="row"/> and
    /// <paramref name="column"/>, at <paramref name="at"/> in its coordinates. A piece not seen before is seen now.
    /// Another piece of the same group slides into place; another group's comes at once, the old group fading out.
    /// </summary>
    public void Choose(int row, int column, Vector3 at, FlagNetwork flags)
    {
        if (Record is not { } record)
            return;
        if (Select(record, row, column, at, flags) is not { } index)
        {
            _current = null;
            return;
        }
        if (_current?.Section == index)
            return;
        var section = record.Sections[index];
        if (section.Group == _group)
        {
            if (_pieces.Find(p => p.Section == index) is { } piece)
            {
                _current = piece;
            }
            else
            {
                _current = new Piece(index);
                _pieces.Insert(0, _current);
            }
            seen.See(record, index);
            _current.Recolour();
            var (x, y) = Offset();
            _offsetX.To(x);
            _offsetY.To(y);
            return;
        }

        // A new group: the old one fades where it is, and the new one is every piece of it seen, the first on top.
        _group = section.Group;
        _fading = _pieces;
        seen.See(record, index);
        _pieces = [.. Enumerable.Range(0, record.Sections.Count).Reverse()
            .Where(i => record.Sections[i].Group == section.Group && seen.IsSeen(record, i)).Select(i => new Piece(i))];
        _current = _pieces.Find(p => p.Section == index)!;
        _current.Recolour();
        var (ox, oy) = Offset();
        _offsetX.Snap(ox);
        _offsetY.Snap(oy);
    }

    /// <summary>
    /// The piece the rules choose for where the player stands: the first rule for the chunk whose flag and bounds hold,
    /// or the chunk's first piece.
    /// </summary>
    public static int? Select(MinimapRecord record, int row, int column, Vector3 at, FlagNetwork flags)
    {
        foreach (var rule in record.Rules)
        {
            if (rule.Row != row || rule.Column != column)
                continue;
            if (rule.Flag is { } flag && !flags.IsSet(flag))
                continue;
            if (rule.Bounds is { } bounds && !bounds.All(b => Within(b, at)))
                continue;
            return rule.Section;
        }
        for (var i = 0; i < record.Sections.Count; i++)
        {
            if (record.Sections[i].Row == row && record.Sections[i].Column == column)
                return i;
        }
        return null;
    }

    private static bool Within(MinimapBound bound, Vector3 at)
    {
        var value = bound.Axis switch { MinimapAxis.X => at.X, MinimapAxis.Y => at.Y, _ => at.Z };
        return (bound.Below is not { } below || value < below) && (bound.Above is not { } above || value > above);
    }

    /// <summary>
    /// A frame (<c>MiniMap::Update</c>), whether it's showing or not: the old group fades out 10 an alpha, the player's
    /// piece brightens in with a shimmer, the others settle at grey 96 and alpha 128, and the box and the plan move
    /// toward where they're going.
    /// </summary>
    public void StepFrame()
    {
        if (Record is null)
            return;
        foreach (var piece in _fading)
            piece.Alpha = (byte)(piece.Alpha <= 10 ? 0 : piece.Alpha - 10);
        foreach (var piece in _pieces)
        {
            if (piece == _current)
                piece.Brighten();
            else
                piece.Settle();
        }
        foreach (var value in new[] { _left, _top, _right, _bottom, _scale, _offsetX, _offsetY })
            value.Step();
        foreach (var piece in _pieces)
        {
            var section = Record.Sections[piece.Section];
            piece.X = (int)(_offsetX.Now + section.X);
            piece.Y = (int)(_offsetY.Now + section.Y);
        }
    }

    /// <summary>
    /// What it draws while showing (<c>MiniMap::Render</c>): the box, the old group, the group cut to the plan's area, and
    /// the vignette over them, the screen at <paramref name="brightness"/>.
    /// </summary>
    public IEnumerable<HudSprite> Draw(float brightness)
    {
        if (!Showing)
            yield break;
        var (left, top, right, bottom) = ((int)_left.Now, (int)_top.Now, (int)_right.Now, (int)_bottom.Now);
        var border = (int)(_scale.Now * Border);
        var black = HudSprite.Times(new Rgba(0, 0, 0, 255), brightness);
        yield return new HudSprite(Box, left - border, top - border, 1, black, (right - left + 2 * border, bottom - top + 2 * border));
        var clip = new ScreenRect(left, top, right, bottom);
        foreach (var piece in _fading.Concat(_pieces))
        {
            if (piece.Alpha == 0)
                continue;
            var color = HudSprite.Times(new Rgba(piece.Grey, piece.Grey, piece.Grey, piece.Alpha), brightness);
            yield return new HudSprite(Record!.Sections[piece.Section].Image, piece.X, piece.Y, 1, color, Clip: clip);
        }
        yield return new HudSprite(Vignette, left, top, 1, black, (right - left + 1, bottom - top + 1));
    }

    /// <summary>A piece shown, with its colour, a grey, and where it was last placed.</summary>
    private sealed class Piece(int section)
    {
        public int Section { get; } = section;
        public byte Grey { get; private set; } = 96;
        public byte Alpha { get; set; }
        public int X { get; set; } = -4096;
        public int Y { get; set; } = -4096;

        /// <summary>Made the player's piece, it starts again from nothing.</summary>
        public void Recolour() => (Grey, Alpha) = (155, 0);

        /// <summary>The player's piece: up 10 an alpha to 255, its grey shimmering between 165 and 175 until it's past 145, then 155.</summary>
        public void Brighten()
        {
            Alpha = Alpha switch { < 240 => (byte)(Alpha + 10), < 245 => 250, _ => 255 };
            Grey = Grey switch
            {
                < 166 when Alpha < 145 => (byte)(Grey + 10),
                < 166 => 155,
                _ => (byte)(Grey - 10),
            };
        }

        /// <summary>Another piece: toward grey 96 and alpha 128, 10 a frame, landing within 10.</summary>
        public void Settle()
        {
            Grey = Toward(Grey, 96);
            Alpha = Toward(Alpha, 128);
        }

        private static byte Toward(byte value, int target) =>
            value < target - 10 ? (byte)(value + 10) : value <= target + 10 ? (byte)target : (byte)(value - 10);
    }

    /// <summary>A value moving to a target by a 14th of the way it started from, a frame, landing within a step.</summary>
    private sealed class Animated
    {
        private float _target, _step;

        public float Now { get; private set; }

        public void Snap(float at) => (Now, _target, _step) = (at, at, 0);

        public void To(float target) => (_target, _step) = (target, (target - Now) / Frames);

        public void Step()
        {
            if (_step == 0)
                return;
            if (MathF.Abs(_target - Now) <= MathF.Abs(_step))
                (Now, _step) = (_target, 0);
            else
                Now += _step;
        }
    }
}
