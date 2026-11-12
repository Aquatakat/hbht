using HBHT.Core;
using HBHT.Core.Events;
using HBHT.Core.Playthrough;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The playthrough bot's planner (docs/systems/playthrough.md, layer 1) on NA 1.0's data: the static pass from a new game, plans to
/// a member joining and to the ending, and the model's ways through scripts against the real runner. Places are all taken as
/// reachable (<see cref="Everywhere"/>), as they are until the world graph answers.
/// </summary>
public sealed class PlaythroughPlannerTests(Na10Import fixture)
{
    private static readonly Lock Building = new();
    private static (PluginSet Set, QuestModel Model)? _built;

    private PluginSet Plugins => fixture.Data.Set;

    /// <summary>The model, built once for the class (some seconds).</summary>
    private QuestModel Model
    {
        get
        {
            lock (Building)
            {
                if (_built is not { } built || built.Set != Plugins)
                    _built = built = (Plugins, QuestModel.Build(Plugins));
                return built.Model;
            }
        }
    }

    [Fact]
    public void TheHistoryBookIsReadOnceAChapterNotRoundAndRound()
    {
        // 0x036F, Gwernia castle's history book (4/O09/6), goes back to its contents after each chapter, and a backdrop's chapters stay
        // takeable: a way read chapter 19 again and again while each pass wrote 23 again.
        var ways = Model.Ways[Ids.Script(0x036F)].Ways;
        Assert.True(ways.Count <= 6, $"{ways.Count} ways");
        Assert.All(ways, w => Assert.Equal(w.Choices.Distinct().Count(), w.Choices.Count));
        Assert.Contains(ways, w => w.Choices.SequenceEqual([19, 24]) && w.Effects.Contains(new SetFlag(Ids.Flag(23), true)));
        Assert.DoesNotContain(Model.Actions, a => a.Choices.Distinct().Count() != a.Choices.Count);
    }

    private PlanState NewGame() => PlanState.From(Model, GameState.NewGame(Plugins));

    [Fact]
    public void TheShadowsBattleIsNeverWon()
    {
        // The Shadow's battle ends in its merging with Alaron or its vanishing, neither of which sets an encounter's flag: its encounter
        // at Overworld H08/12 is fought only for the merge, and its beaten flag, 950, is no target.
        var shadow = Ids.MapObject(0, "H08", 12);
        var fights = Model.Actions.Where(a => a.Place == shadow).ToList();
        Assert.NotEmpty(fights);
        Assert.All(fights, a => Assert.DoesNotContain(a.Effects, e => e is SetFlag { Flag: var f } && f == Flag(950)));
        Assert.DoesNotContain(Flag(950), CoverGoal.StaticTargets(Model, NewGame()));
    }

    [Fact]
    public void ATrapIsAGateTheRunMeetsByThePartysChanceOfDisarmingIt()
    {
        // A trap that goes off destroys its container, so a step to a trapped one is played only while the party has a chance: its
        // mechanic rolls d100 under Intelligence × 4 + Mechanic × 10, and the step that leaves must reach the trap's level.
        var game = GameState.NewGame(Plugins);
        var alaron = game.Party.Members.Single();
        static CodeGate Trap(int level) => new($"disarming a trap of level {level}") { Check = new(CodeCheckKind.Trap, level) };
        Assert.Equal(1, CodeGates.DisarmChance(game.Party, 0));
        if (alaron.Skills.Modified(HBHT.Core.Characters.Skill.Mechanic) == 0)
        {
            Assert.Equal(0, CodeGates.DisarmChance(game.Party, 1));
            Assert.False(CodeGates.Meets(Trap(1), game));
            alaron.Skills.Train(HBHT.Core.Characters.Skill.Mechanic, 1);
        }
        var chances = Enumerable.Range(1, 10).Select(level => CodeGates.DisarmChance(game.Party, level)).ToList();
        Assert.True(chances[0] > 0);
        Assert.Null(CodeGates.Meets(Trap(1), game));
        Assert.Equal(chances.OrderByDescending(c => c), chances);
        Assert.Equal(0, chances[^1]);
        Assert.False(CodeGates.Meets(Trap(10), game));
    }

    private static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    [Fact]
    public void ANewGameStartsWithItsOpeningToPlayAndAlaronAlone()
    {
        var state = NewGame();
        Assert.True(state.Opening);
        Assert.Equal([GameState.Alaron], state.Party);
        // The opening's one way: cinematic 0's done flag, then script 866 from its object in Overworld I08, which comes next.
        var opening = Assert.Single(Model.Opening);
        Assert.Equal([new SetFlag(Flag(3), true), new StartScript(Ids.MapObject(0, "I08", 7))], opening.Effects);
        var after = state.Apply(opening);
        Assert.False(after.Opening);
        Assert.Equal(Ids.MapObject(0, "I08", 7), after.Forced);
    }

    [Fact]
    public void TheLastScenesOnlyWayRollsTheEndingsCredits()
    {
        // 0x04FE's end sets its done flag (599), plays the ending's cinematic (done flag 601), whose end rolls the credits.
        var way = Assert.Single(Model.Ways[Ids.Script(0x04FE)].Ways);
        Assert.Empty(way.Requires);
        Assert.Equal([new SetFlag(Flag(599), true), new SetFlag(Flag(601), true), new RollCredits()], way.Effects);
    }

    [Fact]
    public void ACutScenesMemberChecksThatOnlySpawnActorsAreNotBranches()
    {
        // Arturo's scene at the bridge (0x0404) spawns each member present, one check each, before anything that matters: walked as
        // branches, they cut its search short before its ways that let the party cross (3460) or take Arturo on (3461, his joining).
        var ways = Model.Ways[Ids.Script(0x0404)].Ways;
        Assert.Contains(ways, w => w.Effects.Contains(new SetFlag(Flag(3460), true)));
        Assert.Contains(ways, w => w.Effects.Contains(new SetFlag(Flag(3461), true)));
        Assert.Contains(ways, w => w.Effects.Contains(new Join(Ids.Entity(0x0240))));
        // Crossing needs no one in particular in the party.
        Assert.Contains(ways, w => w.Effects.Contains(new SetFlag(Flag(3460), true)) && !w.Requires.OfType<MemberIs>().Any());
    }

    [Fact]
    public void TheStaticPassFromANewGameReachesTheEndingAndEveryMember()
    {
        var pass = StaticPass.Run(Model, NewGame(), fixture.Data.Index);
        Assert.True(pass.ReachesEnding);
        Assert.Equal(Model.Members.Keys.Select(k => k.ToString()).Order(), pass.Joinable.Select(k => k.ToString()).Order());
        Assert.True(pass.CanSet(Flag(601)));
        // The merge with the Shadow (566, the boost's flag) comes from a battle with it while the party has the Horn of Kynon.
        Assert.True(pass.CanSet(Flag(566)));
        // Members' died flags only the engine sets, as they die; the planner never plans a death.
        var died = pass.Unreachable.Single(u => u.Flag == Flag(613));
        Assert.Equal(NeverSet.OnlyTheEngine, died.Why);
        // Every plain flag is counted once, settable or not.
        Assert.Equal(pass.PlainFlags, pass.PlainSettable + pass.Unreachable.Count);
        Assert.InRange(pass.PlainSettable, 1500, pass.PlainFlags);
    }

    [Fact]
    public void FromANewGameTheMemberGoalHasAPlan()
    {
        var start = NewGame();
        var planner = new QuestPlanner(Model);
        var plan = planner.Plan(start, new MemberGoal());
        Assert.True(plan.Found, plan.Why);
        Assert.Equal(ActionKind.Event, plan.Steps[0].Kind);
        var end = Play(start, plan.Steps);
        Assert.Equal(2, end.Party.Count);
        Assert.Contains(end.Party[1], Model.Members.Keys);
    }

    [Fact]
    public void ThePlanToTheEndingIsFiniteAndEachStepsPreconditionsHoldInTurn()
    {
        var start = NewGame();
        var planner = new QuestPlanner(Model);
        var plan = planner.Plan(start, new EndingGoal());
        Assert.True(plan.Found, plan.Why);
        Assert.InRange(plan.Steps.Count, 2, 500);
        var end = Play(start, plan.Steps);
        Assert.True(end.Credits);
        Assert.False(end.Over);
        // The last step plays the last scene.
        Assert.Equal(Ids.Script(0x04FE), plan.Steps[^1].Script);
    }

    [Fact]
    public void AFlagNothingSetsHasNoPlanAndSaysWhy()
    {
        // Flag 84: Trahern's conversation goes round forever once 67 and 84 are both set, but nothing sets 84 (docs/systems/scripts.md).
        var plan = new QuestPlanner(Model).Plan(NewGame(), new FlagGoal(Flag(84)));
        Assert.False(plan.Found);
        Assert.Contains("nothing the model has does it", plan.Why);
        Assert.Equal(["flag 84 set"], plan.Missing);
    }

    /// <summary>
    /// The member plan's steps played on a real game: each script run by the real runner from the object the step uses, taking the
    /// step's choices, sets and clears the plain flags the model says, and no others, and adds whom it says.
    /// </summary>
    [Fact]
    public void TheMemberPlansStepsDoOnTheRealRunnerWhatTheModelSays()
    {
        var start = NewGame();
        var plan = new QuestPlanner(Model).Plan(start, new MemberGoal());
        Assert.True(plan.Found, plan.Why);
        var game = GameState.NewGame(Plugins);
        var state = start;
        foreach (var step in plan.Steps)
        {
            Assert.Empty(step.Gates);
            if (step.Kind == ActionKind.Event)
            {
                // The opening's cinematic ends with its done flag set; its trigger starts the next step's script.
                game.Flags.Set(Flag(3), true);
            }
            else
            {
                Assert.Null(PlayOnRunner(game, step));
            }
            state = state.Apply(step);
            var differ = Enumerable.Range(0, Model.Flags.Count)
                .Where(h => Model.Flags.Kind(h) == FlagKind.Plain && state.Plain(h) != game.Flags.IsSetAsStored(Model.Flags.Id(h)))
                .Select(h => Model.Flags.Id(h).Key).ToList();
            Assert.True(differ.Count == 0, $"After {step.Describe()} the model and the game differ in flags {string.Join(", ", differ)}.");
            Assert.Equal(state.Party, game.Party.Members.Select(m => m.Entity));
        }
    }

    /// <summary>
    /// Every way through a script that can be taken after the opening, with nothing left to code (no skill, gold or stat check, no
    /// battle) and no flag the engine writes in its way, played from the same save on the real runner, does to the plain flags and
    /// the party what the model says it does.
    /// </summary>
    [Fact]
    public void EveryWayOpenAfterTheOpeningPlaysOnTheRealRunnerAsModelled()
    {
        var start = NewGame();
        start = start.Apply(Model.Opening[0]);
        var forced = Model.Actions.First(a => a.Kind == ActionKind.Play && a.Place == start.Forced && start.CanDo(a));
        start = start.Apply(forced);
        var game = GameState.NewGame(Plugins);
        game.Flags.Set(Flag(3), true);
        Assert.Null(PlayOnRunner(game, forced));
        var save = game.Save();

        var open = Model.Actions
            .Where(a => a.Kind is ActionKind.Talk or ActionKind.Play && !a.Wins && a.Buys is null && !a.Gates.Any() && start.CanDo(a))
            .Where(a => a.Requires.OfType<FlagIs>().All(f => Model.Flags.Kind(Model.Flags.Handle(f.Flag)) is not FlagKind.Engine
                && Model.Flags.Inputs(Model.Flags.Handle(f.Flag)).All(i => Model.Flags.Kind(i) != FlagKind.Engine)))
            .ToList();
        Assert.InRange(open.Count, 100, Model.Actions.Count);
        var wrong = new List<string>();
        foreach (var action in open)
        {
            // A way merged across a condition that changed nothing keeps each branch's choices; the page shows which.
            GameState? played = null;
            string? astray = null;
            foreach (var choices in action.OtherChoices.Prepend(action.Choices))
            {
                played = GameState.Load(Plugins, save);
                astray = PlayOnRunner(played, action with { Choices = choices });
                if (astray is null)
                    break;
            }
            if (astray is not null)
            {
                wrong.Add($"{action.Describe()}: {astray}");
                continue;
            }
            var state = start.Apply(action);
            var differ = Enumerable.Range(0, Model.Flags.Count)
                .Where(h => Model.Flags.Kind(h) == FlagKind.Plain && state.Plain(h) != played!.Flags.IsSetAsStored(Model.Flags.Id(h)))
                .Select(h => Model.Flags.Id(h).Key).ToList();
            if (differ.Count > 0 || !state.Party.SequenceEqual(played!.Party.Members.Select(m => m.Entity)))
                wrong.Add($"{action.Describe()}: flags {string.Join(", ", differ)}");
        }
        Assert.True(wrong.Count == 0, $"{wrong.Count} of {open.Count} differ:\n{string.Join("\n", wrong.Take(20))}");
    }

    [Fact]
    public void TheDryadsTriggerOnceSeenToTurnThePlayerBackIsGotPastByAWayThatDoesnt()
    {
        // The dryads' scene at Overworld G12/5 (0x0531) plays as the player walks out of their wood. With the Amaranth taken (222) and
        // no task from them (229, 3647), it sends him to their queen (3646 opens the teleporter G12/3 about it), every time.
        var triggers = new StoryTriggers(Model);
        var dryads = triggers.Get(Ids.MapObject(0, "G12", 5))!;
        Assert.NotNull(dryads);
        var herb = NewGame().With(Flag(3), true).With(Flag(222), true);
        var queen = Assert.Single(triggers.WaysIn(dryads, herb));
        Assert.Contains(new SetFlag(Flag(3646), true), dryads.Ways[queen].Effects);
        // The ways that open a teleporter about it: to the queen, and to the Gorgon's cave (3648 opens G12/2) twice.
        Assert.Equal(3, dryads.Sends.Count);
        Assert.Contains(queen, dryads.Sends);
        Assert.False(triggers.Blocks(dryads, herb));

        // Seen to turn him back: getting past it is planned for by another of its ways or its gates closed, never by walking into it.
        triggers.Learn(dryads.Id, [queen]);
        Assert.True(triggers.Blocks(dryads, herb));
        var planner = new QuestPlanner(Model) { Triggers = triggers };
        var plan = planner.Plan(herb, new PassGoal(dryads.Id), 4000);
        Assert.True(plan.Found, plan.Why);
        Assert.NotEmpty(plan.Steps);
        Assert.DoesNotContain(plan.Steps, s => s.Place == dryads.Id && s.Way == dryads.Ways[queen]);
        var after = Play(herb, plan.Steps);
        Assert.False(triggers.Blocks(dryads, after));
    }

    /// <summary>Applies each step in turn, asserting it can be done where it comes.</summary>
    private static PlanState Play(PlanState state, IEnumerable<PlayerAction> steps)
    {
        foreach (var step in steps)
        {
            Assert.True(state.CanDo(step), $"{step.Describe()} can't be done where it comes: {state.Blocker(step)}");
            state = state.Apply(step);
        }
        return state;
    }

    /// <summary>
    /// Runs a step's script on the game with the real runner, taking its choices, as the pilot will. Null when it went as the step
    /// says; otherwise where it didn't.
    /// </summary>
    private string? PlayOnRunner(GameState game, PlayerAction step)
    {
        var script = Plugins.Get<ScriptRecord>(step.Script!.Value);
        var o = fixture.Data.Objects[step.Place!.Value];
        var marks = o is ScriptObject play ? play.Marks : 0;
        var runner = game.RunScript(script, new NoHost(), new ScriptStart(marks, step.Place));
        runner.Start();
        var choices = new Queue<int>(step.Choices);
        for (var pages = 0; runner.Status != ScriptStatus.Ended; pages++)
        {
            if (pages == 5000)
                return "it doesn't end";
            if (runner.Status == ScriptStatus.Waiting)
            {
                if (runner.Wait == ScriptWait.Timer)
                    runner.Step(int.MaxValue);
                else
                    runner.Resume();
                continue;
            }
            var page = runner.Page;
            if (runner.Wait != ScriptWait.Player && page is { Choices.Count: > 0 })
            {
                if (!choices.TryDequeue(out var node))
                    return $"a page of choices {string.Join(" ", page.Choices.Select(c => c.Node))} comes after the step's last";
                var index = page.Choices.ToList().FindIndex(c => c.Node == node);
                if (index < 0)
                    return $"choice {node} isn't among {string.Join(" ", page.Choices.Select(c => c.Node))}";
                runner.Choose(index);
            }
            else
            {
                runner.Continue();
            }
        }
        if (choices.Count > 0)
            return $"it ended with choices {string.Join(" ", choices)} untaken";
        if (o is NpcObject { Used: { } used })
            game.Flags.TrySet(used.Flag, true);
        if (script.Mode == ScriptMode.Cutscene)
            game.Triggers.Fire(game, GameEvents.ScriptEnded, script.Id, _ => { });
        return null;
    }

    private sealed class NoHost : IScriptHost
    {
        public void Cue(ScriptCue cue)
        {
        }
    }
}
