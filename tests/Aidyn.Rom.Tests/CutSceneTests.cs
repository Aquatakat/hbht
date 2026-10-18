using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Cut-scenes on a hand-made stage, a flat chunk with four reference points, in a new game with small scripts of our
/// own (<see cref="ConversationPlugins"/>): spawning, the camera's moves, turning and walking, held items, animations,
/// the fades and the waits.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CutSceneTests(Na10Import fixture)
{
    /// <summary>The stage is chunk (1, 1) of a map set of 100-unit chunks, so its corner is at (100, 0, 100).</summary>
    private static readonly Vector3 Corner = new(100, 0, 100);

    /// <summary>The reference points, in the chunk's coordinates.</summary>
    private static readonly Vector3[] Points = [default, new(20, 0, 20), new(30, 0, 20), new(25, 5, 40), new(25, 1, 20)];

    private PluginSet Plugins => ConversationPlugins.Get(fixture.Data.Dir);

    private static CutSceneStage Stage()
    {
        var mapId = RecordId.Hex(RecordTypes.Map, 0x7002);
        var map = new MapRecord(mapId, [.. Enumerable.Range(1, 4).Select(i =>
            (MapObject)new ReferencePointObject(i, Points[i], 1, PakVariant.Any, null, i, $"point {i}", null))]);
        var set = new MapSetRecord(RecordId.Decimal(RecordTypes.MapSet, 99), "Test", 100, [new ChunkEntry(1, 1, null, null, mapId, 0)]);
        return new CutSceneStage(set, set.Chunks[0], map, Worlds.Ground(0, 100, Corner));
    }

    private static Vector3 At(int point) => Corner + Points[point];

    /// <summary>Animations whose walk and run move a body along, and whose states last 20 ticks.</summary>
    private sealed class Walking : IActorMotion
    {
        public static readonly Walking Instance = new();

        public int StateLength => 20;

        public float Tick(int request) => request is Body.Walk or Body.Run ? 0.05f : 0;
    }

    private (CutScene Scene, CueLog Cues) Play(RecordId script)
    {
        var game = GameState.NewGame(Plugins);
        var cues = new CueLog();
        var scene = new CutScene(game, Plugins.Get<DialogueRecord>(script), new ScriptStart(), Stage(), cues);
        scene.Start();
        return (scene, cues);
    }

    private static void Ticks(CutScene scene, int n)
    {
        for (var i = 0; i < n; i++)
            scene.Tick(_ => Walking.Instance);
    }

    /// <summary>Ticks until the box takes a press, at most a thousand.</summary>
    private static void UntilAccepts(CutScene scene)
    {
        for (var i = 0; i < 1000 && !scene.Accepts; i++)
            scene.Tick(_ => Walking.Instance);
        Assert.True(scene.Accepts);
    }

    [Fact]
    public void Spawning_sets_actors_down_and_the_first_cut_places_the_camera()
    {
        var (scene, _) = Play(ConversationPlugins.CutSpawns);

        Assert.Equal(2, scene.Actors.Count);
        var alaron = scene.Actor(GameState.Alaron)!;
        // Set down on the ground, 0.01 above it, whatever the reference point's height; facing the original's default.
        Assert.Equal(alaron.Body.Sphere.Radius + 0.01f, alaron.Body.Sphere.Position.Y, 3);
        Assert.Equal(At(1).X, alaron.Body.Sphere.Position.X, 3);
        Assert.Equal(Vector2.Normalize(new Vector2(-1, -1)), alaron.Body.Heading);
        // The screen fades in from black; the box waits for it.
        Assert.Equal(0, scene.Fade.Brightness);
        Assert.False(scene.Accepts);
        Assert.Null(scene.Page);

        scene.Tick(_ => Walking.Instance);
        Assert.Equal(At(3), scene.Camera.Position);
        Assert.Equal(At(4), scene.Camera.Aim);

        UntilAccepts(scene);
        Assert.Equal("Hello.", scene.Page!.Line.Text);
        Assert.Equal(1, scene.Fade.Brightness);
    }

    [Fact]
    public void The_first_turn_snaps_and_later_ones_turn()
    {
        var (scene, _) = Play(ConversationPlugins.CutSpawns);
        UntilAccepts(scene);
        var alaron = scene.Actor(GameState.Alaron)!.Body;

        Assert.True(alaron.SnapNextFace);
        // Moving on reaches node 3, whose turn to point 2 snaps him to face along +X, and node 4, whose turn to
        // point 4 turns him where he stands, a step at a time.
        scene.Press(scene.Runner.Continue);
        Assert.Equal(1, alaron.Heading.X, 4);
        Assert.False(alaron.SnapNextFace);
        Assert.True(alaron.Turning);
        Assert.Equal("Turn.", scene.Page!.Line.Text);
        Assert.Equal(Body.Talk, alaron.Request);
    }

    [Fact]
    public void A_walk_ends_thirty_ticks_after_the_actor_stops_within_a_unit()
    {
        var (scene, _) = Play(ConversationPlugins.CutSpawns);
        UntilAccepts(scene);
        scene.Press(scene.Runner.Continue);
        scene.Press(scene.Runner.Continue);
        Assert.Equal(ScriptWait.Move, scene.Runner.Wait);
        Assert.Null(scene.Page);
        var alaron = scene.Actor(GameState.Alaron)!.Body;

        var ticks = 0;
        var stopped = -1;
        while (scene.Runner.Wait == ScriptWait.Move && ticks < 2000)
        {
            scene.Tick(_ => Walking.Instance);
            ticks++;
            if (stopped < 0 && !alaron.Steered)
                stopped = ticks;
        }
        Assert.Equal("Arrived.", scene.Page?.Line.Text ?? scene.Runner.Page?.Line.Text);
        var to = new Vector2(At(4).X, At(4).Z);
        Assert.InRange(Vector2.Distance(new Vector2(alaron.Sphere.Position.X, alaron.Sphere.Position.Z), to), 0, 1.2f);
        Assert.Equal(stopped + CutScene.StillTicks, ticks);
    }

    [Fact]
    public void An_actor_that_cant_get_there_is_put_there()
    {
        var (scene, _) = Play(ConversationPlugins.CutSpawns);
        UntilAccepts(scene);
        scene.Press(scene.Runner.Continue);
        scene.Press(scene.Runner.Continue);
        var alaron = scene.Actor(GameState.Alaron)!.Body;
        // Animations that never move him.
        var still = new StillMotion();
        for (var i = 0; i < CutScene.GiveUpTicks + 5; i++)
            scene.Tick(_ => still);
        Assert.Equal(At(4).X, alaron.Sphere.Position.X, 3);
        Assert.Equal(At(4).Z, alaron.Sphere.Position.Z, 3);
    }

    private sealed class StillMotion : IActorMotion
    {
        public int StateLength => 20;

        public float Tick(int request) => 0;
    }

    [Fact]
    public void Angle_to_a_point_pans_and_pan_to_a_point_eases()
    {
        var (scene, _) = Play(ConversationPlugins.CutCameras);
        // The cuts and the pan all start as the script does, so the first tick cuts and takes the pan's first step,
        // a fortieth of the way at a steady speed.
        Ticks(scene, 1);
        Assert.Equal(At(3), scene.Camera.Position);
        Assert.Equal(At(4).X + (At(1).X - At(4).X) / 40, scene.Camera.Aim.X, 3);
        Assert.Equal(ScriptWait.Camera, scene.Runner.Wait);
        Ticks(scene, 9);
        Assert.Equal(At(4).X + (At(1).X - At(4).X) * 0.25f, scene.Camera.Aim.X, 3);
        Assert.True(scene.Cameras.Busy);
        Assert.False(scene.Accepts);

        Ticks(scene, 30);
        Assert.Equal(At(1).X, scene.Camera.Aim.X, 3);
        Assert.Equal(At(1).Y, scene.Camera.Aim.Y, 3);
        // The wait ends once the camera's been still a step, and the ease back starts.
        Ticks(scene, 2);
        Assert.Equal(ScriptWait.Camera, scene.Runner.Wait);
        var start = scene.Camera.Aim;
        Ticks(scene, 10);
        var eased = (1 - MathF.Cos(MathF.PI / 4)) / 2;
        Assert.InRange((scene.Camera.Aim.X - At(1).X) / (At(4).X - At(1).X), eased - 0.03f, eased + 0.03f);
        Assert.NotEqual(start, scene.Camera.Aim);
    }

    [Fact]
    public void A_point_of_view_cut_puts_the_camera_at_the_actors_eyes_and_hides_it()
    {
        var (scene, _) = Play(ConversationPlugins.CutCameras);
        UntilAccepts(scene);
        var trahern = scene.Actor(ConversationPlugins.Trahern)!;
        Assert.True(trahern.Hidden);
        Assert.Equal("Eyes.", scene.Page!.Line.Text);
        var eyes = trahern.Eyes;
        Assert.Equal(eyes, scene.Camera.Position);
        var feet = trahern.Body.Sphere.Position.Y - trahern.Body.Sphere.Radius;
        Assert.Equal(feet + trahern.Look.Height * trahern.Look.Scale - 0.2f, eyes.Y, 2);
    }

    [Fact]
    public void Items_are_held_by_their_model_and_one_without_leaves_the_last()
    {
        var (scene, _) = Play(ConversationPlugins.CutItems);
        UntilAccepts(scene);
        var alaron = scene.Actor(GameState.Alaron)!;
        scene.Press(scene.Runner.Continue);
        Assert.Equal(RecordId.Hex(RecordTypes.Scene, 0x34E5), alaron.Held);
        scene.Press(scene.Runner.Continue);
        Assert.Equal(RecordId.Hex(RecordTypes.Scene, 0x34E5), alaron.Held);
        scene.Press(scene.Runner.Continue);
        Assert.Null(alaron.Held);
    }

    [Fact]
    public void An_animation_waits_for_its_state_and_a_fade_out_dips_and_comes_back()
    {
        var (scene, _) = Play(ConversationPlugins.CutAnimates);
        Assert.Equal(ScriptWait.Animation, scene.Runner.Wait);
        var alaron = scene.Actor(GameState.Alaron)!.Body;
        Assert.Equal(7, alaron.Request);

        // The state's 20 ticks, counted from the tick after the request is seen.
        Ticks(scene, 19);
        Assert.Equal(ScriptWait.Animation, scene.Runner.Wait);
        Ticks(scene, 1);
        Assert.NotEqual(ScriptWait.Animation, scene.Runner.Wait);
        Assert.Equal(Body.Idle, alaron.Request);

        // The fade-in's over by now (60 ticks), so the script's fade dips: down for 60 ticks and up for 60.
        Ticks(scene, 40);
        Assert.Equal(FadeMode.Dip, scene.Fade.Mode);
        Assert.Null(scene.Page);
        UntilAccepts(scene);
        Assert.Equal(1, scene.Fade.Brightness);
        Assert.Equal("Done.", scene.Page!.Line.Text);
    }

    [Fact]
    public void The_end_fades_out_and_then_its_over()
    {
        var (scene, _) = Play(ConversationPlugins.CutItems);
        for (var i = 0; i < 4; i++)
        {
            UntilAccepts(scene);
            scene.Press(scene.Runner.Continue);
        }
        Assert.Equal(ScriptStatus.Ended, scene.Runner.Status);
        Assert.False(scene.Over);
        // A second down, a sixtieth a tick, which single precision leaves a hair above black for a tick more, then three
        // frames at black.
        var ticks = 0;
        while (!scene.Over && ticks < 200)
        {
            scene.Tick(_ => Walking.Instance);
            ticks++;
        }
        Assert.Equal(61 + 3 * Cadence.FrameTicks, ticks);
        Assert.Empty(scene.Problems);
    }
}
