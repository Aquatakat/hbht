using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Plugins.Saves;

namespace Aidyn.Core;

/// <summary>
/// The game as it stands: the flags, the clock, the party with its inventory, what map objects
/// remember, the random streams and where the player is (ADR 0003). A save is a snapshot of it
/// (<see cref="Save"/>), and loading one rebuilds it with the rules, as the original's
/// <c>QuestData::Load</c> does. Scripts read and change it as their <see cref="IScriptWorld"/>.
/// </summary>
public sealed class GameState : IScriptWorld
{
    /// <summary>Alaron, who a new game starts with and who is always first in the party.</summary>
    public static readonly RecordId Alaron = RecordId.Hex(RecordTypes.Entity, 0x023F);

    /// <summary>A new game's gold (<c>init_DBs</c>).</summary>
    public const uint NewGameGold = 1;

    private readonly List<string> _problems = [];

    private GameState(PluginSet plugins)
    {
        Plugins = plugins;
        Rules = CharacterRules.From(plugins);
        Items = ItemCatalog.From(plugins);
        Flags = FlagNetwork.Load(plugins);
        StateFlagsRecord = plugins.All<StateFlagsRecord>(RecordTypes.StateFlags).Single();
        StateFlags = new StateFlags(Flags, StateFlagsRecord);
        Party = new Party(Items, Flags);
    }

    public PluginSet Plugins { get; }

    public CharacterRules Rules { get; }

    public ItemCatalog Items { get; }

    public FlagNetwork Flags { get; }

    public StateFlagsRecord StateFlagsRecord { get; }

    /// <summary>How camping goes (<c>aidyn:camping/0</c>).</summary>
    public CampingRecord Camping => _camping ??= Plugins.All<CampingRecord>(RecordTypes.Camping).Single();

    private CampingRecord? _camping;

    /// <summary>Writes the clock, rain and terrain into the flags.</summary>
    public StateFlags StateFlags { get; }

    public GameClock Clock { get; } = GameClock.NewGame();

    /// <summary>Time played, in 60 Hz ticks (<c>TerrainStruct.PlayTime</c>).</summary>
    public uint PlayTime { get; set; }

    public Party Party { get; }

    public MapObjectMemory Memory { get; } = new();

    public RandomStreams Random { get; } = new();

    /// <summary>Where the player is, as the runtime last said. None is where a new game starts.</summary>
    public SavePlace? Place { get; set; }

    /// <summary>A name for the game, such as the checkpoint or save it came from.</summary>
    public string? Label { get; set; }

    /// <summary>What loading a save couldn't use, such as records no plugin has any more.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// A new game (<c>init_DBs</c>): the flags' initial values, noon on day 2 of week 6, and Alaron
    /// alone with 1 gold, wearing what his record gives him.
    /// </summary>
    public static GameState NewGame(PluginSet plugins)
    {
        var game = new GameState(plugins) { Label = "New game" };
        game.StateFlags.SetTime(game.Clock);
        if (game.Recruit(Alaron) is { } alaron)
            game.Party.Add(alaron);
        game.Party.Gold = NewGameGold;
        return game;
    }

    /// <summary>
    /// A saved game, rebuilt with the rules (<c>QuestData::LoadFile</c>): the plain flags, with the
    /// new journal entry flag cleared and then the derived ones evaluated; the clock; each member from
    /// their record with the save's experience, stats and skills, putting on what they wear; the
    /// inventory; and what map objects remember. What no plugin has any more is left out and
    /// listed in <see cref="Problems"/>.
    /// </summary>
    public static GameState Load(PluginSet plugins, SaveGame save)
    {
        var game = new GameState(plugins) { Label = save.Label, Place = save.Place, PlayTime = save.PlayTime };
        game.CheckPlugins(save.Plugins);

        // Flag 1 is always set, and formulas read it as stored.
        var set = new HashSet<int> { FlagNetwork.True };
        foreach (var flag in save.Flags)
        {
            if (game.Flags.TryHandle(flag, out var handle) && !game.Flags.IsDerived(handle))
                set.Add(handle);
            else
                game._problems.Add($"{flag} isn't a plain flag any plugin has.");
        }
        // The derived flags are evaluated from a new game's values, so every journal entry the save has turns on
        // again, and the last of them becomes the newest. That sets the new journal entry flag too, but the user
        // found the original never says "A new journal entry has been added." after loading, so it's cleared.
        // The port keeps the journal's order, and entries the save doesn't list come first.
        var newJournalEntry = game.Flags.Handle(game.StateFlagsRecord.NewJournalEntry);
        foreach (var entry in save.Journal.Where(e => !game.Flags.HasJournalEntry(e)))
            game._problems.Add($"{entry} isn't a journal entry any flag adds.");
        game.Flags.Restore(h => h != newJournalEntry && set.Contains(h), save.Journal);
        game.Flags.Set(newJournalEntry, false);

        game.Clock.Time = save.Time % GameClock.Year;
        game.StateFlags.SetTime(game.Clock);

        game.Fill(game.Party, save.Party, game._problems);

        foreach (var memory in save.Memories)
        {
            if (!Enum.TryParse<MemoryKind>(memory.Kind, out var kind))
                game._problems.Add($"{memory.MapObject} remembers {memory.Kind}, which isn't a kind of memory.");
            else
                game.Memory.Remember(new Memory(memory.MapObject, kind, memory.Skill ?? 0), memory.Value, memory.Lifetime);
        }

        if (save.Random is { } random)
        {
            game.Random.Combat.Seed = random.Combat;
            game.Random.Loot.Seed = random.Loot;
            game.Random.World.Seed = random.World;
            game.Random.Wander.Seed = random.Wander;
            game.Random.Cosmetic.Seed = random.Cosmetic;
        }
        return game;
    }

    /// <summary>The game as a save, with the installed plugins named in it.</summary>
    public SaveGame Save() => new()
    {
        Label = Label,
        Plugins = [.. Plugins.Plugins.Select(p => new SavedPlugin(p.Manifest.Id, p.Manifest.Version))],
        PlayTime = PlayTime,
        Time = Clock.Time,
        Place = Place,
        Flags = [.. Enumerable.Range(FlagNetwork.True + 1, Flags.Count - FlagNetwork.True - 1)
            .Where(h => !Flags.IsDerived(h) && Flags.Peek(h)).Select(Flags.Id)],
        Journal = [.. Flags.JournalOrder],
        Party = SaveParty(),
        Memories = [.. Memory.Entries.Select(e => new SaveMemory(e.Key.MapObject, e.Key.Kind.ToString(), e.Value, e.Lifetime)
        {
            Skill = e.Key.Kind == MemoryKind.SkillCheck ? e.Key.Skill : null,
        })],
        Random = new SaveRandom(Random.Combat.Seed, Random.Loot.Seed, Random.World.Seed, Random.Wander.Seed, Random.Cosmetic.Seed),
    };

    /// <summary>The party as a save holds it: its members' sheets, those kept for members who left, its gold and its inventory.</summary>
    public SaveParty SaveParty() => new()
    {
        Gold = Party.Gold,
        Members = [.. Party.Members.Select(SaveCharacter)],
        Kept = [.. Party.Kept.Values],
        Inventory = [.. (Party.Inventory?.Stacks ?? []).Select(s =>
            new SaveStack(s.Item.Id, s.Quantity) { Battery = s.Instance.Battery, Charges = s.Instance.Charges })],
    };

    /// <summary>
    /// Puts the party as a save holds it in place of the one there, as loading does. Key items' flags
    /// stay as they are, and those it holds are set.
    /// </summary>
    public void LoadParty(SaveParty saved)
    {
        Party.Clear();
        Fill(Party, saved, _problems);
    }

    /// <summary>
    /// A copy of the party, made as saving and loading it would, for trying things on without
    /// changing the game. Its key items don't write the flags.
    /// </summary>
    public Party CopyParty()
    {
        var party = new Party(Items);
        Fill(party, SaveParty(), []);
        return party;
    }

    // QuestData::Load's party: the members in slot order, the first being Alaron, the sheets kept for those
    // who left, the gold and the inventory.
    private void Fill(Party party, SaveParty saved, ICollection<string> problems)
    {
        party.Gold = saved.Gold;
        for (var slot = 0; slot < saved.Members.Count; slot++)
        {
            if (LoadCharacter(saved.Members[slot], slot == 0, problems) is { } member && !party.Add(member))
                problems.Add($"{member.Name} doesn't fit in the party.");
        }
        foreach (var kept in saved.Kept)
        {
            if (Plugins.TryGet<EntityRecord>(kept.Entity) is null)
                problems.Add($"{kept.Entity} was kept for rejoining, but isn't a character any plugin has.");
            else if (party.Contains(kept.Entity))
                problems.Add($"{kept.Entity} was kept for rejoining, but is in the party.");
            else
                party.Keep(kept);
        }
        foreach (var stack in saved.Inventory)
        {
            if (Instance(stack.Item, stack.Battery, stack.Charges, problems) is not { } held)
                continue;
            if (!party.Inventory!.Add(held.Item, stack.Quantity, held))
                problems.Add($"{held.Item.Name} doesn't fit in the inventory.");
        }
    }

    /// <summary>
    /// Time passing in trek, <paramref name="ticks"/> of play: the clock runs, the calendar's flags are
    /// written, as they are every frame, and the members' potions run down (<c>TickGameTime</c>,
    /// <c>SetFlagArray_on_Time</c>).
    /// </summary>
    public void PassTime(int ticks)
    {
        Clock.Step(ticks);
        foreach (var member in Party.Members)
            member.StepPotionEffects(ticks);
        StateFlags.SetTime(Clock);
    }

    /// <summary>
    /// Hours passing at once, as after camping (<c>World::Lapse8Hours</c>): the clock moves on, within the year, and the
    /// calendar's flags are written. Time played doesn't, and effects don't run down: camping's heal runs those.
    /// </summary>
    public void Lapse(int hours)
    {
        Clock.Time = (uint)((Clock.Time + (ulong)hours * GameClock.Hour) % GameClock.Year);
        StateFlags.SetTime(Clock);
    }

    /// <summary>
    /// A character as their record starts them, wearing their gear, with their True Name by the flags
    /// (<c>Entity::Init</c>), or null if no plugin has the record.
    /// </summary>
    public Character? Recruit(RecordId entity) => Plugins.TryGet<EntityRecord>(entity) is { } record
        ? Character.Create(record, Rules, Plugins.Get<SpellRecord>, IsSet, Items)
        : null;

    private bool IsSet(RecordId flag) => Flags.TryHandle(flag, out var handle) && Flags.Get(handle);

    /// <summary>A character sheet as a save holds it (<c>SaveParty::SaveCharSheet</c>).</summary>
    public static SaveCharacter SaveCharacter(Character member)
    {
        var stats = Enumerable.Range(0, CharacterRules.StatCount).Select(s => (Stat)s).ToList();
        var skills = Enumerable.Range(0, SkillId.Count).Select(i => member.Skills.RawBase(new SkillId(i)) is var level and >= 0 ? level : (int?)null).ToList();
        var worn = member.Equipment;
        return new SaveCharacter(member.Entity)
        {
            Name = member.Name != member.RecordName ? member.Name : null,
            TotalExp = member.TotalExp,
            SpendableExp = member.SpendableExp,
            Trained = SaveStats.From([.. stats.Select(member.Stats.Trained)]),
            Current = SaveStats.From([.. stats.Select(member.Stats.Current)]),
            Skills = new SkillLevels(skills[0], skills[1], skills[2], skills[3], skills[4], skills[5], skills[6], skills[7], skills[8],
                skills[9], skills[10], skills[11]),
            WeaponSkills = new WeaponSkillLevels(skills[12], skills[13], skills[14], skills[15], skills[16], skills[17], skills[18],
                skills[19], skills[20], skills[21], skills[22]),
            ShieldSkill = skills[23],
            Weapon = Item(worn.Weapon),
            Armor = Item(worn.Armor),
            Shield = Item(worn.Shield),
            Gear = [.. worn.Gear.OfType<ItemInstance>().Select(g => Item(g)!)],
            Spells = [.. member.Spells.Select(s => new KnownSpell(s.Spell.Id, s.Level))],
            Potions = [.. member.PotionEffects.Select(p => new SavePotion(Potions.Id(p.Potion), p.Timer) { Power = p.Power })],
        };

        static SaveItem? Item(ItemInstance? item) => item is null ? null : new SaveItem(item.Id) { Battery = item.Battery, Charges = item.Charges };
    }

    /// <summary>
    /// A character sheet from a save (<c>SaveParty::LoadCharSheet</c>): the record's sheet with the
    /// save's experience, stats and base skills, which leaves the modified values the trained and
    /// current ones, then the armor, shield, weapon and gear put on as the rules allow, which applies
    /// them. A skill the record can't learn stays so. The spellbook is put in the original's order
    /// (<see cref="SpellOrder"/>). Null if no plugin has the record.
    /// </summary>
    /// <param name="alaron">
    /// Whether it's the party's first member, whose save also holds the Elemental, Naming and
    /// Necromancy spells (<c>SaveParty::SaveAlaron</c>).
    /// </param>
    public Character? LoadCharacter(SaveCharacter saved, bool alaron = false) => LoadCharacter(saved, alaron, _problems);

    private Character? LoadCharacter(SaveCharacter saved, bool alaron, ICollection<string> problems)
    {
        if (Plugins.TryGet<EntityRecord>(saved.Entity) is not { } entity)
        {
            problems.Add($"{saved.Entity} isn't a character any plugin has.");
            return null;
        }
        var trained = saved.Trained.ToArray();
        var current = saved.Current.ToArray();
        var stats = new CharacterStats(Rules, trained, current, trained, current);

        var recorded = Character.SkillLevels(entity);
        var s = saved.Skills;
        var w = saved.WeaponSkills;
        int?[] levels = [s.Alchemist, s.Diplomat, s.Healer, s.Loremaster, s.Mechanic, s.Merchant, s.Ranger, s.Stealth, s.Thief,
            s.Troubadour, s.Warrior, s.Wizard, w.Bite, w.Breath, w.Claw, w.Hafted, w.Missile, w.Pole, w.Spit, w.Sting, w.Sword, w.Thrown,
            w.Tusk, saved.ShieldSkill];
        // CharSkills::LoadOne: a skill the record can't learn stays unlearnable. The modified level starts at the
        // base, or at 0 for one that can't be learnt, as the checkpoints hold it.
        var skills = new CharacterSkills(Rules, [.. levels.Select((level, i) => recorded[i] is null ? null : (int?)(sbyte)(level ?? 0))]);

        var spells = new List<LearnedSpell>();
        foreach (var known in saved.Spells)
        {
            if (Plugins.TryGet<SpellRecord>(known.Spell) is { } spell)
                spells.Add(new LearnedSpell(spell, known.Level));
            else
                problems.Add($"{entity.Name} knows {known.Spell}, which no plugin has.");
        }
        var character = Character.Restore(entity, Rules, stats, skills, SpellOrder(spells, entity.School, alaron));
        character.TrueName = entity.TrueName || (entity.TrueNameFlag is { } flag && IsSet(flag));
        if (saved.Name is { Length: > 0 } name)
            character.Name = name;
        character.TotalExp = saved.TotalExp;
        character.SpendableExp = saved.SpendableExp;

        var equipment = character.Equipment;
        if (Instance(saved.Armor, problems) is { } armor)
            equipment.EquipArmor(armor);
        Wear(saved.Shield, equipment.EquipShield);
        Wear(saved.Weapon, equipment.EquipWeapon);
        foreach (var gear in saved.Gear)
            Wear(gear, equipment.EquipGear);
        // SaveParty::LoadCharSheetEffects, after the sheet and what it wears.
        foreach (var potion in saved.Potions)
        {
            if (Potions.Of(potion.Potion) is { } which)
                character.ApplyPotionEffect(which, potion.Power, potion.Timer);
            else
                problems.Add($"{entity.Name} has {potion.Potion} working, which isn't a potion.");
        }
        return character;

        void Wear(SaveItem? item, Func<ItemInstance, EquipRefusal> equip)
        {
            if (Instance(item, problems) is { } instance && equip(instance) is var refusal and not EquipRefusal.None)
                problems.Add($"{entity.Name} can't wear the {instance.Item.Name} ({refusal}), so it's lost.");
        }
    }

    /// <summary>
    /// A spellbook in the order loading a save leaves it (<c>SaveParty::LoadSpellsFromSchool</c>,
    /// <c>LoadAlaron</c>): unschooled spells, then the character's school's (Star for Chaos), then for
    /// Alaron the Elemental, Naming and Necromancy spells, each school in the spell table's order. The
    /// original drops a spell of any other school; the port keeps it, last.
    /// </summary>
    public static IReadOnlyList<LearnedSpell> SpellOrder(IEnumerable<LearnedSpell> spells, MagicSchool school, bool alaron)
    {
        List<MagicSchool> schools = [MagicSchool.None, school == MagicSchool.Chaos ? MagicSchool.Star : school];
        if (alaron)
            schools.AddRange([MagicSchool.Elemental, MagicSchool.Naming, MagicSchool.Necromancy]);
        return [.. spells.OrderBy(s => schools.IndexOf(s.Spell.School) is var rank and >= 0 ? rank : schools.Count)
            .ThenBy(s => s.Spell.Position ?? int.MaxValue)];
    }

    private ItemInstance? Instance(SaveItem? item, ICollection<string> problems) =>
        item is null ? null : Instance(item.Item, item.Battery, item.Charges, problems);

    // SaveParty::LoadItem: a new instance of the item, with the save's charges and battery. The price is the item's.
    private ItemInstance? Instance(RecordId id, int? battery, int? charges, ICollection<string> problems)
    {
        if (Items.TryGet(id) is not { } item)
        {
            problems.Add($"{id} isn't an item any plugin has.");
            return null;
        }
        var instance = new ItemInstance(item);
        if (instance.Battery is not null && battery is { } left)
            instance.Battery = (sbyte)left;
        if (instance.Charges is not null && charges is { } count)
            instance.Charges = (byte)count;
        return instance;
    }

    private void CheckPlugins(IReadOnlyList<SavedPlugin> saved)
    {
        var installed = Plugins.Plugins.ToDictionary(p => p.Manifest.Id, p => p.Manifest.Version);
        foreach (var plugin in saved)
        {
            if (!installed.TryGetValue(plugin.Id, out var version))
                _problems.Add($"It was saved with {plugin.Id}, which isn't installed.");
            else if (version != plugin.Version)
                _problems.Add($"It was saved with {plugin.Id} {plugin.Version}; {version} is installed.");
        }
    }

    // What scripts read and change.

    bool IScriptWorld.InParty(RecordId entity) => Party.Contains(entity);

    bool IScriptWorld.HasItem(RecordId item) => Party.HasItem(item);

    int IScriptWorld.SkillCheck(int skill, IRandom random) => Party.SkillCheck((Skill)skill, random);

    int IScriptWorld.BestStat(int stat) => IsStat(stat) ? Party.BestStat((Stat)stat) : 0;

    int IScriptWorld.WorstStat(int stat) => IsStat(stat) ? Party.WorstStat((Stat)stat) : 0;

    int IScriptWorld.MemberStat(RecordId? member, int stat) => member is { } m && IsStat(stat) ? Party.MemberStat(m, (Stat)stat) : 0;

    int IScriptWorld.PartySize => Party.Count;

    uint IScriptWorld.Gold
    {
        get => Party.Gold;
        set => Party.Gold = value;
    }

    /// <summary>Adds one, if a plugin has the item and the inventory has room.</summary>
    void IScriptWorld.AddItem(RecordId item)
    {
        if (Items.TryGet(item) is { } known)
            Party.Inventory!.Add(known);
    }

    bool IScriptWorld.TakeItem(RecordId item) => Party.TakeItem(item);

    bool IScriptWorld.AddMember(RecordId entity) => AddMember(entity);

    void IScriptWorld.RemoveMember(RecordId entity) => RemoveMember(entity);

    /// <summary>
    /// A member joins (<c>Party::AddMember</c>): as their record starts them, bringing the weapons they
    /// don't wield to the inventory, then, if a sheet was kept when they left, with its experience,
    /// stats, skills, spells and what it still wears in place of the record's. Their joined flag is set
    /// and their left flag cleared. False if the party is full, which the scripts check for first.
    /// </summary>
    /// <remarks>
    /// The original adds a second sheet for someone already in the party, and says they joined. Here
    /// they're not added again, but it still says they joined, so the one talking leaves the map as
    /// in the original. Only Sholeh's first meeting in Ugarit (<c>aidyn:dialogue/0x04AD</c>) can do it,
    /// as nothing hides her after she joins.
    /// </remarks>
    public bool AddMember(RecordId entity)
    {
        if (Party.Count == Party.MaxSize)
            return false;
        if (!Party.Contains(entity))
        {
            if (Recruit(entity) is not { } member)
                return false;
            // SaveEntity::CopyMember, after Entity::Init has already put the spare weapons in the inventory.
            if (Party.TakeKept(entity) is { } kept && LoadCharacter(kept) is { } restored)
            {
                restored.Spares.AddRange(member.Spares);
                member = restored;
            }
            Party.Add(member);
        }
        WriteMemberFlags(entity, joined: true);
        return true;
    }

    /// <summary>
    /// A member leaves (<c>Party::removeAliveMemberByID</c>): what they wear goes to the inventory, but
    /// the Archmage Staff and what there's no room for, and their sheet is kept if their record says
    /// so, without the potions working on them. Their joined flag is cleared and their left flag set,
    /// and the rest of the party moves up. Nothing happens for someone not in the party.
    /// </summary>
    public void RemoveMember(RecordId entity)
    {
        if (Party.Get(entity) is not { } member)
            return;
        Party.Strip(member);
        if (Plugins.TryGet<EntityRecord>(entity)?.Member is { Kept: true })
            Party.Keep(SaveCharacter(member) with { Potions = [] });
        WriteMemberFlags(entity, joined: false);
        Party.Remove(entity);
    }

    // Party::CheckFlags, for a member alive: the joined flag as they are, the left flag the other way.
    private void WriteMemberFlags(RecordId entity, bool joined)
    {
        if (Plugins.TryGet<EntityRecord>(entity)?.Member is not { } flags)
            return;
        SetFlag(flags.Joined, joined);
        if (flags.Left is { } left)
            SetFlag(left, !joined);

        void SetFlag(RecordId flag, bool value)
        {
            if (Flags.TryHandle(flag, out var handle))
                Flags.Set(handle, value);
        }
    }

    bool IScriptWorld.AddExp(RecordId member, int amount)
    {
        if (Party.Get(member) is not { } character)
            return false;
        character.AddExp(amount);
        return true;
    }

    void IScriptWorld.CampHeal() => Party.CampHeal(1f, Camping.EffectTicks);

    bool IScriptWorld.TryRecall(Memory memory, out int value) => Memory.TryRecall(memory, out value);

    void IScriptWorld.Remember(Memory memory, int value) => Memory.Remember(memory, value);

    private static bool IsStat(int stat) => stat is >= 0 and <= (int)Stat.Level;
}
