using System.Numerics;
using HBHT.Core.Chunks;
using HBHT.Core.Movement;
using HBHT.Core.Physics;
using HBHT.Plugins;
using HBHT.Plugins.Assets;

namespace HBHT.TestSupport;

/// <summary>Small collision worlds built in code: floors, walls and ledges.</summary>
public static class Worlds
{
    /// <summary>Grass: a surface that keeps no velocity on contact, like most ground.</summary>
    public const int Grass = 4;

    /// <summary>
    /// A key to put a part of a test world under: chunk (0, <paramref name="column"/>) of map set <paramref name="mapSet"/>.
    /// The worlds built here put theirs at columns from <see cref="OwnParts"/>, so a test can add its own beside them.
    /// </summary>
    public static ChunkKey Chunk(int column = 0, int mapSet = 0) => new(RecordId.Decimal(RecordTypes.MapSet, mapSet), 0, column);

    /// <summary>The first column the worlds built here put their parts at.</summary>
    public const int OwnParts = 100;

    public static CollisionTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, int surface = Grass, CollisionFlags flags = CollisionFlags.None) =>
        new(a, b, c, surface, CollisionRole.Normal, flags);

    /// <summary>Triangles as a scene draws them, for a <see cref="VisibleWorld"/>: ones rain splashes on, or that absorb it.</summary>
    public static IEnumerable<DrawnTriangle> Drawn(this IEnumerable<CollisionTriangle> triangles, bool absorbs = false) =>
        triangles.Select(t => new DrawnTriangle(t.A, t.B, t.C, absorbs));

    /// <summary>A level rectangle facing up.</summary>
    public static IEnumerable<CollisionTriangle> Floor(float y, float minX, float maxX, float minZ, float maxZ, int surface = Grass)
    {
        yield return Triangle(new(minX, y, minZ), new(minX, y, maxZ), new(maxX, y, minZ), surface);
        yield return Triangle(new(maxX, y, minZ), new(minX, y, maxZ), new(maxX, y, maxZ), surface);
    }

    /// <summary>A wall across X = <paramref name="x"/>, facing −X, from the ground up to <paramref name="height"/>.</summary>
    public static IEnumerable<CollisionTriangle> Wall(float x, float height, float minZ = -20, float maxZ = 20, CollisionFlags flags = CollisionFlags.None)
    {
        yield return Triangle(new(x, 0, minZ), new(x, 0, maxZ), new(x, height, minZ), flags: flags);
        yield return Triangle(new(x, height, minZ), new(x, 0, maxZ), new(x, height, maxZ), flags: flags);
    }

    /// <summary>Flat ground, rising at X = <paramref name="x"/> to a step <paramref name="height"/> high.</summary>
    public static CollisionWorld Ledge(float x, float height)
    {
        var world = new CollisionWorld();
        world.Add(Chunk(OwnParts), Floor(0, -20, x, -20, 20), Vector3.Zero);
        world.Add(Chunk(OwnParts + 1), Wall(x, height), Vector3.Zero);
        world.Add(Chunk(OwnParts + 2), Floor(height, x, x + 20, -20, 20), Vector3.Zero);
        return world;
    }

    /// <summary>Level ground from <paramref name="min"/> to <paramref name="max"/> on both axes, placed at <paramref name="origin"/>.</summary>
    public static CollisionWorld Ground(float min, float max, Vector3 origin = default)
    {
        var world = new CollisionWorld();
        world.Add(Chunk(OwnParts), Floor(0, min, max, min, max), origin);
        return world;
    }

    /// <summary>
    /// Flat ground. Its two triangles meet along X + Z = 10, away from where tests stand: the
    /// game counts a point on an edge as inside only half its triangles, so a sphere exactly on
    /// the diagonal falls through.
    /// </summary>
    public static CollisionWorld Flat()
    {
        var world = new CollisionWorld();
        world.Add(Chunk(OwnParts), Floor(0, -50, 60, -50, 60), Vector3.Zero);
        return world;
    }
}

/// <summary>An animation whose root moves a fixed amount a tick while he's asked to move.</summary>
public sealed class SteadyMotion(float perTick) : IRootMotion
{
    public float Tick(int request) => request == Body.Idle ? 0 : perTick;
}
