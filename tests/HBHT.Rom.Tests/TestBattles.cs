using System.Reflection;
using HBHT.Core;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Physics;
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

    public static ZoneEngine Zone(Built built) => new(built.Game, built.Streamer) { Camera = built.Battle.Camera };

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
