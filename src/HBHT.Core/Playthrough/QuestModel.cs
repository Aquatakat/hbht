using System.Collections.Concurrent;
using HBHT.Core.Events;
using HBHT.Core.Scripts;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Playthrough;

/// <summary>
/// The quest graph as the planner plays it: every action the player can take, each one way, with its preconditions and effects,
/// built from the installed plugins. Talking to an npc or using a script object is one action per way through its script
/// (<see cref="ScriptWays"/>), with the npc's flag, the battle the way ends in and the key items a shop at its end sells; a container,
/// tripwire, encounter, secret door, door trap and lock, and each world map region, one action each.
/// </summary>
/// <remarks>
/// <para>
/// Facts are what the relaxation works over (<see cref="Relaxation"/>): each flag set or clear, each member in or out, each tracked item
/// held or not, and the credits. They're numbered here.
/// </para>
/// </remarks>
public sealed class QuestModel
{
    private readonly Dictionary<RecordId, MemberFlags> _members;
    private readonly Dictionary<RecordId, KeyItemFlags> _keyItems;
    private readonly Dictionary<RecordId, int> _memberIndex;
    private readonly Dictionary<RecordId, int> _itemIndex;
    private readonly List<PlayerAction> _actions = [];
    private readonly List<string> _notes = [];
    private readonly HashSet<RecordId> _neverThere = [];
    private int[][] _pre = [];
    private int[][] _adds = [];
    private RecordId[][] _places = [];

    private QuestModel(PluginSet plugins, FlagModel flags, IPlaceReach reach)
    {
        Plugins = plugins;
        Flags = flags;
        Reach = reach;
        _members = plugins.All<EntityRecord>(RecordTypes.Entity).Where(e => e.Member is not null).ToDictionary(e => e.Id, e => e.Member!);
        _keyItems = plugins.All<ItemRecord>(RecordTypes.Item).Where(i => i.KeyItem is not null).ToDictionary(i => i.Id, i => i.KeyItem!);
        _memberIndex = [];
        _itemIndex = [];
    }

    public PluginSet Plugins { get; }

    public FlagModel Flags { get; }

    /// <summary>
    /// Who answers whether a place can be reached; <see cref="Everywhere"/> until the world graph does. It's the one thing a run
    /// changes, so runs side by side each take their own model (<see cref="ForRun"/>).
    /// </summary>
    public IPlaceReach Reach { get; set; }

    /// <summary>
    /// The model for one run: the same actions, ways and facts, which nothing changes once it's built and which any number of runs share
    /// on any threads, with a <see cref="Reach"/> of its own (<see cref="Everywhere"/>) and its own cache of what places need.
    /// </summary>
    public QuestModel ForRun()
    {
        var run = (QuestModel)MemberwiseClone();
        run.Reach = Everywhere.Instance;
        run._placed = null;
        return run;
    }

    /// <summary>Every action, by <see cref="PlayerAction.Index"/>.</summary>
    public IReadOnlyList<PlayerAction> Actions => _actions;

    /// <summary>A new game's opening (<c>game.started</c>'s triggers), as actions of their own.</summary>
    public IReadOnlyList<PlayerAction> Opening { get; private set; } = [];

    /// <summary>Every way through each script something starts, by script.</summary>
    public IReadOnlyDictionary<RecordId, ScriptWaysResult> Ways { get; private set; } = new Dictionary<RecordId, ScriptWaysResult>();

    /// <summary>The items whose holding the model follows: key items, and those a script checks for or takes.</summary>
    public IReadOnlyCollection<RecordId> TrackedItems => _itemIndex.Keys;

    /// <summary>Everyone the model follows in or out of the party: the members, and anyone a script checks for, adds or removes.</summary>
    public IReadOnlyCollection<RecordId> People => _memberIndex.Keys;

    /// <summary>The entities who can join, with their joined, left and died flags.</summary>
    public IReadOnlyDictionary<RecordId, MemberFlags> Members => _members;

    /// <summary>Map objects a gate on flag 0 or 1 keeps from ever being there or working.</summary>
    public IReadOnlySet<RecordId> NeverThere => _neverThere;

    /// <summary>What the model leaves out, for the report.</summary>
    public IReadOnlyList<string> Notes => _notes;

    public bool IsTracked(RecordId item) => _itemIndex.ContainsKey(item);

    public MemberFlags? MemberFlags(RecordId entity) => _members.GetValueOrDefault(entity);

    public KeyItemFlags? KeyItem(RecordId item) => _keyItems.GetValueOrDefault(item);

    /// <summary>Builds the model over the plugins, exploring every script something starts (some seconds, on every core).</summary>
    public static QuestModel Build(PluginSet plugins, IPlaceReach? reach = null)
    {
        var flags = new FlagModel(plugins);
        var model = new QuestModel(plugins, flags, reach ?? Everywhere.Instance);
        model.Load();
        return model;
    }

    private void Load()
    {
        var chunks = Plugins.All<ChunkRecord>(RecordTypes.Chunk);
        var objects = chunks.SelectMany(c => c.Objects.Select(o => (Id: c.ObjectId(o.Index), Object: o, Chunk: c))).ToList();
        var triggers = new Triggers(Plugins);

        // The scripts something starts: npcs that talk, script objects, and the objects triggers start.
        var started = objects.Select(o => o.Object switch
        {
            NpcObject n when (n.Flags & PartyFlags.Silent) == 0 => n.Script,
            ScriptObject s => s.Plays,
            _ => (RecordId?)null,
        }).OfType<RecordId>().Distinct().Select(Plugins.TryGet<ScriptRecord>).OfType<ScriptRecord>().ToList();
        var explorers = new ThreadLocal<ScriptWays>(() => new ScriptWays(Plugins, Flags, triggers));
        var ways = new ConcurrentDictionary<RecordId, ScriptWaysResult>();
        Parallel.ForEach(started, script => ways[script.Id] = explorers.Value!.Explore(script));
        Ways = ways;
        foreach (var cut in ways.Values.Where(w => w.Truncated).OrderBy(w => w.Script.ToString(), StringComparer.Ordinal))
            _notes.Add($"{cut.Script}: its ways were cut short after {cut.Runs} runs, so some of what it can do may be missing.");

        Track();

        var shops = Plugins.All<ShopRecord>(RecordTypes.Shop).GroupBy(s => s.Shopkeeper).ToDictionary(g => g.Key, g => g.First());
        var opening = new List<PlayerAction>();
        foreach (var way in explorers.Value!.Event(GameEvents.GameStarted, null))
            opening.Add(Add(ActionKind.Event, null, null, way.Choices, [], way.Requires, way.Effects)!);
        Opening = opening;

        var camping = Plugins.TrySingleton<CampingRecord>();
        foreach (var (id, o, chunk) in objects)
        {
            switch (o)
            {
                case NpcObject npc when (npc.Flags & PartyFlags.Silent) == 0:
                    AddScript(ActionKind.Talk, id, npc.Script, Presence(id, o.Visible, o.Enabled), npc.Used?.Flag, npc, shops);
                    break;
                case ScriptObject play when camping is not null && o.Enabled is { Invert: false } scene && camping.Scenes.Contains(scene.Flag):
                    AddCamp(id, play, chunk, camping);
                    break;
                case ScriptObject play:
                    AddScript(ActionKind.Play, id, play.Plays, Presence(id, o.Visible, o.Enabled), null, null, shops);
                    break;
                case ContainerObject container:
                    AddContainer(id, container);
                    break;
                case TripwireObject tripwire:
                {
                    var effects = new List<Effect>();
                    if (tripwire.Does is SetFlagAction set)
                        effects.Add(new SetFlag(set.Flag, true));
                    if (tripwire.Used is { } used)
                        effects.Add(new SetFlag(used.Flag, !used.Clear));
                    if (effects.Count > 0)
                        Add(ActionKind.Trip, id, null, [], Presence(id, o.Visible, o.Enabled), [], effects);
                    break;
                }
                case EncounterObject encounter:
                {
                    var presence = Presence(id, o.Visible, o.Enabled);
                    if (presence is null)
                        break;
                    if (encounter.Used is { } beaten)
                        presence.Add(new FlagIs(beaten.Flag, false));
                    var effects = Battle(Sure(encounter.Enemies), encounter.Used?.Flag, encounter.Loot, encounter.BattleRules);
                    if (effects.Count > 0 && !NeverWon(encounter.Enemies.Select(e => e.Entity)))
                        Add(ActionKind.Fight, id, null, [], presence, [Won], effects, wins: true);
                    if (Merge(encounter.Enemies.Select(e => e.Entity)) is { } merge)
                        Add(ActionKind.Fight, id, null, [], presence, merge.Requires, merge.Effects, wins: true);
                    break;
                }
                case TeleporterObject door when !door.Tangible:
                    AddDoor(id, door);
                    break;
            }
        }

        foreach (var set in Plugins.All<MapSetRecord>(RecordTypes.MapSet))
        {
            foreach (var region in Plugins.ChunksIn(set.Id).Where(c => c.RegionFound(Plugins) is not null).GroupBy(c => c.RegionFound(Plugins)!.Value))
            {
                var first = region.First().Id;
                Add(ActionKind.Enter, first, null, [], [new Reaches(first)], [], [new SetFlag(region.Key, true)]);
            }
        }

        AddHelp(explorers.Value!);
        Index();
    }

    /// <summary>
    /// The help menu, which the System tab's Help starts wherever the player is in trek (docs/systems/pause-menu.md): an action per way
    /// through its script, with no place, which the run plays by the menu's own presses.
    /// </summary>
    private void AddHelp(ScriptWays explorer)
    {
        if (Plugins.TryGet<ScriptRecord>(Ui.SystemMenu.HelpScript) is not { } help)
            return;
        var result = explorer.Explore(help);
        foreach (var way in result.Ways)
            Add(ActionKind.Help, null, help.Id, way.Choices, [], way.Requires, way.Effects, fault: way.Fault, others: way.OtherChoices, way: way);
    }

    /// <summary>
    /// A camp scene's script object (docs/systems/camping.md): its script plays as the party camps in its chunk while its enabling flag holds,
    /// which is a derived flag over the camping record's check flag, set only while the camp looks for a scene. So it's an action of its own,
    /// camping there: its place the object (the pilot camps once it stands in the object's chunk), its preconditions the scene's flag's other
    /// inputs as its formula wants them and not being ambushed, and its effects the check flag set and cleared about the script's way, so the
    /// relaxation sees the scene's flag come true. The object is never used otherwise, so it has no play actions. A scene whose flag isn't a
    /// value its inputs' bits must equal, or whose chunk's terrain allows no camping, isn't modelled.
    /// </summary>
    private void AddCamp(RecordId id, ScriptObject scene, ChunkRecord chunk, CampingRecord camping)
    {
        var flag = scene.Enabled!.Flag;
        if (Plugins.TryGet<TerrainRecord>(chunk.Terrain) is not { Camp: > 0 })
        {
            _notes.Add($"{id}: its camp scene's chunk allows no camping.");
            return;
        }
        if (!Flags.TryHandle(flag, out var h) || Flags.Formula(h) is not ({ Kind: FlagFormulaKind.Logic, Op: FlagOperation.Mask } or { Kind: FlagFormulaKind.Value, Op: FlagOperation.Equal }) || Flags.Formula(h) is not { } formula)
        {
            _notes.Add($"{id}: its camp scene's flag {flag} isn't a value its inputs make, so camping there isn't modelled.");
            return;
        }
        var wants = new List<Literal>();
        var checking = false;
        for (var i = 0; i < formula.Inputs.Count; i++)
        {
            var set = (formula.Operand >> i & 1) != 0;
            if (formula.Inputs[i] == camping.Checking)
                checking = set;
            else
                wants.Add(new FlagIs(formula.Inputs[i], set));
        }
        if (!checking || formula.Operand >> formula.Inputs.Count != 0 || Presence(id, scene.Visible) is not { } presence || !Ways.TryGetValue(scene.Plays, out var result))
        {
            _notes.Add($"{id}: its camp scene's flag {flag} never holds as the party camps.");
            return;
        }
        presence.AddRange(wants);
        var ambush = new CodeGate("camping without being ambushed") { Check = new(CodeCheckKind.Ambush) };
        foreach (var way in result.Ways)
        {
            Add(ActionKind.Camp, id, scene.Plays, way.Choices, presence, [.. way.Requires, ambush],
                [new SetFlag(camping.Checking, true), .. way.Effects, new SetFlag(camping.Checking, false)], fault: way.Fault, others: way.OtherChoices, way: way);
        }
    }

    /// <summary>A win the planner takes as given: the battle pilot plays it, and plays it again until it's won.</summary>
    private static readonly CodeGate Won = new("winning the battle") { Check = new(CodeCheckKind.Win) };

    /// <summary>
    /// Whether a battle is never won: the Shadow's, which ends in its merging with Alaron or its vanishing, neither of which gives rewards or
    /// sets an encounter or kill flag (docs/systems/combat.md, the special battles). Its encounter's beaten flag (Overworld H08/12's 950)
    /// never comes.
    /// </summary>
    private bool NeverWon(IEnumerable<RecordId> enemies) =>
        Plugins.TrySingleton<CombatRecord>()?.Shadow is { } shadow && enemies.Contains(shadow.Entity);

    /// <summary>
    /// A battle with the Shadow can end in its merging with Alaron instead, while the party carries the Horn of Kynon (the combat
    /// record's <see cref="ShadowRule"/>): its cinematic plays and the boost's flag is set (<c>Combat.MergeShadow</c>). Null if the
    /// Shadow isn't among the enemies.
    /// </summary>
    private (List<Literal> Requires, List<Effect> Effects)? Merge(IEnumerable<RecordId> enemies)
    {
        if (Plugins.TrySingleton<CombatRecord>()?.Shadow is not { } shadow || !enemies.Contains(shadow.Entity))
            return null;
        var effects = new List<Effect>();
        if (Plugins.TryGet<CinematicRecord>(shadow.Cinematic) is { } cinematic)
            effects.Add(new SetFlag(cinematic.DoneFlag, true));
        effects.Add(new SetFlag(shadow.Boost.Flag, true));
        return ([new ItemIs(shadow.Item, true), new CodeGate("the Shadow reaching Alaron and merging") { Check = new(CodeCheckKind.Merge) }], effects);
    }

    /// <summary>
    /// The people and items the model follows, numbered for the facts: the members and Alaron, and whoever the ways name; the key
    /// items, and whatever the ways check for, give or take.
    /// </summary>
    private void Track()
    {
        var people = new HashSet<RecordId>(_members.Keys) { GameState.Alaron };
        var items = new HashSet<RecordId>(_keyItems.Keys);
        if (Plugins.TrySingleton<CombatRecord>()?.Shadow is { } shadow)
            items.Add(shadow.Item);
        foreach (var way in Ways.Values.SelectMany(w => w.Ways))
        {
            foreach (var literal in way.Requires)
            {
                if (literal is MemberIs m)
                    people.Add(m.Entity);
                else if (literal is ItemIs i)
                    items.Add(i.Item);
            }
            foreach (var effect in way.Effects)
            {
                if (effect is Join j)
                    people.Add(j.Entity);
                else if (effect is Leave l)
                    people.Add(l.Entity);
                else if (effect is Lose lost)
                    items.Add(lost.Item);
            }
        }
        foreach (var p in people.OrderBy(p => p.ToString(), StringComparer.Ordinal))
            _memberIndex[p] = _memberIndex.Count;
        foreach (var i in items.OrderBy(i => i.ToString(), StringComparer.Ordinal))
            _itemIndex[i] = _itemIndex.Count;
    }

    /// <summary>An object's gates as literals, and that it can be reached; null if a gate on a constant flag never holds.</summary>
    private List<Literal>? Presence(RecordId id, params FlagGate?[] gates)
    {
        var literals = new List<Literal>();
        foreach (var gate in gates)
        {
            if (gate is not { } g)
                continue;
            if (Flags.TryHandle(g.Flag, out var h) && Flags.Kind(h) == FlagKind.Constant)
            {
                if ((h == 1) == g.Invert)
                {
                    _neverThere.Add(id);
                    return null;
                }
                continue;
            }
            literals.Add(new FlagIs(g.Flag, !g.Invert));
        }
        literals.Add(new Reaches(id));
        return literals;
    }

    private void AddScript(ActionKind kind, RecordId id, RecordId scriptId, List<Literal>? presence, RecordId? talked, NpcObject? npc,
        Dictionary<RecordId, ShopRecord> shops)
    {
        if (presence is null || !Ways.TryGetValue(scriptId, out var result))
            return;
        var script = Plugins.Get<ScriptRecord>(scriptId);
        foreach (var way in result.Ways)
        {
            var effects = new List<Effect>();
            if (talked is { } flag)
                effects.Add(new SetFlag(flag, true));
            effects.AddRange(way.Effects);
            var requires = way.Requires.ToList();
            var wins = way.End == ScriptEndKind.Battle;
            if (wins)
            {
                var fighters = npc is not null ? npc.Fighters.Select(f => f.Entity) : way.Enemies;
                if (Merge(fighters) is { } merge)
                    Add(kind, id, scriptId, way.Choices, presence, [.. requires, .. merge.Requires], [.. effects, .. merge.Effects], true,
                        fault: way.Fault, others: way.OtherChoices, way: way);
                if (NeverWon(fighters))
                {
                    // The Shadow's battle without the Horn ends in its vanishing, with nothing won and no flag set: the way's own effects.
                    wins = false;
                }
                else
                {
                    requires.Add(Won);
                    // In a conversation with someone who wanders, the original fights their party instead (BattleSetup.Against).
                    effects.AddRange(npc is not null
                        ? Battle(Sure(npc.Fighters), npc.BattleFlag, npc.Loot, npc.BattleRules)
                        : Battle(way.Enemies, null, null, script.BattleRules));
                }
            }
            var action = Add(kind, id, scriptId, way.Choices, presence, requires, effects, wins, fault: way.Fault, others: way.OtherChoices, way: way);
            if (action is null || way.End != ScriptEndKind.Shop)
                continue;
            // A shop or training with someone who wanders is theirs (set_shopkeep); else the op's keeper's, their first shop.
            var keeper = npc?.Entity ?? way.Keeper;
            if (keeper is not { } k || !shops.TryGetValue(k, out var shop))
                continue;
            foreach (var stock in shop.Stock.Where(s => _keyItems.ContainsKey(s.Item)).DistinctBy(s => s.Item))
            {
                Add(kind, id, scriptId, way.Choices, presence, [.. requires, new CodeGate($"the gold to buy {stock.Item}") { Check = new(CodeCheckKind.Buy, A: stock.Item, B: k) }],
                    [.. effects, new Gain(stock.Item)], wins, stock.Item, way.Fault, way.OtherChoices, way);
            }
        }
    }

    private void AddContainer(RecordId id, ContainerObject c)
    {
        var chest = c.Kind is ContainerKind.Chest or ContainerKind.Sack or ContainerKind.Crate or ContainerKind.Barrel;
        var presence = Presence(id, c.Visible, chest ? null : c.Enabled);
        if (presence is null)
            return;
        if (chest)
        {
            if (c.Opened is { } opened)
                presence.Add(new FlagIs(opened, false));
            if (c.Exploded is { } exploded)
                presence.Add(new FlagIs(exploded, false));
        }
        var requires = new List<Literal>();
        var effects = new List<Effect>();
        if (c.Kind is not (ContainerKind.Herb or ContainerKind.Spice or ContainerKind.Gem))
        {
            if (c.TrapLevel != 0)
                requires.Add(new CodeGate($"disarming a trap of level {c.TrapLevel}") { Check = new(CodeCheckKind.Trap, c.TrapLevel) });
            if (c.LockLevel != 0)
                requires.Add(new CodeGate($"picking a lock of level {c.LockLevel}") { Check = new(CodeCheckKind.Lock, c.LockLevel) });
            if (c.Opened is { } opened)
                effects.Add(new SetFlag(opened, true));
        }
        if (c.Used is { } used)
            effects.Add(new SetFlag(used.Flag, !used.Clear));
        if (c.Kind is not (ContainerKind.Herb or ContainerKind.Spice or ContainerKind.Gem))
        {
            foreach (var (item, chance) in Given(c.Item, c.Loot))
            {
                effects.Add(new Gain(item));
                if (chance < 100)
                    requires.Add(new CodeGate($"a {chance}% chance of {item}") { Check = new(CodeCheckKind.Chance, chance, A: item) });
            }
        }
        if (effects.Count > 0)
            Add(ActionKind.Open, id, null, [], presence, requires, effects);
        // Its trap going off as the party tries it: the container destroyed with what it held, and its exploded flag set
        // (docs/systems/map-objects.md, containers). It and opening it each rule the other out.
        if (chest && c.TrapLevel != 0 && c.Exploded is { } blown)
        {
            Add(ActionKind.Spring, id, null, [], presence,
                [new CodeGate($"a trap of level {c.TrapLevel} going off") { Check = new(CodeCheckKind.Spring, c.TrapLevel) }], [new SetFlag(blown, true)]);
        }
    }

    private void AddDoor(RecordId id, TeleporterObject door)
    {
        var presence = Presence(id, door.Visible, door.Enabled);
        if (presence is null)
            return;
        if (door.SecretFlag is { } secret)
        {
            Add(ActionKind.FindSecret, id, null, [], [.. presence, new FlagIs(secret, false)], [], [new SetFlag(secret, true)]);
            presence.Add(new FlagIs(secret, true));
        }
        if (door.TrapLevel != 0 && door.DisarmFlag is { } disarmed)
        {
            List<Literal> requires = door.TrapFlag is { } trap ? [new FlagIs(trap, false)] : [];
            Add(ActionKind.Disarm, id, null, [], presence, [.. requires, new CodeGate($"disarming a trap of level {door.TrapLevel}") { Check = new(CodeCheckKind.Trap, door.TrapLevel) }],
                [new SetFlag(disarmed, true)]);
        }
        if (door.LockLevel != 0 && door.PickedFlag is { } picked)
        {
            List<Literal> requires = door.LockFlag is { } locked ? [new FlagIs(locked, false)] : [];
            Add(ActionKind.PickLock, id, null, [], presence, [.. requires, new CodeGate($"picking a lock of level {door.LockLevel}") { Check = new(CodeCheckKind.Lock, door.LockLevel) }],
                [new SetFlag(picked, true)]);
        }
    }

    /// <summary>
    /// What winning a battle does: the encounter flag, the rules' end flag (set however it ends), the bosses' flags of the enemy list's
    /// first four (<c>Combat.SetKillFlags</c>), and the key items of the party's own loot.
    /// </summary>
    private List<Effect> Battle(IEnumerable<RecordId> enemies, RecordId? encounter, RecordId? loot, BattleRules? rules)
    {
        var effects = new List<Effect>();
        if (encounter is { } flag)
            effects.Add(new SetFlag(flag, true));
        if (rules?.End?.Flag is { } end)
            effects.Add(new SetFlag(end, true));
        foreach (var enemy in enemies.Take(4))
        {
            if (Plugins.TryGet<EntityRecord>(enemy)?.Combat?.BossFlag is { } boss)
                effects.Add(new SetFlag(boss, true));
        }
        foreach (var (item, _) in Given(null, loot))
            effects.Add(new Gain(item));
        return effects;
    }

    /// <summary>A party's enemy list before its random extras: each kind's least, in order (<c>BattleSetup.FillEnemies</c>).</summary>
    private static IEnumerable<RecordId> Sure(IEnumerable<PartyEnemy> kinds)
    {
        var list = kinds.ToList();
        var sure = list.SelectMany(k => Enumerable.Repeat(k.Entity, k.Min)).ToList();
        return sure.Count > 0 ? sure : list.Select(k => k.Entity);
    }

    /// <summary>The tracked items something gives: an item, and a loot table's drops (or a single item named as loot), with their chance.</summary>
    private IEnumerable<(RecordId Item, int Chance)> Given(RecordId? item, RecordId? loot)
    {
        if (item is { } i && IsTracked(i))
            yield return (i, 100);
        if (loot is not { } l)
            yield break;
        if (l.Type == RecordTypes.Loot && Plugins.TryGet<LootRecord>(l) is { } table)
        {
            foreach (var drop in table.Drops)
            {
                if (drop.Item is var d && IsTracked(d))
                    yield return (d, drop.Chance);
            }
        }
        else if (IsTracked(l))
        {
            yield return (l, 100);
        }
    }

    private PlayerAction? Add(ActionKind kind, RecordId? place, RecordId? script, IReadOnlyList<int> choices, List<Literal>? presence,
        IReadOnlyList<Literal> requires, IReadOnlyList<Effect> effects, bool wins = false, RecordId? buys = null, string? fault = null,
        IReadOnlyList<IReadOnlyList<int>>? others = null, ScriptWay? way = null)
    {
        if (presence is null || !Consistent(presence.Concat(requires)))
            return null;
        var action = new PlayerAction(_actions.Count, kind, place, script, choices, [.. presence], requires, effects, wins, buys, fault)
        {
            OtherChoices = others ?? [],
            Way = way,
        };
        _actions.Add(action);
        return action;
    }

    /// <summary>Whether literals can all hold at once.</summary>
    private static bool Consistent(IEnumerable<Literal> literals)
    {
        var seen = new HashSet<Literal>();
        int min = 1, max = HBHT.Core.Characters.Party.MaxSize;
        foreach (var literal in literals)
        {
            Literal? opposite = literal switch
            {
                FlagIs f => f with { Set = !f.Set },
                MemberIs m => m with { In = !m.In },
                ItemIs i => i with { Held = !i.Held },
                _ => null,
            };
            if (opposite is not null && seen.Contains(opposite))
                return false;
            seen.Add(literal);
            if (literal is PartySizeIn size)
            {
                min = Math.Max(min, size.Min);
                max = Math.Min(max, size.Max);
            }
        }
        return min <= max;
    }

    // Facts.

    /// <summary>How many facts there are.</summary>
    public int FactCount => 2 * Flags.Count + 2 * _memberIndex.Count + 2 * _itemIndex.Count + 1;

    public int FlagFact(int handle, bool set) => 2 * handle + (set ? 0 : 1);

    public int? PersonFact(RecordId entity, bool inParty) =>
        _memberIndex.TryGetValue(entity, out var i) ? 2 * Flags.Count + 2 * i + (inParty ? 0 : 1) : null;

    public int? ItemFact(RecordId item, bool held) =>
        _itemIndex.TryGetValue(item, out var i) ? 2 * Flags.Count + 2 * _memberIndex.Count + 2 * i + (held ? 0 : 1) : null;

    public int CreditsFact => FactCount - 1;

    /// <summary>A literal's fact, or null for one that isn't a fact (the party's size, a place, a code gate).</summary>
    public int? Fact(Literal literal) => literal switch
    {
        FlagIs f when Flags.TryHandle(f.Flag, out var h) => FlagFact(h, f.Set),
        MemberIs m => PersonFact(m.Entity, m.In),
        ItemIs i => ItemFact(i.Item, i.Held),
        _ => null,
    };

    /// <summary>What a fact says, for reports.</summary>
    public string DescribeFact(int fact)
    {
        if (fact == CreditsFact)
            return "the ending's credits";
        if (fact < 2 * Flags.Count)
            return new FlagIs(Flags.Id(fact / 2), fact % 2 == 0).Describe();
        var rest = fact - 2 * Flags.Count;
        if (rest < 2 * _memberIndex.Count)
            return new MemberIs(_memberIndex.First(p => p.Value == rest / 2).Key, rest % 2 == 0).Describe();
        rest -= 2 * _memberIndex.Count;
        return new ItemIs(_itemIndex.First(p => p.Value == rest / 2).Key, rest % 2 == 0).Describe();
    }

    /// <summary>The facts an action needs (its presence and requirements that are facts).</summary>
    public IReadOnlyList<int> Pre(int action) => _pre[action];

    /// <summary>The facts an action's effects make true.</summary>
    public IReadOnlyList<int> Adds(int action) => _adds[action];

    private (IPlaceReach Reach, int[]?[] Facts)? _placed;

    private static readonly int[] NoFacts = [];

    /// <summary>
    /// The facts the way to an action's places needs under <see cref="Reach"/> (<see cref="IPlaceReach.Gates"/>): none for a place
    /// reached as the gates stand, or null if one of its places can't be reached however they stand. Kept until the reach changes.
    /// </summary>
    public IReadOnlyList<int>? PlaceNeeds(int action)
    {
        if (_placed is not { } placed || !ReferenceEquals(placed.Reach, Reach))
            _placed = placed = (Reach, new int[]?[_actions.Count]);
        if (placed.Facts[action] is { } known)
            return known.Length == 1 && known[0] < 0 ? null : known;
        int[]? facts = NoFacts;
        foreach (var place in _places[action])
        {
            if (Reach.Gates(place) is not { } gates)
            {
                facts = null;
                break;
            }
            if (gates.Count > 0)
                facts = [.. facts!.Concat(gates.Select(Fact).OfType<int>()).Distinct()];
        }
        placed.Facts[action] = facts ?? [-1];
        return facts;
    }

    /// <summary>
    /// Everything an action needs as facts: its own preconditions (<see cref="Pre"/>), and what the way to its place needs
    /// (<see cref="PlaceNeeds"/>), which the relaxation and the planner's backward chain take as preconditions too.
    /// </summary>
    public IReadOnlyList<int> Needs(int action) => PlaceNeeds(action) is { Count: > 0 } placed ? [.. Pre(action).Concat(placed).Distinct()] : Pre(action);

    /// <summary>Whether an action can never be done for its places: one the reach says nothing gets to (<see cref="IPlaceReach.Gates"/> null).</summary>
    public bool Unplaced(int action) => PlaceNeeds(action) is null;

    /// <summary>The facts a forced action needs: its requirements alone, not its object's gates.</summary>
    public IEnumerable<int> RequiresFacts(PlayerAction action) => action.Requires.Select(Fact).OfType<int>().Distinct();

    private void Index()
    {
        _pre = new int[_actions.Count][];
        _adds = new int[_actions.Count][];
        _places = new RecordId[_actions.Count][];
        foreach (var action in _actions)
        {
            _places[action.Index] = [.. action.Presence.OfType<Reaches>().Select(r => r.Place)];
            _pre[action.Index] = [.. action.Presence.Concat(action.Requires).Select(Fact).OfType<int>().Distinct()];
            _adds[action.Index] = [.. AddsOf(action).Distinct()];
        }
    }

    private IEnumerable<int> AddsOf(PlayerAction action)
    {
        int? Flag(RecordId? flag, bool value) =>
            flag is { } f && Flags.TryHandle(f, out var h) && Flags.Kind(h) == FlagKind.Plain ? FlagFact(h, value) : null;

        foreach (var effect in action.Effects)
        {
            IEnumerable<int?> facts = effect switch
            {
                SetFlag s => [Flag(s.Flag, s.Value)],
                Join j => [PersonFact(j.Entity, true), Flag(MemberFlags(j.Entity)?.Joined, true), Flag(MemberFlags(j.Entity)?.Left, false)],
                Leave l => [PersonFact(l.Entity, false), Flag(MemberFlags(l.Entity)?.Joined, false), Flag(MemberFlags(l.Entity)?.Left, true)],
                Gain g => [ItemFact(g.Item, true), Flag(KeyItem(g.Item)?.Held, true), Flag(KeyItem(g.Item)?.Region, true)],
                Lose l => [ItemFact(l.Item, false), Flag(KeyItem(l.Item)?.Held, false)],
                RollCredits => [CreditsFact],
                _ => [],
            };
            foreach (var fact in facts.OfType<int>())
                yield return fact;
        }
    }

    /// <summary>The facts true in a state; engine flags are both, derived flags what their formulas can make them.</summary>
    public bool[] Facts(PlanState state)
    {
        var facts = new bool[FactCount];
        for (var h = 0; h < Flags.Count; h++)
        {
            var v = state.Value(h);
            facts[FlagFact(h, true)] = (v & FlagModel.CanBeSet) != 0;
            facts[FlagFact(h, false)] = (v & FlagModel.CanBeClear) != 0;
        }
        foreach (var (person, _) in _memberIndex)
        {
            var inParty = state.InParty(person);
            facts[PersonFact(person, inParty)!.Value] = true;
        }
        foreach (var (item, _) in _itemIndex)
            facts[ItemFact(item, state.Holds(item))!.Value] = true;
        facts[CreditsFact] = state.Credits;
        return facts;
    }
}
