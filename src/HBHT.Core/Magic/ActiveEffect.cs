using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Magic;

/// <summary>
/// A spell's or potion's effects on a character while they last (ADR 0020): the original's timed effect record (<c>EffectRecord</c>,
/// 0x18 bytes, 15 at <c>entity+0x34</c>) and potion record (<c>PotionRecord</c>, 0xC bytes, 7 at <c>entity+0x38</c>). A worn
/// enchantment is one that never runs down.
/// </summary>
public sealed class ActiveEffect
{
    /// <summary>A timer that never runs down: a worn enchantment, or one that lasts the battle (<c>0xFFFFFFFF</c>).</summary>
    public const uint Never = uint.MaxValue;

    internal ActiveEffect(RecordId source, IReadOnlyList<Effect> effects, int power, MagicSchool school, int margin, uint timer, bool worn = false)
    {
        Source = source;
        Effects = effects;
        Power = (byte)power;
        School = school;
        Margin = (sbyte)margin;
        Timer = timer;
        Worn = worn;
        Amounts = new int[effects.Count];
    }

    /// <summary>The spell or potion it came from (<c>+0x00</c>, the spell's kind or the potion's).</summary>
    public RecordId Source { get; }

    public IReadOnlyList<Effect> Effects { get; }

    /// <summary>The cast's effective level, which its magnitudes are at (<c>+0x01</c>); an enchantment's level; a potion's 0.</summary>
    public int Power { get; }

    /// <summary>The casting spell's school (<c>+0x02</c>), which a dispel matches; a potion's is unschooled.</summary>
    public MagicSchool School { get; }

    /// <summary>
    /// The cast's margin after the target's resist step (<c>+0x03</c>), as a signed byte: what a dispel weighs, Photosynthesis
    /// heals and Cheat Death heals twice. An enchantment's is its level. Wraith Touch's holds the stat it drained, as the
    /// original's does.
    /// </summary>
    public int Margin { get; internal set; }

    /// <summary>Whether it's a debuff (<c>+0x04</c>), which the AI's morale and dispel scoring read.</summary>
    public bool Harmful { get; internal set; }

    /// <summary>What's left, in the game clock's ticks (<c>+0x08</c>), or <see cref="Never"/>.</summary>
    public uint Timer { get; internal set; }

    /// <summary>It's a worn item's enchantment, which a battle's end puts back on.</summary>
    public bool Worn { get; }

    /// <summary>
    /// What a battle shows of it (the record's <c>+0x14</c>, a potion's <c>+0x08</c>: <c>CombatSpellFx</c>'s handle), which its end
    /// tells (<c>EffectRecord.FreeVisual</c>, <c>0x8007AB9C</c>); null outside a battle, or for one no cast or drink showed.
    /// </summary>
    public IEffectVisual? Visual { get; internal set; }

    public bool IsPotion => Source.Type == RecordTypes.Item;

    /// <summary>
    /// What each effect took when it started, where ending it gives that back rather than its magnitude: the morale taken, the
    /// stat drained.
    /// </summary>
    internal int[] Amounts { get; }

    /// <summary>The stat a <see cref="DrainStatEffect"/> drew.</summary>
    internal int DrainedStat { get; set; }

    /// <summary>
    /// Time passing for it (<c>EffectRecord.Age</c>, <c>0x8007ABD8</c>; potions' <c>0x80079DF4</c>): a combat round
    /// (<paramref name="unit"/> 0) takes off <paramref name="round"/>, trek takes off the clock's rate times the ticks. One that
    /// had run down already ends now: the caller removes it.
    /// </summary>
    /// <returns>Whether it has ended.</returns>
    internal bool Age(uint unit, int amount, int round)
    {
        if (Timer == Never)
            return false;
        if (Timer == 0)
            return true;
        if (unit == 0)
        {
            Timer = Timer <= (uint)round ? 0 : Timer - (uint)round;
        }
        else
        {
            var step = (ulong)unit * (uint)amount;
            Timer = step < Timer ? Timer - (uint)step : 0;
        }
        return false;
    }

    public override string ToString() => $"{Source} power {Power} margin {Margin} timer {Timer}";
}

/// <summary>What a battle shows of an effect while it lasts (<c>CombatSpellFx</c>'s spell visual), which hears when it ends.</summary>
public interface IEffectVisual
{
    /// <summary>The effect has come off (<c>CombatSpellFx.EndEffect</c>, <c>0x8008C7A4</c>).</summary>
    void Ended();
}
