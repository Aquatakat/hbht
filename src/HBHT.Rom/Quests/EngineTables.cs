using HBHT.Rom.Db;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Quests;

/// <summary>A recruitable member's flags. The game keeps them in step with the party.</summary>
/// <param name="Joined">Set when the member joins, and cleared when they're removed alive.</param>
/// <param name="Left">Cleared when the member joins, and set when they're removed alive. 0 for Brenna, who never leaves.</param>
/// <param name="Died">Set while the member is in the party and dead.</param>
public sealed record PartyMemberFlags(ItemId Member, ushort Joined, ushort Left, ushort Died);

/// <summary>A key item: it gets its own inventory slot, and a flag that says the party has it.</summary>
/// <param name="Held">Set when the party gains the item, and cleared when it has none left.</param>
/// <param name="Region">A world map region flag that gaining the item also sets, or 0.</param>
public sealed record RomKeyItemFlags(ItemId Item, ushort Held, ushort Region);

/// <summary>Entering this overworld chunk sets the flag of the world map region it's in.</summary>
public sealed record RegionChunk(int Row, int Column, ushort Flag);

/// <summary>A piece of the world map, shown while its flag is set.</summary>
/// <param name="Image">The piece's Borg8.</param>
public sealed record WorldMapPiece(ushort Flag, int Image, int X, int Y);

/// <summary>What happens when one of the <see cref="EngineTables.ScriptEndings"/> scripts ends.</summary>
public enum ScriptEnding
{
    /// <summary>The ending cinematic plays, then the credits.</summary>
    EndingCinematic = 1,
    /// <summary>The game ends and goes back to the title screen.</summary>
    GameOver = 2,
    /// <summary>The Roog cinematic plays if flag 3442 is set.</summary>
    RoogCinematic = 3,
}

/// <summary>
/// Content tables that engine code keeps in the code segment (roadmap 1H), read from the ROM.
/// Flags the code names directly, rather than through a table, are in <see cref="EngineFlags"/>.
/// </summary>
/// <param name="EntityList">
/// The entity index the code's tables use, as the entity it names. The code looks an entity up
/// by its position in this list, not by its ID.
/// </param>
/// <param name="CampDialogueFlags">
/// When the party camps, a dialogue object nearby whose flag A is one of these starts instead of
/// the usual campfire.
/// </param>
/// <param name="ScriptBattleFlags">
/// Winning the battle one of these scripts starts sets the flag. Other scripts' battles leave
/// the encounter flag as it was.
/// </param>
/// <param name="CampfireScenes">Eight hours pass when one of these scripts ends.</param>
/// <param name="GameOverScript">
/// When the party is wiped out, this script starts from its dialogue object in the Dreamscape
/// Borg9. The code's list has two more death scenes that it never uses.
/// </param>
public sealed record EngineTables(
    IReadOnlyList<ItemId> EntityList,
    IReadOnlyList<PartyMemberFlags> Party,
    IReadOnlyList<RomKeyItemFlags> KeyItems,
    IReadOnlyList<RegionChunk> Regions,
    IReadOnlyList<ushort> CampDialogueFlags,
    IReadOnlyDictionary<int, ushort> ScriptBattleFlags,
    IReadOnlyList<int> CampfireScenes,
    IReadOnlyDictionary<int, ScriptEnding> ScriptEndings,
    int GameOverScript,
    IReadOnlyList<WorldMapPiece> WorldMap)
{
    private const ushort End = 0xFFFF;

    public static EngineTables Read(RomImage rom)
    {
        var entityList = rom.Table(CodeTable.EntityList).ToArray()
            .Select(b => ItemId.Of(DbType.Entity, b)).ToList();

        var joins = Rows(Be.U16s(rom.Table(CodeTable.PartyFlags)), 3, CodeTable.PartyFlags);
        var deaths = Rows(Be.U16s(rom.Table(CodeTable.PartyDeathFlags)), 2, CodeTable.PartyDeathFlags)
            .ToDictionary(r => r[0], r => r[1]);
        if (joins.Count != deaths.Count || joins.Any(r => !deaths.ContainsKey(r[0])))
            throw new InvalidDataException("The party and death flag tables name different members.");
        var party = joins.Select(r => new PartyMemberFlags(entityList[r[0]], r[1], r[2], deaths[r[0]])).ToList();

        var held = Be.U16s(rom.Table(CodeTable.KeyItemFlags));
        var items = Be.U16s(rom.Table(CodeTable.KeyItems));
        var regions = Be.U16s(rom.Table(CodeTable.KeyItemRegionFlags));
        if (held.Length != items.Length || regions.Length != items.Length)
            throw new InvalidDataException("The key item tables differ in length.");
        var keyItems = items.Select((item, i) => new RomKeyItemFlags(new ItemId(item), held[i], regions[i])).ToList();

        // Entered chunks are matched until an entry with a zero field.
        var regionData = rom.Table(CodeTable.RegionFlags);
        var chunks = new List<RegionChunk>();
        for (var at = 0; ; at += 12)
        {
            var row = Be.U32(regionData, at);
            var column = Be.U32(regionData, at + 4);
            var flag = Be.U32(regionData, at + 8);
            if (row == 0 || column == 0 || flag == 0)
                break;
            chunks.Add(new RegionChunk((int)row, (int)column, (ushort)flag));
        }

        var camp = Be.U16s(rom.Table(CodeTable.CampDialogueFlags)).TakeWhile(f => f != End).ToList();
        var battles = Rows(Be.U16s(rom.Table(CodeTable.ScriptBattleFlags)), 2, CodeTable.ScriptBattleFlags)
            .ToDictionary(r => (int)r[0], r => r[1]);

        var campfire = new List<int>();
        var campfireData = rom.Table(CodeTable.CampfireScenes);
        for (var at = 0; (int)Be.U32(campfireData, at) is var script and not -1; at += 4)
            campfire.Add(script);

        // Script, padding and ending, up to a script of -1.
        var endings = new Dictionary<int, ScriptEnding>();
        var endingData = rom.Table(CodeTable.ScriptEndings);
        for (var at = 0; (int)Be.U32(endingData, at) is var script and not -1; at += 8)
        {
            var ending = Be.U16(endingData, at + 6);
            endings.Add(script, Enum.IsDefined((ScriptEnding)ending)
                ? (ScriptEnding)ending
                : throw new InvalidDataException($"Script {script} has an unknown ending, {ending}."));
        }

        // Pieces up to an all-zero entry.
        var pieces = Be.U16s(rom.Table(CodeTable.WorldMapPieces)).Chunk(4)
            .TakeWhile(r => r.Any(v => v != 0))
            .Select(r => new WorldMapPiece(r[0], r[1], r[2], r[3]))
            .ToList();

        var gameOver = (int)Be.U32(rom.Table(CodeTable.GameOverScenes), 0);

        return new EngineTables(entityList, party, keyItems, chunks, camp, battles, campfire, endings, gameOver, pieces);
    }

    /// <summary>Fixed-width rows up to the one that starts with 0xFFFF, which must be the table's last.</summary>
    private static List<ushort[]> Rows(ushort[] values, int width, CodeTable table)
    {
        var rows = values.Chunk(width).ToList();
        var end = rows.FindIndex(r => r[0] == End);
        if (end != rows.Count - 1)
            throw new InvalidDataException($"The {table} table doesn't end where expected.");
        return rows[..end];
    }
}
