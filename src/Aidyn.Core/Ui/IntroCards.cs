using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>The cards before the title, in the order they show.</summary>
public enum IntroCard
{
    /// <summary>"Expansion Pak Enhanced / Expansion Pak Detected", Borg8 <c>0x3758</c> (<c>0x3759</c>, "No Expansion Pak Detected", without it).</summary>
    ExpansionPak,

    /// <summary>The Copyright string table's twelve lines in the slim font.</summary>
    Legal,

    /// <summary>The N64 logo, scene <c>0x25A8</c>, turning, over "Licensed by Nintendo", Borg8 <c>0x00FA</c>.</summary>
    Nintendo,

    /// <summary>THQ's logo, Borg8 <c>0x0101</c>.</summary>
    Thq,

    /// <summary>H2O's logo, Borg8 <c>0x00DE</c>.</summary>
    H2o,
}

/// <summary>
/// The intro cards (<c>Title.CardsFrame</c>, <c>0x8001b614</c> in 1.0), which <c>Title.Frame</c> plays over black as the console starts
/// and each time the game goes back to the title: the Expansion Pak's, the legal text, the N64 logo, THQ's and H2O's, each fading in,
/// holding and fading out (<c>Title.CardFade</c>, <c>0x8001b250</c>). The title's music starts with them. The runtime draws
/// <see cref="Card"/> at <see cref="Alpha"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each card fades in by 15 a tick, so it's whole on its 17th, holds (30 ticks for the first, 120 for the rest), fades out by 15 over 17
/// ticks and stays black until 36 ticks after it began to fade out; the next starts on the tick after. They step once a pad event,
/// which the controller's thread pushes once a 60 Hz retrace, so a tick each whatever the frame rate: 775 ticks in all, about 13 s.
/// </para>
/// <para>
/// The N64 logo isn't animated: the camera goes round it, 40 out and 15 up, looking at its origin, 2° a tick from 0
/// (<c>Title.LogoAngle</c>, <c>0x800daebc</c>), so 0.96 of a turn over the card.
/// </para>
/// <para>
/// Nothing reads the buttons on the cards, so the original's can't be skipped. The port lets them be when there's a saved game
/// (<see cref="Skippable"/>), the user's choice: A, B or Start goes straight to the title.
/// </para>
/// </remarks>
public sealed class IntroCards(bool skippable)
{
    /// <summary>How much a card's alpha changes a tick as it fades in and out.</summary>
    public const int FadeStep = 15;

    /// <summary>How long a card is black after it has faded out, counted from when it began to: 36 ticks, so 19 after.</summary>
    public const int DarkTicks = 36;

    /// <summary>How far the N64 logo's camera goes round a tick, in degrees, its distance out and its height.</summary>
    public const float LogoTurn = 2, LogoDistance = 40, LogoHeight = 15;

    /// <summary>The cards and how long each holds at full strength, in ticks.</summary>
    public static readonly IReadOnlyList<(IntroCard Card, int Hold)> Cards =
        [(IntroCard.ExpansionPak, 30), (IntroCard.Legal, 120), (IntroCard.Nintendo, 120), (IntroCard.Thq, 120), (IntroCard.H2o, 120)];

    /// <summary>The cards' images (<c>Title.CardsBegin</c>, <c>0x8001b4d8</c>), drawn centred.</summary>
    public static readonly RecordId ExpansionPakImage = Image(0x3758), LicensedImage = Image(0x00FA), ThqImage = Image(0x0101), H2oImage = Image(0x00DE);

    /// <summary>The N64 logo's scene (<c>Title.LoadN64Logo</c>, <c>0x8001b354</c>), drawn with near and far planes of 1 and 256.</summary>
    public static readonly RecordId NintendoLogo = RecordId.Hex(RecordTypes.Scene, 0x25A8);

    /// <summary>Where "Licensed by Nintendo" is: centred across, its top at 190.</summary>
    public const int LicensedTop = 190;

    /// <summary>
    /// The legal text's lines in the slim font, each centred across (<c>Title.LegalLine</c>, <c>0x8001b928</c>): from y 59, 9 a line, and 5
    /// more after lines 1, 3 and 7, in paragraphs of 2, 2, 4 and 4.
    /// </summary>
    public const int LegalTop = 59, LegalPitch = 9, LegalGap = 5;

    /// <summary>The lines the legal text leaves a gap after.</summary>
    public static readonly IReadOnlyList<int> LegalBreaks = [1, 3, 7];

    private enum Phase { In, Hold, Out }

    private int _index;
    private Phase _phase;
    private int _ticks;

    /// <summary>Whether A, B or Start skips them: with a saved game to load.</summary>
    public bool Skippable { get; } = skippable;

    /// <summary>The card showing.</summary>
    public IntroCard Card => Cards[Math.Min(_index, Cards.Count - 1)].Card;

    /// <summary>How opaque the card is, 0 to 255.</summary>
    public int Alpha { get; private set; }

    /// <summary>The N64 logo's camera's angle round it, in degrees.</summary>
    public float LogoAngle { get; private set; }

    /// <summary>Whether the last card has gone.</summary>
    public bool Ended => _index >= Cards.Count;

    /// <summary>A tick of the card showing (<c>Title.CardFade</c>): its fade in, its hold, its fade out and the black after.</summary>
    public void Step()
    {
        if (Ended)
            return;
        if (Card == IntroCard.Nintendo)
        {
            LogoAngle += LogoTurn;
            if (LogoAngle > 360)
                LogoAngle -= 360;
        }
        _ticks++;
        switch (_phase)
        {
            case Phase.In:
                Alpha = Math.Min(255, Alpha + FadeStep);
                if (Alpha == 255)
                    (_phase, _ticks) = (Phase.Hold, 0);
                break;
            case Phase.Hold:
                if (_ticks >= Cards[_index].Hold)
                    (_phase, _ticks) = (Phase.Out, 0);
                break;
            case Phase.Out:
                Alpha = Math.Max(0, Alpha - FadeStep);
                if (_ticks >= DarkTicks)
                {
                    _index++;
                    (_phase, _ticks, Alpha, LogoAngle) = (Phase.In, 0, 0, 0);
                }
                break;
        }
    }

    /// <summary>A press: A, B or Start ends the cards if they can be skipped. True if it did.</summary>
    public bool Press(MenuAction action)
    {
        if (!Skippable || action is not (MenuAction.Accept or MenuAction.Back or MenuAction.Start))
            return false;
        _index = Cards.Count;
        Alpha = 0;
        return true;
    }

    private static RecordId Image(int borg8) => RecordId.Hex(RecordTypes.Image, borg8);
}
