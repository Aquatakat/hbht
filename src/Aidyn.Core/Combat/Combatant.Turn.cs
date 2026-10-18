using System.Numerics;
using Aidyn.Core.Characters;

namespace Aidyn.Core.Combat;

public sealed partial class Combatant
{
    /// <summary>
    /// The field's radius by modified Dexterity (<c>Combat.RadiusByDexterity</c>, <c>0x800E0C20</c>): the radius is the
    /// first place whose byte is at least the Dexterity, 14 past the end.
    /// </summary>
    public static readonly IReadOnlyList<byte> RadiusByDexterity = [0, 0, 0, 3, 7, 12, 16, 21, 26, 28, 30, 33, 36, 38];

    /// <summary>The field's most (<c>UpdateRadius</c>'s cap).</summary>
    public const int MostRadius = 13;

    /// <summary>How much farther than their bodies' radii two combatants reach each other (<c>0x800D2810</c>).</summary>
    public const float Reach = 0.75f;

    /// <summary>
    /// The field's radius by Dexterity alone (<c>UpdateRadius</c>'s first part): 0 at 0, 3 for 1–3, 4 for 4–7, up to 13
    /// for 37–38 and 14 above, which the cap takes back to 13.
    /// </summary>
    public static int RadiusFor(int dexterity)
    {
        var radius = 0;
        while (radius < RadiusByDexterity.Count && RadiusByDexterity[radius] < dexterity)
            radius++;
        return radius;
    }

    /// <summary>
    /// Works out its field's radius (<c>Combatant.UpdateRadius</c>, <c>0x8005F29C</c>): its modified Dexterity's band;
    /// then each timed effect in its slot's order, Haste adding its level and Stellar Gravity taking its level away, to
    /// no less than 1; then 1 at most while an opponent is within reach (<see cref="CombatantFlags.Engaged"/>, set or
    /// cleared here, which also stops a performance), or while performing, and 1 near the Shadow; 13 at most; and 0 for
    /// those who never move (Pochanargat).
    /// </summary>
    public void UpdateRadius(Battle battle)
    {
        var radius = RadiusFor(Sheet.Stats.Modified(Stat.Dexterity));
        radius = RadiusWithEffects(radius);
        Flags &= ~CombatantFlags.Engaged;
        if (!battle.FreeOfOpponents(this))
        {
            Flags |= CombatantFlags.Engaged;
            radius = Math.Min(radius, 1);
            if (Performing)
                StopPerforming();
        }
        else if (Performing)
        {
            radius = Math.Min(radius, 1);
        }
        else if (battle.NearShadow(this))
        {
            radius = 1;
        }
        if (radius > MostRadius)
            radius = MostRadius;
        if (battle.Rooted(Entity))
            radius = 0;
        Radius = radius;
    }

    /// <summary>Ends a performance (<c>Combatant.StopPerforming</c>). What else it ends is the Troubadour's, session 9's.</summary>
    internal void StopPerforming() => Flags &= ~CombatantFlags.Performing;

    /// <summary>
    /// Whether <paramref name="other"/> is within reach (<c>Combatant.IsWithinReach</c>, <c>0x8006011C</c>): both read
    /// from their bodies first, then the distance across the ground is at most each body's larger radius, scaled or
    /// not, summed, and <see cref="Reach"/>.
    /// </summary>
    public bool IsWithinReach(Battle battle, Combatant other)
    {
        SyncFromBody(battle);
        other.SyncFromBody(battle);
        var reach = BodyReach(this) + BodyReach(other) + Reach;
        return Vector2.Distance(Cell, other.Cell) <= reach;

        static float BodyReach(Combatant c) => Math.Max(c.Body.ScaledRadius, c.Body.Sphere.Radius);
    }

    /// <summary>Where its body was as its <see cref="Cell"/> was last read from it, in the world's coordinates.</summary>
    internal Vector3 ReadFrom { get; set; }

    /// <summary>
    /// Reads where its body is (<c>Combatant.SyncFromBody</c>, <c>0x80067074</c>): its footprint leaves the grid, its
    /// <see cref="Cell"/> is the body's X and Z as they are, and its footprint takes the grid where it is now.
    /// </summary>
    /// <remarks>
    /// The original keeps a body in the arena chunk's own coordinates, and the port in the world's, so taking the chunk's
    /// origin off loses a little; a body that hasn't moved keeps its cell as it was, its spawn's exactly.
    /// </remarks>
    public void SyncFromBody(Battle battle)
    {
        battle.Grid.Vacate(ArenaGrid.CellOf(Cell.X), ArenaGrid.CellOf(Cell.Y), Footprint);
        if (Body.Sphere.Position != ReadFrom)
        {
            ReadFrom = Body.Sphere.Position;
            var at = ReadFrom - battle.Origin;
            Cell = new Vector2(at.X, at.Z);
        }
        battle.Grid.Occupy(ArenaGrid.CellOf(Cell.X), ArenaGrid.CellOf(Cell.Y), Footprint);
    }

    /// <summary>
    /// After the stick has driven its body in its turn (<c>Combatant.UpdateFromBody</c>, <c>0x8005F784</c>): it reads the
    /// body (<see cref="SyncFromBody"/>), the battle's focus follows it, and its facing is the body's; if either has
    /// changed, it counts a move, and its fourth sets <see cref="CombatantFlags.Moved"/>. (The target sets are built
    /// again too, with session 5.)
    /// </summary>
    public void UpdateFromBody(Battle battle)
    {
        var (cell, facing) = (Cell, Facing);
        SyncFromBody(battle);
        battle.Focus = Cell;
        Facing = Body.Heading;
        if (Cell == cell && Facing == facing)
            return;
        if (Moves < 3)
            Moves++;
        else
            Flags |= CombatantFlags.Moved;
    }

    /// <summary>Its place as its turn begins (<c>Combatant.SaveStartPosition</c>, <c>0x8005F770</c>).</summary>
    public void SaveStartPosition() => TurnStart = Cell;

    /// <summary>
    /// Whether it can act (<c>Combatant.UpdateActive</c>, <c>0x8006615C</c>): not with no Intelligence, Willpower,
    /// Dexterity or Strength left, nor held by an immobilising effect (<see cref="IsImmobilised"/>).
    /// </summary>
    public void UpdateActive(Battle battle)
    {
        var stats = Sheet.Stats;
        var able = stats.Modified(Stat.Intelligence) != 0 && stats.Modified(Stat.Willpower) != 0
            && stats.Modified(Stat.Dexterity) != 0 && stats.Modified(Stat.Strength) != 0 && !IsImmobilised;
        Flags = able ? Flags | CombatantFlags.Active : Flags & ~CombatantFlags.Active;
    }

    /// <summary>
    /// Its field about where it stands, its radius across (<c>Combatant.PlaceRingAndBody</c>, <c>0x8006627C</c>): the
    /// body's <see cref="Movement.Body.Field"/>, centred at its X and Z and the body's height now.
    /// </summary>
    public void PlaceField(Battle battle)
    {
        var centre = battle.Origin + new Vector3(Cell.X, 0, Cell.Y);
        Body.Field = (centre with { Y = Body.Sphere.Position.Y }, Radius);
    }
}
