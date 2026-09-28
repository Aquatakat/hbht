using System.Numerics;
using Aidyn.Core.Trek;
using Aidyn.Plugins.Assets;

namespace Aidyn.Core.Tests;

/// <summary>Small collision worlds built in code: floors, walls and ledges.</summary>
internal static class Worlds
{
    /// <summary>Grass: a surface that keeps no velocity on contact, like most ground.</summary>
    public const int Grass = 4;

    public static CollisionTriangle Triangle(Vector3 a, Vector3 b, Vector3 c, int surface = Grass, CollisionFlags flags = CollisionFlags.None) =>
        new(a, b, c, surface, CollisionRole.Normal, flags);

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
        world.Add("ground", Floor(0, -20, x, -20, 20), Vector3.Zero);
        world.Add("face", Wall(x, height), Vector3.Zero);
        world.Add("top", Floor(height, x, x + 20, -20, 20), Vector3.Zero);
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
        world.Add("ground", Floor(0, -50, 60, -50, 60), Vector3.Zero);
        return world;
    }
}

/// <summary>An animation whose root moves a fixed amount a tick while he's asked to move.</summary>
internal sealed class SteadyMotion(float perTick) : IRootMotion
{
    public float Tick(int request) => request == PlayerMotion.Idle ? 0 : perTick;
}
