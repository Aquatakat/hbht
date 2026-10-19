using Aidyn.Core.Calendar;
using Aidyn.Core.Combat;

namespace Aidyn.Core.Magic;

/// <summary>
/// What effects need of the battle they're cast in, beyond the characters and combatants: its part of the day, its weather,
/// fleeing, the placed point, and what the player is shown. <see cref="Battle"/> is one; trek has none, and the effects that need
/// one do nothing there.
/// </summary>
public interface IMagicHost
{
    /// <summary>The combatants in play, whom Darkness and Light reach.</summary>
    IEnumerable<Combatant> Combatants { get; }

    /// <summary>The battle's part of the day, which the cast's chance reads (<c>World+2</c>).</summary>
    PartOfDay PartOfDay { get; }

    /// <summary>Holds the battle at night or by day (<c>Entity.SetLight</c>: <c>World.LockPartOfDay</c>, <c>World+2</c>).</summary>
    void HoldPartOfDay(PartOfDay partOfDay);

    /// <summary>Lets the part of the day go again, back to the battle's own (<c>World.UnlockPartOfDay</c>, <c>Combat.Current[0]</c>).</summary>
    void ReleasePartOfDay();

    /// <summary>The moon's phase, 0–3, which a Lunar caster's chance reads (<c>World+3</c>).</summary>
    int MoonPhase { get; }

    /// <summary>Sets combat's class of bad weather (<c>World+4</c>): Wind's end.</summary>
    void SetWeatherClass(int weatherClass);

    /// <summary>The Troubadour's bonus in play (<c>Combat.Current+0x13</c>), 0 with none.</summary>
    int TroubadourBonus { get; }

    /// <summary>Whether reserves are still to come, while the most enemies disabled at once holds (<c>Combat+7 != 0xC</c>).</summary>
    bool ReservesLeft { get; }

    /// <summary>The first combatant whose sheet is <paramref name="sheet"/>, if it's in the battle.</summary>
    Combatant? CombatantOf(Characters.Character sheet);

    /// <summary>A combatant leaves as one who flees, if the battle allows it (<c>Combatant.Leave</c>). Whether it left.</summary>
    bool Flee(Combatant combatant);

    /// <summary>The caster moves to the placed point if no one stands there (<c>Entity.Teleport</c>). Whether it moved.</summary>
    bool Teleport(Combatant caster);

    /// <summary>Whether Teleportation is barred: the Shadow is in the battle.</summary>
    bool TeleportBarred { get; }

    /// <summary>Whether potions heal nothing: the Shadow is in the battle (<c>Entity.HealPercent</c>, <c>0x800715DC</c>).</summary>
    bool HealingBarred => false;

    /// <summary>Shows the target's details at the cast's level (Sense Aura, <c>0x80070070</c>).</summary>
    void SenseAura(Combatant target, int level);

    /// <summary>Shows the moon's or the sun's phase on the HUD (<c>0x800254C8</c>, <c>0x800254B4</c>).</summary>
    void RevealPhase(bool sun);

    /// <summary>A line for the battle's log.</summary>
    void Log(string line);
}
