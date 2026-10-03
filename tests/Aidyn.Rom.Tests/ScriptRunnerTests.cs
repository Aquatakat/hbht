using Aidyn.Core.Flags;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins.Records;
using Aidyn.Plugins;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Import;
using Aidyn.Rom.Quests;
using ScriptEnding = Aidyn.Core.Scripts.ScriptEnding;

namespace Aidyn.Rom.Tests;

/// <summary>Runs every imported script through the core's script runner, with random game states and choices.</summary>
public class ScriptRunnerTests
{
    private static readonly Lazy<(List<DialogueRecord> Scripts, List<FlagRecord> Flags)> Data = new(() =>
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        return (BaseImporter.BuildDialogues(BorgArchive.Open(rom), EngineTables.Read(rom)), BaseImporter.BuildFlags(rom));
    });

    private static Dictionary<RecordId, DialogueRecord> ById => Data.Value.Scripts.ToDictionary(s => s.Id);

    /// <summary>A party and inventory that answer at random, the same way for the whole run.</summary>
    private sealed class RandomWorld(Random random) : IScriptWorld
    {
        private readonly Dictionary<RecordId, bool> _members = [], _items = [];
        private readonly Dictionary<Memory, int> _memory = [];

        public bool InParty(RecordId entity) => Answer(_members, entity);
        public bool HasItem(RecordId item) => Answer(_items, item);
        public int SkillCheck(int skill, IRandom rolls) => random.Next(0, 22);
        public int BestStat(int stat) => random.Next(0, 31);
        public int WorstStat(int stat) => random.Next(0, 31);
        public int MemberStat(RecordId? member, int stat) => random.Next(0, 31);
        public int PartySize { get; } = random.Next(1, 5);
        public uint Gold { get; set; } = (uint)random.Next(0, 30000);
        public void AddItem(RecordId item) => _items[item] = true;
        public bool TakeItem(RecordId item) => _items.Remove(item);
        public bool AddMember(RecordId entity) => _members[entity] = true;
        public void RemoveMember(RecordId entity) => _members[entity] = false;
        public bool AddExp(RecordId member, int amount) => InParty(member);
        public void CampHeal() { }
        public bool TryRecall(Memory memory, out int value) => _memory.TryGetValue(memory, out value);
        public void Remember(Memory memory, int value) => _memory[memory] = value;

        private bool Answer(Dictionary<RecordId, bool> answers, RecordId id) =>
            answers.TryGetValue(id, out var answer) ? answer : answers[id] = random.Next(2) == 0;
    }

    private sealed class Cues : IScriptHost
    {
        public List<ScriptCue> All { get; } = [];
        public void Cue(ScriptCue cue) => All.Add(cue);
    }

    /// <summary>Plays a script to its end with random flags, answers and choices, and returns how it ended.</summary>
    private static ScriptEnding? Play(DialogueRecord script, int seed, Dictionary<RecordId, DialogueRecord> scripts, out string trailText)
    {
        var random = new Random(seed);
        var flags = new FlagNetwork(Data.Value.Flags, RecordId.Decimal(RecordTypes.Flag, EngineFlags.NewJournalEntry));
        flags.Restore(h => h > 1 && random.Next(4) == 0);
        var runner = new ScriptRunner(script, flags, new RandomWorld(random), new Cues(), new Lcg((uint)seed), id => scripts.GetValueOrDefault(id),
            new ScriptStart(0, RecordId.Hex(RecordTypes.Voxel, 1, 2)));
        runner.Start();
        var trail = new Queue<string>();
        trailText = "";
        for (var moves = 0; runner.Status != ScriptStatus.Ended; moves++)
        {
            trail.Enqueue(runner.Page is { } p ? $"{p.Line.Node}[{string.Join(",", p.Choices.Select(c => c.Node))}]" : $"{runner.Wait}");
            if (trail.Count > 30)
                trail.Dequeue();
            if (moves == 2000)
            {
                trailText = string.Join(" ", trail);
                return null;
            }
            if (runner.Status == ScriptStatus.Waiting)
            {
                if (runner.Wait == ScriptWait.Timer)
                    runner.Step(1000);
                else
                    runner.Resume();
            }
            else if (runner.Page!.Choices.Count > 0 && runner.Wait != ScriptWait.Player)
                runner.Choose(random.Next(runner.Page.Choices.Count));
            else
                runner.Continue();
        }
        return runner.Ending!;
    }

    /// <summary>
    /// Plays every script 40 times. Two things turn up, both in the data: node 24 of script 1040 lost
    /// its link (with Dougal in the party, the harbour scene reads past its nodes), and Trahern's
    /// conversation (936) repeats forever once flags 67 and 84 are set, but nothing sets 84.
    /// </summary>
    [Fact]
    public void EveryScriptEndsExceptWhereTheDataBreaks()
    {
        var scripts = ById;
        var found = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var script in Data.Value.Scripts)
        {
            for (var seed = 0; seed < 40; seed++)
            {
                var ending = Play(script, seed, scripts, out var trail);
                if (ending is null)
                    found.Add($"{script.Id} loops");
                else if (ending.Fault is { } fault)
                    found.Add($"{script.Id}: {fault}");
            }
        }
        Assert.Equal(
        [
            "aidyn:dialogue/0x03A8 loops",
            "aidyn:dialogue/0x0410: Node 24 has no link and there's no menu to go back to: the original reads past the script's nodes.",
        ], found);
    }

    [Fact]
    public void TheKendallCutSceneSetsItsDoneFlag()
    {
        var script = ById[RecordId.Hex(RecordTypes.Dialogue, 866)];
        var flags = new FlagNetwork(Data.Value.Flags, RecordId.Decimal(RecordTypes.Flag, EngineFlags.NewJournalEntry));
        var host = new Cues();
        var runner = new ScriptRunner(script, flags, new RandomWorld(new Random(0)), host, new Lcg(), _ => null);
        runner.Start();
        while (runner.Status != ScriptStatus.Ended)
        {
            if (runner.Status == ScriptStatus.Waiting)
                runner.Step(1000);
            if (runner.Status == ScriptStatus.Waiting)
                runner.Resume();
            else if (runner.Status == ScriptStatus.Showing)
                runner.Continue();
        }

        Assert.Null(runner.Ending!.Fault);
        Assert.True(flags.Get(5));
        // It spawns Kendall and Alaron, and moves the camera.
        Assert.Contains(host.All, c => c is SpawnCue);
        Assert.Contains(host.All, c => c is CameraCue);
    }
}
