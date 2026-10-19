using Aidyn.Core.Calendar;
using Aidyn.Core.Combat;
using Aidyn.Core.Items;
using Aidyn.Core.Magic;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Characters;

/// <summary>What one ageing of a character's effects did, for the numbers over its head (<c>Entity.ShowAgeResult</c>).</summary>
/// <param name="Died">It was alive before and isn't now.</param>
public readonly record struct EffectAgeing(int Damage, int Healed, bool Died);

// The effects of spells, potions and worn enchantments on a character (ADR 0020, docs/systems/magic.md): the original's 15 timed
// records at entity+0x34 and 7 potion records at entity+0x38, with what adds, ages and removes them in entity.cpp.
public sealed partial class Character
{
    /// <summary>How many potions' effects a character holds at once, without the magic record (<c>POTION_FXMAX</c>).</summary>
    public const int PotionSlots = 7;

    /// <summary>How many spells' effects a character holds at once, without the magic record.</summary>
    public const int SpellSlots = 15;

    private ActiveEffect?[]? _spellEffects;
    private ActiveEffect?[]? _potionEffects;

    private ActiveEffect?[] SpellEffectSlots => _spellEffects ??= new ActiveEffect?[Rules.Magic?.Record.SpellEffects ?? SpellSlots];

    private ActiveEffect?[] PotionEffectSlots => _potionEffects ??= new ActiveEffect?[Rules.Magic?.Record.PotionEffects ?? PotionSlots];

    /// <summary>The spells' effects on it and its worn enchantments, by slot.</summary>
    public IReadOnlyList<ActiveEffect> Effects => [.. SpellEffectSlots.OfType<ActiveEffect>()];

    /// <summary>The potions working on it, by slot.</summary>
    public IReadOnlyList<ActiveEffect> PotionEffects => [.. PotionEffectSlots.OfType<ActiveEffect>()];

    /// <summary>The effect a spell or potion has on it, if it's on.</summary>
    public ActiveEffect? FindEffect(RecordId source) =>
        Slots(source).FirstOrDefault(e => e?.Source == source);

    public bool HasEffect(RecordId source) => FindEffect(source) is not null;

    public bool HasPotionEffect(Potion potion) => HasEffect(Potions.Id(potion));

    /// <summary>Whether a Cheat Death is on it (<c>Character.HasCheatDeath</c>, <c>0x8006EB4C</c>).</summary>
    public bool HasCheatDeath => Effects.Any(e => e.Effects.Any(x => x is CheatDeathEffect));

    private ActiveEffect?[] Slots(RecordId source) => source.Type == RecordTypes.Item ? PotionEffectSlots : SpellEffectSlots;

    /// <summary>
    /// A spell's or potion's lasting effects go on (<c>Entity.AddEffect</c>, <c>0x800702F0</c>; potions' <c>Entity.AddPotion</c>,
    /// <c>0x8006F84C</c>): in the first free slot, each starting on the sheet and its combatant if it's in a battle. A spell already
    /// on doesn't go on again or refresh, nor does one with no slot free, nor one an effect refuses: then nothing happens, and
    /// it returns null. A potion isn't checked here: drinking one already working is refused before.
    /// </summary>
    /// <param name="effects">Those of the record's effects that land on this target.</param>
    /// <param name="power">The cast's effective level, an enchantment's level, or a potion's 0.</param>
    /// <param name="margin">The cast's margin after the resist step; an enchantment's level.</param>
    internal ActiveEffect? AddEffect(RecordId source, IReadOnlyList<Effect> effects, int power, MagicSchool school, int margin, uint timer,
        Combatant? on, IMagicHost? host, IRandom? random, bool worn = false, bool start = true)
    {
        var slots = Slots(source);
        if (source.Type != RecordTypes.Item && slots.Any(e => e?.Source == source))
            return null;
        var free = Array.IndexOf(slots, null);
        if (free < 0)
            return null;
        var target = new EffectTarget(this, on, host);
        if (effects.Any(e => EffectRules.Refuses(e, target)))
            return null;
        var record = new ActiveEffect(source, effects, power, school, margin, timer, worn)
        {
            Harmful = effects.Any(e => EffectRules.Harmful(e, power)),
        };
        slots[free] = record;
        for (var i = 0; start && i < effects.Count; i++)
            EffectRules.Start(record, i, target, random ?? new Lcg());
        return record;
    }

    /// <summary>An effect comes off, undoing what it did (<c>Entity.RemoveEffect</c>, <c>0x800724B8</c>; potions' <c>0x8006FADC</c>).</summary>
    public void RemoveEffect(ActiveEffect record, Combatant? on = null, IMagicHost? host = null)
    {
        var slots = Slots(record.Source);
        var slot = Array.IndexOf(slots, record);
        if (slot < 0)
            return;
        var target = new EffectTarget(this, on, host);
        for (var i = 0; i < record.Effects.Count; i++)
            EffectRules.End(record, i, target);
        // EffectRecord.FreeVisual (0x8007AB9C) and PotionRecord.FreeVisual (0x80079DB8): what a battle shows of it ends.
        record.Visual?.Ended();
        record.Visual = null;
        slots[slot] = null;
    }

    /// <summary>
    /// A potion starts working, as drinking one or loading a save does, with the potion record's lasting effects
    /// (<c>Entity.AddPotion</c>): Strength and Dexterity raise the stat's base by 10, unless the character is dead; Clarity,
    /// Charisma and Stealth raise Loremaster, Diplomat and Stealth by 3; Defence protection by 15.
    /// </summary>
    public void ApplyPotionEffect(Potion potion, int power, uint timer) => ApplyPotionEffect(Potions.Id(potion), power, timer);

    /// <inheritdoc cref="ApplyPotionEffect(Potion, int, uint)"/>
    public ActiveEffect? ApplyPotionEffect(RecordId potion, int power, uint timer)
    {
        var effects = Rules.Magic?.Potion(potion)?.Effects?.Where(EffectRules.Lasting).ToList() ?? [];
        return AddEffect(potion, effects, power, MagicSchool.None, 0, timer, null, null, null);
    }

    /// <summary>
    /// Time passing for its effects in trek, <paramref name="ticks"/> of play (<c>Entity.Age</c> from <c>TrekLoop.TickClock</c>,
    /// <c>0x80020CB4</c>): each timer runs down by the game clock's ticks, and one already run down ends, undoing what it did. A
    /// dead character's don't run. On a drawn <paramref name="frame"/>, what the effects do each round they do too: the Ring of
    /// Healing heals.
    /// </summary>
    /// <param name="partOfDay">The clock's part of the day, which Photosynthesis's healing reads.</param>
    public EffectAgeing AgeEffects(int ticks, bool frame = true, PartOfDay partOfDay = PartOfDay.Midday, IRandom? random = null) =>
        Age(GameClock.DaySpeed, ticks, frame, null, null, partOfDay, random ?? new Lcg());

    /// <summary>
    /// A combat round passing for its effects (<c>Entity.Age</c> from <c>TurnOrder.EndRound</c>, <c>0x8006A62C</c>): each timer
    /// runs down by <see cref="MagicRecord.RoundTicks"/>, one already run down ends, and the rest do what they do each round:
    /// damage, poison, healing, a web's escape.
    /// </summary>
    public EffectAgeing AgeRound(Combatant? on, IMagicHost? host, PartOfDay partOfDay, IRandom random) =>
        Age(0, 1, true, on, host, partOfDay, random);

    // Entity.Age (0x80071444): the potions (Entity.AgePotions, 0x8006FA28), then the spells (Entity.AgeEffects, 0x80070A3C).
    private EffectAgeing Age(uint unit, int amount, bool rounds, Combatant? on, IMagicHost? host, PartOfDay partOfDay, IRandom random)
    {
        if (IsDead)
            return default;
        var round = Rules.Magic?.Record.RoundTicks ?? 600;
        var potions = PotionEffectSlots;
        foreach (var potion in potions.OfType<ActiveEffect>().ToList())
        {
            if (potion.Age(unit, amount, round))
                RemoveEffect(potion, on, host);
        }
        int damage = 0, healed = 0;
        var target = new EffectTarget(this, on, host);
        var slots = SpellEffectSlots;
        for (var slot = 0; slot < slots.Length; slot++)
        {
            if (slots[slot] is not { } record)
                continue;
            if (record.Age(unit, amount, round))
            {
                RemoveEffect(record, on, host);
                continue;
            }
            if (!rounds)
                continue;
            for (var i = 0; i < record.Effects.Count; i++)
            {
                if (!EffectRules.Round(record, i, target, partOfDay, random, ref damage, ref healed))
                {
                    RemoveEffect(record, on, host);
                    break;
                }
            }
        }
        return new EffectAgeing(damage, healed, IsDead);
    }

    /// <summary>
    /// A battle's end for its effects (<c>Entity.ClearEffects</c>, <c>0x80071830</c>): every potion's come off but those that
    /// outlast a battle (Stealth's), then every spell's and every enchantment's, then the worn armor's, shield's and gear's
    /// enchantments go back on. A weapon's was never on.
    /// </summary>
    public void EndBattleEffects(IMagicHost? host = null)
    {
        var kept = Rules.Magic?.Record.KeptAfterBattle ?? [Potions.Id(Potion.Stealth)];
        foreach (var potion in PotionEffects.Where(p => !kept.Contains(p.Source)))
            RemoveEffect(potion, null, host);
        foreach (var effect in Effects)
            RemoveEffect(effect, null, host);
        foreach (var item in new[] { Equipment.Armor, Equipment.Shield }.Concat(Equipment.Gear))
            PutOnEnchantment(item);
    }

    /// <summary>
    /// Every spell's effect and every potion's comes off, as a combatant dies or flees (<c>Combatant.RemoveAllEffects</c>,
    /// <c>0x8006701C</c>, and <c>Entity.ClearPotions</c>, <c>0x80071754</c>). A dead character's stats aren't touched by it, so
    /// what a spell or potion raised stays raised, as the original's does.
    /// </summary>
    public void RemoveAllEffects(Combatant? on, IMagicHost? host = null)
    {
        if (on is not null)
        {
            foreach (var effect in Effects)
                RemoveEffect(effect, on, host);
        }
        foreach (var potion in PotionEffects)
            RemoveEffect(potion, on, host);
    }

    /// <summary>
    /// A worn item's enchantment goes on, never running down (<c>0x8007192C</c>), as armor, a shield or gear is put on: the
    /// lasting effects of its spell at its level. A weapon's isn't: it's cast as the weapon strikes.
    /// </summary>
    /// <param name="start">Whether its effects change the sheet, or the sheet counts them already (a checkpoint's).</param>
    internal void PutOnEnchantment(ItemInstance? item, bool start = true)
    {
        if (item?.Item.Bonuses.Enchantment is not { } enchantment || Rules.Magic?.Spell(enchantment.Spell) is not { } spell)
            return;
        var effects = (spell.Effects ?? []).Where(e => EffectRules.Lasting(e) && e.For != EffectFor.Opponents).ToList();
        AddEffect(spell.Id, effects, enchantment.Level, spell.School, enchantment.Level, ActiveEffect.Never, null, null, null, worn: true, start);
    }

    /// <summary>A worn item's enchantment comes off as the item does: the first of its spell that never runs down (<c>0x8006F654</c>).</summary>
    internal void TakeOffEnchantment(ItemInstance? item)
    {
        if (item?.Item.Bonuses.Enchantment is not { } enchantment)
            return;
        if (Effects.FirstOrDefault(e => e.Source == enchantment.Spell && e.Timer == ActiveEffect.Never) is { } worn)
            RemoveEffect(worn);
    }

    /// <summary>
    /// A spell or potion raising or lowering a stat's base and modified values (<c>Entity.AddStat</c>, <c>0x800723E8</c>): not
    /// on a dead character, and not taking Endurance, or the Level while Endurance is 0, below 1.
    /// </summary>
    internal void AddStat(Stat stat, int amount)
    {
        if (IsDead)
            return;
        Stats.ChangeBase(stat, BoundedChange(stat, (sbyte)amount, adding: true));
    }

    /// <summary>The same taken back (<c>Entity.SubStat</c>, <c>0x80072450</c>), bounded the other way.</summary>
    internal void SubStat(Stat stat, int amount)
    {
        if (IsDead)
            return;
        Stats.ChangeBase(stat, -BoundedChange(stat, (sbyte)amount, adding: false));
    }

    // 0x800722A8 adding and 0x8007234C taking off: Endurance, and the Level while Endurance is 0, are left at 1 at the least.
    private sbyte BoundedChange(Stat stat, sbyte amount, bool adding)
    {
        if (stat == Stat.Level ? Stats.Modified(Stat.Endurance) != 0 : stat != Stat.Endurance)
            return amount;
        var value = (sbyte)Stats.Modified(stat);
        return adding
            ? -amount < value ? amount : (sbyte)(1 - value)
            : amount < value ? amount : (sbyte)(value - 1);
    }

    /// <summary>
    /// Takes damage: the Level's points first, then as <see cref="DrainStamina"/> (<c>Character.TakeDamage</c>, <c>0x8006EB8C</c>).
    /// A blow that kills is undone by a Cheat Death on it, which heals twice its margin and ends. Otherwise the dead lose their
    /// spells' effects, if they're in a battle, and their potions'.
    /// </summary>
    /// <param name="on">Its combatant, in a battle.</param>
    /// <returns>Whether a Cheat Death saved it: the log says it "cheats death" (<c>Combatant.CheckDeath</c>).</returns>
    public bool TakeDamage(int amount, Combatant? on = null)
    {
        // A hit ends a Troubadour's performance (0x80066100): session 9 ports it.
        amount = (short)amount;
        var level = Stats.Modified(Stat.Level);
        if (level >= amount)
        {
            Stats.ChangeCurrent(Stat.Level, -amount);
            return false;
        }
        Stats.ChangeCurrent(Stat.Level, -level);
        DrainStamina((short)(amount - level));
        if (!IsDead)
            return false;
        if (Effects.FirstOrDefault(e => e.Effects.Any(x => x is CheatDeathEffect)) is { } cheat)
        {
            Heal((cheat.Margin & 0x7FFF) << 1);
            RemoveEffect(cheat, on, on?.MagicHost);
            return true;
        }
        RemoveAllEffects(on, on?.MagicHost);
        // 0x80071E28: if taking them off brought it back, it's put back at 0.
        if (!IsDead)
        {
            foreach (var stat in (ReadOnlySpan<Stat>)[Stat.Endurance, Stat.Stamina, Stat.Level])
                Stats.ChangeCurrent(stat, -Stats.Modified(stat));
        }
        return false;
    }
}
