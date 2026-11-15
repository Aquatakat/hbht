using System.Numerics;
using HBHT.Core;
using HBHT.Core.Cameras;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using HBHT.TestSupport;
using static HBHT.TestSupport.ScriptNodes;
using Xunit;

namespace HBHT.Core.Tests;

/// <summary>The enhanced conversation camera's shot choice, read from scripts alone (<see cref="DialoguePlanner"/>).</summary>
public class DialoguePlannerTests
{
    private static readonly RecordId Npc = Entity(0x0123);
    private static readonly RecordId Alaron = GameState.Alaron;

    private static ScriptRecord Make(params ScriptNode[] nodes) => new(Script(0x0F00), 1, ScriptMode.Conversation, nodes);

    /// <summary>The pages of a straight run of lines, each shown in turn.</summary>
    private static IEnumerable<DialoguePage> Pages(ScriptRecord script) =>
        script.Nodes.Where(n => n.Text.Length > 0).Select(n => new DialoguePage(n.Index, Talk(n.Speaker), n.Text));

    private static Talker Talk(RecordId? s) => s is null ? Talker.None : s == Alaron ? Talker.Player : Talker.Npc;

    private static ScriptRecord Lines(params (RecordId? Speaker, string Text)[] lines) =>
        Make(lines.Select((l, i) => Line(i, l.Text, i + 1 < lines.Length ? i + 1 : null, l.Speaker)).ToArray());

    private static DialogueShot Ots(Talker t) => new(DialogueShotKind.OverShoulder, t);

    [Fact]
    public void TheFirstPageIsTheTwoShotAndALineGoesOverTheListenersShoulderToItsSpeaker()
    {
        var script = Lines((Npc, "Hello."), (Alaron, "Hi."), (Npc, "Well met."));
        Assert.Equal([DialogueShot.TwoShot, Ots(Talker.Player), Ots(Talker.Npc)], DialoguePlanner.Plan(script, Pages(script)));
    }

    [Fact]
    public void APageOfChoicesLooksAtTheNpc()
    {
        var script = Lines((Npc, "Hello."), (Alaron, "Hi."), (Npc, "What will it be?"));
        var pages = Pages(script).ToList();
        pages[2] = pages[2] with { Choices = 2 };
        pages[1] = pages[1];
        Assert.Equal(Ots(Talker.Npc), DialoguePlanner.Plan(script, pages)[2]);
        // Shouted by the npc, it punches in on him.
        pages[2] = pages[2] with { Text = "Choose, FOOL!" };
        Assert.Equal(new DialogueShot(DialogueShotKind.Punch, Talker.Npc), DialoguePlanner.Plan(script, pages)[2]);
        // Even when Alaron's portrait is on the menu's line, and he shouts, it looks at the npc.
        pages[2] = pages[2] with { Speaker = Talker.Player };
        Assert.Equal(Ots(Talker.Npc), DialoguePlanner.Plan(script, pages)[2]);
    }

    [Fact]
    public void AShoutedLinePunchesInOnItsSpeaker()
    {
        var script = Lines((Npc, "Hello."), (Alaron, "Hi."), (Npc, "Get OUT of here!"), (Alaron, "Fine."), (Npc, "Go!!"));
        var shots = DialoguePlanner.Plan(script, Pages(script));
        Assert.Equal(new DialogueShot(DialogueShotKind.Punch, Talker.Npc), shots[2]);
        Assert.Equal(new DialogueShot(DialogueShotKind.Punch, Talker.Npc), shots[4]);
        Assert.False(DialoguePlanner.Shouted("I am Alaron, of Gwernia."));
        Assert.False(DialoguePlanner.Shouted("Go!"));
    }

    [Fact]
    public void ALongRunGoesWideAtItsThirdLineAndOnceToTheListenerAtItsFifth()
    {
        var lines = Enumerable.Range(0, 12).Select(i => ((RecordId?)Npc, $"Line {i}.")).Prepend((Alaron, "Tell me.")).ToArray();
        var script = Lines(lines);
        var shots = DialoguePlanner.Plan(script, Pages(script));
        // The npc's lines are pages 1 to 12: the run's 1st is page 1.
        Assert.Equal(Ots(Talker.Npc), shots[1]);
        Assert.Equal(Ots(Talker.Npc), shots[2]);
        Assert.Equal(DialogueShot.Wide, shots[3]);
        Assert.Equal(Ots(Talker.Npc), shots[4]);
        Assert.Equal(new DialogueShot(DialogueShotKind.Single, Talker.Player), shots[5]);
        Assert.Single(shots, s => s == DialogueShot.Wide);
        Assert.Single(shots, s => s.Subject == Talker.Player && s.Kind == DialogueShotKind.Single);
        // Past the listener it turns between the single and over the shoulder.
        Assert.Contains(new DialogueShot(DialogueShotKind.Single, Talker.Npc), shots.Skip(6));
        Assert.Contains(Ots(Talker.Npc), shots.Skip(6));
    }

    [Fact]
    public void ALongRunsCycleBringsTheWideAndTheListenerBackButNeverOnItsLastLine()
    {
        var script = Lines(Enumerable.Range(0, 17).Select(i => ((RecordId?)Npc, $"Line {i}.")).Prepend((Alaron, "Tell me.")).ToArray());
        var shots = DialoguePlanner.Plan(script, Pages(script));
        // Run lines 1 to 17 are pages 1 to 17: wide at 3 and 13, Alaron at 5, and the cycle's listener at 17 is the last line.
        Assert.Equal([3, 13], shots.Select((s, i) => (s, i)).Where(x => x.s == DialogueShot.Wide).Select(x => x.i));
        Assert.Equal([5], shots.Select((s, i) => (s, i)).Where(x => x.s == new DialogueShot(DialogueShotKind.Single, Talker.Player)).Select(x => x.i));
        Assert.Equal(Ots(Talker.Npc), shots[17]);
    }

    [Fact]
    public void WhatAScriptWillWantIsReadFromItAlone()
    {
        DialogueShotKind[] plain = [DialogueShotKind.TwoShot, DialogueShotKind.OverShoulder];
        Assert.Equal(plain, DialoguePlanner.WantsOf(Lines((Npc, "Hello."), (Alaron, "Hi."))));
        Assert.Equal([.. plain, DialogueShotKind.Single], DialoguePlanner.WantsOf(Lines((Npc, "Hello."), (Alaron, "GET OUT!"))));
        Assert.Equal([.. plain, DialogueShotKind.Wide], DialoguePlanner.WantsOf(Make(Line(0, "Die.", 1, Npc), Line(1, "", null, null, new ScriptOp.BattleStart()))));
        var tale = Lines(Enumerable.Range(0, 6).Select(i => ((RecordId?)Npc, $"Line {i}.")).ToArray());
        Assert.Equal([.. plain, DialogueShotKind.Wide, DialogueShotKind.Single], DialoguePlanner.WantsOf(tale));
    }

    [Fact]
    public void TheTuningIsSetByName()
    {
        var t = DialogueTuning.Default.Parse("singledistance=3.5,RunPattern=SOWL,AcrossYaws=0;30,MinShotTicks=60");
        Assert.Equal((3.5f, "SOWL", 60), (t.SingleDistance, t.RunPattern, t.MinShotTicks));
        Assert.Equal([0f, 30f], t.AcrossYaws);
        Assert.Throws<FormatException>(() => DialogueTuning.Default.Parse("Nope=1"));
    }

    [Fact]
    public void AShortRunIsReadAheadAndDoesntGoWide()
    {
        var script = Lines((Alaron, "Tell me."), (Npc, "One."), (Npc, "Two."), (Npc, "Three."), (Alaron, "Thanks."));
        Assert.DoesNotContain(DialogueShot.Wide, DialoguePlanner.Plan(script, Pages(script)));
    }

    [Fact]
    public void OpsAreBeats()
    {
        var battle = Make(Line(0, "Hello.", 1, Npc), Line(1, "Hi.", 2, Alaron), Line(2, "Die!", 3, Npc), Line(3, "", null, null, new ScriptOp.BattleStart()));
        Assert.Equal(DialogueShot.Wide, DialoguePlanner.Plan(battle, Pages(battle))[2]);

        var join = Make(Line(0, "Hello.", 1, Npc), Line(1, "Hi.", 2, Alaron), Line(2, "I'll come along.", 3, Npc),
            Line(3, "", null, null, new ScriptOp.PartyAddMember(Npc)));
        Assert.Equal(DialogueShot.TwoShot, DialoguePlanner.Plan(join, Pages(join))[2]);

        var check = Make(Line(0, "Hello.", 1, Npc), Line(1, "A locked chest.", 2, Npc),
            Node(2, ScriptNodeKind.If, "", [3, 3], null, new ScriptOp.CheckSkill(RecordIds.Skill(3))), Line(3, "Done.", null, Npc));
        Assert.Equal(new DialogueShot(DialogueShotKind.Single, Talker.Player), DialoguePlanner.Plan(check, Pages(check))[1]);
    }

    [Fact]
    public void ASpeakerWithNoBodyHoldsTheTwoShot()
    {
        var script = Lines((Npc, "Hello."), (Alaron, "Hi."), (null, "The wind howls."));
        Assert.Equal(DialogueShot.TwoShot, DialoguePlanner.Plan(script, Pages(script))[2]);
    }

    [Fact]
    public void APageSoonerThanTheMinimumKeepsAShotShowingItsSpeakerElseTakesTheTwoShot()
    {
        var script = Lines((Npc, "Hello."), (Alaron, "Hi."), (Npc, "Well."), (Npc, "Well met."));
        var planner = new DialoguePlanner(script);
        var pages = Pages(script).ToList();
        Assert.Equal(DialogueShot.TwoShot, planner.Next(pages[0], 0));
        Assert.Equal(Ots(Talker.Player), planner.Next(pages[1], 200));
        // 30 ticks later the npc speaks: the shot at Alaron doesn't show him, so the two-shot.
        Assert.Equal(DialogueShot.TwoShot, planner.Next(pages[2], 230));
        // The two-shot shows him: kept.
        Assert.Equal(DialogueShot.TwoShot, planner.Next(pages[3], 250));
    }
}

/// <summary>The enhanced conversation camera's side of the line and its fitting, on small worlds (<see cref="DialogueCamera"/>).</summary>
public class DialogueFittingTests
{
    private static readonly DialogueTuning T = DialogueTuning.Default;

    /// <summary>Alaron at the origin and the npc 2 along +X, so the line's perpendicular is +Z.</summary>
    private static DialogueScene Scene(CollisionWorld world, VisibleWorld? visible = null, params ShotSubject[] others) =>
        new(world, visible, new TalkingBody(new(0, 0.5f, 0), 0.5f, new(0.15f, 1.62f, 0)),
            new TalkingBody(new(2, 0.5f, 0), 0.5f, new(1.85f, 1.62f, 0)), others);

    /// <summary>A wall along X at <paramref name="z"/>, facing both ways, 4 high.</summary>
    private static IEnumerable<CollisionTriangle> WallAlongX(float z)
    {
        Vector3 a = new(-20, 0, z), b = new(20, 0, z), c = new(-20, 4, z), d = new(20, 4, z);
        yield return Worlds.Triangle(a, b, c);
        yield return Worlds.Triangle(c, b, d);
        yield return Worlds.Triangle(a, c, b);
        yield return Worlds.Triangle(c, d, b);
    }

    private static CollisionWorld With(params float[] walls)
    {
        var world = Worlds.Flat();
        for (var i = 0; i < walls.Length; i++)
            world.Add(Worlds.Chunk(i), WallAlongX(walls[i]), Vector3.Zero);
        return world;
    }

    [Fact]
    public void TheSideIsTheCamerasUnlessTheOtherHasClearlyMoreRoom()
    {
        var open = Scene(Worlds.Flat());
        Assert.Equal(1, DialogueCamera.ChooseSide(open, new Vector3(1, 3, 5), T));
        Assert.Equal(-1, DialogueCamera.ChooseSide(open, new Vector3(1, 3, -5), T));
        // A wall 1 off on the camera's side: the other is taken.
        var walled = Scene(With(1.2f));
        Assert.Equal(-1, DialogueCamera.ChooseSide(walled, new Vector3(1, 3, 0.8f), T));
        // A wall on the other side doesn't move it.
        Assert.Equal(1, DialogueCamera.ChooseSide(Scene(With(-1.2f)), new Vector3(1, 3, 5), T));
    }

    [Fact]
    public void TheSideIsTheOneWhereMoreOfTheShotsTheScriptWantsFit()
    {
        // A wall 4.6 off on the camera's side (not so near the room alone would leave it): the two-shot and the shoulders fit both sides, the wide only the other.
        var scene = Scene(With(4.6f));
        var camera = new Vector3(1, 3, 2);
        DialogueShotKind[] plain = [DialogueShotKind.TwoShot, DialogueShotKind.OverShoulder];
        Assert.Equal(DialogueCamera.Fitting(scene, 1, T, plain), DialogueCamera.Fitting(scene, -1, T, plain));
        // On a tie the room decides, which here would leave the camera's side only for a margin wider than its own.
        var t = T with { SideMargin = 10 };
        Assert.Equal(1, DialogueCamera.ChooseSide(scene, camera, t, plain));
        Assert.Equal(-1, DialogueCamera.ChooseSide(scene, camera, t, [.. plain, DialogueShotKind.Wide]));
    }

    [Fact]
    public void EveryShotFitsInTheOpenOnItsSideAndNeverCrossesTheLine()
    {
        var scene = Scene(Worlds.Flat());
        foreach (var side in new[] { 1, -1 })
        {
            foreach (var shot in new[] { DialogueShot.TwoShot, DialogueShot.Wide, new(DialogueShotKind.OverShoulder, Talker.Npc),
                         new(DialogueShotKind.OverShoulder, Talker.Player), new(DialogueShotKind.Single, Talker.Npc), new(DialogueShotKind.Punch, Talker.Player) })
            {
                var (shown, at) = DialogueCamera.Fit(scene, side, shot, T);
                Assert.Equal(shot, shown);
                // A single is clean: the listener at most a sliver at the edge. Over the shoulder, the listener in a third or so.
                if (shot.Subject != Talker.None)
                {
                    var cover = DialogueCamera.ListenerCover(scene, at!.Value, shot.Subject == Talker.Npc ? Talker.Player : Talker.Npc, T);
                    Assert.True(shot.Kind == DialogueShotKind.OverShoulder ? cover is > 0.1f and < 0.4f : cover <= T.SingleSliver, $"{shot}: {cover}");
                }
                Assert.Equal(side, MathF.Sign(at!.Value.Eye.Z));
                // A medium shot: the face is under a quarter of the frame's height.
                var frame = 2 * Vector3.Distance(at.Value.Eye, at.Value.Aim) * MathF.Tan(T.FieldOfView * MathF.PI / 360);
                Assert.True(0.35f / frame < 0.25f, $"{shot}: frame {frame}");
            }
        }
    }

    [Fact]
    public void AShotThatDoesntFitFallsDownTheLadderToTheOriginalsFraming()
    {
        Assert.Equal([new(DialogueShotKind.OverShoulder, Talker.Npc), new(DialogueShotKind.Single, Talker.Npc), DialogueShot.TwoShot, DialogueShot.Wide],
            DialogueCamera.Ladder(new DialogueShot(DialogueShotKind.OverShoulder, Talker.Npc)));
        // A single that can't be clean is an over-the-shoulder; a wide falls to the two-shot.
        Assert.Equal([new(DialogueShotKind.Punch, Talker.Npc), new(DialogueShotKind.Single, Talker.Npc), new(DialogueShotKind.OverShoulder, Talker.Npc),
            DialogueShot.TwoShot, DialogueShot.Wide], DialogueCamera.Ladder(new DialogueShot(DialogueShotKind.Punch, Talker.Npc)));
        Assert.Equal([DialogueShot.Wide, DialogueShot.TwoShot], DialogueCamera.Ladder(DialogueShot.Wide));
        // A corridor 1.8 wide: nothing fits beside the line.
        var (shown, at) = DialogueCamera.Fit(Scene(With(0.9f, -0.9f)), 1, new DialogueShot(DialogueShotKind.OverShoulder, Talker.Npc), T);
        Assert.Equal(DialogueShotKind.Original, shown.Kind);
        Assert.Null(at);
        // A wall 3 off: the shoulder fits, the wide doesn't, and falls to the original's framing (it's last on its ladder).
        var room = Scene(With(3f));
        Assert.Equal(DialogueShotKind.OverShoulder, DialogueCamera.Fit(room, 1, new DialogueShot(DialogueShotKind.OverShoulder, Talker.Npc), T).Shown.Kind);
        Assert.Equal(DialogueShotKind.TwoShot, DialogueCamera.Fit(room, 1, DialogueShot.Wide, T).Shown.Kind);
    }

    [Fact]
    public void AWallSeenFromBehindStillStopsAShot()
    {
        // One-sided, facing away from the line: a sweep out from the focus passes it, the sweep back meets it.
        var world = Worlds.Flat();
        Vector3 a = new(-20, 0, 1.5f), b = new(20, 0, 1.5f), c = new(-20, 4, 1.5f), d = new(20, 4, 1.5f);
        world.Add(Worlds.Chunk(0), [Worlds.Triangle(a, b, c), Worlds.Triangle(c, b, d)], Vector3.Zero);
        var (shown, _) = DialogueCamera.Fit(Scene(world), 1, DialogueShot.TwoShot, T);
        Assert.NotEqual(DialogueShotKind.TwoShot, shown.Kind);
    }

    [Fact]
    public void TheDrawnWorldAndOtherBodiesStopAShot()
    {
        var visible = new VisibleWorld();
        visible.Add(Worlds.Chunk(0), WallAlongX(2.5f).Drawn(), Vector3.Zero);
        Assert.NotEqual(DialogueShotKind.Wide, DialogueCamera.Fit(Scene(Worlds.Flat(), visible), 1, DialogueShot.Wide, T).Shown.Kind);

        DialogueCamera.TryPlace(Scene(Worlds.Flat()), 1, new DialogueShot(DialogueShotKind.Single, Talker.Npc), T, out var single);
        var between = Vector3.Lerp(single.Eye, new Vector3(1.85f, 1.62f, 0), 0.5f);
        var crowded = Scene(Worlds.Flat(), null, new ShotSubject(between, 0.5f));
        Assert.NotEqual(single, DialogueCamera.TryPlace(crowded, 1, new DialogueShot(DialogueShotKind.Single, Talker.Npc), T, out var moved) ? moved : default);
    }

    [Fact]
    public void ACutIsTakenOnANewShotAndTheShotDriftsInSlowly()
    {
        var scene = Scene(Worlds.Flat());
        var script = new ScriptRecord(Script(0x0F00), 1, ScriptMode.Conversation,
            [Line(0, "Hello.", 1, Entity(0x0123)), Line(1, "Hi.", null, GameState.Alaron)]);
        var camera = new FollowCamera(new Vector3(1, 0.5f, 5), new Vector2(0, -1));
        var director = new DialogueCamera(new DialoguePlanner(script));
        var first = new DialoguePage(0, Talker.Npc, "Hello.");
        director.Step(camera, scene, first, "a");
        Assert.True(camera.TakeCut());
        Assert.Equal(DialogueShot.TwoShot, director.Shown);
        var start = camera.Position;
        for (var i = 0; i < 120; i++)
            director.Step(camera, scene, first, "a");
        Assert.False(camera.TakeCut());
        var drifted = Vector3.Distance(start, camera.Position);
        Assert.InRange(drifted, 0.05f, T.DriftMost);
        director.Step(camera, scene, new DialoguePage(1, Talker.Player, "Hi."), "b");
        Assert.True(camera.TakeCut());
        Assert.Equal(new DialogueShot(DialogueShotKind.OverShoulder, Talker.Player), director.Shown);
        Assert.Equal(1, director.Side);
    }
}
