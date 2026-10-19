using System.Numerics;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

// Reserves joining at a round's end (Combat.SpawnReserves, 0x8005D914). docs/systems/combat.md's "Reserves" has the original's.
public sealed partial class Battle
{
    /// <summary>The spawn sets a reserve's place is looked for in when its own is taken (<c>Combat.ChooseSpawn</c>), 0–4.</summary>
    private const int SpawnSets = 5;

    /// <summary>Whether the enemies' leader has gone (<c>+0x0C</c>), its slot taken by a reserve.</summary>
    public bool LeaderGone { get; private set; }

    /// <summary>
    /// The reserves come in (<c>Combat.SpawnReserves</c>, <c>0x8005D914</c>, at a round's end): each enemy slot whose combatant is
    /// dead or gone, or that's empty, takes the list's next, at its slot's own spawn of set 0 (<see cref="ChooseSpawn"/>), with a body
    /// set down, its radius and field, its portrait, and the newcomer's flag, which its first turn's "joins the battle" clears. The
    /// leader's slot taken, the leader's gone. The counts aren't changed: the next turn's count finds the newcomers.
    /// </summary>
    /// <remarks>A reserve that flees as it arrives (<c>EnemyAI.ReserveFleesOnArrival</c>, and the hold on its line) is session 6's.</remarks>
    internal void SpawnReserves()
    {
        if (InPlay <= PartySize || !ReserveWaits)
            return;
        for (var slot = PartySize; slot < InPlay && ReserveWaits; slot++)
        {
            if (_slots[slot] is { } old)
            {
                if (!old.Sheet.IsDead && !old.Gone)
                    continue;
                if (!LeaderGone && old == Leader)
                    LeaderGone = true;
            }
            var entity = Enemies[NextReserve];
            NextReserve++;
            if (Game.Recruit(entity) is not { } sheet)
            {
                _problems.Add($"{entity} is in no plugin.");
                continue;
            }
            var look = sheet.Record.Body ?? NoBody;
            var (cell, direction) = ChooseSpawn(Footprint(look), slot - PartySize, look);
            var newcomer = Make(slot, sheet, hostile: true, party: false, cell, direction);
            newcomer.Controlled = true;
            newcomer.Flags |= CombatantFlags.Newcomer;
            // Combat.CreateBody: its radius, its field and its portrait.
            newcomer.UpdateRadius(this);
            newcomer.PlaceField(this);
            Gui.Assign(entity, slot, enemy: true);
        }
    }

    /// <summary>
    /// Where a reserve comes in (<c>Combat.ChooseSpawn</c>, <c>0x8005D748</c>, through <c>Combat.SpawnPosition</c>, one draw for its
    /// direction): its slot's letter of set 0, on the enemies' side (the party's in an ambush); if that's taken on the grid or a body
    /// stands too near (<c>0x8005D474</c>: within its radius and the newcomer's), the first free and clear of each set 0–4 in turn,
    /// its side's letters a–d first, then the other side's (<c>0x8005D5B0</c>). The cell is the point's, truncated.
    /// </summary>
    private (Vector2 Cell, int Direction) ChooseSpawn(int footprint, int letter, EntityBody look)
    {
        var (name, point, direction) = Spawn(party: false, reserve: true, letter, 0, Game.Random.Combat);
        if (point is null)
            _problems.Add($"{Arena.Map.Id} has no spawn {name}, where the original stops (FindReferencePointName).");
        var cell = CellOf(point);
        var reach = look.Radius * look.Scale;
        if (!Grid.IsOccupied((int)cell.X, (int)cell.Y, footprint) && Clear(cell, reach))
            return (cell, direction);
        var enemySide = CollisionByte != AmbushByte;
        for (var set = 0; set < SpawnSets; set++)
        {
            foreach (var good in (ReadOnlySpan<bool>)[!enemySide, enemySide])
            {
                for (var l = 0; l < OnField; l++)
                {
                    if (Arena.Point(SpawnPoint(good, set, l)) is not { } at)
                        continue;
                    var p = new Vector2(at.Position.X, at.Position.Z);
                    if (!Grid.IsOccupied(ArenaGrid.CellOf(p.X), ArenaGrid.CellOf(p.Y), footprint) && Clear(p, reach))
                        return (CellOf(at), direction);
                }
            }
        }
        return (cell, direction);

        bool Clear(Vector2 at, float radius) => Combatants.All(c =>
            c.Freed || Vector2.Distance(new Vector2(c.Body.Sphere.Position.X - Origin.X, c.Body.Sphere.Position.Z - Origin.Z), at) > c.Body.ScaledRadius + radius);
    }
}
