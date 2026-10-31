using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using HBHT.Core;
using HBHT.Core.Chunks;
using HBHT.Core.Combat;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Rig;

using static HBHT.Rom.Tests.CombatCheckpoints;

namespace HBHT.Rom.Tests;

/// <summary>
/// Combat parity's replay (roadmap 2.9): the battles the user fought in the original under <c>tools/parity.ps1</c>, played again in
/// the port on the original's draws. The battle is built from the RAM at <c>Combat.Setup</c>, its combat stream set before each
/// draw to where the original's one stream was before its draw of the same rank, the cosmetic draws skipped, and checked against
/// the RAM at the first turn; and each turn's first action carried out from the RAM at it and checked against the next turn's. The
/// tests skip without logs in <c>out/parity</c> (or <c>HBHT_PARITY</c>).
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed partial class ParityReplayTests(Na10Import fixture, ITestOutputHelper output)
{
    private const uint SetupBlock = 0x800D9456, EnemyList = 0x800D9458, EncounterFlag = 0x800D9472, CollisionByte = 0x800D947E,
        ArenaByte = 0x800D947F, AniByte = 0x800D9480, FleeByte = 0x800D9481;

    private PluginSet Set => fixture.Data.Set;

    /// <summary>The battles logged, as paths under the logs' folder, each with its <c>Combat.Setup</c>.</summary>
    public static TheoryData<string> Battles()
    {
        var data = new TheoryData<string>();
        if (Directory.Exists(LogFolder))
        {
            foreach (var log in Directory.EnumerateFiles(LogFolder, "log.jsonl", SearchOption.AllDirectories).Order())
            {
                if (File.ReadLines(log).Any(l => l.Contains("\"Combat.Setup\"", StringComparison.Ordinal)))
                    data.Add(Path.GetRelativePath(LogFolder, Path.GetDirectoryName(log)!));
            }
        }
        if (data.Count == 0)
            data.Add("");
        return data;
    }

    private static string LogFolder => Environment.GetEnvironmentVariable("HBHT_PARITY") ?? Path.Combine(TestRoms.RepoRoot(), "out", "parity");

    private static ParityLog Log(string battle)
    {
        if (battle.Length == 0)
            Assert.Skip($"No battles logged in {LogFolder}.");
        return ParityLog.Read(Path.Combine(LogFolder, battle));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // The original's code by name (tools/symbols/us.txt), for which draws are cosmetic

    private static readonly Lazy<(uint[] Starts, string[] Names)> Symbols = new(() =>
    {
        var found = File.ReadLines(Path.Combine(TestRoms.RepoRoot(), "tools", "symbols", "us.txt"))
            .Select(l => SymbolLine().Match(l)).Where(m => m.Success)
            .Select(m => (At: Convert.ToUInt32(m.Groups[2].Value, 16), Name: m.Groups[1].Value))
            .Where(s => s.At < 0x800D0000).OrderBy(s => s.At).ToArray();
        return ([.. found.Select(s => s.At)], [.. found.Select(s => s.Name)]);
    });

    [GeneratedRegex(@"^(\S+) = 0x([0-9a-fA-F]+);")]
    private static partial Regex SymbolLine();

    /// <summary>The function an address is in, by the nearest name at or before it.</summary>
    private static string Name(uint at)
    {
        var (starts, names) = Symbols.Value;
        var i = Array.BinarySearch(starts, at);
        return i >= 0 ? names[i] : ~i > 0 ? names[~i - 1] : $"{at:x8}";
    }

    /// <summary>A draw's callers, the stop first, without the generator's own functions.</summary>
    private static IEnumerable<string> Callers(ParityDraw draw) =>
        draw.Chain.Select((a, i) => Name(i == 0 ? a : a - 4)).Where(n => !n.StartsWith("Rng.", StringComparison.Ordinal)
            && !n.StartsWith("RandomExtensions.", StringComparison.Ordinal) && n != "__udivdi3");

    /// <summary>What draws on the original's stream for show, which the port draws on its cosmetic stream or not at all.</summary>
    private static readonly string[] CosmeticCode =
        ["Weather.", "ParticleHead.", "AudioKeys.", "AllocPlayerAudio", "CombatMarkers.", "RenderSky", "Sky.", "CombatSpellFx."];

    private static bool Cosmetic(ParityDraw draw) =>
        Callers(draw).Any(n => CosmeticCode.Any(c => n.StartsWith(c, StringComparison.Ordinal)));

    // ---------------------------------------------------------------------------------------------------------------------------
    // The battle, built from the RAM at Combat.Setup

    private sealed record Replay(Built Built, ParityLog Log, Feed Feed)
    {
        public Battle Battle => Built.Battle;

        public GameState Game => Built.Game;
    }

    /// <summary>
    /// The original's draws of the rules in order, fed to the port's combat stream: before its <c>n</c>th draw, the stream is
    /// set to where the original's was before its <c>n</c>th. Each of the port's draws is kept with where the port drew it. The
    /// ring's (<see cref="CombatMarkers.Ground"/>), which the port draws on the combat stream and the original's are cosmetic, take
    /// none of the original's.
    /// </summary>
    private sealed class Feed
    {
        private readonly IReadOnlyList<ParityDraw> _draws;

        public Feed(IReadOnlyList<ParityDraw> draws, Lcg stream)
        {
            _draws = draws;
            stream.BeforeDraw = Before;
        }

        public List<(ParityDraw? Original, string Port)> Drawn { get; } = [];

        /// <summary>The port's draws for the rules.</summary>
        public int Count => Drawn.Count(d => !d.Port.StartsWith(nameof(CombatMarkers), StringComparison.Ordinal));

        private int _next;

        private void Before(Lcg stream)
        {
            var caller = PortCaller();
            if (caller.StartsWith(nameof(CombatMarkers), StringComparison.Ordinal))
            {
                Drawn.Add((null, caller));
                return;
            }
            var original = _next < _draws.Count ? _draws[_next] : null;
            _next++;
            if (original is not null)
                stream.Seed = original.Old;
            Drawn.Add((original, caller));
        }

        /// <summary>The port's first method outside the generator that drew.</summary>
        private static string PortCaller()
        {
            var frames = new StackTrace(2, false).GetFrames().Select(f => f.GetMethod()).OfType<System.Reflection.MethodBase>()
                .Where(m => m.DeclaringType is { } t && t != typeof(Lcg) && t != typeof(RandomExtensions) && t != typeof(Feed));
            return string.Join(" < ", frames.Take(3).Select(m => $"{m.DeclaringType!.Name}.{m.Name}"));
        }
    }

    private Replay Build(string battle, ParityLog? read = null)
    {
        var log = read ?? Log(battle);
        var setupEntry = log.Entries.First(e => e.Name == "Combat.Setup");
        var ram = log.Ram(setupEntry);
        Assert.Equal(1, ram.ReadByte(SetupBlock));

        var game = GameState.Load(Set, CheckpointSave.Convert(ram, Set, $"parity {battle}"));
        Assert.Empty(game.Problems);

        // The setup block, as every source fills it (combat.md's In and out).
        var enemies = Enumerable.Range(0, Battle.MaxEnemies).Select(i => Half(ram, EnemyList + (uint)i * 2)).TakeWhile(e => e != 0)
            .Select(e => new PartyEnemy(RecordId.Hex(RecordTypes.Entity, e), 1, 1)).ToList();
        var ambush = ram.ReadByte(CollisionByte) == 2;
        var flag = Half(ram, EncounterFlag);
        var setup = new BattleSetup(ambush ? BattleSource.Wanderer : BattleSource.MonsterParty, null, null, null, enemies,
            Arena: ram.ReadByte(ArenaByte), Ambush: ambush, CanFlee: ram.ReadByte(FleeByte) != 0,
            EncounterFlag: flag == 0 ? null : RecordId.Decimal(RecordTypes.Flag, flag), CollisionByte: 0, AniByte: ram.ReadByte(AniByte));

        var arena = Arena.For(Set, setup.Arena!.Value)!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), Set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);

        // The rules' draws from Combat.Setup on (the setup block's own were drawn in trek).
        var setupAt = At([.. log.Records], setupEntry);
        var rules = log.Records.Skip(setupAt).OfType<ParityDraw>().Where(d => !Cosmetic(d)).ToList();
        var feed = new Feed(rules, game.Random.Combat);
        game.Random.Sort.Seed = ram.ReadWord(SortState);

        return new Replay(new Built(Battle.Begin(game, setup, arena, streamer.World), game, streamer, ram), log, feed);
    }

    /// <summary>Where a record is in the log: by reference, as records compare by value.</summary>
    private static int At(List<ParityRecord> records, ParityRecord record) => records.FindIndex(r => ReferenceEquals(r, record));

    /// <summary>The original's draws of the rules between an entry point and the next (but a setup window's).</summary>
    private static List<ParityDraw> RulesDrawsAfter(ParityLog log, ParityEntry entry)
    {
        var records = log.Records.ToList();
        return [.. records.Skip(At(records, entry) + 1).TakeWhile(r => r is not ParityEntry).OfType<ParityDraw>().Where(d => !Cosmetic(d))];
    }

    private void Show(Feed feed, int from, int count)
    {
        foreach (var (original, port) in feed.Drawn.Where(d => !d.Port.StartsWith(nameof(CombatMarkers), StringComparison.Ordinal)).Skip(from).Take(count))
            output.WriteLine($"  {(original is null ? "(none)" : string.Join(" < ", Callers(original)))}  |  {port}");
    }

    [Theory]
    [MemberData(nameof(Battles))]
    public void TheSetupDrawsAsManyAsTheOriginals(string battle)
    {
        var replay = Build(battle);
        Assert.Empty(replay.Battle.Problems);
        var setupEntry = replay.Log.Entries.First(e => e.Name == "Combat.Setup");
        var original = RulesDrawsAfter(replay.Log, setupEntry);
        output.WriteLine($"{battle}: the original drew {original.Count} for the rules from Combat.Setup to the next entry point, the port {replay.Feed.Count}");
        Show(replay.Feed, 0, Math.Max(original.Count, replay.Feed.Count));
        Assert.Equal(original.Count, replay.Feed.Count);
    }

    [Theory]
    [MemberData(nameof(Battles))]
    public void TheFirstTurnIsTheOriginals(string battle)
    {
        var replay = Build(battle);
        var (log, battleState) = (replay.Log, replay.Battle);

        // The RAM at the first turn: Combat.StartFirstTurn's, or failing that (an older log) the first turn's end, with the first
        // combatant's own changes left out.
        var first = log.Entries.FirstOrDefault(e => e.Name == "Combat.StartFirstTurn" && e.Dump is not null)
            ?? log.Entries.First(e => e.Name == "TurnOrder.BeginTurn" && e.Dump is not null);
        var ram = log.Ram(first);
        var combat = ram.ReadWord(CombatPointer);
        var wrong = new List<string>();
        var acted = first.Name == "Combat.StartFirstTurn" ? -1
            : log.Entries.TakeWhile(e => e != first).FirstOrDefault(e => e.Name == "Combatant.ExecuteAction")?.Current ?? -1;
        Check("first turn's", acted < 0 ? battleState.Current?.Slot : acted, battleState.Current?.Slot);
        output.WriteLine($"{battle}: checked against {first.Name}'s RAM ({first.Dump}){(acted >= 0 ? $", slot {acted}'s turn left out" : "")}");

        var order = combat + 0x30;
        var turns = Enumerable.Range(0, ram.ReadByte(order)).Select(i => (int)ram.ReadByte(ram.ReadWord(order + 8) + (uint)i)).ToList();
        Check("turn order", string.Join(",", turns), string.Join(",", battleState.Order.Turns));
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var p = ram.ReadWord(combat + 0x52D0 + (uint)slot * 4);
            var port = battleState.Slots[slot];
            if ((p == 0) != (port is null))
            {
                wrong.Add($"slot {slot}: the original {(p == 0 ? "has no one" : "has a combatant")}, the port {(port is null ? "no one" : port.Sheet.Name)}");
                continue;
            }
            if (port is null || slot == acted)
                continue;
            var who = $"slot {slot} ({port.Sheet.Name})";
            // After the first turn, the bodies it touched have settled their cells a little (a rat hit by Solar Wrath, by 0.03).
            var cell = new Vector2(Float(ram, p), Float(ram, p + 4));
            Check($"{who} cell", acted < 0 ? cell : Vector2.Round(cell * 10) / 10, acted < 0 ? port.Cell : Vector2.Round(port.Cell * 10) / 10);
            Check($"{who} direction", (int)ram.ReadByte(p + 0x14), port.Direction);
            Check($"{who} footprint", (int)ram.ReadByte(p + 0x23), port.Footprint);
            Check($"{who} radius", (int)ram.ReadByte(p + 0x21), port.Radius);
        }
        foreach (var line in wrong)
            output.WriteLine($"  {line}");
        Assert.Empty(wrong);

        void Check<T>(string what, T original, T portValue)
        {
            if (!EqualityComparer<T>.Default.Equals(original, portValue))
                wrong.Add($"{what}: original {original}, port {portValue}");
        }
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Each action, from the RAM at its Combatant.ExecuteAction to the next turn's

    /// <summary>A combat struct's target sets (<c>+0x4E64</c>, <c>+0x4F0C</c>): 14 selected flags at <c>+0x48</c>, a word each.</summary>
    private const uint TargetSetA = 0x4E64, TargetSetB = 0x4F0C, TargetSelected = 0x48;

    /// <summary>
    /// Each turn's first action as the port carries it out from the original's state: the battle built from the setup with the RAM at
    /// the action's <c>Combatant.ExecuteAction</c> laid over it (<see cref="Overlay"/>), its spell, potion and targets as they were
    /// chosen, the original's draws from there to the next turn fed, and the port's <see cref="Battle.ExecuteAction"/> called with the
    /// original's attacker, target and kind. Then the turn plays out, and the next turn's start is checked against its RAM: every
    /// combatant's stats and skills, the dead, whose turn it is, and the draws' count. Bodies' places and the grid are shown, not checked.
    /// </summary>
    [Theory]
    [MemberData(nameof(Battles))]
    public void EachActionIsTheOriginals(string battle)
    {
        var log = Log(battle);
        var records = log.Records.ToList();
        var actions = log.Entries.Where(e => e.Name == "Combatant.ExecuteAction" && e.Dump is not null).ToList();
        if (actions.Count == 0)
            Assert.Skip($"{battle} has no RAM at its actions: a log from before they were dumped.");
        var wrong = new List<string>();
        foreach (var action in actions)
        {
            var next = records.Skip(At(records, action) + 1).OfType<ParityEntry>()
                .FirstOrDefault(e => e.Name is "TurnOrder.BeginTurn" or "Combat.Exit" && e.Dump is not null);
            if (next is null)
                continue;
            wrong.AddRange(ReplayAction(battle, log, action, next).Select(w => $"slot {action.Current}'s action ({action.Dump}): {w}"));
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    private List<string> ReplayAction(string battle, ParityLog log, ParityEntry action, ParityEntry next)
    {
        var replay = Build(battle, log);
        var game = replay.Game;
        var ram = log.Ram(action);
        var built = replay.Built with { Cp = ram };
        var fight = built.Battle;
        var wrong = new List<string>();

        // Reserves that have joined in the original haven't in the port's battle, built afresh.
        var joined = Enumerable.Range(0, Battle.SlotCount).Where(slot => built.Record(slot) != 0 && fight.Slots[slot] is null).ToList();
        if (joined.Count > 0)
        {
            output.WriteLine($"{action.Dump}: slots {string.Join(", ", joined)} joined as reserves, which the replay doesn't lay over yet");
            return [];
        }
        Overlay(built);

        // Who, at whom and how (a0 to a2): a spell's kind is 0, a blow's or a potion's 1.
        int SlotOf(Built b, uint record) => Enumerable.Range(0, Battle.SlotCount).First(slot => b.Record(slot) == record);
        var (a, t) = (built[SlotOf(built, action.Args[0])], built[SlotOf(built, action.Args[1])]);
        var spell = action.Args[2] == 0;
        var p = built.Record(a.Slot);
        var sheet = ram.ReadWord(p + 0x40);
        a.CastMargin = (sbyte)ram.ReadByte(sheet + 0x3C);
        a.Spell = new SpellChoice((SpellSourceKind)ram.ReadByte(sheet + 0x3D), ram.ReadByte(sheet + 0x3E));
        if (a.Action == CombatAction.Flask)
        {
            // The potion's kind (+0x27), its item 0x1000 on.
            var potion = game.Items.TryGet(RecordId.Hex(RecordTypes.Item, 0x1000 + ram.ReadByte(p + 0x27)));
            Assert.NotNull(potion);
            a.HoldPotion(potion, fight);
        }
        foreach (var (set, at) in new[] { (fight.TargetsA, TargetSetA), (fight.TargetsB, TargetSetB) })
        {
            set.Rebuild(fight, a);
            set.ClearSelection();
            for (var slot = 0; slot < Battle.SlotCount; slot++)
            {
                if (ram.ReadWord(built.Struct + at + TargetSelected + (uint)slot * 4) != 0 && !set.Select(slot))
                    wrong.Add($"the port's target set {(set == fight.TargetsB ? "B" : "A")} can't select slot {slot}");
            }
        }
        for (var i = 0; i < 120 && fight.Fade.Fading; i++)
            fight.Fade.Step();

        // The original's draws from here to the next turn, and the action.
        var records = log.Records.ToList();
        var draws = records.Skip(At(records, action) + 1).TakeWhile(r => !ReferenceEquals(r, next)).OfType<ParityDraw>().Where(d => !Cosmetic(d)).ToList();
        var feed = new Feed(draws, game.Random.Combat);
        var what = spell ? $"casts {a.CurrentSpell(game.Rules.Magic)?.Spell.Name}" : a.Action == CombatAction.Flask ? $"uses the {a.Potion?.Name}" : a.Action.ToString();

        // The facing state's (Combat.StateFacing) target, which the action's states read.
        Poke(fight, nameof(Battle.Target), t.Slot);
        fight.ExecuteAction(a, t, spell);
        var zone = Zone(built);
        var states = new List<(CombatState State, int Ticks)>();
        for (var i = 0; i < 20000 && fight.Current == a && !fight.Over; i++)
        {
            fight.Tick(zone, false, _ => null);
            if (states.Count == 0 || states[^1].State != fight.State)
                states.Add((fight.State, 0));
            states[^1] = (fight.State, states[^1].Ticks + 1);
        }
        output.WriteLine($"  states: {string.Join(", ", states.Take(12).Select(st => $"{st.State} {st.Ticks}"))}");

        var then = built with { Cp = log.Ram(next) };
        var current = then.Cp.ReadWord(then.Struct + 0x2C);
        int? nextSlot = Enumerable.Range(0, Battle.SlotCount).Where(slot => current != 0 && then.Record(slot) == current).Cast<int?>().FirstOrDefault();
        output.WriteLine($"{action.Dump} -> {next.Dump}: slot {a.Slot} ({a.Sheet.Name}) {what} at slot {t.Slot} ({t.Sheet.Name}); "
            + $"the original drew {draws.Count}, the port {feed.Count}; next the original's slot {nextSlot}, the port's {fight.Current?.Slot}");
        if (feed.Count != draws.Count)
            Show(feed, 0, Math.Max(feed.Drawn.Count, draws.Count));
        if (feed.Count != draws.Count)
            wrong.Add($"the original drew {draws.Count}, the port {feed.Count}");
        if (nextSlot != fight.Current?.Slot)
            wrong.Add($"next is the original's slot {nextSlot}, the port's {fight.Current?.Slot}");
        foreach (var line in Verify(then))
        {
            if (line.Contains("body at", StringComparison.Ordinal) || line.Contains("grid cells", StringComparison.Ordinal))
                output.WriteLine($"  (shown) {line}");
            else
                wrong.Add(line);
        }
        foreach (var line in wrong)
            output.WriteLine($"  {line}");
        return wrong;
    }
}
