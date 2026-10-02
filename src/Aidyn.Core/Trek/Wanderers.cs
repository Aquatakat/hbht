using System.Numerics;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

/// <summary>
/// A monster party's body in the world, wandering from node to node (the original's <c>Wanderer</c>):
/// an NPC to talk to, or the leader of a group of enemies, which the player sees alone.
/// </summary>
public sealed class Wanderer
{
    internal Wanderer(LoadedMap map, MonsterPartyObject party, EntityBody look, Body body)
    {
        Map = map;
        Party = party;
        Look = look;
        Body = body;
        Home = new Vector2(party.Position.X, party.Position.Z) + new Vector2(map.Origin.X, map.Origin.Z);
    }

    /// <summary>The chunk it belongs to, which it stays in.</summary>
    public LoadedMap Map { get; }

    public MonsterPartyObject Party { get; }

    /// <summary>The party's entity's model and size.</summary>
    public EntityBody Look { get; }

    public Body Body { get; }

    /// <summary>
    /// Whether it talks rather than fights (<c>moveType</c> 0): a party with a dialogue. It never
    /// chases the player.
    /// </summary>
    public bool Talks => Party.Dialogue is not null;

    /// <summary>The party's position, in the world's X and Z (<c>Wanderer.position</c>).</summary>
    public Vector2 Home { get; }

    /// <summary>How far it strays from <see cref="Home"/> after the player: the party's radius (<c>size</c>).</summary>
    public float Leash => Party.Radius;

    /// <summary>The node it's going to or turning from.</summary>
    public WanderNodeObject? Node { get; private set; }

    /// <summary>
    /// Where it's going, in the world's X and Z: the node, scattered on a visit after the first. For a
    /// node it turns away from, the way to face instead (<c>start_position</c>).
    /// </summary>
    public Vector2 Goal { get; private set; }

    /// <summary>Ticks before it may move on from <see cref="Node"/>, counting from when it chose it.</summary>
    public ushort Timer { get; internal set; }

    /// <summary>
    /// Takes the node's values (<c>WanderGetNextNode</c>): where it is, how near to come, the scatter,
    /// the chance of the second next node, the wait and what to do there. A node it turns from gives
    /// the way from the node to the body instead of a place.
    /// </summary>
    internal void Take(WanderNodeObject node)
    {
        Node = node;
        Timer = (ushort)node.Wait;
        Goal = new Vector2(node.Position.X + Map.Origin.X, node.Position.Z + Map.Origin.Z);
        if ((node.Flags & WanderFlags.Rotate) != 0)
            Goal = Vector2.Normalize(new Vector2(Body.Sphere.Position.X, Body.Sphere.Position.Z) - Goal);
    }

    /// <summary>
    /// Sets off for its goal, or turns (<c>FUN_80012d44</c>): to a node it walks to, if it's further
    /// than the node's radius, walking if the party has <see cref="PartyFlags.Walks"/> and running
    /// otherwise; from a node it turns from, facing away.
    /// </summary>
    internal void Resume()
    {
        if (Node is not { } node)
            return;
        if ((node.Flags & WanderFlags.Move) != 0
            && Vector2.Distance(new Vector2(Body.Sphere.Position.X, Body.Sphere.Position.Z), Goal) > node.WanderRadius)
            Body.GoTo(Goal, node.WanderRadius, (Party.Flags & PartyFlags.Walks) != 0);
        if ((node.Flags & WanderFlags.Rotate) != 0)
            Body.Face(Goal);
    }
}

/// <summary>
/// The wanderers of the loaded chunks (the original's <c>WanderManager</c>): each chunk's monster
/// parties get bodies as it loads, standing where the party is and setting off for its home node, and
/// lose them as it goes.
/// </summary>
public sealed class Wanderers(PluginSet plugins)
{
    /// <summary>
    /// How many the original has room for at once in trek (<c>WanderHead</c>). The port has no limit,
    /// as the user chose: mods will add their own, and memory isn't short.
    /// </summary>
    public const int OriginalLimit = 39;

    /// <summary>How far from the camera's aim bodies move and animate, in trek (<c>PlayerHandler.float_0x68</c>).</summary>
    public const float StepDistance = 30;

    private readonly List<Wanderer> _all = [];
    private readonly List<CrowdMember?> _crowd = [];

    public IReadOnlyList<Wanderer> All => _all;

    /// <summary>
    /// Gives the chunk's monster parties their bodies (<c>look_for_monsterparties</c>, <c>AllocWanderer</c>):
    /// each that's active and not remembered as beaten, however many there are already. Its body stands at the
    /// party's position, set down on the ground from the party's height, and makes for its home node.
    /// The chunk's collision must be in <paramref name="world"/>.
    /// </summary>
    /// <returns>Those added.</returns>
    /// <param name="cellSize">The map set's chunks' size, which keeps the bodies inside their chunk.</param>
    public IReadOnlyList<Wanderer> Load(LoadedMap map, float cellSize, CollisionWorld world, MapObjectMemory memory)
    {
        var added = new List<Wanderer>();
        foreach (var party in map.Objects.OfType<MonsterPartyObject>())
        {
            if (!map.IsActive(party.Index) || memory.Find(new Memory(MapRecord.VoxelId(map.Record.Id, party.Index), MemoryKind.Beaten)) is not null)
                continue;
            if (party.Home is not { } homeId || map[LoadedMap.Index(homeId)] is not WanderNodeObject home || (home.Flags & WanderFlags.Move) == 0)
                continue;
            var look = plugins.TryGet<DbRecord>(party.Entity)?.Body;
            if (look is null)
                continue;
            var at = map.Origin + party.Position;
            var body = new Body(at, look.Radius, look.Scale)
            {
                Bounds = (new Vector2(map.Origin.X, map.Origin.Z) + new Vector2(Body.EdgeMargin),
                    new Vector2(map.Origin.X, map.Origin.Z) + new Vector2(cellSize - Body.EdgeMargin)),
            };
            // Actor::CheckCollision: from 1 above the party to 1 below, or failing that from on high.
            if (TeleportArrival.Settle(world, at with { Y = at.Y + look.Radius }, look.Radius) is { } ground)
                body.Sphere.Position = ground;
            var wanderer = new Wanderer(map, party, look, body);
            wanderer.Take(home);
            wanderer.Resume();
            _all.Add(wanderer);
            added.Add(wanderer);
        }
        return added;
    }

    /// <summary>Takes away the bodies of a chunk that's gone (<c>FreeWandererFromZone</c>). Returns those taken.</summary>
    public IReadOnlyList<Wanderer> Unload(LoadedMap map)
    {
        var gone = _all.Where(w => w.Map == map).ToList();
        _all.RemoveAll(w => w.Map == map);
        return gone;
    }

    /// <summary>Takes away every body, as going to another map set does.</summary>
    public void Clear() => _all.Clear();

    /// <summary>
    /// Steps the bodies after the player's own step, as <c>ProcessPlayers</c> does each tick: the
    /// player is pushed out of the bodies in his chunk, then each body within <see cref="StepDistance"/>
    /// of the camera's aim moves and is pushed out of the rest. Those further off stand still.
    /// </summary>
    /// <param name="player">The player's sphere.</param>
    /// <param name="playerStart">Where the player began the tick.</param>
    /// <param name="playerChunk">The chunk the player is in, by its map's key.</param>
    /// <param name="aim">What the camera looks at.</param>
    /// <param name="animation">Each body's animations.</param>
    /// <returns>The body nearest the player across the ground that he was weighed against, and how far, if any.</returns>
    public (Wanderer? Nearest, float Distance) Step(CollisionWorld world, CollisionSphere player, Vector3 playerStart, object? playerChunk,
        Vector3 aim, Func<Wanderer, IRootMotion> animation)
    {
        _crowd.Clear();
        _crowd.Add(new CrowdMember(player, 1, playerChunk));
        foreach (var w in _all)
            _crowd.Add(new CrowdMember(w.Body.Sphere, w.Body.Scale, w.Map.Key));

        var (nearest, distance) = Crowd.Push(_crowd, 0, playerStart);
        for (var i = 0; i < _all.Count; i++)
        {
            var body = _all[i].Body;
            if (Vector3.Distance(body.Sphere.Position, aim) > StepDistance)
                continue;
            var start = body.Sphere.Position;
            body.Step(world, animation(_all[i]));
            Crowd.Push(_crowd, i + 1, start);
        }
        return nearest > 0 ? (_all[nearest - 1], distance) : (null, distance);
    }
}
