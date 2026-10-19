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
    private CombatGuiView? _battleScreen;
    private CombatNumbersView? _numbersView;
    private MissileView? _missileView;
    private AimMarkerView? _aimView;
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
        SyncCombatants(battle);
        _ringView = new CombatRingView(_models!);
        AddChild(_ringView);
        _numbersView = new CombatNumbersView(_plugins!);
        AddChild(_numbersView);
        _missileView = new MissileView(_models!);
        AddChild(_missileView);
        _aimView = new AimMarkerView(_models!);
        AddChild(_aimView);
        _battleScreen!.Battle = battle;
        _battleScreen.Visible = true;
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

    /// <summary>
    /// A view for each combatant whose body is on the field: a reserve that has come in gets one, and one whose body has gone, its
    /// fall played or its flight's fade, loses it (<c>Combat.FreeBody</c>).
    /// </summary>
    private void SyncCombatants(Battle battle)
    {
        foreach (var (combatant, view) in _combatantViews.Where(p => p.Key.Freed && p.Key.Fading == 0 || !battle.Slots.Contains(p.Key)).ToList())
        {
            view.QueueFree();
            _combatantViews.Remove(combatant);
        }
        foreach (var combatant in battle.Combatants.Where(c => !c.Freed && !_combatantViews.ContainsKey(c)))
        {
            var view = _combatantViews[combatant] = new CombatantView(combatant, _plugins!, _models!, _shadowTexture);
            AddChild(view);
            view.ResetPhysicsInterpolation();
        }
    }

    /// <summary>Draws the combatants, the numbers over their heads, an arrow in flight, the camera and the fade where the battle has them.</summary>
    private void SyncBattle()
    {
        var battle = Battle!;
        SyncCombatants(battle);
        foreach (var view in _combatantViews.Values)
        {
            view.Sync();
            view.Draw(_world, battle.Camera.Aim);
        }
        _numbersView?.Sync(battle.Numbers, battle.Camera.Position);
        _missileView?.Sync(battle.Flight);
        _aimView?.Sync(battle);
        // The spells' visuals and sounds are session 10's: until then, what the battle asked for is let go.
        battle.SpellVisuals.Clear();
        _ringView?.Sync(battle);
        _camera!.Sync();
        ShowFade(battle.Fade.Brightness);
        _battleScreen!.QueueRedraw();
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
            view.Light(arena.Lights, arena.Chunks, party: !view.Combatant.Hostile, view.Combatant.Mark ?? 1);
    }

    /// <summary>
    /// A press in combat (<c>Combat.Controls</c>): Start asks for the pause menu (combat's state 27, then 14); Z (the left trigger)
    /// skips a turn the player plays (<c>Combat.SkipTurn</c>) or puts an aimed attack away; A attacks in a turn and confirms an
    /// aimed attack's target; B puts it away; left and right step its cursor (state 9), and C-left and C-right a bow's or thrown
    /// weapon's (state 18).
    /// </summary>
    private void CombatInput(MenuAction action)
    {
        var battle = Battle!;
        // Combat.DismissPopup: any press closes Sense Aura's popup, and ends the turn.
        if (action is not (MenuAction.None or MenuAction.DetailsRelease or MenuAction.AppraiseRelease) && battle.DismissAura())
            return;
        // A held menu (states 22 and 23): up and down move its cursor, and letting its button go chooses; nothing else does anything.
        if (battle.Menu is { } menu)
        {
            if (action is MenuAction.Up or MenuAction.Down)
                battle.MoveMenu(MenuStick.Vertical(action));
            else if (action == (menu.Items ? MenuAction.AppraiseRelease : MenuAction.DetailsRelease))
                battle.CloseMenu(menu.Items);
            return;
        }
        switch (action)
        {
            case MenuAction.Appraise:
                battle.OpenMenu(items: true);
                break;
            case MenuAction.Details:
                battle.OpenMenu(items: false);
                break;
            case MenuAction.Start:
                battle.Pause();
                break;
            case MenuAction.TabPrevious:
                battle.Skip();
                break;
            case MenuAction.Accept:
                if (!battle.Attack())
                    battle.Confirm();
                break;
            case MenuAction.Back:
                battle.Cancel();
                break;
            case MenuAction.Left or MenuAction.Right when battle.State is CombatState.TargetAttack or CombatState.TargetSpell:
                battle.StepTarget(action == MenuAction.Left ? 1 : -1);
                break;
            case MenuAction.MemberLeft or MenuAction.MemberRight when battle.State == CombatState.TargetThrown:
                battle.StepTarget(action == MenuAction.MemberLeft ? 1 : -1);
                break;
        }
    }

    /// <summary>
    /// The battle's debug keys: F9 moves the radar between the log box and the screen's bottom right corner, F10 routs the enemies
    /// and F11 fells the party (<see cref="Battle.Rout"/>, <see cref="Battle.Collapse"/>). Whether the event was one.
    /// </summary>
    private bool CombatDebugKey(InputEvent e)
    {
        // F10 routs the enemies and F11 fells the party, so the next turn's change (Z) wins or loses the battle.
        if (e.IsActionPressed("battle_rout"))
        {
            Battle!.Rout();
            return true;
        }
        if (e.IsActionPressed("battle_collapse"))
        {
            Battle!.Collapse();
            return true;
        }
        if (!e.IsActionPressed("toggle_radar_anchor"))
            return false;
        _battleScreen!.Radar = _battleScreen.Radar == RadarAnchor.LogBox ? RadarAnchor.Corner : RadarAnchor.LogBox;
        _battleScreen.QueueRedraw();
        return true;
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
        _numbersView?.QueueFree();
        _numbersView = null;
        _missileView?.QueueFree();
        _missileView = null;
        _aimView?.QueueFree();
        _aimView = null;
        _arenaView?.QueueFree();
        _arenaView = null;
        _battleScreen!.Visible = false;
        _battleScreen.Battle = null;
        _sky!.ClearSky();
    }
}
