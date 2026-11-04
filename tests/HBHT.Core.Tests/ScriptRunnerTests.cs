using HBHT.Core.Combat;
using HBHT.Core.Flags;
using HBHT.Core.MapObjects;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Plugins.Records;
using HBHT.Plugins;

namespace HBHT.Core.Tests;

public class ScriptRunnerTests
{
    private static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);
    private static readonly RecordId Trahern = RecordId.Hex(RecordTypes.Entity, 0x0239);
    private static readonly RecordId Goblin = RecordId.Hex(RecordTypes.Entity, 0x0280);
    private static readonly RecordId Sword = RecordId.Hex(RecordTypes.Item, 0x073A);
    private static readonly RecordId Chest = MapObject(0, "K11", 3);
    private static readonly RecordId ScriptId = RecordId.Hex(RecordTypes.Script, 0x0400);
    private static readonly RecordId Rumours = RecordId.Hex(RecordTypes.Script, 0x0325);

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

    /// <summary>A line with no speaker.</summary>
    private static ScriptNode Line(int index, string text, int? link = null, params ScriptOp[] ops) =>
        ScriptNodes.Line(index, text, link, null, ops);

    /// <summary>A line its <paramref name="speaker"/> says.</summary>
    private static ScriptNode Spoken(int index, RecordId speaker, string text, int? link = null, params ScriptOp[] ops) =>
        ScriptNodes.Line(index, text, link, speaker, ops);

    private static ScriptNode Choice(int index, string text, int? link, ScriptRange? selects, params ScriptOp[] ops) =>
        Line(index, text, link, ops) with { Selects = selects };

    private static ScriptNode Menu(int index, string text, int[] links, RecordId? speaker = null, params ScriptOp[] ops) =>
        ScriptNodes.Node(index, ScriptNodeKind.Menu, text, links, speaker, ops);

    private static ScriptNode If(int index, ScriptOp condition, int then, int otherwise) =>
        ScriptNodes.Node(index, ScriptNodeKind.If, "", [then, otherwise], null, condition);

    private static ScriptNode Case(int index, string text, ScriptOp condition, params int[] links) =>
        ScriptNodes.Node(index, ScriptNodeKind.Case, text, links, null, condition);

    private static readonly ScriptOp End = new ScriptOp.ScriptEnd();

    private static ScriptRecord Script(ScriptMode mode, params ScriptNode[] nodes) => new(ScriptId, 1234, mode, nodes);

    private sealed record Run(ScriptRunner Runner, FlagNetwork Flags, World World, Host Host);

    private static Run Start(ScriptRecord script, World? world = null, IRandom? random = null, ScriptStart? start = null,
        FlagNetwork? flags = null, IEnumerable<ScriptRecord>? others = null, RecordId? randomSkillChecks = null)
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
        var run = Start(Script(ScriptMode.Conversation,
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
        var run = Start(Script(ScriptMode.Conversation,
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
        var run = Start(Script(ScriptMode.Conversation, Line(0, "Hail, {player}.", null, End)), new World { PlayerName = null });

        Assert.Equal("Hail, {player}.", Text(run.Runner));
    }

    [Fact]
    public void ALinesEffectRunsWhenItIsLeftAndOnlyTheFirstRuns()
    {
        var run = Start(Script(ScriptMode.Conversation,
            Line(0, "Hello.", 1, new ScriptOp.FlagSet(Flag(2)), new ScriptOp.FlagSet(Flag(3))),
            Line(1, "Goodbye.", null, End)));

        Assert.False(run.Flags.Get(2));
        run.Runner.Continue();
        Assert.True(run.Flags.Get(2));
        Assert.False(run.Flags.Get(3));
    }

    [Fact]
    public void ALineWithNoTextIsWalkedThrough()
    {
        var run = Start(Script(ScriptMode.Conversation,
            Line(0, "", 1, new ScriptOp.FlagSet(Flag(2))),
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
        var run = Start(Script(ScriptMode.Conversation,
            If(0, new ScriptOp.CheckPartySize(), 1, 2),
            Line(1, "Just you.", null, End),
            Line(2, "Many of you.", null, End)), new World { PartySize = partySize });

        Assert.Equal(expected, Text(run.Runner));
    }

    private static ScriptRecord Shopkeeper(ScriptMode mode) => Script(mode,
        Menu(0, "What'll it be?", [1, 2], Trahern),
        Spoken(1, Alaron, "Tell me about the goblins.", 3),
        Spoken(2, Alaron, "Goodbye.", null, End),
        Spoken(3, Trahern, "They're back."));

    [Fact]
    public void AMenuHidesTheChoicesTakenAndALineWithNoLinkGoesBackToIt()
    {
        var run = Start(Shopkeeper(ScriptMode.Conversation));
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
    [InlineData(ScriptMode.Backdrop)]
    [InlineData(ScriptMode.Help)]
    public void BackdropsAndTheHelpMenuOfferChoicesAgain(ScriptMode mode)
    {
        var run = Start(Shopkeeper(mode));
        run.Runner.Choose(0);
        run.Runner.Continue();

        Assert.Equal(["Tell me about the goblins.", "Goodbye."], Choices(run.Runner));
    }

    [Fact]
    public void EachLineCarriesTheSpeakerWhenItWasAdded()
    {
        var run = Start(Shopkeeper(ScriptMode.Conversation));

        Assert.Equal(Trahern, run.Runner.Page!.Line.Speaker);
        Assert.Equal([Alaron, Alaron], run.Runner.Page.Choices.Select(c => c.Speaker));
        // Reaching the choices set the speaker as they were added.
        Assert.Equal(Alaron, run.Runner.Speaker);
    }

    [Fact]
    public void ALineShowsUnderItsOwnNodesSpeakerAndANodeWithNoneShowsNone()
    {
        var run = Start(Script(ScriptMode.Conversation,
            Spoken(0, Trahern, "Well met.", 1),
            Line(1, "Indeed.", 2),
            Spoken(2, Trahern, "Farewell.", null, End)));

        Assert.Equal(Trahern, run.Runner.Page!.Line.Speaker);
        run.Runner.Continue();
        // The earlier speaker doesn't carry over to a node that names none.
        Assert.Equal("Indeed.", Text(run.Runner));
        Assert.Null(run.Runner.Page!.Line.Speaker);
        Assert.Null(run.Runner.Speaker);
    }

    [Fact]
    public void ChoosingAChoiceItCannotLeaveEndsWithAFault()
    {
        var run = Start(Script(ScriptMode.Conversation,
            Menu(0, "What'll it be?", [1]),
            Menu(1, "A menu as a choice.", [0])));

        run.Runner.Choose(0);

        Assert.Equal(ScriptStatus.Ended, run.Runner.Status);
        Assert.Contains("Invalid node to evaluate", run.Runner.Ending!.Fault);
    }

    private static ScriptRecord Haggle() => Script(ScriptMode.Conversation,
        Case(0, "How much have you got?", new ScriptOp.CheckGold(), 1, 2, 3),
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
        var run = Start(Script(ScriptMode.Cutscene,
            Spoken(0, Trahern, "Follow me.", 1, new ScriptOp.ActorRunTo(Trahern, 4)),
            Line(1, "Here we are.", null, End)), start: new ScriptStart(Marks: 20));

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
        var run = Start(Script(ScriptMode.Cutscene,
            Line(0, "", 1, new ScriptOp.ScriptWait(10)),
            Line(1, "Done.", null, End)));

        Assert.Equal(ScriptWait.Timer, run.Runner.Wait);
        run.Runner.Step(4);
        run.Runner.Step(5);
        Assert.Equal(ScriptWait.Timer, run.Runner.Wait);
        run.Runner.Step(1);
        Assert.Equal("Done.", Text(run.Runner));
    }

    [Fact]
    public void AConversationsAnimatedLineAsksItsSpeakerForTheRequestAsItShowsAndShowsOnce()
    {
        // Borg13 890's nodes 12 to 16, the lighthouse keeper handing Alaron his key. 1.0 shows each animated line twice with no
        // one animating; the port's fix plays the request as the line shows, once.
        var keeper = RecordId.Hex(RecordTypes.Entity, 0x0205);
        var key = RecordId.Hex(RecordTypes.Item, 0x1205);
        var run = Start(Script(ScriptMode.Conversation,
            Spoken(0, Alaron, "I suppose I can understand your reluctance.", 1),
            Spoken(1, keeper, "Here... take the key.", 2, new ScriptOp.ActorAnimate(keeper, RecordIds.Request(19))),
            Spoken(2, Alaron, "Thank you.", 3, new ScriptOp.ActorAnimate(Alaron, RecordIds.Request(19))),
            Line(3, "", 4, new ScriptOp.PartyAddItem(key)),
            Spoken(4, Alaron, "A secret?", null, End)));

        run.Runner.Continue();
        Assert.Equal("Here... take the key.", Text(run.Runner));
        Assert.Null(run.Runner.Wait);
        Assert.Equal(new AnimateCue(keeper, 19), Assert.Single(run.Host.Cues));

        run.Runner.Continue();
        Assert.Equal("Thank you.", Text(run.Runner));
        Assert.Null(run.Runner.Wait);
        Assert.Equal(new AnimateCue(Alaron, 19), run.Host.Cues[^1]);

        run.Runner.Continue();
        Assert.Equal("A secret?", Text(run.Runner));
        Assert.Equal(2, run.Host.Cues.Count);
    }

    [Fact]
    public void AnAnimatedLineAsksTheActorItNamesWhichIsTheOneTheBoxShows()
    {
        // 0x037B's node 26, "Get out of my sight, worm!": the Marquis, speaking from node 25 and again here, is asked.
        var marquis = RecordId.Hex(RecordTypes.Entity, 0x0221);
        var run = Start(Script(ScriptMode.Conversation,
            Spoken(0, marquis, "Hmph.", 1),
            Spoken(1, marquis, "Get out of my sight, worm!", null, new ScriptOp.ActorAnimate(marquis, RecordIds.Request(5)))));

        run.Runner.Continue();
        Assert.Equal("Get out of my sight, worm!", Text(run.Runner));
        Assert.Equal(marquis, run.Runner.Page!.Line.Speaker);
        Assert.Equal(new AnimateCue(marquis, 5), Assert.Single(run.Host.Cues));
    }

    [Fact]
    public void ACutSceneAnimatesNoOneAsALineShows()
    {
        var run = Start(Script(ScriptMode.Cutscene,
            Spoken(0, Trahern, "Watch this.", 1, new ScriptOp.ActorAnimate(Trahern, RecordIds.Request(5))),
            Line(1, "Ta-da.", null, End)));

        Assert.Empty(run.Host.Cues);
        run.Runner.Continue();

        // Trahern wasn't spawned, so nothing plays and the line waits for A.
        Assert.Equal(new AnimateCue(null, 5), Assert.Single(run.Host.Cues));
        Assert.Equal(ScriptWait.Player, run.Runner.Wait);
        Assert.Equal("Watch this.", Text(run.Runner));
    }

    [Fact]
    public void ACutSceneAnimatesOnlyActorsItHasSpawned()
    {
        var script = Script(ScriptMode.Cutscene,
            Line(0, "", 1, new ScriptOp.ActorSpawn(Trahern, 2), new ScriptOp.ActorAnimate(Trahern, RecordIds.Request(5))),
            Line(1, "Ta-da.", null, End)) with { Actors = [Trahern] };

        var run = Start(script);

        Assert.Equal(new SpawnCue(Trahern, 2), run.Host.Cues[0]);
        Assert.Equal(new AnimateCue(Trahern, 5), run.Host.Cues[1]);
        Assert.Equal(ScriptWait.Animation, run.Runner.Wait);
    }

    private static ScriptRecord RumourScript() => new(Rumours, 900, ScriptMode.Conversation,
    [
        Line(0, "First rumour.", 1), Line(1, "Second rumour.", 2), Line(2, "Third rumour.", null, End),
    ]);

    /// <summary>A line of the rumours; <paramref name="blankNodes"/> is 1 where the rumours ended with a blank node, which counts as the first.</summary>
    private static ScriptRecord Gossip(int blankNodes = 0) => Script(ScriptMode.Conversation,
        Line(0, "", 1, new ScriptOp.ScriptRandomLine(Rumours, blankNodes == 0 ? null : 1 + blankNodes)),
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
        var run = Start(Gossip(blankNodes), random: new ScriptedRandom(Draw(roll, 101)), others: [RumourScript()]);

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
        var first = Start(Gossip(), world, new ScriptedRandom(Draw(40, 101)), start, others: [RumourScript()]);
        var again = Start(Gossip(), world, new ScriptedRandom(Draw(90, 101)), start, others: [RumourScript()]);

        Assert.Equal("Second rumour.", Text(first.Runner));
        Assert.Equal("Second rumour.", Text(again.Runner));
        Assert.Equal(40, world.Memory[new Memory(Chest, MemoryKind.RandomLine)]);
    }

    private static ScriptRecord LockPicking(int skill) => Script(ScriptMode.Conversation,
        Case(0, "A lock.", new ScriptOp.CheckSkill(RecordIds.Skill(skill)), 1, 2),
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
    [InlineData(ScriptMode.Cutscene, true)]
    [InlineData(ScriptMode.Backdrop, true)]
    [InlineData(ScriptMode.Conversation, false)]
    public void ScriptsWithADoneFlagSetItWhenTheyEnd(ScriptMode mode, bool set)
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
        var script = Script(ScriptMode.Cutscene, Line(0, "A long scene.", 1), Line(1, "Still going.", null, End)) with { DoneFlag = Flag(4) };
        var run = Start(script);

        run.Runner.Skip();
        Assert.Equal(ScriptStatus.Ended, run.Runner.Status);
        Assert.True(run.Flags.Get(4));
    }

    [Theory]
    [InlineData(ScriptMode.Cutscene, false)]
    [InlineData(ScriptMode.Conversation, true)]
    public void ABattleEndsTheScriptWithItsEnemies(ScriptMode mode, bool canFlee)
    {
        var script = Script(mode,
            Line(0, "Get them!", null, new ScriptOp.BattleAddEnemy(Goblin), new ScriptOp.BattleAddEnemy(Goblin),
                new ScriptOp.BattleArena(Chunk(16, "I03")), new ScriptOp.BattleCollisionByte(2), new ScriptOp.BattleStart()))
            with { BattleFlag = Flag(6) };
        var run = Start(script);
        run.Runner.Continue();

        var ending = run.Runner.Ending!;
        Assert.Equal(ScriptEndKind.Battle, ending.Kind);
        Assert.Equal(BattleSource.Script, ending.Battle!.Source);
        Assert.Equal([new PartyEnemy(Goblin, 1, 1), new PartyEnemy(Goblin, 1, 1)], ending.Battle.Enemies);
        Assert.Equal(Chunk(16, "I03"), ending.Battle.Arena);
        Assert.Equal(2, ending.Battle.CollisionByte);
        Assert.Equal(canFlee, ending.Battle.CanFlee);
        Assert.Equal(Flag(6), ending.Battle.EncounterFlag);
        Assert.False(ending.Battle.Ambush);
    }

    [Fact]
    public void AThirteenthEnemyEndsWithAFault()
    {
        var ops = Enumerable.Repeat<ScriptOp>(new ScriptOp.BattleAddEnemy(Goblin), 4).ToArray();
        var run = Start(Script(ScriptMode.Cutscene,
            Line(0, "", 1, ops), Line(1, "", 2, ops), Line(2, "", 3, ops), Line(3, "One too many.", null, ops)));

        Assert.Contains("Too many monsters", run.Runner.Ending!.Fault);
    }

    [Fact]
    public void ALineWithNoWayOnEndsWithAFault()
    {
        var run = Start(Script(ScriptMode.Cutscene, Line(0, "Dougal's line.", null)));

        run.Runner.Continue();
        Assert.Contains("no menu to go back to", run.Runner.Ending!.Fault);
    }

    [Fact]
    public void GoldIsOnlyTakenIfThePartyHasEnough()
    {
        var script = Script(ScriptMode.Conversation,
            Line(0, "That'll be 50.", 1, new ScriptOp.PartyTakeGold(50)),
            Line(1, "And 50 back.", null, new ScriptOp.PartyAddGold(50)));
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
        var script = Script(ScriptMode.Conversation, Line(0, "Rest now.", null, new ScriptOp.PartyCampHeal()));
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
        var run = Start(Script(ScriptMode.Conversation,
            Line(0, "I'll join you.", 1, new ScriptOp.PartyAddMember(Trahern)),
            Line(1, "Let's go.", null, End)));
        run.Runner.Continue();
        run.Runner.Continue();

        Assert.Contains(Trahern, run.World.Members);
        Assert.True(run.Runner.Ending!.PartnerJoined);
    }

    [Fact]
    public void ExperienceGoesToTheSpeakerIfTheyAreAMember()
    {
        var script = Script(ScriptMode.Conversation,
            Spoken(0, Alaron, "Well done.", 1, new ScriptOp.PartyAddExp(Alaron, 100)),
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
        var script = Script(ScriptMode.Conversation,
            Menu(0, "What'll it be?", [1, 2], null, new ScriptOp.ScriptResetChoices()),
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
    public void CamerasTakeTheCameraTimeAndCountMarksFromTheScriptsFirst()
    {
        var run = Start(Script(ScriptMode.Cutscene,
            Spoken(0, Trahern, "Look.", null, new ScriptOp.CameraTime(90),
                new ScriptOp.CameraCutTo { Mark = 3 }, new ScriptOp.CameraTurnTo { Mark = 4 }, new ScriptOp.CameraLookAt { Actor = Alaron })),
            start: new ScriptStart(Marks: 10));

        Assert.Equal(
        [
            new CameraCue(CameraShot.CutTo, 13, null, 1),
            new CameraCue(CameraShot.TurnTo, 14, null, 90),
            new CameraCue(CameraShot.LookAt, null, Alaron, 1),
        ], run.Host.Cues);
    }

    [Fact]
    public void ThePagesLineLosesItsLeadingSpacesButChoicesKeepTheirs()
    {
        var run = Start(Script(ScriptMode.Sign,
            Menu(0, "   The crossroads.", [1]),
            Line(1, "  Lonely Mountain", null, End)));

        Assert.Equal("The crossroads.", Text(run.Runner));
        Assert.Equal(["  Lonely Mountain"], Choices(run.Runner));
    }

    [Fact]
    public void ItemsAreTakenAndGiven()
    {
        var run = Start(Script(ScriptMode.Conversation,
            If(0, new ScriptOp.CheckItem(Sword), 1, 2),
            Line(1, "My sword! Here's a reward.", 3, new ScriptOp.PartyTakeItem(Sword)),
            Line(2, "No sword?", null, End),
            Line(3, "Thanks.", null, End)), new World { Items = { Sword } });

        Assert.Equal("My sword! Here's a reward.", Text(run.Runner));
        run.Runner.Continue();
        Assert.Empty(run.World.Items);
    }
}
