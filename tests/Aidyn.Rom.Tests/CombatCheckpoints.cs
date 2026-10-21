using System.Numerics;
using System.Reflection;
using Aidyn.Core;
using Aidyn.Core.Calendar;
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
/// The user's Combat checkpoints (one battle against ten rats) laid over the battle the port sets up from checkpoint 01, for the tests
/// that replay the original's results on them (<see cref="AttackCheckpointTests"/>, <see cref="AiCheckpointTests"/>).
/// </summary>
internal static class CombatCheckpoints
{
    public const string Before = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Combat 01, before.bs1";

    public const uint CombatPointer = 0x800E0BF0, RandomState = 0x800D8088, BodyPointers = 0x800D9488;

    /// <summary>The quicksort's stream (<c>Quicksort.Seed</c>), which the AI's sorts draw on.</summary>
    public const uint SortState = 0x800E6B60;

    /// <summary>Where a body keeps its position (x, y, z), in the arena chunk's own coordinates.</summary>
    public const uint BodyPosition = 0x68;

    /// <summary>The draws from checkpoint 01's stream to the ambush roll (see <see cref="BattleCheckpointTests"/>).</summary>
    public const int DrawsBeforeSetup = 293 + 1;

    public static readonly RecordId Rat = RecordId.Hex(RecordTypes.Entity, 0x026E);


    // ---------------------------------------------------------------------------------------------------------------------------
    // The checkpoints, and a battle with one laid over it

    public static AresCheckpoint Combat(int number)
    {
        var tag = $" - Combat {number:00},";
        var name = Directory.Exists(TestRoms.Folder)
            ? Directory.EnumerateFiles(TestRoms.Folder).Select(Path.GetFileName).OfType<string>()
                .FirstOrDefault(n => n.Contains("(V1.0)", StringComparison.Ordinal) && n.Contains(tag, StringComparison.Ordinal) && n.EndsWith(".bs1", StringComparison.Ordinal))
            : null;
        if (name is null)
            Assert.Skip($"No Combat {number:00} checkpoint in {TestRoms.Folder}.");
        return TestRoms.Checkpoint(name);
    }

    public static ushort Half(AresCheckpoint c, uint address) => (ushort)(c.ReadByte(address) << 8 | c.ReadByte(address + 1));

    public static float Float(AresCheckpoint c, uint address) => BitConverter.UInt32BitsToSingle(c.ReadWord(address));

    public static Vector3 Vector(AresCheckpoint c, uint address) => new(Float(c, address), Float(c, address + 4), Float(c, address + 8));

    /// <summary>Sets a property with a private setter (a battle's state the tests lay over).</summary>
    public static void Poke(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    /// <summary>Whether a combatant's footprint covers a cell.</summary>
    public static bool Covers(Combatant c, int x, int z)
    {
        var (cx, cz) = (ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y));
        return x >= cx && x < cx + c.Footprint && z >= cz && z < cz + c.Footprint;
    }

    public sealed record Built(Battle Battle, GameState Game, ChunkStreamer Streamer, AresCheckpoint Cp)
    {
        /// <summary>The cells the overlay freed because the checkpoint has them free with no one on them (history it can't replay).</summary>
        public List<(int X, int Z)> Freed { get; } = [];

        /// <summary>The checkpoint's combat struct (<c>Combat.Current</c>).</summary>
        public uint Struct => Cp.ReadWord(CombatPointer);

        /// <summary>A combatant's record in the checkpoint, or 0.</summary>
        public uint Record(int slot) => Cp.ReadWord(Struct + 0x52D0 + (uint)slot * 4);

        /// <summary>A combatant's body in the checkpoint, or 0.</summary>
        public uint Body(int slot) => Cp.ReadWord(BodyPointers + (uint)slot * 4);

        public Combatant this[int slot] => Battle.Slots[slot]!;
    }

    /// <summary>Checkpoint 01's battle, as <see cref="BattleCheckpointTests"/> builds it: the party and ten rats, and nothing of the checkpoint laid over it.</summary>
    public static Built Plain(PluginSet Set, int checkpoint)
    {
        var before = TestRoms.Checkpoint(Before);
        var cp = Combat(checkpoint);
        var game = GameState.Load(Set, CheckpointSave.Convert(before, Set, "Combat 01, before"));
        Assert.Empty(game.Problems);
        var arena = Arena.For(Set, 3)!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), Set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [.. Enumerable.Repeat(new PartyEnemy(Rat, 1, 1), 10)],
            Arena: 3, Ambush: true, CanFlee: true, EncounterFlag: null, CollisionByte: 0, AniByte: 2);
        var random = new Lcg(before.ReadWord(RandomState));
        for (var i = 0; i < DrawsBeforeSetup; i++)
            random.Next();
        game.Random.Combat.Seed = random.Seed;
        return new Built(Battle.Begin(game, setup, arena, streamer.World), game, streamer, cp);
    }

    /// <summary>
    /// Checkpoint 01's battle with checkpoint <paramref name="checkpoint"/>'s state laid over it: the day, then each combatant's cell, turn
    /// start, last target, direction, facing, moves, action, flags, field radius, stats, and its body's place and heading, who is dead
    /// (their bodies gone), the grid's taken cells, and the battle's counts, turn and next reserve. What the checkpoint can be checked
    /// against that the overlay doesn't copy, it checks (<see cref="Verify"/>).
    /// </summary>
    public static Built At(PluginSet Set, int checkpoint)
    {
        var built = Plain(Set, checkpoint);
        var (battle, game, cp) = (built.Battle, built.Game, built.Cp);

        // The day: the clock, its part and the rain, which the chances read.
        game.Clock.Time = cp.Time;
        if (game.Weather is { } weather)
        {
            weather.PartOfDay = (PartOfDay)cp.Weather.PartOfDay;
            weather.Intensity = cp.Weather.Intensity;
        }

        // Every footprint leaves the grid where it stood, and takes it again where it stands now.
        foreach (var c in battle.Combatants)
            battle.Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var p = built.Record(slot);
            Assert.Equal(p != 0, battle.Slots[slot] is not null);
            if (p == 0)
                continue;
            var c = built[slot];
            c.Cell = new Vector2(Float(cp, p), Float(cp, p + 4));
            c.TurnStart = new Vector2(Float(cp, p + 8), Float(cp, p + 12));
            c.LastTarget = (int)cp.ReadWord(p + 0x10);
            c.Direction = cp.ReadByte(p + 0x14);
            c.Facing = new Vector2(Float(cp, p + 0x18), Float(cp, p + 0x1C));
            c.Radius = cp.ReadByte(p + 0x21);
            c.Moves = cp.ReadByte(p + 0x22);
            c.Footprint = cp.ReadByte(p + 0x23);
            c.Action = (CombatAction)cp.ReadByte(p + 0x25);
            c.Damage = cp.ReadByte(p + 0x29);
            c.Flags = (CombatantFlags)Half(cp, p + 0x2C);

            // The AI record (+0x44): what changes as the battle goes; its style is checked (Verify).
            var record = cp.ReadWord(p + 0x44);
            Assert.Equal(record != 0, c.Ai is not null);
            if (c.Ai is { } ai)
            {
                ai.Kind = (AiKind)cp.ReadByte(record + 4);
                ai.Target = (sbyte)cp.ReadByte(record + 5);
                ai.Morale = (sbyte)cp.ReadByte(record + 6);
                ai.Wanted = (AiKind)cp.ReadByte(record + 7);
                ai.Flags = (AiFlags)cp.ReadByte(record + 9);
                ai.PathPlace = (sbyte)cp.ReadByte(record + 0x12);
                ai.PathLength = cp.ReadByte(record + 0x13);
                ai.FlightLeft = (int)cp.ReadWord(record + 0x18);
            }

            var sheet = cp.ReadWord(p + 0x40);
            var stats = cp.ReadWord(sheet + 0x18);
            foreach (var stat in Enum.GetValues<Stat>())
            {
                var i = (uint)stat;
                SetStat(c.Sheet.Stats, stat, cp.ReadByte(stats + i), (sbyte)cp.ReadByte(stats + 7 + i), cp.ReadByte(stats + 0xE + i), (sbyte)cp.ReadByte(stats + 0x15 + i));
            }

            // A freed body has no pointer: a dead combatant's. The body of a live one stands where the checkpoint's does.
            var body = built.Body(slot);
            c.Freed = body == 0;
            if (body != 0)
            {
                var at = battle.Origin + Vector(cp, body + BodyPosition);
                c.Body.Sphere.Position = at;
                c.Body.Heading = c.Facing;
                // Its field (Body.SetGoal: the centre at +0x274, the radius at +0x280, with flag 4), or none.
                c.Body.Field = (Half(cp, body + 0x1E) & 4) != 0
                    ? (battle.Origin + Vector(cp, body + 0x274), Float(cp, body + 0x280))
                    : null;
                // The original's record reads its body only at times (Combatant.SyncFromBody), and bodies pushed since then stand a little off
                // their cells: the next read, which every reach test makes, brings the cell to the body. The port reads when the body is
                // not where it last read it, so a body off its cell is left as unread.
                var stale = battle.Origin + new Vector3(c.Cell.X, at.Y - battle.Origin.Y, c.Cell.Y);
                c.ReadFrom = Vector2.Distance(new Vector2(at.X - battle.Origin.X, at.Z - battle.Origin.Z), c.Cell) < 0.0001f ? at : stale;
            }
        }
        foreach (var c in battle.Combatants.Where(c => !c.Sheet.IsDead))
            battle.Grid.Occupy(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);

        // A cell off the collision is taken from the start, and freed with a combatant's footprint that once stood on it: a history the
        // overlay can't replay (08's cell (60,45), where Sholeh walked past). Free those the checkpoint has free and no one stands on.
        var rows = built.Struct + 0x3C;
        for (var x = 0; x < battle.Grid.Width; x++)
        {
            for (var z = 0; z < battle.Grid.Depth; z++)
            {
                if ((cp.ReadByte(rows + (uint)(x * ArenaGrid.Side + z)) & ArenaGrid.Taken) == 0 && (battle.Grid[x, z] & ArenaGrid.Taken) != 0
                    && !battle.Combatants.Any(c => !c.Sheet.IsDead && Covers(c, x, z)))
                {
                    battle.Grid.Vacate(x, z, 1);
                    built.Freed.Add((x, z));
                }
            }
        }

        var s = built.Struct;
        battle.ActiveCounts = (cp.ReadByte(s + 2), cp.ReadByte(s + 4), cp.ReadByte(s + 6));
        battle.FirstEnemyDied = cp.ReadWord(s + 8) != 0;
        battle.Order.Now = cp.ReadByte(s + 0x31);
        if (Enumerable.Range(0, Battle.SlotCount).Where(slot => built.Record(slot) == cp.ReadWord(s + 0x2C)).Cast<int?>().FirstOrDefault() is { } current)
            battle.Current = battle.Slots[current];
        battle.FocusAt(new Vector2(Float(cp, s + 0x18), Float(cp, s + 0x1C)));
        battle.NextReserve = cp.ReadByte(s + 7);
        // The leader's morale and whether it's gone (+0x11, +0x0C), and the quicksort's stream (Quicksort.Seed), which the AI reads.
        battle.LeaderMorale = cp.ReadByte(s + 0x11);
        Poke(battle, nameof(Battle.LeaderGone), cp.ReadWord(s + 0x0C) != 0);
        game.Random.Sort.Seed = cp.ReadWord(SortState);

        Assert.Equal(cp.ReadByte(s + 0x10), battle.LeaderSlot);
        var wrong = Verify(built);
        Assert.True(wrong.Count == 0, $"the overlay of checkpoint {checkpoint:00} differs from its RAM: {string.Join("; ", wrong)}");
        return built;
    }

    /// <summary>Sets a stat's four values (trained, current, base, modified) by what the sheet's mutators can do.</summary>
    public static void SetStat(CharacterStats stats, Stat stat, int trained, int current, int @base, int modified)
    {
        if (stats.Trained(stat) != trained)
            stats.Train(stat, trained - stats.Trained(stat));
        stats.ChangeCurrent(stat, current - stats.Current(stat));
        stats.ChangeBase(stat, @base - stats.RawBase(stat));
        stats.ChangeModified(stat, modified - stats.RawModified(stat));
    }

    /// <summary>
    /// What the overlaid battle has that its checkpoint hasn't, among what the overlay doesn't simply copy: the bodies' places and the cells
    /// they imply, the whole grid's taken bits, every sheet's stats and skills, its protection and its weapon, and the dead.
    /// </summary>
    public static List<string> Verify(Built built)
    {
        var (battle, cp) = (built.Battle, built.Cp);
        var wrong = new List<string>();
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var p = built.Record(slot);
            if (p == 0)
                continue;
            var c = built[slot];
            var who = $"slot {slot} ({c.Sheet.Name})";
            var sheet = cp.ReadWord(p + 0x40);
            var stats = cp.ReadWord(sheet + 0x18);
            var skills = cp.ReadWord(sheet + 0x1C);

            foreach (var stat in Enum.GetValues<Stat>())
            {
                var i = (uint)stat;
                var original = (cp.ReadByte(stats + i), (sbyte)cp.ReadByte(stats + 7 + i), cp.ReadByte(stats + 0xE + i), (sbyte)cp.ReadByte(stats + 0x15 + i));
                var port = ((byte)c.Sheet.Stats.Trained(stat), (sbyte)c.Sheet.Stats.Current(stat), (byte)c.Sheet.Stats.RawBase(stat), (sbyte)c.Sheet.Stats.RawModified(stat));
                if (original != port)
                    wrong.Add($"{who} {stat}: original {original}, port {port}");
            }
            foreach (var skill in SkillId.All)
            {
                var original = ((sbyte)cp.ReadByte(skills + (uint)skill.Slot), (sbyte)cp.ReadByte(skills + 0x18 + (uint)skill.Slot));
                var port = (c.Sheet.Skills.RawBase(skill), c.Sheet.Skills.RawModified(skill));
                if (original != port)
                    wrong.Add($"{who} skill {skill.Slot}: original {original}, port {port}");
            }
            if (cp.ReadWord(p + 0x44) is not 0 and var record && c.Ai is { } ai && (cp.ReadByte(record + 8), cp.ReadByte(record + 0x0A)) != (ai.Style, 0xFF))
                wrong.Add($"{who} AI style: original {cp.ReadByte(record + 8)}, port {ai.Style}");
            if (cp.ReadByte(cp.ReadWord(sheet + 0x14) + 2) != c.Sheet.Protection)
                wrong.Add($"{who} protection: original {cp.ReadByte(cp.ReadWord(sheet + 0x14) + 2)}, port {c.Sheet.Protection}");
            var weapon = cp.ReadWord(sheet + 0x24);
            if ((weapon == 0 ? null : (RecordId?)TestRoms.ItemId(Half(cp, weapon))) != c.Sheet.Equipment.Weapon?.Id)
                wrong.Add($"{who} weapon: original {(weapon == 0 ? "none" : Half(cp, weapon).ToString("X4"))}, port {c.Sheet.Equipment.Weapon?.Id}");
            if (c.Sheet.IsDead != (cp.ReadByte(stats + 0x15 + (uint)Stat.Endurance) == 0 && cp.ReadByte(stats + 0x15 + (uint)Stat.Stamina) == 0 && cp.ReadByte(stats + 0x15 + (uint)Stat.Level) == 0))
                wrong.Add($"{who} is dead: port {c.Sheet.IsDead}");

            // The body: where the checkpoint's is, in the arena chunk's coordinates; gone for the dead whose bodies were.
            if (built.Body(slot) != 0)
            {
                var at = c.Body.Sphere.Position - battle.Origin;
                if (Vector3.Distance(at, Vector(cp, built.Body(slot) + BodyPosition)) > 0.001f)
                    wrong.Add($"{who}: body at {at}, original {Vector(cp, built.Body(slot) + BodyPosition)}");
            }
            else if (!c.Freed)
            {
                wrong.Add($"{who}: the original's body is gone, the port's isn't");
            }
        }

        var grid = built.Struct + 0x3C;
        var cells = new List<string>();
        for (var x = 0; x < battle.Grid.Width; x++)
        {
            for (var z = 0; z < battle.Grid.Depth; z++)
            {
                var original = cp.ReadByte(grid + (uint)(x * ArenaGrid.Side + z));
                if (original != battle.Grid[x, z])
                    cells.Add($"({x},{z}) original {original:X2}, port {battle.Grid[x, z]:X2}");
            }
        }
        if (cells.Count > 0)
            wrong.Add($"{cells.Count} grid cells differ, e.g. {string.Join(", ", cells.Take(8))}");
        return wrong;
    }


    /// <summary>The arena as a zone engine, for ticking the battle.</summary>
    public static ZoneEngine Zone(Built built) => new(built.Game, built.Streamer) { Camera = built.Battle.Camera };
}
