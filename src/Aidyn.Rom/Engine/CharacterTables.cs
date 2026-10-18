namespace Aidyn.Rom.Engine;

/// <summary>
/// How characters level and train, read from the code segment: the experience each level starts at,
/// each stat's caps, and what raising a stat, skill or weapon skill costs.
/// </summary>
/// <param name="LevelExperience">
/// The experience each level starts at, levels 0 to 40 (<c>EXP_TNL</c>). The table ends with a 0,
/// which isn't kept.
/// </param>
/// <param name="BaseStatCaps">
/// The most each stat's base value reads as, by stat: Intelligence, Willpower, Dexterity, Endurance,
/// Strength, Stamina, Level (<c>base_stat_cap</c>).
/// </param>
/// <param name="ModdedStatCaps">The most each stat reads as with gear and spells, in the same order (<c>modded_stat_cap</c>).</param>
/// <param name="StatTrainingCosts">
/// Raising a stat to level <c>n</c> costs <c>n²</c> times its entry here in experience
/// (<c>stat_multiplier</c>, which <c>CharStats::GetTraningPrice</c> copies to its stack). The Level's
/// 500 is never used: level isn't trained.
/// </param>
/// <param name="SkillTrainingCosts">
/// The 12 skills' experience multipliers, Alchemist to Wizard, which <c>CharSkills::GetSkillXpMod</c>
/// copies from rodata.
/// </param>
/// <param name="WeaponTrainingCosts">
/// The 11 weapon classes' multipliers, Bite to Tusk, from <c>CharSkills::GetWeaponXpMod</c>. The
/// monsters' natural weapons cost nothing.
/// </param>
/// <remarks>
/// The decomp's C source has Merchant at 7500 and Ranger at 750. The ROMs, and the debug build's
/// machine code, have 750 and 1000, so the source is wrong there.
/// </remarks>
public sealed record CharacterTables(
    IReadOnlyList<int> LevelExperience,
    IReadOnlyList<int> BaseStatCaps,
    IReadOnlyList<int> ModdedStatCaps,
    IReadOnlyList<int> StatTrainingCosts,
    IReadOnlyList<int> SkillTrainingCosts,
    IReadOnlyList<int> WeaponTrainingCosts)
{
    /// <summary>The attributes and the level.</summary>
    public const int StatCount = 7;

    public const int SkillCount = 12;

    public const int WeaponClassCount = 11;

    /// <summary>The highest level there is: <see cref="LevelExperience"/> has 41 entries, from level 0.</summary>
    public const int MaxLevel = 40;

    public static CharacterTables Read(RomImage rom)
    {
        var levels = Words(rom, CodeTable.LevelExp);
        if (levels.Count != MaxLevel + 2 || levels[^1] != 0)
            throw new InvalidDataException($"The level experience table has {levels.Count} entries, ending with {levels[^1]}.");
        var caps = Words(rom, CodeTable.StatCaps);
        if (caps.Count != StatCount * 2)
            throw new InvalidDataException($"The stat caps table has {caps.Count} entries, not {StatCount * 2}.");
        return new CharacterTables(
            levels.Take(MaxLevel + 1).ToList(),
            caps.Take(StatCount).ToList(),
            caps.Skip(StatCount).ToList(),
            Words(rom, CodeTable.StatTrainingCosts, StatCount),
            Words(rom, CodeTable.SkillTrainingCosts, SkillCount),
            Words(rom, CodeTable.WeaponTrainingCosts, WeaponClassCount));
    }

    private static List<int> Words(RomImage rom, CodeTable table, int? count = null)
    {
        var data = rom.Table(table);
        if (count is { } n && data.Length != n * 4)
            throw new InvalidDataException($"The {table} table is {data.Length} bytes, not {n * 4}.");
        return [.. Be.S32s(data)];
    }
}
