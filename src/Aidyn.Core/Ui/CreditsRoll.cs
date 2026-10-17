using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>A line of the credits as it stands: its text, its top on the screen, and its colour with its alpha.</summary>
public readonly record struct CreditsLine(string Text, int Y, Rgba Color);

/// <summary>
/// The credits (game mode 0x13, the credits widget, <c>Credits.Step</c> at <c>0x8002c6d8</c> in 1.0) over a black screen:
/// the first cards one at a time in the middle of the screen, fading in, holding and fading out, then the rest
/// scrolling up from the screen's foot, and after the ending's, "The End". The runtime draws <see cref="Lines"/> and
/// carries out the end.
/// </summary>
/// <remarks>
/// <para>
/// The original steps it once a frame, its fades by the frame's ticks and its scroll by the frame. The port steps it a
/// tick at a time, so it runs as the original's would at 60 frames a second.
/// </para>
/// <para>
/// Each card is its heading over its names, 11 pixels a line in the main font and centred across, a name with a
/// newline in it taking a line more. Every card starts with its top at the screen's foot. One shown alone is moved up
/// by half its height and the screen's (<c>Credits.StepCards</c>, <c>0x8002c7b8</c>), which centres it, and fades in
/// by 4 a tick, holds 120 ticks and fades out by 4. The next is set up on the tick after, and the cards before the
/// first to scroll go through it one after another. Those scrolling move up a pixel every other tick
/// (<c>Credits.StepScroll</c>, <c>0x8002ca64</c>), each joining once the one before has its foot 32 pixels above the
/// screen's, until the last has gone off the top. Then the ending shows "The End" in the screen's middle, fading in by
/// 2 a tick, holding 240 and fading out by 2 (<c>Credits.StepEnd</c>, <c>0x8002cb50</c>); the System tab's credits end
/// at once. A, B and Start end them at once, but the ending's (<c>Credits.Skip</c>, <c>0x8002cd90</c>).
/// </para>
/// </remarks>
public sealed class CreditsRoll
{
    /// <summary>The headings' colour, the names' and "The End"'s (<c>Credits.Start</c>, <c>0x8002c22c</c> on).</summary>
    public static readonly Rgba Heading = new(225, 225, 0, 255), Name = new(225, 225, 225, 255), EndColor = new(255, 255, 255, 255);

    /// <summary>The ticks a card shown alone holds at full strength (120.0 / delta frames), and "The End" (240 frames).</summary>
    public const int CardHold = 120, EndHold = 240;

    /// <summary>How fast a card shown alone fades in and out, a tick (4 × delta), and "The End" (2 × delta).</summary>
    public const int CardFade = 4, EndFade = 2;

    /// <summary>How far above the screen's foot a scrolling card's foot is when the next joins it.</summary>
    public const int ScrollGap = 32;

    /// <summary>The ticks before the first card is set up: the widget opening, about three frames.</summary>
    public const int Opening = 3;

    private enum State { Opening, Cards, Scroll, End, Ended }

    private sealed class Card(IReadOnlyList<string> lines, int height, int y)
    {
        public IReadOnlyList<string> Lines { get; } = lines;
        public int Height { get; } = height;
        public int Y = y;
        public int Alpha = 255;
    }

    private readonly List<Card> _cards;
    private readonly int _shown, _lineHeight, _screenHeight;
    private readonly string _end;
    private State _state;
    private int _timer = Opening, _current, _hold;
    private bool _fadingIn, _moved;
    private int _endAlpha;
    private bool _endShown;

    /// <param name="lineHeight">A line's height in the main font (<see cref="TextLayout.LineHeight"/>): 11.</param>
    /// <param name="screenHeight">The screen's height, 240 on the console's.</param>
    /// <param name="ending">Whether the ending started them, so they can't be skipped and end with "The End".</param>
    public CreditsRoll(CreditsRecord credits, int lineHeight, int screenHeight, bool ending = false)
    {
        _lineHeight = lineHeight;
        _screenHeight = screenHeight;
        _shown = Math.Min(credits.Shown, credits.Cards.Count);
        _end = credits.End;
        Ending = ending;
        _cards = [.. credits.Cards.Select(c =>
        {
            IReadOnlyList<string> lines = [c.Heading, .. c.Names];
            return new Card(lines, lines.Sum(Height), screenHeight);
        })];
    }

    public bool Ending { get; }

    /// <summary>Whether they've ended, so trek comes back, or the ending's game ends.</summary>
    public bool Ended => _state == State.Ended;

    /// <summary>The lines on the screen now, with their colours' alphas, and "The End".</summary>
    public IEnumerable<CreditsLine> Lines
    {
        get
        {
            if (Ended)
                yield break;
            foreach (var card in _cards)
            {
                if (card.Alpha == 0 || card.Y >= _screenHeight || card.Y + card.Height <= 0)
                    continue;
                var y = card.Y;
                for (var i = 0; i < card.Lines.Count; i++)
                {
                    var height = Height(card.Lines[i]);
                    if (y < _screenHeight && y + height > 0)
                        yield return new CreditsLine(card.Lines[i], y, (i == 0 ? Heading : Name) with { A = (byte)card.Alpha });
                    y += height;
                }
            }
            if (_endShown && _endAlpha > 0)
                yield return new CreditsLine(_end, (_screenHeight - _lineHeight) / 2, EndColor with { A = (byte)_endAlpha });
        }
    }

    /// <summary>A, B or Start: the credits end at once, but the ending's.</summary>
    public void Skip()
    {
        if (!Ending)
            _state = State.Ended;
    }

    /// <summary>One tick.</summary>
    public void Step()
    {
        switch (_state)
        {
            case State.Opening:
                if (--_timer <= 0)
                    _state = _shown > 0 ? State.Cards : State.Scroll;
                break;
            case State.Cards:
                StepCard();
                break;
            case State.Scroll:
                StepScroll();
                break;
            case State.End:
                StepEnd();
                break;
        }
    }

    /// <summary>A line's height: a line of the font for each line of the string, its newlines with it.</summary>
    private int Height(string line) => _lineHeight * (1 + line.Count(c => c == '\n'));

    private void StepCard()
    {
        var card = _cards[_current];
        if (card.Y >= _screenHeight)
        {
            card.Y -= (card.Height + _screenHeight) >> 1;
            card.Alpha = 0;
            _fadingIn = true;
            _hold = CardHold;
        }
        else if (_fadingIn)
        {
            if (card.Alpha < 255 - CardFade)
                card.Alpha += CardFade;
            else
            {
                card.Alpha = 255;
                if (--_hold <= 0)
                    _fadingIn = false;
            }
        }
        else if (card.Alpha > CardFade)
            card.Alpha -= CardFade;
        else
        {
            card.Alpha = 0;
            if (++_current >= _shown)
            {
                _state = State.Scroll;
                // The step counter is left at 1, so the first move is on the scroll's second tick.
                _moved = true;
            }
        }
    }

    private void StepScroll()
    {
        if (_current >= _cards.Count)
        {
            _state = Ending ? State.End : State.Ended;
            return;
        }
        _moved = !_moved;
        if (!_moved)
            return;
        // The first card still on the screen moves, and each after it once the one before is clear of the foot.
        _cards[_current].Y--;
        for (var k = _current + 1; k < _cards.Count; k++)
        {
            var before = _cards[k - 1];
            if (before.Y + before.Height + ScrollGap >= _screenHeight)
                break;
            _cards[k].Y--;
        }
        if (_cards[_current].Y + _cards[_current].Height < 0)
            _current++;
    }

    private void StepEnd()
    {
        if (!_endShown)
        {
            _endShown = true;
            _fadingIn = true;
            _hold = EndHold;
            return;
        }
        if (_fadingIn)
        {
            if (_endAlpha < 255 - EndFade)
                _endAlpha += EndFade;
            else
            {
                _endAlpha = 255;
                if (--_hold <= 0)
                    _fadingIn = false;
            }
        }
        else if (_endAlpha > EndFade)
            _endAlpha -= EndFade;
        else
        {
            _endAlpha = 0;
            _state = State.Ended;
        }
    }
}
