using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Movement;
using Aidyn.Core.Physics;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Trek;

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

    /// <summary>A dialogue entity's perception (<c>EntityDB::GetPerception</c>).</summary>
    public const int DialoguePerception = 10;

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
            var entity = plugins.TryGet<EntityRecord>(party.Entity);
            var perception = entity?.Perception ?? DialoguePerception;
            var wanderer = new Wanderer(map, party, look, body, perception) { Sounds = entity?.Sounds };
            wanderer.Take(home);
            wanderer.Resume();
            _all.Add(wanderer);
            added.Add(wanderer);
        }
        return added;
    }

    /// <summary>
    /// Whether A reaches the player's nearest body (<c>0x80004480</c> in 1.0, the decomp's
    /// <c>FUN_80005d60</c>): it's a wanderer's, its party isn't silent, the distance between them across the
    /// ground is at most its scaled radius plus his plus 0.5, and they're less than 2 apart in height.
    /// </summary>
    /// <param name="nearest">The nearest body the push loop found, and how far across the ground (<see cref="Step"/>'s result).</param>
    /// <param name="player">The player's sphere, whose radius is his scaled radius.</param>
    public static bool Reaches(Wanderer nearest, float distance, CollisionSphere player) =>
        (nearest.Party.Flags & PartyFlags.Silent) == 0
        && distance <= nearest.Body.ScaledRadius + player.Radius + TalkMargin
        && MathF.Abs(player.Position.Y - nearest.Body.Sphere.Position.Y) < TalkHeight;

    /// <summary>What A's reach of a body adds to the two radii.</summary>
    public const float TalkMargin = 0.5f;

    /// <summary>How far apart in height A reaches a body.</summary>
    public const float TalkHeight = 2;

    /// <summary>The wanderer whose party this is, in that chunk, if it has a body.</summary>
    public Wanderer? Of(LoadedMap map, MonsterPartyObject party) => _all.FirstOrDefault(w => w.Map == map && w.Party == party);

    /// <summary>
    /// Takes away a wanderer's body, as when its party joins the player's (<c>Actor::FreePlayer</c>).
    /// It comes back as its chunk next loads, if its flags still let it.
    /// </summary>
    public void Free(Wanderer wanderer) => _all.Remove(wanderer);

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
    /// What the wanderers decide in a tick of trek (<c>WanderTick</c>, checked in 1.0's code): each
    /// counts down its timer, hunters notice the player and chase him, give up and go back, and those
    /// that don't hunt turn to face him when he's near; and each moves on to its next node when its
    /// timer has run out and it's idle. The original skips it under a popup, the menu or a fade, and
    /// outside trek, and so must the caller. It doesn't care how far they are from the camera.
    /// </summary>
    /// <param name="player">The player's sphere's centre.</param>
    /// <param name="playerRequest">What he's doing: the animation he asks for.</param>
    /// <param name="party">The party, whose best Stealth hides him; null for none.</param>
    /// <param name="random">The wandering stream.</param>
    /// <param name="fogTime">How thick the weather's fog is, 0 to 1.</param>
    /// <returns>The hunters that ambush the player, starting a battle.</returns>
    public IReadOnlyList<Wanderer> Tick(CollisionWorld world, Vector3 player, int playerRequest, Party? party, IRandom random, float fogTime = 0)
    {
        var tick = new WanderTick(world, player, playerRequest, NoticeRanges.SneakFor(party), fogTime, random);
        List<Wanderer>? ambushes = null;
        foreach (var w in _all)
        {
            if (w.Tick(tick))
                (ambushes ??= []).Add(w);
        }
        return ambushes ?? (IReadOnlyList<Wanderer>)[];
    }

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
