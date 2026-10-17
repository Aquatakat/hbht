using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.MapObjects;

/// <summary>What a map object remembers.</summary>
public enum MemoryKind
{
    /// <summary>
    /// A conversation's random line: the roll, 0–100, that picked it, so talking again shows the same one. The
    /// one talking is a dialogue object or a monster party.
    /// </summary>
    RandomLine,
    /// <summary>A conversation's skill check: the party's step the first time, so talking again can't do better.</summary>
    SkillCheck,
    /// <summary>A container emptied that has no opened flag, which stays empty while it's remembered.</summary>
    Opened,
    /// <summary>A monster party beaten, which stays away while it's remembered.</summary>
    Beaten,
    /// <summary>
    /// A scene object put out as the player reached it, one of Oriana's path lights (<see cref="SceneObject.GoesOut"/>),
    /// which stays out while it's remembered. The original's type 0, a scene object's.
    /// </summary>
    PutOut,
}

/// <summary>A memory's key: the map object, what it remembers, and for a skill check, which skill.</summary>
public readonly record struct Memory(RecordId MapObject, MemoryKind Kind, int Skill = 0);

/// <summary>A memory held, with how many more map loads it lasts.</summary>
public sealed class MemoryEntry(Memory key, int value, int lifetime)
{
    public Memory Key { get; } = key;

    /// <summary>The roll, the step, or nothing.</summary>
    public int Value { get; set; } = value;

    /// <summary>Map loads left, as a signed byte: at 0 or below, it's forgotten.</summary>
    public int Lifetime { get; set; } = (sbyte)lifetime;
}

/// <summary>
/// What map objects remember for a while (the original's <c>VoxelChart</c>): a conversation's random
/// line and skill checks, a container emptied that has no opened flag, a monster party beaten, a path
/// light put out.
/// It holds 19 memories. Each lasts a number of map loads, a teleport or a conversation's end, and
/// is saved with the game.
/// </summary>
/// <remarks>
/// <para>
/// The original keys a memory by the object's index in its map, the map set and the chunk. The port
/// keys it by the object's record ID, which names its map, so a map placed in two chunks would share
/// its objects' memories. In the base game only the battle arenas and an empty overworld map are
/// placed twice, and they hold nothing that remembers.
/// </para>
/// <para>
/// The original keeps its slots in a free list, so a new memory can land in any free slot. Only a
/// lookup's order could tell, and no two memories share a key, so the port keeps them in the order
/// they were made.
/// </para>
/// </remarks>
public sealed class MapObjectMemory
{
    /// <summary>How many memories it holds (<c>VCHARTCOUNT</c>).</summary>
    public const int Slots = 19;

    /// <summary>How many map loads a conversation's memory and a container's last, and a recalled memory lasts again.</summary>
    public const int Long = 10;

    /// <summary>How many map loads a beaten monster party is remembered.</summary>
    public const int Short = 3;

    /// <summary>How many map loads a path light put out is remembered (<c>RenderVoxelScenes</c>, <c>0x8000cdec</c> in 1.0).</summary>
    public const int PutOutLoads = 16;

    private readonly List<MemoryEntry> _entries = [];

    /// <summary>Failed writes in a row (<c>VoxelChartVar</c>), which picks what's forgotten to make room.</summary>
    private int _misses;

    public IReadOnlyList<MemoryEntry> Entries => _entries;

    public MemoryEntry? Find(Memory key) => _entries.FirstOrDefault(e => e.Key == key);

    /// <summary>
    /// A conversation's memory, which recalling keeps for <see cref="Long"/> map loads more
    /// (<c>dialougSkillCheck</c> and the random line set <c>arg7</c> back to 10).
    /// </summary>
    public bool TryRecall(Memory key, out int value)
    {
        if (Find(key) is not { } entry)
        {
            value = 0;
            return false;
        }
        entry.Lifetime = Long;
        value = entry.Value;
        return true;
    }

    /// <summary>
    /// Remembers something for <paramref name="lifetime"/> map loads (<c>WriteTo_VoxelChart</c>). The
    /// caller looks first, as the original's do, so a key isn't written twice. When all 19 are
    /// taken, it forgets the beaten monster parties, then the random lines, then everything, until
    /// there's room.
    /// </summary>
    public MemoryEntry Remember(Memory key, int value, int lifetime = Long)
    {
        _misses++;
        if (_entries.Count < Slots)
        {
            _misses = 0;
            var entry = new MemoryEntry(key, value, lifetime);
            _entries.Add(entry);
            return entry;
        }
        switch (_misses)
        {
            case 1:
                _entries.RemoveAll(e => e.Key.Kind == MemoryKind.Beaten);
                break;
            case 2:
                _entries.RemoveAll(e => e.Key.Kind == MemoryKind.RandomLine);
                break;
            case 3:
                _entries.Clear();
                break;
        }
        return Remember(key, value, lifetime);
    }

    /// <summary>
    /// A map load, as a teleport or a conversation's end makes (<c>memset_voxelChart_entries</c>): every
    /// memory has one fewer to last, and those at 0 are forgotten.
    /// </summary>
    public void Age()
    {
        foreach (var entry in _entries)
            entry.Lifetime = (sbyte)(entry.Lifetime - 1);
        _entries.RemoveAll(e => e.Lifetime < 1);
    }

    /// <summary>Forgets a memory (<c>Ofunc_8005fd98</c>). False if there wasn't one.</summary>
    public bool Forget(Memory key) => _entries.RemoveAll(e => e.Key == key) > 0;

    /// <summary>Forgets everything, as a new game or loading a save starts (<c>memset_voxelChart</c>).</summary>
    public void Clear()
    {
        _entries.Clear();
        _misses = 0;
    }
}
