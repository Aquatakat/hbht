using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using HBHT.Core;
using HBHT.Core.Calendar;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Plugins;
using HBHT.Plugins.Records;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// The AI's casters and bosses (M3 session 11) against the original's own results: a caster built by 1.0's own entity builder put in a
/// rat's place on the user's Combat checkpoints, its turn begun, and 1.0's <c>ScoreSpellCells</c>, <c>ChooseSpellOrAttack</c>,
/// <c>RunStyle</c> and <c>EnemyAI.Step</c> run on it in the MIPS interpreter (<c>tools/aivectors</c>, which writes
/// <see cref="CasterVectors"/>). The port lays the same caster over the same battle
/// (<see cref="Battle.Recast"/>, the rat's body kept) and plays the same turn. The tests skip without the ROM and the checkpoints.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class CasterCheckpointTests(Na10Import fixture, ITestOutputHelper output)
{
    private PluginSet Set => fixture.Data.Set;

    /// <summary>
    /// The cases whose turn an attack's line of sight decides, which the port runs head to head where 1.0 lifts the attacker's end by
    /// both heads and aims at the target's middle (the user's choice, <see cref="AnAttacksSightRunsHeadToHead"/>).
    /// </summary>
    private static readonly string[] SightFixed = ["cp11-s6-21c-BanditBoss-nostamina", "cp08-s5-221-Marquis-nostamina"];

    /// <summary>
    /// The cases, but the five that wear a weapon as armor or a shield to give those action slots a spell (no enemy has one, and the
    /// port's equipment won't wear it), and those the port's line of sight changes.
    /// </summary>
    public static TheoryData<string> Cases =>
        [.. CasterVectors.Cases.Where(c => !c.Mods.Contains("itemspell") && !SightFixed.Contains(c.Id)).Select(c => c.Id)];

    [Fact]
    public void AnAttacksSightRunsHeadToHead()
    {
        // The Bandit Boss with its bow at checkpoint 11: Rheda stands behind Alaron. 1.0's line, from high above the Boss down to
        // Rheda's middle, passes through Alaron's body, and its set A is Alaron, Brenna and Sholeh; head to head, it clears him.
        var (built, boss) = Begin(Find("cp11-s6-21c-BanditBoss-nostamina"));
        var battle = built.Battle;
        Assert.Equal([0, 1, 2, 3], Enumerable.Range(0, Battle.SlotCount).Where(battle.TargetsA.IsCandidate));
        Assert.True(battle.CanAttack(boss, built[1]));
    }

    private static CasterVectors.Case Find(string id) => CasterVectors.Cases.Single(c => c.Id == id);

    /// <summary>
    /// The case's battle as its turn begins: the checkpoint laid over 01's battle (02's is 01's own), the caster put in the rat's
    /// place, the case's changes made, and the turn begun as <c>TurnOrder.Advance</c> and <c>BeginTurn</c> do: the focus on the caster,
    /// state 8, and both target sets built for it.
    /// </summary>
    private (Built Built, Combatant Caster) Begin(CasterVectors.Case k)
    {
        Built built;
        if (k.Checkpoint == 2)
        {
            built = Plain(Set, 2);
            // What 01's battle doesn't hold of 02's: Rheda has moved, which an attack's chance reads.
            foreach (var p in built.Battle.Combatants)
                p.Moves = built.Cp.ReadByte(built.Record(p.Slot) + 0x22);
            built.Game.Clock.Time = built.Cp.Time;
            if (built.Game.Weather is { } weather)
                weather.PartOfDay = (PartOfDay)built.Cp.Weather.PartOfDay;
        }
        else
        {
            built = At(Set, k.Checkpoint);
        }
        var battle = built.Battle;
        var entity = RecordId.Hex(RecordTypes.Entity, k.Entity);
        var c = battle.Recast(k.Slot, built.Game.Recruit(entity)!);
        var radius = (int?)null;
        foreach (var mod in k.Mods.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var (name, args) = (mod[..mod.IndexOf(':')], mod[(mod.IndexOf(':') + 1)..]);
            var n = args.Split(',');
            switch (name)
            {
                case "move":
                    Move(battle, battle.Slots[int.Parse(n[0])]!, float.Parse(n[1], CultureInfo.InvariantCulture), float.Parse(n[2], CultureInfo.InvariantCulture));
                    break;
                case "stamina":
                    var stats = battle.Slots[int.Parse(n[0])]!.Sheet.Stats;
                    SetStat(stats, Stat.Stamina, stats.Trained(Stat.Stamina), int.Parse(n[1]), stats.Base(Stat.Stamina), int.Parse(n[1]));
                    break;
                case "danger":
                    battle.Danger = int.Parse(n[0]);
                    break;
                case "effect":
                    var on = battle.Slots[int.Parse(n[0])]!;
                    var spell = Set.All<SpellRecord>(RecordTypes.Spell).Single(s => s.Position == int.Parse(n[1]));
                    on.Sheet.AddEffect(spell.Id, spell.Effects ?? [], int.Parse(n[2]), spell.School, int.Parse(n[4]), uint.Parse(n[3]), on, battle, null);
                    break;
                case "radius":
                    radius = int.Parse(n[0]);
                    break;
                case "aibytes":
                    foreach (var pair in n)
                    {
                        var (offset, value) = (int.Parse(pair[..pair.IndexOf('=')]), int.Parse(pair[(pair.IndexOf('=') + 1)..]));
                        switch (offset)
                        {
                            case 4: c.Ai!.Kind = (AiKind)value; break;
                            case 7: c.Ai!.Wanted = (AiKind)value; break;
                            case >= 10 and < 18 when value == 0xFF: c.Ai!.Slots[offset - 10] = null; break;
                            default: throw new InvalidDataException($"AI byte {offset} = {value}");
                        }
                    }
                    break;
                case "spellsel":
                    c.Spell = int.Parse(n[0]) == 0 ? SpellChoice.None : new SpellChoice((SpellSourceKind)int.Parse(n[0]), int.Parse(n[1]));
                    break;
                case "daypart":
                    built.Game.Weather!.PartOfDay = (PartOfDay)int.Parse(n[0]);
                    break;
                default:
                    throw new InvalidDataException(mod);
            }
        }
        c.UpdateRadius(battle);
        if (radius is { } r)
            c.Radius = r;

        built.Game.Random.Combat.Seed = built.Cp.ReadWord(RandomState);
        // The action's target (+0x14) as the turn before left it, which the state machine reads.
        Poke(battle, nameof(Battle.Target), (int)(sbyte)built.Cp.ReadByte(built.Struct + 0x14));
        battle.Current = c;
        c.SyncFromBody(battle);
        battle.FocusAt(c.Cell);
        Poke(battle, nameof(Battle.State), CombatState.AI);
        Poke(battle, nameof(Battle.NextState), CombatState.AI);
        Poke(battle, nameof(Battle.Stuck), 0);
        // The sets' cursors as the checkpoint left them, which their rebuilding keeps where it can (TargetSet.RestoreCursor).
        Poke(battle.TargetsA, nameof(TargetSet.Cursor), (int)built.Cp.ReadByte(built.Struct + 0x4E64 + 0xA6));
        Poke(battle.TargetsB, nameof(TargetSet.Cursor), (int)built.Cp.ReadByte(built.Struct + 0x4F0C + 0xA6));
        battle.RebuildTargets(c);
        return (built, c);
    }

    /// <summary>A combatant and its body moved to a place, the grid following.</summary>
    private static void Move(Battle battle, Combatant c, float x, float z)
    {
        battle.Grid.Vacate(ArenaGrid.CellOf(c.Cell.X), ArenaGrid.CellOf(c.Cell.Y), c.Footprint);
        c.Cell = c.TurnStart = new Vector2(x, z);
        c.Body.Sphere.Position = c.Body.Sphere.Position with { X = battle.Origin.X + x, Z = battle.Origin.Z + z };
        c.ReadFrom = c.Body.Sphere.Position;
        battle.Grid.Occupy(ArenaGrid.CellOf(x), ArenaGrid.CellOf(z), c.Footprint);
        c.UpdateRadius(battle);
    }

    private static readonly MethodInfo ScoreSpellCells = typeof(Battle).GetMethod("ScoreSpellCells", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo ChooseSpellOrAttack = typeof(Battle).GetMethod("ChooseSpellOrAttack", BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static readonly MethodInfo StepState = typeof(Battle).GetMethod("Step", BindingFlags.NonPublic | BindingFlags.Instance, [])!;
    private static readonly MethodInfo RunStyle = typeof(Battle).GetMethod("RunStyle", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>A list of scores as the vectors hash it: "score,x,z,slot,combatant;" for each entry, in order.</summary>
    private static (int Count, ulong Hash, string Text) Hash(object scores)
    {
        var entries = (System.Collections.IEnumerable)scores.GetType().GetProperty("Entries")!.GetValue(scores)!;
        var text = new StringBuilder();
        var count = 0;
        foreach (var e in entries)
        {
            var t = e.GetType();
            var who = (Combatant?)t.GetProperty("Combatant")!.GetValue(e);
            text.Append(CultureInfo.InvariantCulture, $"{t.GetProperty("Score")!.GetValue(e)},{t.GetProperty("X")!.GetValue(e)},{t.GetProperty("Z")!.GetValue(e)},{t.GetProperty("Slot")!.GetValue(e)},{who?.Slot ?? -1};");
            count++;
        }
        var hash = 0xcbf29ce484222325ul;
        foreach (var b in Encoding.UTF8.GetBytes(text.ToString()))
            hash = (hash ^ b) * 0x100000001b3ul;
        return (count, hash, text.ToString());
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheCastersScoresAndChoiceAreTheOriginals(string id)
    {
        var k = Find(id);
        var (built, c) = Begin(k);
        var (battle, ai) = (built.Battle, c.Ai!);
        Assert.Equal(k.Radius, c.Radius);
        var (spell, focus) = (c.Spell, battle.AiAreaCentre);

        var raw = Hash(ScoreSpellCells.Invoke(battle, [ai])!);
        output.WriteLine($"raw {raw.Count}: {raw.Text[..Math.Min(raw.Text.Length, 2000)]}");
        if (Environment.GetEnvironmentVariable("HBHT_CASTER_DUMP") is { } dump)
            File.WriteAllText(Path.Combine(dump, $"{id}.raw.txt"), raw.Text);
        Assert.Equal((k.RawCount, k.RawHash), (raw.Count, raw.Hash));

        foreach (var seed in k.Seeds)
        {
            (c.Spell, battle.AiAreaCentre, ai.Target) = (spell, focus, -1);
            built.Game.Random.Sort.Seed = seed.SortSeed;
            var chosen = ChooseSpellOrAttack.Invoke(battle, [ai]);
            var list = chosen is null ? (0, 0xcbf29ce484222325ul, "") : Hash(chosen);
            Assert.Equal((seed.Returned, seed.ChooseCount, seed.ChooseHash, seed.ChooseSortAfter),
                (chosen is not null, list.Item1, list.Item2, built.Game.Random.Sort.Seed));
            if (seed.Run is not { } run)
                continue;
            (c.Spell, battle.AiAreaCentre, ai.Target) = (spell, focus, -1);
            built.Game.Random.Sort.Seed = seed.SortSeed;
            var goal = ((int X, int Z))RunStyle.Invoke(battle, [ai])!;
            Assert.Equal(run, new CasterVectors.Run(goal.X, goal.Z, ai.Target, (int)c.Spell.Source, c.Spell.Index, (int)ai.Kind, (int)ai.Wanted,
                built.Game.Random.Sort.Seed));
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void TheCastersTurnIsTheOriginals(string id)
    {
        var k = Find(id);
        foreach (var seed in k.Seeds)
        {
            if (seed.Step is not { } step)
                continue;
            var (built, c) = Begin(k);
            var battle = built.Battle;
            built.Game.Random.Sort.Seed = seed.SortSeed;
            battle.StepAI(c);
            Compare("after Step", step.After, built, c);

            // The leg run to its end, then the battle ticked until the action starts or the caster rests.
            if (step.Arrival is { } arrival)
            {
                c.Body.ClearPath();
                c.Body.Sphere.Position = c.Body.Sphere.Position with { X = battle.Origin.X + arrival.X, Z = battle.Origin.Z + arrival.Y };
            }
            // Only the state machine is stepped (Battle.Step), as the research's drive does: the bodies stay where they're put.
            for (var i = 0; i < 600 && battle.State is not (CombatState.Action or CombatState.Rest); i++)
                StepState.Invoke(battle, []);
            Compare("the action's start", step.Final, built, c);
        }
    }

    private void Compare(string when, CasterVectors.Snapshot expected, Built built, Combatant c)
    {
        var (battle, ai) = (built.Battle, c.Ai!);
        var path = string.Join(' ', battle.PathOf(ai).Select(p => $"{p.X},{p.Z}"));
        var actual = expected with
        {
            State = (int)battle.State,
            Next = (int)battle.NextState,
            Target = battle.Target,
            Path = path,
            AiTarget = ai.Target,
            SpellSource = (int)c.Spell.Source,
            SpellPlace = c.Spell.IsNone ? expected.SpellPlace : c.Spell.Index,
            Kind = (int)ai.Kind,
            Wanted = (int)ai.Wanted,
            Margin = c.CastMargin,
            SelectedA = Mask(battle.TargetsA),
            SelectedB = Mask(battle.TargetsB),
            Rng = built.Game.Random.Combat.Seed,
            SortRng = built.Game.Random.Sort.Seed,
            X = c.Cell.X,
            Z = c.Cell.Y,
            FocusX = battle.AiAreaCentre.X,
            FocusZ = battle.AiAreaCentre.Y,
        };
        output.WriteLine($"{when}: {actual}");
        Assert.Equal(expected with { X = 0, Z = 0, FocusX = 0, FocusZ = 0 }, actual with { X = 0, Z = 0, FocusX = 0, FocusZ = 0 });
        Assert.Equal(expected.X, actual.X, 0.01f);
        Assert.Equal(expected.Z, actual.Z, 0.01f);
        Assert.Equal(expected.FocusX, actual.FocusX, 0.01f);
        Assert.Equal(expected.FocusZ, actual.FocusZ, 0.01f);

        static int Mask(TargetSet set) => set.SelectedSlots.Sum(s => 1 << s);
    }
}
