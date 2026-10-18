using Aidyn.Core.Flags;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins.Records;
using Aidyn.Plugins;

namespace Aidyn.Core.Tests;

public class ScriptRunnerTests
{
    private static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);
    private static readonly RecordId Trahern = RecordId.Hex(RecordTypes.Entity, 0x0239);
    private static readonly RecordId Goblin = RecordId.Hex(RecordTypes.Entity, 0x0280);
    private static readonly RecordId Sword = RecordId.Hex(RecordTypes.Weapon, 0x073A);
    private static readonly RecordId Chest = RecordId.Parse("aidyn:voxel/0x1A20/3");
    private static readonly RecordId ScriptId = RecordId.Hex(RecordTypes.Dialogue, 0x0400);
    private static readonly RecordId Rumours = RecordId.Hex(RecordTypes.Dialogue, 0x0325);

    /// <summary>Plain flags 0–9, with 1 set; 9 is the new journal entry flag.</summary>
    private static FlagNetwork Flags() => new(Enumerable.Range(0, 10).Select(i => new FlagRecord(Flag(i), Initial: i == 1)), Flag(9));

    private sealed class World : IScriptWorld
    {
        public HashSet<RecordId> Members { get; } = [];
        public HashSet<RecordId> Items { get; } = [];
        public Dictionary<int, int> Skills { get; } = [];
        public Dictionary<Memory, int> Memory { get; } = [];
        public bool Healed { get; private set; }
        public List<(RecordId, int)> Experience { get; } = [];

        public bool InParty(RecordId entity) => Members.Contains(entity);
        public bool HasItem(RecordId item) => Items.Contains(item);
        public int SkillCheck(int skill, IRandom random) => Skills.GetValueOrDefault(skill);
        public int BestStat(int stat) => 0;
        public int WorstStat(int stat) => 0;
        public int MemberStat(RecordId? member, int stat) => 0;
        public int PartySize { get; set; } = 1;
        public string? PlayerName { get; set; } = "Alaron";
        public uint Gold { get; set; }
        public void AddItem(RecordId item) => Items.Add(item);
        public bool TakeItem(RecordId item) => Items.Remove(item);
        public bool AddMember(RecordId entity) => Members.Count < 4 && Members.Add(entity);
        public void RemoveMember(RecordId entity) => Members.Remove(entity);
        public void CampHeal() => Healed = true;
        public bool TryRecall(Memory memory, out int value) => Memory.TryGetValue(memory, out value);
        public void Remember(Memory memory, int value) => Memory[memory] = value;

        public bool AddExp(RecordId member, int amount)
        {
            if (!Members.Contains(member))
                return false;
            Experience.Add((member, amount));
            return true;
        }
    }

    private sealed class Host : IScriptHost
    {
        public List<ScriptCue> Cues { get; } = [];
        public void Cue(ScriptCue cue) => Cues.Add(cue);
    }

    /// <summary>The value that makes <see cref="RandomExtensions.Range"/> over <paramref name="count"/> numbers give <paramref name="n"/>.</summary>
    private static uint Draw(int n, int count) => (uint)(((ulong)n << 32) / (ulong)count + 1);

    private static DialogueNode Line(int index, string text, int? link = null, params DialogueOp[] ops) =>
        new(index, DialogueNodeKind.Line, text, ops.Length == 0 ? null : ops, link is { } l ? [l] : null);

    private static DialogueNode Choice(int index, string text, int? link, DialogueRange? selects, params DialogueOp[] ops) =>
        Line(index, text, link, ops) with { Selects = selects };

    private static DialogueNode Menu(int index, string text, int[] links, params DialogueOp[] ops) =>
        new(index, DialogueNodeKind.Menu, text, ops.Length == 0 ? null : ops, links);

    private static DialogueNode If(int index, DialogueOp condition, int then, int otherwise) =>
        new(index, DialogueNodeKind.If, "", [condition], [then, otherwise]);

    private static DialogueNode Case(int index, string text, DialogueOp condition, params int[] links) =>
        new(index, DialogueNodeKind.Case, text, [condition], links);

    private static DialogueOp Op(DialogueCommand command, int? value = null, RecordId? target = null) => new(command, value, target);

    private static readonly DialogueOp End = Op(DialogueCommand.EndDialogue);

    private static DialogueRecord Script(DialogueMode mode, params DialogueNode[] nodes) => new(ScriptId, 1234, mode, nodes);

    private sealed record Run(ScriptRunner Runner, FlagNetwork Flags, World World, Host Host);

    private static Run Start(DialogueRecord script, World? world = null, IRandom? random = null, ScriptStart? start = null,
        FlagNetwork? flags = null, IEnumerable<DialogueRecord>? others = null, RecordId? randomSkillChecks = null)
    {
        flags ??= Flags();
        world ??= new World();
        var host = new Host();
        var scripts = (others ?? []).Append(script).ToDictionary(s => s.Id);
        var runner = new ScriptRunner(script, flags, world, host, random ?? new Lcg(), id => scripts.GetValueOrDefault(id), start,
            randomSkillChecks);
        runner.Start();
        return new Run(runner, flags, world, host);
    }

    private static string Text(ScriptRunner runner) => runner.Page!.Line.Text;

    private static List<string> Choices(ScriptRunner runner) => [.. runner.Page!.Choices.Select(c => c.Text)];

    [Fact]
    public void LinesShowInTurnAndTheLastOneEndsTheScript()
    {
        var run = Start(Script(DialogueMode.Conversation,
            Line(0, "Hello.", 1),
            Line(1, "Goodbye.", null, End)));

        Assert.Equal("Hello.", Text(run.Runner));
        run.Runner.Continue();
        Assert.Equal("Goodbye.", Text(run.Runner));
        run.Runner.Continue();
        Assert.Equal(ScriptStatus.Ended, run.Runner.Status);
        Assert.Equal(ScriptEndKind.Ended, run.Runner.Ending!.Kind);
        Assert.Null(run.Runner.Ending.Fault);
    }

    [Fact]
    public void ThePlayersNameIsTheFirstMembersAsItStandsWhenEachPageIsShown()
    {
        var world = new World { PlayerName = "Eric" };
        var run = Start(Script(DialogueMode.Conversation,
            Line(0, "Hail, {player}.", 1),
            Menu(1, "What will {player} do?", [2]),
            Choice(2, "{player} leaves.", null, null, End)), world);

        Assert.Equal("Hail, Eric.", Text(run.Runner));
        world.PlayerName = "Alaron the Bold";
        run.Runner.Continue();
        Assert.Equal("What will Alaron the Bold do?", Text(run.Runner));
        Assert.Equal(["Alaron the Bold leaves."], Choices(run.Runner));
    }

    [Fact]
    public void ThePlayersNameStaysAsTheRecordHoldsItWithNoOneInTheParty()
    {
        var run = Start(Script(DialogueMode.Conversation, Line(0, "Hail, {player}.", null, End)), new World { PlayerName = null });

        Assert.Equal("Hail, {player}.", Text(run.Runner));
    }

    [Fact]
    public void ALinesEffectRunsWhenItIsLeftAndOnlyTheFirstRuns()
    {
        var run = Start(Script(DialogueMode.Conversation,
            Line(0, "Hello.", 1, Op(DialogueCommand.SetFlag, target: Flag(2)), Op(DialogueCommand.SetFlag, target: Flag(3))),
            Line(1, "Goodbye.", null, End)));

        Assert.False(run.Flags.Get(2));
        run.Runner.Continue();
        Assert.True(run.Flags.Get(2));
        Assert.False(run.Flags.Get(3));
    }

    [Fact]
    public void ALineWithNoTextIsWalkedThrough()
    {
        var run = Start(Script(DialogueMode.Conversation,
            Line(0, "", 1, Op(DialogueCommand.SetFlag, target: Flag(2))),
            Line(1, "Goodbye.", null, End)));

        Assert.True(run.Flags.Get(2));
        Assert.Equal("Goodbye.", Text(run.Runner));
    }

    [Theory]
    [InlineData(1, "Just you.")]
    [InlineData(2, "Many of you.")]
    [InlineData(0, "Many of you.")]
    public void AnIfTakesItsFirstLinkOnlyForExactlyOne(int partySize, string expected)
    {
        var run = Start(Script(DialogueMode.Conversation,
            If(0, Op(DialogueCommand.CheckPartySize), 1, 2),
            Line(1, "Just you.", null, End),
            Line(2, "Many of you.", null, End)), new World { PartySize = partySize });

        Assert.Equal(expected, Text(run.Runner));
    }

    private static DialogueRecord Shopkeeper(DialogueMode mode) => Script(mode,
        Menu(0, "What'll it be?", [1, 2], Op(DialogueCommand.SetSpeaker, target: Trahern)),
        Line(1, "Tell me about the goblins.", 3, Op(DialogueCommand.SetSpeaker, target: Alaron)),
        Line(2, "Goodbye.", null, End),
        Line(3, "They're back.", null, Op(DialogueCommand.SetSpeaker, target: Trahern)));

    [Fact]
    public void AMenuHidesTheChoicesTakenAndALineWithNoLinkGoesBackToIt()
    {
        var run = Start(Shopkeeper(DialogueMode.Conversation));
        Assert.Equal(["Tell me about the goblins.", "Goodbye."], Choices(run.Runner));

        run.Runner.Choose(0);
        Assert.Equal("They're back.", Text(run.Runner));
        Assert.Empty(run.Runner.Page!.Choices);
        run.Runner.Continue();

        Assert.Equal("What'll it be?", Text(run.Runner));
        Assert.Equal(["Goodbye."], Choices(run.Runner));
        Assert.True(run.Runner.Visited(1));
    }

    [Theory]
    [InlineData(DialogueMode.Backdrop)]
    [InlineData(DialogueMode.Help)]
    public void BackdropsAndTheHelpMenuOfferChoicesAgain(DialogueMode mode)
    {
        var run = Start(Shopkeeper(mode));
        run.Runner.Choose(0);
        run.Runner.Continue();

        Assert.Equal(["Tell me about the goblins.", "Goodbye."], Choices(run.Runner));
    }

    [Fact]
    public void EachLineCarriesTheSpeakerWhenItWasAdded()
    {
        var run = Start(Shopkeeper(DialogueMode.Conversation));

        Assert.Equal(Trahern, run.Runner.Page!.Line.Speaker);
        Assert.Equal([Alaron, Alaron], run.Runner.Page.Choices.Select(c => c.Speaker));
        // Reaching the choices set the speaker as they were added.
        Assert.Equal(Alaron, run.Runner.Speaker);
    }

    [Fact]
    public void ChoosingAChoiceItCannotLeaveEndsWithAFault()
    {
        var run = Start(Script(DialogueMode.Conversation,
            Menu(0, "What'll it be?", [1]),
            Menu(1, "A menu as a choice.", [0])));

        run.Runner.Choose(0);

        Assert.Equal(ScriptStatus.Ended, run.Runner.Status);
        Assert.Contains("Invalid node to evaluate", run.Runner.Ending!.Fault);
    }

    private static DialogueRecord Haggle() => Script(DialogueMode.Conversation,
        Case(0, "How much have you got?", Op(DialogueCommand.CheckGold), 1, 2, 3),
        Choice(1, "Nothing at all.", null, new(0, 0), End),
        Choice(2, "Some.", null, new(1, 99), End),
        Choice(3, "Plenty.", null, new(50, null), End));

    [Fact]
    public void ACaseWithOneMatchShowsItsTextThenTakesTheMatch()
    {
        var run = Start(Haggle(), new World { Gold = 10 });

        Assert.Equal("How much have you got?", Text(run.Runner));
        Assert.Empty(run.Runner.Page!.Choices);
        run.Runner.Continue();
        Assert.Equal("Some.", Text(run.Runner));
    }

    [Fact]
    public void ACaseWithSeveralMatchesOffersThem()
    {
        var run = Start(Haggle(), new World { Gold = 70 });

        Assert.Equal(["Some.", "Plenty."], Choices(run.Runner));
    }

    [Fact]
    public void ACaseReadsGoldAs16BitsAnd65535AsNoValue()
    {
        Assert.Equal("Nothing at all.", Continued(0x10000));
        // 65535 is the original's mark for "no value", so leaving the case finds nothing to take.
        var run = Start(Haggle(), new World { Gold = 0xFFFF });
        run.Runner.Continue();
        Assert.Contains("Invalid node to evaluate", run.Runner.Ending!.Fault);

        static string Continued(uint gold)
        {
            var run = Start(Haggle(), new World { Gold = gold });
            run.Runner.Continue();
            return Text(run.Runner);
        }
    }

    [Fact]
    public void WalkingWaitsForTheRuntimeAndCountsFromTheScriptsReferencePoint()
    {
        var run = Start(Script(DialogueMode.Cutscene,
            Line(0, "Follow me.", 1, Op(DialogueCommand.SetSpeaker, target: Trahern), Op(DialogueCommand.RunTo, 4)),
            Line(1, "Here we are.", null, End)), start: new ScriptStart(RefPoint: 20));

        run.Runner.Continue();
        Assert.Equal(ScriptStatus.Waiting, run.Runner.Status);
        Assert.Equal(ScriptWait.Move, run.Runner.Wait);
        Assert.Null(run.Runner.Page);
        Assert.Equal(new MoveCue(Trahern, 24, Run: true), run.Host.Cues[^1]);

        run.Runner.Resume();
        Assert.Equal("Here we are.", Text(run.Runner));
    }

    [Fact]
    public void AWaitCountsDownTicks()
    {
        var run = Start(Script(DialogueMode.Cutscene,
            Line(0, "", 1, Op(DialogueCommand.Wait, 10)),
            Line(1, "Done.", null, End)));

        Assert.Equal(ScriptWait.Timer, run.Runner.Wait);
        run.Runner.Step(4);
        run.Runner.Step(5);
        Assert.Equal(ScriptWait.Timer, run.Runner.Wait);
        run.Runner.Step(1);
        Assert.Equal("Done.", Text(run.Runner));
    }

    [Fact]
    public void AnAnimationWithNoActorWaitsForTheLastLineToBeReadAgain()
    {
        var run = Start(Script(DialogueMode.Conversation,
            Line(0, "Watch this.", 1, Op(DialogueCommand.SetSpeaker, target: Trahern), Op(DialogueCommand.PlayAnimation, 5)),
            Line(1, "Ta-da.", null, End)));

        run.Runner.Continue();

        Assert.Equal(new AnimateCue(null, 5), run.Host.Cues[^1]);
        Assert.Equal(ScriptWait.Player, run.Runner.Wait);
        Assert.Equal("Watch this.", Text(run.Runner));
        run.Runner.Continue();
        Assert.Equal("Ta-da.", Text(run.Runner));
    }

    [Fact]
    public void ACutSceneAnimatesOnlyActorsItHasSpawned()
    {
        var script = Script(DialogueMode.Cutscene,
            Line(0, "", 1, Op(DialogueCommand.SetSpeaker, target: Trahern), Op(DialogueCommand.CreateActor, 2), Op(DialogueCommand.PlayAnimation, 5)),
            Line(1, "Ta-da.", null, End)) with { Actors = [Trahern] };

        var run = Start(script);

        Assert.Equal(new SpawnCue(Trahern, 2), run.Host.Cues[0]);
        Assert.Equal(new AnimateCue(Trahern, 5), run.Host.Cues[1]);
        Assert.Equal(ScriptWait.Animation, run.Runner.Wait);
    }

    private static DialogueRecord RumourScript(int blankNodes) => new(Rumours, 900, DialogueMode.Conversation,
    [
        Line(0, "First rumour.", 1), Line(1, "Second rumour.", 2), Line(2, "Third rumour.", null, End),
    ], BlankNodes: blankNodes);

    private static DialogueRecord Gossip() => Script(DialogueMode.Conversation,
        Line(0, "", 1, Op(DialogueCommand.RandomLine, target: Rumours)),
        Line(1, "That's all I know.", null, End));

    [Theory]
    [InlineData(0, 0, "First rumour.")]
    [InlineData(66, 0, "Second rumour.")]
    [InlineData(67, 0, "Third rumour.")]
    [InlineData(100, 0, "Third rumour.")]
    // A blank node counts, and shows node 0's text.
    [InlineData(74, 1, "Third rumour.")]
    [InlineData(75, 1, "First rumour.")]
    public void ARandomLineIsPickedByARollOfTheOtherScriptsNodes(int roll, int blankNodes, string expected)
    {
        var run = Start(Gossip(), random: new ScriptedRandom(Draw(roll, 101)), others: [RumourScript(blankNodes)]);

        Assert.Equal(expected, Text(run.Runner));
        Assert.Equal(ScriptWait.Player, run.Runner.Wait);
        run.Runner.Continue();
        Assert.Equal("That's all I know.", Text(run.Runner));
    }

    [Fact]
    public void TheMapObjectRemembersTheRandomLine()
    {
        var world = new World();
        var start = new ScriptStart(MapObject: Chest);
        var first = Start(Gossip(), world, new ScriptedRandom(Draw(40, 101)), start, others: [RumourScript(0)]);
        var again = Start(Gossip(), world, new ScriptedRandom(Draw(90, 101)), start, others: [RumourScript(0)]);

        Assert.Equal("Second rumour.", Text(first.Runner));
        Assert.Equal("Second rumour.", Text(again.Runner));
        Assert.Equal(40, world.Memory[new Memory(Chest, MemoryKind.RandomLine)]);
    }

    private static DialogueRecord LockPicking(int skill) => Script(DialogueMode.Conversation,
        Case(0, "A lock.", Op(DialogueCommand.CheckSkill, skill), 1, 2),
        Choice(1, "Too hard.", null, new(0, 4), End),
        Choice(2, "It opens.", null, new(5, null), End));

    [Fact]
    public void ASkillCheckIsRememberedByTheMapObject()
    {
        var world = new World { Skills = { [4] = 7 } };
        var start = new ScriptStart(MapObject: Chest);
        var first = Start(LockPicking(4), world, start: start);
        world.Skills[4] = 2;
        var again = Start(LockPicking(4), world, start: start);

        first.Runner.Continue();
        again.Runner.Continue();
        Assert.Equal("It opens.", Text(first.Runner));
        Assert.Equal("It opens.", Text(again.Runner));
    }

    [Fact]
    public void TroubadourChecksAreNotRemembered()
    {
        var world = new World { Skills = { [9] = 7 } };
        Start(LockPicking(9), world, start: new ScriptStart(MapObject: Chest));

        Assert.Empty(world.Memory);
    }

    [Fact]
    public void WhileTheRandomFlagIsSetSkillChecksRoll()
    {
        var flags = Flags();
        flags.Set(5, true);
        var run = Start(LockPicking(4), new World { Skills = { [4] = 7 } }, new ScriptedRandom(Draw(3, 22)), flags: flags, randomSkillChecks: Flag(5));

        run.Runner.Continue();
        Assert.Equal("Too hard.", Text(run.Runner));
    }

    [Theory]
    [InlineData(DialogueMode.Cutscene, true)]
    [InlineData(DialogueMode.Backdrop, true)]
    [InlineData(DialogueMode.Conversation, false)]
    public void ScriptsWithADoneFlagSetItWhenTheyEnd(DialogueMode mode, bool set)
    {
        var script = Script(mode, Line(0, "The end.", null, End)) with { DoneFlag = set ? Flag(4) : null };
        var run = Start(script);

        Assert.False(run.Flags.Get(4));
        run.Runner.Continue();
        Assert.Equal(set, run.Flags.Get(4));
    }

    [Fact]
    public void SkippingEndsTheScriptAndSetsItsDoneFlag()
    {
        var script = Script(DialogueMode.Cutscene, Line(0, "A long scene.", 1), Line(1, "Still going.", null, End)) with { DoneFlag = Flag(4) };
        var run = Start(script);

        run.Runner.Skip();
        Assert.Equal(ScriptStatus.Ended, run.Runner.Status);
        Assert.True(run.Flags.Get(4));
    }

    [Theory]
    [InlineData(DialogueMode.Cutscene, false)]
    [InlineData(DialogueMode.Conversation, true)]
    public void ABattleEndsTheScriptWithItsEnemies(DialogueMode mode, bool canFlee)
    {
        var script = Script(mode,
            Line(0, "Get them!", null, Op(DialogueCommand.AddEnemy, target: Goblin), Op(DialogueCommand.AddEnemy, target: Goblin),
                Op(DialogueCommand.SetBattlefield, 30), Op(DialogueCommand.SetCollideByte, 2), Op(DialogueCommand.Battle)))
            with { BattleFlag = Flag(6) };
        var run = Start(script);
        run.Runner.Continue();

        var ending = run.Runner.Ending!;
        Assert.Equal(ScriptEndKind.Battle, ending.Kind);
        Assert.Equal([Goblin, Goblin], ending.Battle!.Enemies);
        Assert.Equal(0, ending.Battle.Battlefield);
        Assert.Equal(2, ending.Battle.CollisionByte);
        Assert.Equal(canFlee, ending.Battle.CanFlee);
        Assert.Equal(Flag(6), ending.Battle.WinFlag);
    }

    [Fact]
    public void AThirteenthEnemyEndsWithAFault()
    {
        var ops = Enumerable.Repeat(Op(DialogueCommand.AddEnemy, target: Goblin), 4).ToArray();
        var run = Start(Script(DialogueMode.Cutscene,
            Line(0, "", 1, ops), Line(1, "", 2, ops), Line(2, "", 3, ops), Line(3, "One too many.", null, ops)));

        Assert.Contains("Too many monsters", run.Runner.Ending!.Fault);
    }

    [Fact]
    public void ALineWithNoWayOnEndsWithAFault()
    {
        var run = Start(Script(DialogueMode.Cutscene, Line(0, "Dougal's line.", null)));

        run.Runner.Continue();
        Assert.Contains("no menu to go back to", run.Runner.Ending!.Fault);
    }

    [Fact]
    public void GoldIsOnlyTakenIfThePartyHasEnough()
    {
        var script = Script(DialogueMode.Conversation,
            Line(0, "That'll be 50.", 1, Op(DialogueCommand.TakeGold, 50)),
            Line(1, "And 50 back.", null, Op(DialogueCommand.AddGold, 50)));
        var poor = Start(script, new World { Gold = 40 });
        var rich = Start(script, new World { Gold = 60 });

        poor.Runner.Continue();
        rich.Runner.Continue();
        Assert.Equal(40u, poor.World.Gold);
        Assert.Equal(10u, rich.World.Gold);
        Assert.Equal([new SoundCue(ScriptSound.GoldPaid)], rich.Host.Cues);
    }

    [Fact]
    public void ACampHealRestsEightHoursAndACampSceneEightMore()
    {
        var script = Script(DialogueMode.Conversation, Line(0, "Rest now.", null, Op(DialogueCommand.CampHeal)));
        var heal = Start(script);
        var campScene = Start(script with { Rest = true });
        heal.Runner.Continue();
        campScene.Runner.Continue();

        Assert.True(heal.World.Healed);
        Assert.Equal(ScriptEndKind.Camp, heal.Runner.Ending!.Kind);
        Assert.Equal(8, heal.Runner.Ending.RestHours);
        Assert.Equal(16, campScene.Runner.Ending!.RestHours);
    }

    [Fact]
    public void RecruitingInAConversationSendsThePartnerAway()
    {
        var run = Start(Script(DialogueMode.Conversation,
            Line(0, "I'll join you.", 1, Op(DialogueCommand.AddMember, target: Trahern)),
            Line(1, "Let's go.", null, End)));
        run.Runner.Continue();
        run.Runner.Continue();

        Assert.Contains(Trahern, run.World.Members);
        Assert.True(run.Runner.Ending!.PartnerJoined);
    }

    [Fact]
    public void ExperienceGoesToTheSpeakerIfTheyAreAMember()
    {
        var script = Script(DialogueMode.Conversation,
            Line(0, "Well done.", 1, Op(DialogueCommand.SetSpeaker, target: Alaron), Op(DialogueCommand.AddExp, 100)),
            Line(1, "Thanks.", null, End));
        var member = Start(script, new World { Members = { Alaron } });
        var stranger = Start(script);
        member.Runner.Continue();
        stranger.Runner.Continue();

        Assert.Equal([(Alaron, 100)], member.World.Experience);
        Assert.Equal([new SoundCue(ScriptSound.Experience)], member.Host.Cues);
        Assert.Empty(stranger.Host.Cues);
    }

    [Fact]
    public void ResettingVisitedOffersEveryChoiceAgain()
    {
        var script = Script(DialogueMode.Conversation,
            Menu(0, "What'll it be?", [1, 2], Op(DialogueCommand.ResetVisited)),
            Line(1, "Tell me again.", 3),
            Line(2, "Goodbye.", null, End),
            Line(3, "All right.", null));
        var run = Start(script);

        run.Runner.Choose(0);
        run.Runner.Continue();
        // The menu resets as it's reached, before its choices are offered.
        Assert.Equal(["Tell me again.", "Goodbye."], Choices(run.Runner));
    }

    [Fact]
    public void CamerasFrameFromTheSpeakerAndTakeTheCameraTime()
    {
        var run = Start(Script(DialogueMode.Cutscene,
            Line(0, "Look.", null, Op(DialogueCommand.SetSpeaker, target: Trahern), Op(DialogueCommand.CameraTime, 90),
                Op(DialogueCommand.CameraCutTo, 3), Op(DialogueCommand.CameraPanTo, 4), Op(DialogueCommand.CameraOnActor, target: Alaron))),
            start: new ScriptStart(RefPoint: 10));

        Assert.Equal(
        [
            new CameraCue(DialogueCommand.CameraCutTo, 13, null, Trahern, 1),
            new CameraCue(DialogueCommand.CameraPanTo, 14, null, Trahern, 90),
            new CameraCue(DialogueCommand.CameraOnActor, null, Alaron, Trahern, 1),
        ], run.Host.Cues);
    }

    [Fact]
    public void ThePagesLineLosesItsLeadingSpacesButChoicesKeepTheirs()
    {
        var run = Start(Script(DialogueMode.Sign,
            Menu(0, "   The crossroads.", [1]),
            Line(1, "  Lonely Mountain", null, End)));

        Assert.Equal("The crossroads.", Text(run.Runner));
        Assert.Equal(["  Lonely Mountain"], Choices(run.Runner));
    }

    [Fact]
    public void ItemsAreTakenAndGiven()
    {
        var run = Start(Script(DialogueMode.Conversation,
            If(0, Op(DialogueCommand.CheckItem, target: Sword), 1, 2),
            Line(1, "My sword! Here's a reward.", 3, Op(DialogueCommand.TakeItem, target: Sword)),
            Line(2, "No sword?", null, End),
            Line(3, "Thanks.", null, End)), new World { Items = { Sword } });

        Assert.Equal("My sword! Here's a reward.", Text(run.Runner));
        run.Runner.Continue();
        Assert.Empty(run.World.Items);
    }
}
