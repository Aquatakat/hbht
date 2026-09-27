using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Quests;

public enum FlagAccess
{
    Read,
    Set,
    Clear,
}

/// <summary>How something uses a flag.</summary>
public enum FlagRole
{
    // Dialogue.
    /// <summary>A SetFlag or ClearFlag effect.</summary>
    DialogueEffect,
    /// <summary>A CheckFlag condition in an If node.</summary>
    DialogueCheck,
    /// <summary>The script's done flag, set when a cut-scene, backdrop or help script finishes.</summary>
    ScriptDone,
    /// <summary>Winning the battle the script starts.</summary>
    ScriptBattleWon,

    // Map objects.
    /// <summary>Flag C: whether the object is there.</summary>
    ObjectVisible,
    /// <summary>Flag A: whether it works, or for a monster party whether it's there.</summary>
    ObjectEnabled,
    /// <summary>Flag B, written when the object is used.</summary>
    ObjectUsed,
    /// <summary>A monster party's flag B: set when it's beaten, and it's gone while set.</summary>
    PartyBeaten,
    /// <summary>A monster party's flag B when it has a dialogue: set when talked to.</summary>
    PartyTalkedTo,
    ContainerOpened,
    ContainerExploded,
    DoorLocked,
    DoorUnlocked,
    TrapArmed,
    TrapDisarmed,
    SecretFound,
    /// <summary>A SetFlag trigger.</summary>
    TriggerSets,

    // Other.
    /// <summary>An input of a derived flag.</summary>
    NetworkInput,
    /// <summary>Written by engine code (see <see cref="EngineFlags"/>).</summary>
    Engine,
}

/// <summary>Where a flag reference is.</summary>
public abstract record AtlasSource;

/// <param name="Node">The node, or null for the script's header.</param>
public sealed record ScriptSource(int Script, int? Node) : AtlasSource;

/// <param name="Borg9">The Borg9 holding the object.</param>
public sealed record ObjectSource(int Borg9, int Index) : AtlasSource;

/// <summary>A derived flag that reads this one.</summary>
public sealed record FlagSource(ushort Flag) : AtlasSource;

public sealed record EngineSource(string Description) : AtlasSource;

/// <param name="Ignored">
/// The game never performs this write: it names flag 0 or 1 or a derived flag, which
/// <c>setEventFlag</c> refuses, or it's a node's second effect.
/// </param>
public sealed record FlagReference(ushort Flag, FlagAccess Access, FlagRole Role, AtlasSource Source, bool Ignored = false);

/// <summary>A script and where the game can start it from.</summary>
/// <param name="StartedBy">Map objects and scripts (through RandomLine) that start it. Empty if only code does, or nothing.</param>
public sealed record AtlasScript(int Borg, Borg13Script Script, IReadOnlyList<AtlasSource> StartedBy);

/// <param name="Chunks">The map chunks that use this Borg9. Empty for the few loaded some other way.</param>
public sealed record AtlasObject(int Borg9, VoxelObject Object, IReadOnlyList<MapChunk> Chunks);

/// <summary>
/// Roadmap 1E: every event flag with what sets it, what reads it, and its journal entry, from the
/// dialogue scripts, map objects, the flag network and the engine's own flags.
/// </summary>
public sealed class QuestAtlas
{
    public required GameStateFunnelTable Funnel { get; init; }
    public required IReadOnlyList<FlaggedText> Journal { get; init; }
    public required IReadOnlyDictionary<int, AtlasScript> Scripts { get; init; }
    public required IReadOnlyList<AtlasObject> Objects { get; init; }
    public required IReadOnlyList<FlagReference> References { get; init; }

    /// <summary>The names of entities, dialogue entities and items, for showing operands.</summary>
    public required IReadOnlyDictionary<ItemId, string> Names { get; init; }

    /// <summary>Journal entries by their flag.</summary>
    public ILookup<ushort, int> JournalByFlag => Journal.Select((e, i) => (e.Flag, i)).ToLookup(e => e.Flag, e => e.i);

    public bool IsBitFlag(int flag) => flag < Funnel.BitFlagCount;

    public static QuestAtlas Build(RomImage rom)
    {
        var archive = BorgArchive.Open(rom);
        var db = GameDb.Read(rom);
        var funnel = db.GameStateFunnel;
        var refs = new List<FlagReference>();

        void Write(int flag, FlagAccess access, FlagRole role, AtlasSource source, bool secondEffect = false) =>
            refs.Add(new((ushort)flag, access, role, source, secondEffect || flag < 2 || flag >= funnel.BitFlagCount));
        // Map objects leave flag fields they don't use at 0.
        void WriteField(int flag, FlagAccess access, FlagRole role, AtlasSource source)
        {
            if (flag >= 2)
                Write(flag, access, role, source);
        }
        void Read(int flag, FlagRole role, AtlasSource source)
        {
            if (flag >= 2)
                refs.Add(new((ushort)flag, FlagAccess.Read, role, source));
        }
        void Gate(FlagGate gate, FlagRole role, AtlasSource source)
        {
            if (gate.Constant is null)
                Read(gate.Flag, role, source);
        }

        // Scripts.
        var scripts = new Dictionary<int, Borg13Script>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Dialogue))
            scripts[listing.Index] = Borg13Script.Parse(archive.Read(listing.Index));
        var startedBy = scripts.Keys.ToDictionary(k => k, _ => new List<AtlasSource>());

        foreach (var (index, script) in scripts)
        {
            if (script.StartMode is Borg13StartMode.Cutscene or Borg13StartMode.Backdrop or Borg13StartMode.Help)
                Write(script.DoneFlag, FlagAccess.Set, FlagRole.ScriptDone, new ScriptSource(index, null));
            if (EngineFlags.BattleFlags.TryGetValue(index, out var battleFlag))
                Write(battleFlag, FlagAccess.Set, FlagRole.ScriptBattleWon, new ScriptSource(index, null));

            foreach (var node in script.Nodes)
            {
                var source = new ScriptSource(index, node.Index);
                if (node.Kind == Borg13NodeKind.If && node.Condition is { Opcode: Borg13Opcode.CheckFlag } check)
                    Read(check.Value, FlagRole.DialogueCheck, source);

                var effects = node.Ops.Where(o => o.Opcode.IsEffect()).ToList();
                for (var i = 0; i < effects.Count; i++)
                {
                    var op = effects[i];
                    if (op.Opcode is Borg13Opcode.SetFlag or Borg13Opcode.ClearFlag)
                        Write(op.Value, op.Opcode == Borg13Opcode.SetFlag ? FlagAccess.Set : FlagAccess.Clear,
                            FlagRole.DialogueEffect, source, secondEffect: i > 0);
                    else if (op.Opcode == Borg13Opcode.RandomLine && startedBy.TryGetValue(op.Value, out var by))
                        by.Add(source);
                }
            }
        }

        // Map objects.
        var chunksByBorg9 = MapChunks.Read(rom).ToLookup(c => c.Collision);
        var objects = new List<AtlasObject>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.MapChunk))
        {
            foreach (var o in Borg9Voxels.Read(archive.Read(listing.Index)))
            {
                objects.Add(new AtlasObject(listing.Index, o, chunksByBorg9[listing.Index].ToList()));
                AddObject(o, new ObjectSource(listing.Index, o.Index));
            }
        }

        void AddObject(VoxelObject o, ObjectSource source)
        {
            var used = o.Flags.HasFlag(VoxelFlags.ClearFlagBOnUse) ? FlagAccess.Clear : FlagAccess.Set;
            switch (o.Data)
            {
                case ContainerData c:
                    // Herb, spice and gem sources are gated like triggers. Other containers show
                    // until they're opened or destroyed.
                    if (c.Kind is >= 4 and <= 6)
                    {
                        Gate(o.Visible, FlagRole.ObjectVisible, source);
                        Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    }
                    Read(c.OpenFlag, FlagRole.ContainerOpened, source);
                    Read(c.ExplodeFlag, FlagRole.ContainerExploded, source);
                    WriteField(c.OpenFlag, FlagAccess.Set, FlagRole.ContainerOpened, source);
                    WriteField(c.ExplodeFlag, FlagAccess.Set, FlagRole.ContainerExploded, source);
                    WriteField(o.FlagB, used, FlagRole.ObjectUsed, source);
                    break;
                case MonsterPartyData m:
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (m.Dialogue != 0)
                    {
                        WriteField(o.FlagB, FlagAccess.Set, FlagRole.PartyTalkedTo, source);
                        if (startedBy.TryGetValue(m.Dialogue, out var by))
                            by.Add(source);
                    }
                    else
                    {
                        Read(o.FlagB, FlagRole.PartyBeaten, source);
                        WriteField(o.FlagB, FlagAccess.Set, FlagRole.PartyBeaten, source);
                    }
                    break;
                case TeleporterData t:
                    Gate(o.Visible, FlagRole.ObjectVisible, source);
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (t.LockLevel != 0)
                    {
                        Read(t.LockFlag, FlagRole.DoorLocked, source);
                        WriteField(t.UnlockFlag, FlagAccess.Set, FlagRole.DoorUnlocked, source);
                    }
                    Read(t.TrapFlag, FlagRole.TrapArmed, source);
                    WriteField(t.DisarmFlag, FlagAccess.Set, FlagRole.TrapDisarmed, source);
                    if (t.SecretLevel != 0)
                    {
                        Read(t.SecretFlag, FlagRole.SecretFound, source);
                        WriteField(t.SecretFlag, FlagAccess.Set, FlagRole.SecretFound, source);
                    }
                    break;
                case DialogueData d:
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (startedBy.TryGetValue(d.Script, out var started))
                        started.Add(source);
                    break;
                case TriggerData t:
                    Gate(o.Visible, FlagRole.ObjectVisible, source);
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (t.Kind == TriggerKind.SetFlag)
                        Write(t.A, FlagAccess.Set, FlagRole.TriggerSets, source);
                    WriteField(o.FlagB, used, FlagRole.ObjectUsed, source);
                    break;
                default:
                    if (o.Type is not (VoxelType.SavePoint or VoxelType.Dialogue))
                        Gate(o.Visible, FlagRole.ObjectVisible, source);
                    break;
            }
        }

        // The flag network.
        for (var flag = funnel.BitFlagCount; flag < funnel.Flags.Count; flag++)
        {
            foreach (var input in Inputs(funnel, flag))
                Read(input, FlagRole.NetworkInput, new FlagSource((ushort)flag));
        }

        foreach (var range in EngineFlags.Ranges)
        {
            for (var flag = range.First; flag <= range.Last; flag++)
                Write(flag, FlagAccess.Set, FlagRole.Engine, new EngineSource(range.Description));
        }

        return new QuestAtlas
        {
            Funnel = funnel,
            Journal = db.Journal,
            Scripts = scripts.ToDictionary(s => s.Key, s => new AtlasScript(s.Key, s.Value, startedBy[s.Key])),
            Objects = objects,
            References = refs,
            Names = db.Entities.Select(e => (e.Id, e.Name))
                .Concat(db.DialogEntities.Select(e => (e.Id, e.Name)))
                .Concat(db.Weapons.Select(e => (e.Id, e.Name)))
                .Concat(db.Armor.Select(e => (e.Id, e.Name)))
                .Concat(db.Gear.Select(e => (e.Id, e.Name)))
                .Concat(db.Spells.Select(e => (e.Id, e.Name)))
                .Concat(db.Loot.Select(e => (e.Id, e.Name)))
                .DistinctBy(e => e.Id)
                .ToDictionary(e => e.Id, e => e.Name),
        };
    }

    /// <summary>A derived flag's inputs, without the unused ones.</summary>
    public static IEnumerable<ushort> Inputs(GameStateFunnelTable funnel, int flag)
    {
        var definition = funnel.Flags[flag];
        for (var i = 0; i < definition.InputCount; i++)
        {
            var input = funnel.Inputs[definition.InputStart + i];
            if (input != 0xFFFF)
                yield return input;
        }
    }
}
