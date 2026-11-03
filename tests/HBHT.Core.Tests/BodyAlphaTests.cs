using System.Numerics;
using System.Text.Json;
using HBHT.Core.Characters;
using HBHT.Core.Combat;
using HBHT.Core.Movement;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Tests;

/// <summary>
/// A combat body's alpha (<c>+0x70C</c>, <c>EntityBody.ShadowAlpha</c>, <c>0x8006C798</c>) and how a freed body fades from it
/// (<c>ProcessPlayers</c>, <c>0x80012CA4</c>–<c>0x80012D40</c>).
/// </summary>
public sealed class BodyAlphaTests
{
    private static Combatant Of(byte? alpha)
    {
        var entity = Characters.Alaron() with { Id = Entity(0x2000) };
        var look = new EntityBody(RecordId.Hex(RecordTypes.Actor, 0x33DB), 0.7f, 1, 1);
        if (alpha is { } a)
            look = look with { Alpha = a };
        return new Combatant(0, Characters.Create(entity), look, new Body(Vector3.Zero, 0.7f), CombatantFlags.Active);
    }

    [Fact]
    public void ABodyIsAsOpaqueAsItsEntitysAlphaWhileItIsAlive()
    {
        Assert.Equal(255, Of(null).Alpha);
        Assert.Equal(127, Of(127).Alpha);
    }

    // The original fades a freed body from 255 whatever its alpha was; the port fades it from its own, so it doesn't jump to
    // opaque as it starts (the user's choice).
    [Theory]
    [InlineData(255, 120, 255)]
    [InlineData(255, 60, 255)]
    [InlineData(255, 59, 250)]
    [InlineData(255, 30, 127)]
    [InlineData(255, 1, 4)]
    [InlineData(255, 0, 0)]
    [InlineData(127, 120, 127)]
    [InlineData(127, 60, 127)]
    [InlineData(127, 59, 124)]
    [InlineData(127, 30, 63)]
    [InlineData(127, 1, 2)]
    [InlineData(127, 0, 0)]
    public void AFreedBodyFadesToNothingFromItsOwnAlpha(int alpha, int left, int expected)
    {
        var c = Of((byte)alpha);
        c.Freed = true;
        c.Fading = left;
        Assert.Equal(expected, c.Alpha);
    }

    [Fact]
    public void ABodyRecordWithoutAnAlphaIsOpaque()
    {
        var plain = JsonSerializer.Deserialize<EntityBody>("""{"actor": "aidyn:actor/0x302A", "radius": 1, "scale": 1, "height": 1}""",
            PluginJson.Options)!;
        Assert.Equal(255, plain.Alpha);
        var half = JsonSerializer.Deserialize<EntityBody>("""{"actor": "aidyn:actor/0x302A", "radius": 1, "scale": 1, "height": 1, "alpha": 127}""",
            PluginJson.Options)!;
        Assert.Equal(127, half.Alpha);
    }
}
