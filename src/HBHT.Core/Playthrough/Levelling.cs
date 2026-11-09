using HBHT.Core.Characters;
using HBHT.Core.MapObjects;
using HBHT.Core.Ui;
using HBHT.Plugins;

namespace HBHT.Core.Playthrough;

/// <summary>Something the party bought with experience, and gold at a trainer.</summary>
/// <param name="Tick">The host's tick it was bought on.</param>
/// <param name="Member">Who it was bought for.</param>
/// <param name="Build">Their build's name.</param>
/// <param name="Pick">What it was, from what level, and its price.</param>
/// <param name="Trainer">The trainer's entity, or null after a battle.</param>
public sealed record Purchase(long Tick, RecordId Member, string Build, BuildPick Pick, RecordId? Trainer)
{
    public override string ToString() => $"{Tick} {Member} ({Build}): {Pick}{(Trainer is { } t ? $" at {t}" : "")}";
}

/// <summary>
/// The menu policy that levels the party (docs/systems/playthrough.md, layer 5): in the post-battle window it spends each member's
/// experience by their build (<see cref="Builds"/>), and at a trainer their experience and the party's gold, by the windows' own presses
/// as a player makes them: C-left to the member, the stick to the list and the row, A, and A on the question's Yes. So the game's own
/// rules decide what can be bought, and what's refused is left. Everything else goes to <see cref="Inner"/>, which takes the loot and
/// leaves the rest by default.
/// </summary>
public sealed class Levelling(IMenuPolicy? inner = null, IReadOnlyList<Build>? builds = null) : IMenuPolicy
{
    private readonly List<Purchase> _bought = [];

    /// <summary>What the menu does otherwise: the loot, shops, the pause menu and popups.</summary>
    public IMenuPolicy Inner { get; } = inner ?? new TakeLootLeaveTheRest();

    /// <summary>The builds, first fit first.</summary>
    public IReadOnlyList<Build> Builds { get; } = builds ?? HBHT.Core.Playthrough.Builds.Default;

    /// <summary>The gold kept back from training, for what else the party needs.</summary>
    public uint GoldReserve { get; set; }

    /// <summary>Everything bought, in order.</summary>
    public IReadOnlyList<Purchase> Bought => _bought;

    /// <summary>What the windows refused that the builds asked for, which the bot's and the game's rules disagree on: none is expected.</summary>
    public List<string> Refused { get; } = [];

    /// <summary>The window being shopped in, so a new one starts afresh.</summary>
    private IPartyWindow? _window;

    /// <summary>What's being bought, and for whom (a slot).</summary>
    private (int Slot, BuildPick Pick, string Build)? _target;

    /// <summary>Whether A has asked for the target, so the next popup is its question or its refusal.</summary>
    private bool _asked;

    /// <summary>Presses waited since asking.</summary>
    private int _waited;

    /// <summary>The purchase Yes was pressed for, checked once the question has gone: who, what, and its place in <see cref="Bought"/>.</summary>
    private (Character Member, BuildPick Pick, int At)? _answered;

    /// <summary>What this window refused, by member and what it was, left till it closes.</summary>
    private readonly HashSet<(RecordId Member, string What)> _refused = [];

    public MenuAction Press(PlaythroughHost host, PauseMenu menu)
    {
        if (menu.Tab != PauseTab.Party)
            return Inner.Press(host, menu);
        switch (menu.Window)
        {
            case PostBattleMenu post:
                return Shop(host, post, post.Party, null, post.Overlay);
            case TrainingMenu training:
                return Shop(host, training, training.Party, training.Trainer, training.Overlay);
            default:
                _window = null;
                return Inner.Press(host, menu);
        }
    }

    public int? Popup(PlaythroughHost host, TrekPopup popup) => Inner.Popup(host, popup);

    /// <summary>
    /// The next press in the post-battle window or training: a refusal or help closed, the question answered Yes, or on to the next thing
    /// the builds want, B once there's nothing more.
    /// </summary>
    private MenuAction Shop(PlaythroughHost host, IPartyWindow window, PartyMenu party, Trainer? trainer, MenuOverlay overlay)
    {
        if (!ReferenceEquals(window, _window))
        {
            _window = window;
            _target = null;
            _asked = false;
            _refused.Clear();
            _answered = null;
        }
        if (_answered is var (answeredFor, done, at) && overlay.Choice is null)
        {
            // Yes was taken: what it bought has gone up, or it bought nothing and is left.
            _answered = null;
            if (LevelOf(answeredFor, done) <= done.From)
            {
                _bought.RemoveAt(at);
                _refused.Add((answeredFor.Entity, Name(done)));
                Refused.Add($"{answeredFor.Name}: {done}: Yes trained nothing");
            }
        }
        if (overlay.Choice is { } choice)
        {
            // The question for what was asked: Yes, at the top.
            if (choice.Choices.Cursor != 0)
                return MenuAction.Up;
            if (_answered is null && _target is { } bought && party.Party.Slots.ElementAtOrDefault(bought.Slot) is { } member)
            {
                _bought.Add(new Purchase(host.Clock.Ticks, member.Entity, bought.Build, bought.Pick, trainer?.Entity));
                _answered = (member, bought.Pick, _bought.Count - 1);
            }
            _target = null;
            _asked = false;
            return MenuAction.Accept;
        }
        if (overlay.HasPopup || party.HasPopup)
        {
            // A refusal for what was asked, or the first spell's help: A closes it.
            if (_asked && overlay.Message is not null && _target is { } refused && party.Party.Slots.ElementAtOrDefault(refused.Slot) is { } who)
            {
                _refused.Add((who.Entity, Name(refused.Pick)));
                Refused.Add($"{who.Name}: {refused.Pick}: {overlay.Message.Text}");
            }
            _target = null;
            _asked = false;
            return MenuAction.Accept;
        }
        if (_asked && ++_waited < 8)
        {
            // Asked, and nothing came up yet: wait for it, and ask again if it doesn't come.
            return MenuAction.None;
        }
        _asked = false;
        _waited = 0;
        if (_target is null)
        {
            _target = Plan(party.Party, trainer);
            if (_target is null)
                return MenuAction.Back;
        }
        var (slot, pick, _) = _target.Value;
        if (party.Member != slot)
            return MenuAction.MemberLeft;
        var list = pick.Stat is not null ? PartyList.Stats : pick.Skill is not null ? PartyList.Skills : PartyList.Spells;
        if (party.List != list)
            return MenuAction.Right;
        if (party.Slide is not null || (window is TrainingMenu { Slide: not null }))
            return MenuAction.None;
        var (row, cursor) = RowOf(window, party, pick);
        if (row < 0)
        {
            // The window doesn't list it: left, and the next planned.
            if (party.Party.Slots.ElementAtOrDefault(slot) is { } missing)
            {
                _refused.Add((missing.Entity, Name(pick)));
                Refused.Add($"{missing.Name}: {pick}: not listed");
            }
            _target = null;
            return MenuAction.None;
        }
        if (cursor != row)
            return cursor < row ? MenuAction.Down : MenuAction.Up;
        _asked = true;
        _waited = 0;
        return MenuAction.Accept;
    }

    /// <summary>The first member, in the party's order, whose build wants something they can buy here now.</summary>
    private (int Slot, BuildPick Pick, string Build)? Plan(Party party, Trainer? trainer)
    {
        var gold = party.Gold > GoldReserve ? party.Gold - GoldReserve : 0;
        for (var slot = 0; slot < party.Slots.Count; slot++)
        {
            if (party.Slots[slot] is not { IsDead: false } member)
                continue;
            if (HBHT.Core.Playthrough.Builds.Next(member, Builds, trainer, gold, p => _refused.Contains((member.Entity, Name(p)))) is { } pick)
                return (slot, pick, HBHT.Core.Playthrough.Builds.For(member, Builds)!.Name);
        }
        return null;
    }

    /// <summary>
    /// Where the pick's row is in the list the window's cursor moves, and where the cursor is: the member's own list after a battle, the
    /// trainer's at a trainer (the member's follows it). −1 if it isn't listed.
    /// </summary>
    private static (int Row, int Cursor) RowOf(IPartyWindow window, PartyMenu party, BuildPick pick)
    {
        if (window is TrainingMenu training)
        {
            return pick.Skill is { } taught
                ? (IndexOf(training.Skills.Rows, r => r.Skill == taught), training.Skills.Cursor)
                : (IndexOf(training.Spells.Rows, r => r.Spell.Spell.Id == pick.Spell!.Id), training.Spells.Cursor);
        }
        if (pick.Stat is { } stat)
            return (IndexOf(party.Stats.Rows, r => r.Stat == stat), party.Stats.Cursor);
        if (pick.Skill is { } skill)
            return (IndexOf(party.Skills.Rows, r => r.Skill == skill), party.Skills.Cursor);
        return (IndexOf(party.Spells.Rows, r => r.Spell.Spell.Id == pick.Spell!.Id), party.Spells.Cursor);
    }

    private static int IndexOf<T>(IReadOnlyList<T> rows, Func<T, bool> match)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (match(rows[i]))
                return i;
        }
        return -1;
    }

    /// <summary>Where a member stands now in what a pick raises: the attribute's trained value, the skill's base, the spell's level (0 unknown).</summary>
    private static int LevelOf(Character member, BuildPick pick) =>
        pick.Stat is { } stat ? member.Stats.Trained(stat)
        : pick.Skill is { } skill ? member.Skills.Base(skill)
        : member.FindSpell(pick.Spell!.Id)?.Level ?? 0;

    private static string Name(BuildPick pick) => pick.Stat?.ToString() ?? pick.Skill?.ToString() ?? pick.Spell!.Id.ToString();

    /// <summary>
    /// Trains with <paramref name="trainer"/> as a script's Train command does (<see cref="GameSession.OpenTraining"/>): the window opens,
    /// this buys by the builds what the experience and gold allow, through its presses, and closes it. The host must have the player's
    /// controls in trek. What was bought; the host's menu policy is put back after.
    /// </summary>
    public IReadOnlyList<Purchase> Train(PlaythroughHost host, Trainer trainer, long most = 60 * 60 * 10)
    {
        if (!host.PlayerHasControl)
            throw new InvalidOperationException("Training opens from trek, with the player at the controls.");
        var before = host.Menus;
        var from = _bought.Count;
        host.Menus = this;
        try
        {
            host.Session.OpenTraining(trainer);
            host.RunUntil(h => h.Session.Mode != GameMode.Menu, most);
        }
        finally
        {
            host.Menus = before;
        }
        return _bought[from..];
    }
}
