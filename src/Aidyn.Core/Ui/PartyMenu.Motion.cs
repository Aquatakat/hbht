using Aidyn.Core.Characters;

namespace Aidyn.Core.Ui;

public sealed partial class PartyMenu
{
    /// <summary>
    /// The frames the header takes to change over to a new member (<c>PartyMenu.PortraitsChange</c>, <c>0x8003b3cc</c>):
    /// the portraits move and resize, and the name, numbers, icons and worn items crossfade.
    /// </summary>
    public const int MemberFrames = 15;

    /// <summary>The frames a list takes to slide in as the stick changes it, and how far it slides (<c>PartyMenu.ListsSlide</c>, <c>0x80031620</c>).</summary>
    public const int ListFrames = 20, ListDistance = 140;

    private MenuSlide<(Character Member, IReadOnlyList<int> Order)>? _memberChange;

    /// <summary>The member shown before the last change, while the header and silhouette change over to the new one.</summary>
    public Character? Previous => _memberChange?.From.Member;

    /// <summary>The portraits' order before the last change, which they move from.</summary>
    public IReadOnlyList<int> PreviousOrder => _memberChange?.From.Order ?? [];

    /// <summary>How far the header has changed over to the member shown, eased, from 0 to 1.</summary>
    public float MemberChange => _memberChange?.Change ?? 1;

    /// <summary>The list sliding out as another slides in, with the way the lists move (1 right, −1 left), or null.</summary>
    public MenuSlide<PartyList>? Slide { get; private set; }

    /// <summary>How far the lists have slid, eased, from 0 to 1.</summary>
    public float ListChange => Slide?.Change ?? 1;

    /// <summary>Whether the header or the lists are still changing, which holds off another change (<c>PartyMenu.CanChange</c>, <c>0x800304b4</c>).</summary>
    public bool Changing => _memberChange is not null || Slide is not null;

    /// <summary>Starts the header and silhouette changing over from the member shown.</summary>
    private void StartMemberChange() =>
        _memberChange = Shown is { } shown ? new MenuSlide<(Character, IReadOnlyList<int>)>((shown, PortraitOrder), 0, MemberFrames, MenuTween.Ease) : null;

    /// <summary>Starts the list showing sliding out, the lists moving <paramref name="direction"/>'s way.</summary>
    private void StartListSlide(int direction) => Slide = new MenuSlide<PartyList>(List, Math.Sign(direction), ListFrames, MenuTween.Ease);

    /// <summary>A frame of the member's and the list's changes.</summary>
    private void StepMotion()
    {
        if (_memberChange?.Step() == false)
            _memberChange = null;
        if (Slide?.Step() == false)
            Slide = null;
    }
}
