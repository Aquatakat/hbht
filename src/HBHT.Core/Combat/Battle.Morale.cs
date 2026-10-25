using System.Numerics;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Rng;
using HBHT.Plugins.Records;

namespace HBHT.Core.Combat;

// Morale and fleeing (EnemyAI.Morale, CheckFlee, StartFleeing and the flight's walk, Combatant.Leave, and reserves that flee as they
// arrive). docs/systems/combat.md's "Morale and fleeing" has the original's.
public sealed partial class Battle
{
    /// <summary>The combat strings' "Enemy reinforcements flee" (0xEC).</summary>
    private const int ReinforcementsFleeLine = 0xEC;

    /// <summary>How near a flee point a fleeing combatant leaves the field (<c>0x800D2498</c>, <c>0x800D249C</c>).</summary>
    private const float FleePointNear = 1.5f;

    /// <summary>How near a flee point it walks straight there when it has no path (<c>EnemyAI.FleeStep</c>, <c>0x800D2480</c>).</summary>
    private const float FleePointReach = 2.25f;

    /// <summary>
    /// The ticks a freed body, dead or fled, takes to fade from the field (<c>Body.FadeOut</c>'s <c>0x78</c>), and the last of them it
    /// fades over (<c>ProcessPlayers</c>' 60.0 at <c>0x800CDB2C</c>): it's drawn whole for the first 60.
    /// </summary>
    public const int FadeTicks = 120, FadeShownTicks = 60;

    /// <summary>The leader's slot (<c>+0x10</c>): its combatant now, which a reserve may have taken.</summary>
    public int LeaderSlot { get; private set; } = -1;

    /// <summary>Who's in the leader's slot now: the leader, or a reserve that has taken it.</summary>
    private Combatant? LeaderNow => LeaderSlot >= 0 ? _slots[LeaderSlot] : null;

    /// <summary>
    /// The leader's morale (<c>+0x11</c>): its record's at the start, then as <see cref="Morale"/> works it out when another enemy's
    /// morale is checked; it moves every enemy's.
    /// </summary>
    public int LeaderMorale { get; internal set; }

    /// <summary>Where a fleeing combatant's turn began (<c>+0x5318</c>): one that hasn't got a unit from it leaves.</summary>
    public Vector2 FleeOrigin { get; private set; }

    /// <summary>Whether a combatant's aspect is against the time of day (<c>Combatant.AspectOpposesTime</c>, <c>0x800615B4</c>): Lunar by day, Solar at night.</summary>
    private bool AspectOpposed(Aspect aspect)
    {
        var night = PartOfDay == PartOfDay.Night;
        return aspect == Aspect.Lunar ? !night : aspect == Aspect.Solar && night;
    }

    /// <summary>
    /// A combatant's morale (<c>EnemyAI.Morale</c>, <c>0x80056FA8</c>), which clears its check: its record's, less 1 for its aspect
    /// against the time, 2 once a weakness has hit it, 1 with no Stamina, 1 with strictly fewer than half the enemies that began
    /// able, and 1 under a harmful effect; 2 more with fewer than half the party able, 3 more if its target has no Stamina (but not
    /// while it casts a friendly spell, session 11's), and 2 for the flag nothing sets; then for its own check, 4 less with the leader
    /// gone or unable, and 1 more for the leader's morale from 8 to 10, 2 from 11. From 2 to 11. The leader's own
    /// (<paramref name="own"/> false) is kept as <see cref="LeaderMorale"/>.
    /// </summary>
    public int Morale(Combatant c, bool own)
    {
        var ai = c.Ai!;
        ai.Flags &= ~AiFlags.CheckDue;
        var m = (int)(sbyte)ai.Morale;
        if (AspectOpposed(c.Sheet.Aspect))
            m--;
        if (ai.Flags.HasFlag(AiFlags.HitLately))
            m -= 2;
        if (c.Sheet.Stats.Modified(Stat.Stamina) == 0)
            m--;
        if (ActiveCounts.Enemies < EnemiesOnField >> 1)
            m--;
        if (c.Sheet.Effects.Any(e => e.Harmful))
            m--;
        if (ActiveCounts.Party < PartySize >> 1)
            m += 2;
        if (!IsFriendlySpell(c) && ai.Target >= 0 && _slots[ai.Target] is { } target && target.Sheet.Stats.Modified(Stat.Stamina) == 0)
            m += 3;
        if (ai.Flags.HasFlag(AiFlags.Steady))
            m += 2;
        if (own)
        {
            if (LeaderGone || LeaderNow is not { Active: true })
                m -= 4;
            m += LeaderBonus();
        }
        m = Math.Clamp((int)(sbyte)m, 2, 11);
        if (!own)
            LeaderMorale = m;
        return m;
    }

    /// <summary>The leader's morale's say in another's: 1 from 8 to 10, 2 from 11.</summary>
    private int LeaderBonus() => LeaderMorale is >= 8 and <= 10 ? 1 : (byte)LeaderMorale >= 11 ? 2 : 0;

    /// <summary>
    /// Whether a combatant's morale is checked (<c>EnemyAI.MayCheckMorale</c>, <c>0x8005722C</c>): a check due, or no Stamina left;
    /// otherwise, not fleeing already and no more than half the enemies that began still able.
    /// </summary>
    internal bool MayCheckMorale(Combatant c)
    {
        var ai = c.Ai!;
        if (ai.Flags.HasFlag(AiFlags.CheckDue) || c.Sheet.Stats.Modified(Stat.Stamina) <= 0)
            return true;
        return !ai.Flags.HasFlag(AiFlags.Fleeing) && ActiveCounts.Enemies <= EnemiesOnField >> 1;
    }

    /// <summary>
    /// The check to flee as an AI's turn begins (<c>EnemyAI.CheckFlee</c>, <c>0x800572AC</c>), for any but the leader while it
    /// lives (<paramref name="asked"/>): with morale under 12 and a check called for (<see cref="MayCheckMorale"/>), the leader's
    /// morale worked out first while it lives, then its own against 2d6 on the combat stream: rolled over, it flees
    /// (<see cref="StartFleeing"/>); otherwise it stops fleeing.
    /// </summary>
    internal void CheckFlee(Combatant c, bool asked)
    {
        var ai = c.Ai!;
        if (!asked || (byte)ai.Morale >= EnemyAI.Fearless || !MayCheckMorale(c))
            return;
        if (!LeaderGone && LeaderNow is { Ai: not null } leader)
            Morale(leader, own: false);
        var morale = Morale(c, own: true);
        if (morale < Game.Random.Combat.RollD(2, 6))
            StartFleeing(ai);
        else
            ai.Flags &= ~AiFlags.Fleeing;
    }

    /// <summary>
    /// Sets a combatant fleeing for <see cref="EnemyAI.FlightTurns"/> turns (<c>EnemyAI.StartFleeing</c>, <c>0x8005B9E4</c>), unless
    /// it's engaged or the arena has nowhere to flee to.
    /// </summary>
    private void StartFleeing(EnemyAI ai)
    {
        if (ai.Combatant.Engaged || !HasFleePoints)
            return;
        ai.FlightLeft = EnemyAI.FlightTurns;
        ai.Flags |= AiFlags.Fleeing;
    }

    /// <summary>
    /// The arena's flee point nearest a combatant (<c>EnemyAI.NearestFleePoint</c>, <c>0x8005AE3C</c>), the last of equals, and how
    /// far; null with none.
    /// </summary>
    private (Vector2 Point, float Distance)? NearestFleePoint(Combatant c)
    {
        (Vector2, float)? nearest = null;
        var best = 100000f;
        for (var i = 0; i < MaxFleePoints; i++)
        {
            if (Arena.Point(FleePoint(i)) is not { } point)
                continue;
            var at = new Vector2(point.Position.X, point.Position.Z);
            var distance = Vector2.Distance(at, c.Cell);
            if (distance <= best)
            {
                best = distance;
                nearest = (at, distance);
            }
        }
        return nearest;
    }

    /// <summary>
    /// A fleeing combatant's choice (<c>EnemyAI.BeginFleeWalk</c>, <c>0x8005B038</c>): the nearest flee point's cell, with "%s flees",
    /// every flight's turn; within 1.5 of it, it leaves now and rests. With no flee point, it stops fleeing and chooses as any other
    /// (<see cref="ChooseGoal"/>). Whether it has done what it does.
    /// </summary>
    private bool BeginFleeWalk(EnemyAI ai, out int x, out int z)
    {
        var c = ai.Combatant;
        if (NearestFleePoint(c) is not var (point, distance))
        {
            ai.Flags &= ~AiFlags.Fleeing;
            return ChooseGoal(ai, out x, out z);
        }
        Write(Line(FleesLine, c));
        (x, z) = ((byte)(int)point.X, (byte)(int)point.Y);
        if (distance >= FleePointNear)
            return false;
        Leave(c);
        EnterRest();
        return true;
    }

    /// <summary>
    /// With no path, a straight walk to the nearest flee point when it's within 2.25 (<c>EnemyAI.FleeStep</c>, <c>0x8005AA3C</c>).
    /// Whether it walks.
    /// </summary>
    private bool FleeStep(EnemyAI ai)
    {
        var c = ai.Combatant;
        if (c.Freed || NearestFleePoint(c) is not var (point, distance) || distance > FleePointReach)
            return false;
        StartWalkTo(ai, c.Cell, point, 1f);
        return true;
    }

    /// <summary>
    /// A combatant leaves the field (<c>Combatant.Leave</c>, <c>0x80061480</c>), fled or banished: it leaves the grid, it's gone, the
    /// counts of those able fall, its effects and potions come off, and its body fades out over <see cref="FadeTicks"/>, its portrait
    /// marked fled. It isn't dead: a fled enemy still pays its experience and chest.
    /// </summary>
    internal void Leave(Combatant c)
    {
        if (c.Gone)
            return;
        var (x, z) = Cell8(c);
        Grid.Vacate(x, z, c.Footprint);
        c.Flags |= CombatantFlags.Gone;
        var (all, party, enemies) = ActiveCounts;
        ActiveCounts = c.Hostile ? (all - 1, party, enemies - 1) : (all - 1, party - 1, enemies);
        FreeBody(c, fled: true);
    }

    /// <summary>
    /// Whether a reserve flees as it arrives (<c>EnemyAI.ReserveFleesOnArrival</c>, <c>0x80056E18</c>): only where there are flee
    /// points, with morale under 12, and while more than half the enemies that began are still able (the original's test, the
    /// opposite of what one might expect); its record's morale, less 1 for its aspect against the time (any but Solar counting as
    /// Lunar), 4 with the leader gone or unable, and the leader's morale's say, from 2 to 11, against 2d6 on the combat stream.
    /// </summary>
    internal bool ReserveFleesOnArrival(EntityRecord entity)
    {
        if (!HasFleePoints)
            return false;
        var morale = (int)(sbyte)entity.Morale;
        if (morale > 11 || ActiveCounts.Enemies <= EnemiesOnField >> 1)
            return false;
        var m = morale;
        if (AspectOpposed(entity.Aspect == Aspect.Solar ? Aspect.Solar : Aspect.Lunar))
            m--;
        if (LeaderGone || LeaderNow is not { Active: true })
            m -= 4;
        m += LeaderBonus();
        m = Math.Clamp(m, 2, 11);
        return m < Game.Random.Combat.RollD(2, 6);
    }
}
