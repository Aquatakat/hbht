using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Quests;
using PluginSpell = Aidyn.Plugins.Records.SpellRecord;

namespace Aidyn.Rom.Import;

/// <summary>
/// What casting is, as records (ADR 0020): each spell's effects, duration, resist rule, filter and AI value, each potion's effects
/// and duration, and casting's numbers (<c>aidyn:magic/0</c>). The ROM keeps none of it as data: the original's code switches on a
/// spell's kind, its place in <c>SpellList</c> (<c>0x800E2B40</c>), so these are facts, read from 1.0's machine code (the cites are on
/// each). Importing checks every fact against the ROM: a spell the ROM lacks, a spell with no fact, a name that differs or an entity
/// not in the place the code names is an error.
/// </summary>
internal static class MagicImporter
{
    /// <summary>Writes the magic record.</summary>
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Magic");
        var visuals = ctx.SpellVisuals;
        ctx.WriteRecords("magic.json", new[]
        {
            BuildMagic(CastingFactors.Read(ctx.Rom)) with
            {
                Refused = Visual(ctx.Archive, visuals.Refused, BorgType.Actor, RecordTypes.Actor, "The refused cast's actor"),
                Resisted = Visual(ctx.Archive, visuals.Resisted, BorgType.Actor, RecordTypes.Actor, "The resisted cast's actor"),
            },
        });
    }

    /// <summary>
    /// The magic record. The weights are the code's integers (<c>Combatant.CastChance</c>: Intelligence × 4 + level × 6 + character
    /// level, at least 5; <c>Combatant.ResistRoll</c>: Willpower × 3 + Endurance + level), the 600 a round is
    /// <c>EffectRecord.Age</c> (<c>0x8007ABD8</c>), the 2 enemies disabled at most is <c>Combatant.CheckDisableLimit</c>
    /// (<c>0x80063A00</c>), 10 the top of <c>ResolveCastRoll</c>'s improvement, and 15 and 7 the sizes of the arrays at
    /// <c>entity+0x34</c> and <c>+0x38</c>. The Stealth potion alone outlasts a battle (<c>Entity.ClearEffects</c>, <c>0x80071830</c>).
    /// The floats are the code segment's, read per ROM.
    /// </summary>
    internal static MagicRecord BuildMagic(CastingFactors factors) => new(
        RecordId.Decimal(RecordTypes.Magic, 0),
        new CastChance(4, 6, 1, 5, factors.ByPartOfDay, factors.ByMoon),
        new ResistRoll(3, 1, 1, factors.SameAspect, factors.OtherAspect),
        RoundTicks: 600,
        MostDisabled: 2,
        factors.Improvement,
        MaxLevel: 10,
        [DbRecordIds.Required(ItemId.Of(DbType.Potion, (byte)StealthPotion))]);

    private const int StealthPotion = 16;

    /// <summary>
    /// The record of a Borg a visual names, which must be of <paramref name="type"/> in the archive: an actor for roles 0 to 4, a scene for
    /// 5 and 6.
    /// </summary>
    private static RecordId Visual(BorgArchive archive, int borg, BorgType type, string recordType, string what) =>
        borg >= 0 && borg < archive.Count && archive.Listings[borg].Type == type
            ? RecordId.Hex(recordType, borg)
            : throw new InvalidDataException($"{what}, Borg 0x{borg:X}, isn't a {type}.");

    private static RecordId? Actor(BorgArchive archive, IReadOnlyDictionary<int, VisualBorg> table, int kind, string what) =>
        table.TryGetValue(kind, out var v) ? Visual(archive, v.Borg, BorgType.Actor, RecordTypes.Actor, $"{what} {kind}'s actor") : null;

    private static RecordId? Aura(BorgArchive archive, IReadOnlyDictionary<int, VisualBorg> table, int kind, string what) =>
        table.TryGetValue(kind, out var v) ? Visual(archive, v.Borg, BorgType.Scene, RecordTypes.Scene, $"{what} {kind}'s aura") : null;

    /// <summary>
    /// Gives each spell what casting it shows (<see cref="SpellVisualTables"/>), by its place in <c>SpellList</c>, its kind. Every actor and
    /// scene must be the Borg of its type; a spell with nothing keeps none.
    /// </summary>
    public static List<PluginSpell> WithVisuals(List<PluginSpell> spells, SpellVisualTables tables, BorgArchive archive) =>
    [
        .. spells.Select(s =>
        {
            var kind = s.Position ?? throw new InvalidDataException($"{s.Id} has no place in the spell list.");
            var caster = Actor(archive, tables.Caster, kind, "Spell");
            var target = Actor(archive, tables.Target, kind, "Spell");
            var aura = Aura(archive, tables.Aura, kind, "Spell");
            if (caster is null && target is null && aura is null)
                return s;
            return s with
            {
                Visuals = new MagicVisuals(
                    caster, target, tables.TargetAtHead.Contains(kind), aura,
                    caster is null ? 0 : tables.Caster[kind].Memory, target is null ? 0 : tables.Target[kind].Memory),
            };
        }),
    ];

    /// <summary>
    /// Gives each potion what drinking it (or a flask's blast) shows: its actor, which asks for its memory, and its aura. The potions are
    /// <paramref name="items"/>' of kind <see cref="ItemKind.Potion"/>, in the order of their index.
    /// </summary>
    public static List<ItemRecord> WithPotionVisuals(List<ItemRecord> items, SpellVisualTables tables, BorgArchive archive)
    {
        var kinds = Enumerable.Range(0, ItemTables.PotionCount)
            .ToDictionary(i => DbRecordIds.Required(ItemId.Of(DbType.Potion, (byte)i)), i => i);
        return [.. items.Select(item =>
        {
            if (!kinds.TryGetValue(item.Id, out var kind))
                return item;
            var actor = Actor(archive, tables.Potion, kind, "Potion");
            var aura = Aura(archive, tables.PotionAura, kind, "Potion");
            return actor is null && aura is null
                ? item
                : item with { Visuals = new MagicVisuals(null, actor, false, aura, 0, actor is null ? 0 : tables.Potion[kind].Memory) };
        })];
    }

    // Records the facts name.
    private const int Intelligence = 0, Willpower = 1, Dexterity = 2, Endurance = 3, Strength = 4, Stamina = 5;
    private const int Diplomat = 1, Loremaster = 3, Stealth = 7;

    private static RecordId Stat(int n) => RecordId.Decimal(RecordTypes.Stat, n);

    private static RecordId Skill(int n) => RecordId.Decimal(RecordTypes.Skill, n);

    private static RecordId Spell(int id) => RecordId.Hex(RecordTypes.Spell, id);

    /// <summary>The ticks a cast's effects last for each level (<c>ApplySpellEffect</c>, <c>0x800646E0</c>: <c>level × 1800</c>).</summary>
    private static readonly EffectDuration Lasting = new(PerLevel: 1800);

    /// <summary>A potion's, drunk or thrown: <c>0x9E340</c> (<c>Entity.AddPotion</c>, <c>0x8006F84C</c>).</summary>
    private static readonly EffectDuration PotionLasts = new(Flat: 648000);

    /// <summary>Wraith Touch's record has the timer <c>0xFFFFFFFF</c>, which only the battle's end undoes (<c>Entity.WraithDrain</c>, <c>0x80071B0C</c>).</summary>
    private static readonly EffectDuration UntilBattleEnds = new(Battle: true);

    /// <summary>One spell's facts.</summary>
    private sealed record Fact(
        int Id, string Name, int Ai, IReadOnlyList<Effect> Effects, EffectDuration? Duration = null, Resisted Resisted = Resisted.Never,
        TargetFilter? Filter = null, IReadOnlyList<RecordId>? NotResistedBy = null);

    private static Fact F(int id, string name, int ai, Effect[] effects, EffectDuration? duration = null, Resisted resisted = Resisted.Never,
        TargetFilter? filter = null, RecordId[]? notResistedBy = null) => new(id, name, ai, effects, duration, resisted, filter, notResistedBy);

    private static Effect Boost(int stat, float perLevel) => new StatEffect(Stat(stat), Magnitude.Per(perLevel));

    /// <summary>
    /// Every spell's facts, by the ROM's spell ID. The effects are what <c>Entity.AddEffect</c> (<c>0x800702F0</c>) and
    /// <c>ApplySpellEffect</c> (<c>0x800646E0</c>) do for the spell's kind; the AI values are <c>EnemyAI.SpellValue</c>'s
    /// (<c>0x80057498</c>); the resist rule is <c>Combatant.IsResistable</c> (<c>0x800641B8</c>): a spell at enemies or with a
    /// placed area is resisted by all, Wind across sides, the dispels and Teleportation never, the rest never. The entities are by
    /// their places in the entity list, as <c>SpellKindAllowsTarget</c> (<c>0x8006088C</c>) names them.
    /// </summary>
    private static IReadOnlyList<Fact> Facts(EngineTables tables)
    {
        RecordId At(int place, int expected) => tables.EntityList[place] == new ItemId((ushort)expected)
            ? DbRecordIds.Required(tables.EntityList[place], $"entity list place {place}")
            : throw new InvalidDataException($"Entity list place {place} is {tables.EntityList[place]}, not 0x{expected:X4}.");

        // The Air, Earth, Fire and Water Elementals and the Firelord (Ids.IsElemental, 0x8006E110).
        RecordId[] elementals = [.. new[] { 0xC2, 0xC4, 0xC5, 0xC7, 0xBF }.Select(p => DbRecordIds.Required(tables.EntityList[p], $"entity list place {p}"))];
        var shadow = DbRecordIds.Required(tables.EntityList[0xAC], "entity list place 172");
        var marquis = At(0xAA, 0x0221);
        var zombie = At(0xBE, 0x0266);
        var plagueZombie = At(0xB6, 0x028C);
        RecordId[] immuneShadow = [shadow];

        // Wraith Touch's four (0x800D34B0: Intelligence, Willpower, Dexterity, Strength); Restore's six (0x800D34B4).
        RecordId[] wraithStats = [Stat(Intelligence), Stat(Willpower), Stat(Dexterity), Stat(Strength)];

        Effect Ward(MagicSchool school) => new WardEffect(school, Magnitude.Per(6));
        Effect Dispel(MagicSchool school) => new DispelEffect(school);
        Effect Burn(int count) => new DamagePerRoundEffect(new Dice(count));
        Effect Hit(int dice, params Element[] elements) => new DamageEffect(Magnitude.Fixed(dice), elements);
        Effect Shield() => new ProtectionEffect(Magnitude.Per(2));
        Effect LeakyShield() => new ProtectionEffect(Magnitude.Per(2), Magnitude.Per(1));

        return
        [
            F(0x033D, "Immolation", 22, [Burn(1)], Lasting, Resisted.Always),
            F(0x033C, "Escape", 0, [new FleeEffect()]),
            F(0x0338, "Remove Poison", 83, [new CureEffect([Spell(0x0332)], BeatsMargin: true)]),
            F(0x0300, "Air Shield", 61, [Shield()], Lasting),
            F(0x0301, "Control Elem", 84, [new CharmEffect()], Lasting, Resisted.Always, new TargetFilter(Entities: elementals)),
            F(0x0302, "Debilitation", 31, [Boost(Strength, -2)], Lasting, Resisted.Always),
            F(0x0303, "Dragon Flames", 12, [Hit(2, Element.Fire)], resisted: Resisted.Always),
            F(0x0304, "Earth Smite", 13, [Hit(4, Element.Earth)], resisted: Resisted.Always),
            F(0x0305, "Fireball", 14, [new DamageEffect(Magnitude.Per(1), [Element.Fire])], resisted: Resisted.Always),
            F(0x0306, "Lightning", 11, [Hit(2, Element.Water)], resisted: Resisted.Always),
            F(0x0308, "Strength", 51, [Boost(Strength, 2)], Lasting),
            F(0x0309, "Wind", 12,
            [
                new DamageEffect(Magnitude.Fixed(2), [Element.Air]) { For = EffectFor.Opponents },
                new StaminaEffect(Magnitude.Per(2), RaisesMaximum: true) { For = EffectFor.Allies },
                new WeatherEffect(1) { For = EffectFor.Allies },
            ], Lasting, Resisted.Opponents),
            F(0x033A, "Teleportation", 0, [new TeleportEffect()]),
            F(0x0337, "Brilliance", 53, [Boost(Intelligence, 2)], Lasting),
            F(0x0336, "Stupidity", 33, [Boost(Intelligence, -2)], Lasting, Resisted.Always),
            F(0x030A, "Banishing", 82, [new BanishEffect()], resisted: Resisted.Always, filter: new TargetFilter(Entities: elementals)),
            F(0x030C, "Charming", 84, [new CharmEffect()], Lasting, Resisted.Always, new TargetFilter(NotCategories: [EntityCategory.Chaos])),
            F(0x030D, "Control Marquis", 0, [new CharmEffect()], Lasting, Resisted.Always, new TargetFilter(Entities: [marquis])),
            F(0x030F, "Detecting Traps", 0, []),
            F(0x0310, "Endurance", 52, [Boost(Endurance, 2)], Lasting),
            F(0x0311, "Opening", 0, []),
            F(0x0312, "Sense Aura", 0, [new SenseAuraEffect()], resisted: Resisted.Always),
            F(0x0313, "Weakness", 32, [Boost(Endurance, -2)], Lasting, Resisted.Always),
            F(0x033F, "Cheat Death", 82, [new CheatDeathEffect()], Lasting),
            F(0x033B, "Acid Bolt", 22, [Burn(1)], Lasting, Resisted.Always),
            F(0x0334, "Aura of Death", 0, [new MoraleEffect(Magnitude.Per(-1))], Lasting, Resisted.Always),
            F(0x0333, "Wraith Touch", 36, [new DrainStatEffect(wraithStats, new Dice(2))], UntilBattleEnds, Resisted.Always),
            F(0x0314, "Control Zombies", 84, [new CharmEffect()], Lasting, Resisted.Always, new TargetFilter(Entities: [zombie, plagueZombie])),
            F(0x0315, "Crushing Death", 13, [Hit(4)], resisted: Resisted.Always),
            F(0x0316, "Darkness", 85, [new PartOfDayEffect(Night: true)], Lasting),
            F(0x0317, "Haste", 73, [new FieldRadiusEffect(Magnitude.Per(1))], Lasting),
            F(0x0318, "Exhaustion", 34, [Boost(Stamina, -2)], Lasting, Resisted.Always),
            F(0x0319, "Stamina", 54, [Boost(Stamina, 2)], Lasting),
            F(0x031A, "Tap Stamina", 55,
                [new StaminaEffect(Magnitude.Per(-2)), new StaminaEffect(Magnitude.Per(2), ToCaster: true)], Lasting, Resisted.Always),
            F(0x031B, "Wall Of Bones", 42, [new ImmobiliseEffect(Immune: immuneShadow)], Lasting, Resisted.Always, notResistedBy: [marquis]),
            F(0x031C, "Spirit Shield", 61, [LeakyShield()], Lasting),
            F(0x0332, "Poison", 21, [new PoisonEffect(Magnitude.Per(0.5f))], Lasting, Resisted.Always),
            F(0x033E, "Mirror", 72, [new ReflectEffect(Magnitude.Per(3))], Lasting),
            F(0x031D, "vs. Elemental", 71, [Ward(MagicSchool.Elemental)], Lasting),
            F(0x031E, "vs. Naming", 71, [Ward(MagicSchool.Naming)], Lasting),
            F(0x031F, "vs. Necromancy", 71, [Ward(MagicSchool.Necromancy)], Lasting),
            F(0x0320, "vs. Star", 71, [Ward(MagicSchool.Star)], Lasting),
            F(0x0321, "Dispel Elemental", 81, [Dispel(MagicSchool.Elemental)]),
            F(0x0322, "Dispel Naming", 81, [Dispel(MagicSchool.Naming)]),
            F(0x0323, "Dispel Necro", 81, [Dispel(MagicSchool.Necromancy)]),
            F(0x0324, "Dispel Star", 81, [Dispel(MagicSchool.Star)]),
            F(0x0339, "Photosynthesis", 83, [new HealPerRoundEffect(DayOnly: true)], Lasting),
            F(0x0335, "Solar Wrath", 0, [new MoraleEffect(Magnitude.Per(-1))], Lasting, Resisted.Always),
            F(0x0326, "Starlight Shield", 61, [LeakyShield()], Lasting),
            F(0x0327, "Detect Moon Phase", 0, [new RevealPhaseEffect(Sun: false)]),
            F(0x0328, "Detect Sun Phase", 0, [new RevealPhaseEffect(Sun: true)]),
            F(0x0329, "Dexterity", 56, [Boost(Dexterity, 2)], Lasting),
            F(0x032A, "Clumsiness", 35, [Boost(Dexterity, -2)], Lasting, Resisted.Always),
            F(0x032B, "Frozen Doom", 42, [new ImmobiliseEffect(Immune: immuneShadow)], Lasting, Resisted.Always),
            F(0x032D, "Light", 85, [new PartOfDayEffect(Night: false)], Lasting),
            F(0x032E, "Stealth", 0, [new SkillEffect(Skill(Stealth), Magnitude.Per(6))], Lasting),
            F(0x032F, "Stellar Gravity", 41, [new FieldRadiusEffect(Magnitude.Per(-1))], Lasting, Resisted.Always),
            F(0x0330, "Web Of Starlight", 43, [new ImmobiliseEffect(Escape: 2, Immune: immuneShadow), Burn(2)], Lasting, Resisted.Always),
            F(0x0331, "Whitefire", 13, [Hit(4)], resisted: Resisted.Always),
        ];
    }

    /// <summary>
    /// Gives every spell its facts, and its aspect and placed area from the ROM's <c>unk1E</c> bits (1 Lunar and 2 Solar, which feed
    /// <c>Combatant.ResistRoll</c> and <c>SpellResistanceApplies</c>; 0x10 a placed area, which <c>CanCast</c>,
    /// <c>BeginSpellTargeting</c> and <c>IsResistable</c> read). The facts and the ROM's spells must name each other exactly.
    /// </summary>
    public static List<PluginSpell> WithEffects(List<PluginSpell> spells, EngineTables tables)
    {
        var facts = Facts(tables).ToDictionary(f => Spell(f.Id));
        var byId = spells.ToDictionary(s => s.Id);
        foreach (var missing in facts.Keys.Where(id => !byId.ContainsKey(id)))
            throw new InvalidDataException($"A fact names {missing}, which the ROM lacks.");
        return [.. spells.Select(s =>
        {
            if (!facts.TryGetValue(s.Id, out var fact))
                throw new InvalidDataException($"The ROM's {s.Id} ({s.Name}) has no fact.");
            if (fact.Name != s.Name)
                throw new InvalidDataException($"The fact for {s.Id} is for {fact.Name}, but the ROM's spell is {s.Name}.");
            var aspect = (s.Unk1E & 3) switch { 0 => Aspect.None, 1 => Aspect.Lunar, 2 => Aspect.Solar, _ => throw new InvalidDataException($"{s.Id} is both Lunar and Solar.") };
            return s with
            {
                Effects = fact.Effects, Duration = fact.Duration, Aspect = aspect, Placed = (s.Unk1E & PlacedBit) != 0, Resisted = fact.Resisted,
                NotResistedBy = fact.NotResistedBy, Filter = fact.Filter, AiValue = fact.Ai,
            };
        })];
    }

    /// <summary><c>unk1E</c> bit 0x10: the caster places the area.</summary>
    private const int PlacedBit = 0x10;

    /// <summary>
    /// A potion's effects and duration, by its index (<c>DrinkPotion</c>, <c>0x80064C84</c>; <c>Entity.AddPotion</c>, <c>0x8006F84C</c>;
    /// <c>FlaskDamage</c>, <c>0x80065270</c>; <c>FlaskExplosion</c>, <c>0x800652F8</c>). The Aspect and Jumping potions do nothing.
    /// </summary>
    public static (IReadOnlyList<Effect>? Effects, EffectDuration? Duration) Potion(int potion) => potion switch
    {
        // The flasks: 4d6, 8d6, 15 flat from Stamina, and 2d6 with a radius of 2d6 for the corrosion (FlaskDamage rolls all three).
        0 => ([new BlastEffect(new Dice(4))], null),
        1 => ([new BlastEffect(new Dice(8))], null),
        2 => ([new BlastEffect(new Dice(0, 6, 15), Stamina: true)], null),
        3 => ([new BlastEffect(new Dice(2)), new CorrodeEffect(new Dice(2))], null),
        // Healing and Curing heal 15-25 and 40-60 percent of the maximum (Entity.HealPercent, 0x800715DC).
        4 => ([new HealEffect(15, 25)], null),
        5 => ([new CureEffect([Spell(0x0318)]), new StaminaEffect(Magnitude.Fixed(0), ToBase: true)], null),
        6 => ([new HealEffect(40, 60)], null),
        7 => ([new CureEffect([Spell(0x0332)])], null),
        // Entity.CureWeakness's list (0x800D34B4): Wraith Touch, Debilitation, Weakness, Exhaustion, Stupidity, Clumsiness.
        8 => ([new CureEffect([Spell(0x0333), Spell(0x0302), Spell(0x0313), Spell(0x0318), Spell(0x0336), Spell(0x032A)])], null),
        9 => ([new StatEffect(Stat(Strength), Magnitude.Fixed(10))], PotionLasts),
        10 => ([new StatEffect(Stat(Dexterity), Magnitude.Fixed(10))], PotionLasts),
        13 => ([new SkillEffect(Skill(Loremaster), Magnitude.Fixed(3))], PotionLasts),
        14 => ([new SkillEffect(Skill(Diplomat), Magnitude.Fixed(3))], PotionLasts),
        15 => ([new ProtectionEffect(Magnitude.Fixed(15))], PotionLasts),
        16 => ([new SkillEffect(Skill(Stealth), Magnitude.Fixed(3))], PotionLasts),
        _ => (null, null),
    };
}
