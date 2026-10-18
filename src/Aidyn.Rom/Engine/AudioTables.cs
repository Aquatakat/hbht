using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Engine;

/// <summary>
/// The Borg12 sequences engine code plays from its tables (docs/systems/audio.md), read from the code segment.
/// </summary>
/// <param name="Script">What a script's <c>PlaySound</c> plays, by its operand (<c>dialoug_SFX</c>).</param>
/// <param name="Containers">A container opening, by its kind.</param>
/// <param name="Explosions">A trapped container exploding, one at random.</param>
/// <param name="OverworldMusicScenes">
/// The Overworld's chunks with music, by their Borg5 scenes: entering at one of the first list plays
/// <see cref="AudioFacts.OverworldMusic"/>'s first, and the second list its second.
/// </param>
/// <param name="JundarMusicScenes">The Jundar interiors' chunks with music, by their Borg5 scenes.</param>
/// <param name="BattleMusic">The battle music with the Expansion Pak: one at random.</param>
public sealed record AudioTables(
    IReadOnlyList<int> Script,
    IReadOnlyList<int> Containers,
    IReadOnlyList<int> Explosions,
    IReadOnlyList<IReadOnlyList<int>> OverworldMusicScenes,
    IReadOnlyList<int> JundarMusicScenes,
    IReadOnlyList<int> BattleMusic)
{
    public static AudioTables Read(RomImage rom)
    {
        var script = rom.Table(CodeTable.ScriptSounds);
        var containers = Be.S32s(rom.Table(CodeTable.ContainerSounds)[..(12 * 4)]);
        var scenes = rom.Table(CodeTable.OverworldMusicScenes);
        var first = Be.U16sUntilZero(scenes);
        var second = Be.U16sUntilZero(scenes, (first.Count + 1) * 2);
        return new AudioTables(
            Be.U32sUntilZero(script),
            containers[..8],
            containers[8..],
            [first, second],
            Be.U16sUntilZero(rom.Table(CodeTable.JundarMusicScenes)),
            Be.U16sUntilZero(rom.Table(CodeTable.BattleMusic)));
    }
}

/// <summary>The sequences engine code names in literals rather than tables (docs/systems/audio.md).</summary>
public static class AudioFacts
{
    /// <summary>The music for the Overworld's two lists of chunks (<see cref="AudioTables.OverworldMusicScenes"/>), with the Expansion Pak.</summary>
    public static readonly int[] OverworldMusic = [0x062C, 0x05EA];

    /// <summary>
    /// The music a chunk plays as the player comes into it (<c>Zones.SetMapSet</c>, 1.0 <c>0x80026264</c>), with the
    /// Expansion Pak: the Overworld's and the Jundar interiors' by their lists, and Chaos Isle's by its column.
    /// </summary>
    public static int? ChunkMusic(MapChunk chunk, AudioTables tables) => chunk.Set switch
    {
        MapSet.Overworld => tables.OverworldMusicScenes.Select((scenes, i) => (scenes, i)).FirstOrDefault(l => l.scenes.Contains(chunk.Scene)) is ({ } _, var list)
            ? OverworldMusic[list] : null,
        MapSet.JundarInteriors => tables.JundarMusicScenes.Contains(chunk.Scene) ? 0x061C : null,
        MapSet.ChaosIsle => (chunk.Column & 2) == 0 ? 0x05D7 : 0x05D5,
        _ => null,
    };

    /// <summary>The music a map set plays as it's entered, one at random, where the chunk has none of its own.</summary>
    public static IReadOnlyList<int> MapMusic(MapSet set, AudioTables tables) => set switch
    {
        MapSet.GwerniaCastle => [0x05FD],
        MapSet.Erromon => [0x0790],
        MapSet.EhudUnder => [0x05DF],
        MapSet.Ugarit => [0x061B],
        MapSet.Battlefields => tables.BattleMusic,
        _ => [],
    };

    public const int Title = 0x0632, Credits = 0x05D9;

    public const int Rain = 0x071B;

    public static readonly int[] Thunder = [0x071C, 0x071D, 0x071E, 0x071F];

    public const int Experience = 0x05E7, LevelUp = 0x05E8, GoldGained = 0x072D, GoldPaid = 0x072E;

    public const int Cursor = 0x073B, PageCursor = 0x073D, Accept = 0x0740, Back = 0x0741, TabBack = 0x074D, TabForward = 0x074E,
        Member = 0x0739, CheatAccepted = 0x0718, CheatRejected = 0x0721;
}
