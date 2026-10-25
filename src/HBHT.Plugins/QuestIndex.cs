using HBHT.Plugins.Records;

namespace HBHT.Plugins;

/// <summary>What a record does with a flag.</summary>
public enum FlagAccess
{
    Read,
    Set,
    Clear,
}

/// <summary>Where a use of a flag comes from, which says what the use means.</summary>
public enum FlagRole
{
    /// <summary>A script node's SetFlag, ClearFlag or CheckFlag op.</summary>
    ScriptOp,
    /// <summary>A script's done flag, set when it ends.</summary>
    ScriptDone,
    /// <summary>A script's battle flag, set by winning its battle.</summary>
    ScriptBattle,
    /// <summary>
    /// Something that gives a key item, and so sets its held flag and its region flag: a script's
    /// AddItem op (<see cref="FlagUse.At"/> is the node), a container's item or loot, a fought monster
    /// party's loot, or a shop's stock. <see cref="FlagUse.By"/> is the giver.
    /// </summary>
    ItemGiven,
    /// <summary>
    /// A script's TakeItem op on a key item, which clears its held flag once the party has none. The
    /// region flag stays set.
    /// </summary>
    ItemTaken,
    /// <summary>
    /// A key item's own flags: its held flag and its region flag. <see cref="FlagUse.By"/> is the item
    /// record, which sets them when the party gains it.
    /// </summary>
    KeyItem,
    /// <summary>
    /// A script's AddMember op on a recruitable member, which sets their joined flag and clears their
    /// left flag. <see cref="FlagUse.At"/> is the node.
    /// </summary>
    MemberAdded,
    /// <summary>
    /// A script's RemoveMember op on a recruitable member, which clears their joined flag and sets their
    /// left flag. <see cref="FlagUse.At"/> is the node.
    /// </summary>
    MemberRemoved,
    /// <summary>
    /// A recruitable member's own flags: joined, left and died. <see cref="FlagUse.By"/> is the entity
    /// record, which the engine keeps them in step with.
    /// </summary>
    Member,
    /// <summary>An input of a derived flag's formula. <see cref="FlagUse.By"/> is the derived flag, which reads it.</summary>
    DerivedInput,
    /// <summary>
    /// A flag's journal entry. <see cref="FlagUse.By"/> is the entry, which appears when the flag turns
    /// on, so the entry reads the flag.
    /// </summary>
    JournalAdds,
    /// <summary>A journal entry's flag, which the journal lists it by. <see cref="FlagUse.By"/> is the entry, which reads it.</summary>
    JournalListed,
    /// <summary>A map object's visibility gate. <see cref="FlagUse.By"/> is the object (<c>aidyn:voxel/…</c>), which reads the flag.</summary>
    ObjectVisible,
    /// <summary>A map object's enabled gate: whether it works, or for a monster party whether it's there.</summary>
    ObjectEnabled,
    /// <summary>What a container or trigger writes when it's used.</summary>
    ObjectUsed,
    /// <summary>A monster party without a dialogue: it's gone while the flag is set, and beating it sets it.</summary>
    PartyBeaten,
    /// <summary>A monster party with a dialogue: talking to it sets the flag.</summary>
    PartyTalkedTo,
    /// <summary>A monster party's battle flag, which winning the battle its dialogue starts sets.</summary>
    PartyFought,
    /// <summary>A container's opened flag: it's empty while set, and opening it sets it.</summary>
    ContainerOpened,
    /// <summary>A container's exploded flag: it's destroyed while set, and its trap going off sets it.</summary>
    ContainerExploded,
    /// <summary>A locked teleporter's lock flag, which unlocks it while set.</summary>
    DoorLocked,
    /// <summary>A locked teleporter's picked flag, which picking the lock sets.</summary>
    DoorUnlocked,
    /// <summary>A trapped teleporter's trap flag, which disarms it while set.</summary>
    TrapArmed,
    /// <summary>A trapped teleporter's disarm flag, which disarming it sets.</summary>
    TrapDisarmed,
    /// <summary>A secret door's flag: it works while set, and finding it sets it.</summary>
    SecretFound,
    /// <summary>A SetFlag trigger.</summary>
    TriggerSets,
    /// <summary>
    /// A world map region's flag, which entering a chunk of the map set (<see cref="FlagUse.By"/>) in the region sets
    /// (<see cref="ChunkEntry.Region"/>), where the sky is drawn and the set has no minimap.
    /// </summary>
    ChunkEntered,
    /// <summary>The state flags record: the engine writes the flags it keeps its state in, and reads the random skill check option.</summary>
    Engine,
    /// <summary>
    /// Not a use of a flag, but an edge of the graph: from a map object to the script it starts, or
    /// from a script to the one its RandomLine op takes a line from (<see cref="ScriptStarter"/>).
    /// Its access is <see cref="FlagAccess.Set"/>.
    /// </summary>
    StartsScript,
}

/// <summary>One use of a flag.</summary>
/// <param name="Flag">The flag used.</param>
/// <param name="By">The record that uses it, which the role explains.</param>
/// <param name="Access">Whether the record reads the flag, sets it or clears it.</param>
/// <param name="Role">Where the use comes from.</param>
/// <param name="At">The script node, for <see cref="FlagRole.ScriptOp"/>. Null for other roles.</param>
/// <param name="Inverted">For an object gate: whether it holds while the flag is clear.</param>
public sealed record FlagUse(RecordId Flag, RecordId By, FlagAccess Access, FlagRole Role, int? At = null, bool Inverted = false);

/// <summary>
/// What starts a script: a dialogue object or a monster party (<c>aidyn:voxel/…</c>), or another
/// script, whose RandomLine op at node <see cref="At"/> takes a line from it.
/// </summary>
public sealed record ScriptStarter(RecordId Script, RecordId By, int? At = null);

/// <summary>
/// An edge of the atlas graph, pointing the way things happen: from a setter to the flag it sets,
/// from a flag to what reads it, and from what starts a script to the script.
/// </summary>
public sealed record QuestEdge(RecordId From, RecordId To, FlagRole Role, FlagAccess Access);

/// <summary>
/// The quest atlas over plugin records: how each record uses each event flag, the atlas's
/// upstream and downstream graph, and a search over script text. Built once over a
/// <see cref="PluginSet"/>, like <see cref="PluginCatalog"/>.
/// </summary>
/// <remarks>
/// A new kind of record that uses flags is one more loop in <see cref="Build"/>.
/// </remarks>
public sealed class QuestIndex
{
    private readonly ILookup<RecordId, FlagUse> _byFlag;
    private readonly ILookup<RecordId, FlagUse> _byUser;
    private readonly ILookup<RecordId, QuestEdge> _into;
    private readonly ILookup<RecordId, QuestEdge> _outOf;
    private readonly List<(RecordId Script, int Node, string Text)> _text;
    private readonly ILookup<RecordId, ScriptStarter> _startersOf;
    private readonly ILookup<RecordId, ScriptStarter> _startedBy;

    private QuestIndex(List<FlagUse> uses, List<ScriptStarter> starters, List<(RecordId, int, string)> text)
    {
        _text = text;
        _byFlag = uses.OrderBy(u => u, Comparer<FlagUse>.Create(CompareByUser)).ToLookup(u => u.Flag);
        _byUser = uses.OrderBy(u => u, Comparer<FlagUse>.Create(CompareByFlag)).ToLookup(u => u.By);
        var byStarter = Comparer<ScriptStarter>.Create((a, b) => Compare(a.By, b.By) is var c and not 0 ? c : Nullable.Compare(a.At, b.At));
        _startersOf = starters.OrderBy(s => s, byStarter).ToLookup(s => s.Script);
        _startedBy = starters.OrderBy(s => s.Script, Comparer<RecordId>.Create(Compare)).ThenBy(s => s.At).ToLookup(s => s.By);
        var edges = uses.Select(Edge)
            .Concat(starters.Select(s => new QuestEdge(s.By, s.Script, FlagRole.StartsScript, FlagAccess.Set)))
            .Distinct().ToList();
        _into = edges.OrderBy(e => e, Comparer<QuestEdge>.Create((a, b) => CompareEdges(a, b, a.From, b.From))).ToLookup(e => e.To);
        _outOf = edges.OrderBy(e => e, Comparer<QuestEdge>.Create((a, b) => CompareEdges(a, b, a.To, b.To))).ToLookup(e => e.From);
    }

    /// <summary>Reads every script, flag, journal entry, map and the state flags. The base plugin takes a second or two.</summary>
    public static QuestIndex Build(PluginSet plugins)
    {
        var uses = new List<FlagUse>();
        var starters = new List<ScriptStarter>();
        var text = new List<(RecordId, int, string)>();
        var battles = new HashSet<RecordId>();
        var keyItems = plugins.All<ItemRecord>(RecordTypes.Item)
            .Where(i => i.KeyItem is not null)
            .ToDictionary(i => i.Id, i => i.KeyItem!);
        var loot = plugins.All<LootRecord>(RecordTypes.Loot).ToDictionary(l => l.Id);
        var members = plugins.All<EntityRecord>(RecordTypes.Entity)
            .Where(e => e.Member is not null)
            .ToDictionary(e => e.Id, e => e.Member!);

        // The key items that something gives. A loot field names either a loot table, which gives
        // whatever its drops name, or a single item.
        IEnumerable<KeyItemFlags> KeyItemsIn(params RecordId?[] given) => given
            .OfType<RecordId>()
            .SelectMany(i => i.Type == RecordTypes.Loot && loot.TryGetValue(i, out var table)
                ? table.Drops.Select(d => d.Item).OfType<RecordId>()
                : [i])
            .Where(keyItems.ContainsKey)
            .Select(i => keyItems[i])
            .Distinct();

        void GiveKeyItems(IEnumerable<KeyItemFlags> keys, RecordId giver, int? at = null)
        {
            foreach (var key in keys)
            {
                Use(key.Held, giver, FlagAccess.Set, FlagRole.ItemGiven, at: at);
                Use(key.Region, giver, FlagAccess.Set, FlagRole.ItemGiven, at: at);
            }
        }

        foreach (var script in plugins.All<DialogueRecord>(RecordTypes.Dialogue))
        {
            foreach (var node in script.Nodes)
            {
                if (node.Text.Length > 0)
                    text.Add((script.Id, node.Index, node.Text));
                foreach (var op in node.Ops ?? [])
                {
                    if (op.Target is { Type: RecordTypes.Flag } flag && ScriptAccess(op.Command) is { } access)
                        uses.Add(new FlagUse(flag, script.Id, access, FlagRole.ScriptOp, node.Index));
                    else if (op is { Command: DialogueCommand.RandomLine, Target: { } lines })
                        starters.Add(new ScriptStarter(lines, script.Id, node.Index));
                    else if (op.Command == DialogueCommand.Battle)
                        battles.Add(script.Id);
                    else if (op is { Command: DialogueCommand.AddItem, Target: { } given } && keyItems.TryGetValue(given, out var key))
                        GiveKeyItems([key], script.Id, node.Index);
                    else if (op is { Command: DialogueCommand.TakeItem, Target: { } taken } && keyItems.TryGetValue(taken, out var lost))
                        Use(lost.Held, script.Id, FlagAccess.Clear, FlagRole.ItemTaken, at: node.Index);
                    else if (op is { Command: DialogueCommand.AddMember or DialogueCommand.RemoveMember, Target: { } member }
                        && members.TryGetValue(member, out var flags))
                    {
                        var joins = op.Command == DialogueCommand.AddMember;
                        var role = joins ? FlagRole.MemberAdded : FlagRole.MemberRemoved;
                        Use(flags.Joined, script.Id, joins ? FlagAccess.Set : FlagAccess.Clear, role, at: node.Index);
                        Use(flags.Left, script.Id, joins ? FlagAccess.Clear : FlagAccess.Set, role, at: node.Index);
                    }
                }
            }
            if (script.DoneFlag is { } done)
                uses.Add(new FlagUse(done, script.Id, FlagAccess.Set, FlagRole.ScriptDone));
            if (script.BattleFlag is { } battle)
                uses.Add(new FlagUse(battle, script.Id, FlagAccess.Set, FlagRole.ScriptBattle));
        }

        foreach (var flag in plugins.All<FlagRecord>(RecordTypes.Flag))
        {
            foreach (var input in flag.Formula?.Inputs ?? [])
                uses.Add(new FlagUse(input, flag.Id, FlagAccess.Read, FlagRole.DerivedInput));
            if (flag.Journal is { } entry)
                uses.Add(new FlagUse(flag.Id, entry, FlagAccess.Read, FlagRole.JournalAdds));
        }

        foreach (var entry in plugins.All<JournalRecord>(RecordTypes.Journal))
            uses.Add(new FlagUse(entry.Flag, entry.Id, FlagAccess.Read, FlagRole.JournalListed));

        foreach (var map in plugins.All<MapRecord>(RecordTypes.Map))
        {
            foreach (var mapObject in map.Objects)
                AddObject(MapRecord.VoxelId(map.Id, mapObject.Index), mapObject);
        }

        // Flags 0 and 1 are constants, not quest state, so neither reads nor writes of them count.
        void Use(RecordId? flag, RecordId by, FlagAccess access, FlagRole role, bool inverted = false, int? at = null)
        {
            if (flag is { } f && !IsConstant(f))
                uses.Add(new FlagUse(f, by, access, role, at, inverted));
        }

        void AddObject(RecordId voxel, MapObject o)
        {
            if (o.Visible is { } visible)
                Use(visible.Flag, voxel, FlagAccess.Read, FlagRole.ObjectVisible, visible.Invert);
            if (o.Enabled is { } enabled)
                Use(enabled.Flag, voxel, FlagAccess.Read, FlagRole.ObjectEnabled, enabled.Invert);
            var used = o.Used is { Clear: true } ? FlagAccess.Clear : FlagAccess.Set;
            switch (o)
            {
                case ContainerObject c:
                    Use(c.Opened, voxel, FlagAccess.Read, FlagRole.ContainerOpened);
                    Use(c.Opened, voxel, FlagAccess.Set, FlagRole.ContainerOpened);
                    Use(c.Exploded, voxel, FlagAccess.Read, FlagRole.ContainerExploded);
                    Use(c.Exploded, voxel, FlagAccess.Set, FlagRole.ContainerExploded);
                    Use(c.Used?.Flag, voxel, used, FlagRole.ObjectUsed);
                    GiveKeyItems(KeyItemsIn(c.Item, c.Loot), voxel);
                    break;
                case TriggerObject t:
                    if (t.Kind == TriggerKind.SetFlag)
                        Use(t.Flag, voxel, FlagAccess.Set, FlagRole.TriggerSets);
                    Use(t.Used?.Flag, voxel, used, FlagRole.ObjectUsed);
                    break;
                // A silent party does nothing when the player uses it.
                case MonsterPartyObject m when m.Dialogue is { } dialogue:
                    if (!m.Flags.HasFlag(PartyFlags.Silent))
                    {
                        starters.Add(new ScriptStarter(dialogue, voxel));
                        Use(m.Used?.Flag, voxel, FlagAccess.Set, FlagRole.PartyTalkedTo);
                        // The dialogue decides whether there's a fight, and only a fought party drops its loot.
                        if (battles.Contains(dialogue))
                        {
                            Use(m.BattleFlag, voxel, FlagAccess.Set, FlagRole.PartyFought);
                            GiveKeyItems(KeyItemsIn(m.Loot), voxel);
                        }
                    }
                    break;
                case MonsterPartyObject m:
                    Use(m.Used?.Flag, voxel, FlagAccess.Read, FlagRole.PartyBeaten);
                    Use(m.Used?.Flag, voxel, FlagAccess.Set, FlagRole.PartyBeaten);
                    GiveKeyItems(KeyItemsIn(m.Loot), voxel);
                    break;
                case TeleporterObject t:
                    if (t.LockLevel != 0)
                    {
                        Use(t.LockFlag, voxel, FlagAccess.Read, FlagRole.DoorLocked);
                        Use(t.PickedFlag, voxel, FlagAccess.Set, FlagRole.DoorUnlocked);
                    }
                    if (t.TrapLevel != 0)
                    {
                        Use(t.TrapFlag, voxel, FlagAccess.Read, FlagRole.TrapArmed);
                        Use(t.DisarmFlag, voxel, FlagAccess.Set, FlagRole.TrapDisarmed);
                    }
                    Use(t.SecretFlag, voxel, FlagAccess.Read, FlagRole.SecretFound);
                    Use(t.SecretFlag, voxel, FlagAccess.Set, FlagRole.SecretFound);
                    break;
                case DialogueObject d:
                    starters.Add(new ScriptStarter(d.Script, voxel));
                    break;
            }
        }

        // The importer has already left out what the game doesn't stock.
        foreach (var shop in plugins.All<ShopRecord>(RecordTypes.Shop))
            GiveKeyItems(KeyItemsIn([.. shop.Stock.Select(s => (RecordId?)s.Item)]), shop.Id);

        foreach (var (entity, flags) in members)
        {
            Use(flags.Joined, entity, FlagAccess.Set, FlagRole.Member);
            Use(flags.Left, entity, FlagAccess.Set, FlagRole.Member);
            Use(flags.Died, entity, FlagAccess.Set, FlagRole.Member);
        }

        foreach (var (item, key) in keyItems)
        {
            Use(key.Held, item, FlagAccess.Set, FlagRole.KeyItem);
            Use(key.Region, item, FlagAccess.Set, FlagRole.KeyItem);
        }

        // One use a map set and region, however many of its chunks find the region.
        foreach (var set in plugins.All<MapSetRecord>(RecordTypes.MapSet))
            foreach (var region in set.Chunks.Select(c => c.RegionFound(plugins, set.Id)).OfType<RecordId>().Distinct())
                Use(region, set.Id, FlagAccess.Set, FlagRole.ChunkEntered);

        foreach (var state in plugins.All<StateFlagsRecord>(RecordTypes.StateFlags))
        {
            IReadOnlyList<RecordId>[] runs =
                [state.MoonPhase, state.TimeOfDay, state.DayOfSeason, state.Season, state.DayOfWeek, state.Week, state.Rain, state.Terrain];
            foreach (var flag in runs.SelectMany(run => run).Append(state.NewJournalEntry))
                uses.Add(new FlagUse(flag, state.Id, FlagAccess.Set, FlagRole.Engine));
            if (state.RandomSkillChecks is { } option)
                uses.Add(new FlagUse(option, state.Id, FlagAccess.Read, FlagRole.Engine));
        }

        return new QuestIndex(uses, starters, text);
    }

    /// <summary>Every use of a flag, by role, then by the record that uses it, then by script node.</summary>
    public IReadOnlyList<FlagUse> UsesOf(RecordId flag) => [.. _byFlag[flag]];

    /// <summary>
    /// Every flag a script, flag, journal entry, map object or the state flags record uses, by role,
    /// then by script node, then by flag.
    /// </summary>
    public IReadOnlyList<FlagUse> UsesBy(RecordId record) => [.. _byUser[record]];

    /// <summary>
    /// The edges into <paramref name="key"/>, by where they come from: for a flag, its setters and,
    /// if it's derived, its inputs; for a script, the flags it checks and what starts it; for a map
    /// object, the flags that gate it.
    /// </summary>
    public IReadOnlyList<QuestEdge> Upstream(RecordId key) => [.. _into[key]];

    /// <summary>
    /// The edges out of <paramref name="key"/>, by where they lead: for a flag, what reads it; for a
    /// script, the flags it sets or clears, its done and battle flags included, and the scripts it
    /// takes random lines from; for a map object, the flags it writes and the script it starts.
    /// </summary>
    public IReadOnlyList<QuestEdge> Downstream(RecordId key) => [.. _outOf[key]];

    /// <summary>What starts a script, by the record that does, then by script node.</summary>
    public IReadOnlyList<ScriptStarter> StartersOf(RecordId script) => [.. _startersOf[script]];

    /// <summary>The scripts a map object starts, or that a script's RandomLine ops take lines from, by script, then by node.</summary>
    public IReadOnlyList<ScriptStarter> StartedBy(RecordId record) => [.. _startedBy[record]];

    /// <summary>The nodes whose text holds <paramref name="text"/>, ignoring case, in script order and then node order.</summary>
    public IReadOnlyList<(RecordId Script, int Node)> FindText(string text) =>
        text.Length == 0 ? [] : [.. _text.Where(t => t.Text.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(t => (t.Script, t.Node))];

    /// <summary>Flag 0 always reads as clear and flag 1 as set, so a gate on either is a constant, not quest state.</summary>
    private static bool IsConstant(RecordId flag) =>
        flag.Namespace == RecordId.BaseNamespace && flag.Type == RecordTypes.Flag && flag.Key is "0" or "1";

    private static FlagAccess? ScriptAccess(DialogueCommand command) => command switch
    {
        DialogueCommand.CheckFlag => FlagAccess.Read,
        DialogueCommand.SetFlag => FlagAccess.Set,
        DialogueCommand.ClearFlag => FlagAccess.Clear,
        _ => null,
    };

    private static QuestEdge Edge(FlagUse use) => use.Access == FlagAccess.Read
        ? new QuestEdge(use.Flag, use.By, use.Role, use.Access)
        : new QuestEdge(use.By, use.Flag, use.Role, use.Access);

    private static int CompareByUser(FlagUse a, FlagUse b)
    {
        var c = a.Role.CompareTo(b.Role);
        if (c == 0)
            c = Compare(a.By, b.By);
        if (c == 0)
            c = Nullable.Compare(a.At, b.At);
        return c != 0 ? c : a.Access.CompareTo(b.Access);
    }

    private static int CompareByFlag(FlagUse a, FlagUse b)
    {
        var c = a.Role.CompareTo(b.Role);
        if (c == 0)
            c = Nullable.Compare(a.At, b.At);
        if (c == 0)
            c = Compare(a.Flag, b.Flag);
        return c != 0 ? c : a.Access.CompareTo(b.Access);
    }

    private static int CompareEdges(QuestEdge a, QuestEdge b, RecordId otherA, RecordId otherB)
    {
        var c = Compare(otherA, otherB);
        if (c == 0)
            c = a.Role.CompareTo(b.Role);
        return c != 0 ? c : a.Access.CompareTo(b.Access);
    }

    /// <summary>By type, then namespace, then key, with shorter keys first so that 47 comes before 5423.</summary>
    private static int Compare(RecordId a, RecordId b)
    {
        var c = string.CompareOrdinal(a.Type, b.Type);
        if (c == 0)
            c = string.CompareOrdinal(a.Namespace, b.Namespace);
        if (c == 0)
            c = a.Key.Length.CompareTo(b.Key.Length);
        return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
    }
}
