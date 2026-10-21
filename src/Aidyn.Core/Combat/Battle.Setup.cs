using System.Globalization;
using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Combat;

public sealed partial class Battle
{
    /// <summary>Where the zone engine's point is put in the arena chunk (<c>Zones.SetMapSet</c>'s case for the Battle arenas).</summary>
    public static readonly Vector3 ZonePoint = new(20, 0, 20);

    /// <summary>The fade in as a battle starts: a sixtieth a tick (<c>Combat.Init</c>, <c>0x800CE308</c>).</summary>
    public const float FadePerTick = 1f / 60;

    /// <summary>The height a body is made at before it's set down (<c>Combat.CreateBody</c>).</summary>
    private const float BodyHeight = 1;

    /// <summary>
    /// The way each direction faces, 0–7 from north round to north-west (<c>Combat.DirectionVectors</c>, <c>0x800D27A8</c>),
    /// normalised.
    /// </summary>
    public static readonly IReadOnlyList<Vector2> Directions =
        [.. new Vector2[] { new(0, -1), new(1, -1), new(1, 0), new(1, 1), new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1) }.Select(Vector2.Normalize)];

    /// <summary>The collision byte of an ambush: the sides' spawns swap, and the Ranger can move the party's.</summary>
    public const int AmbushByte = 2;

    /// <summary>The Ranger's roll that leaves the party's spawn set as it is: no one to roll (<c>Party.BattleAmbushRoll</c>).</summary>
    public const int NoAmbushRoll = 2;

    /// <summary>
    /// A battle set up from <paramref name="setup"/> on its arena, whose chunks are loaded in <paramref name="world"/>
    /// (<c>Combat.Init</c>, <c>0x80022D1C</c>, and <c>Combat.Setup</c>, <c>0x8005D15C</c>): the enemy list rolled, Sholeh
    /// taken to the party's side, the grid sized and filled, a combatant and a body for each living party member, Sholeh
    /// and each enemy of the first four on its spawn, the leader, whether there are flee points, the turn order and whether
    /// the party can flee; then the camera, the first turn's line and the fade in. The draws are on the combat stream, in
    /// the original's order: the enemy list, the Ranger's roll, each combatant's direction, the turn order.
    /// </summary>
    public static Battle Begin(GameState game, BattleSetup setup, Arena arena, CollisionWorld world)
    {
        var plugins = game.Plugins;
        var rules = plugins.Singleton<CombatRecord>();
        var random = game.Random.Combat;
        var list = setup.EnemyList(game.Party, e => plugins.TryGet<EntityRecord>(e)?.Level ?? 0, random, rules.Fallback).ToList();

        // Combat.FindSholeh: she's taken from the first four to fight with the party, and the list closes up.
        var onField = Math.Min(list.Count, OnField);
        var ally = list.Take(OnField).Contains(rules.Ally) ? rules.Ally : (RecordId?)null;
        if (ally is { } sholeh)
        {
            list.RemoveAt(list.IndexOf(sholeh));
            onField--;
        }
        var battle = new Battle(game, setup, arena, world, list) { EnemiesOnField = onField, NextReserve = onField };
        battle.Build(rules, ally);
        return battle;
    }

    /// <summary>
    /// A battle of combatants already made, for tests of the rules that read only its sides, setup and arena: no game,
    /// grid, camera or turn yet.
    /// </summary>
    internal static Battle Of(BattleSetup setup, Arena? arena, int partySize, int enemiesOnField, IReadOnlyList<Combatant?> slots)
    {
        var battle = new Battle(null!, setup, arena!, new CollisionWorld(), []) { PartySize = partySize, EnemiesOnField = enemiesOnField };
        for (var i = 0; i < slots.Count; i++)
            battle._slots[i] = slots[i];
        battle.Gui = Ui.CombatGui.For(battle);
        return battle;
    }

    /// <summary>
    /// A battle on <paramref name="game"/> with only its enemy list and the combat record, for tests of what its end does to the
    /// game: the rewards, the flags and the party. No arena, grid, combatants or turn.
    /// </summary>
    internal static Battle Ending(GameState game, BattleSetup setup, IReadOnlyList<RecordId> enemies)
    {
        var battle = new Battle(game, setup, null!, new CollisionWorld(), enemies) { NextReserve = enemies.Count };
        battle._rules = game.Plugins.Singleton<CombatRecord>();
        return battle;
    }

    /// <summary>The setup's collision byte: an ambush's, or a script's own.</summary>
    private int CollisionByte => Setup.Ambush ? AmbushByte : Setup.CollisionByte;

    /// <summary><c>Combat.Setup</c> and the rest of <c>Combat.Init</c> once the list is made.</summary>
    private void Build(CombatRecord rules, RecordId? ally)
    {
        var random = Game.Random.Combat;
        _rules = rules;
        Grid.Init(Arena.Map);
        Grid.Fill(World, Arena.Origin, (Arena.Chunk.Row, Arena.Chunk.Column));
        var roll = CollisionByte == AmbushByte ? AmbushRoll(Game.Party, random) : 0;

        // Combat.CreatePartyCombatants: the living members in order, each its spawn's letter by the count so far.
        var slot = 0;
        foreach (var member in Game.Party.Slots)
        {
            if (member is null || member.IsDead)
                continue;
            Add(slot, member, hostile: false, party: true, letter: slot, roll);
            slot++;
        }
        // Combat.CreateSholeh: after the party, always at the spawns' d.
        if (ally is { } sholeh && Game.Recruit(sholeh) is { } allySheet)
        {
            var spare = ArmFromList(allySheet, Game.Items);
            GiveAi(Add(slot, allySheet, hostile: false, party: true, letter: 3, roll), spare);
            slot++;
        }
        PartySize = slot;
        Danger = PartyDanger(rules);

        // Combat.CreateEnemyCombatants: the first four of the list, each at the letter of its place in it; bystanders
        // are dropped, leaving their letter unused.
        for (var i = 0; i < EnemiesOnField; i++)
        {
            var entity = Enemies[i];
            if (rules.Bystanders.Contains(entity))
            {
                _problems.Add($"{entity} never fights, and is left out.");
                continue;
            }
            if (Game.Recruit(entity) is not { } sheet)
            {
                _problems.Add($"{entity} is in no plugin.");
                continue;
            }
            var spare = ArmFromList(sheet, Game.Items);
            QuickenShadow(sheet);
            var enemy = Add(slot, sheet, hostile: true, party: false, letter: i, roll);
            GiveAi(enemy, spare);
            // Combat.ForbidFleeForBosses: the last on the field with a say has it.
            if (rules.NoFlee.Contains(entity))
                CanFlee = false;
            else if (rules.Flee.Contains(entity))
                CanFlee = true;
            // The leader: the first enemy, unless a later one is strictly cleverer by base Intelligence.
            if (Leader is null || sheet.Stats.Base(Stat.Intelligence) > Leader.Sheet.Stats.Base(Stat.Intelligence))
                Leader = enemy;
            // Combat.CreateEnemyCombatants: the leader's morale starts as its record's.
            LeaderSlot = Leader.Slot;
            LeaderMorale = Leader.Ai!.Morale;
            slot++;
        }

        // Combat.FindFleePoints: whether any of flee0–flee7 is there; the AI finds them by name as it flees.
        HasFleePoints = Enumerable.Range(0, MaxFleePoints).Any(i => Arena.Point(FleePoint(i)) is not null);
        Order.Build(this, random);
        Current = Order.Current(this);
        FocusAt(Current?.Cell ?? default);
        // Combat.ApplyFleeRuleByFlag has the last word.
        if (Setup.EncounterFlag is { } flag && rules.FleeByEncounter.FirstOrDefault(e => e.Flag == flag) is { } rule)
            CanFlee = rule.CanFlee;

        // Combat.CreateBody: each body as it's made works its radius out and is held in its field where it stands.
        _rooted = rules.Rooted ?? [];
        foreach (var combatant in Combatants)
        {
            combatant.UpdateRadius(this);
            combatant.PlaceField(this);
        }
        var at = Current?.Body.Sphere.Position ?? Arena.Origin + ZonePoint;
        CombatCamera = new CombatCamera(at, Current?.Body.Heading ?? Vector2.UnitX);
        // CombatGui.Create, then each combatant's portrait into its frame as its body is made (Combat.CreateBody).
        Gui = Ui.CombatGui.For(this);
        StartFirstTurn();
        Fade.In(FadePerTick);
        // Combat.Init: a battle with its own rules (the Goblin Ambush's) heals the party's first member, and counts the hits it holds.
        if (Rule is { } own)
        {
            if (own.HealsLeader && Game.Party.Slots[0] is { } leader)
                leader.Heal(leader.MaxHp - leader.Hp);
            KeelOverHits = own.KeelOver?.Hits ?? 0;
        }
    }

    /// <summary>
    /// What a combatant from the enemy list wields (the entity's enemy build, <c>0x8006E2E8</c>, through <c>0x8006E434</c>): its
    /// entity's third weapon if it can, else its first; the second is never read. Returns the spare its AI keeps, the first weapon
    /// when it has a third too.
    /// </summary>
    internal static Items.ItemInstance? ArmFromList(Character sheet, Items.ItemCatalog items)
    {
        var weapons = sheet.Record.Weapons;
        var first = weapons.Count > 0 ? weapons[0] : null;
        var third = weapons.Count > 2 ? weapons[2] : null;
        sheet.Equipment.RemoveWeapon();
        sheet.Spares.Clear();
        if (third is { } t)
            sheet.Equipment.EquipWeapon(new Items.ItemInstance(items[t]));
        if (first is { } f && sheet.Equipment.Weapon is null)
            sheet.Equipment.EquipWeapon(new Items.ItemInstance(items[f]));
        return first is { } spare && third is not null ? new Items.ItemInstance(items[spare]) : null;
    }

      /// <summary>
    /// For tests: the combatant at <paramref name="slot"/> made again from another sheet where it stands (<c>Combatant.Init</c> on a
    /// new entity), keeping its body, cell, turn's start, direction, footprint and flags, with the new entity's look (the heights the attack's sight reads); armed, with its first spell selected, its
    /// action from its weapon and a new AI record, as an enemy is made. Its field radius is worked out again.
    /// </summary>
    internal Combatant Recast(int slot, Character sheet)
    {
        var old = _slots[slot]!;
        var spare = ArmFromList(sheet, Game.Items);
        var c = new Combatant(slot, sheet, sheet.Record.Body ?? old.Look, old.Body, old.Flags)
        {
            MagicHost = this,
            Cell = old.Cell,
            TurnStart = old.TurnStart,
            Direction = old.Direction,
            Facing = old.Facing,
            ReadFrom = old.ReadFrom,
            Footprint = old.Footprint,
            Party = old.Party,
            Sounds = sheet.Record.Sounds,
        };
        c.SetDefaultAction(_rules);
        if (sheet.Spells.Count > 0)
            c.Spell = new SpellChoice(SpellSourceKind.Known);
        _slots[slot] = c;
        GiveAi(c, spare);
        c.UpdateRadius(this);
        return c;
    }

  /// <summary>A combatant the AI plays, with its record (<see cref="EnemyAI.Init"/>).</summary>
    private void GiveAi(Combatant c, Items.ItemInstance? spare)
    {
        c.Controlled = true;
        c.Ai = EnemyAI.Init(c, spare, AllowsMelee, _rules?.BossStyles?.Contains(c.Entity) == true);
    }

    /// <summary>The flee points' most: <c>flee0</c>–<c>flee7</c>.</summary>
    public const int MaxFleePoints = 8;

    /// <summary>A flee point's name (the combat strings' <c>flee%d</c>).</summary>
    public static string FleePoint(int i) => string.Create(CultureInfo.InvariantCulture, $"flee{i}");

    /// <summary>
    /// A spawn's name (<c>%s%u%c</c>): <c>good</c> or <c>bad</c>, the set and the letter (<c>Combat.SpawnPosition</c>,
    /// <c>0x8005C294</c>).
    /// </summary>
    public static string SpawnPoint(bool good, int set, int letter) =>
        string.Create(CultureInfo.InvariantCulture, $"{(good ? "good" : "bad")}{set}{(char)('a' + letter)}");

    /// <summary>
    /// Where a combatant starts, and which way it faces (<c>Combat.SpawnPosition</c>): one draw for the direction, 1–3 for
    /// the party and for the enemies unless the collision byte is 0, when theirs is 5–7; the set, 4 less the animation
    /// byte, the reserves' 0; the party at <c>good</c> and the enemies at <c>bad</c>, swapped in an ambush, where the
    /// Ranger's roll moves the party's set. Returns the spawn's name, its point if the arena has it, and the direction.
    /// </summary>
    public (string Name, ReferencePointObject? Point, int Direction) Spawn(bool party, bool reserve, int letter, int roll, IRandom random)
    {
        var direction = !party && CollisionByte == 0 ? random.Range(5, 7) : random.Range(1, 3);
        var animation = Setup.AniByte;
        int set = reserve ? 0 : (byte)(4 - animation);
        var good = true;
        if (CollisionByte == AmbushByte)
        {
            good = false;
            if (party)
            {
                if (roll == 0)
                    set = animation == 3 ? 3 : 4;
                else if (roll == 1 && animation != 0)
                    set++;
                else if (roll >= 4 && animation != 3)
                    set--;
            }
        }
        if (!party)
            good = !good;
        var name = SpawnPoint(good, set, letter);
        return (name, Arena.Point(name), direction);
    }

    /// <summary>
    /// The Ranger's roll in an ambush (<c>Party.BattleAmbushRoll</c>, <c>0x80076C60</c>): the member with the strictly
    /// best modified Ranger who has the Stamina for it, dead or not, rolls a d100 against Intelligence × 3 + Ranger × 10.
    /// Under it, the step for what's left (<see cref="Party.SkillCheckStep"/>), for the Stamina it costs (5 less the Ranger,
    /// at least 1); otherwise 0. With no one to roll, <see cref="NoAmbushRoll"/>, with no draw.
    /// </summary>
    public static int AmbushRoll(Party party, IRandom random)
    {
        Character? ranger = null;
        var best = 0;
        foreach (var member in party.Slots)
        {
            if (member is null)
                continue;
            var skill = member.Skills.Modified(Skill.Ranger);
            if (best < skill && member.Stats.Modified(Stat.Stamina) >= Math.Max(1, 5 - skill))
            {
                best = skill;
                ranger = member;
            }
        }
        if (ranger is null)
            return NoAmbushRoll;
        var target = (ushort)(ranger.Stats.Modified(Stat.Intelligence) * 3 + best * 10);
        var roll = random.RollD(1, 100);
        if (roll >= target)
            return 0;
        var step = Party.SkillCheckStep((short)(target - roll));
        var cost = Math.Max(1, 5 - best);
        if (ranger.Stats.Modified(Stat.Stamina) < cost)
            return 0;
        ranger.DrainStamina(cost);
        return step;
    }

    /// <summary>
    /// A combatant on its spawn (<c>Combatant.Init</c>, <c>0x8005EAE0</c>, and <c>Combat.CreateBody</c>, <c>0x8005C474</c>):
    /// its cell the spawn's, truncated; its footprint twice its scaled radius, rounded, at least 1, taken on the grid; its
    /// facing by its direction, its body heading that way; and its body made a unit up at the cell and set down.
    /// </summary>
    private Combatant Add(int slot, Character sheet, bool hostile, bool party, int letter, int roll)
    {
        var (name, point, direction) = Spawn(party, reserve: false, letter, roll, Game.Random.Combat);
        if (point is null)
            _problems.Add($"{Arena.Map.Id} has no spawn {name}, where the original stops (FindReferencePointName).");
        return Make(slot, sheet, hostile, party, CellOf(point), direction);
    }

    /// <summary>A spawn's cell: its X and Z truncated, or the zone engine's point for one the arena lacks.</summary>
    private static Vector2 CellOf(ReferencePointObject? point) =>
        point is null ? new Vector2(ZonePoint.X, ZonePoint.Z) : new Vector2((byte)(int)point.Position.X, (byte)(int)point.Position.Z);

    /// <summary>
    /// A combatant at a cell, facing a direction (<c>Combatant.Init</c> and <c>Combat.CreateBody</c>): its footprint taken on the
    /// grid, its body made a unit up and set down, and what A does with its weapon (<see cref="Combatant.SetDefaultAction"/>).
    /// </summary>
    private Combatant Make(int slot, Character sheet, bool hostile, bool party, Vector2 cell, int direction)
    {
        var look = sheet.Record.Body ?? NoBody;
        var footprint = Footprint(look);
        var at = Arena.Origin + new Vector3(cell.X, BodyHeight, cell.Y);
        // A body walking to a point in combat gives it up where it stands after 30 ticks of coming no nearer (Body.StepTowardGoal).
        var body = new Body(at, look.Radius, look.Scale) { Heading = Directions[direction], GiveUpTicks = 30, StaysWhenStalled = true };
        body.Sphere.Position = Body.SetDown(World, at, look.Radius, Game.Random.Combat).Position;
        var flags = CombatantFlags.Active | (hostile ? CombatantFlags.Hostile | CombatantFlags.FromEnemyList : 0);
        var combatant = new Combatant(slot, sheet, look, body, flags)
        {
            MagicHost = this,
            Cell = cell,
            TurnStart = cell,
            Direction = direction,
            Facing = Directions[direction],
            ReadFrom = body.Sphere.Position,
            Footprint = footprint,
            Party = party,
            Sounds = sheet.Record.Sounds,
        };
        Grid.Occupy((int)cell.X, (int)cell.Y, footprint);
        combatant.SetDefaultAction(_rules);
        // Combatant.Init: the first spell it knows is selected.
        if (sheet.Spells.Count > 0)
            combatant.Spell = new SpellChoice(SpellSourceKind.Known);
        _slots[slot] = combatant;
        return combatant;
    }

    /// <summary>How many cells across a body stands on: twice its scaled radius, rounded half away from zero, at least 1.</summary>
    public static int Footprint(EntityBody look)
    {
        var across = look.Radius * look.Scale;
        across += across;
        var cells = (byte)(across > 0 ? (int)(across + 0.5f) : -(int)(0.5f - across));
        return cells == 0 ? 1 : cells;
    }

    /// <summary>The combat strings' "%s begins action" (offset 0).</summary>
    public const int BeginsAction = 0x00;

    /// <summary>An entity with no body: the original's defaults for one, a radius of 0.55 and the bat's actor (<c>EntityDB::GetBorg7</c>, as [actors](../../../docs/formats/actors.md) records).</summary>
    private static readonly EntityBody NoBody = new(RecordId.Hex(RecordTypes.Actor, 0x2D4A), 0.55f, 1, 1);

    /// <summary>A line of the combat strings (String/Combat), by its byte offset in the original's table, a line every 4.</summary>
    public string CombatText(int offset) =>
        Game.Plugins.All<StringsRecord>(RecordTypes.Strings).FirstOrDefault(s => s.Name == "Combat")?.Lines.ElementAtOrDefault(offset / 4) ?? "";
}
