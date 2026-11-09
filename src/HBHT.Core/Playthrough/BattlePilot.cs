using System.Numerics;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Trek;
using HBHT.Plugins.Saves;

namespace HBHT.Core.Playthrough;

/// <summary>
/// Plays the party's battles (docs/systems/playthrough.md, layer 5): every member's turns are the original's combat AI's, as the
/// enemies' and Sholeh's are (<see cref="Battle.PlayByAi"/>), and the bot never flees. A battle the party loses (the game over:
/// every member down, or Alaron dead) is played again from its start and won outright: the game is started again from a save made as
/// the battle was cued, the same battle cued on it, and won on the party's first turn (<see cref="Battle.ForceWin"/>), so its flags,
/// rewards and what follows come as a win's do, once. The second <see cref="BattleFought"/> says so (<see cref="BattleFought.LostFirst"/>).
/// </summary>
public sealed class BattlePilot : IBattlePolicy
{
    private readonly List<string> _problems = [];

    /// <summary>
    /// How long the AI may play a battle before it's won outright as a stalemate (<see cref="Battle.Rout"/>): half an hour of the game's
    /// time.
    /// </summary>
    public long MostTicks { get; init; } = 60 * 60 * 30;

    /// <summary>
    /// How long the battle may go with nobody's hit points changing before it's won outright as a stalemate: three minutes. The AI's way
    /// to a goal is the original's greedy step (<see cref="ArenaGrid.FindPath"/>), which a pocket stops, and two AIs that both find none
    /// rest turn after turn, where a player would walk round.
    /// </summary>
    public long QuietTicks { get; init; } = 60 * 60 * 3;

    /// <summary>
    /// Whether the members' AI may cast blasts such as Fireball, which it aims with no care for its own side: not by default.
    /// </summary>
    public bool CastsBlasts { get; init; }

    /// <summary>
    /// Whether a battle won with a member dead in it is played again from its start and won outright, as a battle lost is: a player who
    /// loses a companion reloads. The replay's <see cref="BattleFought.DiedFirst"/> says so. On by default.
    /// </summary>
    public bool RewindDeaths { get; init; } = true;

    /// <summary>What went wrong that the host's records don't say: a battle lost with no save to go back to, or lost again.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>The save made as the last battle was cued, and its setup: what a loss goes back to.</summary>
    private (string Save, BattleSetup Setup)? _cued;

    /// <summary>The next battle to begin is the one lost, played again to be won outright.</summary>
    private bool _replayNext;

    /// <summary>The replay to come is of a battle won with a death.</summary>
    private bool _diedNext;

    /// <summary>The battle playing is the one lost, played again to be won outright.</summary>
    private bool _replaying;

    /// <summary>Ticks of the battle playing, and whether it has been routed as a stalemate.</summary>
    private long _ticks;

    private bool _routed;

    /// <summary>Every combatant's hit points summed as last seen, and the ticks since they changed.</summary>
    private int _hp = -1;

    private long _quiet;

    /// <summary>Saves the game as it stands in trek, the battle cued, unless it's the replay of one lost.</summary>
    public void Cued(PlaythroughHost host, BattleSetup setup)
    {
        if (_replayNext)
            return;
        _cued = host.Session.NotePlace() ? (SaveFile.Serialize(host.Game.Save()), setup) : null;
        if (_cued is null)
            _problems.Add($"{host.Clock.Ticks}: the battle with {Name(setup)} was cued where the player stands in no chunk, so it can't be played again.");
    }

    /// <summary>The party's members are handed to the AI; the replay of a lost battle is left to be won outright.</summary>
    public void Begin(PlaythroughHost host, Battle battle)
    {
        (_ticks, _quiet, _hp) = (0, 0, -1);
        _routed = false;
        _replaying = _replayNext;
        _replayNext = false;
        if (_replaying)
        {
            if (host.BattlesFought.LastOrDefault() is { Ended: null } fought)
            {
                if (_diedNext)
                    fought.DiedFirst = true;
                else
                    fought.LostFirst = true;
            }
            _diedNext = false;
            return;
        }
        foreach (var member in battle.Combatants.Where(c => c.Party).ToList())
        {
            if (!battle.PlayByAi(member) || CastsBlasts || member.Ai is not { } ai)
                continue;
            // The AI aims a blast (Fireball) at the opponents' midst whatever of its own side stands there, and its score for one that
            // catches more of its own than of theirs wraps round as a byte to the best of all (EnemyAI.ScoreSpellCells): an enemy's
            // Fireball takes its fellows with it, and a member's would take the party. So members don't cast them.
            for (var i = 0; i < ai.Slots.Length; i++)
            {
                if (ai.Slots[i] is { } spell && host.Game.Plugins.TryGet<Plugins.Records.SpellRecord>(spell)?.Targeting.Blast == true)
                    ai.Slots[i] = null;
            }
        }
    }

    public PlayerInput Tick(PlaythroughHost host, Battle battle)
    {
        var still = new PlayerInput(Vector2.Zero);
        if (_replaying)
        {
            host.WinBattleOutright();
            return still;
        }
        // A battle the AI can't end, as two sides that can't reach each other: won outright, said as a stalemate.
        var hp = battle.Combatants.Sum(c => c.Sheet.Hp);
        if (hp != _hp)
            (_hp, _quiet) = (hp, 0);
        _ticks++;
        if ((++_quiet > QuietTicks || _ticks > MostTicks) && !_routed && battle.Shadow is null && !battle.Over)
        {
            _routed = true;
            battle.Rout();
            if (host.BattlesFought.LastOrDefault() is { Ended: null } fought)
                fought.WonOutright = fought.Stalemate = true;
            _problems.Add(_ticks > MostTicks
                ? $"{host.Clock.Ticks}: the battle with {Name(battle.Setup)} went on {MostTicks} ticks, and was won outright."
                : $"{host.Clock.Ticks}: nobody in the battle with {Name(battle.Setup)} was hit for {QuietTicks} ticks, and it was won outright.");
        }
        return still;
    }

    /// <summary>A loss starts the game again from the save made as the battle was cued, and cues it again, to be won outright.</summary>
    public void End(PlaythroughHost host, Battle battle)
    {
        var replayed = _replaying;
        _replaying = false;
        var fought = host.BattlesFought.LastOrDefault();
        var died = battle.Outcome != BattleOutcome.Lost && RewindDeaths && !replayed && fought is { Deaths.Count: > 0 } && _cued is not null;
        if (battle.Outcome != BattleOutcome.Lost && !died)
            return;
        if (replayed || _cued is not var (save, setup))
        {
            _problems.Add($"{host.Clock.Ticks}: the battle with {Name(battle.Setup)} was lost{(replayed ? " again, played to be won outright" : ", with no save to go back to")}.");
            return;
        }
        var plugins = host.Game.Plugins;
        var game = GameState.Load(plugins, SaveFile.Parse(save, plugins));
        if (fought is not null)
            fought.Undone = true;
        _replayNext = true;
        _diedNext = died;
        host.Restart(game, h => h.Session.CueBattle(setup));
    }

    private static string Name(BattleSetup setup) => setup.Party?.ToString() ?? $"a {setup.Source} battle";
}

/// <summary>What the run asks of the party's slots (docs/systems/playthrough.md, "Goals and the party").</summary>
public static class PartyRoom
{
    /// <summary>
    /// Whether the party has room for a member: fewer than four living. A member who died in a battle leaves the party as it ends
    /// (<see cref="GameState.BringOutYourDead"/>), so their slot is empty after it; during it, a dead member's slot counts as empty too.
    /// </summary>
    public static bool HasEmptySlot(this Party party) => party.Members.Count(m => !m.IsDead) < Party.MaxSize;
}
