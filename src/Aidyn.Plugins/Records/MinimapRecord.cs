namespace Aidyn.Plugins.Records;

/// <summary>
/// A map set's minimap in trek (<c>aidyn:minimap/&lt;map set&gt;</c>, <c>MiniMap</c>): pieces of a drawn plan, one of which is
/// the player's, chosen by the chunk he's in, and in some chunks by where he stands or by flags. The pieces of the chosen
/// one's group that have been seen show around it, and seeing a piece is saved.
/// </summary>
/// <param name="Sections">The pieces, in the order the game saves whether they've been seen.</param>
/// <param name="Rules">
/// Which piece a chunk shows, where it isn't simply the chunk's own: for the player's chunk, the first rule whose flag
/// and bounds hold chooses its piece, or none. With no rule holding, the chunk's first piece is chosen, and a chunk with
/// none shows no minimap.
/// </param>
public sealed record MinimapRecord(RecordId Id, RecordId MapSet, IReadOnlyList<MinimapSection> Sections, IReadOnlyList<MinimapRule> Rules);

/// <summary>
/// A piece of a minimap: its image's top-left at (<paramref name="X"/>, <paramref name="Y"/>) in its group's plan, in
/// screen pixels.
/// </summary>
/// <param name="Row">The chunk's row, counting from 1 for A, that the piece is for.</param>
/// <param name="Group">Pieces drawn together: a floor or a building.</param>
/// <param name="Seen">Whether it's seen from the start.</param>
public sealed record MinimapSection(int Row, int Column, int Group, RecordId Image, int X, int Y, bool Seen = false);

/// <summary>
/// In the chunk at <paramref name="Row"/> and <paramref name="Column"/>, chooses piece <paramref name="Section"/>, or none
/// when it's null, while <paramref name="Flag"/> is set and the player is within <paramref name="Bounds"/>.
/// </summary>
public sealed record MinimapRule(int Row, int Column, int? Section, RecordId? Flag = null, IReadOnlyList<MinimapBound>? Bounds = null);

/// <summary>A bound on where the player stands in the chunk, in its own coordinates: below a value, above one, or both.</summary>
public sealed record MinimapBound(MinimapAxis Axis, float? Below = null, float? Above = null);

public enum MinimapAxis { X, Y, Z }
