using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Events;
using Aidyn.Core.Scripts;
using Aidyn.Core.Trek;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Rom.Tests;

/// <summary>
/// What starts the story's cinematics and what follows them, as the base plugin's triggers carry them (roadmap 2.10), end
/// to end on NA 1.0's data: a new game's opening, the Rooughah scene's end into Roog's and the last scene's into the ending's.
/// </summary>
public sealed partial class GameSessionTests
{
    private static RecordId Cinematic(int n) => RecordId.Decimal(RecordTypes.Cinematic, n);

    /// <summary>A cut-scene begun from the dialogue object that starts it, as walking into it would, once trek has faded out.</summary>
    private CutScene PlayCutScene(GameSession session, int script)
    {
        var id = RecordId.Hex(RecordTypes.Dialogue, script);
        var (voxel, _, dialogue) = Data.Of<DialogueObject>().First(d => d.Object.Script == id && d.Object.Stage is not null);
        session.Cue(Data.Set.Get<DialogueRecord>(id), new ScriptStart(dialogue.RefPointBase, voxel), dialogue.Stage!);
        Assert.Equal(SessionStep.CutSceneCued, TickUntil(session, SessionStep.CutSceneCued));
        session.BeginCutScene();
        return session.Scene!;
    }

    [Fact]
    public void TheBasePluginsTriggersAreOnEventsTheCoreFires()
    {
        var triggers = new Triggers(Data.Set);
        Assert.Empty(triggers.Problems);
        Assert.Equal([GameEvents.GameStarted, GameEvents.CinematicEnded, GameEvents.ScriptEnded],
            Data.Set.All<TriggerRecord>(RecordTypes.Trigger).Select(t => t.On).Distinct());
    }

    [Fact]
    public void ANewGamePlaysTheOpeningThenItsCutSceneWithNoTrekBetween()
    {
        var (session, host) = Start();
        Assert.True(session.StartNewGame());
        var opening = Data.Set.Get<CinematicRecord>(Cinematic(0));
        Assert.Equal(new Transition.PlayCinematic(opening), Assert.Single(host.Transitions));
        Assert.Equal(GameMode.Cinematic, session.Mode);

        // Its end sets its done flag, and script 866 starts from its object in Overworld I08 at once, on its stage.
        host.Transitions.Clear();
        PlayOut(session);
        Assert.True(session.Game.Flags.IsSet(opening.DoneFlag));
        Assert.Equal([typeof(Transition.EndCinematic), typeof(Transition.StartCutScene)], host.Transitions.Select(t => t.GetType()));
        var started = (Transition.StartCutScene)host.Transitions[1];
        Assert.Equal(RecordId.Hex(RecordTypes.Dialogue, 0x0362), started.Scene.Runner.Script.Id);
        Assert.Equal(RecordId.Parse("aidyn:voxel/0x0FDB/7"), started.Scene.Runner.Origin.MapObject);
        Assert.Equal((GameMode.CutScene, SessionStep.CutScene), (session.Mode, Tick(session)));
    }

    [Fact]
    public void TheRooughahScenesEndPlaysRoogsCinematicWhoseEndGivesAlaronHisTrueName()
    {
        var (session, host) = Start();
        var game = session.Game;
        var alaron = game.Party.Get(GameState.Alaron)!;
        Assert.False(alaron.TrueName);

        // Without flag 3442, the scene ends into trek.
        PlayCutScene(session, 0x04F4);
        host.Transitions.Clear();
        session.EndCutScene();
        Assert.Equal([typeof(Transition.EndCutScene), typeof(Transition.ResumeTrek)], host.Transitions.Select(t => t.GetType()));
        Assert.Equal(GameMode.Trek, session.Mode);

        // With it, Roog's cinematic plays in trek's place.
        game.Flags.Set(RecordId.Decimal(RecordTypes.Flag, 3442), true);
        PlayCutScene(session, 0x04F4);
        host.Transitions.Clear();
        session.EndCutScene();
        var roog = Data.Set.Get<CinematicRecord>(Cinematic(2));
        Assert.Equal([typeof(Transition.EndCutScene), typeof(Transition.PlayCinematic)], host.Transitions.Select(t => t.GetType()));
        Assert.Equal(new Transition.PlayCinematic(roog), host.Transitions[1]);
        Assert.Equal(GameMode.Cinematic, session.Mode);

        // Its end: its done flag, 590, Alaron's True Name, and trek built again where he stood.
        host.Transitions.Clear();
        PlayOut(session);
        Assert.True(game.Flags.IsSet(RecordId.Decimal(RecordTypes.Flag, 590)));
        Assert.True(alaron.TrueName);
        Assert.Equal([new Transition.EndCinematic(roog), new Transition.ResumeTrek()], host.Transitions);
        Assert.Equal(GameMode.Trek, session.Mode);
    }

    [Fact]
    public void TheLastScenesEndPlaysTheEndingThenTheCreditsWhichEndTheGame()
    {
        var (session, host) = Start();
        PlayCutScene(session, 0x04FE);
        host.Transitions.Clear();
        session.EndCutScene();
        var ending = Data.Set.Get<CinematicRecord>(Cinematic(3));
        Assert.Equal(new Transition.PlayCinematic(ending), host.Transitions[^1]);

        // Its end: its done flag, and the ending's credits, which can't be skipped, over everything.
        host.Transitions.Clear();
        PlayOut(session);
        Assert.True(session.Game.Flags.IsSet(ending.DoneFlag));
        Assert.Equal([new Transition.EndCinematic(ending), new Transition.PlayCredits(Ending: true)], host.Transitions);
        Assert.Equal(GameMode.Credits, session.Mode);
        Assert.True(session.Credits!.Ending);

        // They roll to their end, and it's the game's: back to the title.
        host.Transitions.Clear();
        session.Credits.Roll.Skip();
        Assert.Equal(GameMode.Credits, session.Mode);
        PlayOut(session);
        Assert.Equal([new Transition.EndCredits(Ending: true), new Transition.GameOver()], host.Transitions);
    }

    [Fact]
    public void TheGoblinAmbushsEndGoesIntoOrianasHutsCutScenesWithNoTrekBetween()
    {
        var (session, host) = Start();
        var game = session.Game;
        var (battle, _, _) = RatBattle(session, host, RecordId.Decimal(RecordTypes.Flag, 978));
        Kill(battle, battle.Current!);
        battle.EndTurn(battle.Current!);
        Assert.Equal(SessionStep.CombatOver, TickUntil(session, SessionStep.CombatOver));
        session.EndBattle();
        Assert.True(session.Trek.AtPlace);

        // Trek's first tick, drawn black, walks into the hut's first cut-scene (#1181, enabled by 5041), which fades out from
        // black: the screen never comes up.
        var trek = session.Trek;
        var brightest = 0f;
        var step = SessionStep.Trek;
        var ticks = 0;
        for (; ticks < 100 && step != SessionStep.CutSceneCued; ticks++)
        {
            step = Tick(session);
            brightest = MathF.Max(brightest, trek.Brightness);
        }
        Assert.Equal(SessionStep.CutSceneCued, step);
        Assert.Equal(0, brightest);
        Assert.InRange(ticks, 1, 10);
        Assert.Equal(RecordId.Hex(RecordTypes.Dialogue, 0x0392), trek.Cued!.Value.Script.Id);
        session.BeginCutScene();

        // Its end, its done flag (48) set, chains into the next (#1183, the blue area, enabled by 5042) from the place kept.
        game.Flags.Set(RecordId.Decimal(RecordTypes.Flag, 48), true);
        host.Transitions.Clear();
        session.EndCutScene();
        Assert.Equal([typeof(Transition.EndCutScene), typeof(Transition.StartCutScene)], host.Transitions.Select(t => t.GetType()));
        Assert.Equal(RecordId.Hex(RecordTypes.Dialogue, 0x0393), session.Scene!.Runner.Script.Id);
        var stage = session.Scene.Stage;
        Assert.Equal((RecordId.Decimal(RecordTypes.MapSet, 6), 3, 3), (stage.Set.Id, stage.Chunk.Row, stage.Chunk.Column));

        // And its end, with 49 set, is trek's.
        game.Flags.Set(RecordId.Decimal(RecordTypes.Flag, 49), true);
        host.Transitions.Clear();
        session.EndCutScene();
        Assert.Equal([typeof(Transition.EndCutScene), typeof(Transition.ResumeTrek)], host.Transitions.Select(t => t.GetType()));
        // Back in the hut, Alaron faces Oriana (her monster party, object 6), as the place kept has him.
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        var oriana = map.Origin + map.Record.Objects.OfType<MonsterPartyObject>().Single(o => o.Index == 6).Position;
        var toOriana = Vector2.Normalize(new Vector2(oriana.X - trek.Player.Sphere.Position.X, oriana.Z - trek.Player.Sphere.Position.Z));
        Assert.True(Vector2.Dot(toOriana, trek.Player.Heading) > 0.95f, $"he faces {trek.Player.Heading}, she's {toOriana} from him");
    }

    [Fact]
    public void TheTheatresCinematicsLeadNowhere()
    {
        var (session, host) = Start();
        var ending = Data.Set.Get<CinematicRecord>(Cinematic(3));
        session.MenuClosed(PauseExit.Theatre(ending));
        host.Transitions.Clear();
        PlayOut(session);
        Assert.Equal([new Transition.EndCinematic(ending)], host.Transitions);
        Assert.Equal(GameMode.Trek, session.Mode);
    }
}
