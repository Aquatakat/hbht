using System.Numerics;
using Aidyn.Core.Actors;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Physics;
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
/// The states are filled in session by session (the roadmap's M3): so far the intro, a turn that does nothing but pause,
/// the pause and leaving.
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
    public int NextReserve { get; private set; }

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
    public Vector2 Focus { get; private set; }

    /// <summary>The arena's grid (<c>+0x3C</c>).</summary>
    public ArenaGrid Grid { get; }

    /// <summary>The state (<c>Combat.State</c>, <c>0x800D9454</c>).</summary>
    public CombatState State { get; private set; } = CombatState.Intro;

    /// <summary>Where a walking or turning state goes on to (<c>Combat.NextState</c>, <c>0x800D9455</c>).</summary>
    public CombatState NextState { get; private set; }

    /// <summary>Whose turn it is (<c>+0x2C</c>).</summary>
    public Combatant? Current { get; private set; }

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

    /// <summary>The log's lines, oldest first, as the battle wrote them (the log box shows them with session 4).</summary>
    public IReadOnlyList<string> Log => _log;

    /// <summary>What the original would crash on or can't do, which the port goes on past.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// Whether it's over, for whoever runs it to end it (<c>Combat.Exit</c>): it has left, for trek or fled (states 16 and
    /// 20), which it does at once, without a fade.
    /// </summary>
    public bool Over => State is CombatState.Leave or CombatState.LeaveFled;

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

    /// <summary>Notes a line in the log.</summary>
    internal void Write(string line) => _log.Add(line);

    /// <summary>
    /// Start (<c>Combat.HandleCancelAndPause</c>, <c>0x80083D34</c>): from a turn, the pause menu with the Party tab and
    /// Flee (state 14), to come back to a turn; from any other state, the System tab alone (state 27, which keeps the state
    /// to come back to, <c>Combat.RequestPause</c>). Not while the screen fades, nor once it's leaving or paused.
    /// </summary>
    public bool Pause()
    {
        if (Fade.Fading || Over || State is CombatState.Pause or CombatState.PauseRequested)
            return false;
        if (State == CombatState.Turn)
        {
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
        if (State == CombatState.Pause)
            State = flee && CanFlee ? CombatState.LeaveFled : NextState;
        else if (State == CombatState.PauseRequested)
            State = _paused;
    }

    /// <summary>
    /// One tick (<c>Combat.Frame</c>): the camera, the bodies, then the state's step, and the fade.
    /// </summary>
    /// <param name="arena">The zone engine the arena is loaded in.</param>
    /// <param name="skyDrawn">Whether the arena's sky is drawn, for its weather.</param>
    /// <param name="motion">Each combatant's animations, or null for one with none.</param>
    public void Tick(ZoneEngine arena, bool skyDrawn, Func<Combatant, IActorMotion?> motion)
    {
        // Bgm.Update waits for the fade, so the battle's music starts once it's in.
        arena.Audio.HoldMusic = Fade.Fading;
        arena.Listen();
        var keys = arena.Keys;
        foreach (var combatant in Combatants)
        {
            var animation = motion(combatant) is { } m ? keys.Sounding(m, combatant.Audio, combatant.Sounds, combatant.Body.Sphere) : IActorMotion.Still;
            combatant.Body.Step(World, animation);
        }
        CombatCamera.Step(this);
        arena.StepSounds(Camera.Aim);
        arena.StepWeather(skyDrawn, arena.Precipitation);
        Step();
        Fade.Step();
    }

    /// <summary>The state's step.</summary>
    private void Step()
    {
        switch (State)
        {
            case CombatState.Intro when !Fade.Fading:
                // Combat.StateIntro: a turn for a combatant the player controls, the AI's for one it plays (Sholeh too).
                State = Current is { Controlled: true } ? CombatState.AI : CombatState.Turn;
                break;
            case CombatState.AI:
                // Until the AI (session 6), an enemy's turn ends at once.
                NextTurn();
                break;
            case CombatState.Pause or CombatState.PauseRequested:
                _idle++;
                break;
        }
    }

    /// <summary>
    /// The turn passes to the next in the round, a new round built when it runs out. A stand-in for
    /// <c>Combatant.EndTurn</c> and <c>TurnOrder.Advance</c>, whose rules are session 3's: here only enemies' turns end.
    /// </summary>
    private void NextTurn()
    {
        Order.Now++;
        if (Order.Now >= Order.Turns.Count)
        {
            Order.Toggle = !Order.Toggle;
            Order.Build(this, Game.Random.Combat);
        }
        Current = Order.Current(this);
        if (Current is { } next)
        {
            Focus = next.Cell;
            Write(CombatText(BeginsAction).Replace("%s", next.Sheet.Name, StringComparison.Ordinal));
        }
        State = Current is { Controlled: true } ? CombatState.AI : CombatState.Turn;
    }

    /// <summary>
    /// The battle's end (<c>Combat.Exit</c>, <c>0x80023060</c>): every combatant's effects come off, the spells' and the potions'
    /// but Stealth's, and the worn enchantments go back on (<see cref="Characters.Character.EndBattleEffects"/>); hit points stay as
    /// they are. Its bodies and arena go with it.
    /// </summary>
    public void Leave()
    {
        foreach (var combatant in Combatants)
            combatant.Sheet.EndBattleEffects(this);
    }
}
