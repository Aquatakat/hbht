namespace Aidyn.Core.Combat;

/// <summary>
/// The states of a battle (<c>Combat.State</c>, <c>0x800D9454</c>), 32 in <c>Combat.StateTable</c> (<c>0x800DB0D4</c>).
/// There's no machine of a turn's own: choosing, targeting, the menus, the pause and the AI's turn are all states.
/// docs/systems/combat.md has each one's function; the port fills them in session by session.
/// </summary>
public enum CombatState
{
    /// <summary>Leave the game (<c>Combat.StateEnd</c>).</summary>
    End = 0,

    /// <summary>A turn: the stick moves the combatant in its field, A attacks, Z skips, the C buttons open the menus, Start pauses (<c>Combat.StateDraw</c>).</summary>
    Turn = 1,

    /// <summary>An attack's or spell's animation, then the hit or miss (<c>Combat.StateAction</c>).</summary>
    Action = 2,

    /// <summary>The camera glides to a spell (<c>Combat.StateCast</c>).</summary>
    Cast = 3,

    /// <summary>The numbers, then the fallen taken off and the turn ended (<c>Combat.StateResult</c>).</summary>
    Result = 4,

    /// <summary>A body walks to a point, then <see cref="Battle.NextState"/> (<c>Combat.StateMoving</c>).</summary>
    Moving = 5,

    /// <summary>Turns to the target, then carries out the action (<c>Combat.StateFacing</c>).</summary>
    Facing = 6,

    /// <summary>Waits for the body to stop, then <see cref="Moving"/> (<c>Combat.StateWaitIdle</c>).</summary>
    WaitIdle = 7,

    /// <summary>An enemy's turn: <c>EnemyAI.Step</c> each frame (<c>Combat.StateAI</c>).</summary>
    AI = 8,

    /// <summary>An attack's target: the stick cycles, A confirms.</summary>
    TargetAttack = 9,

    /// <summary>A spell's target.</summary>
    TargetSpell = 10,

    /// <summary>Placing an area: the stick moves its centre.</summary>
    PlaceArea = 11,

    /// <summary>After the fade: the flags, the monster party remembered, the loot, then the post-battle windows (<c>Combat.StateVictory</c>).</summary>
    Victory = 12,

    /// <summary>After the fade: the game over script, or the non-lethal ending (<c>Combat.StateDefeat</c>).</summary>
    Defeat = 13,

    /// <summary>The pause menu is up (<c>Combat.StatePause</c>): the only state that offers flee.</summary>
    Pause = 14,

    /// <summary>The pause menu sliding out.</summary>
    Unpause = 15,

    /// <summary>Leave for trek (<c>Combat.StateLeave</c>).</summary>
    Leave = 16,

    /// <summary>After the Shadow's cinematic (<c>Combat.StateShadowMerge</c>).</summary>
    ShadowMerge = 17,

    /// <summary>Aiming a bow, a thrown weapon that flies or a flask, with a free cursor (<c>Combat.StateTargetThrown</c>).</summary>
    TargetThrown = 18,

    /// <summary>Ends the turn: a potion drunk in the pause menu (<c>Combat.StateEndTurn</c>).</summary>
    EndTurn = 19,

    /// <summary>Leave for trek, with <c>0x800D9376</c> set (<c>Combat.StateLeaveFled</c>).</summary>
    LeaveFled = 20,

    /// <summary>Leave for the game over script (<c>Combat.StateLeaveToScript</c>).</summary>
    LeaveToScript = 21,

    /// <summary>C-down's spells and skills (<c>Combat.StateMenuSpells</c>).</summary>
    MenuSpells = 22,

    /// <summary>C-up's potions and item spells (<c>Combat.StateMenuItems</c>).</summary>
    MenuItems = 23,

    /// <summary>120 ticks, then the turn ends: nothing to do (<c>Combat.StateRest</c>).</summary>
    Rest = 24,

    /// <summary>The Troubadour's prompt, A to start.</summary>
    TroubadourPrompt = 25,

    /// <summary>120 ticks, then the turn ends: a performance begun.</summary>
    Perform = 26,

    /// <summary>Start from any state, keeping it to come back to (<c>Combat.StatePauseRequested</c>, <c>0x800D94D4</c>).</summary>
    PauseRequested = 27,

    /// <summary>120 frames, then <see cref="Defeat"/> (<c>Combat.StateDefeatWait</c>).</summary>
    DefeatWait = 28,

    /// <summary>The Shadow vanishes without the Horn: its text.</summary>
    ShadowVanish = 29,

    /// <summary>The Shadow vanishes: the fade, and leave.</summary>
    ShadowVanishLeave = 30,

    /// <summary>The start: once the fade ends, a turn or the AI's (<c>Combat.StateIntro</c>).</summary>
    Intro = 31,
}
