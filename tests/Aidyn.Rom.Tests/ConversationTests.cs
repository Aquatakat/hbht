using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aidyn.Core;
using Aidyn.Core.Cameras;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// A second plugin on top of NA 1.0's, holding the scripts the conversation tests talk through, so the core's
/// <c>GameState</c> and its rules are the real ones and the scripts are small enough to know.
/// </summary>
internal static class ConversationPlugins
{
    public static readonly RecordId Trahern = RecordId.Hex(RecordTypes.Entity, 0x0239);
    public static readonly RecordId Sign = RecordId.Hex(RecordTypes.DialogEntity, 5);

    public static RecordId Script(int n) => RecordId.Parse($"convtest:dialogue/0x{n:x4}");

    public static readonly RecordId Talk = Script(1), Animates = Script(2), Waits = Script(3), Joins = Script(4), Menus = Script(5),
        Ends = Script(6), Starts = Script(7), CutSpawns = Script(8), CutCameras = Script(9), CutItems = Script(10), CutAnimates = Script(11);

    public static readonly RecordId ShortSword = RecordId.Hex(RecordTypes.Weapon, 0x072C), ASpell = RecordId.Hex(RecordTypes.Spell, 0x03EB);

    private static readonly object Lock = new();
    private static PluginSet? _set;

    private static DialogueOp Op(DialogueCommand command, int? value = null, RecordId? target = null) => new(command, value, target);

    private static DialogueNode Line(int index, string text, int? link, params DialogueOp[] ops) =>
        new(index, DialogueNodeKind.Line, text, ops, link is { } l ? [l] : null);

    private static DialogueOp Says(RecordId speaker) => Op(DialogueCommand.SetSpeaker, target: speaker);

    private static readonly DialogueOp End = Op(DialogueCommand.EndDialogue);

    private static DialogueRecord Record(RecordId id, params DialogueNode[] nodes) => new(id, 9000, DialogueMode.Conversation, nodes);

    /// <summary>A cut-scene of Alaron and Trahern.</summary>
    private static DialogueRecord Cut(RecordId id, params DialogueNode[] nodes) =>
        new(id, 9001, DialogueMode.Cutscene, nodes, [GameState.Alaron, Trahern]);

    private static DialogueOp Op(DialogueCommand command, RecordId target) => new(command, null, target);

    private static IEnumerable<DialogueRecord> Scripts() =>
    [
        // Trahern, Alaron, a sign and Trahern again.
        Record(Talk,
            Line(0, "Hello.", 1, Says(Trahern)),
            Line(1, "Hmm.", 2, Says(GameState.Alaron)),
            Line(2, "Woof.", 3, Says(Sign)),
            Line(3, "Bye.", null, Says(Trahern), End)),
        // A line whose leaving plays an animation: it waits for the line to be read again.
        Record(Animates,
            Line(0, "Watch.", 1, Says(Trahern), Op(DialogueCommand.PlayAnimation, 5)),
            Line(1, "Ta-da.", null, Says(Trahern), End)),
        // A wait of three ticks before the second line.
        Record(Waits,
            Line(0, "", 1, Op(DialogueCommand.Wait, 3)),
            Line(1, "Done.", null, Says(Trahern), End)),
        // Someone joins the party as the first line is left.
        Record(Joins,
            Line(0, "Join me.", 1, Says(Trahern), Op(DialogueCommand.AddMember, target: Trahern)),
            Line(1, "Welcome.", null, Says(Trahern), End)),
        // A menu: its choice is Alaron's and the answer Trahern's.
        Record(Menus,
            new DialogueNode(0, DialogueNodeKind.Menu, "What?", [Says(Trahern)], [1, 2]),
            Line(1, "Tell me.", 3, Says(GameState.Alaron)),
            Line(2, "Bye.", null, Says(GameState.Alaron), End),
            Line(3, "Reply.", null, Says(Trahern))),
        // Over before it starts.
        Record(Ends, Line(0, "", null, End)),
        // Alaron and Trahern spawn at reference points 1 and 2, and the camera cuts to 3, aimed at 4. Alaron turns to 2,
        // which snaps, then to 4, which turns, then walks to 4.
        Cut(CutSpawns,
            Line(0, "", 1, Says(GameState.Alaron), Op(DialogueCommand.CreateActor, 1)),
            Line(1, "", 2, Says(Trahern), Op(DialogueCommand.CreateActor, 2), Op(DialogueCommand.CameraCutTo, 3), Op(DialogueCommand.CameraOn, 4)),
            Line(2, "Hello.", 3, Says(Trahern)),
            Line(3, "", 4, Says(GameState.Alaron), Op(DialogueCommand.FaceRefPoint, 2)),
            Line(4, "Turn.", 5, Op(DialogueCommand.FaceRefPoint, 4)),
            Line(5, "", 6, Op(DialogueCommand.WalkTo, 4)),
            Line(6, "Arrived.", null, End)),
        // The camera pans its aim to 1 over 40 ticks (angle-to a point), eases it back to 4 (pan-to a point), waiting each
        // time, then looks out of Trahern's eyes.
        Cut(CutCameras,
            Line(0, "", 1, Says(Trahern), Op(DialogueCommand.CreateActor, 2), Op(DialogueCommand.CameraCutTo, 3), Op(DialogueCommand.CameraOn, 4)),
            Line(1, "", 2, Op(DialogueCommand.CameraTime, 40), Op(DialogueCommand.CameraAngleTo, 1), Op(DialogueCommand.WaitForCamera)),
            Line(2, "", 3, Op(DialogueCommand.CameraPanTo, 4), Op(DialogueCommand.WaitForCamera)),
            Line(3, "Eyes.", null, Op(DialogueCommand.CameraCutToActor, Trahern), End)),
        // Alaron holds a short sword, then a spell, which has no model, then nothing.
        Cut(CutItems,
            Line(0, "", 1, Says(GameState.Alaron), Op(DialogueCommand.CreateActor, 1), Op(DialogueCommand.CameraCutTo, 3), Op(DialogueCommand.CameraOn, 4)),
            Line(1, "Sword.", 2, Op(DialogueCommand.ShowItem, ShortSword)),
            Line(2, "Spell.", 3, Op(DialogueCommand.ShowItem, ASpell)),
            Line(3, "Hide.", 4, Op(DialogueCommand.HideItem)),
            Line(4, "Bye.", null, End)),
        // Alaron plays animation 7, then the screen dips, and a line shows.
        Cut(CutAnimates,
            Line(0, "", 1, Says(GameState.Alaron), Op(DialogueCommand.CreateActor, 1), Op(DialogueCommand.CameraCutTo, 3), Op(DialogueCommand.CameraOn, 4)),
            Line(1, "", 2, Op(DialogueCommand.PlayAnimation, 7)),
            Line(2, "", 3, Op(DialogueCommand.FadeOut)),
            Line(3, "Done.", null, End)),
        // Checks a skill, which a map object remembers, and cuts the camera to reference point 2 from the script's own.
        Record(Starts,
            new DialogueNode(0, DialogueNodeKind.If, "", [Op(DialogueCommand.CheckSkill, 0)], [1, 2]),
            Line(1, "Yes.", null, Says(Trahern), Op(DialogueCommand.CameraCutTo, 2), End),
            Line(2, "No.", null, Says(Trahern), Op(DialogueCommand.CameraCutTo, 2), End)),
    ];

    /// <summary>NA 1.0's plugin with the test scripts' plugin after it, loaded once.</summary>
    public static PluginSet Get(string baseDirectory)
    {
        lock (Lock)
        {
            if (_set is not null)
                return _set;
            var root = Path.Combine(Path.GetTempPath(), "aidyn-convtest-" + Guid.NewGuid().ToString("N"));
            var dir = Path.Combine(root, "convtest");
            Directory.CreateDirectory(Path.Combine(dir, "records"));
            File.WriteAllText(Path.Combine(dir, "plugin.json"), """{ "id": "convtest", "name": "convtest", "version": "1.0.0", "dependencies": ["aidyn"] }""");
            var records = new JsonArray(Scripts().Select(s => JsonSerializer.SerializeToNode(s, PluginJson.Options)).ToArray());
            File.WriteAllText(Path.Combine(dir, "records", "dialogue.json"), records.ToJsonString());
            _set = PluginSet.Load([baseDirectory, dir]);
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            };
            return _set;
        }
    }
}

/// <summary>Records the cues a script sends.</summary>
internal sealed class CueLog : IScriptHost
{
    public List<ScriptCue> Seen { get; } = [];

    public void Cue(ScriptCue cue) => Seen.Add(cue);
}

/// <summary>Records which monster party A talked to.</summary>
internal sealed class TalkHost : IMapObjectHost
{
    public List<MonsterPartyObject> Talks { get; } = [];

    public void Teleport(LoadedMap from, TeleporterObject teleporter) { }

    public void OpenLoot(ItemPile loot, int gold) { }

    public void StartScript(LoadedMap map, DialogueObject dialogue) { }

    public void Talk(LoadedMap map, MonsterPartyObject party) => Talks.Add(party);

    public void Animate(LoadedMap map, SceneObject actor, int request) { }

    public void Changed(LoadedMap map, SceneObject scene) { }

    public void Sound(Aidyn.Core.Audio.OneShot sound) { }
}

/// <summary>
/// Talking to a wanderer, over a hand-made chunk of one party on flat ground, in a new game with small scripts of our own:
/// how a conversation starts, what each press asks of the two talking, each tick, and its end.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class ConversationTests(MapObjectFixture fixture)
{
    private static readonly RecordId MapId = RecordId.Hex(RecordTypes.Map, 0x7001);

    private PluginSet Plugins => ConversationPlugins.Get(fixture.Data.Dir);

    private sealed class Scene
    {
        public required GameState Game { get; init; }
        public required Wanderers Wanderers { get; init; }
        public required Wanderer Wanderer { get; init; }
        public required LoadedMap Map { get; init; }
        public required CollisionWorld World { get; init; }
        public required PlayerMotion Player { get; init; }
        public required FollowCamera Camera { get; init; }
        public required CueLog Cues { get; init; }

        public Body Body => Wanderer.Body;

        public Conversation Talk() => Conversation.TalkTo(Game, Wanderer, Cues, Player)!;
    }

    private static CollisionWorld Ground()
    {
        var world = new CollisionWorld();
        var (a, b, c, d) = (new Vector3(-50, 0, -50), new Vector3(-50, 0, 80), new Vector3(80, 0, -50), new Vector3(80, 0, 80));
        world.Add("ground", [new CollisionTriangle(a, b, c, 4, CollisionRole.Normal, CollisionFlags.None),
            new CollisionTriangle(c, b, d, 4, CollisionRole.Normal, CollisionFlags.None)], Vector3.Zero);
        return world;
    }

    /// <summary>A real talker's entity, which has a body.</summary>
    private RecordId Entity => fixture.Data.Of<MonsterPartyObject>().First(p => p.Object.Dialogue is not null).Object.Entity;

    /// <summary>A plain flag nothing has set, which a party's flag B can set.</summary>
    private static RecordId SpareFlag(GameState game)
    {
        for (var n = 5000; n > 0; n--)
        {
            var id = RecordId.Decimal(RecordTypes.Flag, n);
            if (game.Flags.TryHandle(id, out var handle) && !game.Flags.IsDerived(handle) && !game.Flags.Get(handle))
                return id;
        }
        throw new InvalidOperationException("No spare flag.");
    }

    /// <summary>
    /// The party's body stands 10 along X and 10 along Z, by its home node, which it's in the radius of; the player is
    /// 4 along X and 3 along Z from it.
    /// </summary>
    private Scene Make(RecordId? dialogue, PartyFlags flags = PartyFlags.Walks, bool withUsed = false, float nodeRadius = 1)
    {
        var game = GameState.NewGame(Plugins);
        var node = new WanderNodeObject(0, new Vector3(10, 0, 10), 0, PakVariant.Any, null, nodeRadius, 0, 0, 1000, WanderFlags.Move, []);
        var party = new MonsterPartyObject(1, new Vector3(10, 0, 10), 20, PakVariant.Any, null, [], Entity, null, 0, 0, MapRecord.VoxelId(MapId, 0), flags,
            dialogue, null) { Used = withUsed ? new FlagWrite(SpareFlag(game)) : null };
        var map = LoadedMap.Activate(new MapRecord(MapId, [node, party]), "chunk", game.Flags, game.Memory, game.Party, new Lcg());
        var world = Ground();
        var wanderers = new Wanderers(game.Plugins);
        var wanderer = Assert.Single(wanderers.Load(map, 100, world, game.Memory));
        var at = wanderer.Body.Sphere.Position;
        var player = new PlayerMotion(new Vector3(at.X - 4, 0.5f, at.Z - 3), Vector2.UnitY);
        return new Scene
        {
            Game = game, Wanderers = wanderers, Wanderer = wanderer, Map = map, World = world, Player = player, Cues = new CueLog(),
            Camera = new FollowCamera(player.Sphere.Position, new Vector2(0, 1)),
        };
    }

    private static void AssertNear(Vector2 expected, Vector2 actual, float tolerance = 1e-4f)
    {
        Assert.Equal(expected.X, actual.X, tolerance);
        Assert.Equal(expected.Y, actual.Y, tolerance);
    }

    private static Vector2 Toward(Vector3 from, Vector3 to) => Vector2.Normalize(new Vector2(to.X - from.X, to.Z - from.Z));

    // Starting.

    [Fact]
    public void ThePluginHoldsTheScriptsAndARecruitableTrahern()
    {
        var game = GameState.NewGame(Plugins);
        Assert.All(new[] { ConversationPlugins.Talk, ConversationPlugins.Animates, ConversationPlugins.Waits, ConversationPlugins.Joins,
            ConversationPlugins.Menus, ConversationPlugins.Ends, ConversationPlugins.Starts }, id => Assert.NotNull(Plugins.TryGet<DialogueRecord>(id)));
        Assert.NotNull(game.Recruit(ConversationPlugins.Trahern));
        Assert.False(game.Party.Contains(ConversationPlugins.Trahern));
    }

    [Fact]
    public void ThereIsNoConversationWithAHunterASilentPartyOrAScriptThatIsntThere()
    {
        var hunter = Make(null);
        Assert.Null(Conversation.TalkTo(hunter.Game, hunter.Wanderer, hunter.Cues, hunter.Player));
        Assert.False(hunter.Body.Held);
        Assert.False(hunter.Wanderer.Calmed);

        var silent = Make(ConversationPlugins.Talk, PartyFlags.Walks | PartyFlags.Silent);
        Assert.Null(Conversation.TalkTo(silent.Game, silent.Wanderer, silent.Cues, silent.Player));
        Assert.False(silent.Body.Held);
        Assert.False(silent.Wanderer.Calmed);

        var missing = Make(ConversationPlugins.Script(0x0FFF));
        Assert.Null(Conversation.TalkTo(missing.Game, missing.Wanderer, missing.Cues, missing.Player));
        Assert.False(missing.Body.Held);
    }

    [Fact]
    public void TalkingStartsTheScriptHoldsAndCalmsTheWandererAndSetsItsFlagB()
    {
        var s = Make(ConversationPlugins.Talk, withUsed: true);
        var used = s.Wanderer.Party.Used!.Flag;
        Assert.False(s.Game.Flags.Get(used));

        var conversation = s.Talk();

        Assert.Same(s.Wanderer, conversation.With);
        Assert.Equal(ScriptStatus.Showing, conversation.Runner.Status);
        Assert.Equal("Hello.", conversation.Runner.Page!.Line.Text);
        Assert.False(conversation.Ended);
        Assert.True(s.Wanderer.Calmed);
        Assert.True(s.Body.Held);
        Assert.True(s.Game.Flags.Get(used));
    }

    [Fact]
    public void ThePartyAndTheBodyTurnToFaceEachOther()
    {
        var s = Make(ConversationPlugins.Talk);
        var body = s.Body.Sphere.Position;
        var player = s.Player.Sphere.Position;
        s.Talk();

        Assert.True(s.Body.Turning);
        AssertNear(Toward(body, player), s.Body.TurnTo);
        AssertNear(Toward(player, body), s.Player.TurnTo!.Value);
        Assert.Equal(0.8f, s.Player.TurnTo!.Value.X, 1e-4f);
        Assert.Equal(0.6f, s.Player.TurnTo!.Value.Y, 1e-4f);
    }

    [Fact]
    public void AScriptThatEndsAtOnceLeavesTheConversationEndedButTheWandererStillHeld()
    {
        var s = Make(ConversationPlugins.Ends);
        var conversation = s.Talk();

        Assert.True(conversation.Ended);
        Assert.Null(conversation.Runner.Page);
        Assert.Null(conversation.Runner.Ending!.Fault);
        Assert.True(s.Body.Held);
        Assert.True(s.Wanderer.Calmed);
    }

    [Fact]
    public void AWanderersPartyWithNothingToSetLeavesTheFlagsAlone()
    {
        var s = Make(ConversationPlugins.Talk);
        Assert.Null(s.Wanderer.Party.Used);
        Assert.NotNull(s.Talk());
    }

    [Fact]
    public void TheScriptStartsFromReferencePointZeroAndMapObjectRemembersItsSkillCheckByTheParty()
    {
        var s = Make(ConversationPlugins.Starts);
        var conversation = s.Talk();

        Assert.Equal(ScriptStatus.Showing, conversation.Runner.Status);
        Assert.Contains(s.Cues.Seen, c => c is CameraCue { Command: DialogueCommand.CameraCutTo, RefPoint: 2 });
        var voxel = MapRecord.VoxelId(MapId, s.Wanderer.Party.Index);
        Assert.NotNull(s.Game.Memory.Find(new Memory(voxel, MemoryKind.SkillCheck, 0)));
    }

    [Fact]
    public void OfFindsTheWandererOfAPartyAndFreeTakesItAway()
    {
        var s = Make(ConversationPlugins.Talk);
        Assert.Same(s.Wanderer, s.Wanderers.Of(s.Map, s.Wanderer.Party));
        s.Wanderers.Free(s.Wanderer);
        Assert.Null(s.Wanderers.Of(s.Map, s.Wanderer.Party));
        Assert.Empty(s.Wanderers.All);
    }

    // Pressing.

    [Fact]
    public void BothStandBeforeTheButtonDoesAnythingAndTheSpeakerOfWhatShowsNextTalks()
    {
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        s.Body.Request = 7;
        s.Player.Asked = 9;

        (int? Asked, int Request)? during = null;
        conversation.Press(() =>
        {
            during = (s.Player.Asked, s.Body.Request);
            conversation.Runner.Continue();
        });

        // Both asked to stand, then the line of Alaron's showing: he talks.
        Assert.NotNull(during);
        Assert.Equal(PlayerMotion.Idle, during!.Value.Asked);
        Assert.Equal(PlayerMotion.Idle, during.Value.Request);
        Assert.Equal("Hmm.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(Conversation.TalkRequest, s.Player.Asked);
        Assert.Equal(PlayerMotion.Idle, s.Body.Request);
    }

    [Fact]
    public void AnyOtherEntitysLineHasTheWandererTalkAndASignsHasNobodyTalk()
    {
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        conversation.Press(conversation.Runner.Continue);
        Assert.Equal(PlayerMotion.Idle, s.Body.Request);

        // "Woof.", a dialogue entity's.
        conversation.Press(conversation.Runner.Continue);
        Assert.Equal("Woof.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(PlayerMotion.Idle, s.Body.Request);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);

        // "Bye.", Trahern's: the wanderer talks, and the player stands.
        conversation.Press(conversation.Runner.Continue);
        Assert.Equal("Bye.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(Conversation.TalkRequest, s.Body.Request);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);
    }

    [Fact]
    public void APressThatMovesNothingOnStillAsksTheSpeakerOfWhatShowsToTalk()
    {
        // The first line is Trahern's, so the wanderer talks, not Alaron.
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        conversation.Press(() => { });
        Assert.Equal("Hello.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(Conversation.TalkRequest, s.Body.Request);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);
    }

    [Fact]
    public void AScriptsOwnAnimationMeansNobodyIsAskedToTalkThatPress()
    {
        var s = Make(ConversationPlugins.Animates);
        var conversation = s.Talk();
        conversation.Press(conversation.Runner.Continue);

        Assert.Contains(new AnimateCue(null, 5), s.Cues.Seen);
        Assert.Equal("Watch.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(ConversationPlugins.Trahern, conversation.Runner.Page.Line.Speaker);
        Assert.Equal(PlayerMotion.Idle, s.Body.Request);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);

        // The next press has none, so Trahern talks.
        conversation.Press(conversation.Runner.Continue);
        Assert.Equal("Ta-da.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(Conversation.TalkRequest, s.Body.Request);
    }

    [Fact]
    public void AMenusChoiceIsPressedAndTheAnswersSpeakerTalks()
    {
        var s = Make(ConversationPlugins.Menus);
        var conversation = s.Talk();
        Assert.Equal(["Tell me.", "Bye."], conversation.Runner.Page!.Choices.Select(c => c.Text));

        conversation.Press(() => conversation.Runner.Choose(0));

        Assert.Equal("Reply.", conversation.Runner.Page!.Line.Text);
        Assert.Equal(Conversation.TalkRequest, s.Body.Request);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);
    }

    [Fact]
    public void WhenThePressEndsTheScriptNobodyIsAskedToTalk()
    {
        var s = Make(ConversationPlugins.Menus);
        var conversation = s.Talk();
        conversation.Press(() => conversation.Runner.Choose(1));
        Assert.True(conversation.Ended);
        Assert.Equal(PlayerMotion.Idle, s.Body.Request);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);
    }

    // Ticking.

    [Fact]
    public void ATickPutsBothBackToStandingAndHoldsTheWandererAgain()
    {
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        conversation.Press(conversation.Runner.Continue);
        Assert.Equal(Conversation.TalkRequest, s.Player.Asked);
        s.Body.Held = false;
        s.Wanderer.Calmed = false;
        s.Body.Request = Conversation.TalkRequest;

        Assert.False(conversation.Tick());

        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);
        Assert.Equal(PlayerMotion.Idle, s.Body.Request);
        Assert.True(s.Body.Held);
        Assert.True(s.Wanderer.Calmed);
    }

    [Fact]
    public void ATimerWaitCountsDownATickAtATimeAndTheTickItRunsOutMovesTheScriptOn()
    {
        var s = Make(ConversationPlugins.Waits);
        var conversation = s.Talk();
        Assert.Equal(ScriptWait.Timer, conversation.Runner.Wait);
        Assert.Null(conversation.Runner.Page);

        Assert.False(conversation.Tick());
        Assert.False(conversation.Tick());
        Assert.Equal(ScriptWait.Timer, conversation.Runner.Wait);
        Assert.True(conversation.Tick());

        Assert.Null(conversation.Runner.Wait);
        Assert.Equal("Done.", conversation.Runner.Page!.Line.Text);
        // Nothing to count any more.
        Assert.False(conversation.Tick());
    }

    [Fact]
    public void AConversationFramesTheTwoAndASignLeavesTheCameraBe()
    {
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        for (var i = 0; i < 300; i++)
            conversation.Frame(s.Camera, s.World);
        var middle = (s.Player.Sphere.Position + s.Body.Sphere.Position) * 0.5f;
        Assert.Equal(middle.X, s.Camera.Aim.X, 0.05f);
        Assert.Equal(middle.Z, s.Camera.Aim.Z, 0.05f);

        var sign = Conversation.Show(s.Game, Plugins.Get<DialogueRecord>(ConversationPlugins.Talk), new ScriptStart(), s.Cues, s.Player);
        var camera = new FollowCamera(Vector3.Zero, Vector2.UnitY);
        var (position, aim) = (camera.Position, camera.Aim);
        sign.Frame(camera, s.World);
        Assert.Equal(position, camera.Position);
        Assert.Equal(aim, camera.Aim);
    }

    // Ending.

    private static void Finish(Conversation conversation)
    {
        for (var i = 0; i < 10 && !conversation.Ended; i++)
            conversation.Press(conversation.Runner.Continue);
        Assert.True(conversation.Ended);
    }

    [Fact]
    public void EndingLetsTheBodyGoAndTheWandererSetOffForItsNodeAndGivesTheCameraBack()
    {
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        Finish(conversation);
        // It's wandered off from its node, by more than the node's radius, while it talked.
        s.Body.Sphere.Position.X += 5;
        s.Camera.Pitch = 40;
        s.Camera.Snap = 0;

        conversation.End(s.Wanderers, s.Camera);

        Assert.False(s.Body.Held);
        Assert.False(s.Wanderer.Calmed);
        Assert.True(s.Body.Moving);
        Assert.Equal(s.Wanderer.Goal, s.Body.Goal);
        Assert.Contains(s.Wanderer, s.Wanderers.All);
        Assert.Equal(5, s.Camera.Snap);
        Assert.Equal(s.Camera.DefaultPitch, s.Camera.Pitch);
        Assert.Null(s.Player.Asked);
    }

    [Fact]
    public void AWandererAlreadyWithinItsNodesRadiusJustStandsAfterwards()
    {
        var s = Make(ConversationPlugins.Talk, nodeRadius: 5);
        var conversation = s.Talk();
        Finish(conversation);
        conversation.End(s.Wanderers, s.Camera);

        Assert.False(s.Body.Held);
        Assert.False(s.Wanderer.Calmed);
        Assert.False(s.Body.Moving);
    }

    [Fact]
    public void SomeoneJoiningInTheConversationTakesTheWanderersBodyAway()
    {
        var s = Make(ConversationPlugins.Joins);
        var conversation = s.Talk();
        Assert.False(s.Game.Party.Contains(ConversationPlugins.Trahern));
        Finish(conversation);
        Assert.True(conversation.Runner.Ending!.PartnerJoined);
        Assert.True(s.Game.Party.Contains(ConversationPlugins.Trahern));
        s.Camera.Snap = 0;

        conversation.End(s.Wanderers, s.Camera);

        Assert.DoesNotContain(s.Wanderer, s.Wanderers.All);
        Assert.False(s.Body.Held);
        Assert.Equal(5, s.Camera.Snap);
    }

    [Fact]
    public void ARecruitWhoCantJoinBecauseThePartyIsFullDoesntTakeTheWanderersBodyAway()
    {
        var s = Make(ConversationPlugins.Joins);
        foreach (var member in new[] { 0x024C, 0x0243, 0x024D })
            Assert.True(s.Game.AddMember(RecordId.Hex(RecordTypes.Entity, member)));
        var conversation = s.Talk();

        Finish(conversation);

        // Trahern's AddMember was refused, so the script says no one joined.
        Assert.False(conversation.Runner.Ending!.PartnerJoined);
        Assert.False(s.Game.Party.Contains(ConversationPlugins.Trahern));
        Assert.Equal(4, s.Game.Party.Count);
        conversation.End(s.Wanderers, s.Camera);
        Assert.Contains(s.Wanderer, s.Wanderers.All);
    }

    [Fact]
    public void AnEndingWithNoOneJoinedKeepsTheWanderer()
    {
        var s = Make(ConversationPlugins.Talk);
        var conversation = s.Talk();
        Finish(conversation);
        Assert.False(conversation.Runner.Ending!.PartnerJoined);
        conversation.End(s.Wanderers, s.Camera);
        Assert.Contains(s.Wanderer, s.Wanderers.All);
    }

    [Fact]
    public void ASignHasNoWandererAndEndingItLeavesTheCameraAlone()
    {
        var s = Make(ConversationPlugins.Talk);
        var sign = Conversation.Show(s.Game, Plugins.Get<DialogueRecord>(ConversationPlugins.Talk), new ScriptStart(), s.Cues, s.Player);

        Assert.Null(sign.With);
        Assert.Equal(ScriptStatus.Showing, sign.Runner.Status);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);
        Assert.False(s.Body.Held);

        // Trahern's line, then Alaron's: only he talks, as no one stands for the others.
        sign.Press(sign.Runner.Continue);
        Assert.Equal("Hmm.", sign.Runner.Page!.Line.Text);
        Assert.Equal(Conversation.TalkRequest, s.Player.Asked);
        sign.Press(sign.Runner.Continue);
        Assert.Equal(PlayerMotion.Idle, s.Player.Asked);

        s.Camera.Pitch = 40;
        s.Camera.Snap = 0;
        sign.End(s.Wanderers, s.Camera);
        Assert.Equal(0, s.Camera.Snap);
        Assert.Equal(40, s.Camera.Pitch);
        Assert.Null(s.Player.Asked);
        Assert.Contains(s.Wanderer, s.Wanderers.All);
    }

    // Reaching a body with A.

    private static MapObjectUse UseOver(GameState game, TalkHost host) => new(game, MenuText.From(game.Plugins), host);

    private static SavePointObject Spot(int index, Vector3 at, float radius = 10) => new(index, at, radius, PakVariant.Any, null);

    private static MonsterPartyObject Party(Vector3 at) =>
        new(1, at, 20, PakVariant.Any, null, [], RecordId.Hex(RecordTypes.Entity, 0x300), null, 0, 0, null, PartyFlags.None, ConversationPlugins.Talk, null);

    private (MapObjectUse Use, LoadedMap Map, MonsterPartyObject Party, TalkHost Host) Reaching(Vector3 partyAt, params SavePointObject[] objects)
    {
        var game = GameState.NewGame(Plugins);
        var party = Party(partyAt);
        var map = LoadedMap.Activate(new MapRecord(MapId, [party, .. objects]), "chunk", game.Flags, game.Memory, game.Party, new Lcg());
        var host = new TalkHost();
        return (UseOver(game, host), map, party, host);
    }

    private static readonly Vector3 Player = new(0, 0.5f, 0);
    private static readonly Vector3 Far = new(500, 0, 500);

    [Fact]
    public void ABodyAloneIsWhatAReachesItsPartyObject()
    {
        var (use, map, party, _) = Reaching(Far);
        Assert.Same(party, use.Reach(map, Player, Vector2.UnitX, 0.5f, (party, new Vector3(2, 0, 0), 2)));
        Assert.Null(use.Reach(map, Player, Vector2.UnitX, 0.5f));
    }

    [Fact]
    public void ABodyAndAnObjectAreWeighedByFacingEitherSide()
    {
        // Both 3 away: the body ahead on +X, the object behind on -X.
        var (use, map, party, _) = Reaching(Far, Spot(2, new Vector3(-3, 0, 0)));
        var body = (party, new Vector3(3, 0, 0), 3f);
        Assert.Same(party, use.Reach(map, Player, Vector2.UnitX, 0.5f, body));
        Assert.Same(map[2], use.Reach(map, Player, -Vector2.UnitX, 0.5f, body));
    }

    [Fact]
    public void TheBodyIsWeighedAtThePositionPassedNotThePartyObjectsOwn()
    {
        // The party object itself is at -3, behind him; the body it stands for is ahead on +X.
        var (use, map, party, _) = Reaching(new Vector3(-3, 0, 0), Spot(2, new Vector3(0, 0, -3)));
        Assert.Same(party, use.Reach(map, Player, Vector2.UnitX, 0.5f, (party, new Vector3(3, 0, 0), 3)));
        // Were it weighed at the party's own place, facing would favour the object (0.5) over it (0).
        Assert.Same(map[2], use.Reach(map, Player, Vector2.UnitX, 0.5f, (party, new Vector3(-3, 0, 0), 3)));
    }

    [Fact]
    public void TheBodyIsWeighedByTheDistanceItWasGivenNotThePositionsOwn()
    {
        // The body's position and the object are both 4 away at right angles, and he faces between them.
        var (use, map, party, _) = Reaching(Far, Spot(2, new Vector3(0, 0, 4)));
        var heading = Vector2.Normalize(new Vector2(1, 1));
        Assert.Same(party, use.Reach(map, Player, heading, 0.5f, (party, new Vector3(4, 0, 0), 1)));
        Assert.Same(map[2], use.Reach(map, Player, heading, 0.5f, (party, new Vector3(4, 0, 0), 6)));
    }

    [Fact]
    public void AnObjectAtTheSameScoreBeatsTheBodyBecauseTheLaterWinsATie()
    {
        // At his height, so the object's distance is the 3 the body was given.
        var (use, map, party, _) = Reaching(Far, Spot(2, new Vector3(0, 0.5f, 3)));
        Assert.Same(map[2], use.Reach(map, Player, Vector2.UnitX, 0.5f, (party, new Vector3(0, 0.5f, 3), 3)));
    }

    [Fact]
    public void AnObjectOutOfReachDoesntCompeteWithTheBody()
    {
        var (use, map, party, _) = Reaching(Far, Spot(2, new Vector3(-30, 0, 0), radius: 10));
        Assert.Same(party, use.Reach(map, Player, -Vector2.UnitX, 0.5f, (party, new Vector3(3, 0, 0), 3)));
    }

    [Fact]
    public void UsingTheBodyTalksToItsParty()
    {
        var (use, map, party, host) = Reaching(Far);
        Assert.False(use.UseNearest(map, Player, Vector2.UnitX, 0.5f));
        Assert.Empty(host.Talks);

        Assert.True(use.UseNearest(map, Player, Vector2.UnitX, 0.5f, (party, new Vector3(2, 0, 0), 2)));
        Assert.Same(party, Assert.Single(host.Talks));
    }

    // Every party in the game.

    /// <summary>
    /// Godric's late-game cut-scene (<c>aidyn:dialogue/0x0529</c>), from his party on Overworld map <c>0x0FC5</c>: he joins,
    /// the cut-scene rebuilds trek as it ends, and the chunk loaded again has no body for him, as his party is there only
    /// while his joined flag (90) is clear. Before, the cut-scene's AddMember left him wandering, to be recruited again.
    /// </summary>
    [Fact]
    public void GodricsCutSceneTakesHimOffTheMapWhenTrekIsRebuilt()
    {
        var (_, record, party) = fixture.Data.Of<MonsterPartyObject>()
            .Single(p => p.Map.Id == RecordId.Hex(RecordTypes.Map, 0x0FC5) && p.Object.Index == 26);
        var godric = RecordId.Hex(RecordTypes.Entity, 0x0248);
        Assert.Equal(godric, party.Entity);
        var game = GameState.NewGame(Plugins);
        var map = LoadedMap.Activate(record, "chunk", game.Flags, game.Memory, game.Party, new Lcg());
        var wanderers = new Wanderers(game.Plugins);
        wanderers.Load(map, 1_000_000, new CollisionWorld(), game.Memory);
        var wanderer = Assert.IsType<Wanderer>(wanderers.Of(map, party));

        var player = new PlayerMotion(wanderer.Body.Sphere.Position + new Vector3(-1, 0, -1), Vector2.UnitY);
        var conversation = Assert.IsType<Conversation>(Conversation.TalkTo(game, wanderer, new CueLog(), player));
        Assert.True(conversation.RebuildsTrek);
        var runner = conversation.Runner;
        for (var steps = 0; !conversation.Ended; steps++)
        {
            Assert.True(steps < 500, "The cut-scene doesn't end.");
            if (runner.Status == ScriptStatus.Waiting)
                runner.Resume();
            else if (runner.Page is { Choices.Count: > 0 })
                conversation.Press(() => runner.Choose(0));
            else
                conversation.Press(runner.Continue);
        }
        Assert.True(game.Party.Contains(godric));
        Assert.False(runner.Ending!.PartnerJoined);
        conversation.End(wanderers, new FollowCamera(player.Sphere.Position, Vector2.UnitY));
        // A cut-scene's AddMember doesn't free the speaker: until trek is rebuilt he's still there.
        Assert.NotNull(wanderers.Of(map, party));

        // As the runtime rebuilds trek: the wanderers go, and the chunk loads again with the flags as they are now.
        wanderers.Clear();
        var again = LoadedMap.Activate(record, "chunk", game.Flags, game.Memory, game.Party, new Lcg());
        wanderers.Load(again, 1_000_000, new CollisionWorld(), game.Memory);
        Assert.Null(wanderers.Of(again, party));
    }

    /// <summary>
    /// Every monster party with a script that isn't silent, in every map of the game: each is made active, given a body
    /// on empty ground (a chunk's own collision isn't needed to start a conversation) and talked to on a new game, and
    /// its script must start without a fault.
    /// </summary>
    [Fact]
    public void TalkingToEveryTalkerInTheGameStartsItsScriptWithoutAFault()
    {
        var talkers = fixture.Data.Of<MonsterPartyObject>().Where(p => p.Object.Dialogue is not null).ToList();
        var silent = talkers.Count(p => (p.Object.Flags & PartyFlags.Silent) != 0);
        var started = new Dictionary<string, int>();
        var faults = new List<string>();
        var tested = 0;
        var maps = 0;

        foreach (var group in talkers.Where(p => (p.Object.Flags & PartyFlags.Silent) == 0).GroupBy(p => p.Map.Id))
        {
            maps++;
            var game = GameState.NewGame(Plugins);
            var wanted = group.Select(p => p.Object.Index).ToHashSet();
            // Without their flags' gates, which a new game mostly keeps shut.
            var record = group.First().Map with
            {
                Objects = [.. group.First().Map.Objects.Select(o => o is MonsterPartyObject p && wanted.Contains(p.Index) ? p with { Visible = null, Enabled = null } : o)],
            };
            var map = LoadedMap.Activate(record, "chunk", game.Flags, game.Memory, game.Party, new Lcg());
            var wanderers = new Wanderers(game.Plugins);
            wanderers.Load(map, 1_000_000, new CollisionWorld(), game.Memory);
            foreach (var (id, _, party) in group)
            {
                if (wanderers.Of(map, record.Objects.OfType<MonsterPartyObject>().Single(p => p.Index == party.Index)) is not { } wanderer)
                {
                    faults.Add($"{id}: no body");
                    continue;
                }
                tested++;
                var at = wanderer.Body.Sphere.Position;
                var player = new PlayerMotion(at + new Vector3(-3, 0, -2), Vector2.UnitY);
                var cues = new CueLog();
                var conversation = Conversation.TalkTo(game, wanderer, cues, player);
                if (conversation is null)
                {
                    faults.Add($"{id}: no conversation ({party.Dialogue})");
                    continue;
                }
                var kind = conversation.Runner.Status switch
                {
                    ScriptStatus.Ended => $"ended {conversation.Runner.Ending!.Kind}",
                    var status => status.ToString().ToLowerInvariant(),
                };
                started[kind] = started.GetValueOrDefault(kind) + 1;
                if (conversation.Runner.Ending?.Fault is { } fault)
                    faults.Add($"{id} ({party.Dialogue}): {fault}");
                else if (conversation.Runner.Status == ScriptStatus.Showing && conversation.Runner.Page is null)
                    faults.Add($"{id}: showing nothing");
                Assert.True(wanderer.Body.Held && wanderer.Calmed);
            }
        }

        var summary = $"{talkers.Count} parties have a script ({silent} silent) ({maps} maps hold the non-silent ones); {tested} tested; started as: "
            + string.Join(", ", started.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"));
        TestContext.Current.SendDiagnosticMessage(summary);
        Assert.True(faults.Count == 0, summary + "\n" + string.Join("\n", faults));
        Assert.Equal(talkers.Count - silent, tested);
    }
}
