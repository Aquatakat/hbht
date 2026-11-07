using System.Collections.Concurrent;
using System.Numerics;
using System.Reflection;
using HBHT.Core;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Magic;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

namespace HBHT.Rom.Tests;

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

    /// <summary>The ring's pool (<c>CombatMarkers.Pool</c>): 20 of 0x1C bytes, each its position at +4 and its flag at +0x18.</summary>
    public const uint RingPool = 0x800E3138;

    /// <summary>A checkpoint's ring: the positions of the teardrops it draws, in the arena chunk's coordinates.</summary>
    public static List<Vector3> Ring(AresCheckpoint c)
    {
        var pool = c.ReadWord(RingPool);
        return [.. Enumerable.Range(0, CombatMarkers.Pool).Select(i => pool + (uint)i * 0x1C).Where(m => c.ReadByte(m + 0x18) != 0).Select(m => Vector(c, m + 4))];
    }

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
    public static Built Plain(PluginSet Set, int checkpoint, ICombatHost? host = null)
    {
        var before = TestRoms.Checkpoint(Before);
        var cp = Combat(checkpoint);
        var game = GameState.Load(Set, CheckpointSave.Convert(before, Set, "Combat 01, before"));
        Assert.Empty(game.Problems);
        var arena = Arena.For(Set, Battlefield.Chunk(3))!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), Set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);
        var setup = new BattleSetup(BattleSource.Wanderer, null, null, null, [.. Enumerable.Repeat(new PartyEnemy(Rat, 1, 1), 10)],
            Arena: Battlefield.Chunk(3), Ambush: true, CanFlee: true, EncounterFlag: null, CollisionByte: 0, AniByte: 2);
        var random = new Lcg(before.ReadWord(RandomState));
        for (var i = 0; i < DrawsBeforeSetup; i++)
            random.Next();
        game.Random.Combat.Seed = random.Seed;
        return new Built(Battle.Begin(game, setup, arena, streamer.World, host), game, streamer, cp);
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // A parity log's battle (tools/parity.ps1), built from the RAM at its Combat.Setup

    private const uint SetupBlock = 0x800D9456, EnemyList = 0x800D9458, EncounterFlag = 0x800D9472, CollisionByte = 0x800D947E,
        ArenaByte = 0x800D947F, AniByte = 0x800D9480, FleeByte = 0x800D9481;

    /// <summary>
    /// A logged battle as the port begins it from the RAM at its <c>Combat.Setup</c>: the game the RAM holds, and the battle its setup
    /// block (combat.md's In and out) asks for. <paramref name="beforeBegin"/> runs on the game before <see cref="Battle.Begin"/> draws
    /// (<see cref="ParityReplayTests"/> feeds the original's draws from there). The built's RAM is the setup's.
    /// </summary>
    public static Built FromSetup(PluginSet set, ParityLog log, string label, Action<GameState>? beforeBegin = null, ICombatHost? host = null)
    {
        var ram = SetupRam(log);
        Assert.Equal(1, ram.ReadByte(SetupBlock));

        var game = GameState.Load(set, CheckpointSave.Convert(ram, set, label));
        Assert.Empty(game.Problems);

        var enemies = Enumerable.Range(0, Battle.MaxEnemies).Select(i => Half(ram, EnemyList + (uint)i * 2)).TakeWhile(e => e != 0)
            .Select(e => new PartyEnemy(RecordId.Hex(RecordTypes.Entity, e), 1, 1)).ToList();
        var ambush = ram.ReadByte(CollisionByte) == 2;
        var flag = Half(ram, EncounterFlag);
        var setup = new BattleSetup(ambush ? BattleSource.Wanderer : BattleSource.Encounter, null, null, null, enemies,
            Arena: Battlefield.Chunk(ram.ReadByte(ArenaByte)), Ambush: ambush, CanFlee: ram.ReadByte(FleeByte) != 0,
            EncounterFlag: flag == 0 ? null : RecordId.Decimal(RecordTypes.Flag, flag), CollisionByte: ram.ReadByte(CollisionByte),
            AniByte: ram.ReadByte(AniByte));

        var arena = Arena.For(set, setup.Arena!.Value)!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(set, game, new CollisionWorld(), set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);
        beforeBegin?.Invoke(game);
        game.Random.Sort.Seed = ram.ReadWord(SortState);
        return new Built(Battle.Begin(game, setup, arena, streamer.World, host), game, streamer, ram);
    }

    /// <summary>
    /// A parity log, read once and shared: the logs and the RAM they dumped are read-only, and the formula vectors and the replays
    /// build a battle from one for each vector, action and turn, which re-reading each time made the suite's slowest part.
    /// </summary>
    public static ParityLog Log(string folder) =>
        Logs.GetOrAdd(Path.GetFullPath(folder), f => new Lazy<ParityLog>(() => ParityLog.Read(f))).Value;

    /// <summary>The RAM a log dumped at its <c>Combat.Setup</c>, read once and shared, as <see cref="Log"/>.</summary>
    public static AresCheckpoint SetupRam(ParityLog log) =>
        SetupRams.GetOrAdd(Path.GetFullPath(log.Folder), _ => new Lazy<AresCheckpoint>(() => log.Ram(log.Entries.First(e => e.Name == "Combat.Setup")))).Value;

    private static readonly ConcurrentDictionary<string, Lazy<ParityLog>> Logs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, Lazy<AresCheckpoint>> SetupRams = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A logged battle with the RAM an entry point dumped laid over it (<paramref name="dump"/>, its file in the log's folder): built
    /// from its setup (<see cref="FromSetup"/>), the reserves that had joined by then brought in (<see cref="Join"/>), then
    /// <see cref="Overlay"/>.
    /// </summary>
    public static Built FromLog(PluginSet set, ParityLog log, string dump)
    {
        var entry = log.Entries.FirstOrDefault(e => e.Dump == dump) ?? throw new ArgumentException($"{log.Folder} dumped no {dump}.");
        var built = FromSetup(set, log, $"parity {log.Folder}") with { Cp = log.Ram(entry) };
        Join(built);
        return Overlay(built);
    }

    /// <summary>
    /// The reserves in the original's RAM that the battle built afresh hasn't: each enemy slot where the original has a living
    /// combatant and the port none, or a dead or gone one, takes the original's entity as <c>Combat.SpawnReserves</c> brings it in
    /// (<see cref="Battle.SpawnReserves"/>), at the cell and direction the RAM has, which the overlay then lays over it. The slots.
    /// </summary>
    public static List<int> Join(Built built)
    {
        var (fight, game, ram) = (built.Battle, built.Game, built.Cp);
        var joined = new List<int>();
        for (var slot = fight.PartySize; slot < Battle.SlotCount; slot++)
        {
            var p = built.Record(slot);
            if (p == 0)
                continue;
            var sheet = ram.ReadWord(p + 0x40);
            var stats = ram.ReadWord(sheet + 0x18);
            var dead = Enum.GetValues<Stat>().Where(s => s is Stat.Endurance or Stat.Stamina or Stat.Level)
                .All(s => ram.ReadByte(stats + 0x15 + (uint)s) == 0);
            var gone = (Half(ram, p + 0x2C) & (ushort)CombatantFlags.Gone) != 0;
            if (fight.Slots[slot] is { } c && !(c.Sheet.IsDead || c.Gone) || dead || gone)
                continue;
            var entity = RecordId.Hex(RecordTypes.Entity, Half(ram, sheet));
            var recruit = game.Recruit(entity);
            Assert.NotNull(recruit);
            var spare = Battle.ArmFromList(recruit, game.Items);
            var cell = new Vector2(Float(ram, p), Float(ram, p + 4));
            var newcomer = (Combatant)Call(fight, "Make", slot, recruit, true, false, cell, (int)ram.ReadByte(p + 0x14))!;
            Call(fight, "GiveAi", newcomer, spare);
            newcomer.UpdateRadius(fight);
            newcomer.PlaceField(fight);
            fight.Gui.Assign(entity, slot, enemy: true);
            joined.Add(slot);
        }
        return joined;
    }

    private static object? Call(object target, string method, params object?[] args) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(target, args);

    /// <summary>
    /// Checkpoint 01's battle with checkpoint <paramref name="checkpoint"/>'s state laid over it: the day, then each combatant's cell, turn
    /// start, last target, direction, facing, moves, action, flags, field radius, stats, and its body's place and heading, who is dead
    /// (their bodies gone), the grid's taken cells, and the battle's counts, turn and next reserve. What the checkpoint can be checked
    /// against that the overlay doesn't copy, it checks (<see cref="Verify"/>).
    /// </summary>
    public static Built At(PluginSet Set, int checkpoint)
    {
        var built = Overlay(Plain(Set, checkpoint));
        var wrong = Verify(built);
        Assert.True(wrong.Count == 0, $"the overlay of checkpoint {checkpoint:00} differs from its RAM: {string.Join("; ", wrong)}");
        return built;
    }

    /// <summary>
    /// Lays the RAM of <paramref name="built"/>'s <see cref="Built.Cp"/> over its battle, as <see cref="At"/> describes, for a checkpoint's
    /// or a parity log's (<see cref="ParityReplayTests"/>). The original's combatants must be the battle's, slot for slot.
    /// </summary>
    public static Built Overlay(Built built)
    {
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
            c.Mirror = cp.ReadByte(p + 0x28);
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
            // The weapon (+0x24), which the Party tab changes mid-battle, with its spell's charges (+0x10's +4): put on before the stats and
            // skills are laid, as putting it on changes them.
            var weapon = cp.ReadWord(sheet + 0x24);
            var wielded = weapon == 0 ? null : (RecordId?)TestRoms.ItemId(Half(cp, weapon));
            if (wielded != c.Sheet.Equipment.Weapon?.Id)
            {
                c.Sheet.Equipment.RemoveWeapon();
                if (wielded is { } id)
                    Assert.Equal(EquipRefusal.None, c.Sheet.Equipment.EquipWeapon(new ItemInstance(game.Items.TryGet(id)!)));
            }
            if (c.Sheet.Equipment.Weapon is { Charges: not null } held && cp.ReadWord(weapon + 0x10) is var charges and >= 0x80000000 and < 0x80800000)
                held.Charges = cp.ReadByte(charges + 4);
            // The batteries (the StatMod at +0xC's amount, +1), which casts draw before Stamina: Kitarak's Warfang has 15. The armor
            // and shield are +0x20's two pointers, and the gear +0x28's slots.
            var armor = cp.ReadWord(sheet + 0x20);
            var gear = cp.ReadWord(sheet + 0x28);
            var slots = InRam(gear) ? cp.ReadWord(gear) : 0;
            LayBattery(cp, c.Sheet.Equipment.Weapon, weapon);
            LayBattery(cp, c.Sheet.Equipment.Armor, InRam(armor) ? cp.ReadWord(armor) : 0);
            LayBattery(cp, c.Sheet.Equipment.Shield, InRam(armor) ? cp.ReadWord(armor + 4) : 0);
            for (var i = 0; i < Equipment.GearSlots && InRam(slots); i++)
                LayBattery(cp, c.Sheet.Equipment.Gear[i], cp.ReadWord(slots + (uint)i * 4));

            var stats = cp.ReadWord(sheet + 0x18);
            foreach (var stat in Enum.GetValues<Stat>())
            {
                var i = (uint)stat;
                SetStat(c.Sheet.Stats, stat, cp.ReadByte(stats + i), (sbyte)cp.ReadByte(stats + 7 + i), cp.ReadByte(stats + 0xE + i), (sbyte)cp.ReadByte(stats + 0x15 + i));
            }
            // The skills' base and modified levels (+0x1C), which the potions' and gear's bonuses, laid on without starting, don't move.
            // Laid raw: training caps a base, which an entity's record can pass (Lugash's Wizard 15).
            var skills = cp.ReadWord(sheet + 0x1C);
            var (rawBase, rawModified) = (SkillBytes(c.Sheet.Skills, "_base"), SkillBytes(c.Sheet.Skills, "_modified"));
            foreach (var skill in SkillId.All)
            {
                rawBase[skill.Slot] = (sbyte)cp.ReadByte(skills + (uint)skill.Slot);
                rawModified[skill.Slot] = (sbyte)cp.ReadByte(skills + 0x18 + (uint)skill.Slot);
            }
            OverlayEffects(game, cp, sheet, c.Sheet);
            // The protection (+0x14's +2), which the effects laid on without starting don't add.
            c.Sheet.Protection = cp.ReadByte(cp.ReadWord(sheet + 0x14) + 2);

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

        // Darkness or Light holds the part of the day as it lands (World.LockPartOfDay, world +2), and the clock's turn doesn't move
        // it; laid on without starting, its hold is laid too, at the part the RAM's world has. A record's end puts back the part the
        // battle began in (Combat+0x00).
        battle.StartPartOfDay = (PartOfDay)cp.ReadByte(built.Struct);
        if (battle.Combatants.Any(c => c.Sheet.Effects.Any(e => e.Effects.OfType<PartOfDayEffect>().Any())))
            ((IMagicHost)battle).HoldPartOfDay((PartOfDay)cp.Weather.PartOfDay);

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
        // The round's turns (+0x30 their count, +0x38 the list) and the toggle (+0x34), which each round's end builds afresh.
        var turns = (List<int>)typeof(TurnOrder).GetField("_turns", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(battle.Order)!;
        turns.Clear();
        turns.AddRange(Enumerable.Range(0, cp.ReadByte(s + 0x30)).Select(i => (int)cp.ReadByte(cp.ReadWord(s + 0x38) + (uint)i)));
        battle.Order.Toggle = cp.ReadWord(s + 0x34) != 0;
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
        return built;
    }

    /// <summary>
    /// A character's effects as the RAM has them, slot for slot, as their order is the order they age and draw in: the spells' 15
    /// (<c>entity+0x34</c>, pointers to 0x18-byte records: kind, power, school, margin, u32 harmful, u32 timer, …, the visual at
    /// <c>+0x14</c>) and the potions' 7 (<c>+0x38</c>, 0xC bytes: kind, power, u32 timer, the visual). They're laid on without
    /// starting, as the RAM's stats and flags already hold what they did. A record's effects are its spell's lasting ones for its side
    /// (by its harmful flag), Tap Stamina's caster's its gain. A morale effect gets back what it took, the record's <c>+0x01</c>, and
    /// Wraith Touch the stat its margin byte names by the amount its <c>+0x01</c> holds (<c>Entity.UndoEffect</c>'s kind <c>0x1A</c>).
    /// </summary>
    public static void OverlayEffects(GameState game, AresCheckpoint cp, uint entity, Character sheet)
    {
        var spells = game.Plugins.All<SpellRecord>(RecordTypes.Spell).Where(s => s.Position is not null).ToDictionary(s => s.Position!.Value);
        var spellSlots = new ActiveEffect?[sheet.Rules.Magic.Record.SpellEffects];
        var potionSlots = new ActiveEffect?[sheet.Rules.Magic.Record.PotionEffects];
        for (var i = 0; i < spellSlots.Length; i++)
        {
            var r = cp.ReadWord(cp.ReadWord(entity + 0x34) + (uint)i * 4);
            if (r == 0)
                continue;
            var spell = spells[cp.ReadByte(r)];
            var harmful = cp.ReadWord(r + 4) != 0;
            var lasting = spell.Effects?.Where(EffectRules.Lasting).ToList() ?? [];
            var gain = lasting.Where(e => e is StaminaEffect { ToCaster: true }).ToList();
            var effects = !harmful && gain.Count > 0 ? gain
                : [.. lasting.Except(gain).Where(e => e.For == EffectFor.Any || e.For == EffectFor.Opponents == harmful)];
            var power = cp.ReadByte(r + 1);
            var record = new ActiveEffect(spell.Id, effects, power, RomIds.School((HBHT.Rom.Db.MagicSchool)cp.ReadByte(r + 2)), (sbyte)cp.ReadByte(r + 3), cp.ReadWord(r + 8))
            {
                Harmful = harmful,
            };
            for (var e = 0; e < effects.Count; e++)
            {
                if (effects[e] is MoraleEffect m)
                    record.Amounts[e] = m.Amount.At(power) < 0 ? -power : power;
                else if (effects[e] is DrainStatEffect)
                    (record.DrainedStat, record.Amounts[e]) = (record.Margin, power);
            }
            spellSlots[i] = record;
        }
        for (var i = 0; i < potionSlots.Length; i++)
        {
            var r = cp.ReadWord(cp.ReadWord(entity + 0x38) + (uint)i * 4);
            if (r == 0)
                continue;
            var potion = RecordId.Hex(RecordTypes.Item, 0x1000 + cp.ReadByte(r));
            var effects = sheet.Rules.Magic.Potion(potion)?.Effects?.Where(EffectRules.Lasting).ToList() ?? [];
            // A flask's corrosion (kind 3, Acid's) is the protection its burst takes, a level a point (IMagicHost.Burst).
            if (sheet.Rules.Magic.Potion(potion)?.Effects?.OfType<CorrodeEffect>().Any() == true)
                effects = [new ProtectionEffect(new Magnitude(PerLevel: -1))];
            potionSlots[i] = new ActiveEffect(potion, effects, cp.ReadByte(r + 1), RomIds.School(HBHT.Rom.Db.MagicSchool.None), 0, cp.ReadWord(r + 4));
        }
        typeof(Character).GetField("_spellEffects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sheet, spellSlots);
        typeof(Character).GetField("_potionEffects", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(sheet, potionSlots);
    }

    /// <summary>Sets a stat's four values (trained, current, base, modified) by what the sheet's mutators can do.</summary>
    private static bool InRam(uint pointer) => pointer is >= 0x80000000 and < 0x80800000;

    private static sbyte[] SkillBytes(CharacterSkills skills, string field) =>
        (sbyte[])typeof(CharacterSkills).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(skills)!;

    /// <summary>An item's battery from its <c>ItemInstance</c> in RAM: the Stamina (stat 5) its <c>StatMod</c> has left.</summary>
    private static void LayBattery(AresCheckpoint cp, ItemInstance? item, uint instance)
    {
        if (item is not { Battery: not null } || !InRam(instance))
            return;
        var statMod = cp.ReadWord(instance + 0xC);
        if (InRam(statMod) && cp.ReadByte(statMod) == 5)
            item.Battery = (sbyte)cp.ReadByte(statMod + 1);
    }

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
            if (cp.ReadWord(p + 0x44) is not 0 and var record && c.Ai is { } ai)
            {
                if (cp.ReadByte(record + 8) != ai.Style)
                    wrong.Add($"{who} AI style: original {cp.ReadByte(record + 8)}, port {ai.Style}");
                // The action slots (+0x0A to +0x11): spell kinds, or 0xFF.
                var slots = Enumerable.Range(0, EnemyAI.ActionSlots).Select(i => cp.ReadByte(record + 0x0A + (uint)i)).ToList();
                var portSlots = ai.Slots.Select(id => id is { } spell ? (byte)built.Game.Plugins.Get<SpellRecord>(spell).Position!.Value : (byte)0xFF).ToList();
                if (!slots.SequenceEqual(portSlots))
                    wrong.Add($"{who} AI slots: original {Convert.ToHexString([.. slots])}, port {Convert.ToHexString([.. portSlots])}");
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


    /// <summary>The arena as a zone engine, for ticking the battle.</summary>
    public static ZoneEngine Zone(Built built) => new(built.Game, built.Streamer) { Camera = built.Battle.Camera };
}
