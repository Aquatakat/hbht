namespace Aidyn.Rom.Engine;

/// <summary>A town the World tab marks.</summary>
/// <param name="Image">The town's name, a Borg8.</param>
public sealed record WorldMapTown(int Image, int X, int Y);

/// <summary>
/// The World tab's towns (<c>WorldMapTowns</c>), each a Borg8 of its name and where it goes, read from the code segment.
/// The map's pieces are the other table of the tab (<see cref="Quests.EngineTables.WorldMap"/>).
/// </summary>
public static class WorldMapTowns
{
    private const int EntrySize = 6;

    public static IReadOnlyList<WorldMapTown> Read(RomImage rom)
    {
        var data = rom.Table(CodeTable.WorldMapTowns);
        var towns = new WorldMapTown[data.Length / EntrySize];
        for (var i = 0; i < towns.Length; i++)
            towns[i] = new WorldMapTown(Be.U16(data, i * EntrySize), Be.S16(data, i * EntrySize + 2), Be.S16(data, i * EntrySize + 4));
        return towns;
    }
}
