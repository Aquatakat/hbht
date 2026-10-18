using Aidyn.Core.Characters;
using Aidyn.Core.Items;

namespace Aidyn.Core.Tests;

/// <summary>A battle's end takes a character's potion effects off but Stealth's (<c>Entity.ClearEffects</c>, <c>0x80071830</c>).</summary>
public sealed class EndPotionEffectsTests
{
    private static Character Alaron() => Characters.Create(Characters.Alaron());

    [Fact]
    public void EveryPotionButTheKeptOneEndsAndIsUndone()
    {
        var alaron = Alaron();
        var strength = alaron.Stats.Base(Stat.Strength);
        var dexterity = alaron.Stats.Base(Stat.Dexterity);
        var loremaster = alaron.Skills.Modified(Skill.Loremaster);
        var diplomat = alaron.Skills.Modified(Skill.Diplomat);
        var stealth = alaron.Skills.Modified(Skill.Stealth);
        foreach (var potion in new[] { Potion.Strength, Potion.Dexterity, Potion.Clarity, Potion.Charisma, Potion.Stealth, Potion.Healing })
            alaron.ApplyPotionEffect(potion, 0, 1000);
        Assert.Equal(strength + 10, alaron.Stats.Base(Stat.Strength));
        Assert.Equal(stealth + 3, alaron.Skills.Modified(Skill.Stealth));

        alaron.EndPotionEffects(keep: Potion.Stealth);

        Assert.Equal([Potion.Stealth], alaron.PotionEffects.Select(p => p.Potion));
        Assert.Equal(strength, alaron.Stats.Base(Stat.Strength));
        Assert.Equal(dexterity, alaron.Stats.Base(Stat.Dexterity));
        Assert.Equal(loremaster, alaron.Skills.Modified(Skill.Loremaster));
        Assert.Equal(diplomat, alaron.Skills.Modified(Skill.Diplomat));
        // Stealth's +3 stays on, still counting down.
        Assert.Equal(stealth + 3, alaron.Skills.Modified(Skill.Stealth));
        Assert.True(alaron.HasPotionEffect(Potion.Stealth));
    }

    [Fact]
    public void StrengthsTenPointsComeOffTheBase()
    {
        var alaron = Alaron();
        var modified = alaron.Stats.Modified(Stat.Strength);
        alaron.ApplyPotionEffect(Potion.Strength, 0, 1000);
        Assert.Equal(modified + 10, alaron.Stats.Modified(Stat.Strength));

        alaron.EndPotionEffects(keep: Potion.Stealth);

        Assert.Equal(modified, alaron.Stats.Modified(Stat.Strength));
        Assert.Empty(alaron.PotionEffects);
    }

    [Fact]
    public void TheKeptPotionsTimerIsLeftAlone()
    {
        var alaron = Alaron();
        alaron.ApplyPotionEffect(Potion.Stealth, 0, 1234);
        alaron.ApplyPotionEffect(Potion.Strength, 0, 99);

        alaron.EndPotionEffects(keep: Potion.Stealth);

        Assert.Equal(1234u, Assert.Single(alaron.PotionEffects).Timer);
    }

    [Fact]
    public void ThePotionsLeftKeepTheirOrder()
    {
        var alaron = Alaron();
        foreach (var potion in new[] { Potion.Clarity, Potion.Stealth, Potion.Charisma, Potion.Stealth })
            alaron.ApplyPotionEffect(potion, 0, 1000);

        alaron.EndPotionEffects(keep: Potion.Stealth);

        Assert.Equal([Potion.Stealth, Potion.Stealth], alaron.PotionEffects.Select(p => p.Potion));
    }

    [Fact]
    public void WithNoPotionsNothingChanges()
    {
        var alaron = Alaron();
        var strength = alaron.Stats.Base(Stat.Strength);

        alaron.EndPotionEffects(keep: Potion.Stealth);

        Assert.Empty(alaron.PotionEffects);
        Assert.Equal(strength, alaron.Stats.Base(Stat.Strength));
    }
}
