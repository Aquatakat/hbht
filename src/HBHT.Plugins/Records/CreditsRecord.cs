namespace HBHT.Plugins.Records;

/// <summary>
/// The credits (<c>aidyn:credits/0</c>, the only one): their cards in order, each a heading over its names, which the
/// System tab's Credits row and the ending play (docs/systems/ui.md#the-credits). Their music is the sounds record's
/// (<see cref="SoundsRecord.Credits"/>).
/// </summary>
/// <param name="Shown">How many cards, from the first, show one at a time in the screen's middle before the rest scroll.</param>
/// <param name="End">What shows at the end of the ending's credits.</param>
public sealed record CreditsRecord(RecordId Id, IReadOnlyList<CreditsCard> Cards, int Shown, string End);

/// <summary>A card of the credits: a heading, drawn in yellow, and the names under it, in white. A card may have no names.</summary>
/// <param name="Names">Each is a line, or more where it has newlines in it, which the original uses for a blank line before a name.</param>
public sealed record CreditsCard(string Heading, IReadOnlyList<string> Names);
