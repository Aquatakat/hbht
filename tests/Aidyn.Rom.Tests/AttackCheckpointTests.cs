using System.Numerics;
using System.Reflection;
using Aidyn.Core;
using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Chunks;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Rig;

namespace Aidyn.Rom.Tests;

/// <summary>
/// Attacking in battle (M3 session 5) against the original's own results: the user's Combat checkpoints 05 (Alaron's turn, aiming at a
/// rat), 08 (two rats dead) and 10 (the reserves have joined) are laid over the battle <see cref="BattleCheckpointTests"/> builds from
/// checkpoint 01, and the port's chances, rolls, damage, logs, kill, target order and reserves are compared with what the original's
/// machine code gave when it was run on those checkpoints' RAM (docs/systems/combat.md "An attack": the oracle tables of the session's
/// research). The tests skip without the ROM and the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class AttackCheckpointTests(Na10Import fixture, ITestOutputHelper output)
{
    private const string Before = "Aidyn Chronicles - The First Mage (U) (V1.0) [!] - Combat 01, before.bs1";

    private const uint CombatPointer = 0x800E0BF0, RandomState = 0x800D8088, BodyPointers = 0x800D9488;

    /// <summary>Where a body keeps its position (x, y, z), in the arena chunk's own coordinates.</summary>
    private const uint BodyPosition = 0x68;

    /// <summary>The draws from checkpoint 01's stream to the ambush roll (see <see cref="BattleCheckpointTests"/>).</summary>
    private const int DrawsBeforeSetup = 293 + 1;

    private static readonly RecordId Rat = RecordId.Hex(RecordTypes.Entity, 0x026E);

    private PluginSet Set => fixture.Data.Set;

    // ---------------------------------------------------------------------------------------------------------------------------
    // The checkpoints, and a battle with one laid over it

    private static AresCheckpoint Combat(int number)
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

    private static ushort Half(AresCheckpoint c, uint address) => (ushort)(c.ReadByte(address) << 8 | c.ReadByte(address + 1));

    private static float Float(AresCheckpoint c, uint address) => BitConverter.UInt32BitsToSingle(c.ReadWord(address));

    private static Vector3 Vector(AresCheckpoint c, uint address) => new(Float(c, address), Float(c, address + 4), Float(c, address + 8));

    /// <summary>Sets a property with a private setter (a battle's state the tests lay over).</summary>
    private static void Poke(object target, string property, object? value) =>
        target.GetType().GetProperty(property, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.SetValue(target, value);

    /// <summary>Whether a combatant's footprint covers a cell.</summary>
    private static bool Covers(Combatant c, int x, int z)
    {
        var (cx, cz) = (ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y));
        return x >= cx && x < cx + c.Footprint && z >= cz && z < cz + c.Footprint;
    }

    private sealed record Built(Battle Battle, GameState Game, ChunkStreamer Streamer, AresCheckpoint Cp)
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
    private Built Plain(int checkpoint)
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
    private Built At(int checkpoint)
    {
        var built = Plain(checkpoint);
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
        battle.Focus = new Vector2(Float(cp, s + 0x18), Float(cp, s + 0x1C));
        battle.NextReserve = cp.ReadByte(s + 7);

        var wrong = Verify(built);
        Assert.True(wrong.Count == 0, $"the overlay of checkpoint {checkpoint:00} differs from its RAM: {string.Join("; ", wrong)}");
        return built;
    }

    /// <summary>Sets a stat's four values (trained, current, base, modified) by what the sheet's mutators can do.</summary>
    private static void SetStat(CharacterStats stats, Stat stat, int trained, int current, int @base, int modified)
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
    private List<string> Verify(Built built)
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

    // ---------------------------------------------------------------------------------------------------------------------------
    // The overlay itself

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(10)]
    public void TheOverlayMatchesTheCheckpointsCellsFacingsStatsAndGrid(int checkpoint)
    {
        // At() verified everything but what it copies; check that, and a few of the facts the tests lean on.
        var built = At(checkpoint);
        var battle = built.Battle;
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var p = built.Record(slot);
            if (p == 0)
                continue;
            Assert.Equal(new Vector2(Float(built.Cp, p), Float(built.Cp, p + 4)), built[slot].Cell);
            Assert.Equal(new Vector2(Float(built.Cp, p + 0x18), Float(built.Cp, p + 0x1C)), built[slot].Facing);
            // The body's heading is the combatant's facing, and the original's own field the negation of it.
            var body = built.Body(slot);
            if (body != 0)
            {
                var field = new Vector2(Float(built.Cp, body + 0x20), Float(built.Cp, body + 0x24));
                Assert.True(Vector2.Distance(-field, built[slot].Body.Heading) < 0.001f, $"slot {slot}: body field {field}, heading {built[slot].Body.Heading}");
            }
        }
        Assert.Equal(built.Cp.ReadByte(built.Struct + 0x31), battle.Order.Now);
        Assert.Equal(built.Record(battle.Current!.Slot), built.Cp.ReadWord(built.Struct + 0x2C));
        Assert.Equal(PartOfDay.Midday, battle.PartOfDay);
        // The grid needed nothing replayed but 08's one hole: the cells the overlay had to free are only those a walked-over hole left.
        output.WriteLine($"cells freed by the overlay at {checkpoint:00}: {string.Join(" ", built.Freed)}");
        Assert.Equal(checkpoint == 8 ? [(60, 45)] : [], built.Freed);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Chances

    /// <summary>The melee chances of checkpoint 05 (the original's code run on its RAM): rows are the attacker's slot, columns the target's.</summary>
    private static readonly int[][] MeleeChances =
    [
        [86, 101, 90, 73, 150, 158, 142, 158],
        [38, 53, 48, 31, 105, 111, 100, 111],
        [198, 216, 193, 177, 270, 282, 257, 282],
        [88, 99, 87, 78, 130, 136, 124, 136],
        [5, 5, 5, 5, 20, 21, 18, 21],
        [5, 5, 5, 5, 18, 20, 18, 20],
        [5, 5, 5, 5, 21, 21, 20, 21],
        [5, 5, 5, 5, 18, 20, 18, 20],
    ];

    [Fact]
    public void EveryMeleeChanceAtCheckpoint05IsTheOriginals()
    {
        var built = At(5);
        var wrong = new List<string>();
        for (var a = 0; a < 8; a++)
        {
            for (var t = 0; t < 8; t++)
            {
                if (a == t)
                    continue;
                var port = built.Battle.MeleeChance(built[a], built[t], behind: false);
                if (port != MeleeChances[a][t])
                    wrong.Add($"{built[a].Sheet.Name}({a}) -> {built[t].Sheet.Name}({t}): original {MeleeChances[a][t]}, port {port}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    /// <summary>Faces the attacker to the target as <c>Combatant.FaceCombatant</c> does: its facing is the way to it.</summary>
    private static void FaceCombatant(Combatant attacker, Combatant target) => attacker.Facing = Vector2.Normalize(target.Cell - attacker.Cell);

    [Fact]
    public void AlaronBehindARatHasTheFlankBonusAndTheFacingThatFindsIt()
    {
        var built = At(5);
        var (alaron, rat) = (built[0], built[7]);
        Assert.Equal(158, built.Battle.MeleeChance(alaron, rat, behind: false));
        Assert.Equal(183, built.Battle.MeleeChance(alaron, rat, behind: true));

        // As the oracle faced him: toward the rat, which faces away (up and right, the rat's own facing (0.71, -0.71)).
        Assert.False(Battle.IsBehind(alaron, rat), "Alaron's checkpoint facing isn't straight at the rat");
        FaceCombatant(alaron, rat);
        Assert.True(Battle.IsBehind(alaron, rat));
        // The rat faces him from the front side: not behind the other way round.
        FaceCombatant(rat, alaron);
        Assert.False(Battle.IsBehind(rat, alaron));
    }

    [Fact]
    public void ARatsMeleeChancesAtCheckpoint10AreTheOriginals()
    {
        // Row 4 of checkpoint 10's table: rat 4 at the party and the other rats, the reserves far off.
        var built = At(10);
        var row = Enumerable.Range(0, 8).Select(t => t == 4 ? 20 : built.Battle.MeleeChance(built[4], built[t], behind: false)).ToList();
        Assert.Equal([5, 5, 5, 5, 20, 16, 16, 16], row);
    }

    [Fact]
    public void OnlyAlaronAndRat7CanAttackEachOtherAtCheckpoint05()
    {
        var built = At(5);
        var wrong = new List<string>();
        for (var a = 0; a < 8; a++)
        {
            for (var t = 0; t < 8; t++)
            {
                if (a == t)
                    continue;
                var expected = (a, t) is (0, 7) or (7, 0);
                if (built.Battle.CanAttack(built[a], built[t]) != expected)
                    wrong.Add($"{a} -> {t}: original {expected}");
            }
        }
        Assert.True(wrong.Count == 0, string.Join("; ", wrong));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Melee rolls

    /// <summary>One row of the oracle: the seed, the d100 roll, the roll after Warrior's second draw if there was one, the damage, the stream after.</summary>
    public sealed record Row(uint Seed, int Roll, int? Second, int Damage, uint End);

    private void MeleeRow(int attacker, int target, Row row, int chance, bool behind, string shown, string queued, bool critical = false)
    {
        var built = At(5);
        var battle = built.Battle;
        var (a, t) = (built[attacker], built[target]);
        FaceCombatant(a, t);
        built.Game.Random.Combat.Seed = row.Seed;
        var skills = SkillId.All.Select(s => (a.Sheet.Skills.RawBase(s), a.Sheet.Skills.RawModified(s))).ToList();

        var outcome = battle.ResolveMelee(a, t);

        var hit = row.Damage > 0;
        Assert.Equal(chance, outcome.Chance);
        Assert.Equal(behind, outcome.Behind);
        Assert.Equal(row.Roll, outcome.Roll);
        Assert.Equal(row.Second is { } second ? row.Roll - second : row.Roll, outcome.Adjusted);
        Assert.Equal(hit, outcome.Hit);
        Assert.Equal(critical, outcome.Critical);
        Assert.Equal(row.Damage, outcome.Damage);
        Assert.Equal(row.End, built.Game.Random.Combat.Seed);
        Assert.Equal(shown, battle.Lines.Shown);
        Assert.Equal(queued, battle.Lines.Queued);
        // No row's experience roll was low enough to train anything.
        Assert.Equal(skills, SkillId.All.Select(s => (a.Sheet.Skills.RawBase(s), a.Sheet.Skills.RawModified(s))));
    }

    [Theory]
    [InlineData(0x00000001u, 24, 49, 0x8E625FC9u)]
    [InlineData(0x00000002u, 24, 46, 0x78EB5FEAu)]
    [InlineData(0x415A0000u, 42, 57, 0x10735FA8u)]
    [InlineData(0xF3BCEC1Fu, 95, 46, 0xC1CACFA7u)]
    public void AlaronsSwordHitsRat7FromBehindForTheOriginalsDamageAndLeavesTheOriginalsStream(uint seed, int roll, int damage, uint end) =>
        MeleeRow(0, 7, new Row(seed, roll, null, damage, end), chance: 183, behind: true,
            shown: "Alaron attacks Giant Rat", queued: $"Giant Rat loses {damage} hit points");

    [Fact]
    public void AlaronHitsSholehForFortyWithNoCritical() =>
        MeleeRow(0, 3, new Row(0xdb5586aeu, 61, null, 40, 0x08FDBC16u), chance: 73, behind: false,
            shown: "Alaron attacks Sholeh", queued: "Sholeh loses 40 hit points");

    [Fact]
    public void AlaronMissesSholehAndShesAvoidedTheAttack() =>
        MeleeRow(0, 3, new Row(0x5457da22u, 83, null, 0, 0x4D2361A4u), chance: 73, behind: false,
            shown: "Alaron attacks Sholeh", queued: "Sholeh avoids the attack");

    [Fact]
    public void AlaronsLowRollIsTurnedByWarriorIntoACriticalHit() =>
        MeleeRow(0, 3, new Row(0xe02e3377u, 6, 83, 45, 0x557AA752u), chance: 73, behind: false,
            shown: "Alaron scores a critical hit on Sholeh", queued: "Sholeh loses 45 hit points", critical: true);

    [Fact]
    public void RhedaMissesSholeh() =>
        MeleeRow(1, 3, new Row(0xdb5586aeu, 61, null, 0, 0xE73C9A10u), chance: 31, behind: false,
            shown: "Rheda attacks Sholeh", queued: "Sholeh avoids the attack");

    [Fact]
    public void RhedaHitsSholehForFifteen() =>
        MeleeRow(1, 3, new Row(0xc8764d7eu, 12, null, 15, 0xCF07D294u), chance: 31, behind: false,
            shown: "Rheda attacks Sholeh", queued: "Sholeh loses 15 hit points");

    // ---------------------------------------------------------------------------------------------------------------------------
    // Ranged

    /// <summary>
    /// Sholeh with a Long Bow patched into her weapon, as the oracle did it (the item struct's fields, in place): class Missile, hit 30,
    /// damage 4, range 255, animation 5, element Cutting; her action ranged.
    /// </summary>
    private static void GiveLongBow(Combatant sholeh)
    {
        var weapon = sholeh.Sheet.Equipment.Weapon!.Item.Weapon! with
        {
            Class = WeaponClass.Missile,
            Hit = 30,
            Damage = 4,
            Range = 255,
            Animation = 5,
            DamageType = Element.Cutting,
        };
        sholeh.Sheet.Equipment.Restore(new ItemInstance(Item.From(weapon)));
        sholeh.Action = CombatAction.Ranged;
    }

    [Fact]
    public void SholehsLongBowsChancesAtTheRatsAreTheOriginals()
    {
        var built = At(5);
        var sholeh = built[3];
        GiveLongBow(sholeh);

        Assert.Equal([92, 96, 91, 100], Enumerable.Range(4, 4).Select(t => built.Battle.RangedChance(sholeh, built[t], behind: false)));
        Assert.Equal([111, 117, 109, 120], Enumerable.Range(4, 4).Select(t => built.Battle.RangedChance(sholeh, built[t], behind: true)));
    }

    [Fact]
    public void SholehsLongBowReachesRats4And6And7ButNotRat5()
    {
        // Rat 5 is behind another body: the grid walk is blocked and the 3D line of sight meets the rat between them.
        var built = At(5);
        GiveLongBow(built[3]);
        Assert.Equal([true, false, true, true], Enumerable.Range(4, 4).Select(t => built.Battle.CanAttack(built[3], built[t])));
    }

    private void RangedRow(Row row, int chance, string shown, string queued, bool critical = false)
    {
        var built = At(5);
        var battle = built.Battle;
        var (sholeh, rat) = (built[3], built[7]);
        GiveLongBow(sholeh);
        FaceCombatant(sholeh, rat);
        built.Game.Random.Combat.Seed = row.Seed;

        var outcome = battle.ResolveRanged(sholeh, rat);

        Assert.Equal((chance, false, row.Roll), (outcome.Chance, outcome.Behind, outcome.Roll));
        Assert.Equal(row.Second is { } second ? row.Roll - second : row.Roll, outcome.Adjusted);
        Assert.Equal(row.Damage > 0, outcome.Hit);
        Assert.Equal(critical, outcome.Critical);
        Assert.Equal(row.Damage, outcome.Damage);
        Assert.Equal(row.End, built.Game.Random.Combat.Seed);
        Assert.Equal(shown, battle.Lines.Shown);
        Assert.Equal(queued, battle.Lines.Queued);
    }

    [Fact]
    public void SholehsArrowHitsRat7ForThirtyNine() =>
        RangedRow(new Row(0x52e6b438u, 97, null, 39, 0x86DDB2FEu), 100, "Sholeh attacks Giant Rat", "Giant Rat loses 39 hit points");

    [Fact]
    public void SholehsLowRollIsACriticalHitForFortyOne() =>
        RangedRow(new Row(0x1818e811u, 5, 43, 41, 0xEC197BE2u), 100, "Sholeh scores a critical hit on Giant Rat", "Giant Rat loses 41 hit points", critical: true);

    [Fact]
    public void SholehsArrowMissesRat7() =>
        RangedRow(new Row(0x3e7d1bfbu, 100, null, 0, 0x29DDD5E5u), 100, "Sholeh attacks Giant Rat", "Sholeh misses Giant Rat");

    // ---------------------------------------------------------------------------------------------------------------------------
    // The kill

    [Fact]
    public void AlaronsBlowKillsRat7AtOnceAsTheOriginalsExecuteActionDoes()
    {
        var built = At(5);
        var battle = built.Battle;
        var (alaron, rat) = (built[0], built[7]);
        Assert.Same(alaron, battle.Current);
        FaceCombatant(alaron, rat);
        built.Game.Random.Combat.Seed = 1;
        var grid = rat.Cell;
        Assert.Equal(ArenaGrid.Taken, battle.Grid[(int)grid.X, (int)grid.Y] & ArenaGrid.Taken);

        battle.ExecuteAction(alaron, rat);

        Assert.Equal((CombatState.Action, CombatState.Action), (battle.State, battle.NextState));
        Assert.Equal((7, 4, 3), battle.ActiveCounts);
        Assert.True(battle.FirstEnemyDied);
        Assert.Equal(49, rat.Damage);
        Assert.Equal((1, 7), (alaron.Moves, alaron.LastTarget));
        Assert.Equal("Alaron attacks Giant Rat", battle.Lines.Shown);
        Assert.Equal("Giant Rat killed", battle.Lines.Queued);
        Assert.Equal(12, alaron.Body.Request);
        Assert.True(battle.ActionActive);
        Assert.Equal(0x8E625FC9u, built.Game.Random.Combat.Seed);
        // Its cells at once: the footprint of 2 x 2 from its cell on.
        for (var x = 0; x < rat.Footprint; x++)
        {
            for (var z = 0; z < rat.Footprint; z++)
                Assert.Equal(0, battle.Grid[(int)grid.X + x, (int)grid.Y + z] & ArenaGrid.Taken);
        }
        // The hit points wait for the swing: the rat is alive until the blow lands.
        Assert.Equal(31, rat.Sheet.Hp);
    }

    /// <summary>The arena as a zone engine, for ticking the battle.</summary>
    private static Aidyn.Core.Chunks.ZoneEngine Zone(Built built) =>
        new(built.Game, built.Streamer) { Camera = built.Battle.Camera };

    [Fact]
    public void TheKillPlaysOutThroughTheHitTheFallAndTheEndOfTheTurn()
    {
        var built = At(5);
        var battle = built.Battle;
        var (alaron, rat) = (built[0], built[7]);
        var zone = Zone(built);

        // A in his turn: the cursor on the one rat in reach, the facing, then the blow.
        Poke(battle, nameof(Battle.State), CombatState.Turn);
        for (var i = 0; i < 120 && battle.Fade.Fading; i++)
            battle.Fade.Step();
        Assert.True(battle.Attack());
        Assert.Equal(CombatState.TargetAttack, battle.State);
        Assert.Equal([7], battle.TargetsA.SelectedSlots);
        Assert.True(battle.Confirm());
        Assert.Equal((CombatState.Facing, 7), (battle.State, battle.Target));
        built.Game.Random.Combat.Seed = 1;

        // Each state's ticks, the hit points when the blow lands (state 4), and what the log and the numbers say then.
        var ticks = new List<(CombatState State, int Ticks)>();
        var landed = false;
        for (var i = 0; i < 600 && battle.Current == alaron; i++)
        {
            battle.Tick(zone, false, _ => null);
            if (ticks.Count == 0 || ticks[^1].State != battle.State)
                ticks.Add((battle.State, 0));
            ticks[^1] = (battle.State, ticks[^1].Ticks + 1);
            if (battle.State == CombatState.Action)
            {
                // Struck, killed and counted already; the hit points wait for the swing to end.
                Assert.Equal(31, rat.Sheet.Hp);
                Assert.Equal(49, rat.Damage);
            }
            if (battle.State == CombatState.Result && !landed)
            {
                landed = true;
                Assert.Equal(0, rat.Sheet.Hp);
                Assert.Equal(0, rat.Damage);
                Assert.Equal("Giant Rat killed", battle.Lines.Shown);
                Assert.Equal(49, Assert.Single(battle.Numbers.Shown).Amount);
                Assert.Equal(PortraitState.Enemy, PartyOrEnemy(battle, 7));
                Assert.False(rat.Freed);
            }
        }
        output.WriteLine($"states and their ticks: {string.Join(", ", ticks.Select(t => $"{t.State} {t.Ticks}"))}");

        Assert.True(landed);
        Assert.Equal([CombatState.Facing, CombatState.Action, CombatState.Result], ticks.Take(3).Select(t => t.State));
        Assert.NotSame(alaron, battle.Current);
        Assert.Equal(0, rat.Sheet.Hp);
        Assert.True(rat.Freed);
        Assert.Equal(PortraitState.Fallen, PartyOrEnemy(battle, 7));
        Assert.Equal((7, 4, 3), battle.ActiveCounts);
        Assert.DoesNotContain(battle.Combatants, c => c.Slot == 7 && !c.Freed);
    }

    [Fact]
    public void KillingTheLastEnemyWithNoReserveLeftWinsTheBattleAsTheTurnChanges()
    {
        var built = At(5);
        var battle = built.Battle;
        var alaron = built[0];
        var zone = Zone(built);

        // Rats 4-6 already dead and the list run out: rat 7 is the last enemy.
        foreach (var slot in (int[])[4, 5, 6])
        {
            built[slot].Sheet.TakeDamage(1000, built[slot]);
            battle.Death(built[slot]);
            battle.FreeBody(built[slot]);
        }
        battle.NextReserve = battle.Enemies.Count;
        Poke(battle, nameof(Battle.State), CombatState.Turn);
        for (var i = 0; i < 120 && battle.Fade.Fading; i++)
            battle.Fade.Step();
        Assert.True(battle.Attack());
        Assert.True(battle.Confirm());
        built.Game.Random.Combat.Seed = 1;

        // The kill doesn't end the battle: the turn's end does (TurnOrder.CheckEnd), with the victory line and the fade out.
        for (var i = 0; i < 600 && battle.State != CombatState.Victory; i++)
        {
            Assert.False(battle.Over);
            battle.Tick(zone, false, _ => null);
        }
        Assert.Equal(CombatState.Victory, battle.State);
        Assert.Equal(battle.CombatText(Battle.Victorious), battle.Lines.Shown);
        Assert.Equal(0, battle.ActiveCounts.Enemies);
        Assert.True(battle.Fade.Fading);

        // Until session 7's windows, the battle leaves once it has faded out.
        for (var i = 0; i < 600 && !battle.Over; i++)
            battle.Tick(zone, false, _ => null);
        Assert.True(battle.Over);
        Assert.False(battle.Fled);
        Assert.False(alaron.Sheet.IsDead);
    }

    private static PortraitState PartyOrEnemy(Battle battle, int slot) => battle.Gui.Portraits.Single(p => p.Slot == slot).State;

    // ---------------------------------------------------------------------------------------------------------------------------
    // Reserves

    [Fact]
    public void TheReservesFillSlots6And7WhereCheckpoint09HasThem()
    {
        var built = At(8);
        var battle = built.Battle;
        var c09 = Combat(9);
        var s09 = c09.ReadWord(CombatPointer);
        Assert.Equal(4, battle.NextReserve);
        Assert.True(built[6].Sheet.IsDead && built[7].Sheet.IsDead);
        Assert.False(built[4].Sheet.IsDead || built[5].Sheet.IsDead);

        built.Game.Random.Combat.Seed = 0x12345;
        battle.SpawnReserves();

        var expected = new Lcg(0x12345);
        expected.Next();
        expected.Next();
        Assert.Equal(expected.Seed, built.Game.Random.Combat.Seed);
        Assert.Equal(6, battle.NextReserve);
        Assert.Equal([new Vector2(12, 43), new Vector2(13, 52)], [built[6].Cell, built[7].Cell]);
        Assert.Equal([1, 2], [built[6].Direction, built[7].Direction]);
        foreach (var slot in new[] { 6, 7 })
        {
            var c = built[slot];
            Assert.Equal((CombatantFlags)0x432, c.Flags);
            Assert.Equal((6, 2, -1), (c.Radius, c.Footprint, c.LastTarget));
            Assert.False(c.Sheet.IsDead);
            Assert.Equal(31, c.Sheet.Hp);
            Assert.Equal(Rat, c.Entity);
            Assert.False(c.Freed);

            // Checkpoint 09 is their first turn on: each begins it where it was made (its turn start), the newcomer's flag has gone and the
            // moved flag is up from its walk, and the direction has followed the walk, so only the place and the flags are comparable.
            var p = c09.ReadWord(s09 + 0x52D0 + (uint)slot * 4);
            output.WriteLine($"09 slot {slot}: cell ({Float(c09, p)},{Float(c09, p + 4)}) start ({Float(c09, p + 8)},{Float(c09, p + 12)}) dir {c09.ReadByte(p + 0x14)} flags {Half(c09, p + 0x2C):X}; port {c.Cell} dir {c.Direction}");
            if (slot == 6 || new Vector2(Float(c09, p + 8), Float(c09, p + 12)) == c.Cell)
                Assert.Equal(new Vector2(Float(c09, p + 8), Float(c09, p + 12)), c.Cell);
            Assert.Equal(CombatantFlags.Newcomer | (CombatantFlags)(Half(c09, p + 0x2C) & ~(int)CombatantFlags.Moved), c.Flags);
            // And the grid takes the footprint where it was made.
            Assert.Equal(ArenaGrid.Taken, battle.Grid[(int)c.Cell.X, (int)c.Cell.Y] & ArenaGrid.Taken);
        }
        Assert.Equal(["Giant Rat", "Giant Rat"], [built[6].Sheet.Name, built[7].Sheet.Name]);
        Assert.False(battle.LeaderGone);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The target set

    /// <summary>Puts a combatant at a place in the arena, its grid cells moving with it.</summary>
    private static void Place(Battle battle, Combatant c, float x, float z)
    {
        battle.Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Cell = new Vector2(x, z);
        var at = battle.Origin + new Vector3(x, c.Body.Sphere.Position.Y - battle.Origin.Y, z);
        c.Body.Sphere.Position = at;
        c.ReadFrom = at;
        battle.Grid.Occupy(ArenaGrid.CellOf(x), ArenaGrid.CellOf(z), c.Footprint);
    }

    private static readonly float[] Angles = [0.3f, 1.7f, 3.0f, 4.4f];

    /// <summary>Checkpoint 05 with Alaron at (60, 55) and rats 4 to 7 1.9 away from him at the oracle's angles, Alaron's body heading (the original's field) at <paramref name="heading"/>.</summary>
    private Built Surrounded(float heading)
    {
        var built = At(5);
        Place(built.Battle, built[0], 60, 55);
        for (var i = 0; i < 4; i++)
            Place(built.Battle, built[4 + i], 60 + 1.9f * MathF.Cos(Angles[i]), 55 + 1.9f * MathF.Sin(Angles[i]));
        // The original's body heading field is (cos h, sin h); the port's heading is the negation of it.
        built[0].Body.Heading = new Vector2(-MathF.Cos(heading), -MathF.Sin(heading));
        return built;
    }

    [Fact]
    public void FourRatsAroundAlaronAreOrderedByAngleAndTheCursorStartsWhereTheOriginalsDoes()
    {
        var built = Surrounded(0);
        var (battle, alaron) = (built.Battle, built[0]);
        Assert.Equal([4, 5, 6, 7], Enumerable.Range(4, 4).Where(t => battle.CanAttack(alaron, built[t])));

        // The cursor set A already had: on rat 7, as checkpoint 05's has it.
        battle.TargetsA.Mark(7, selected: true);
        battle.TargetsA.Rebuild(battle, alaron);
        Assert.Equal(7, battle.TargetsA.Cursor);
        Assert.True(battle.TargetsA.BeginAttack(battle, alaron));
        Assert.Equal([4, 6, 5, 7], battle.TargetsA.Order);
        Assert.Equal((7, 3, 1), (battle.TargetsA.Cursor, battle.TargetsA.Place, battle.TargetsA.Selected));

        // The cursor forced to 0, which is no candidate, and no last target: the lowest slot, rat 4.
        built = Surrounded(0);
        (battle, alaron) = (built.Battle, built[0]);
        battle.TargetsA.Mark(0, selected: true);
        battle.TargetsA.Rebuild(battle, alaron);
        Assert.Equal(4, battle.TargetsA.Cursor);
        Assert.True(battle.TargetsA.BeginAttack(battle, alaron));
        Assert.Equal([4, 6, 5, 7], battle.TargetsA.Order);
        Assert.Equal(4, battle.TargetsA.Cursor);

        // Heading 3.0: another order, and the cursor still 4, at its place 3.
        built = Surrounded(3.0f);
        (battle, alaron) = (built.Battle, built[0]);
        battle.TargetsA.Mark(0, selected: true);
        battle.TargetsA.Rebuild(battle, alaron);
        Assert.True(battle.TargetsA.BeginAttack(battle, alaron));
        Assert.Equal([5, 6, 7, 4], battle.TargetsA.Order);
        Assert.Equal((4, 3), (battle.TargetsA.Cursor, battle.TargetsA.Place));

        // The last target, 6, is where it starts.
        alaron.LastTarget = 6;
        battle.TargetsA.Mark(0, selected: true);
        battle.TargetsA.Rebuild(battle, alaron);
        Assert.True(battle.TargetsA.BeginAttack(battle, alaron));
        Assert.Equal(6, battle.TargetsA.Cursor);
    }
}
