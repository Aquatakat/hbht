using System.Numerics;
using Aidyn.Core;
using Aidyn.Core.Characters;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Sets up the battle of the user's Combat checkpoints (ten giant rats, an ambush, arena 3, animation byte 2) as
/// <see cref="Battle.Begin"/> does, from checkpoint 01's party and random state, and checks it against checkpoint 02 (the first
/// turn, Rheda's): the combatants' slots, cells, directions, footprints and flags, the turn order, the leader, whether the
/// party can flee, the arena grid and the bodies' places. The tests skip without the ROM and the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class BattleCheckpointTests(Na10Import fixture, ITestOutputHelper output)
{
    private const string Before = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Combat 01, before.bs1";
    private const string FirstTurn = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Combat 02, rheda.bs1";

    private const uint CombatPointer = 0x800E0BF0;
    private const uint RandomState = 0x800D8088;
    private const uint EnemyList = 0x800D9458;
    private const uint FleeAllowed = 0x800D9481;
    private const uint BodyPointers = 0x800D9488;

    /// <summary>Where a player body keeps its position (the radius is at +0x64, the goal at +0x274).</summary>
    private const uint BodyPosition = 0x68;

    /// <summary>
    /// The steps of the stream from checkpoint 01 to the ambush roll, the first of the setup's draws (and the first the port
    /// makes on its combat stream): the 293 of the frames before the battle began, then the battle music's, which the port
    /// draws on its cosmetic stream. The roll is the 295th draw, and checkpoint 02's directions follow it.
    /// </summary>
    private const int DrawsBeforeSetup = 293 + 1;

    private static readonly RecordId Rat = RecordId.Hex(RecordTypes.Entity, 0x026E);

    private PluginSet Set => fixture.Data.Set;

    private static ushort Half(AresCheckpoint c, uint address) => (ushort)(c.ReadByte(address) << 8 | c.ReadByte(address + 1));

    private static float Float(AresCheckpoint c, uint address) => BitConverter.UInt32BitsToSingle(c.ReadWord(address));

    /// <summary>The battle set up as checkpoint 02's, with what it was made from, and the checkpoints.</summary>
    private sealed record Built(Battle Battle, Arena Arena, GameState Game, AresCheckpoint Before, AresCheckpoint After)
    {
        /// <summary>Checkpoint 02's combat struct (<c>Combat.Current</c>).</summary>
        public uint Combat => After.ReadWord(CombatPointer);

        /// <summary>Its combatants by slot, as addresses or 0.</summary>
        public uint Combatant(int slot) => After.ReadWord(Combat + 0x52D0 + (uint)slot * 4);
    }

    private Built Build()
    {
        var before = TestRoms.Checkpoint(Before);
        var after = TestRoms.Checkpoint(FirstTurn);
        var game = GameState.Load(Set, CheckpointSave.Convert(before, Set, "Combat 01, before"));
        Assert.Empty(game.Problems);

        // The arena's chunk loaded about the zone engine's point in it, as GameSession.BeginBattle has it.
        var arena = Arena.For(Set, 3)!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), Set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);

        // A wanderer's ambush of ten giant rats, handed over as the list (the enemy roll is checked elsewhere).
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [.. Enumerable.Repeat(new PartyEnemy(Rat, 1, 1), 10)],
            Arena: 3, Ambush: true, CanFlee: true, EncounterFlag: null, CollisionByte: 0, AniByte: 2);

        var random = new Lcg(before.ReadWord(RandomState));
        for (var i = 0; i < DrawsBeforeSetup; i++)
            random.Next();
        game.Random.Combat.Seed = random.Seed;

        return new Built(Battle.Begin(game, setup, arena, streamer.World), arena, game, before, after);
    }

    [Fact]
    public void TheBattleHasTheCheckpointsPartyAndTenGiantRats()
    {
        var built = Build();
        Assert.Empty(built.Battle.Problems);

        Assert.Equal(["Alaron", "Rheda", "Brenna", "Sholeh"], built.Game.Party.Members.Select(m => m.Name));
        var list = Enumerable.Range(0, 12).Select(i => Half(built.After, EnemyList + (uint)i * 2)).ToList();
        Assert.Equal([.. Enumerable.Repeat((ushort)0x026E, 10), (ushort)0, (ushort)0], list);
        Assert.Equal(10, built.Battle.Enemies.Count);
        Assert.All(built.Battle.Enemies, e => Assert.Equal(Rat, e));
        Assert.Equal(4, built.Battle.PartySize);
        Assert.Equal(4, built.Battle.EnemiesOnField);
    }

    [Fact]
    public void EachCombatantHasTheCheckpointsSlotCellDirectionFootprintAndFlags()
    {
        var built = Build();
        var battle = built.Battle;
        var after = built.After;

        Assert.Equal(0x80282230u, built.Combat);
        var present = Enumerable.Range(0, Battle.SlotCount).Count(s => built.Combatant(s) != 0);
        Assert.Equal(8, present);
        Assert.Equal(present, battle.Combatants.Count());

        var wrong = new List<string>();
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var p = built.Combatant(slot);
            var port = battle.Slots[slot];
            if (p == 0)
            {
                if (port is not null)
                    wrong.Add($"slot {slot}: the port has {port}, the original no one");
                continue;
            }
            if (port is null)
            {
                wrong.Add($"slot {slot}: the original has a combatant, the port no one");
                continue;
            }
            var who = $"slot {slot} ({port.Sheet.Name})";
            Check($"{who} slot", after.ReadByte(p + 0x20), port.Slot);
            Check($"{who} cell", new Vector2(Float(after, p), Float(after, p + 4)), port.Cell);
            Check($"{who} turn start", new Vector2(Float(after, p + 8), Float(after, p + 12)), port.TurnStart);
            Check($"{who} direction", after.ReadByte(p + 0x14), port.Direction);
            Check($"{who} footprint", after.ReadByte(p + 0x23), port.Footprint);
            // Active, and for an enemy hostile and from the list; the rest of the flags are for the turns.
            Check($"{who} flags", Half(after, p + 0x2C), (ushort)port.Flags);
            Check($"{who} flags (as the party and rats have them)", slot < 4 ? 0x02 : 0x32, (int)port.Flags);
            Check($"{who} last target", -1, port.LastTarget);
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));

        void Check<T>(string what, T original, T port)
        {
            if (!EqualityComparer<T>.Default.Equals(original, port))
                wrong.Add($"{what}: original {original}, port {port}");
        }
    }

    [Fact]
    public void TheTurnOrderIsTheCheckpointsAndRhedaGoesFirst()
    {
        var built = Build();
        var battle = built.Battle;
        var after = built.After;

        var order = built.Combat + 0x30;
        var count = after.ReadByte(order);
        var turns = Enumerable.Range(0, count).Select(i => (int)after.ReadByte(after.ReadWord(order + 8) + (uint)i)).ToList();
        Assert.Equal([1, 3, 0, 2, 3, 2, 1, 5, 4, 7, 0, 6], turns);
        Assert.Equal(turns, battle.Order.Turns);
        Assert.Equal(0, after.ReadByte(order + 1));
        Assert.Equal(0, battle.Order.Now);

        Assert.Equal(built.Combatant(turns[0]), after.ReadWord(built.Combat + 0x2C));
        Assert.Equal(1, battle.Current?.Slot);
        Assert.Equal("Rheda", battle.Current?.Sheet.Name);

        // Who can act, and the party's and the enemies' (+2, +4, +6).
        Assert.Equal((8, 4, 4), (after.ReadByte(order - 0x30 + 2), after.ReadByte(order - 0x30 + 4), after.ReadByte(order - 0x30 + 6)));
        Assert.Equal((8, 4, 4), battle.ActiveCounts);
    }

    [Fact]
    public void TheAmbushRollDrainedTheStaminaOfTheBestRanger()
    {
        // Sholeh's Ranger of 6 rolls in an ambush and costs 1 Stamina (5 less the skill, at least 1): the checkpoint's 60
        // is 59 as the battle begins, the party's others untouched. (The notes of the research that it drew nothing missed
        // that this was the draw before the directions.)
        var built = Build();
        var before = built.Before.Party;
        var after = built.After.Party;
        var stamina = (int)Stat.Stamina;
        Assert.Equal(before.Select(s => s.Current[stamina]), built.Game.Party.Members.Select(m => m.Stats.Current(Stat.Stamina) + (m.Name == "Sholeh" ? 1 : 0)));
        Assert.Equal(after.Select(s => s.Current[stamina]), built.Game.Party.Members.Select(m => m.Stats.Current(Stat.Stamina)));
        Assert.Equal(after.Select(s => s.Modified[stamina]), built.Game.Party.Members.Select(m => m.Stats.Modified(Stat.Stamina)));
    }

    [Fact]
    public void TheLeaderIsTheFirstRatAndThePartyMayFleeFromAnArenaWithFleePoints()
    {
        var built = Build();
        var battle = built.Battle;
        var after = built.After;

        Assert.Equal(4, after.ReadByte(built.Combat + 0x10));
        Assert.Equal(4, battle.Leader?.Slot);
        Assert.Equal(1u, after.ReadWord(built.Combat + 0x5440));
        Assert.True(battle.HasFleePoints);

        // The permission a monster party's battle sets and no rat takes away (0x800D9481).
        Assert.Equal(1, after.ReadByte(FleeAllowed));
        Assert.True(battle.CanFlee, "the setup allows fleeing, and no enemy forbids it");
    }

    [Fact]
    public void TheGridIsSizedAndFilledAsTheCheckpointsIs()
    {
        var built = Build();
        var battle = built.Battle;
        var after = built.After;
        var grid = built.Combat + 0x3C;

        Assert.Equal((85, 98), (after.ReadByte(grid + 0x4E22), after.ReadByte(grid + 0x4E23)));
        Assert.Equal((85, 98), (battle.Grid.Width, battle.Grid.Depth));
        Assert.Equal(8330, Half(after, grid + 0x4E20));
        Assert.Equal(8330, battle.Grid.Area);
        Assert.Equal((after.ReadByte(grid + 0x4E24), after.ReadByte(grid + 0x4E25)), battle.Grid.Half);
        Assert.Equal(after.ReadByte(grid + 0x4E26), battle.Grid.Diagonal);

        // Each cell: the floor's height times two, with the cells taken (off the collision, or under a combatant) marked.
        var differ = new List<string>();
        var matched = 0;
        for (var x = 0; x < battle.Grid.Width; x++)
        {
            for (var z = 0; z < battle.Grid.Depth; z++)
            {
                var original = after.ReadByte(grid + (uint)(x * ArenaGrid.Side + z));
                if (original == battle.Grid[x, z])
                    matched++;
                else
                    differ.Add($"({x},{z}) original {original:X2}, port {battle.Grid[x, z]:X2}");
            }
        }
        output.WriteLine($"{matched} of {battle.Grid.Area} cells match");
        Assert.True(differ.Count == 0, $"{differ.Count} of {battle.Grid.Area} cells differ, e.g. {string.Join("; ", differ.Take(12))}");
    }

    [Fact]
    public void EachBodyStandsWhereTheCheckpointsDoesInTheArenaChunk()
    {
        var built = Build();
        var wrong = new List<string>();
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var body = built.After.ReadWord(BodyPointers + (uint)slot * 4);
            if (built.Battle.Slots[slot] is not { } combatant)
            {
                Assert.Equal(0u, body);
                continue;
            }
            var original = new Vector3(Float(built.After, body + BodyPosition), Float(built.After, body + BodyPosition + 4), Float(built.After, body + BodyPosition + 8));
            var port = combatant.Body.Sphere.Position - built.Arena.Origin;
            if (Vector3.Distance(original, port) > 0.01f)
                wrong.Add($"slot {slot}: original {original}, port {port}");
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }
}
