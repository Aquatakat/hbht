namespace HBHT.Rom.Engine;

/// <summary>
/// How the credits split their string table into cards (docs/systems/ui.md#the-credits), read from the code segment.
/// </summary>
/// <param name="Starts">
/// The first line of each card (<c>Credits.CardStarts</c>): card k is lines <c>Starts[k]</c> up to <c>Starts[k + 1]</c>,
/// and the last runs to the line before the table's last, "The End" (<c>Credits.Start</c>, 1.0 <c>0x8002c0b0</c>).
/// </param>
public sealed record CreditsTable(IReadOnlyList<int> Starts)
{
    /// <summary>
    /// The line after the first card that scrolls: <c>Credits.StepCards</c> (1.0 <c>0x8002c7b8</c>) stops showing cards
    /// one at a time on reaching the card that ends here, a literal (<c>0x1D</c>).
    /// </summary>
    public const int ScrollEndLine = 0x1D;

    public static CreditsTable Read(RomImage rom) => new([.. rom.Table(CodeTable.CreditsCards).ToArray().Select(b => (int)b)]);

    /// <summary>How many cards show one at a time before the rest scroll: those before the one ending at <see cref="ScrollEndLine"/>.</summary>
    public int Shown => Starts.ToList().IndexOf(ScrollEndLine) - 1;
}
