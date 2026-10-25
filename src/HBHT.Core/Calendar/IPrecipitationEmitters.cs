using System.Numerics;

namespace HBHT.Core.Calendar;

/// <summary>
/// The rain's or snow's pair of particle emitters, which <see cref="Weather.Draw"/> makes, steers, keeps up, forgets
/// and frees, as <c>ProcessWeather</c> (<c>0x8001e7c0</c> in 1.0) does through its one pointer, <c>Weather.Data</c> +
/// 0x14 (<c>0x800d8430</c>), which <see cref="Weather.Emitting"/> stands for. The particles' side is
/// <see cref="Particles.WeatherParticles"/>.
/// </summary>
public interface IPrecipitationEmitters
{
    /// <summary>Makes a pair for what falls, returning whether there was room (<c>WeatherParticles.Make</c>, <c>0x8001e62c</c>).</summary>
    bool Make(Precipitation falling);

    /// <summary>Steers the pair by what falls, how hard (before this tick's change) and the wind.</summary>
    void Steer(Precipitation falling, float intensity, Vector3 wind);

    /// <summary>
    /// Fills the air with what falls at once, on the frame the pair is made while it's already falling, as after trek
    /// rebuilds (the user's choice; the original's new pair starts empty, so its drops take a couple of seconds to come).
    /// </summary>
    void FillAir() { }

    /// <summary>Keeps the pair alive while what falls is what the weather wants: both lives at 300.</summary>
    void Keep();

    /// <summary>Forgets the pair, which runs out its life on its own.</summary>
    void Forget();

    /// <summary>Frees the pair at once, where the sky isn't drawn.</summary>
    void Free();
}
