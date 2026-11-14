using System.Reflection;
using HBHT.Core;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Physics;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// Battles the port sets up itself, for the tests of what the port draws and plays on a battle (the cameras, the menus, the cues):
/// the story saves' party (<see cref="StorySaves"/>, slot A: Alaron, Rheda, Brenna and Sholeh) against ten giant rats, an ambush in
/// arena 3, as the user's rat fight was, or against the enemies a test names. The combat stream starts from a fixed seed, so a battle
/// plays the same each run.
/// </summary>
internal static class TestBattles
{
    public static readonly RecordId Rat = RecordId.Hex(RecordTypes.Entity, 0x026E);

    public const uint Seed = 0x2B1C_0001;

    public sealed record Built(Battle Battle, GameState Game, ChunkStreamer Streamer)
    {
        public Combatant this[int slot] => Battle.Slots[slot]!;
    }

    /// <summary>The rats' battle, or one against <paramref name="enemies"/>, from story save <paramref name="slot"/>'s party.</summary>
    public static Built Plain(PluginSet set, ICombatHost? host = null, IReadOnlyList<PartyEnemy>? enemies = null, char slot = 'A', uint seed = Seed)
    {
        var game = StorySaves.Game(set, slot);
        var arena = Arena.For(set, Battlefield.Chunk(3))!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(set, game, new CollisionWorld(), set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [.. enemies ?? Enumerable.Repeat(new PartyEnemy(Rat, 1, 1), 10)],
            Arena: Battlefield.Chunk(3), Ambush: true, CanFlee: true, EncounterFlag: null, CollisionByte: 0, AniByte: 2);
        game.Random.Combat.Seed = seed;
        return new Built(Battle.Begin(game, setup, arena, streamer.World, host), game, streamer);
    }

    /// <summary>
    /// A battle against <paramref name="enemies"/> in arena <paramref name="arena"/> (<see cref="Battlefield"/>'s numbers), from story save
    /// <paramref name="slot"/>'s party, as <see cref="Plain"/> builds the rats' in arena 3.
    /// </summary>
    public static Built InArena(PluginSet set, int arena, IReadOnlyList<PartyEnemy> enemies, ICombatHost? host = null, char slot = 'A',
        uint seed = Seed, bool canFlee = true)
    {
        var game = StorySaves.Game(set, slot);
        var chunk = Battlefield.Chunk(arena);
        var at = Arena.For(set, chunk)!;
        Assert.NotNull(at);
        var streamer = new ChunkStreamer(set, game, new CollisionWorld(), set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(at.Origin + Battle.ZonePoint);
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [.. enemies], Arena: chunk, Ambush: true, CanFlee: canFlee,
            EncounterFlag: null, CollisionByte: 0, AniByte: 2);
        game.Random.Combat.Seed = seed;
        return new Built(Battle.Begin(game, setup, at, streamer.World, host), game, streamer);
    }

    public static ZoneEngine Zone(Built built) => new(built.Game, built.Streamer) { Camera = built.Battle.Camera };

    /// <summary>
    /// Plays the battle on with the port's own stepping, the party skipping every turn <paramref name="take"/> doesn't want, until one it
    /// does: a member's turn, faded in, with no line held. The member.
    /// </summary>
    public static Combatant Turn(Built built, ZoneEngine zone, Func<Combatant, bool> take)
    {
        var battle = built.Battle;
        for (var i = 0; i < 200_000 && !battle.Over; i++)
        {
            if (battle.Current is { Controlled: false } c && battle.State == CombatState.Turn && !battle.Fade.Fading && battle.Lines.Hold is null)
            {
                if (take(c))
                    return c;
                Assert.True(battle.Skip());
            }
            battle.Tick(zone);
        }
        throw new InvalidOperationException("No turn came that was wanted.");
    }

    /// <summary>As <see cref="Turn"/>, until <paramref name="name"/>'s turn, and one <paramref name="when"/> wants if given.</summary>
    public static Combatant TurnOf(Built built, ZoneEngine zone, string name, Func<Combatant, bool>? when = null) =>
        Turn(built, zone, c => c.Sheet.Name == name && (when?.Invoke(c) ?? true));

    /// <summary>Whether an enemy of <paramref name="c"/>'s on the field is within its reach, a blow's (<see cref="Combatant.IsWithinReach"/>).</summary>
    public static bool EnemyInReach(Built built, Combatant c) =>
        built.Battle.Combatants.Any(o => o.Hostile != c.Hostile && !o.Sheet.IsDead && !o.Gone && c.IsWithinReach(built.Battle, o));

    /// <summary>Opens a held menu, the items' (C-up) or the spells' (C-down), moves to the row that <paramref name="pick"/> finds, and lets go.</summary>
    public static void Choose(Battle battle, bool items, Func<CombatMenuRow, bool> pick)
    {
        Assert.True(battle.OpenMenu(items));
        Assert.Equal(items ? CombatState.MenuItems : CombatState.MenuSpells, battle.State);
        var menu = battle.Menu!;
        var row = menu.Rows.Select((r, i) => (r, i)).First(p => pick(p.r)).i;
        for (var i = 0; i < row; i++)
            battle.MoveMenu(1);
        Assert.True(battle.CloseMenu(items));
        Assert.Null(battle.Menu);
    }

    /// <summary>Sets a property with a private setter.</summary>
    public static void Poke(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    /// <summary>Whether a combatant's footprint covers a cell.</summary>
    public static bool Covers(Combatant c, int x, int z)
    {
        var (cx, cz) = (ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y));
        return x >= cx && x < cx + c.Footprint && z >= cz && z < cz + c.Footprint;
    }
}
