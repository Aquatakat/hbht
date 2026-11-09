using HBHT.Core.Combat;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Core.Ui;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Playthrough;

/// <summary>What the session and its parts ask of the host: the transitions, trek's, the pause menu's and a battle's.</summary>
public sealed partial class PlaythroughHost : ITrekHost, IPauseMenuHost, ICombatHost
{
    /// <summary>
    /// A transition, carried out as <c>Game.cs</c>'s switch does, less what only draws. The session has already carried out its rules,
    /// opened the menu for those that open it, and built or freed the world; what's left for a host is mostly views, which this has none
    /// of. Every case is here, carried out or left with why.
    /// </summary>
    void ITransitionHost.Transition(Transition next)
    {
        _transitions.Add(new TransitionAt(_now, next));
        switch (next)
        {
            case Transition.OpenShop or Transition.OpenTraining or Transition.OpenLoot or Transition.OpenMenu or Transition.PauseBattle
                or Transition.PostBattle or Transition.ReopenMenu:
                // The session has opened the menu on its tab or in its barter, training or post-battle state; the runtime shows its view.
                // The menu policy acts on it from the next tick, as it takes presses.
                _menuOpen = 0;
                break;
            case Transition.LoadGame load:
                // The runtime reads the save and starts the game again from it (Game.LoadFrom); this does too, once the tick is over.
                _load = load.Path;
                break;
            case Transition.StartBattle battle:
                // A battle no trek cued, which nothing in the runtime sends: the runtime says so, and so does this.
                Note(NoteKind.Say, $"A battle with {battle.Setup.Party?.ToString() ?? "a script's enemies"} would start.");
                break;
            case Transition.StartedBattle started:
                // The runtime builds the arena's views (ShowArena); the battle is the policy's to play.
                _battles.Add(new BattleFought(_now, started.Battle.Setup));
                Battles.Begin(this, started.Battle);
                break;
            case Transition.EndBattle ended:
                // The runtime frees the arena's views (FreeArena).
                if (_battles.LastOrDefault() is { Ended: null } fought)
                {
                    fought.Ended = _now;
                    fought.Outcome = ended.Battle.Outcome;
                }
                Battles.End(this, ended.Battle);
                break;
            case Transition.GameOver:
                // The runtime goes back to the title on a new game (Game.GameOver): the run is over, its starting again the caller's.
                End = HostEnd.GameOver;
                break;
            case Transition.Quit:
                End = HostEnd.Quit;
                break;
            case Transition.ToTitle:
                End = HostEnd.ToTitle;
                break;
            case Transition.Restart restart:
                // A change of renderer: the runtime saves and restarts on it. Headless there's no renderer, so the game goes on as it stands.
                Note(NoteKind.Log, $"The options asked for {restart.Renderer}: no renderer here, so the game goes on.");
                break;
            case Transition.ShowTitle or Transition.StartTitleScenery or Transition.EndTitleScenery or Transition.EndTitle:
                // The title's views. The host starts its games itself and never shows the title (GameSession.ShowTitle); the session steps
                // its scenery if it does.
                break;
            case Transition.Camp camp:
                // The runtime prints it; the session has camped.
                Note(NoteKind.Log, $"Camping: {camp.Outcome}.");
                break;
            case Transition.Rest rest:
                // The session has passed the hours (Transition.CarryOut); the runtime prints them.
                Note(NoteKind.Log, $"{rest.Hours} hours pass.");
                break;
            case Transition.Say say:
                Note(NoteKind.Say, say.Text);
                break;
            case Transition.StartCutScene start:
                // The runtime builds the stage's views and the actors' (ShowStage), whose animations move them: the actors spawned as it
                // started get theirs now.
                _actors.Clear();
                MakeActors(start.Scene);
                break;
            case Transition.EndCutScene:
                // The runtime frees the actors' views and the stage's (FreeStage).
                _actors.Clear();
                break;
            case Transition.ResumeTrek:
                // The runtime shows the player's body and puts its camera on trek's (ShowTrek): drawing alone.
                break;
            case Transition.PlayCredits or Transition.EndCredits:
                // The credits' layer and music (PlayCredits, EndCredits): the session steps them, and the host skips or hurries them as it ticks.
                break;
            case Transition.PlayCinematic or Transition.EndCinematic:
                // The cinematic's layer, its music stopped and started (PlayCinematic, EndCinematic): the session steps it, and the host
                // skips it as it ticks.
                break;
            default:
                Warn($"The host has no way to carry out {next}.");
                break;
        }
    }

    // Trek's asks.

    /// <summary>The runtime puts its camera and the player's body where he came through to: drawing alone.</summary>
    void ITrekHost.Arrived()
    {
    }

    /// <summary>The runtime redraws the box: drawing alone. The host reads the page as it ticks.</summary>
    void ITrekHost.ConversationChanged()
    {
    }

    /// <summary>A wanderer's animations, which move its body: its actor's, made as the runtime's view makes them.</summary>
    IRootMotion ITrekHost.Motion(Wanderer wanderer) => _wanderers.GetValue(wanderer, w => MotionOf(w.Look.Actor));

    /// <summary>
    /// A scene object's actor asked for an animation (a tripwire's): the runtime's animator plays it. Nothing in the rules waits on a map
    /// object's actor, so there's nothing to play.
    /// </summary>
    void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
    {
    }

    /// <summary>A scene object came or went: the runtime builds its chunk's scenery again. Drawing alone.</summary>
    void ITrekHost.Changed(LoadedMap map, SceneObject scene)
    {
    }

    /// <summary>One of Oriana's path lights went out: drawing alone.</summary>
    void ITrekHost.PutOut(LoadedMap map, SceneObject scene)
    {
    }

    /// <summary>The menu rolled up and a chunk's objects came or went: the runtime rebuilds its scenery. Drawing alone.</summary>
    void ITrekHost.Regated(LoadedMap map)
    {
    }

    void ITrekHost.Log(string text) => Note(NoteKind.Log, text);

    void ITrekHost.Warn(string text) => Warn(text);

    /// <summary>A script's cue: the runtime plays its sounds, and the cut-scene carries out the rest. Nothing to hear here.</summary>
    void IScriptHost.Cue(ScriptCue cue)
    {
    }

    // The pause menu's asks.

    /// <summary>The menu's sounds and the scroll's steps: nothing to hear or draw. The scroll's clock is past any state's end, so it takes no time.</summary>
    void IPauseMenuHost.Cue(PauseMenuCue cue)
    {
    }

    /// <summary>The System tab's Save, as the runtime's (<c>Game.SaveTo</c>) with no screenshot beside it.</summary>
    string? IPauseMenuHost.Save(string path)
    {
        try
        {
            if (!Session.NotePlace())
                return "The player isn't in a chunk.";
            Game.Label = null;
            SaveFile.Write(path, Game.Save());
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Warn(e.ToString());
            return e.Message;
        }
    }

    // A battle's asks.

    /// <summary>A battle's one-off: the runtime makes and frees views by them. The host notes the party's deaths.</summary>
    void ICombatHost.Cue(CombatCue cue)
    {
        if (cue is CombatantDied { Combatant: { Hostile: false } died } && _battles.LastOrDefault() is { Ended: null } fought)
            fought.Deaths.Add(died.Entity);
    }

    /// <summary>
    /// The System tab's Load, as the runtime's (<c>Game.LoadFrom</c>, then <c>Game.Start</c>): the save is read, and a new session starts on
    /// it where it stands, a save of a new game playing the opening. One that can't be read is said, and the game goes on.
    /// </summary>
    private void Load(string path)
    {
        SaveGame save;
        try
        {
            save = SaveFile.Read(path, _plugins);
        }
        catch (Exception e) when (e is IOException or System.Text.Json.JsonException or InvalidDataException)
        {
            Note(NoteKind.Say, e.Message);
            return;
        }
        var game = GameState.Load(_plugins, save);
        StartSession(game, GameStart.For(game), opening: game.StartsAsNewGame);
    }
}
