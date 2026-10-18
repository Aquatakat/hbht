using System.Numerics;
using Aidyn.Core.Physics;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.MapObjects;

/// <summary>
/// Where a teleporter takes the player (<c>TeleportPlayer</c>): a chunk of a map set, a point in it, the
/// way to face, and a point for the camera.
/// </summary>
/// <param name="Position">In the chunk's coordinates: the arrival point, or the teleporter's own position.</param>
/// <param name="Heading">The way the arrival point turns the player, or null to keep his.</param>
/// <param name="Camera">A reference point in the chunk for the camera, in its coordinates, or null.</param>
public sealed record TeleportArrival(MapSetRecord Set, ChunkEntry Chunk, Vector3 Position, Vector2? Heading, Vector3? Camera)
{
    /// <summary>
    /// Where a teleporter leads, or null if its map set or chunk isn't installed. Without an arrival
    /// point, the player arrives at the teleporter's own position, in the destination's coordinates.
    /// </summary>
    public static TeleportArrival? For(PluginSet plugins, TeleporterObject teleporter)
    {
        var destination = teleporter.Destination;
        if (plugins.TryGet<MapSetRecord>(destination.MapSet) is not { } set
            || set.Chunks.FirstOrDefault(c => c.Row == destination.Row && c.Column == destination.Column) is not { } chunk)
            return null;
        var arrival = teleporter.Arrival is { } id ? Point(plugins, id) : null;
        Vector2? heading = null;
        // The original flattens the point's facing and turns the player to it if it isn't nothing.
        if (arrival?.Facing is { } facing && new Vector2(facing.X, facing.Z) is var flat && flat.Length() > 1e-6f)
            heading = Vector2.Normalize(flat);
        var camera = teleporter.Camera is { } cameraId ? Point(plugins, cameraId)?.Position : null;
        return new TeleportArrival(set, chunk, arrival?.Position ?? teleporter.Position, heading, camera);
    }

    private static ReferencePointObject? Point(PluginSet plugins, RecordId voxel) =>
        plugins.TryGet<MapRecord>(MapRecord.MapOf(voxel)) is { } map
            ? map.Objects.OfType<ReferencePointObject>().FirstOrDefault(o => o.Index == MapRecord.IndexOf(voxel))
            : null;

    /// <summary>
    /// Sets a body down where it arrives (<c>Actor::CheckCollision</c>): a sphere swept from 1 above to 1
    /// below where its bottom would be with its centre at <paramref name="point"/>, then, missing, from a
    /// height of 100 to −100. It rests on what it hits, 0.01 above. Null if it hits nothing either way.
    /// The original takes the first triangle its grid lists that the sweep hits; the port takes the
    /// nearest, which its own grid's order can't change.
    /// </summary>
    /// <param name="point">In the world's coordinates.</param>
    public static Vector3? Settle(CollisionWorld world, Vector3 point, float radius)
    {
        var bottom = point.Y - radius;
        if (Sweep(new Vector3(point.X, bottom + 1, point.Z), new Vector3(point.X, bottom - 1, point.Z)) is { } near)
            return near;
        return Sweep(new Vector3(point.X, 100, point.Z), new Vector3(point.X, -100, point.Z));

        Vector3? Sweep(Vector3 from, Vector3 to) =>
            SpherePhysics.SweepNearest(world, from, to, radius, false, -1, out var hit, out var normal)
                ? hit + normal * radius + new Vector3(0, 0.01f, 0)
                : null;
    }
}
