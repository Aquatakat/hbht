using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Trek;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>
/// A battle (<c>Combat.Current</c>, <c>0x800E0BF0</c>, the original's struct of 0x544C bytes, and the machine of 32 states
/// that drives it, <see cref="CombatState"/>): its arena, the enemy list, the combatants on their spawns, the leader, the
/// flee points, the arena's grid, whose turn it is, the log, the camera and the fade. <see cref="Begin"/> builds one from a
/// <see cref="BattleSetup"/> as <c>Combat.Init</c> and <c>Combat.Setup</c> do, and <see cref="Tick"/> steps the state.
/// docs/systems/combat.md maps the original's.
/// </summary>
/// <remarks>
/// The states are filled in session by session (the roadmap's M3): so far the intro, a turn the player walks in its field,
/// skips or attacks in (aiming, turning, the action, an arrow's flight and the result), the AI's turn (its walk, its turn to the
/// target, its attack and its rest), the pause and leaving.
/// </remarks>
public sealed partial class Battle
{
    /// <summary>The combatants a battle holds (<c>Combat.Bodies</c>, 14 by slot).</summary>
    public const int SlotCount = 14;

    /// <summary>The enemies on the field at once; the rest of the list are reserves.</summary>
    public const int OnField = 4;

    /// <summary>The enemy list's length (the setup block's 12 IDs).</summary>
    public const int MaxEnemies = 12;

    private readonly Combatant?[] _slots = new Combatant?[SlotCount];
    private readonly List<string> _log = [];
    private readonly List<string> _problems = [];
    /// <summary>The state the pause menu returns to (<c>0x800D94D4</c>).</summary>
    private CombatState _paused;

    private Battle(GameState game, BattleSetup setup, Arena arena, CollisionWorld world, IReadOnlyList<RecordId> enemies)
    {
        Game = game;
        Setup = setup;
        Arena = arena;
        World = world;
        Enemies = enemies;
        Grid = new ArenaGrid();
        CanFlee = setup.CanFlee;
    }

    public GameState Game { get; }

    /// <summary>What it was started from.</summary>
    public BattleSetup Setup { get; }

    /// <summary>Its arena.</summary>
    public Arena Arena { get; }

    /// <summary>The arena's loaded collision.</summary>
    public CollisionWorld World { get; }

    /// <summary>
    /// The enemy list (<c>+0x28</c>, the setup block's): the first four start on the field, the rest are reserves, in order.
    /// </summary>
    public IReadOnlyList<RecordId> Enemies { get; }

    /// <summary>The combatants by slot (<c>+0x52D0</c>): the party's from 0, then the enemies on the field.</summary>
    public IReadOnlyList<Combatant?> Slots => _slots;

    /// <summary>The combatants in play, by slot.</summary>
    public IEnumerable<Combatant> Combatants => _slots.OfType<Combatant>();

    /// <summary>The party's size (<c>+0x03</c>).</summary>
    public int PartySize { get; private set; }

    /// <summary>The enemies on the field at the start (<c>+0x05</c>).</summary>
    public int EnemiesOnField { get; private set; }

    /// <summary>The next reserve's place in <see cref="Enemies"/>, or the list's length with none left (<c>+0x07</c>).</summary>
    public int NextReserve { get; internal set; }

    /// <summary>The enemies' leader (<c>+0x10</c>): the first with the highest base Intelligence.</summary>
    public Combatant? Leader { get; private set; }

    /// <summary>
    /// Whether the arena has flee points, any of <c>flee0</c>–<c>flee7</c> (<c>+0x5440</c>): not arenas 17 and 20, where
    /// enemies can't flee. The AI finds them by name (<see cref="FleePoint"/>).
    /// </summary>
    public bool HasFleePoints { get; private set; }

    /// <summary>The turn order (<c>+0x30</c>).</summary>
    public TurnOrder Order { get; } = new();

    /// <summary>The camera's focus in the arena's cells (<c>+0x18</c>, <c>+0x1C</c>): the current combatant's, or its target's.</summary>
    public Vector2 Focus { get; internal set; }

    /// <summary>The arena's grid (<c>+0x3C</c>).</summary>
    public ArenaGrid Grid { get; }

    /// <summary>The state (<c>Combat.State</c>, <c>0x800D9454</c>).</summary>
    public CombatState State { get; private set; } = CombatState.Intro;

    /// <summary>Where a walking or turning state goes on to (<c>Combat.NextState</c>, <c>0x800D9455</c>).</summary>
    public CombatState NextState { get; private set; }

    /// <summary>Whose turn it is (<c>+0x2C</c>).</summary>
    public Combatant? Current { get; internal set; }

    /// <summary>The combatants in play (<c>+0x01</c>), for the turn order's indexes.</summary>
    internal int InPlay => PartySize + EnemiesOnField;

    /// <summary>Of them, those able to act, the party's and the enemies' (<c>+0x02</c>, <c>+0x04</c>, <c>+0x06</c>).</summary>
    public (int All, int Party, int Enemies) ActiveCounts { get; internal set; }

    /// <summary>Whether the party can flee: the setup's say, and the bosses' and the flags' (<c>0x800D9481</c>).</summary>
    public bool CanFlee { get; private set; }

    /// <summary>The camera (camera mode 4), which the zone engine draws from.</summary>
    public FollowCamera Camera => CombatCamera.Camera;

    /// <summary>What moves the camera (<c>ProcessCombatCamera</c>).</summary>
    public CombatCamera CombatCamera { get; private set; } = null!;

    /// <summary>The screen's fade: in as the battle starts, out as it leaves.</summary>
    public ScreenFade Fade { get; } = new();

    /// <summary>The log's lines, oldest first, as the battle wrote them.</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>The log box's lines: the one shown, the one queued and the third (<see cref="CombatLog"/>).</summary>
    public CombatLog Lines { get; } = new();

    /// <summary>The battle screen: the portraits, the log box and the radar (<c>combatgui.cpp</c>).</summary>
    public Ui.CombatGui Gui { get; private set; } = null!;

    /// <summary>What the original would crash on or can't do, which the port goes on past.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Whether it's over, for whoever runs it to end it (<c>Combat.Exit</c>): it has left, as <see cref="Outcome"/> says: for
    /// trek, after a win, a non-lethal loss or fleeing (states 16 and 20), or for the game over script (21).
    /// </summary>
    public bool Over => State is CombatState.Leave or CombatState.LeaveFled or CombatState.LeaveToScript;

    /// <summary>Whether the party fled (<c>Zones.Fled</c>, <c>0x800D9376</c>), which trek reads as it's built again.</summary>
    public bool Fled => State == CombatState.LeaveFled;

    /// <summary>
    /// Whether the pause menu is up, from a turn (state 14, with the Party tab and Flee) or from any other state (27, the
    /// System tab alone), once its two frames have passed (<c>Combat.IdleFrames</c>).
    /// </summary>
    public bool Paused => State is CombatState.Pause or CombatState.PauseRequested && _idle >= PauseFrames * Cadence.FrameTicks;

    /// <summary>Whether the pause menu has the Party tab and offers Flee: it came from a turn (state 14).</summary>
    public bool PausedFromTurn => State == CombatState.Pause;

    /// <summary>The frames the pause waits before the menu (<c>Combat.IdleFrames</c>, <c>0x800DB0AC</c>).</summary>
    public const int PauseFrames = 2;

    /// <summary>Ticks in a pause, counting to its menu.</summary>
    private int _idle;

    /// <summary>Notes a line in the log, and shows it in the log box at once (<c>Combat.ShowLog</c>).</summary>
    internal void Write(string line)
    {
        _log.Add(line);
        Lines.Show(line);
    }

    /// <summary>
    /// Start (<c>Combat.HandleCancelAndPause</c>, <c>0x80083D34</c>): from a turn, the pause menu with the Party tab and
    /// Flee (state 14), to come back to a turn; from any other state, the System tab alone (state 27, which keeps the state
    /// to come back to, <c>Combat.RequestPause</c>). Not while the screen fades, nor once it's leaving or paused.
    /// </summary>
    public bool Pause()
    {
        // A held menu takes no Start (its handler's default), nor does Sense Aura's popup, which any press closes first.
        if (Fade.Fading || Over || State is CombatState.Pause or CombatState.PauseRequested || Menu is not null || Aura is not null)
            return false;
        if (State is CombatState.Turn or CombatState.TargetAttack or CombatState.TargetThrown or CombatState.TargetSpell
            or CombatState.PlaceArea or CombatState.TroubadourPrompt)
        {
            // Combat.HandleCancelAndPause: from aiming too, the camera back on the combatant, and the pause comes back to the turn.
            if (State != CombatState.Turn)
            {
                CameraOwner = Current;
                _aimTurn = null;
                if (Current is { } aiming)
                    Focus = aiming.Cell;
                State = CombatState.Turn;
            }
            State = CombatState.Pause;
            NextState = CombatState.Turn;
        }
        else
        {
            _paused = State;
            State = CombatState.PauseRequested;
        }
        _idle = 0;
        return true;
    }

    /// <summary>
    /// The pause menu has rolled up (<c>PauseMenu.Frame</c>'s state 9): with <paramref name="flee"/>, from a turn's pause
    /// while the party can flee, the battle leaves for trek, fled (state 20; the original shows no line for it); otherwise
    /// it goes back to the state it paused.
    /// </summary>
    public void Unpause(bool flee)
    {
        if (State == CombatState.Pause && flee && CanFlee)
        {
            State = CombatState.LeaveFled;
            Outcome = BattleOutcome.Fled;
        }
        else if (State == CombatState.Pause)
            State = NextState;
        else if (State == CombatState.PauseRequested)
            State = _paused;
    }

    /// <summary>
    /// One tick (<c>Combat.Frame</c>): the controls, the bodies (each held in its field and pushed out of the others,
    /// <c>ProcessPlayers</c>), the camera, then the state's step, and the fade.
    /// </summary>
    /// <param name="arena">The zone engine the arena is loaded in.</param>
    /// <param name="skyDrawn">Whether the arena's sky is drawn, for its weather.</param>
    /// <param name="motion">Each combatant's animations, or null for one with none.</param>
    /// <param name="readInput">The player's controls, read only in a turn the player plays, once the screen has faded in.</param>
    public void Tick(ZoneEngine arena, bool skyDrawn, Func<Combatant, IActorMotion?> motion, Func<PlayerInput>? readInput = null)
    {
        // Combat.HoldForMessage: while "Enemy reinforcements flee" holds the battle, its controls and states wait.
        var held = Lines.StepHold();
        // Combat.Controls: the controls come first, once the screen has faded in.
        if (!Fade.Fading && !held)
        {
            switch (State)
            {
                case CombatState.Turn:
                    InputTurn(readInput?.Invoke() ?? default);
                    break;
                case CombatState.TargetThrown:
                    MoveAimCursor(readInput?.Invoke() ?? default);
                    break;
                case CombatState.PlaceArea:
                    MoveArea(readInput?.Invoke() ?? default);
                    break;
                case CombatState.Facing when _turning && Current is { } facing:
                    // Combat.InputFacing: the body's turn is snapped to its goal.
                    facing.Body.Heading = facing.Facing;
                    break;
            }
        }
        if (State is CombatState.TargetAttack or CombatState.TargetSpell && _aimTurn is { } aim && Current is { } aiming && aiming.Body.TurnToward(aim))
            _aimTurn = null;
        // Bgm.Update waits for the fade, so the battle's music starts once it's in.
        arena.Audio.HoldMusic = Fade.Fading;
        arena.Listen();
        var keys = arena.Keys;
        var crowd = Crowd();
        for (var i = 0; i < crowd.Count; i++)
        {
            var combatant = _crowd[i];
            var start = combatant.Body.Sphere.Position;
            var animation = motion(combatant) is { } m ? keys.Sounding(m, combatant.Audio, combatant.Sounds, combatant.Body.Sphere) : IActorMotion.Still;
            // A body that has fallen holds its last frame.
            combatant.Body.Step(World, combatant.Fallen ? IActorMotion.Still : animation);
            Movement.Crowd.Push(crowd, i, start);
            AfterBodies(combatant, animation);
        }
        CombatCamera.Step(this);
        arena.StepSounds(Camera.Aim);
        arena.StepWeather(skyDrawn, arena.Precipitation);
        if (!held)
            Step();
        StepMarks();
        Numbers.Tick();
        Fade.Step();
        Gui.Tick(this);
    }

    private readonly List<Combatant> _crowd = [];

    /// <summary>The combatants' bodies as they push each other, in slot order.</summary>
    private List<CrowdMember?> Crowd()
    {
        _crowd.Clear();
        _crowd.AddRange(Combatants.Where(c => !c.Freed));
        // A fled body, gone from the battle at once, fades out where it stood.
        foreach (var c in Combatants.Where(c => c.Fading > 0))
            c.Fading--;
        return [.. _crowd.Select(c => (CrowdMember?)new CrowdMember(c.Body.Sphere, c.Body.Scale, null))];
    }

    /// <summary>The state's step.</summary>
    private void Step()
    {
        switch (State)
        {
            case CombatState.Intro when !Fade.Fading:
                // Combat.StateIntro: a turn for a combatant the player controls, the AI's for one it plays. It sets the
                // state alone, not the state to go on to.
                State = Current is { Controlled: true } ? CombatState.AI : CombatState.Turn;
                break;
            case CombatState.AI when Current is { } current:
                StepAI(current);
                break;
            case CombatState.Moving:
                StepMoving();
                break;
            case CombatState.Rest or CombatState.Perform:
                StepRest();
                break;
            case CombatState.MenuSpells or CombatState.MenuItems:
                StepMenu();
                break;
            case CombatState.Facing:
                StepFacing();
                break;
            case CombatState.Action:
                StepAction();
                break;
            case CombatState.Cast:
                StepFlight();
                break;
            case CombatState.Result:
                StepResult();
                break;
            case CombatState.Victory when !Fade.Fading:
                StepVictory();
                break;
            case CombatState.Defeat when !Fade.Fading:
                StepDefeat();
                break;
            case CombatState.DefeatWait:
                StepDefeatWait();
                break;
            case CombatState.Pause or CombatState.PauseRequested:
                _idle++;
                break;
            case CombatState.EndTurn when Current is { } ending:
                // Combat.StateEndTurn (0x80022764): a weapon changed or a flask taken in hand in the pause menu.
                EndTurn(ending);
                break;
        }
    }

}
