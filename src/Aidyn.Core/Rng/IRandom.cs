namespace Aidyn.Core.Rng;

/// <summary>
/// A stream of random numbers (ADR 0006). Rules draw from a stream they're given, never from a
/// global one, so tests can feed them the values the console drew.
/// </summary>
public interface IRandom
{
    /// <summary>The next 32-bit value.</summary>
    uint Next();
}

/// <summary>
/// The original's generator (<c>random.cpp</c>): a 32-bit linear congruential generator, which
/// every stream in the port uses.
/// </summary>
public sealed class Lcg(uint seed = Lcg.BootSeed) : IRandom
{
    /// <summary>What the original seeds its stream with at boot (<c>appProc_init</c>). Saves don't store it.</summary>
    public const uint BootSeed = 0x3DBB6CD;

    public uint Seed { get; set; } = seed;

    public uint Next() => Seed = Seed * 0x19660D + 0x3C6EF35F;
}

public static class RandomExtensions
{
    /// <summary>A number from 0 to <paramref name="n"/> − 1: (n × next) >> 32 (<c>Random::MultiByB</c>). 0 draws nothing.</summary>
    public static uint Below(this IRandom random, uint n) => n == 0 ? 0 : (uint)((ulong)n * random.Next() >> 32);

    /// <summary>
    /// A number from <paramref name="a"/> to <paramref name="b"/> inclusive, in either order
    /// (<c>Random::Range</c>). Equal bounds draw nothing.
    /// </summary>
    public static int Range(this IRandom random, int a, int b)
    {
        if (a == b)
            return a;
        var (low, high) = a < b ? (a, b) : (b, a);
        return (int)random.Below((uint)(high - low + 1)) + low;
    }

    /// <summary>
    /// A number from 0 to <paramref name="x"/> (<c>RAND.GetFloat0ToX</c>, checked in 1.0's code): the
    /// next value as an unsigned number, rounded to a float, times 2⁻³² and then <paramref name="x"/>.
    /// It reaches <paramref name="x"/> itself for the top 128 values. 0 draws nothing.
    /// </summary>
    public static float Float(this IRandom random, float x)
    {
        if (x == 0)
            return x;
        return (float)(double)random.Next() * (1f / 4294967296f) * x;
    }

    /// <summary>
    /// The sum of <paramref name="dice"/> rolls of 1 to <paramref name="sides"/> (<c>RollD</c>), as a u16.
    /// Each roll is <c>rand_range</c>, which is <see cref="Range"/> for these bounds.
    /// </summary>
    public static int RollD(this IRandom random, int dice, int sides)
    {
        var sum = 0;
        for (var i = 0; i < (byte)dice; i++)
            sum = (ushort)(sum + random.Range(1, (byte)sides));
        return sum;
    }
}

/// <summary>
/// The port's random streams, each the original's generator (ADR 0006): combat, loot, the world
/// (weather and camp ambushes), wandering, and cosmetic effects, so that drawing for one can't
/// shift another. A new game seeds them all as the original seeds its one stream at boot; a save
/// keeps their states.
/// </summary>
public sealed class RandomStreams
{
    public Lcg Combat { get; } = new();

    public Lcg Loot { get; } = new();

    public Lcg World { get; } = new();

    public Lcg Wander { get; } = new();

    public Lcg Cosmetic { get; } = new();

    /// <summary>
    /// The stream bodies' animations roll their branches on (<c>ActorStateMachine.Tick</c>'s, <c>0x800e69c0</c>): one
    /// for every body, as the original's. The original starts it at 0, never seeds it and doesn't save it, so neither
    /// does the port: it starts at 0 with every new or loaded game.
    /// </summary>
    public Lcg Animation { get; } = new(0);
}
