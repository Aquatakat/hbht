using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The World tab's fog: entering a chunk sets its world map region's flag (<c>LoadGameBorgScenes</c>, <c>0x8004960c</c>), outdoors
/// in a map set with no minimap, and the map shows the pieces whose flags are set.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class WorldMapRegionTests(Na10Import fixture)
{
    private Na10Import.Loaded Data => fixture.Data;

    private static readonly RecordId Gwernia = Ids.Flag(788);
    private static readonly RecordId Talewok = Ids.Flag(791);

    private MapSetRecord Set(int n) => Data.MapSets.Single(s => s.Id == RecordId.Decimal(RecordTypes.MapSet, n));

    /// <summary>
    /// The code's table names 160 of the Overworld's 172 chunks, in eighteen regions: the prologue's forest and Gwernia are all
    /// region 1. It names chunks by row and column alone, so Ugarit's M06 carries region 1 as well.
    /// </summary>
    [Fact]
    public void ChunksCarryTheRegionTheirRowAndColumnName()
    {
        var overworld = Set(0);
        Assert.Equal((172, 160), (overworld.Chunks.Count, overworld.Chunks.Count(c => c.Region is not null)));
        Assert.Equal(Enumerable.Range(788, 18), overworld.Chunks.Select(c => c.Region).OfType<RecordId>().Distinct()
            .Select(f => int.Parse(f.Key)).Order());
        Assert.All(new[] { "I08", "J08", "K08", "K09", "L08", "M06", "M07", "M08" },
            name => Assert.Equal(Gwernia, overworld.Chunks.Single(c => c.Name == name).Region));
        Assert.Equal(Talewok, overworld.Chunks.Single(c => c.Name == "I10").Region);
        Assert.Null(overworld.Chunks.Single(c => c.Name == "I16").Region);
        Assert.Equal(Gwernia, Set(13).Chunks.Single(c => c.Name == "M06").Region);
        Assert.Equal(Data.Set.Get<WorldMapRecord>(RecordId.Decimal(RecordTypes.WorldMap, 0)).Pieces.Select(p => p.Flag),
            overworld.Chunks.Select(c => c.Region).OfType<RecordId>().Distinct().OrderBy(f => int.Parse(f.Key)));
    }

    /// <summary>
    /// A new game's party, from its start in I08 through the forest to Gwernia, finds region 1 as trek's world is built, and
    /// Talewok's region on stepping into I10: the World tab shows those two pieces and no other.
    /// </summary>
    [Fact]
    public void ANewGamesPartyFindsTheRegionsItWalksInto()
    {
        var game = GameState.NewGame(Data.Set);
        Assert.False(game.Flags.IsSet(Gwernia));
        var overworld = Set(0);
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), overworld, new VisibleWorld());
        var session = GameSession.Start(game, new Host(), new ZoneEngine(game, streamer), GameStart.OfNewGame(Data.Set));
        Assert.Equal("I08", session.Trek.Chunks.ChunkAt(session.Trek.Player.Sphere.Position)?.Name);
        Assert.True(game.Flags.IsSet(Gwernia));
        Assert.Equal([Gwernia], Pieces(game));

        foreach (var name in new[] { "I09", "I10", "I09", "J09", "K09", "L08", "M08", "M07" })
            WalkInto(session, name);
        Assert.True(game.Flags.IsSet(Talewok));
        Assert.Equal([Gwernia, Talewok], Pieces(game));
    }

    /// <summary>
    /// Outdoors in a map set with a minimap (the Ehud underground's C09) or indoors (Gwernia castle's chunks) finds no region,
    /// but Ugarit's M06, outdoors with none, finds the Overworld's region 1, as in the original.
    /// </summary>
    [Fact]
    public void OnlyOutdoorChunksOfMapSetsWithoutAMinimapFindTheirRegions()
    {
        var game = GameState.NewGame(Data.Set);
        bool Enter(int set, string name)
        {
            var mapSet = Set(set);
            var zone = new ZoneEngine(game, new ChunkStreamer(Data.Set, game, new CollisionWorld(), mapSet, new VisibleWorld()));
            return zone.EnterRegion(mapSet.Chunks.Single(c => c.Name == name));
        }

        var ehud = Set(12).Chunks.Single(c => c.Name == "C09");
        Assert.Equal((ehud.Region, true), (Ids.Flag(789), game.Terrain(ehud.Terrain)!.SkyDrawn));
        Assert.False(Enter(12, "C09"));
        Assert.All(Set(4).Chunks.Where(c => c.Region is not null), c => Assert.False(Enter(4, c.Name)));
        Assert.Empty(Pieces(game));
        Assert.True(Enter(13, "M06"));
        Assert.Equal([Gwernia], Pieces(game));
    }

    /// <summary>A cut-scene started from trek finds the region of its stage's chunk as the stage loads (<c>CutScene.LoadStage</c>).</summary>
    [Fact]
    public void ACutScenesStageFindsItsRegion()
    {
        var overworld = Set(0);
        var (_, map, dialogue, region) = Data.Of<DialogueObject>()
            .Where(d => d.Object.Stage is { } stage && stage.MapSet == overworld.Id)
            .Select(d => (d.Id, d.Map, d.Object, Region: Data.Chunk(d.Object.Stage!)?.RegionFound(Data.Set, overworld.Id)))
            .First(d => d.Region is { } r && r != Gwernia);
        var game = GameState.NewGame(Data.Set);
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), overworld, new VisibleWorld());
        var session = GameSession.Start(game, new Host(), new ZoneEngine(game, streamer), GameStart.OfNewGame(Data.Set));
        session.Cue(Data.Set.Get<DialogueRecord>(dialogue.Script), new ScriptStart(dialogue.RefPointBase, MapRecord.VoxelId(map.Id, dialogue.Index)),
            dialogue.Stage!);
        var step = SessionStep.Trek;
        for (var i = 0; i < 200 && step != SessionStep.CutSceneCued; i++)
            step = session.Tick(SessionSettings.Original, () => new PlayerInput(Vector2.Zero), false, _ => null);
        Assert.False(game.Flags.IsSet(region!.Value));
        session.BeginCutScene();
        Assert.NotNull(session.Scene);
        Assert.True(game.Flags.IsSet(region.Value));
    }

    /// <summary>The flags of the World tab's pieces that show, in the table's order.</summary>
    private RecordId[] Pieces(GameState game) =>
        [.. new WorldMenu(Data.Set, game, new TextLayout(Data.Set.Get<FontRecord>(RecordId.Decimal(RecordTypes.Font, 0))), null, false)
            .Pieces.Select(p => p.Flag)];

    /// <summary>The player steps into the middle of a chunk next to his, set down on its ground, and trek ticks.</summary>
    private static void WalkInto(GameSession session, string name)
    {
        var trek = session.Trek;
        var chunk = trek.Chunks.Find(name) ?? throw new InvalidOperationException($"{name} isn't loaded.");
        var middle = trek.Chunks.Origin(chunk) + new Vector3(trek.Chunks.MapSet.CellSize / 2, 1000, trek.Chunks.MapSet.CellSize / 2);
        trek.Player.Sphere.Position = Body.SetDown(trek.Chunks.World, middle, trek.Player.Sphere.Radius, null).Position;
        trek.Chunks.Update(trek.Player.Sphere.Position);
        session.Tick(SessionSettings.Original, () => new PlayerInput(Vector2.Zero), false, _ => null);
        Assert.Equal(name, trek.Chunks.ChunkAt(trek.Player.Sphere.Position)?.Name);
    }

    private sealed class Host : ITrekHost
    {
        void ITransitionHost.Transition(Transition next) { }
        void ITrekHost.Arrived() { }
        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;
        void IScriptHost.Cue(ScriptCue cue) { }
        void ITrekHost.ConversationChanged() { }
        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request) { }
        void ITrekHost.Changed(LoadedMap map, SceneObject scene) { }
        void ITrekHost.Log(string text) { }
        void ITrekHost.Warn(string text) { }
    }
}
