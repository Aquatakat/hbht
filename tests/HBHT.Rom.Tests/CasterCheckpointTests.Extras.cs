using System.Reflection;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Items;
using HBHT.Core.Magic;
using HBHT.Plugins;
using HBHT.Plugins.Records;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

// The caster vectors that aren't a caster's turn (tools/aivectors/extras.py, written as CasterExtraVectors): ScoreSpellCells by action
// slot, SpellValue by spell kind, AICanCast, the party's area danger and the morale spells. The same checkpoints' RAM, the same
// functions of 1.0 run in the interpreter; the port lays the same state over its battle and asks its own.
//
// Left out, and why:
//  - AICanCast's "AI flag 8 clear, Stamina 0" row: flag +9's bit 8 is the port's Combatant.Controlled, and Battle.CanCast only asks
//    AiCanCast of a Controlled combatant, so there is no AiCanCast call to compare with (the row says it returns true).
//  - AICanCast and the kinds' vectors lay no spell struct or entity-id word in RAM: the spell struct's special byte (+0x1B) is the
//    SpellRecord's Special, set on a copy of the record, and the entity id word is a combatant made again from that entity (Recast).
//  - SpellValue kinds that no spell record carries as its Position are skipped (the data has no spell of that kind), and the answer
//    1.0 gives for them is not tested.
public sealed partial class CasterCheckpointTests
{
    private static readonly MethodInfo AiCanCast = typeof(Battle).GetMethod("AiCanCast", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo InPlayCombatants = typeof(Battle).GetMethod("InPlayCombatants", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo PartyDanger = typeof(Battle).GetMethod("PartyDanger", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly FieldInfo BattleRules = typeof(Battle).GetField("_rules", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>Vectors the port disagrees with, by the vector's name, and why (each is asserted to still disagree, so a fix shows).</summary>
    private static readonly Dictionary<string, string> KnownDifferences = [];

    private SpellRecord SpellOfKind(int kind) => Set.All<SpellRecord>(RecordTypes.Spell).SingleOrDefault(s => s.Position == kind)!;

    // ---------------------------------------------------------------------------------------------------------------------------
    // ScoreSpellCells by action slot

    public static TheoryData<string> SingleIds => [.. CasterExtraVectors.Singles.Select(s => s.Id)];

    [Theory]
    [MemberData(nameof(SingleIds))]
    public void AnActionSlotsScoresAreTheOriginals(string id)
    {
        var k = CasterExtraVectors.Singles.Single(s => s.Id == id);
        var (built, c) = Begin(k.Checkpoint, k.Slot, k.Entity, k.Mods);
        Assert.Equal(k.Radius, c.Radius);
        // The slots 1.0's AI record holds are the ones the port's does.
        Assert.Equal(k.ActionKinds.Select(b => b != 0xFF), c.Ai!.Slots.Select(s => s is not null));

        var full = Hash(ScoreSpellCells.Invoke(built.Battle, [c.Ai])!);
        Assert.Equal(k.Full, full.Text);

        foreach (var slot in k.PerSlot)
        {
            (built, c) = Begin(k.Checkpoint, k.Slot, k.Entity, k.Mods);
            for (var i = 0; i < EnemyAI.ActionSlots; i++)
            {
                if (i != slot.Index)
                    c.Ai!.Slots[i] = null;
            }
            // The slot's spell is the kind 1.0's AI byte holds.
            Assert.Equal(slot.Kind, built.Game.Plugins.Get<SpellRecord>(c.Ai!.Slots[slot.Index]!.Value).Position);
            var alone = Hash(ScoreSpellCells.Invoke(built.Battle, [c.Ai!])!);
            Assert.Equal(slot.Entries, alone.Text);
        }
        // Where the slots score independently, the full run is their entries in slot order (the 32-entry list of an engaged caster
        // drops the last of a longer one, so it isn't).
        if (k.Independent)
            Assert.Equal(k.Full, string.Concat(k.PerSlot.Select(s => s.Entries)));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // EnemyAI.SpellValue by kind

    public static TheoryData<string> KindContexts => [.. CasterExtraVectors.Kinds.Select(k => k.Context).Distinct()];

    [Theory]
    [MemberData(nameof(KindContexts))]
    public void ASpellKindsValueIsTheOriginals(string context)
    {
        // The Sorceror at slot 5 of checkpoint 08 with set B's candidates slots 0 to 5 (the vectors' ctx_with_B).
        var (built, caster) = Begin(8, 5, 0x24B, context);
        var battle = built.Battle;
        var candidates = Enumerable.Range(0, 6).Select(i => battle.Slots[i]!).ToList();
        var inPlay = ((IEnumerable<Combatant>)InPlayCombatants.Invoke(battle, [])!).ToList();
        var wrong = new List<string>();
        var tested = 0;
        foreach (var row in CasterExtraVectors.Kinds.Where(r => r.Context == context))
        {
            var spells = Set.All<SpellRecord>(RecordTypes.Spell).Where(s => s.Position == row.Kind).ToList();
            foreach (var spell in spells)
            {
                tested++;
                var value = SpellValues.Value(built.Game.Rules.Magic, spell, caster, candidates, inPlay, battle.PartOfDay);
                if (value != row.Value && !KnownDifferences.ContainsKey($"{row} {spell.Name}"))
                    wrong.Add($"{row} {spell.Name}: 1.0 {row.Value}, port {value}");
            }
        }
        output.WriteLine($"{tested} spells tested in [{context}]");
        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryNeutralKindsSpellsAreTested()
    {
        // The data's spells are of kinds 0..58 and the neutral context has a value for each; the port's spells carry a Position for
        // those that 1.0 gives a value for.
        var kinds = Set.All<SpellRecord>(RecordTypes.Spell).Select(s => s.Position).OfType<int>().Distinct().Order().ToList();
        var neutral = CasterExtraVectors.Kinds.Where(r => r.Context == "").Select(r => r.Kind).ToHashSet();
        output.WriteLine($"spell kinds in the data: {string.Join(' ', kinds)}");
        Assert.All(kinds, kind => Assert.Contains(kind, neutral));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Combatant.AICanCast

    public static TheoryData<string> AiCanGroups => [.. CasterExtraVectors.AiCans.Select(r => $"{r.Entity:X4}/{r.ActionSlot}").Distinct()];

    [Theory]
    [MemberData(nameof(AiCanGroups))]
    public void WhatTheAiMayCastIsTheOriginals(string group)
    {
        var rows = CasterExtraVectors.AiCans.Where(r => $"{r.Entity:X4}/{r.ActionSlot}" == group).ToList();
        var (built, c) = Begin(8, 5, rows[0].Entity, "");
        var battle = built.Battle;
        var target = built[0];
        var source = c.SpellOf(Battle.SlotChoice(rows[0].ActionSlot), built.Game.Rules.Magic)!;
        Assert.NotNull(source);
        var wrong = new List<string>();
        foreach (var row in rows)
        {
            Assert.Equal((row.Kind, row.Level, row.Cost), (source.Spell.Position, Casting.Level(c.Sheet, source), Casting.Cost(source.Spell, Casting.Level(c.Sheet, source))));
            SetStamina(c, row.Stamina);
            if (row.Charges >= 0)
                source.Item!.Charges = row.Charges;
            var result = MayCast(battle, c, source, target);
            if (result != row.Result && !KnownDifferences.ContainsKey(row.ToString()))
                wrong.Add($"{row}: 1.0 {row.Result}, port {result}");
        }
        Assert.Empty(wrong);
    }

    /// <summary>1.0's AICanCast: the target's requirement and the AI's own tests (the port's Battle.CanCast asks them one after the other).</summary>
    private static bool MayCast(Battle battle, Combatant c, SpellSource source, Combatant target) =>
        Casting.Requirement(source.Spell, target.Sheet) == CastRefusal.None && (bool)AiCanCast.Invoke(battle, [c, source, target])!;

    public static TheoryData<int> AiCanSpecialPartsOfDay => [1, 2, 3, 4];

    [Theory]
    [MemberData(nameof(AiCanSpecialPartsOfDay))]
    public void ASpellsSpecialAndThePartOfDayAreTheOriginals(int part)
    {
        // The Sorceror's spell at known index 3 with the special byte of its struct edited; Alaron the target.
        var (built, c) = Begin(8, 5, 0x24B, $"daypart:{part}");
        var target = built[0];
        SetStamina(c, 60);
        var known = SpellSource.From(c.Sheet.Spells[3]);
        var wrong = new List<string>();
        foreach (var row in CasterExtraVectors.AiCanSpecials.Where(r => r.PartOfDay == part))
        {
            var source = known with { Spell = known.Spell with { Special = (SpellSpecial)row.Special } };
            var requirement = Casting.Requirement(source.Spell, target.Sheet) != CastRefusal.None;
            var timeAllows = Casting.TimeAllows(source.Spell, battleDay(built));
            var result = MayCast(built.Battle, c, source, target);
            if ((row.Requirement != 0, row.TimeAllows == 0, row.Result) != (requirement, timeAllows, result) && !KnownDifferences.ContainsKey(row.ToString()))
                wrong.Add($"{row}: 1.0 requirement refused {row.Requirement != 0}, time allows {row.TimeAllows == 0}, result {row.Result}; port {requirement}, {timeAllows}, {result}");
        }
        Assert.Empty(wrong);

        static PartOfDay battleDay(Built b) => b.Battle.PartOfDay;
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The party's area danger

    public static TheoryData<int> DangerCases => [.. Enumerable.Range(0, CasterExtraVectors.Dangers.Length)];

    [Theory]
    [MemberData(nameof(DangerCases))]
    public void ThePartysAreaDangerIsTheOriginals(int index)
    {
        var k = CasterExtraVectors.Dangers[index];
        var built = At(Set, 8);
        var (battle, game) = (built.Battle, built.Game);
        var rules = (CombatRecord)BattleRules.GetValue(battle)!;

        // The party as 1.0 had it: no flasks, every member's Wizard level as it was, then the case's changes.
        foreach (var flask in rules.FlaskDanger!)
            game.Party.Inventory.Take(flask.Item, int.MaxValue);
        foreach (var flask in k.Flasks)
            Assert.True(game.Party.Inventory.Add(RecordId.Hex(RecordTypes.Item, flask)));
        var fireball = Set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Area);
        for (var m = 0; m < k.MemberWizard.Length; m++)
            SetWizard(battle.Slots[m]!.Sheet, k.MemberWizard[m]);
        foreach (var (member, level) in k.Fireballs)
        {
            var sheet = battle.Slots[member]!.Sheet;
            Assert.Null(sheet.FindSpell(fireball.Id));
            sheet.LearnSpell(fireball, level);
        }
        foreach (var member in k.Dead)
        {
            var stats = battle.Slots[member]!.Sheet.Stats;
            foreach (var stat in (ReadOnlySpan<Stat>)[Stat.Endurance, Stat.Stamina, Stat.Level])
                SetStat(stats, stat, stats.Trained(stat), 0, stats.RawBase(stat), 0);
            Assert.True(battle.Slots[member]!.Sheet.IsDead);
        }

        var tier = rules.FlaskDanger!.FirstOrDefault(f => game.Party.Inventory.Has(f.Item))?.Radius ?? 0;
        var danger = (int)PartyDanger.Invoke(battle, [rules])!;
        Assert.Equal((k.Tier, k.Value), (tier, danger));
    }

    /// <summary>A member's Wizard level, base and modified, as a raw byte (the vectors poke both).</summary>
    private static void SetWizard(Character sheet, int level)
    {
        foreach (var field in new[] { "_base", "_modified" })
            ((sbyte[])typeof(CharacterSkills).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(sheet.Skills)!)[((SkillId)Skill.Wizard).Slot] = (sbyte)level;
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The morale spells

    public static TheoryData<int> MoraleCases => [.. Enumerable.Range(0, CasterExtraVectors.Morales.Length)];

    [Theory]
    [MemberData(nameof(MoraleCases))]
    public void AMoraleSpellIsTheOriginals(int index)
    {
        var k = CasterExtraVectors.Morales[index];
        var (built, c, spell) = Shaken(k.Kind, k.Morale0);
        var (battle, ai) = (built.Battle, c.Ai!);

        var record = Lay(c, spell, k.Power, k.Timer, battle);
        Assert.NotNull(record);
        Assert.Equal((k.MoraleAfterStart, k.FlagsAfterStart), (ai.Morale, (int)ai.Flags));
        // A second cast while it holds is refused, and takes no more morale.
        Assert.Null(Lay(c, spell, k.Power, k.Timer, battle));
        Assert.Equal(k.MoraleAfterSecond, ai.Morale);

        var rounds = new List<(int, bool)>();
        for (var round = 0; round < 11; round++)
        {
            c.Sheet.AgeRound(c, battle, battle.PartOfDay, built.Game.Random.Combat);
            var present = c.Sheet.HasEffect(spell.Id);
            rounds.Add((ai.Morale, present));
            if (!present)
                break;
        }
        Assert.Equal(k.Rounds, rounds);
        Assert.Equal(k.MoraleAtEnd, ai.Morale);
    }

    public static TheoryData<string> RemovalPaths => [.. CasterExtraVectors.Removals.Select(r => r.Path)];

    [Theory]
    [MemberData(nameof(RemovalPaths))]
    public void AMoraleSpellTakenOffGivesTheMoraleBackAsTheOriginalDoes(string path)
    {
        var k = CasterExtraVectors.Removals.Single(r => r.Path == path);
        var (built, c, spell) = Shaken(25, 5);
        var (battle, ai) = (built.Battle, c.Ai!);
        Assert.NotNull(Lay(c, spell, 3, 99, battle));
        Assert.Equal(k.MoraleAfterStart, ai.Morale);

        var dispelled = false;
        switch (path)
        {
            case "dispel":
                // Dispel Necro (spell kind 44) at a margin of 50: Entity.Dispel, which the landing of the spell calls.
                var school = SpellOfKind(44).Effects!.OfType<DispelEffect>().Single().School;
                dispelled = (bool)typeof(EffectLanding).GetMethod("Dispel", BindingFlags.NonPublic | BindingFlags.Static)!
                    .Invoke(null, [new EffectTarget(c.Sheet, c, battle), school, 50])!;
                break;
            case "lethal":
                c.Sheet.TakeDamage(10000, c);
                break;
            case "death":
                battle.Death(c);
                break;
            case "removeall":
                c.Sheet.RemoveAllEffects(c, battle);
                break;
            default:
                throw new InvalidDataException(path);
        }
        Assert.Equal((k.Dispelled, k.MoraleAfter, k.RecordPresent), (path == "dispel" ? dispelled : false, ai.Morale, c.Sheet.HasEffect(spell.Id)));
    }

    /// <summary>The rat at slot 5 of checkpoint 08, with the morale it is to have, and the spell of the kind (25 Aura of Death, 47 Solar Wrath).</summary>
    private (Built Built, Combatant Rat, SpellRecord Spell) Shaken(int kind, int morale)
    {
        var built = At(Set, 8);
        var rat = built[5];
        Assert.NotNull(rat.Ai);
        rat.Ai.Morale = morale;
        return (built, rat, SpellOfKind(kind));
    }

    /// <summary>The spell's lasting effects put on the combatant at a margin of 7 (<c>Entity.AddEffect</c>, as the cast lands them), or null where it refuses.</summary>
    private static ActiveEffect? Lay(Combatant c, SpellRecord spell, int power, int timer, Battle battle) =>
        c.Sheet.AddEffect(spell.Id, [.. (spell.Effects ?? []).Where(EffectRules.Lasting)], power, spell.School, 7, (uint)timer, c, battle, null);
}
