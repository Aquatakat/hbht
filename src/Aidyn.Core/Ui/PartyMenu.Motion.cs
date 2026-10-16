using Aidyn.Core.Characters;

namespace Aidyn.Core.Ui;

/// <summary>A list sliding out as the stick changes it, and which way the lists move: 1 right, −1 left.</summary>
public sealed record ListSlide(PartyList From, int Direction);

/// <summary>
/// The menu's tweens (<c>Tween.CreateShort</c> at <c>0x800cc880</c>, <c>Tween.CreateByte</c> <c>0x800ccb88</c> in 1.0): a
/// value moved from where it is to an end over so many frames, a step a frame, eased in and out, and truncated to a
/// whole number each step.
/// </summary>
public static class MenuTween
{
    /// <summary>
    /// The share of the way a tween has gone after <paramref name="frame"/> of <paramref name="frames"/>, on its curve
    /// (<c>Tween.SineInOut</c>, <c>0x800e6590</c>): 0.5 × sin(π t − π/2) + 0.5, which starts and ends slowly.
    /// </summary>
    public static float Ease(int frame, int frames)
    {
        var t = Math.Clamp((float)frame / frames, 0, 1);
        return 0.5f * MathF.Sin(MathF.PI * t - MathF.PI / 2) + 0.5f;
    }

    /// <summary>A value that far from <paramref name="from"/> to <paramref name="to"/>, truncated as the original's are.</summary>
    public static int Value(int from, int to, float ease) => (int)(from + (to - from) * ease);
}

public sealed partial class PartyMenu
{
    /// <summary>
    /// The frames the header takes to change over to a new member (<c>PartyMenu.PortraitsChange</c>, <c>0x8003b3cc</c>):
    /// the portraits move and resize, and the name, numbers, icons and worn items crossfade.
    /// </summary>
    public const int MemberFrames = 15;

    /// <summary>The frames a list takes to slide in as the stick changes it, and how far it slides (<c>PartyMenu.ListsSlide</c>, <c>0x80031620</c>).</summary>
    public const int ListFrames = 20, ListDistance = 140;

    // A tween steps its frames and then finishes on the next, so a change is busy one frame longer than it moves.
    private int _memberFrame = MemberFrames + 1, _listFrame = ListFrames + 1;

    /// <summary>The member shown before the last change, while the header and silhouette change over to the new one.</summary>
    public Character? Previous { get; private set; }

    /// <summary>The portraits' order before the last change, which they move from.</summary>
    public IReadOnlyList<int> PreviousOrder { get; private set; } = [];

    /// <summary>How far the header has changed over to the member shown, eased, from 0 to 1.</summary>
    public float MemberChange => Previous is null ? 1 : MenuTween.Ease(_memberFrame, MemberFrames);

    /// <summary>The list sliding out as another slides in, or null.</summary>
    public ListSlide? Slide { get; private set; }

    /// <summary>How far the lists have slid, eased, from 0 to 1.</summary>
    public float ListChange => Slide is null ? 1 : MenuTween.Ease(_listFrame, ListFrames);

    /// <summary>Whether the header or the lists are still changing, which holds off another change (<c>PartyMenu.CanChange</c>, <c>0x800304b4</c>).</summary>
    public bool Changing => Previous is not null || Slide is not null;

    /// <summary>Starts the header and silhouette changing over from the member shown.</summary>
    private void StartMemberChange()
    {
        Previous = Shown;
        PreviousOrder = PortraitOrder;
        _memberFrame = 0;
    }

    /// <summary>Starts the list showing sliding out, the lists moving <paramref name="direction"/>'s way.</summary>
    private void StartListSlide(int direction)
    {
        Slide = new ListSlide(List, Math.Sign(direction));
        _listFrame = 0;
    }

    /// <summary>A frame of the member's and the list's changes.</summary>
    private void StepMotion()
    {
        if (Previous is not null && ++_memberFrame > MemberFrames)
            Previous = null;
        if (Slide is not null && ++_listFrame > ListFrames)
            Slide = null;
    }
}
