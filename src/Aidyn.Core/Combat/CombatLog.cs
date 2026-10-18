using Aidyn.Plugins;

namespace Aidyn.Core.Combat;

/// <summary>
/// The battle's log lines (<c>Combat.Current</c>'s <c>+0x4FC4</c>–<c>+0x52C4</c>): the line the log box shows, a line
/// queued behind it and a third that overrides the queued one, with the flag that says the third is waiting. There's no
/// timer: <see cref="Show"/> replaces the line at once, and a queued line comes up only when the battle flushes it at a
/// state's change (an attack's result as its animation ends, a cast's end, a potion), so a line holds until the next.
/// See docs/systems/ui.md's battle screen.
/// </summary>
public sealed class CombatLog
{
    /// <summary>The line shown (<c>+0x4FC4</c>), a space before the first, as the box's text starts.</summary>
    public string Shown { get; private set; } = " ";

    /// <summary>The line queued behind it (<c>+0x50C4</c>), which a flush shows.</summary>
    public string Queued { get; private set; } = "";

    /// <summary>A third line (<c>+0x51C4</c>), which only Banish queues: a flush shows it in place of the queued one.</summary>
    public string Third { get; private set; } = "";

    /// <summary>Whether the third line is waiting (<c>+0x52C4</c>'s bit 2, the only one used).</summary>
    public bool ThirdWaiting { get; private set; }

    /// <summary>
    /// What the log box says now: the shown line, or while a hold is up its own line, or once locked the line it was
    /// locked on.
    /// </summary>
    public string Text => _locked ?? Hold ?? Shown;

    /// <summary>
    /// A line that holds the battle (<c>+0x543C</c>): while it's up the box shows it in place of the shown line, and the
    /// battle's states and input wait. Only a reserve that flees as it arrives sets it, with "Enemy reinforcements flee"
    /// (<c>Combat.HoldForMessage</c>, <c>0x80022AEC</c>); session 5's reserves set and step it.
    /// </summary>
    public string? Hold { get; private set; }

    /// <summary>The ticks a hold has been up (<c>+0x52C8</c>).</summary>
    private int _held;

    private string? _locked;

    /// <summary>The ticks a hold lasts: until <c>+0x52C8</c> is above 59.</summary>
    public const int HoldTicks = 60;

    /// <summary>
    /// The portrait the box is locked on, once the party is down (<c>CombatGui.SetLogPortraitLocked</c>,
    /// <c>0x80087A34</c>): nothing changes the box after.
    /// </summary>
    public RecordId? LockedPortrait { get; private set; }

    /// <summary>Whether the box is locked.</summary>
    public bool Locked => _locked is not null;

    /// <summary>Shows a line at once (<c>Combat.ShowLog</c>, <c>0x8005E604</c>).</summary>
    public void Show(string line)
    {
        if (!Locked)
            Shown = line;
    }

    /// <summary>Queues a line, shown at the next flush; a second overwrites it (<c>Combat.QueueLog</c>, <c>0x8005E658</c>).</summary>
    public void Queue(string line) => Queued = line;

    /// <summary>Queues the third line, which the next flush shows in place of the queued one (<c>Combat.QueueLog2</c>, <c>0x8005E698</c>).</summary>
    public void QueueThird(string line)
    {
        Third = line;
        ThirdWaiting = true;
    }

    /// <summary>
    /// Shows the queued line, or the third if it's waiting, which then becomes the queued one (<c>Combat.FlushQueuedLog</c>,
    /// <c>0x8005E6F4</c>). The queued line stays, so a second flush shows it again.
    /// </summary>
    public void Flush()
    {
        if (ThirdWaiting)
        {
            Queued = Third;
            ThirdWaiting = false;
        }
        Show(Queued);
    }

    /// <summary>Holds the battle on <paramref name="line"/> for a second (<c>+0x543C</c>).</summary>
    public void HoldOn(string line)
    {
        Hold = line;
        _held = 0;
    }

    /// <summary>
    /// A tick of a hold (<c>Combat.HoldForMessage</c>): once a second has passed it's over, and the shown line comes back
    /// (<c>Combat.ShowLogAgain</c>, <c>0x8005E6D8</c>). Whether it's still holding.
    /// </summary>
    public bool StepHold()
    {
        if (Hold is null)
            return false;
        if (++_held < HoldTicks)
            return true;
        Hold = null;
        return false;
    }

    /// <summary>
    /// Shows <paramref name="line"/> under <paramref name="portrait"/> for good, as the party falls (<c>TurnOrder.Defeat</c>,
    /// <c>0x8006A780</c>, and <c>CombatGui.LockLogBox</c>): nothing changes the box after.
    /// </summary>
    public void Lock(string line, RecordId? portrait)
    {
        if (Locked)
            return;
        Shown = line;
        _locked = line;
        LockedPortrait = portrait;
    }
}
