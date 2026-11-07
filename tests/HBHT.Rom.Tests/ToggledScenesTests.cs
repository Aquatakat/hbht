using System.Numerics;
using HBHT.Core;
using HBHT.Core.Chunks;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Tests;

/// <summary>
/// The lighthouse's lit scenes (<see cref="ToggledScenes"/>, <c>CheckToggleZoneScene</c>): read from every ROM, imported as Overworld
/// A15's and Lighthouse A05's swaps, and drawn by the chunk streamer while 5017 holds.
/// </summary>
public sealed class ToggledScenesTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private static RecordId Scene(int borg) => RecordId.Hex(RecordTypes.Scene, borg);

    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void EveryVersionSwapsTheTwoLighthouseScenesBy5017(RomVersionId id)
    {
        Assert.Equal([new ToggledScene(0x0EA9, 5017, 0x35BA), new ToggledScene(0x11F9, 5017, 0x35AF)], ToggledScenes.Read(TestRoms.Get(id)));
    }

    [Fact]
    public void TheImportSwapsTheFirstSceneOfOverworldA15AndLighthouseA05Alone()
    {
        var swapped = Data.MapSets.SelectMany(s => Data.Set.ChunksIn(s.Id).Where(c => c.Swap is not null).Select(c => (s.Label, c))).ToList();
        Assert.Equal(["Overworld A15", "Lighthouse A05"], swapped.Select(s => $"{s.Label} {s.c.Name}"));
        var (_, a15) = swapped[0];
        Assert.Equal(new SceneSwap(new FlagGate(Ids.Flag(5017)), Scene(0x35BA), Scene(0x0EAD)), a15.Swap);
        var (_, a05) = swapped[1];
        Assert.Equal(new SceneSwap(new FlagGate(Ids.Flag(5017)), Scene(0x35AF), null), a05.Swap);
        Assert.All(swapped, s => Assert.NotNull(Data.Set.FindAsset(s.c.Swap!.Scene!.Value, "glb")));
    }

    [Fact]
    public void LighthouseA05LoadsLitOnceTheScrollsCutSceneHasSet286AndTheChunkLoadsAgain()
    {
        var game = GameState.NewGame(Data.Set);
        var set = Data.MapSets.Single(s => s.Label == "Lighthouse");
        var chunks = new ChunkStreamer(Data.Set, game, new CollisionWorld(), set, new VisibleWorld());
        var a05 = chunks.Origin(Data.Set.ChunksIn(set.Id).Single(c => c.Name == "A05")) + new Vector3(10, 0, 10);
        var loaded = new List<ChunkRecord>();
        chunks.Loaded += (chunk, _) => loaded.Add(chunk);

        chunks.Update(a05);
        Assert.Equal(Scene(0x11F9), chunks.ChunkAt(a05)!.Scene);
        var dark = chunks.MapAt(a05);

        // The cut-scene sets plain 286, which derived 5017 reads; standing still, nothing reloads.
        game.Flags.Set(Ids.Flag(286), true);
        Assert.Equal(Scene(0x11F9), chunks.ChunkAt(a05)!.Scene);

        // The next load of the 3×3 drops the cell and loads it whole again, lit.
        Assert.True(chunks.Update(a05));
        Assert.Equal(Scene(0x35AF), chunks.ChunkAt(a05)!.Scene);
        Assert.NotSame(dark, chunks.MapAt(a05));
        Assert.Equal(Scene(0x35AF), loaded[^1].Scene);
        Assert.False(chunks.Update(a05));

        // So does the pause menu's closing, the other way.
        game.Flags.Set(Ids.Flag(286), false);
        Assert.Empty(chunks.Regate());
        Assert.Equal(Scene(0x11F9), chunks.ChunkAt(a05)!.Scene);
    }
}
