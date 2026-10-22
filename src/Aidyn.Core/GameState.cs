using Aidyn.Core.Calendar;
using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Rng;
using Aidyn.Core.Scripts;
using Aidyn.Core.Ui;
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

    private readonly List<string> _problems = [];
    private readonly CharacterSaves _saves;

    private GameState(PluginSet plugins)
    {
        Plugins = plugins;
        Rules = CharacterRules.From(plugins);
        Items = ItemCatalog.From(plugins);
        Flags = FlagNetwork.Load(plugins);
        StateFlagsRecord = plugins.Singleton<StateFlagsRecord>();
        StateFlags = new StateFlags(Flags, StateFlagsRecord);
        Party = new Party(Items, Flags);
        _saves = new CharacterSaves(plugins, Rules, Items, Flags, _problems);
        Minimaps = new MinimapSeen(plugins);
        _problems.AddRange(Rules.Magic.Problems);
        Weather = new Weather(plugins.Singleton<WeatherRecord>());
    }

    public PluginSet Plugins { get; }

    public CharacterRules Rules { get; }

    public ItemCatalog Items { get; }

    public FlagNetwork Flags { get; }

    public StateFlagsRecord StateFlagsRecord { get; }

    /// <summary>How camping goes (<c>aidyn:camping/0</c>).</summary>
    public CampingRecord Camping => Plugins.Singleton<CampingRecord>();

    /// <summary>The sounds engine code plays (<c>aidyn:sounds/0</c>).</summary>
    public SoundsRecord Sounds => Plugins.Singleton<SoundsRecord>();

    /// <summary>Writes the clock, rain and terrain into the flags.</summary>
    public StateFlags StateFlags { get; }

    public GameClock Clock { get; } = GameClock.NewGame();

    /// <summary>The weather, by its rules (<c>aidyn:weather/0</c>).</summary>
    public Weather Weather { get; }

    /// <summary>Time played, in 60 Hz ticks (<c>TerrainStruct.PlayTime</c>).</summary>
    public uint PlayTime { get; set; }

    /// <summary>
    /// The ticks counted since the game started running, whatever it was doing: the original's frame counter
    /// (<c>GameFrame.Counter</c>, <c>gGlobals</c> + 8, <c>0x800d8090</c>), one more each frame in <c>AppProc</c>
    /// (<c>0x8001ab0c</c>), which camera objects and the script's cameras keep their place by (<c>ProcessVoxelCamera</c>,
    /// <c>0x800160d4</c>). Not <see cref="PlayTime"/>, which a save keeps; this isn't saved.
    /// </summary>
    public long Ticker { get; private set; }

    /// <summary>
    /// A 60 Hz tick has begun, in every mode, the menus included: the time played counts (<c>World::AddPlayTime</c>), and so
    /// does the <see cref="Ticker"/>. Whatever runs the game calls it once a tick, before stepping anything.
    /// </summary>
    public void CountTick()
    {
        PlayTime++;
        Ticker++;
    }

    public Party Party { get; }

    public MapObjectMemory Memory { get; } = new();

    public RandomStreams Random { get; } = new();

    /// <summary>Which minimaps' pieces the party has seen.</summary>
    public MinimapSeen Minimaps { get; }

    /// <summary>Where the player is, as the runtime last said. None is where a new game starts.</summary>
    public SavePlace? Place { get; set; }

    /// <summary>
    /// Whether the game, as it's loaded, starts as a new game does: it stands nowhere yet, so it's a new game's, or a save of
    /// one (<c>aidyn save new</c>, the editor's new game), which the load screen lists as any other. Every save made in play
    /// notes where the player stands (<see cref="GameSession.NotePlace"/>), as every save of the original is made in trek.
    /// The runtime starts such a game with what the title's New Game does (<see cref="GameSession.StartNewGame"/>), the
    /// opening cinematic and its script, wherever it comes from. Only meaningful before the game is played.
    /// </summary>
    public bool StartsAsNewGame => Place is null;

    /// <summary>A name for the game, such as the checkpoint or save it came from.</summary>
    public string? Label { get; set; }

    /// <summary>What loading a save couldn't use, such as records no plugin has any more.</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// A new game (<c>init_DBs</c>): the flags' initial values, noon on day 2 of week 6, and the new game record's leader,
    /// Alaron, alone with its gold, 1, wearing what his record gives him.
    /// </summary>
    public static GameState NewGame(PluginSet plugins)
    {
        var game = new GameState(plugins) { Label = "New game" };
        game.Weather.NewGame(game.Clock, game.Terrain(0), game.Random.World, game.Random.Cosmetic);
        game.WriteStateFlags();
        var start = plugins.Singleton<NewGameRecord>();
        if (game.Recruit(start.Leader) is { } leader)
            game.Party.Add(leader);
        game.Party.Gold = start.Gold;
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
        // again. That sets the new journal entry flag too, but the user found the original never says "A new journal
        // entry has been added." after loading, so it's cleared, and its journal highlights nothing (Restore).
        // The port keeps the journal's order, and entries the save doesn't list come first.
        var newJournalEntry = game.Flags.Handle(game.StateFlagsRecord.NewJournalEntry);
        foreach (var entry in save.Journal.Where(e => !game.Flags.HasJournalEntry(e)))
            game._problems.Add($"{entry} isn't a journal entry any flag adds.");
        game.Flags.Restore(h => h != newJournalEntry && set.Contains(h), save.Journal);
        game.Flags.Set(newJournalEntry, false);

        game.Clock.Time = save.Time % GameClock.Year;
        game.StateFlags.SetTime(game.Clock);

        game._saves.Load(game.Party, save.Party);

        foreach (var memory in save.Memories)
        {
            if (!Enum.TryParse<MemoryKind>(memory.Kind, out var kind))
                game._problems.Add($"{memory.MapObject} remembers {memory.Kind}, which isn't a kind of memory.");
            else
                game.Memory.Remember(new Memory(memory.MapObject, kind, memory.Skill ?? 0), memory.Value, memory.Lifetime);
        }

        game.Minimaps.Load(save.Minimaps, game._problems);

        // The weather sees the loaded time's part of the day, so the first frame doesn't roll, and the first terrain
        // without a roll. A save without the weather keeps its rain flags until the weather next changes.
        game.Weather.PartOfDay = game.Clock.Date.PartOfDay;
        if (save.Weather is { } weather)
        {
            game.LoadWeather(game.Weather, weather);
            game.WriteStateFlags();
        }

        if (save.Random is { } random)
        {
            game.Random.Combat.Seed = random.Combat;
            game.Random.Loot.Seed = random.Loot;
            game.Random.World.Seed = random.World;
            game.Random.Wander.Seed = random.Wander;
            game.Random.Cosmetic.Seed = random.Cosmetic;
            if (random.Twinkle is { } twinkle)
                game.Random.Twinkle.Seed = twinkle;
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
        Random = new SaveRandom(Random.Combat.Seed, Random.Loot.Seed, Random.World.Seed, Random.Wander.Seed, Random.Cosmetic.Seed)
        {
            Twinkle = Random.Twinkle.Seed,
        },
        Minimaps = Minimaps.Save(),
        Weather = new SaveWeather(Weather.Kind.ToString(), Weather.Precipitation.ToString(), Weather.Intensity, Weather.Fog, Weather.Storm,
            Weather.Wind, Weather.Falling.ToString(), Weather.FallIntensity, Weather.FogTime),
    };

    private void LoadWeather(Weather weather, SaveWeather saved)
    {
        weather.Kind = Parse<WeatherKind>(saved.Kind, "kind of weather");
        weather.Precipitation = Parse<Precipitation>(saved.Precipitation, "precipitation");
        weather.Falling = Parse<Precipitation>(saved.Falling, "precipitation");
        (weather.Intensity, weather.Fog, weather.Storm, weather.Wind) = (saved.Intensity, saved.Fog, saved.Storm, saved.Wind);
        (weather.FallIntensity, weather.FogTime) = (saved.FallIntensity, saved.FogTime);
        // The port's: what's drawn carries on from the save, the rain sky up at once where it rains.
        weather.ResumeDrawing();

        T Parse<T>(string text, string what) where T : struct, Enum
        {
            if (Enum.TryParse<T>(text, out var value))
                return value;
            _problems.Add($"The weather's {text} isn't a {what}.");
            return default;
        }
    }

    /// <summary>The party as a save holds it: its members' sheets, those kept for members who left, its gold and its inventory.</summary>
    public SaveParty SaveParty() => CharacterSaves.Save(Party);

    /// <summary>
    /// Puts the party as a save holds it in place of the one there, as loading does. Key items' flags
    /// stay as they are, and those it holds are set.
    /// </summary>
    public void LoadParty(SaveParty saved)
    {
        Party.Clear();
        _saves.Load(Party, saved);
    }

    /// <summary>
    /// A copy of the party, made as saving and loading it would, for trying things on without
    /// changing the game. Its key items don't write the flags.
    /// </summary>
    public Party CopyParty()
    {
        var party = new Party(Items);
        new CharacterSaves(Plugins, Rules, Items, Flags, []).Load(party, SaveParty());
        return party;
    }

    /// <summary>
    /// Time passing in trek, <paramref name="ticks"/> of play: the clock runs, the weather may change, the calendar's
    /// and the rain's flags are written, as they are every frame, and the members' effects run down
    /// (<c>TickGameTime</c>, <c>World.Update</c>, <c>SetFlagArray_on_Time</c>). What the effects do each round they do once a
    /// 30 fps frame (<see cref="Cadence.FrameTicks"/>), as the original's <c>TrekLoop.TickClock</c> does once a drawn frame:
    /// a worn Ring of Healing heals.
    /// </summary>
    private readonly FrameGate _effectFrame = new(Cadence.FrameTicks);

    public void PassTime(int ticks)
    {
        Clock.Step(ticks);
        var frame = _effectFrame.Tick(ticks) > 0;
        foreach (var member in Party.Members)
            member.AgeEffects(ticks, frame, Clock.Date.PartOfDay, Random.Combat);
        for (var i = 0; i < ticks; i++)
            Weather.Tick(Clock.Date.PartOfDay, Clock.Date.Season, Terrain(Weather.Terrain), Random.World, Random.Cosmetic);
        WriteStateFlags();
    }

    /// <summary>
    /// The player enters a chunk of <paramref name="terrain"/> (<c>World::SetTerrain</c>): its terrain's flags are
    /// written, and for another terrain, the weather may change.
    /// </summary>
    public void EnterTerrain(int terrain)
    {
        Weather.SetTerrain(terrain);
        Weather.Frame(Clock.Date.PartOfDay, Clock.Date.Season, Terrain(terrain), Random.World, Random.Cosmetic);
        WriteStateFlags();
        StateFlags.SetTerrain(terrain);
    }

    /// <summary>The terrain's record, if a plugin has it.</summary>
    public TerrainRecord? Terrain(int? terrain) =>
        terrain is { } t ? TerrainRecord.Find(Plugins, t) : null;

    // World.Update: the calendar's flags and the rain's.
    private void WriteStateFlags()
    {
        StateFlags.SetTime(Clock);
        StateFlags.SetRain((int)Weather.Precipitation);
    }

    /// <summary>
    /// Hours passing at once, as after camping (<c>World::Lapse8Hours</c>): the clock moves on, within the year, the
    /// weather may change, and the calendar's and the rain's flags are written. Time played doesn't, and effects don't run down: camping's heal runs those.
    /// </summary>
    public void Lapse(int hours)
    {
        Clock.Time = (uint)((Clock.Time + (ulong)hours * GameClock.Hour) % GameClock.Year);
        Weather.Frame(Clock.Date.PartOfDay, Clock.Date.Season, Terrain(Weather.Terrain), Random.World, Random.Cosmetic);
        WriteStateFlags();
    }

    /// <summary>How far a combatant's turn moves the clock on (<c>World.AdvanceTurn</c>): 600, ten in-game seconds.</summary>
    public const uint TurnTime = 600;

    /// <summary>
    /// A combatant's turn begins (<c>World.AdvanceTurn</c>, <c>0x8007C01C</c>, from <c>TurnOrder.Advance</c>): the clock
    /// moves on <see cref="TurnTime"/> within the year (<c>World.WrapYear</c>), and <c>World.Update</c> follows, so the
    /// weather rolls if the part of the day has changed and the calendar's and the rain's flags are written. Time played
    /// doesn't move on, and no effects age.
    /// </summary>
    public void AdvanceTurn()
    {
        Clock.Time = (uint)((Clock.Time + (ulong)TurnTime) % GameClock.Year);
        Weather.Frame(Clock.Date.PartOfDay, Clock.Date.Season, Terrain(Weather.Terrain), Random.World, Random.Cosmetic);
        WriteStateFlags();
    }

    /// <summary>
    /// A character as their record starts them, wearing their gear, with their True Name by the flags
    /// (<c>Entity::Init</c>), or null if no plugin has the record.
    /// </summary>
    public Character? Recruit(RecordId entity) => Plugins.TryGet<EntityRecord>(entity) is { } record
        ? Character.Create(record, Rules, Plugins.Get<SpellRecord>, Flags.IsSet, Items)
        : null;

    /// <summary>A character sheet as a save holds it (<see cref="CharacterSaves.Save(Character)"/>).</summary>
    public static SaveCharacter SaveCharacter(Character member) => CharacterSaves.Save(member);

    /// <summary>A spellbook in the order loading a save leaves it (<see cref="CharacterSaves.SpellOrder"/>).</summary>
    public static IReadOnlyList<LearnedSpell> SpellOrder(IEnumerable<LearnedSpell> spells, MagicSchool school, bool alaron) =>
        CharacterSaves.SpellOrder(spells, school, alaron);

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

    /// <summary>
    /// A runner for a script against this game: its flags, its world stream of random numbers, the installed scripts for
    /// random lines, and the flag that rolls skill checks 0–21 (<see cref="StateFlagsRecord.RandomSkillChecks"/>). The
    /// runtime's conversations and cut-scenes and the editor's script page all start theirs here.
    /// </summary>
    /// <param name="script">The script to run.</param>
    /// <param name="host">Where the script's cues go.</param>
    /// <param name="start">Where it starts and which map object it remembers by, or a new game's start.</param>
    /// <param name="world">What the script reads and changes, or this game. The editor passes its own, which fixes skill checks by hand and logs.</param>
    public ScriptRunner RunScript(DialogueRecord script, IScriptHost host, ScriptStart? start = null, IScriptWorld? world = null) =>
        new(script, Flags, world ?? this, host, Random.World, id => Plugins.TryGet<DialogueRecord>(id), start, StateFlagsRecord.RandomSkillChecks);

    bool IScriptWorld.InParty(RecordId entity) => Party.Contains(entity);

    bool IScriptWorld.HasItem(RecordId item) => Party.HasItem(item);

    int IScriptWorld.SkillCheck(int skill, IRandom random) => Party.SkillCheck((Skill)skill, random);

    int IScriptWorld.BestStat(int stat) => IsStat(stat) ? Party.BestStat((Stat)stat) : 0;

    int IScriptWorld.WorstStat(int stat) => IsStat(stat) ? Party.WorstStat((Stat)stat) : 0;

    int IScriptWorld.MemberStat(RecordId? member, int stat) => member is { } m && IsStat(stat) ? Party.MemberStat(m, (Stat)stat) : 0;

    int IScriptWorld.PartySize => Party.Count;

    string? IScriptWorld.PlayerName => Party.Members.FirstOrDefault()?.Name;

    uint IScriptWorld.Gold
    {
        get => Party.Gold;
        set => Party.Gold = value;
    }

    /// <summary>Adds one, if a plugin has the item and the inventory has room.</summary>
    void IScriptWorld.AddItem(RecordId item)
    {
        if (Items.TryGet(item) is { } known)
            Party.Inventory.Add(known);
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
            if (Party.TakeKept(entity) is { } kept && _saves.Load(kept) is { } restored)
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

    /// <summary>
    /// The members' died flags (<c>Party.SyncMemberFlags</c>, <c>0x80074A4C</c>, the docs' <c>Party::CheckDeaths</c>): set for each
    /// member who's dead, and cleared for each who isn't. A member's death in battle and the battle's end write them.
    /// </summary>
    public void WriteDeathFlags()
    {
        foreach (var member in Party.Members)
        {
            if (Plugins.TryGet<EntityRecord>(member.Entity)?.Member is { } flags)
                Flags.TrySet(flags.Died, member.IsDead);
        }
    }

    /// <summary>
    /// The dead leave the party after a battle (<c>Party.AfterBattle</c>, <c>0x80074EE8</c>), however it ended: the died flags are
    /// written (<see cref="WriteDeathFlags"/>), then each dead member in order has any sheet kept of them erased and what they wear
    /// put in the inventory, but what it has no room for, and is gone; the rest move up. Their joined flag stays set and their left
    /// flag clear.
    /// </summary>
    public void BringOutYourDead()
    {
        WriteDeathFlags();
        foreach (var member in Party.Members.Where(m => m.IsDead).ToList())
        {
            Party.TakeKept(member.Entity);
            Party.Strip(member);
            Party.Remove(member.Entity);
        }
    }

    /// <summary>
    /// The clock goes on to 6:00 the next day, whatever the hour (<c>World.SetDate</c>, <c>0x8007B914</c>, as a non-lethal battle
    /// is lost): the weather may change, and the calendar's and the rain's flags are written. No effects age, and time played
    /// doesn't move on.
    /// </summary>
    public void WakeNextMorning()
    {
        Clock.Time = (uint)((Clock.Time - Clock.Time % GameClock.Day + GameClock.Day + 6 * (ulong)GameClock.Hour) % GameClock.Year);
        Weather.Frame(Clock.Date.PartOfDay, Clock.Date.Season, Terrain(Weather.Terrain), Random.World, Random.Cosmetic);
        WriteStateFlags();
    }

    // Party::CheckFlags, for a member alive: the joined flag as they are, the left flag the other way.
    private void WriteMemberFlags(RecordId entity, bool joined)
    {
        if (Plugins.TryGet<EntityRecord>(entity)?.Member is not { } flags)
            return;
        Flags.TrySet(flags.Joined, joined);
        if (flags.Left is { } left)
            Flags.TrySet(left, !joined);
    }

    bool IScriptWorld.AddExp(RecordId member, int amount)
    {
        if (Party.Get(member) is not { } character)
            return false;
        character.AddExp(amount);
        return true;
    }

    void IScriptWorld.CampHeal() => Party.CampHeal(1f, Camping.EffectTicks, Random.Combat);

    bool IScriptWorld.TryRecall(Memory memory, out int value) => Memory.TryRecall(memory, out value);

    void IScriptWorld.Remember(Memory memory, int value) => Memory.Remember(memory, value);

    private static bool IsStat(int stat) => stat is >= 0 and <= (int)Stat.Level;
}
