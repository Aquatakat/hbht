using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Audio;
using HBHT.Core.Movement;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// The Fire Lord's scene at the end of Shamsuk's Tower (<c>aidyn:dialogue/0x048F</c>), played with its actors' real animations: he
/// dies at node 221, and Niesen at 230 (Rheda in the party) or 237 (not), and both lie at their deaths' last frames to the end.
/// </summary>
public sealed partial class GameSessionTests
{
    private static readonly RecordId FireLord = RecordId.Hex(RecordTypes.Entity, 0x0261), Niesen = RecordId.Hex(RecordTypes.Entity, 0x024A);

    private sealed record FireLordScene(CutScene Scene, Dictionary<CutSceneActor, ActorMotion?> Motions, List<RecordId> DeathCries)
    {
        public CutSceneActor Of(RecordId entity) => Scene.Actor(entity)!;

        public ActorStateMachine Machine(RecordId entity) => Motions[Of(entity)]!.Machine;
    }

    /// <summary>
    /// Plays 0x048F to its end from a new game, Rheda in the party or not, pressing A once each page has shown for <paramref name="linger"/>
    /// ticks. Notes each actor's death cry: the key Alaron's profile, which every cut-scene actor has, plays as a death starts.
    /// </summary>
    private FireLordScene PlayFireLordScene(bool rheda, int linger)
    {
        var (session, _) = Start("G14", 40, 40);
        if (rheda)
            Assert.True(session.Game.AddMember(RecordId.Hex(RecordTypes.Entity, 0x024C)));
        Tick(session);
        var scene = PlayCutScene(session, 0x048F);
        var motions = new Dictionary<CutSceneActor, ActorMotion?>();
        IActorMotion? Motion(CutSceneActor actor)
        {
            if (!motions.TryGetValue(actor, out var motion))
                motions[actor] = motion = ActorMotion.For(Data.Set, Data.Set.Get<ActorRecord>(actor.Look.Actor), new Lcg(0));
            return motion;
        }
        var profile = Data.Set.Get<EntityRecord>(GameState.Alaron).Sounds!;
        var cries = new List<RecordId>();
        var shown = 0;
        for (var i = 0; i < 100_000 && !scene.Over; i++)
        {
            if (scene.Accepts && scene.Page is not null && ++shown > linger)
            {
                shown = 0;
                scene.Press(scene.Runner.Continue);
            }
            session.Tick(SessionSettings.Original, () => new PlayerInput(Vector2.Zero), false, Motion);
            foreach (var actor in scene.Actors)
            {
                if (motions.GetValueOrDefault(actor) is { } motion && motion.Entered && profile.Keys.Any(k =>
                        k.Request == Body.Dying && AudioKeys.Reached(k, actor.Audio.Request, motion.Frame, motion.StateLength)))
                    cries.Add(actor.Entity);
            }
        }
        Assert.True(scene.Over);
        Assert.Empty(scene.Problems);
        return new FireLordScene(scene, motions, cries);
    }

    /// <summary>The state an actor's death takes it to from idle, and that state's length.</summary>
    private static (int State, int Length) Death(ActorStateMachine machine)
    {
        var state = machine.Actor.States[0].Branches.Single(b => b.Request == Body.Dying).State;
        return (state, machine.Length(state));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheFireLordAndNiesenLieAtTheirDeathsLastFramesToTheScenesEnd(bool rheda)
    {
        // ProcessPlayers holds a death's last frame (the body's +0x77C), where each actor's state machine would go back to idle (the
        // Fire Lord's arms up) for any request. Pages read slowly, so Niesen is done talking when he's asked to die.
        var played = PlayFireLordScene(rheda, linger: 300);
        foreach (var entity in new[] { FireLord, Niesen })
        {
            var machine = played.Machine(entity);
            Assert.Equal(Death(machine), (machine.State, machine.Clock));
            Assert.True(played.Of(entity).Body.DeathHeld);
        }
        // Each cries out once as he falls: the request 9 key of Alaron's profile, at its first frame.
        Assert.Equal([FireLord, Niesen], played.DeathCries);
    }

    [Fact]
    public void NiesenStillTurningToAlaronIsAskedToDieAndDies()
    {
        // Without Rheda, A pressed at once on "I remembered..." asks for his death (node 237) while he still turns to Alaron (236's
        // FaceRefPoint): the stick of a steered body leaves request 9 standing (Body.ApplyInput, 0x80014FEC), so he dies.
        var played = PlayFireLordScene(rheda: false, linger: 0);
        foreach (var entity in new[] { FireLord, Niesen })
        {
            var machine = played.Machine(entity);
            Assert.Equal(Death(machine), (machine.State, machine.Clock));
        }
        Assert.Equal([FireLord, Niesen], played.DeathCries);
    }

    [Fact]
    public void ADeathAskedOfNiesenWhileHeTalksIsntPlayedAsIn10()
    {
        // With Rheda, A pressed at once on "Rheda, I see a place..." asks for his death (node 230) while the press that brought the
        // line up has him talking (request 4, state 15 of 241 ticks), which has no branch for 9: 1.0's state machine takes no request
        // a state has no branch for, so the death goes, and the wait lasts the talk's length.
        var played = PlayFireLordScene(rheda: true, linger: 0);
        var niesen = played.Machine(Niesen);
        Assert.Equal(0, niesen.State);
        Assert.False(played.Of(Niesen).Body.DeathHeld);
        Assert.Equal([FireLord], played.DeathCries);
    }
}
