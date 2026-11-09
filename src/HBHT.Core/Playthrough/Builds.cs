using HBHT.Core.Characters;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>What a build's step raises.</summary>
public enum BuildWant
{
    /// <summary>An attribute (<see cref="BuildStep.Stat"/>), which only the post-battle window raises.</summary>
    Stat,

    /// <summary>A skill (<see cref="BuildStep.Skill"/>).</summary>
    Skill,

    /// <summary>The weapon skill of the weapon the member wields, else the best weapon skill they have.</summary>
    Weapon,

    /// <summary>The shield skill.</summary>
    Shield,

    /// <summary>Every spell the member knows, each to the step's level.</summary>
    Spells,

    /// <summary>New spells, which only a trainer teaches: learnt until the member knows as many as the step's level, the AI's five at most.</summary>
    NewSpells,
}

/// <summary>
/// One line of a build: raise <see cref="Want"/> to <see cref="Level"/>. A build's steps are bought tier by tier: everything wanted in
/// a tier, that can be bought where the member is, comes before anything in the next.
/// </summary>
/// <param name="Tier">Its tier: lower tiers are bought first.</param>
/// <param name="Want">What it raises.</param>
/// <param name="Level">The level to raise it to: a skill's or spell's level, an attribute's trained value, or how many spells to know.</param>
/// <param name="Stat">The attribute, for <see cref="BuildWant.Stat"/>.</param>
/// <param name="Skill">The skill, for <see cref="BuildWant.Skill"/>.</param>
public sealed record BuildStep(int Tier, BuildWant Want, int Level, Stat? Stat = null, Skill? Skill = null)
{
    public static BuildStep Raise(int tier, Stat stat, int to) => new(tier, BuildWant.Stat, to, Stat: stat);

    public static BuildStep Raise(int tier, Skill skill, int to) => new(tier, BuildWant.Skill, to, Skill: skill);

    public static BuildStep Weapon(int tier, int to) => new(tier, BuildWant.Weapon, to);

    public static BuildStep Shield(int tier, int to) => new(tier, BuildWant.Shield, to);

    public static BuildStep Spells(int tier, int to) => new(tier, BuildWant.Spells, to);

    public static BuildStep Learn(int tier, int count) => new(tier, BuildWant.NewSpells, count);

    public override string ToString() => Want switch
    {
        BuildWant.Stat => $"{Tier}: {Stat} to {Level}",
        BuildWant.Skill => $"{Tier}: {Skill} to {Level}",
        BuildWant.NewSpells => $"{Tier}: know {Level} spells",
        _ => $"{Tier}: {Want} to {Level}",
    };
}

/// <summary>
/// Which members a build is for, by their base skill levels: <see cref="Skill"/> at least <see cref="AtLeast"/> and, with
/// <see cref="Over"/>, above that skill's level (a skill they can never learn counting as −1). With no skill, anyone.
/// </summary>
public sealed record BuildFit(Skill? Skill = null, int AtLeast = 0, Skill? Over = null)
{
    public bool Fits(Character member)
    {
        if (Skill is not { } skill)
            return true;
        var level = member.Skills.RawBase(skill);
        if (level < AtLeast || !member.Skills.CanLearn(skill))
            return false;
        return Over is not { } over || level > member.Skills.RawBase(over);
    }
}

/// <summary>A way to spend a member's experience: who it's for, and its steps in tiers.</summary>
public sealed record Build(string Name, BuildFit For, IReadOnlyList<BuildStep> Steps)
{
    public override string ToString() => Name;
}

/// <summary>One thing to buy: an attribute's point, a skill's level or a spell's (a new spell's first).</summary>
public readonly record struct BuildPick(BuildStep Step, Stat? Stat, SkillId? Skill, SpellRecord? Spell, TrainingQuote Quote)
{
    /// <summary>Where the member stands in it now: the attribute's trained value, the skill's base level, the spell's level (0 unknown).</summary>
    public int From { get; init; }

    public override string ToString() =>
        $"{(Stat?.ToString() ?? Skill?.ToString() ?? Spell?.Name)} {From}→{From + 1} for {Quote.Exp} XP{(Quote.Gold > 0 ? $" and {Quote.Gold} gold" : "")}";
}

/// <summary>
/// The builds the bot levels the party by (docs/systems/playthrough.md, layer 5): a table, first fit first, read afresh each time from
/// what the member has, so it works for any recruit, a mod's among them. <see cref="Default"/> is the base game's; a run can be given
/// another.
/// </summary>
/// <remarks>
/// <para>
/// The prices are what drive it: a point or level from n costs (n + 1)² × the cost, so the early tiers ask little of each line and the later
/// ones finish them. Within a tier the cheapest thing that can be bought is bought first; a tier with something wanted that can't yet
/// be afforded holds the rest back, so experience is saved for it rather than spent on the next tier's. What can't be bought where the
/// member is doesn't hold a tier back: attributes at a trainer, a skill at 0 or a new spell after a battle, what the trainer doesn't
/// teach or teaches no further, what's at its most.
/// </para>
/// <para>
/// Casting is at the lesser of the spell's level and the Wizard skill, so the mages raise both; the AI fights with its first five spells
/// (<see cref="Combat.EnemyAI.Init"/>), so no one learns more than five.
/// </para>
/// </remarks>
public static class Builds
{
    /// <summary>The most spells the AI fights with: its first five action slots.</summary>
    public const int AiSpells = 5;

    // The rules the builds are weighed by (docs/systems/combat-actions.md, docs/systems/characters.md):
    // - a melee blow's chance is Strength + Endurance + Level + the weapon's hit + 6 × the weapon skill + 2 × Warrior, less the target's
    //   2 × Dexterity, Level and shield defence (the shield's defence × the shield skill, + Warrior);
    // - its damage is (the weapon's dice + Strength's tier) d6, the tiers stepping at 4, 9, 17, 24, 28, 32, 36 and 39, less protection;
    // - hit points are Endurance + Stamina + Level, and spells and skills spend Stamina;
    // - a point or level from n costs (n + 1)² × its cost: Stamina 4, Strength, Endurance, Intelligence and Willpower 12, Dexterity 24,
    //   a weapon skill 600, the shield 500, Warrior 1,000, Wizard 1,500.
    // So the weapon skill is the cheapest chance to hit (6 a level), the shield skill the cheapest defence with a big shield (a Tower
    // Shield's 6 a level, three times Dexterity's), Stamina the cheapest hit point, and Strength is worth its price at the tiers.

    /// <summary>
    /// A caster, whose Wizard is at least 2 and above their Warrior, the party's artillery and support as a D&amp;D wizard is: Wizard and
    /// their spells together, since a spell is cast at the lesser of the two; Intelligence for the casting; Stamina, which every cast
    /// spends and which is their hit points; Willpower against the foes' magic. Their weapon only once the magic is strong.
    /// </summary>
    public static readonly Build Mage = new("Mage", new BuildFit(Skill.Wizard, 2, Over: Skill.Warrior),
    [
        BuildStep.Raise(1, Skill.Wizard, 4), BuildStep.Spells(1, 4), BuildStep.Raise(1, Stat.Intelligence, 20), BuildStep.Raise(1, Stat.Stamina, 35),
        BuildStep.Learn(1, 3),
        BuildStep.Raise(2, Skill.Wizard, 6), BuildStep.Spells(2, 6), BuildStep.Raise(2, Stat.Intelligence, 24), BuildStep.Raise(2, Stat.Stamina, 45),
        BuildStep.Raise(2, Stat.Willpower, 18), BuildStep.Raise(2, Stat.Endurance, 18), BuildStep.Learn(2, AiSpells),
        BuildStep.Raise(3, Skill.Wizard, 8), BuildStep.Spells(3, 8), BuildStep.Raise(3, Stat.Intelligence, 28), BuildStep.Raise(3, Stat.Stamina, 55),
        BuildStep.Raise(3, Stat.Dexterity, 20), BuildStep.Weapon(3, 4),
        BuildStep.Raise(4, Skill.Wizard, 10), BuildStep.Spells(4, 10), BuildStep.Raise(4, Stat.Intelligence, 30), BuildStep.Raise(4, Stat.Willpower, 25),
        BuildStep.Raise(4, Stat.Endurance, 24), BuildStep.Raise(4, Stat.Stamina, 65),
    ]);

    /// <summary>
    /// A fighter who casts too, with Wizard: Alaron, whose death ends the game, so he's built to last first, as a D&amp;D party's paladin
    /// fronts the line: the weapon, the shield and Stamina as a fighter's are, then Wizard and his spells alongside, Warrior and the
    /// Strength tiers after.
    /// </summary>
    public static readonly Build Spellsword = new("Spellsword", new BuildFit(Skill.Wizard, 1),
    [
        BuildStep.Weapon(1, 5), BuildStep.Shield(1, 3), BuildStep.Raise(1, Stat.Stamina, 40), BuildStep.Raise(1, Stat.Strength, 18),
        BuildStep.Raise(1, Skill.Wizard, 3), BuildStep.Spells(1, 3), BuildStep.Learn(1, 2),
        BuildStep.Weapon(2, 7), BuildStep.Shield(2, 5), BuildStep.Raise(2, Stat.Stamina, 50), BuildStep.Raise(2, Skill.Warrior, 4),
        BuildStep.Raise(2, Stat.Strength, 24), BuildStep.Raise(2, Skill.Wizard, 5), BuildStep.Spells(2, 5), BuildStep.Raise(2, Stat.Endurance, 20),
        BuildStep.Learn(2, 4),
        BuildStep.Weapon(3, 9), BuildStep.Shield(3, 7), BuildStep.Raise(3, Skill.Warrior, 6), BuildStep.Raise(3, Skill.Wizard, 7), BuildStep.Spells(3, 7),
        BuildStep.Raise(3, Stat.Stamina, 60), BuildStep.Raise(3, Stat.Dexterity, 20), BuildStep.Learn(3, AiSpells),
        BuildStep.Weapon(4, 10), BuildStep.Shield(4, 9), BuildStep.Raise(4, Skill.Warrior, 8), BuildStep.Raise(4, Skill.Wizard, 9), BuildStep.Spells(4, 9),
        BuildStep.Raise(4, Stat.Strength, 28), BuildStep.Raise(4, Stat.Endurance, 26),
    ]);

    /// <summary>
    /// A thief, whose Thief is at least 3 and above their Warrior, a D&amp;D rogue who strikes from behind: a blow from behind adds Thief × 10
    /// to the chance and Thief × 2 to the damage, so the weapon and Thief come first; Dexterity is their defence, twice over; Stamina
    /// their hit points. Stealth is left: the AI doesn't sneak, and the scripts' Stealth check is a stub.
    /// </summary>
    public static readonly Build Rogue = new("Rogue", new BuildFit(Skill.Thief, 3, Over: Skill.Warrior),
    [
        BuildStep.Weapon(1, 5), BuildStep.Raise(1, Skill.Thief, 4), BuildStep.Raise(1, Stat.Dexterity, 25), BuildStep.Raise(1, Stat.Stamina, 35),
        BuildStep.Weapon(2, 7), BuildStep.Raise(2, Skill.Thief, 6), BuildStep.Raise(2, Stat.Dexterity, 28), BuildStep.Raise(2, Stat.Stamina, 45),
        BuildStep.Raise(2, Skill.Warrior, 2), BuildStep.Raise(2, Stat.Endurance, 16),
        BuildStep.Weapon(3, 9), BuildStep.Raise(3, Skill.Thief, 8), BuildStep.Raise(3, Stat.Strength, 17), BuildStep.Raise(3, Stat.Stamina, 55),
        BuildStep.Raise(3, Skill.Warrior, 4),
        BuildStep.Weapon(4, 10), BuildStep.Raise(4, Skill.Thief, 10), BuildStep.Raise(4, Stat.Dexterity, 30), BuildStep.Raise(4, Stat.Endurance, 22),
        BuildStep.Raise(4, Skill.Warrior, 6), BuildStep.Raise(4, Stat.Stamina, 65),
    ]);

    /// <summary>
    /// Everyone else, the front line, as a D&amp;D fighter: the weapon skill first (the cheapest chance to hit), the shield beside it (its
    /// defence multiplies the skill), Stamina (the cheapest hit points), Strength to its tiers for the damage dice, then Warrior, which adds
    /// to the chance, the defence and the protection at once, and Endurance.
    /// </summary>
    public static readonly Build Fighter = new("Fighter", new BuildFit(),
    [
        BuildStep.Weapon(1, 5), BuildStep.Shield(1, 3), BuildStep.Raise(1, Stat.Stamina, 40), BuildStep.Raise(1, Stat.Strength, 18),
        BuildStep.Raise(1, Skill.Warrior, 3),
        BuildStep.Weapon(2, 7), BuildStep.Shield(2, 5), BuildStep.Raise(2, Stat.Stamina, 50), BuildStep.Raise(2, Stat.Strength, 24),
        BuildStep.Raise(2, Skill.Warrior, 5), BuildStep.Raise(2, Stat.Endurance, 22),
        BuildStep.Weapon(3, 9), BuildStep.Shield(3, 7), BuildStep.Raise(3, Stat.Strength, 28), BuildStep.Raise(3, Skill.Warrior, 7),
        BuildStep.Raise(3, Stat.Endurance, 26), BuildStep.Raise(3, Stat.Dexterity, 20),
        BuildStep.Weapon(4, 10), BuildStep.Shield(4, 10), BuildStep.Raise(4, Stat.Strength, 32), BuildStep.Raise(4, Skill.Warrior, 10),
        BuildStep.Raise(4, Stat.Stamina, 70), BuildStep.Raise(4, Stat.Endurance, 30),
    ]);

    /// <summary>The base game's builds, first fit first; the last fits anyone.</summary>
    public static readonly IReadOnlyList<Build> Default = [Mage, Spellsword, Rogue, Fighter];

    /// <summary>The first build in <paramref name="builds"/> that fits <paramref name="member"/>, or null if none does.</summary>
    public static Build? For(Character member, IReadOnlyList<Build> builds) => builds.FirstOrDefault(b => b.For.Fits(member));

    /// <summary>
    /// The next thing <paramref name="member"/>'s build would buy, where they are: after a battle (no <paramref name="trainer"/>) or at a
    /// trainer, with <paramref name="gold"/> to spend there. Null when the build wants nothing more here, or saves for something it can't
    /// yet afford. <paramref name="skip"/> leaves out what the window has refused.
    /// </summary>
    public static BuildPick? Next(Character member, IReadOnlyList<Build> builds, Trainer? trainer, uint gold, Func<BuildPick, bool>? skip = null)
    {
        if (For(member, builds) is not { } build)
            return null;
        foreach (var tier in build.Steps.GroupBy(s => s.Tier).OrderBy(g => g.Key))
        {
            var wanted = tier.SelectMany(step => Picks(member, step, trainer, gold))
                .Where(p => p.Quote.Refusal is TrainingRefusal.None or TrainingRefusal.CantAfford && skip?.Invoke(p) != true)
                .ToList();
            if (wanted.Count == 0)
                continue;
            // The cheapest that can be afforded; with none, the tier is saved for.
            return wanted.Where(p => p.Quote.Allowed).OrderBy(p => p.Quote.Exp).ThenBy(p => p.Quote.Gold).Cast<BuildPick?>().FirstOrDefault();
        }
        return null;
    }

    /// <summary>What a step would buy next: one pick for an attribute or skill, one for each spell below its level.</summary>
    private static IEnumerable<BuildPick> Picks(Character member, BuildStep step, Trainer? trainer, uint gold)
    {
        switch (step.Want)
        {
            case BuildWant.Stat when step.Stat is { } stat:
                // Attributes are raised only after a battle.
                if (trainer is null && member.Stats.Trained(stat) < step.Level)
                    yield return new BuildPick(step, stat, null, null, Training.QuoteStat(member, stat)) { From = member.Stats.Trained(stat) };
                break;
            case BuildWant.Skill when step.Skill is { } skill:
                if (SkillPick(member, step, skill, trainer, gold) is { } pick)
                    yield return pick;
                break;
            case BuildWant.Weapon:
                if (WeaponSkill(member) is { } weapon && SkillPick(member, step, weapon, trainer, gold) is { } wielded)
                    yield return wielded;
                break;
            case BuildWant.Shield:
                if (SkillPick(member, step, SkillId.Shield, trainer, gold) is { } shield)
                    yield return shield;
                break;
            case BuildWant.Spells:
                foreach (var known in member.Spells)
                {
                    // At a trainer, only what they teach.
                    if (known.Level < step.Level && (trainer is null || trainer.Spells.Any(s => s.Spell.Id == known.Spell.Id)))
                        yield return new BuildPick(step, null, null, known.Spell, Training.QuoteSpell(member, known.Spell, trainer, gold)) { From = known.Level };
                }
                break;
            case BuildWant.NewSpells when trainer is not null && member.Spells.Count < Math.Min(step.Level, AiSpells):
                foreach (var (spell, _) in trainer.Spells)
                {
                    if (member.FindSpell(spell.Id) is null && member.CanLearn(spell) == SpellLearnability.Learnable)
                        yield return new BuildPick(step, null, null, spell, Training.QuoteSpell(member, spell, trainer, gold));
                }
                break;
        }
    }

    /// <summary>
    /// A skill's next level, if it's wanted and offered where the member is: after a battle a skill above 0, at a trainer one they teach.
    /// </summary>
    private static BuildPick? SkillPick(Character member, BuildStep step, SkillId skill, Trainer? trainer, uint gold)
    {
        var level = member.Skills.Base(skill);
        if (level >= step.Level || !member.Skills.CanLearn(skill))
            return null;
        if (trainer is null ? level <= 0 : !trainer.SkillsOffered.Contains(skill))
            return null;
        return new BuildPick(step, null, skill, null, Training.QuoteSkill(member, skill, trainer, gold)) { From = level };
    }

    /// <summary>
    /// The weapon skill a member fights with: their wielded weapon's, else the best weapon skill they can learn (the later on a tie),
    /// else none.
    /// </summary>
    public static SkillId? WeaponSkill(Character member)
    {
        if (member.Equipment.Weapon?.Item.Weapon is { } weapon && SkillId.TryFromRecord(weapon.Skill) is { IsWeapon: true } wielded)
            return wielded;
        SkillId? best = null;
        foreach (var skill in SkillId.All.Where(s => s.IsWeapon && member.Skills.CanLearn(s) && member.Rules[s].TrainingCost > 0))
        {
            if (best is not { } b || member.Skills.Base(skill) >= member.Skills.Base(b))
                best = skill;
        }
        return best;
    }
}
