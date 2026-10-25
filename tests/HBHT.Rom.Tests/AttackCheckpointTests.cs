using System.Numerics;
using System.Reflection;
using HBHT.Core;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Core.Ui;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

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
    private PluginSet Set => fixture.Data.Set;

    private Built Plain(int checkpoint) => CombatCheckpoints.Plain(Set, checkpoint);

    private Built At(int checkpoint) => CombatCheckpoints.At(Set, checkpoint);

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

    /// <summary>
    /// Fleeing and being untargetable are the combat record's two rules (<see cref="CombatRecord.Flee"/>,
    /// <see cref="CombatRecord.Untargetable"/>): a mod's enemy the party may flee from can still be attacked, and only an untargetable
    /// one can't be, though it still attacks.
    /// </summary>
    [Fact]
    public void AnEnemyThePartyMayFleeFromCanBeAttackedAndOnlyAnUntargetableOneCant()
    {
        var built = At(5);
        var (battle, alaron, rat) = (built.Battle, built[0], built[7]);
        var rules = battle.Rules!;
        var field = typeof(Battle).GetField("_rules", BindingFlags.NonPublic | BindingFlags.Instance)!;

        field.SetValue(battle, rules with { Flee = [.. rules.Flee, rat.Entity] });
        Assert.True(battle.CanAttack(alaron, rat));

        field.SetValue(battle, rules with { Untargetable = [rat.Entity] });
        Assert.False(battle.CanAttack(alaron, rat));
        Assert.True(battle.CanAttack(rat, alaron));
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

        // The rewards come at once: ten rats' experience, 1500 to each living member.
        Assert.Equal(1000, battle.ExperiencePool);

        // Once it has faded out, it leaves for the post-battle windows, won.
        for (var i = 0; i < 600 && !battle.Over; i++)
            battle.Tick(zone, false, _ => null);
        Assert.True(battle.Over);
        Assert.Equal(BattleOutcome.Won, battle.Outcome);
        Assert.Equal(1000, battle.Spoils!.Experience);
        Assert.False(battle.Fled);
        Assert.False(alaron.Sheet.IsDead);
    }

    [Fact]
    public void SholehsDeathSetsHerDiedFlagAtOnce()
    {
        var built = At(5);
        var sholeh = built.Battle.Combatants.Single(c => c.Entity == RecordId.Hex(RecordTypes.Entity, 0x024D));
        var died = RecordId.Decimal(RecordTypes.Flag, 622);
        built.Game.Flags.TrySet(died, false);
        sholeh.Sheet.TakeDamage(1000, sholeh);
        built.Battle.Death(sholeh);
        Assert.True(built.Game.Flags.IsSet(died));
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
