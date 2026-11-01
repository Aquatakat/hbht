using System.Diagnostics;
using System.Numerics;
using System.Text.RegularExpressions;
using HBHT.Core;
using HBHT.Core.Characters;
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
                if (ParityLog.Lines(log).Any(l => l.Contains("\"Combat.Setup\"", StringComparison.Ordinal)))
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
        ["Weather.", "ParticleHead.", "AudioKeys.", "AllocPlayerAudio", "CombatMarkers.", "RenderSky", "Sky.", "SkyObjects.", "CombatSpellFx."];

    private static bool Cosmetic(ParityDraw draw) =>
        Callers(draw).Any(n => CosmeticCode.Any(c => n.StartsWith(c, StringComparison.Ordinal)));

    /// <summary>
    /// Whether a draw is the ring's (<c>CombatMarkers.RingPoint</c>). It retries by calling itself, so a deep retry's callers are
    /// all <c>RingPoint</c>, with no sign of the turn it's laid out for.
    /// </summary>
    private static bool RingDraw(ParityDraw draw) => Callers(draw).Any(n => n.StartsWith("CombatMarkers.", StringComparison.Ordinal));

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
    /// ring's draws (<see cref="CombatMarkers.Ground"/>), which the port takes from its cosmetic stream, are fed the same way from
    /// the original's ring draws, and each of the cosmetic stream's is kept by where alone.
    /// </summary>
    private sealed class Feed
    {
        private readonly IReadOnlyList<ParityDraw> _draws, _ring;

        public Feed(IReadOnlyList<ParityDraw> draws, Lcg stream, Lcg? cosmetic = null, IReadOnlyList<ParityDraw>? ring = null)
        {
            (_draws, _ring) = (draws, ring ?? []);
            stream.BeforeDraw = Before;
            if (cosmetic is not null)
                cosmetic.BeforeDraw = BeforeCosmetic;
        }

        public List<(ParityDraw? Original, string Port)> Drawn { get; } = [];

        /// <summary>The port's draws for the rules.</summary>
        public int Count => Drawn.Count;

        /// <summary>Where the port drew on its cosmetic stream.</summary>
        public List<string> CosmeticDrawn { get; } = [];

        private int _next, _nextRing;

        private void BeforeCosmetic(Lcg stream)
        {
            var caller = PortCaller();
            if (caller.StartsWith(nameof(CombatMarkers), StringComparison.Ordinal) && _nextRing < _ring.Count)
                stream.Seed = _ring[_nextRing++].Old;
            CosmeticDrawn.Add(caller);
        }

        private void Before(Lcg stream)
        {
            var caller = PortCaller();
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
            EncounterFlag: flag == 0 ? null : RecordId.Decimal(RecordTypes.Flag, flag), CollisionByte: ram.ReadByte(CollisionByte),
            AniByte: ram.ReadByte(AniByte));

        var arena = Arena.For(Set, setup.Arena!.Value)!;
        Assert.NotNull(arena);
        var streamer = new ChunkStreamer(Set, game, new CollisionWorld(), Set.Get<MapSetRecord>(Arena.MapSet));
        streamer.Update(arena.Origin + Battle.ZonePoint);

        // The rules' draws from Combat.Setup on (the setup block's own were drawn in trek).
        var setupAt = At([.. log.Records], setupEntry);
        var rules = log.Records.Skip(setupAt).OfType<ParityDraw>().Where(d => !Cosmetic(d)).ToList();
        var ring = log.Records.Skip(setupAt).OfType<ParityDraw>().Where(RingDraw).ToList();
        var feed = new Feed(rules, game.Random.Combat, game.Random.Cosmetic, ring);
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
            // After the first turn, the bodies it touched have settled their cells a little (a rat hit by Solar Wrath, by 0.03). Before
            // it, a body set down on the floor can have moved by float noise, its cell read back from it (setup 7's second session:
            // 1.0's Dust Devil at 31.999998 for its spawn's 32).
            var cell = new Vector2(Float(ram, p), Float(ram, p + 4));
            var places = acted < 0 ? 10000 : 10;
            Check($"{who} cell", Vector2.Round(cell * places) / places, Vector2.Round(port.Cell * places) / places);
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

    /// <summary>
    /// The first turn's ring (<c>CombatMarkers.Update</c> from <c>Combat.StartFirstTurn</c>), on the original's ring draws: the port
    /// draws as many for it, and its teardrops stand where the original's do in the first RAM dumped after it, which holds it until
    /// the next turn lays its own. In the first trial, Sholeh's ring of 20 misses the ground under its 12th and 13th on every sweep,
    /// four retries of two draws each, in both.
    /// </summary>
    [Theory]
    [MemberData(nameof(Battles))]
    public void TheFirstRingIsTheOriginals(string battle)
    {
        var replay = Build(battle);
        var (log, fight) = (replay.Log, replay.Battle);
        var records = log.Records.ToList();
        var from = At(records, log.Entries.First(e => e.Name == "Combat.Setup"));
        var after = records.Skip(from + 1).OfType<ParityEntry>().ToList();
        var next = after.FirstOrDefault(e => e.Name is "TurnOrder.BeginTurn" or "Combat.Exit");
        var held = after.FirstOrDefault(e => e.Dump is not null && e.Name != "Combat.StartFirstTurn");
        if (next is null || held is null)
            Assert.Skip($"{battle} has no RAM dumped after its first ring.");

        var original = records.Skip(from + 1).TakeWhile(r => !ReferenceEquals(r, next)).OfType<ParityDraw>().Count(RingDraw);
        var port = replay.Feed.CosmeticDrawn.Count(c => c.StartsWith(nameof(CombatMarkers), StringComparison.Ordinal));
        output.WriteLine($"{battle}: {fight.Current?.Sheet.Name}'s ring of {fight.Ring.Count}, radius {fight.Ring.RingRadius}; the original drew {original} for it, the port {port}; checked against {held.Name}'s RAM ({held.Dump})");
        Assert.Equal(original, port);

        var theirs = Ring(log.Ram(held));
        // 1.0 keeps the direction it turns a ring's teardrops by from ring to ring, from boot, and a ring's steps of
        // 360 / n × 0.017453 fall 0.006° short of a turn, so each ring it lays starts that far back (setup 10's second battle: 9 rings
        // before it, 0.054°). The port starts every ring straight ahead, as the drift can't be seen.
        var centre = fight.Origin + new Vector3(fight.Current!.TurnStart.X, 0, fight.Current.TurnStart.Y);
        var (sin, cos) = MathF.SinCos(RingsBefore(battle) * (360 * 0.017453f - 2 * MathF.PI));
        var ours = fight.Ring.Points.ToArray().Select(p => p - centre)
            .Select(d => centre + new Vector3(cos * d.X - sin * d.Z, d.Y, cos * d.Z + sin * d.X) - fight.Origin).ToList();
        Assert.Equal(theirs.Count, ours.Count);
        var off = theirs.Zip(ours).Select((p, i) => (p, i)).Where(t => Vector3.Distance(t.p.First, t.p.Second) > 0.01f)
            .Select(t => $"teardrop {t.i}: original {t.p.First}, port {t.p.Second}").ToList();
        Assert.True(off.Count == 0, string.Join("; ", off));
    }

    /// <summary>The rings 1.0 laid in the session before a battle: each earlier battle's first turn and every turn begun after it.</summary>
    private static int RingsBefore(string battle)
    {
        var session = Path.GetDirectoryName(battle)!;
        return Directory.EnumerateDirectories(Path.Combine(LogFolder, session), "battle-*")
            .Where(b => string.CompareOrdinal(Path.GetFileName(b), Path.GetFileName(battle)) < 0)
            .Sum(b => ParityLog.Read(b).Entries.Count(e => e.Name is "Combat.StartFirstTurn" or "TurnOrder.BeginTurn"));
    }

    // ---------------------------------------------------------------------------------------------------------------------------
    // Each action, from the RAM at its Combatant.ExecuteAction to the next turn's, and each AI's turn from its start

    /// <summary>A combat struct's target sets (<c>+0x4E64</c>, <c>+0x4F0C</c>): 14 selected flags at <c>+0x48</c>, a word each.</summary>
    private const uint TargetSetA = 0x4E64, TargetSetB = 0x4F0C, TargetSelected = 0x48;

    /// <summary>
    /// Each turn's first action as the port carries it out from the original's state: the battle built from the setup with the RAM at
    /// the action's <c>Combatant.ExecuteAction</c> laid over it (<see cref="LayOver"/>), its spell, potion and targets as they were
    /// chosen, the original's draws from there to the next turn fed, and the port's <see cref="Battle.ExecuteAction"/> called with the
    /// original's attacker, target and kind. Then the turn plays out, and the next turn's start is checked (<see cref="CheckNext"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(Battles))]
    public void EachActionIsTheOriginals(string battle)
    {
        var log = Log(battle);
        var actions = log.Entries.Where(e => e.Name == "Combatant.ExecuteAction" && e.Dump is not null).ToList();
        if (actions.Count == 0)
            Assert.Skip($"{battle} has no RAM at its actions: a log from before they were dumped.");
        var wrong = new List<string>();
        foreach (var action in actions)
        {
            if (NextTurn(log, action) is not { } next)
                continue;
            var found = ReplayAction(battle, log, action, next).Select(w => $"slot {action.Current}'s action ({action.Dump}): {w}").ToList();
            if (!KnownFixes.TryGetValue((battle.Replace('\\', '/'), action.Dump!), out var why))
                wrong.AddRange(found);
            else if (found.Count == 0)
                wrong.Add($"slot {action.Current}'s action ({action.Dump}) is the original's now: take it off the known fixes");
            else
                output.WriteLine($"{action.Dump}: the port's fix, {why}:\n  {string.Join("\n  ", found)}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// Each enemy's turn as the port's AI plays it from the original's state: the RAM at its <c>TurnOrder.BeginTurn</c> laid over the
    /// battle, the original's draws from there to the next turn fed, and the port's turn begun as <c>TurnOrder.Advance</c> begins it.
    /// What the AI chose, if it acted, is checked against the original's <c>Combatant.ExecuteAction</c>: whom it went for, and where it
    /// stood (to 0.5, as the walks' steps differ); then the next turn's start (<see cref="CheckNext"/>), and the AI's own record.
    /// </summary>
    [Theory]
    [MemberData(nameof(Battles))]
    public void EachEnemyTurnIsTheOriginals(string battle)
    {
        var log = Log(battle);
        var turns = log.Entries.Where(e => e.Name == "TurnOrder.BeginTurn" && e.Dump is not null && e.Combatant is { } c
            && BitConverter.ToUInt32([.. c.AsSpan(0x44, 4).ToArray().Reverse()]) != 0).ToList();
        if (turns.Count == 0)
            Assert.Skip($"{battle} has no enemy's turn with RAM at its start.");
        var wrong = new List<string>();
        foreach (var turn in turns)
        {
            if (NextTurn(log, turn) is not { } next)
                continue;
            var found = ReplayEnemyTurn(battle, log, turn, next).Select(w => $"slot {turn.Current}'s turn ({turn.Dump}): {w}").ToList();
            if (!KnownCadence.TryGetValue((battle.Replace('\\', '/'), turn.Dump!), out var why)
                && !KnownFixes.TryGetValue((battle.Replace('\\', '/'), turn.Dump!), out why))
                wrong.AddRange(found);
            else if (found.Count == 0)
                wrong.Add($"slot {turn.Current}'s turn ({turn.Dump}) is the original's now: take it off the known cadence turns or fixes");
            else
                output.WriteLine($"{turn.Dump}: a known cadence turn or fix, {why}:\n  {string.Join("\n  ", found)}");
        }
        Assert.True(wrong.Count == 0, string.Join("\n", wrong));
    }

    /// <summary>
    /// The enemy turns that differ only by 1.0's frame lengths, by battle and dump ([combat parity](docs/systems/combat-parity.md#the-known-differences)): the
    /// logger slows 1.0 to frames of 3–6 ticks, and the port steps 2, so a body that sets off facing away curves another
    /// way, and a walk ends further past its arrival. The replay doesn't play 1.0's frame lengths; these are logged, not failed, and one
    /// that matches fails until it comes off the list. A turn goes on it only once its difference is shown to be the frame lengths'.
    /// </summary>
    private static readonly Dictionary<(string Battle, string Dump), string> KnownCadence = new()
    {
        [("20261031-123809-setup2/battle-01", "0018.rdram.gz")] = "the Sorceror curves away from its first corner and gives up, as the port's does at 4–6-tick frames",
        [("20261031-123809-setup2/battle-01", "0096.rdram.gz")] = "the Spellweaver decides 0.3 short of 1.0's place, Alaron just out of its reach",
        [("20261031-134011-setup2/battle-01", "0016.rdram.gz")] = "the Spellweaver curves away from its first corner, dead astern, and gives up near its start; the port's does at 4–6-tick frames, curving the other way",
        [("20261031-205206-setup6/battle-01", "0040.rdram.gz")] = "1.0's Wraith decides two frames after its walk arrives, the logger's 6-tick frames carrying it 0.63 on into Wraith Touch's reach of Dougal; the port's decides 2–3 ticks after, as 1.0's at 30 fps, out of reach",
        [("20261031-190448-setup4/battle-01", "0006.rdram.gz")] = "the Woodsman's first corner is dead astern: 1.0's walks 0.9 away in 6 frames of 5–6 ticks and gives up; the port's gives up within 0.37 of it at 5-tick frames",
        [("20261031-223128-setup7/battle-01", "0024.rdram.gz")] = "the charmed Air Elemental's walk at the logger's 4-tick frames ends 1.17 further on, in reach of the Dust Devil; the port's at 4-tick frames ends within 0.01 of it and strikes",
        [("20261031-230020-setup8/battle-01", "0018.rdram.gz")] = "the Giant Skeleton's walk ends 0.58 off 1.0's and out of reach of Dougal; at 3-tick frames the port's strikes him as 1.0's does",
        [("20261031-230020-setup8/battle-01", "0037.rdram.gz")] = "the Skeleton Archer faces 162° from its leg and swings wide through +Z as it turns, coming no nearer, and 1.0's body gives up after 11 frames; the port's turns round at 2-tick frames and walks on, and at 5-tick frames gives up and shoots from within 0.27 of 1.0's",
        [("20261101-083418-setup11/battle-01", "0009.rdram.gz")] = "the Hobgoblin's walk at Alaron ends 1.05 off 1.0's, out of reach, and it rests; at the logger's 4-tick frames the port's strikes him from within 0.08 of 1.0's",
        [("20261101-083418-setup11/battle-01", "0068.rdram.gz")] = "not the cadence but the animation: 1.0's Hobgoblin was mid-fidget (its actor's state 12, clock 27 of 101), which takes no run request, so its walk to (19, 10) gave up after 30 ticks on its cell; the replay starts bodies idle, and the port's walks to (19, 11.14). Seeded at state 12, clock 27, the port's acts from (19, 12) as 1.0's does",
        [("20261101-102351-setup13/battle-01", "0062.rdram.gz")] = "the player's A put the Gorgon at its leg's end, 2.25 from Sholeh for a reach of 2.14; 1.0's body ran on 0.34 toward her through the 4 frames of 3 ticks before it decided and struck from 1.78, the port's decides 2 ticks after and rests",
    };

    /// <summary>
    /// The actions and enemy turns where the port fixes one of 1.0's bugs or differs by the user's choice, by battle and dump
    /// ([combat parity](docs/systems/combat-parity.md#the-known-differences)). Their differences are logged, not failed, and one that matches fails
    /// until it comes off the list.
    /// </summary>
    private const string OwnFootprint = "the joining Hobgoblin's own 2-cell footprint no longer blocks its cast toward +X or +Z, so it casts the Tap Stamina it scored where 1.0's rested (ArenaGrid.WalkClear's own footprint)";

    private static readonly Dictionary<(string Battle, string Dump), string> KnownFixes = new()
    {
        [("20261031-172849-setup3/battle-01", "0097.rdram.gz")] = "Dougal's Fireball with a flask in hand does its damage",
        [("20261031-172849-setup3/battle-01", "0103.rdram.gz")] = "Dougal's Fireball with a flask in hand does its damage",
        [("20261031-172849-setup3/battle-01", "0113.rdram.gz")] = "Dougal's Fireball with a flask in hand does its damage",
        [("20261031-172849-setup3/battle-01", "0120.rdram.gz")] = "Dougal's Fireball with a flask in hand does its damage",
        [("20261031-201521-setup5b/battle-01", "0064.rdram.gz")] = "1.0's Poisoner sticks on Keelin's body by the push's rounding and gives up 0.24 from its start; the port's slides on round her (Crowd.Rounding), and with the slide off ends within 0.35 of 1.0's",
        [("20261031-230020-setup8/battle-01", "0016.rdram.gz")] = "the Skeleton slides on round Keelin (Crowd.Rounding) and strikes from 0.59 past 1.0's; with the slide off it strikes from within 0.38 of 1.0's",
        [("20261031-235130-setup10/battle-02", "0008.rdram.gz")] = "1.0's Troll sticks on the Troll in slot 3, exactly touching, 2.98 from Alaron and gives up without a blow; the port's slides on round it (Crowd.Rounding) and strikes him; with the slide off it stops within 0.07 of 1.0's and doesn't",
        [("20261101-083418-setup11/battle-01", "0044.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0056.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0087.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0094.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0100.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0110.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0115.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0117.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0118.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0125.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0132.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0134.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0135.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0143.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0144.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0145.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0152.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0154.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0160.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0165.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0168.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0172.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0178.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0184.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0187.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0190.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0198.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0199.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0207.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0211.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0222.rdram.gz")] = OwnFootprint,
        [("20261101-083418-setup11/battle-01", "0236.rdram.gz")] = OwnFootprint,
    };

    /// <summary>The next turn's start with RAM, or the battle's end.</summary>
    private static ParityEntry? NextTurn(ParityLog log, ParityEntry entry)
    {
        var records = log.Records.ToList();
        return records.Skip(At(records, entry) + 1).OfType<ParityEntry>()
            .FirstOrDefault(e => e.Name is "TurnOrder.BeginTurn" or "Combat.Exit" && e.Dump is not null);
    }

    /// <summary>The original's draws of the rules from an entry point to another.</summary>
    private static List<ParityDraw> DrawsBetween(ParityLog log, ParityEntry from, ParityEntry to)
    {
        var records = log.Records.ToList();
        return [.. records.Skip(At(records, from) + 1).TakeWhile(r => !ReferenceEquals(r, to) && !IsRewards(r)).OfType<ParityDraw>().Where(d => !Cosmetic(d))];
    }

    /// <summary>
    /// The original's draws for a won battle's rewards (<c>Combat.TallyRewards</c>) before <paramref name="to"/>, on its one stream,
    /// which the port rolls on its loot stream (ADR 0006): none for a turn that doesn't win.
    /// </summary>
    private static List<ParityDraw> LootBetween(ParityLog log, ParityEntry from, ParityEntry to)
    {
        var records = log.Records.ToList();
        return [.. records.Skip(At(records, from) + 1).TakeWhile(r => !ReferenceEquals(r, to)).SkipWhile(r => !IsRewards(r)).OfType<ParityDraw>().Where(d => !Cosmetic(d))];
    }

    private static bool IsRewards(object record) => record is ParityEntry { Name: "Combat.TallyRewards" };

    private List<string> CheckLoot(Feed feed, List<ParityDraw> draws)
    {
        if (feed.Count == draws.Count)
            return [];
        output.WriteLine($"  the rewards: the original drew {draws.Count}, the port's loot stream {feed.Count}");
        return [$"the rewards: the original drew {draws.Count}, the port's loot stream {feed.Count}"];
    }

    /// <summary>
    /// The battle built from the setup with an entry point's RAM laid over it: the reserves that have joined in the original first
    /// (<see cref="Join"/>), then <see cref="Overlay"/>.
    /// </summary>
    private (Replay Replay, Built Built) LayOver(string battle, ParityLog log, ParityEntry entry)
    {
        var replay = Build(battle, log);
        var built = replay.Built with { Cp = log.Ram(entry) };
        var joined = Join(built);
        if (joined.Count > 0)
            output.WriteLine($"{entry.Dump}: slots {string.Join(", ", joined)} joined as reserves");
        Overlay(built);
        return (replay, built);
    }

    /// <summary>
    /// The reserves in the original's RAM that the battle built afresh hasn't: each enemy slot where the original has a living
    /// combatant and the port none, or a dead or gone one, takes the original's entity as <c>Combat.SpawnReserves</c> brings it in
    /// (<see cref="Battle.SpawnReserves"/>), at the cell and direction the RAM has, which the overlay then lays over it. The slots.
    /// </summary>
    private static List<int> Join(Built built)
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
        target.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.Public)!.Invoke(target, args);

    /// <summary>A combatant's slot by its record's address in the RAM, or null.</summary>
    private static int? SlotOf(Built built, uint record) =>
        Enumerable.Range(0, Battle.SlotCount).Where(slot => record != 0 && built.Record(slot) == record).Cast<int?>().FirstOrDefault();

    /// <summary>
    /// Ticks until the combatant's turn has ended, the bodies moved by their animations' roots as the runtime's are, and checks the
    /// next turn as it begins: at the first line after <c>TurnOrder.Advance</c> has chosen it, its own (<c>TurnOrder.BeginTurn</c>), where
    /// the original's RAM was dumped, before its flee check and its ring. Failing a cue (a line the same as the last), after the tick.
    /// A combatant's next turn in a row is the next turn too.
    /// </summary>
    private List<string> PlayOut(Built built, Combatant c, bool toExit, Func<List<string>> check, Action? eachTick = null)
    {
        var fight = built.Battle;
        var zone = Zone(built);
        var motions = new Dictionary<Combatant, HBHT.Core.Actors.IActorMotion?>();
        HBHT.Core.Actors.IActorMotion? Motion(Combatant m) => motions.TryGetValue(m, out var found) ? found
            : motions[m] = HBHT.Core.Actors.ActorMotion.For(Set, Set.Get<ActorRecord>(m.Look.Actor), built.Game.Random.Animation);
        List<string>? checkedThen = null;
        // The turn, not the combatant: one with two turns in a row (Haste, or the order's grouping) begins its second at once.
        var turn = fight.Order.Now;
        // A battle won is checked at its Combat.Exit, after the rewards' draws: as the victory begins, not at the line of the death that won it.
        bool Ended() => toExit ? fight.State == CombatState.Victory || fight.Over : fight.Current != c || fight.Order.Now != turn;
        // A line, not just any cue: Advance passes over a combatant that can't act and ends the round with Current on it, and the
        // round's end cues its reserves and numbers before the next round is built.
        fight.Host = new Watch(cue =>
        {
            if (checkedThen is null && !toExit && Ended() && cue is LogLine)
                checkedThen = check();
        });
        var states = new List<(CombatState State, int Ticks)>();
        for (var i = 0; i < 20000 && !Ended() && !fight.Over; i++)
        {
            fight.Tick(zone, false, Motion);
            // Sense Aura's popup waits for a press, which ends the turn; the player's came before the next turn's RAM.
            if (fight.Aura is not null)
                fight.DismissAura();
            eachTick?.Invoke();
            if (states.Count == 0 || states[^1].State != fight.State)
                states.Add((fight.State, 0));
            states[^1] = (fight.State, states[^1].Ticks + 1);
        }
        fight.Host = null;
        output.WriteLine($"  states: {string.Join(", ", states.Take(12).Select(st => $"{st.State} {st.Ticks}"))}");
        if (checkedThen is null && !toExit)
            output.WriteLine("  (no cue as the next turn began: checked after its start)");
        return checkedThen ?? check();
    }

    private sealed class Watch(Action<CombatCue> cued) : ICombatHost
    {
        public void Cue(CombatCue cue) => cued(cue);
    }

    private List<string> ReplayAction(string battle, ParityLog log, ParityEntry action, ParityEntry next)
    {
        var (replay, built) = LayOver(battle, log, action);
        var (game, fight, ram) = (replay.Game, built.Battle, built.Cp);
        var wrong = new List<string>();

        // Who, at whom and how (a0 to a2): a spell's kind is 0, a blow's or a potion's 1.
        var (a, t) = (built[SlotOf(built, action.Args[0])!.Value], built[SlotOf(built, action.Args[1])!.Value]);
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
        // Rebuilding the sets tests reach, which reads the bodies into the combatants' cells; 1.0's records kept the cells they last read
        // (setup 5's 0061: Alaron's a cell from his body, which the blow's height difference reads), so they're put back after.
        var cells = fight.Combatants.Select(c => (c, c.Cell, c.ReadFrom)).ToList();
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
        foreach (var (c, cell, readFrom) in cells)
            (c.Cell, c.ReadFrom) = (cell, readFrom);
        for (var i = 0; i < 120 && fight.Fade.Fading; i++)
            fight.Fade.Step();
        var what = spell ? $"casts {a.CurrentSpell(game.Rules.Magic)?.Spell.Name}" : a.Action == CombatAction.Flask ? $"uses the {a.Potion?.Name}" : a.Action.ToString();

        // The original's draws from here to the next turn; the facing state's (Combat.StateFacing) target, which the action's states
        // read; and the action.
        var draws = DrawsBetween(log, action, next);
        var feed = new Feed(draws, game.Random.Combat);
        var loot = LootBetween(log, action, next);
        var lootFeed = new Feed(loot, game.Random.Loot);
        Poke(fight, nameof(Battle.Target), t.Slot);
        fight.ExecuteAction(a, t, spell);
        output.WriteLine($"{action.Dump} -> {next.Dump}: slot {a.Slot} ({a.Sheet.Name}) {what} at slot {t.Slot} ({t.Sheet.Name})");
        wrong.AddRange(PlayOut(built, a, next.Name == "Combat.Exit", () => [.. CheckNext(built, log, next, a, feed, draws), .. CheckLoot(lootFeed, loot)]));
        return wrong;
    }

    private List<string> ReplayEnemyTurn(string battle, ParityLog log, ParityEntry turn, ParityEntry next)
    {
        var (replay, built) = LayOver(battle, log, turn);
        var (game, fight) = (replay.Game, built.Battle);
        var c = built[turn.Current!.Value];
        Assert.Same(c, fight.Current);
        var wrong = new List<string>();
        for (var i = 0; i < 120 && fight.Fade.Fading; i++)
            fight.Fade.Step();

        // What the original did with its turn: its action, if it acted (the turn's first Combatant.ExecuteAction, with its RAM).
        var records = log.Records.ToList();
        var acted = records.Skip(At(records, turn) + 1).TakeWhile(r => !ReferenceEquals(r, next)).OfType<ParityEntry>()
            .FirstOrDefault(e => e.Name == "Combatant.ExecuteAction");
        var originalTarget = acted is null ? null : SlotOf(built with { Cp = acted.Dump is null ? built.Cp : log.Ram(acted) }, acted.Args[1]);
        var originalCell = acted?.Combatant is { } bytes ? new Vector2(BigFloat(bytes, 0), BigFloat(bytes, 4)) : (Vector2?)null;

        // Its turn begun as TurnOrder.Advance begins it (TurnOrder.BeginTurn, then the clock), on the original's draws.
        var draws = DrawsBetween(log, turn, next);
        var feed = new Feed(draws, game.Random.Combat);
        var loot = LootBetween(log, turn, next);
        var lootFeed = new Feed(loot, game.Random.Loot);
        output.WriteLine($"{turn.Dump} -> {next.Dump}: slot {c.Slot} ({c.Sheet.Name})'s turn");
        Call(fight, "BeginTurn", c);
        game.AdvanceTurn();
        (int Target, Vector2 Cell)? portAction = null;
        var (press, walked) = (PressedA(built, log, turn, next), 0);
        var atNext = PlayOut(built, c, next.Name == "Combat.Exit", () => [.. CheckNext(built, log, next, c, feed, draws), .. CheckLoot(lootFeed, loot)], () =>
        {
            if (fight.State == CombatState.Moving && ++walked == press?.Ticks
                && (press.Value.EndedAt is not { } end || Vector2.Distance(end, c.Cell) < 0.5f) && fight.SkipWalk())
                output.WriteLine($"  A pressed {walked} ticks into the walk, where the original's player cut it short");
            if (portAction is null && fight.ActionActive)
                portAction = (fight.Target, c.Cell);        });
        output.WriteLine("  the original "
            + (acted is null ? "didn't act" : $"went for slot {originalTarget} from {originalCell}")
            + $", the port {(portAction is { } pa ? $"went for slot {pa.Target} from {pa.Cell}" : "didn't act")}");
        if ((acted is null) != (portAction is null))
            wrong.Add($"the original {(acted is null ? "didn't act" : "acted")}, the port {(portAction is null ? "didn't" : "did")}");
        else if (portAction is { } port && acted is not null)
        {
            if (port.Target != originalTarget)
                wrong.Add($"the original went for slot {originalTarget}, the port for slot {port.Target}");
            if (originalCell is { } cell && Vector2.Distance(cell, port.Cell) > 0.5f)
                wrong.Add($"the original acted from {cell}, the port from {port.Cell}");
        }
        wrong.AddRange(atNext);
        return wrong;
    }

    /// <summary>The first of <c>Weather.DriftWind</c>'s two draws, one every frame the original draws, which counts its frames.</summary>
    private const uint FrameDraw = 0x8007BAE8;

    /// <summary>
    /// When the original's player pressed A in the AI's walk (<c>Combat.InputMoving</c>, which puts the body at its leg's end with
    /// <c>Body.GiveUp</c>), in ticks of the walk, for the port to press it then if it's still walking; null where it wasn't. A log
    /// that watched for it gives the frames walked before it, at its frame's delta. One made before is read from the walk: a walk
    /// the port's outlasts at six ticks a frame, the most a frame takes, was cut short, by A if the body then stood at its leg's
    /// end (<paramref name="built"/>'s RAM at the first dump after the walk, which the port's combatant's place, standing on the
    /// leg's end, is to be within 0.5 of), and is pressed then; a body that stuck and gave up where it stood wasn't. Pressed then
    /// rather than sooner, setup 1's two (in the second session, at 0037 and 0051) end within 0.1 of the original's bodies. A body
    /// gone by then (fled) can't say.
    /// </summary>
    private static (int Ticks, Vector2? EndedAt)? PressedA(Built built, ParityLog log, ParityEntry turn, ParityEntry next)
    {
        var records = log.Records.ToList();
        var during = records.Skip(At(records, turn) + 1).TakeWhile(r => !ReferenceEquals(r, next)).ToList();
        var press = during.OfType<ParityEntry>().FirstOrDefault(e => e.Name == "Combat.InputMoving");
        var frames = during.TakeWhile(r => !ReferenceEquals(r, press)).OfType<ParityDraw>()
            .Count(d => d.At == FrameDraw && d.State == (int)CombatState.Moving);
        if (press is not null)
            return (Math.Max(1, frames * (int)BigFloat(press.Delta!, 0)), null);
        if (frames == 0 || log.Watches("Combat.InputMoving"))
            return null;
        var after = during.OfType<ParityEntry>().FirstOrDefault(e => e.Dump is not null) ?? next;
        var then = built with { Cp = log.Ram(after) };
        if (then.Body(turn.Current!.Value) is not (not 0 and var body))
            return null;
        var at = Vector(then.Cp, body + BodyPosition);
        return (frames * 6, new Vector2(at.X, at.Z));
    }

    private static float BigFloat(byte[] bytes, int at) => BitConverter.UInt32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at)));

    /// <summary>
    /// The next turn's start against its RAM: the draws' count, whose turn it is, every combatant's stats, skills, protection, weapon
    /// and death (<see cref="Verify"/>), and the AI records' kind, target, morale and flags, and whether each enemy has fled. Bodies'
    /// places, the grid, and where the combatant that played ended (to 0.5) are shown, not checked.
    /// </summary>
    private List<string> CheckNext(Built built, ParityLog log, ParityEntry next, Combatant played, Feed feed, List<ParityDraw> draws)
    {
        var fight = built.Battle;
        var then = built with { Cp = log.Ram(next) };
        var wrong = new List<string>();
        var nextSlot = SlotOf(then, then.Cp.ReadWord(then.Struct + 0x2C));
        output.WriteLine($"  the original drew {draws.Count}, the port {feed.Count}; next the original's slot {nextSlot}, the port's {fight.Current?.Slot}");
        if (feed.Count != draws.Count)
        {
            Show(feed, 0, Math.Max(feed.Count, draws.Count));
            wrong.Add($"the original drew {draws.Count}, the port {feed.Count}");
        }
        if (nextSlot != fight.Current?.Slot)
            wrong.Add($"next is the original's slot {nextSlot}, the port's {fight.Current?.Slot}");
        foreach (var line in Verify(then))
        {
            if (line.Contains("body at", StringComparison.Ordinal) || line.Contains("grid cells", StringComparison.Ordinal))
                output.WriteLine($"  (shown) {line}");
            else
                wrong.Add(line);
        }
        for (var slot = 0; slot < Battle.SlotCount; slot++)
        {
            var p = then.Record(slot);
            if (p == 0 || fight.Slots[slot] is not { } c)
                continue;
            var gone = (Half(then.Cp, p + 0x2C) & (ushort)CombatantFlags.Gone) != 0;
            if (gone != c.Gone)
                wrong.Add($"slot {slot} ({c.Sheet.Name}) gone: original {gone}, port {c.Gone}");
            // A dead or gone combatant's AI record is never read again: the original's fleeing rat in the second trial has a morale of 5
            // written as it leaves, the port's 0.
            if (then.Cp.ReadWord(p + 0x44) is not 0 and var record && c.Ai is { } ai && !c.Sheet.IsDead && !gone)
            {
                var original = ((AiKind)then.Cp.ReadByte(record + 4), (int)(sbyte)then.Cp.ReadByte(record + 5), (int)(sbyte)then.Cp.ReadByte(record + 6), (AiFlags)then.Cp.ReadByte(record + 9));
                var port = (ai.Kind, ai.Target, ai.Morale, ai.Flags);
                if (original != port)
                    wrong.Add($"slot {slot} ({c.Sheet.Name}) AI (kind, target, morale, flags): original {original}, port {port}");
            }
        }
        if (then.Record(played.Slot) is not 0 and var r && !played.Gone && Vector2.Distance(new Vector2(Float(then.Cp, r), Float(then.Cp, r + 4)), played.Cell) > 0.5f)
            output.WriteLine($"  (shown) slot {played.Slot} ended at {played.Cell}, the original at {new Vector2(Float(then.Cp, r), Float(then.Cp, r + 4))}");
        foreach (var line in wrong)
            output.WriteLine($"  {line}");
        return wrong;
    }
}
