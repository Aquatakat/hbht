using System.Numerics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Chunks;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Rom.Tests;

/// <summary>
/// A save the port wrote before the data format overhaul (format 1) loads over the NA 1.0 import, plays and is written back as
/// format 2, with every ID it names brought up (ADR 0037).
/// </summary>
/// <remarks>
/// <c>Saves/format-1.json</c> was written by the port at <c>aca1a8b</c> (importer 66, the commit before the overhaul's first
/// slice), from story save A of the reference rig's pak (<c>hbht save from-pak … A</c>, before the final encounter: the party's
/// weapons, armor and shields, 80 stacks, two memories in the Overworld's L08 and eight minimaps). That code's own
/// <c>GameState</c> then remembered a container emptied, a party beaten and three random lines in other chunks, two of them
/// on Borg9s shared between cells, stood the player in the importer's northern sea (<c>"0,13"</c>), and added a kept sheet for a
/// twin dialogue entity (Cadme's, <c>0x1404</c>), which no play can, before writing it with that code's <c>SaveFile</c>.
/// </remarks>
public sealed class SaveFormat1Tests(Na10Import fixture)
{
    private PluginSet Set => fixture.Data.Set;

    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Saves", "format-1.json"));

    private const string Dropped = "Its memory of aidyn:voxel/0x0E87/0 (RandomLine) was left out: Borg9 0x0E87 is 2 chunks', and the save doesn't stand in one of them.";

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

    [Fact]
    public void AFormat1SaveLoadsPlaysAndIsWrittenBackAsFormat2()
    {
        var save = SaveFile.Parse(Fixture, Set);
        Assert.Equal(1, save.Upconverted!.From);
        Assert.Equal([Dropped], save.Upconverted.Dropped);

        var game = GameState.Load(Set, save);
        Assert.Equal([Dropped], game.Problems);

        // A tick of trek where it stands.
        var start = GameStart.For(game);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), start.Set, new VisibleWorld());
        var session = GameSession.Start(game, new Host(), new ZoneEngine(game, streamer), start);
        session.Tick(SessionSettings.Original, () => new PlayerInput(Vector2.Zero), false, _ => null);
        Assert.Equal("0_13", session.Trek.Chunks.ChunkAt(session.Trek.Player.Sphere.Position)?.Name);

        var written = SaveFile.Serialize(game.Save());
        var json = JsonNode.Parse(written)!.AsObject();
        Assert.Equal(2, (int)json["format"]!);
        Assert.Equal("0_13", (string)json["place"]!["chunk"]!);

        var members = json["party"]!["members"]!.AsArray();
        Assert.Equal("aidyn:entity/0x023F", (string)members[0]!["entity"]!);
        Assert.Equal("aidyn:item/0x0734", (string)members[0]!["weapon"]!["item"]!);
        Assert.Equal(3, (int)members[0]!["weapon"]!["charges"]!);
        Assert.Equal("aidyn:item/0x0507", (string)members[0]!["armor"]!["item"]!);
        Assert.Equal("aidyn:item/0x0612", (string)members[0]!["shield"]!["item"]!);
        Assert.Equal(["aidyn:entity/0x0240", "aidyn:entity/0x0242", "aidyn:entity/0x0248", "aidyn:entity/0x02DD"],
            json["party"]!["kept"]!.AsArray().Select(k => (string)k!["entity"]!));
        Assert.Equal(80, json["party"]!["inventory"]!.AsArray().Count);

        Assert.Equal(
            ["aidyn:mapobject/0/L08/2", "aidyn:mapobject/0/L08/2", "aidyn:mapobject/0/B06/14", "aidyn:mapobject/0/A06/0",
                "aidyn:mapobject/8/G01/32", "aidyn:mapobject/0/0_13/0"],
            json["memories"]!.AsArray().Select(m => (string)m!["mapObject"]!));
        Assert.Equal(["aidyn:mapset/10", "aidyn:mapset/11", "aidyn:mapset/12", "aidyn:mapset/14", "aidyn:mapset/4", "aidyn:mapset/6", "aidyn:mapset/7", "aidyn:mapset/9"],
            json["minimaps"]!.AsArray().Select(m => (string)m!["mapSet"]!).Order(StringComparer.Ordinal));
        Assert.All(json["minimaps"]!.AsArray(), m => Assert.Null(m!["minimap"]));

        // Every ID it names is format 2's: a record a plugin has, or a map object in a chunk one has.
        var ids = Regex.Matches(written, "\"([a-z.]+:[a-z]+/[^\"]+)\"").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(ids.Count > 600);
        Assert.All(ids, id =>
        {
            var parsed = RecordId.Parse(id);
            if (parsed.Type == RecordTypes.MapObject)
                Assert.True(Set.Contains(ChunkRecord.ChunkOf(parsed)), id);
            else
                Assert.True(Set.Contains(parsed), id);
        });

        // Written back, it reads as format 2, with nothing to bring up and nothing left out.
        var again = SaveFile.Parse(written, Set);
        Assert.Null(again.Upconverted);
        var reloaded = GameState.Load(Set, again);
        Assert.Empty(reloaded.Problems);
        Assert.Equal(written, SaveFile.Serialize(reloaded.Save()));
    }

    [Fact]
    public void NoFormat1IdIsLeftInTheSaveAsRead()
    {
        var save = SaveFile.Parse(Fixture, Set);
        foreach (var old in new[] { "weapon/", "armor/", "dialogentity/", "dialogue/", "voxel/", "minimap/" })
            Assert.DoesNotContain("aidyn:" + old, SaveFile.Serialize(save));
    }

    [Fact]
    public void AFlagNoPluginHasIsLeftOutAndSaid()
    {
        // The derived journal flags 5556 on that the overhaul's first slice stopped importing were never saved, as no derived flag is;
        // were one named, it's a load problem, not a failure.
        var json = JsonNode.Parse(Fixture)!.AsObject();
        json["flags"]!.AsArray().Add("aidyn:flag/5600");
        var game = GameState.Load(Set, SaveFile.Parse(json.ToJsonString(), Set));
        Assert.Contains("aidyn:flag/5600 isn't a plain flag any plugin has.", game.Problems);
        Assert.DoesNotContain("aidyn:flag/5600", SaveFile.Serialize(game.Save()));
    }
}
