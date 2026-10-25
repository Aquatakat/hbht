namespace HBHT.Rom.Engine;

/// <summary>
/// The floats casting reads from the code segment (<see cref="CodeTable.CastingFactors"/>, <c>0x800D29CC</c> in 1.0): the cast chance's
/// factors (<c>Combatant.CastChance</c>, <c>0x80063CA0</c>), the spell-improvement factor (<c>ResolveCastRoll</c>, <c>0x80063DF4</c>) and the
/// resist roll's aspect factors (<c>ResistRoll</c>, <c>0x80064298</c>). Identical in all three ROMs.
/// </summary>
/// <param name="ByMoon">A Lunar caster's factor by the moon's phase (<c>0x800D29CC</c>, 4 floats).</param>
/// <param name="ByPartOfDay">Anyone else's by the part of the day, night last (<c>0x800D29DC</c>, 5 floats).</param>
/// <param name="Improvement">The chance of a spell's improving, per point (<c>0x800D29F0</c>).</param>
/// <param name="SameAspect">The resist roll's factor when the target's aspect is the caster's (<c>0x800D29F8</c>).</param>
/// <param name="OtherAspect">The resist roll's factor when it isn't (<c>0x800D29FC</c>).</param>
public sealed record CastingFactors(
    IReadOnlyList<float> ByMoon, IReadOnlyList<float> ByPartOfDay, float Improvement, float SameAspect, float OtherAspect)
{
    public static CastingFactors Read(RomImage rom)
    {
        var data = rom.Table(CodeTable.CastingFactors);
        // After the improvement factor (+0x24) comes 2^31 as a float (+0x28), the constant an unsigned int converts with.
        return new CastingFactors(
            [.. Be.F32s(data[..0x10])], [.. Be.F32s(data[0x10..0x24])], Be.F32(data, 0x24), Be.F32(data, 0x2C), Be.F32(data, 0x30));
    }
}
