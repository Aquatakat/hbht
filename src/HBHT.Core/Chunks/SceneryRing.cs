using HBHT.Core.MapObjects;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Chunks;

/// <summary>
/// The Draw Distance setting's Extended, a port addition: on the Overworld, the 16 chunks round the 3×3 the chunk streamer keeps
/// loaded, so that the 5×5 around the player is drawn. They're scenery only: their scenes and the scene objects their maps
/// place, and nothing else. They have no collision, sounds, lights or wanderers, their map objects do nothing, and loading
/// them is no map load, so what map objects remember doesn't age by them. Which scene objects show is decided as the 3×3's
/// are, by their flag gates and the memory that keeps a put-out light out (<see cref="LoadedMap.Scenery"/>), read without
/// changing either.
/// </summary>
/// <remarks>
/// It's the runtime's alone: the core's game never holds one, so a game plays the same whatever the setting, and a headless run
/// plays as the runtime's does. It follows the 3×3 (<see cref="ChunkStreamer.Centre"/>) rather than leading it, so it's
/// <see cref="Update"/>d after the streamer, and it's empty while the 3×3 is, as under a cut-scene's stage or a battle's arena,
/// which keep the original's 3×3 and planes. Other map sets keep the 3×3: interiors and the towns' sets are walled in.
/// </remarks>
public sealed class SceneryRing(GameState game, ChunkStreamer inner)
{
    /// <summary>The map set it draws further in, <c>aidyn:mapset/0</c>.</summary>
    public static readonly RecordId Overworld = RecordId.Decimal(RecordTypes.MapSet, 0);

    /// <summary>How many chunks out from the centre it reaches: the 5×5's 2, where the streamer's 3×3 reaches 1.</summary>
    public const int Reach = 2;

    private readonly Dictionary<ChunkKey, (ChunkEntry Chunk, LoadedMap Map)> _maps = [];
    /// <summary>What it was last brought round: the 3×3's map set, its centre, and the centre's map, which is another once it reloads.</summary>
    private (RecordId Set, ChunkKey Centre, LoadedMap? Map)? _around;

    /// <summary>Whether the setting asks for it. Off, it holds nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether it draws now: it's enabled, and the 3×3 is loaded on the Overworld.</summary>
    public bool Active => Enabled && inner.MapSet.Id == Overworld && inner.All.Any();

    /// <summary>The 3×3 it's drawn around.</summary>
    public ChunkStreamer Inner => inner;

    /// <summary>A chunk's scenery has come into the ring.</summary>
    public event Action<ChunkEntry, LoadedMap>? Loaded;

    /// <summary>A chunk's scenery is leaving the ring: out of the 5×5, or into the 3×3.</summary>
    public event Action<ChunkEntry, LoadedMap>? Unloading;

    /// <summary>The ring's chunks, and which of their scene objects show.</summary>
    public IEnumerable<(ChunkEntry Chunk, LoadedMap Map)> All => _maps.Values;

    /// <summary>The ring's chunk at a key, if it has it.</summary>
    public LoadedMap? Map(ChunkKey key) => _maps.TryGetValue(key, out var entry) ? entry.Map : null;

    /// <summary>
    /// Brings the ring round the 3×3 as it stands: the chunks of the 5×5 that aren't in it come in, and the rest go. Returns
    /// whether any changed.
    /// </summary>
    public bool Update()
    {
        if (!Active)
        {
            _around = null;
            return Clear();
        }
        var centre = inner.Centre;
        var set = inner.MapSet;
        var centreMap = inner.Map(centre);
        if (_around is var (aroundSet, aroundCentre, aroundMap) && aroundSet == set.Id && aroundCentre == centre)
        {
            if (aroundMap == centreMap)
                return false;
            // The 3×3 loaded again where it was, as trek does after a cut-scene, a backdrop or the help menu: so is the ring.
            _around = (set.Id, centre, centreMap);
            return Regate();
        }
        _around = (set.Id, centre, centreMap);

        var wanted = new HashSet<ChunkKey>();
        for (var r = centre.Row - Reach; r <= centre.Row + Reach; r++)
        {
            for (var c = centre.Column - Reach; c <= centre.Column + Reach; c++)
            {
                if (Math.Max(Math.Abs(r - centre.Row), Math.Abs(c - centre.Column)) < Reach)
                    continue;
                if (set.Chunk(r, c) is not null)
                    wanted.Add(centre with { Row = r, Column = c });
            }
        }

        var changed = false;
        foreach (var key in _maps.Keys.Where(k => !wanted.Contains(k)).ToList())
        {
            Unload(key);
            changed = true;
        }
        foreach (var key in wanted.Where(k => !_maps.ContainsKey(k)))
        {
            var chunk = set.Chunk(key.Row, key.Column)!;
            var map = LoadedMap.Scenery(game.Plugins.Get<MapRecord>(chunk.Map), key, game.Flags, game.Memory, set.Origin(key.Row, key.Column));
            _maps[key] = (chunk, map);
            Loaded?.Invoke(chunk, map);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Gates the ring's scene objects again by the flags and the memory, as the streamer's are when the pause menu closes
    /// (<see cref="ChunkStreamer.Regate"/>) and as they load again: each chunk whose scenery changed goes and comes back
    /// (<see cref="Unloading"/>, <see cref="Loaded"/>). Returns whether any did.
    /// </summary>
    public bool Regate()
    {
        var changed = false;
        foreach (var (key, (chunk, old)) in _maps.ToList())
        {
            var map = LoadedMap.Scenery(old.Record, key, game.Flags, game.Memory, old.Origin);
            if (old.Record.Objects.OfType<SceneObject>().All(o => old.IsActive(o.Index) == map.IsActive(o.Index) && old.Look(o.Index) == map.Look(o.Index)))
                continue;
            Unload(key);
            _maps[key] = (chunk, map);
            Loaded?.Invoke(chunk, map);
            changed = true;
        }
        return changed;
    }

    /// <summary>Lets every chunk go. Returns whether it held any.</summary>
    public bool Clear()
    {
        if (_maps.Count == 0)
            return false;
        foreach (var key in _maps.Keys.ToList())
            Unload(key);
        return true;
    }

    private void Unload(ChunkKey key)
    {
        var (chunk, map) = _maps[key];
        Unloading?.Invoke(chunk, map);
        _maps.Remove(key);
    }
}
