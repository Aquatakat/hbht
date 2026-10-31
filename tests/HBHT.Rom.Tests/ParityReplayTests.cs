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
/// the RAM at the first turn. The tests skip without logs in <c>out/parity</c> (or <c>HBHT_PARITY</c>).
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

    private sealed record Replay(Battle Battle, GameState Game, ParityLog Log, AresCheckpoint Setup, Feed Feed);

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

    private Replay Build(string battle)
    {
        var log = Log(battle);
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
        var setupAt = log.Records.ToList().IndexOf(setupEntry);
        var rules = log.Records.Skip(setupAt).OfType<ParityDraw>().Where(d => !Cosmetic(d)).ToList();
        var feed = new Feed(rules, game.Random.Combat);
        game.Random.Sort.Seed = ram.ReadWord(SortState);

        return new Replay(Battle.Begin(game, setup, arena, streamer.World), game, log, ram, feed);
    }

    /// <summary>The original's draws of the rules between an entry point and the next (but a setup window's).</summary>
    private static List<ParityDraw> RulesDrawsAfter(ParityLog log, ParityEntry entry)
    {
        var records = log.Records.ToList();
        return [.. records.Skip(records.IndexOf(entry) + 1).TakeWhile(r => r is not ParityEntry).OfType<ParityDraw>().Where(d => !Cosmetic(d))];
    }

    private void Show(Feed feed, int from, int count)
    {
        foreach (var (original, port) in feed.Drawn.Skip(from).Take(count))
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
}
