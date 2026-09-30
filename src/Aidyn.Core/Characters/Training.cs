using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Characters;

/// <summary>
/// Someone who teaches: their skills and spells, as their entity record has them
/// (<c>init_skill_trainer</c>). A trainer teaches a skill or spell up to their own level of it.
/// </summary>
public sealed class Trainer
{
    private Trainer(RecordId entity, CharacterSkills skills, IReadOnlyList<(SpellRecord Spell, int Level)> spells, bool free)
    {
        Entity = entity;
        Skills = skills;
        Spells = spells;
        Free = free;
    }

    public RecordId Entity { get; }

    /// <summary>Their skills, whose capped base levels are what they teach up to.</summary>
    public CharacterSkills Skills { get; }

    /// <summary>The spells they teach, up to these levels.</summary>
    public IReadOnlyList<(SpellRecord Spell, int Level)> Spells { get; }

    /// <summary>They charge no gold (<see cref="TrainingPrices.FreeTrainers"/>).</summary>
    public bool Free { get; }

    public static Trainer From(EntityRecord entity, CharacterRules rules, Func<RecordId, SpellRecord> spells) => new(
        entity.Id,
        new CharacterSkills(rules, Character.SkillLevels(entity)),
        [.. entity.Spells.Select(s => (spells(s.Spell), (int)(byte)s.Level))],
        rules.Training.FreeTrainers.Contains(entity.Id));

    /// <summary>What they teach, in the skill screen's order: the skills, weapon skills and shield they're above level 0 in.</summary>
    public IEnumerable<SkillId> SkillsOffered => SkillId.All.Where(s => Skills.Base(s) > 0);
}

/// <summary>Why training was refused, with the message the original shows.</summary>
public enum TrainingRefusal
{
    None,
    /// <summary>The character can never learn it ("SkillTrainCant"), or can't learn spells at all.</summary>
    CantLearn,
    /// <summary>The trainer knows it no better ("SkillTrainCantTeach").</summary>
    TrainerKnowsNoMore,
    /// <summary>It's at its most ("SkillTrainMax", "StatTrainMax").</summary>
    AtMost,
    /// <summary>The spell needs a higher Wizard skill.</summary>
    WizardTooLow,
    /// <summary>The spell is of another school.</summary>
    WrongSchool,
    /// <summary>Not enough experience, or at a trainer, gold. The original shows the prices.</summary>
    CantAfford,
}

/// <summary>A training price, or why it's refused (the <c>Confirm</c> of each training screen).</summary>
/// <param name="Exp">What it costs in experience.</param>
/// <param name="Gold">What it costs the party in gold. Always 0 after a battle.</param>
public readonly record struct TrainingQuote(TrainingRefusal Refusal, uint Exp, uint Gold)
{
    public bool Allowed => Refusal == TrainingRefusal.None;
}

/// <summary>
/// Spending experience on a character (<c>WidgetStatTrain</c>, <c>WidgetSkillTrain</c>,
/// <c>WidgetSpellTrain</c>). After a battle, with no trainer, a character raises its attributes,
/// the skills it's above level 0 in and the spells it knows, for experience alone. At a trainer, it
/// raises what the trainer teaches, up to the trainer's level, for less experience and gold, and
/// can learn new spells; attributes aren't offered.
/// </summary>
public static class Training
{
    /// <summary>What the skill screen lists: the trainer's skills, or the character's own above level 0.</summary>
    public static IEnumerable<SkillId> SkillsOffered(Character student, Trainer? trainer) =>
        trainer?.SkillsOffered ?? SkillId.All.Where(s => student.Skills.Base(s) > 0);

    /// <summary>
    /// What raising a skill a level costs, or why it's refused (<c>WidgetSkillTrain::Confirm</c>).
    /// </summary>
    /// <param name="gold">The party's gold.</param>
    public static TrainingQuote QuoteSkill(Character student, SkillId skill, Trainer? trainer, uint gold)
    {
        var rules = student.Rules;
        var level = student.Skills.Base(skill);
        var goldPrice = level > 0 ? (uint)rules.GoldPrice(student.Skills.RawBase(skill)) : (uint)rules.Training.FirstLevelGold;
        var expPrice = Discount(student.Skills.TrainingCost(skill), trainer, rules);
        // After a battle, the original passes 255 as the teacher's level.
        var teaches = trainer?.Skills.Base(skill) ?? 255;
        if (level == CharacterSkills.CantLearn)
            return new(TrainingRefusal.CantLearn, expPrice, goldPrice);
        if (teaches <= level)
            return new(TrainingRefusal.TrainerKnowsNoMore, expPrice, goldPrice);
        if (level >= rules[skill].BaseCap)
            return new(TrainingRefusal.AtMost, expPrice, goldPrice);
        return Afford(student, trainer, expPrice, goldPrice, gold);
    }

    /// <summary>
    /// Raises a skill a level if the quote allows, taking the experience and gold
    /// (<c>WidgetSkillTrain::Purchase</c>). Returns the quote.
    /// </summary>
    /// <remarks>
    /// When a skill goes from 0 to 1, the original also applies the equipped gear's bonus to it,
    /// which comes with equipment (2.3).
    /// </remarks>
    public static TrainingQuote TrainSkill(Character student, SkillId skill, Trainer? trainer, ref uint gold)
    {
        var quote = QuoteSkill(student, skill, trainer, gold);
        if (!quote.Allowed || student.Skills.IsCapped(skill))
            return quote;
        var before = student.Skills.Base(skill);
        student.Skills.Train(skill, 1);
        if (student.Skills.Base(skill) == before)
            return quote;
        student.SpendableExp -= quote.Exp;
        gold -= quote.Gold;
        return quote;
    }

    /// <summary>
    /// What raising an attribute a point costs, or why it's refused (<c>WidgetStatTrain::Confirm</c>).
    /// Only after a battle, and never the level.
    /// </summary>
    public static TrainingQuote QuoteStat(Character student, Stat stat)
    {
        if (student.Stats.TrainingCost(stat) is not { } cost)
            return new(TrainingRefusal.CantLearn, 0, 0);
        var price = (uint)cost;
        if (student.Stats.IsCapped(stat))
            return new(TrainingRefusal.AtMost, price, 0);
        return student.SpendableExp < price ? new(TrainingRefusal.CantAfford, price, 0) : new(TrainingRefusal.None, price, 0);
    }

    /// <summary>Raises an attribute a point if the quote allows (<c>WidgetStatTrain::Purchase</c>).</summary>
    public static TrainingQuote TrainStat(Character student, Stat stat)
    {
        var quote = QuoteStat(student, stat);
        if (!quote.Allowed)
            return quote;
        var before = student.Stats.TrainedCapped(stat);
        student.Stats.Train(stat, 1);
        if (student.Stats.TrainedCapped(stat) != before)
            student.SpendableExp -= quote.Exp;
        return quote;
    }

    /// <summary>What the spell screen lists: the trainer's spells, or the character's own.</summary>
    public static IEnumerable<SpellRecord> SpellsOffered(Character student, Trainer? trainer) =>
        trainer?.Spells.Select(s => s.Spell) ?? student.Spells.Select(s => s.Spell);

    /// <summary>
    /// What learning a spell, or raising one a level, costs, or why it's refused
    /// (<c>WidgetSpellTrain::Confirm</c>). A spell the character doesn't know can only come from a trainer.
    /// </summary>
    public static TrainingQuote QuoteSpell(Character student, SpellRecord spell, Trainer? trainer, uint gold)
    {
        var rules = student.Rules;
        var wizard = student.Skills.Base(Skill.Wizard);
        if (wizard == CharacterSkills.CantLearn)
            return new(TrainingRefusal.CantLearn, 0, 0);
        uint expPrice, goldPrice;
        if (student.FindSpell(spell.Id) is { } known)
        {
            // After a battle, the original passes 255 as the teacher's level.
            var teaches = trainer is null ? 255 : trainer.Spells.FirstOrDefault(s => s.Spell.Id == spell.Id).Level;
            if (teaches <= known.Level)
                return new(TrainingRefusal.TrainerKnowsNoMore, 0, 0);
            if (known.Level >= rules.Training.SpellMaxLevel)
                return new(TrainingRefusal.AtMost, 0, 0);
            goldPrice = (uint)rules.GoldPrice(known.Level);
            expPrice = Discount((known.Level + 1) * (known.Level + 1) * spell.TrainingCost, trainer, rules);
        }
        else
        {
            if (trainer is null)
                return new(TrainingRefusal.CantLearn, 0, 0);
            if (wizard < spell.Wizard)
                return new(TrainingRefusal.WizardTooLow, 0, 0);
            if (student.School != MagicSchool.Chaos && spell.School != MagicSchool.None && student.School != spell.School)
                return new(TrainingRefusal.WrongSchool, 0, 0);
            goldPrice = (uint)rules.Training.FirstLevelGold;
            expPrice = Discount(spell.TrainingCost, trainer, rules);
        }
        return Afford(student, trainer, expPrice, goldPrice, gold);
    }

    /// <summary>
    /// Learns a spell at level 1, or raises one a level, if the quote allows
    /// (<c>WidgetSpellTrain::Purchase</c>). Returns the quote; <paramref name="learnt"/> says whether
    /// the spell is new, which the original follows with its first-spell help (flag 3611).
    /// </summary>
    public static TrainingQuote TrainSpell(Character student, SpellRecord spell, Trainer? trainer, ref uint gold, out bool learnt)
    {
        learnt = false;
        var quote = QuoteSpell(student, spell, trainer, gold);
        if (!quote.Allowed)
            return quote;
        if (student.FindSpell(spell.Id) is { } known)
        {
            known.Level++;
        }
        else
        {
            student.LearnSpell(spell);
            learnt = true;
        }
        student.SpendableExp -= quote.Exp;
        gold -= quote.Gold;
        return quote;
    }

    // A trainer takes less experience; the price goes through single precision, as the original's does.
    private static uint Discount(int price, Trainer? trainer, CharacterRules rules) =>
        (uint)((float)(uint)price * (trainer is null ? 1f : rules.Training.TrainerExp));

    private static TrainingQuote Afford(Character student, Trainer? trainer, uint exp, uint gold, uint partyGold)
    {
        if (trainer is null || trainer.Free)
            gold = 0;
        if (student.SpendableExp < exp || (trainer is not null && partyGold < gold))
            return new(TrainingRefusal.CantAfford, exp, gold);
        return new(TrainingRefusal.None, exp, gold);
    }
}
