using HBHT.Core;
using HBHT.Plugins;
using HBHT.Plugins.Saves;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

/// <summary>
/// The four story saves on the user's 1.0 pak (<see cref="ControllerPakTests.Na10Pak"/>, the reference rig's "Existing saves") as the
/// port's saves and games, for tests that want a party and a place in the story. Slot A is before the final encounter (Overworld
/// M06), B before Shamsuk's Tower (P17), C before Rooughah (B01), D before Rooughah in Terminor (G01). The party is Alaron, Rheda,
/// Brenna and Sholeh, with Niesen in Sholeh's place in B. The tests skip without the pak.
/// </summary>
internal static class StorySaves
{
    public static string Label(char slot) => slot switch
    {
        'A' => "Before final encounter",
        'B' => "Before Shamsuk's Tower",
        'C' => "Before Rooughah",
        'D' => "Before Rooughah, in Terminor",
        _ => throw new ArgumentOutOfRangeException(nameof(slot)),
    };

    /// <summary>A slot as the port's save, as <c>hbht save from-pak</c> makes it.</summary>
    public static SaveGame Save(PluginSet set, char slot)
    {
        var pak = ControllerPak.Read(ControllerPakTests.Pak(ControllerPakTests.Na10Pak));
        var note = pak.Notes.Single(n => n.Label == $"CHRONICLES.{slot}");
        var saves = new PakSaves(set, TestRoms.Get(RomVersionId.Na10));
        var problems = new List<string>();
        var save = saves.ToSave(saves.Decode(pak.ReadNote(note)), Label(slot), problems);
        Assert.Empty(problems);
        return save;
    }

    /// <summary>A slot loaded into a game.</summary>
    public static GameState Game(PluginSet set, char slot)
    {
        var game = GameState.Load(set, Save(set, slot));
        Assert.Empty(game.Problems);
        return game;
    }
}
