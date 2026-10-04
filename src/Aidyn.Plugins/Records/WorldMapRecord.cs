using System.Numerics;

namespace Aidyn.Plugins.Records;

/// <summary>
/// The World tab's map (<c>aidyn:worldmap/0</c>, the only one; <c>WidgetMenuWorldMap</c>): the pieces that fill it in as
/// the party finds their regions, the towns' names, and where the party's ring goes. Positions are in the tab's
/// 320 × 240 frame, each image's top-left.
/// </summary>
/// <param name="Pieces">Drawn in order, tinted as the tab's text, while their flags are set (<c>worldMapPieces</c>).</param>
/// <param name="Towns">The towns' names with their dots, always drawn, untinted.</param>
/// <param name="Marker">The party's ring, drawn while the player is in <paramref name="MapSet"/>.</param>
/// <param name="MapSet">The map set the ring follows the player in: the Overworld.</param>
/// <param name="Origin">Where the ring's top-left is for row 1 and column <paramref name="FirstColumn"/>.</param>
/// <param name="Step">How far the ring moves a row (X) and a column (Y), truncated.</param>
public sealed record WorldMapRecord(
    RecordId Id,
    IReadOnlyList<MapPiece> Pieces,
    IReadOnlyList<MapTown> Towns,
    RecordId Marker,
    RecordId MapSet,
    Vector2 Origin,
    int FirstColumn,
    Vector2 Step);

/// <summary>A region of the world map, shown while <paramref name="Flag"/> is set.</summary>
public sealed record MapPiece(RecordId Flag, RecordId Image, int X, int Y);

/// <summary>A town's name and dot on the world map.</summary>
public sealed record MapTown(RecordId Image, int X, int Y);
