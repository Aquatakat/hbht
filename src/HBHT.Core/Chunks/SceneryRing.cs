using HBHT.Core.MapObjects;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Chunks;

/// <summary>
/// The scenery the 3×3 the chunk streamer keeps loaded looks out on: the chunks its chunks show (<see cref="ChunkEntry.Shows"/>),
/// which draw what their scenes drew over those chunks' cells in the original, out to one past the 3×3; and under the Draw
/// Distance setting's Extended, a port addition, the chunks round the 3×3 that make up the 5×5 round the player, with what all of
/// those show, out to one past the 5×5 (docs/systems/rendering.md#draw-distance). Extended draws the 5×5 only round a chunk that
/// has it (<see cref="ChunkEntry.Extended"/>), and only the chunks of it that have it too. They're scenery only: their scenes and
/// the scene objects their maps place, and nothing else. They have no collision, sounds, lights or wanderers, their map objects do
/// nothing, and loading them is no map load, so what map objects remember doesn't age by them. Which scene objects show is decided
/// as the 3×3's are, by their flag gates and the memory that keeps a put-out light out (<see cref="LoadedMap.Scenery"/>), read
/// without changing either.
/// </summary>
/// <remarks>
/// It's the runtime's alone: the core's game never holds one, so a game plays the same whatever the setting, and a headless run
/// plays as the runtime's does. It follows the 3×3 (<see cref="ChunkStreamer.Centre"/>) rather than leading it, so it's
/// <see cref="Update"/>d after the streamer, and it's empty while the 3×3 is, as under a cut-scene's stage or a battle's arena,
/// which keep the original's 3×3 and planes.
/// </remarks>
public sealed class SceneryRing(GameState game, ChunkStreamer inner)
{
    /// <summary>How many chunks out from the centre Extended reaches: the 5×5's 2, where the streamer's 3×3 reaches 1.</summary>
    public const int ExtendedReach = 2;

    private readonly Dictionary<ChunkKey, (ChunkEntry Chunk, LoadedMap Map)> _maps = [];
    /// <summary>
    /// What it was last brought round: the 3×3's map set, its centre, and the centre's map, which is another once it reloads, and
    /// how far it reached.
    /// </summary>
    private (RecordId Set, ChunkKey Centre, LoadedMap? Map, int Reach)? _around;

    /// <summary>Whether it draws at all, as in trek. Off, it holds nothing.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether the setting asks for Draw Distance's Extended.</summary>
    public bool Extended { get; set; }

    /// <summary>Whether it draws now: it's enabled, and the 3×3 is loaded.</summary>
    public bool Active => Enabled && inner.All.Any();

    /// <summary>
    /// How many chunks out from the centre it reaches now: <see cref="ExtendedReach"/> under Extended round a chunk that has it,
    /// else the 3×3's 1. What the chunks show is drawn one further.
    /// </summary>
    public int Reach => Active && Extended && inner.MapSet.Chunk(inner.Centre.Row, inner.Centre.Column) is { Extended: true } ? ExtendedReach : 1;

    /// <summary>The 3×3 it's drawn around.</summary>
    public ChunkStreamer Inner => inner;

    /// <summary>A chunk's scenery has come into the ring.</summary>
    public event Action<ChunkEntry, LoadedMap>? Loaded;

    /// <summary>A chunk's scenery is leaving the ring: out of it, or into the 3×3.</summary>
    public event Action<ChunkEntry, LoadedMap>? Unloading;

    /// <summary>The ring's chunks, and which of their scene objects show.</summary>
    public IEnumerable<(ChunkEntry Chunk, LoadedMap Map)> All => _maps.Values;

    /// <summary>The ring's chunk at a key, if it has it.</summary>
    public LoadedMap? Map(ChunkKey key) => _maps.TryGetValue(key, out var entry) ? entry.Map : null;

    /// <summary>
    /// Brings the ring round the 3×3 as it stands: the chunks it wants that aren't in the 3×3 come in, and the rest go. Returns
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
        var reach = Reach;
        if (_around is var (aroundSet, aroundCentre, aroundMap, aroundReach) && aroundSet == set.Id && aroundCentre == centre && aroundReach == reach)
        {
            if (aroundMap == centreMap)
                return false;
            // The 3×3 loaded again where it was, as trek does after a cut-scene, a backdrop or the help menu: so is the ring.
            _around = (set.Id, centre, centreMap, reach);
            return Regate();
        }
        _around = (set.Id, centre, centreMap, reach);

        var wanted = Wanted(set, centre, reach);
        var changed = false;
        foreach (var key in _maps.Keys.Where(k => !wanted.Contains(k) || Drawn(set, k) != _maps[k].Chunk).ToList())
        {
            Unload(key);
            changed = true;
        }
        foreach (var key in wanted.Where(k => !_maps.ContainsKey(k)))
        {
            var chunk = Drawn(set, key);
            var map = LoadedMap.Scenery(game.Plugins.Get<MapRecord>(chunk.Map), key, game.Flags, game.Memory, set.Origin(key.Row, key.Column));
            _maps[key] = (chunk, map);
            Loaded?.Invoke(chunk, map);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// The chunks past the 3×3 it draws round <paramref name="centre"/>: those of the square <paramref name="reach"/> out that have
    /// Extended, and those that the 3×3's chunks and these show, out to one further.
    /// </summary>
    public static HashSet<ChunkKey> Wanted(MapSetRecord set, ChunkKey centre, int reach)
    {
        static int Distance(ChunkKey centre, int row, int column) => Math.Max(Math.Abs(row - centre.Row), Math.Abs(column - centre.Column));
        var wanted = new HashSet<ChunkKey>();
        var looking = new List<ChunkEntry>();
        for (var r = centre.Row - reach; r <= centre.Row + reach; r++)
        {
            for (var c = centre.Column - reach; c <= centre.Column + reach; c++)
            {
                if (set.Chunk(r, c) is not { } chunk)
                    continue;
                if (Distance(centre, r, c) <= 1)
                {
                    looking.Add(chunk);
                }
                else if (chunk.Extended)
                {
                    looking.Add(chunk);
                    wanted.Add(centre with { Row = r, Column = c });
                }
            }
        }
        foreach (var cell in looking.SelectMany(c => c.Shows ?? []))
        {
            if (Distance(centre, cell.Row, cell.Column) is > 1 and var d && d <= reach + 1 && set.Chunk(cell.Row, cell.Column) is not null)
                wanted.Add(centre with { Row = cell.Row, Column = cell.Column });
        }
        return wanted;
    }

    /// <summary>
    /// Gates the ring's scene objects again by the flags and the memory, as the streamer's are when the pause menu closes
    /// (<see cref="ChunkStreamer.Regate"/>) and as they load again, and its scenes by them (<see cref="ChunkEntry.Swap"/>): each chunk
    /// whose scenery changed goes and comes back (<see cref="Unloading"/>, <see cref="Loaded"/>). Returns whether any did.
    /// </summary>
    public bool Regate()
    {
        var changed = false;
        foreach (var (key, (was, old)) in _maps.ToList())
        {
            var chunk = Drawn(inner.MapSet, key);
            var map = LoadedMap.Scenery(old.Record, key, game.Flags, game.Memory, old.Origin);
            if (chunk == was
                && old.Record.Objects.OfType<SceneObject>().All(o => old.IsActive(o.Index) == map.IsActive(o.Index) && old.Look(o.Index) == map.Look(o.Index)))
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

    /// <summary>
    /// A chunk of the map set as the flags draw it now (<see cref="ChunkEntry.Drawn"/>), read as they're stored, so that drawing it
    /// changes nothing (<see cref="Flags.FlagNetwork.IsSetAsStored"/>).
    /// </summary>
    private ChunkEntry Drawn(MapSetRecord set, ChunkKey key) =>
        set.Chunk(key.Row, key.Column)!.Drawn(gate => game.Flags.IsSetAsStored(gate.Flag) != gate.Invert);

    private void Unload(ChunkKey key)
    {
        var (chunk, map) = _maps[key];
        Unloading?.Invoke(chunk, map);
        _maps.Remove(key);
    }
}
