using Aidyn.Core.Characters;
using Aidyn.Core.MapObjects;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

/// <summary>How a battle ended, for whoever runs it to carry out what follows.</summary>
public enum BattleOutcome
{
    /// <summary>It hasn't.</summary>
    None,

    /// <summary>Won (state 12): the post-battle windows, then trek where it began.</summary>
    Won,

    /// <summary>Lost (state 13, then 21): the game over script.</summary>
    Lost,

    /// <summary>Lost, but not the game's end (<see cref="EncounterRule.NonLethal"/>): trek the next morning.</summary>
    Spared,

    /// <summary>Fled (state 20): trek at the farthest breadcrumb.</summary>
    Fled,

    /// <summary>The Shadow vanished (states 29 and 30): trek where the battle began, with nothing won.</summary>
    Vanished,

    /// <summary>The Shadow merged with its merger (state 17): its cinematic, then trek where the battle began.</summary>
    Merged,
}

/// <summary>
/// A battle's end (<c>Combat.StateVictory</c>, <c>Combat.StateDefeat</c>, <c>Combat.StateDefeatWait</c> and <c>Combat.Exit</c>):
/// what a win or a loss does once the screen has faded, and what leaving does to the party. docs/systems/combat.md's "In and out".
/// </summary>
public sealed partial class Battle
{
    /// <summary>The combat strings' non-lethal defeat line (offset 0x74), the speaker's own.</summary>
    public const int SparedLine = 0x74;

    /// <summary>Frames the non-lethal defeat holds before the screen fades (<c>Combat.StateDefeatWait</c>, 0x78).</summary>
    public const int SparedFrames = 120;

    /// <summary>How the battle ended, once it has left.</summary>
    public BattleOutcome Outcome { get; private set; }

    /// <summary>The battle ends <paramref name="outcome"/>, leaving through <paramref name="state"/>, and says so (<see cref="BattleEnded"/>).</summary>
    private void EndAs(BattleOutcome outcome, CombatState state)
    {
        Outcome = outcome;
        State = state;
        Cue(new BattleEnded(outcome));
    }

    /// <summary>What a win gave, once the battle has left after it.</summary>
    public BattleSpoils? Spoils { get; private set; }

    /// <summary>The battle's own rules, by its encounter flag (<see cref="CombatRecord.Encounters"/>): the Goblin Ambush's.</summary>
    public EncounterRule? Rule => _rules.Encounter(Setup.EncounterFlag);

    /// <summary>Whether losing isn't the game's end (<c>Combat.NonLethal</c>, <c>0x800DA0DC</c>).</summary>
    public bool NonLethal => Rule is { NonLethal: true };

    /// <summary>Ticks in the non-lethal defeat's hold (<c>Combat.IdleFrames</c>).</summary>
    private int _defeatWait;

    /// <summary>
    /// The battle is lost (<c>Combat.Defeat</c>, <c>0x80023F98</c>): state 13 and the screen fading out, or in a non-lethal battle
    /// state 28, which holds a while first.
    /// </summary>
    private void Lose()
    {
        if (NonLethal)
        {
            State = CombatState.DefeatWait;
            _defeatWait = 0;
            return;
        }
        EnterDefeat();
    }

    /// <summary><c>Combat.EnterDefeat</c> (<c>0x80023FD0</c>): state 13, and the screen fades out.</summary>
    private void EnterDefeat()
    {
        State = CombatState.Defeat;
        Fade.Out(FadeOutPerTick);
    }

    /// <summary>
    /// State 28 (<c>Combat.StateDefeatWait</c>, <c>0x80022998</c>): the field goes on being drawn for 120 frames, then the screen
    /// fades out (state 13).
    /// </summary>
    private void StepDefeatWait()
    {
        if (++_defeatWait >= Cadence.Frames(SparedFrames))
            EnterDefeat();
    }

    /// <summary>
    /// State 12 once the screen has faded out (<c>Combat.StateVictory</c>, <c>0x80022450</c>): a monster party's battle sets its
    /// encounter flag, and if that can't hold, remembers the party beaten while its own gate still holds; the bosses' flags again;
    /// and the gold pool goes to the party at once (<c>PauseMenu.PrepareLoot</c>, <c>0x80048FF8</c>). Then the battle leaves, for
    /// the post-battle windows (game mode 6).
    /// </summary>
    /// <remarks>
    /// A party with no encounter flag fights under flag 0, which the original's <c>FlagNetwork.Set</c> (<c>0x8001FDA8</c>) ignores
    /// and its <c>FlagNetwork.Get</c> (<c>0x8001FD5C</c>) reads as 0, so it's remembered: a flagless party, as most wolves and
    /// bears are, stays away for three map loads rather than standing there again as trek comes back.
    /// </remarks>
    private void StepVictory()
    {
        if (Setup.Party is { } party)
        {
            if (Setup.EncounterFlag is { } flag)
                Game.Flags.TrySet(flag, true);
            if ((Setup.PartyGate is not { } gate || Game.Flags.Holds(gate))
                && !(Setup.EncounterFlag is { } set && Game.Flags.IsSet(set)))
                RememberBeaten(party);
        }
        SetKillFlags();
        Game.Party.Gold = (uint)(Game.Party.Gold + GoldPool);
        Spoils = new BattleSpoils(GoldPool, ExperiencePool, Loot!, [.. Levelled]);
        EndAs(BattleOutcome.Won, CombatState.Leave);
    }

    /// <summary>
    /// State 13 once the screen has faded out (<c>Combat.StateDefeat</c>, <c>0x80022590</c>). A loss that is the game's end heals
    /// the party's first member in full and leaves for the game over script (<c>Combat.StartGameOverScript</c>, <c>0x8001F860</c>,
    /// then state 21). A non-lethal one sets the encounter flag, remembers the monster party fought, whatever its gate, and the
    /// clock goes on to 6:00 the next day; then the battle leaves for trek.
    /// </summary>
    private void StepDefeat()
    {
        if (!NonLethal)
        {
            if (Game.Party.Slots[0] is { } leader)
                leader.Heal(leader.MaxHp - leader.Hp);
            EndAs(BattleOutcome.Lost, CombatState.LeaveToScript);
            return;
        }
        if (Setup.EncounterFlag is { } flag)
            Game.Flags.TrySet(flag, true);
        if (Setup.Party is { } party)
            RememberBeaten(party);
        Game.WakeNextMorning();
        EndAs(BattleOutcome.Spared, CombatState.Leave);
    }

    /// <summary>The monster party fought stays away a while (<c>MapObjectMemory.Remember</c>, kind 5, three map loads).</summary>
    private void RememberBeaten(Plugins.RecordId party)
    {
        var key = new Memory(party, MemoryKind.Beaten);
        if (Game.Memory.Find(key) is null)
            Game.Memory.Remember(key, 0, MapObjectMemory.Short);
    }

    /// <summary>
    /// The battle's end (<c>Combat.Exit</c>, <c>0x80023060</c>), however it ends. A battle with an end of its own
    /// (<see cref="EncounterEnd"/>, the Goblin Ambush's) first puts the party's leader's Strength back to its base, sets its
    /// flag and heals its member in full; where trek starts again is the session's to carry out. Then every combatant's effects
    /// come off, the spells' and the potions' but Stealth's, and the worn enchantments go back on
    /// (<see cref="Character.EndBattleEffects"/>), hit points staying as they are; and the dead leave the party
    /// (<see cref="GameState.BringOutYourDead"/>). Its bodies and arena go with it.
    /// </summary>
    /// <remarks>
    /// The original adds the base Strength to the current, which puts it back after the Goblin Ambush's second hit took it to 0
    /// but doubles it when the battle ended before; the port sets it to the base, the user's choice.
    /// </remarks>
    public void Leave()
    {
        if (Rule?.End is { } end)
        {
            if (end.RestoresStrength && Game.Party.Slots[0] is { } leader)
                leader.Stats.ChangeCurrent(Stat.Strength, leader.Stats.RawBase(Stat.Strength) - leader.Stats.Current(Stat.Strength));
            if (end.Flag is { } flag)
                Game.Flags.TrySet(flag, true);
            if (end.Healed is { } healed && Game.Party.Get(healed) is { } member)
                member.Heal(member.MaxHp - member.Hp);
        }
        // Combat.Exit frees the spells' visuals (CombatSpellFx.Free) before the effects come off.
        Fx.Free();
        foreach (var combatant in Combatants)
            combatant.Sheet.EndBattleEffects(this);
        Game.BringOutYourDead();
    }
    /// <summary>
    /// The runtime's debug key: every enemy on the field falls at once and no reserve comes, so the next turn's change wins the
    /// battle. Not the original's.
    /// </summary>
    public void Rout()
    {
        foreach (var enemy in Combatants.Where(c => c.Hostile && !c.Sheet.IsDead && !c.Freed).ToList())
            Fell(enemy);
        NextReserve = Enemies.Count;
    }

    /// <summary>
    /// The runtime's debug win, B held on a turn the player plays: the enemies are routed (<see cref="Rout"/>) and the turn
    /// ends, so its change wins the battle. Whether it was such a turn. Not the original's.
    /// </summary>
    /// <remarks>
    /// Not with the Shadow on the field: its battle ends in the merge or its vanishing, never a win, and felling it would skip both.
    /// </remarks>
    public bool ForceWin()
    {
        if (State != CombatState.Turn || Fade.Fading || Current is not { Controlled: false } current || Shadow is not null)
            return false;
        Rout();
        EndTurn(current);
        return true;
    }

    /// <summary>The runtime's debug key: the whole party falls at once, so the next turn's change loses the battle. Not the original's.</summary>
    public void Collapse()
    {
        foreach (var member in Combatants.Where(c => !c.Hostile && !c.Sheet.IsDead && !c.Freed).ToList())
            Fell(member);
    }

    private void Fell(Combatant c)
    {
        // However many Cheat Deaths it has (Lugash has one).
        while (!c.Sheet.IsDead)
            c.Sheet.TakeDamage(c.Sheet.Hp, c);
        Death(c);
        FreeBody(c);
    }
}
