using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Ui;
using Godot;

namespace HBHT.Game;

/// <summary>
/// Battles in the runtime: trek fades out, the session frees its world, loads the battle's arena and sets the battle up on
/// it (<see cref="Core.GameSession.BeginBattle"/>), and this draws it: the arena's chunks and sky, every combatant's body
/// with its actor, the combat camera and the fade. Start asks for combat's pause menu; when the battle is over, the session
/// brings trek back. The battle's one-offs come as its cues (<see cref="ICombatHost"/>): a body's view is made as it joins and freed
/// as it goes, and so are a number's, a missile's and a spell's visual's; what they show each frame is the battle's state. See
/// docs/systems/combat.md.
/// </summary>
public partial class Game
{
    private ChunkStreamerView? _arenaView;
    private CombatRingView? _ringView;
    private CombatGuiView? _battleScreen;
    private CombatNumbersView? _numbersView;
    private MissileView? _missileView;
    private AimMarkerView? _aimView;
    private SpellFxView? _spellFxView;
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
    /// The battle has begun on its arena: its chunks drawn under the sky the session chose for it, a view for each
    /// combatant's body, and the view through the combat camera.
    /// </summary>
    private void ShowArena(Battle battle, ZoneEngine arena)
    {
        _arenaView = new ChunkStreamerView(_plugins!, _models!, arena.Chunks, null, _shadowTexture) { Name = "Arena" };
        AddChild(_arenaView);
        _camera!.Follow = battle.Camera;
        _camera.Far = SkyView.Far;
        _ringView = new CombatRingView(_models!);
        AddChild(_ringView);
        _numbersView = new CombatNumbersView(_plugins!);
        AddChild(_numbersView);
        _missileView = new MissileView(_models!);
        AddChild(_missileView);
        _aimView = new AimMarkerView(_models!);
        AddChild(_aimView);
        if (_spellFx)
        {
            _spellFxView = new SpellFxView(_plugins!, _models!, _particles, c => _combatantViews.GetValueOrDefault(c));
            AddChild(_spellFxView);
        }
        _battleScreen!.Battle = battle;
        _battleScreen.Visible = true;
        SyncBattle();
        _camera.ResetPhysicsInterpolation();
    }

    /// <summary>
    /// What draws a tick of the battle the session stepped: the arena's scenery goes on, and the bodies, the
    /// camera, the lights and the fade follow it. Once its screen has faded out, the session ends it.
    /// </summary>
    private void StepBattle(bool over)
    {
        HoldToWin();
        ScrollTextures();
        SyncBattle();
        if (_pointLights)
            LightArena();
        if (over)
            _session!.EndBattle();
    }

    /// <summary>
    /// A battle's one-off (ADR 0019): a body's view made as it comes onto the field, as the battle is set up or a reserve joins, and
    /// freed once it has gone, its fall played or its flight's fade over (<c>Combat.FreeBody</c>); a number over a head, an arrow, thrown
    /// weapon or flask in flight and a spell's visual made as each starts and freed as it ends. The rest is for the record.
    /// </summary>
    void ICombatHost.Cue(CombatCue cue)
    {
        switch (cue)
        {
            case CombatantJoined { Combatant: var combatant } when !_combatantViews.ContainsKey(combatant):
                var view = _combatantViews[combatant] = new CombatantView(combatant, _plugins!, _models!, _shadowTexture);
                combatant.LocatorAt = view.Locator;
                AddChild(view);
                view.ResetPhysicsInterpolation();
                break;
            case CombatantGone { Combatant: var combatant } when _combatantViews.Remove(combatant, out var gone):
                gone.QueueFree();
                break;
            case NumberShown shown:
                _numbersView?.Show(shown.Number);
                break;
            case NumberGone gone:
                _numbersView?.Remove(gone.Number);
                break;
            case MissileLaunched launched:
                _missileView?.Launch(launched.Flight);
                break;
            case MissileLanded:
                _missileView?.Land();
                break;
            case SpellVisualStarted started:
                _spellFxView?.Started(started.Visual);
                break;
            case SpellVisualEnded ended:
                _spellFxView?.Ended(ended.Visual);
                break;
        }
    }

    /// <summary>Draws the combatants, the numbers over their heads, an arrow in flight, the camera and the fade where the battle has them.</summary>
    private void SyncBattle()
    {
        var battle = Battle!;
        var scenery = _session!.Arena!.Sky.Colors.Tint;
        foreach (var view in _combatantViews.Values)
        {
            view.Sync();
            view.Draw(_world, battle.Camera.Aim, battle.Camera.Position, scenery);
        }
        _numbersView?.Sync(battle.Camera.Position);
        _missileView?.Sync();
        _aimView?.Sync(battle);
        _spellFxView?.Sync(battle.Fade.Brightness, _camera!.Current.GlobalPosition);
        _ringView?.Sync(battle);
        _camera!.Sync();
        ShowFade(battle.Fade.Brightness);
        _battleScreen!.QueueRedraw();
    }

    /// <summary>
    /// Lights the arena as the zone engine draws it in combat's frame: the actors its maps place, then the combatants'
    /// bodies, the party's as the party (<c>+0x70a</c>), each by its tint (the target mark's pulse and a cast's glow): its
    /// ambient, and the moon's light on it.
    /// </summary>
    private void LightArena()
    {
        var arena = _session!.Arena!;
        var fade = Battle!.Fade.Brightness;
        arena.BeginLightFrame(fade);
        _arenaView!.LightActors(arena.Lights);
        foreach (var view in _combatantViews.Values)
        {
            view.Light(arena.Lights, arena.Chunks, party: !view.Combatant.Hostile, view.Combatant.Tint.Level);
            if (_spellFx)
                view.Tinted(arena.Sky.Colors.Sky * 0.5f, fade);
        }
    }

    /// <summary>Debug: how long B is held for the battle to be won (<see cref="HoldToWin"/>), in seconds.</summary>
    private const double WinHoldSeconds = 2;

    /// <summary>How long B has been held in this battle, in seconds of the clock, or −1 once it has won and until it's let go.</summary>
    private double _winHeld;

    /// <summary>
    /// Debug: B held for <see cref="WinHoldSeconds"/> wins the battle on the next turn the player plays (<see cref="Battle.ForceWin"/>).
    /// Held through the enemies' turns, it wins as the player's comes. Counted in the clock's seconds, so fast-forward doesn't shorten it.
    /// </summary>
    private void HoldToWin()
    {
        if (!Debugging || !Input.IsActionPressed(Core.Controls.GameControls.BattleCancel))
        {
            _winHeld = 0;
            return;
        }
        if (_winHeld < 0)
            return;
        _winHeld += 1.0 / Engine.PhysicsTicksPerSecond;
        if (_winHeld < WinHoldSeconds)
            return;
        if (Battle!.Shadow is not null)
        {
            _winHeld = -1;
            Say("The Shadow's battle can't be won by holding B");
        }
        else if (Battle.ForceWin())
        {
            _winHeld = -1;
            Say("Battle won (B held)");
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
    /// The battle's arena has gone: its bodies and chunks are no longer drawn.
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
        _spellFxView?.Clear();
        _spellFxView?.QueueFree();
        _spellFxView = null;
        _arenaView?.QueueFree();
        _arenaView = null;
        _battleScreen!.Visible = false;
        _battleScreen.Battle = null;
    }
}
