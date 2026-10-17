using System.Text;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Ui;

/// <summary>
/// The name entry (<c>InputMenu</c>), which renames Alaron from the options menu: a grid of keys, 13 a row, and
/// a name of up to 10 letters with a slot being chosen. Moving over the grid writes the key into the slot, as
/// the original's does, and A types it and moves on, but not past the last slot. Start goes to OK, and a second
/// Start confirms; B cancels.
/// </summary>
/// <remarks>
/// <para>
/// The first letter and any after a space are capitals, the rest small, whatever key wrote them.
/// </para>
/// <para>
/// On a PC the keyboard types too: a letter, digit or one of the keys' symbols goes into the slot and moves on,
/// Backspace rubs the last out, and Enter confirms. The original runs its cheat codes on the name here
/// (<c>Cheats::Check</c>); they aren't ported.
/// </para>
/// </remarks>
public sealed class NameEntry
{
    /// <summary>The longest a name is (<c>IMNameLength</c>).</summary>
    public const int MaxLength = 10;

    /// <summary>The keys in a row.</summary>
    public const int Columns = 13;

    /// <summary>What the grid's last three keys do: rub out, cancel, and confirm.</summary>
    public const char RubOutKey = '~', CancelKey = '`', OkKey = '\\';

    /// <summary>The grid's keys, in order: the characters, then rub out, cancel and OK (<c>InputMenu</c>'s key string).</summary>
    public const string Keys = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789<>|*_^\"'()!- " + "~`\\";

    private readonly StringBuilder _name;

    public NameEntry(string name)
    {
        _name = new StringBuilder(name.Length > MaxLength ? name[..MaxLength] : name);
        Slot = Math.Min(_name.Length, MaxLength - 1);
        Key = Keys.IndexOf(OkKey);
    }

    /// <summary>The name as it stands, with the slot being chosen in it.</summary>
    public string Name => _name.ToString();

    /// <summary>The slot being chosen: a letter's, or one past the last, where the next goes.</summary>
    public int Slot { get; private set; }

    /// <summary>The key the cursor is on.</summary>
    public int Key { get; private set; }

    /// <summary>The name chosen, once confirmed.</summary>
    public string? Chosen { get; private set; }

    /// <summary>Whether it was cancelled, or confirmed (<see cref="Chosen"/>): either way, it's closed.</summary>
    public bool Cancelled { get; private set; }

    public bool Closed => Cancelled || Chosen is not null;

    /// <summary>
    /// The letter being chosen's pulse (<c>InputMenu.Step</c>, <c>0x8002d7e4</c>): grey from 150, 5 a step between 95 and
    /// 230, 54 frames a cycle, as 27 steps of a 27th from <see cref="PulseFrom"/> to <see cref="PulseTo"/>.
    /// </summary>
    public MenuPulse Pulse { get; } = new(frames: 27, top: 27, start: 11);

    /// <summary>The ends of the letter's pulse.</summary>
    public static readonly Rgba PulseFrom = new(95, 95, 95, 255), PulseTo = new(230, 230, 230, 255);

    public void Step() => Pulse.Step();

    /// <summary>The stick: moves over the grid, wrapping round its rows, and writes the key into the slot.</summary>
    public void Move(int dx, int dy)
    {
        var rows = (Keys.Length + Columns - 1) / Columns;
        var (row, column) = (Key / Columns, Key % Columns);
        if (dx != 0)
        {
            Key = ((Key + dx) % Keys.Length + Keys.Length) % Keys.Length;
        }
        else
        {
            row = ((row + dy) % rows + rows) % rows;
            Key = Math.Min(row * Columns + column, Keys.Length - 1);
        }
        Write();
    }

    /// <summary>Puts the cursor on a key, as hovering does.</summary>
    public void Point(int key)
    {
        Key = Math.Clamp(key, 0, Keys.Length - 1);
        Write();
    }

    /// <summary>A: types the key and moves on, or rubs out, cancels or confirms.</summary>
    public void Accept()
    {
        switch (Keys[Key])
        {
            case RubOutKey:
                RubOut();
                break;
            case CancelKey:
                Cancelled = true;
                break;
            case OkKey:
                Confirm();
                break;
            default:
                Type(Keys[Key]);
                break;
        }
    }

    /// <summary>Start: goes to OK, or confirms if it's there.</summary>
    public void Start()
    {
        if (Keys[Key] == OkKey)
            Confirm();
        else
            Key = Keys.IndexOf(OkKey);
    }

    /// <summary>B: cancels, leaving the name as it was.</summary>
    public void Back() => Cancelled = true;

    /// <summary>A character typed on the keyboard: written into the slot, which moves on, if a key has it.</summary>
    public bool Type(char c)
    {
        var upper = char.ToUpperInvariant(c);
        var key = Keys.IndexOf(upper);
        if (key < 0 || key >= Keys.Length - 3)
            return false;
        Key = key;
        Write();
        // The last slot takes each letter in turn.
        Slot = Math.Min(Slot + 1, MaxLength - 1);
        return true;
    }

    /// <summary>Rubs out the last letter, and the slot goes where the next goes.</summary>
    public void RubOut()
    {
        if (_name.Length > 0)
            _name.Remove(_name.Length - 1, 1);
        Slot = Math.Min(_name.Length, MaxLength - 1);
    }

    public void Confirm()
    {
        var name = _name.ToString().Trim();
        if (name.Length > 0)
            Chosen = name;
        else
            Cancelled = true;
    }

    /// <summary>Writes the cursor's key into the slot, if it's a character, in the name's case.</summary>
    private void Write()
    {
        var c = Keys[Key];
        if (c is RubOutKey or CancelKey or OkKey)
            return;
        var capital = Slot == 0 || _name[Slot - 1] == ' ';
        c = capital ? char.ToUpperInvariant(c) : char.ToLowerInvariant(c);
        if (Slot < _name.Length)
            _name[Slot] = c;
        else
            _name.Append(c);
    }
}
