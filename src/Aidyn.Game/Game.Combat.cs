using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Ui;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// Battles in the runtime: trek fades out, the session frees its world, loads the battle's arena and sets the battle up on
/// it (<see cref="Core.GameSession.BeginBattle"/>), and this draws it: the arena's chunks and sky, every combatant's body
/// with its actor, the combat camera and the fade. Start asks for combat's pause menu; when the battle is over, the session
/// brings trek back. See docs/systems/combat.md.
/// </summary>
public partial class Game
{
    private ChunkStreamerView? _arenaView;
    private CombatRingView? _ringView;
    private readonly Dictionary<Combatant, CombatantView> _combatantViews = [];

    /// <summary>The battle playing, in place of trek's world.</summary>
    private Battle? Battle => _session?.Battle;

    /// <summary>
    /// Trek has faded out into a battle: the player's body goes with trek's world, and the session begins it
    /// (<c>Combat.Init</c>).
    /// </summary>
    private void BeginBattle()
    {
        _changed.Clear();
        _player!.Visible = false;
        if (_shadow is not null)
            _shadow.Visible = false;
        _session!.BeginBattle();
    }

    /// <summary>
    /// The battle has begun on its arena: its chunks drawn, the arena chunk's sky chosen at once, a view for each
    /// combatant's body, and the view through the combat camera.
    /// </summary>
    private void ShowArena(Battle battle, ZoneEngine arena)
    {
        _arenaView = new ChunkStreamerView(_plugins!, _models!, arena.Chunks, null, _shadowTexture) { Name = "Arena" };
        AddChild(_arenaView);
        _sky!.ClearSky();
        _sky.Enter(battle.Arena.Chunk);
        _sky.Apply();
        _camera!.Follow = battle.Camera;
        _camera.Far = SkyView.Far;
        foreach (var combatant in battle.Combatants)
        {
            var view = _combatantViews[combatant] = new CombatantView(combatant, _plugins!, _models!, _shadowTexture);
            AddChild(view);
            view.ResetPhysicsInterpolation();
        }
        _ringView = new CombatRingView(_models!);
        AddChild(_ringView);
        SyncBattle();
        _camera.ResetPhysicsInterpolation();
    }

    /// <summary>
    /// What draws a tick of the battle the session stepped: the sky and the arena's scenery go on, and the bodies, the
    /// camera, the lights and the fade follow it. Once its screen has faded out, the session ends it.
    /// </summary>
    private void StepBattle(bool over)
    {
        _sky!.Step();
        ScrollTextures();
        SyncBattle();
        if (_pointLights)
            LightArena();
        if (over)
            _session!.EndBattle();
    }

    /// <summary>Draws the combatants, the camera and the fade where the battle has them.</summary>
    private void SyncBattle()
    {
        var battle = Battle!;
        foreach (var view in _combatantViews.Values)
        {
            view.Sync();
            view.Draw(_world, battle.Camera.Aim);
        }
        _ringView?.Sync(battle);
        _camera!.Sync();
        ShowFade(battle.Fade.Brightness);
    }

    /// <summary>
    /// Lights the arena as the zone engine draws it in combat's frame: the actors its maps place, then the combatants'
    /// bodies, the party's as the party (<c>+0x70a</c>).
    /// </summary>
    private void LightArena()
    {
        var arena = _session!.Arena!;
        arena.BeginLightFrame(Battle!.Fade.Brightness, _sky!.State.Drawn);
        _arenaView!.LightActors(arena.Lights);
        foreach (var view in _combatantViews.Values)
            view.Light(arena.Lights, arena.Chunks, party: !view.Combatant.Hostile);
    }

    /// <summary>
    /// A press in combat: Start asks for the pause menu (combat's state 27, then 14), and Z (the left trigger) skips a
    /// turn the player plays (<c>Combat.SkipTurn</c>).
    /// </summary>
    private void CombatInput(MenuAction action)
    {
        if (action == MenuAction.Start)
            Battle!.Pause();
        else if (action == MenuAction.TabPrevious)
            Battle!.Skip();
    }

    /// <summary>
    /// The battle's arena has gone: its bodies and chunks are no longer drawn, and the sky is cleared, so trek's chunk
    /// chooses its own at once as trek's world is built again.
    /// </summary>
    private void FreeArena()
    {
        foreach (var view in _combatantViews.Values)
            view.QueueFree();
        _combatantViews.Clear();
        _ringView?.QueueFree();
        _ringView = null;
        _arenaView?.QueueFree();
        _arenaView = null;
        _sky!.ClearSky();
    }
}
