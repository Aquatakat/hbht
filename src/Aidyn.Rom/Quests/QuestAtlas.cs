using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Quests;

public enum FlagAccess
{
    Read,
    Set,
    Clear,
}

/// <summary>How something uses a flag.</summary>
public enum FlagRole
{
    // Dialogue.
    /// <summary>A SetFlag or ClearFlag effect.</summary>
    DialogueEffect,
    /// <summary>A CheckFlag condition in an If node.</summary>
    DialogueCheck,
    /// <summary>The script's done flag, set when a cut-scene, backdrop or help script finishes.</summary>
    ScriptDone,
    /// <summary>
    /// The encounter flag of the battle the script starts (<see cref="EngineTables.ScriptBattleFlags"/>).
    /// Losing the Goblin Ambush sets it.
    /// </summary>
    ScriptBattle,
    /// <summary>An AddItem effect, a container's item or loot, or a shop's stock, that gives a key item and so sets its flags.</summary>
    ItemGiven,
    /// <summary>A TakeItem effect on a key item, which clears its flag once the party has none.</summary>
    ItemTaken,
    /// <summary>An AddMember effect: sets the member's joined flag and clears their left flag.</summary>
    MemberAdded,
    /// <summary>A RemoveMember effect: clears the member's joined flag and sets their left flag.</summary>
    MemberRemoved,
    /// <summary>A battle with a boss in its first four enemy slots, which sets the boss's flag when won.</summary>
    BossBeaten,

    // Map objects.
    /// <summary>Flag C: whether the object is there.</summary>
    ObjectVisible,
    /// <summary>Flag A: whether it works, or for a monster party whether it's there.</summary>
    ObjectEnabled,
    /// <summary>Flag B, written when the object is used.</summary>
    ObjectUsed,
    /// <summary>A monster party's flag B: set when it's beaten, and it's gone while set.</summary>
    PartyBeaten,
    /// <summary>A monster party's flag B when it has a dialogue: set when talked to.</summary>
    PartyTalkedTo,
    /// <summary>
    /// A monster party's flag C when its dialogue can start a battle: that battle gets flag C as its
    /// encounter flag, and winning sets it.
    /// </summary>
    PartyFought,
    ContainerOpened,
    ContainerExploded,
    DoorLocked,
    DoorUnlocked,
    TrapArmed,
    TrapDisarmed,
    SecretFound,
    /// <summary>A SetFlag trigger.</summary>
    TriggerSets,

    // Other.
    /// <summary>An input of a derived flag.</summary>
    NetworkInput,
    /// <summary>Written by engine code (see <see cref="EngineFlags"/> and <see cref="EngineTables"/>).</summary>
    Engine,
    /// <summary>Read by engine code.</summary>
    EngineCheck,
}

/// <summary>Where a flag reference is.</summary>
public abstract record AtlasSource;

/// <param name="Node">The node, or null for the script's header.</param>
public sealed record ScriptSource(int Script, int? Node) : AtlasSource;

/// <param name="Borg9">The Borg9 holding the object.</param>
public sealed record ObjectSource(int Borg9, int Index) : AtlasSource;

/// <summary>A shop in the DB, which the Shop effect opens when talking to its shopkeeper.</summary>
public sealed record ShopSource(int Shop, ItemId Shopkeeper) : AtlasSource;

/// <summary>A derived flag that reads this one.</summary>
public sealed record FlagSource(ushort Flag) : AtlasSource;

public sealed record EngineSource(string Description) : AtlasSource;

/// <param name="Ignored">
/// The game never performs this write: it names flag 0 or 1 or a derived flag, which
/// <c>setEventFlag</c> refuses, or it's a node's second effect.
/// </param>
public sealed record FlagReference(ushort Flag, FlagAccess Access, FlagRole Role, AtlasSource Source, bool Ignored = false);

/// <summary>A script and where the game can start it from.</summary>
/// <param name="StartedBy">Map objects and scripts (through RandomLine) that start it. Empty if only code does, or nothing.</param>
public sealed record AtlasScript(int Borg, Borg13Script Script, IReadOnlyList<AtlasSource> StartedBy);

/// <param name="Chunks">The map chunks that use this Borg9. Empty for the few loaded some other way.</param>
public sealed record AtlasObject(int Borg9, VoxelObject Object, IReadOnlyList<MapChunk> Chunks);

/// <summary>
/// Roadmap 1E: every event flag with what sets it, what reads it, and its journal entry, from the
/// dialogue scripts, map objects, the flag network and the engine's own flags.
/// </summary>
public sealed class QuestAtlas
{
    public required GameStateFunnelTable Funnel { get; init; }
    public required IReadOnlyList<FlaggedText> Journal { get; init; }
    public required IReadOnlyDictionary<int, AtlasScript> Scripts { get; init; }
    public required IReadOnlyList<AtlasObject> Objects { get; init; }
    public required IReadOnlyList<FlagReference> References { get; init; }

    /// <summary>The names of entities, dialogue entities and items, for showing operands.</summary>
    public required IReadOnlyDictionary<ItemId, string> Names { get; init; }

    /// <summary>What engine code means by a flag, such as "Brenna is in the party", where it says.</summary>
    public required IReadOnlyDictionary<ushort, string> Labels { get; init; }

    /// <summary>Journal entries by their flag.</summary>
    public ILookup<ushort, int> JournalByFlag => Journal.Select((e, i) => (e.Flag, i)).ToLookup(e => e.Flag, e => e.i);

    public bool IsBitFlag(int flag) => flag < Funnel.BitFlagCount;

    public static QuestAtlas Build(RomImage rom)
    {
        var archive = BorgArchive.Open(rom);
        var db = GameDb.Read(rom);
        var tables = EngineTables.Read(rom);
        var funnel = db.GameStateFunnel;
        var refs = new List<FlagReference>();
        var names = db.Entities.Select(e => (e.Id, e.Name))
            .Concat(db.DialogEntities.Select(e => (e.Id, e.Name)))
            .Concat(db.Weapons.Select(e => (e.Id, e.Name)))
            .Concat(db.Armor.Select(e => (e.Id, e.Name)))
            .Concat(db.Gear.Select(e => (e.Id, e.Name)))
            .Concat(db.Spells.Select(e => (e.Id, e.Name)))
            .Concat(db.Loot.Select(e => (e.Id, e.Name)))
            .DistinctBy(e => e.Id)
            .ToDictionary(e => e.Id, e => e.Name);
        string Name(ItemId id) => names.TryGetValue(id, out var n) ? $"{n} ({id})" : id.ToString();

        var keyItems = tables.KeyItems.ToDictionary(k => k.Item);
        var members = tables.Party.ToDictionary(p => p.Member);
        var loot = db.Loot.ToDictionary(l => l.Id);
        // Key items that containers and monster parties give. Their loot fields name either a loot
        // table or a single item.
        IEnumerable<KeyItemFlags> KeyItemsIn(params ItemId[] given) => given
            .SelectMany(i => i.Type == DbType.Loot && loot.TryGetValue(i, out var l) ? l.Drops.Select(d => d.Item) : [i])
            .Where(keyItems.ContainsKey)
            .Select(i => keyItems[i]);

        void Write(int flag, FlagAccess access, FlagRole role, AtlasSource source, bool secondEffect = false) =>
            refs.Add(new((ushort)flag, access, role, source, secondEffect || flag < 2 || flag >= funnel.BitFlagCount));
        // Map objects leave flag fields they don't use at 0.
        void WriteField(int flag, FlagAccess access, FlagRole role, AtlasSource source, bool secondEffect = false)
        {
            if (flag >= 2)
                Write(flag, access, role, source, secondEffect);
        }
        void Read(int flag, FlagRole role, AtlasSource source)
        {
            if (flag >= 2)
                refs.Add(new((ushort)flag, FlagAccess.Read, role, source));
        }
        void Gate(FlagGate gate, FlagRole role, AtlasSource source)
        {
            if (gate.Constant is null)
                Read(gate.Flag, role, source);
        }

        // Scripts.
        var scripts = new Dictionary<int, Borg13Script>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Dialogue))
            scripts[listing.Index] = Borg13Script.Parse(archive.Read(listing.Index));
        var startedBy = scripts.Keys.ToDictionary(k => k, _ => new List<AtlasSource>());

        foreach (var (index, script) in scripts)
        {
            if (script.StartMode is Borg13StartMode.Cutscene or Borg13StartMode.Backdrop or Borg13StartMode.Help)
                Write(script.DoneFlag, FlagAccess.Set, FlagRole.ScriptDone, new ScriptSource(index, null));
            if (tables.ScriptBattleFlags.TryGetValue(index, out var battleFlag))
                Write(battleFlag, FlagAccess.Set, FlagRole.ScriptBattle, new ScriptSource(index, null));

            var fights = script.Nodes.Any(n => n.Ops.Any(o => o.Opcode == Borg13Opcode.Battle));
            foreach (var node in script.Nodes)
            {
                var source = new ScriptSource(index, node.Index);
                if (node.Kind == Borg13NodeKind.If && node.Condition is { Opcode: Borg13Opcode.CheckFlag } check)
                    Read(check.Value, FlagRole.DialogueCheck, source);

                // Enemies accumulate across nodes, so this assumes the script adds them in node order.
                foreach (var op in node.Ops.Where(o => o.Opcode == Borg13Opcode.AddEnemy))
                {
                    if (fights && EngineFlags.BossFlags.TryGetValue(new ItemId(op.Value), out var boss))
                        Write(boss, FlagAccess.Set, FlagRole.BossBeaten, source);
                }

                var effects = node.Ops.Where(o => o.Opcode.IsEffect()).ToList();
                for (var i = 0; i < effects.Count; i++)
                {
                    var op = effects[i];
                    var second = i > 0;
                    var id = new ItemId(op.Value);
                    switch (op.Opcode)
                    {
                        case Borg13Opcode.SetFlag or Borg13Opcode.ClearFlag:
                            Write(op.Value, op.Opcode == Borg13Opcode.SetFlag ? FlagAccess.Set : FlagAccess.Clear,
                                FlagRole.DialogueEffect, source, second);
                            break;
                        case Borg13Opcode.RandomLine when startedBy.TryGetValue(op.Value, out var by):
                            by.Add(source);
                            break;
                        case Borg13Opcode.AddItem when keyItems.TryGetValue(id, out var key):
                            GiveKeyItem(key, source, second);
                            break;
                        case Borg13Opcode.TakeItem when keyItems.TryGetValue(id, out var key):
                            Write(key.Held, FlagAccess.Clear, FlagRole.ItemTaken, source, second);
                            break;
                        case Borg13Opcode.AddMember when members.TryGetValue(id, out var member):
                            Write(member.Joined, FlagAccess.Set, FlagRole.MemberAdded, source, second);
                            WriteField(member.Left, FlagAccess.Clear, FlagRole.MemberAdded, source, second);
                            break;
                        case Borg13Opcode.RemoveMember when members.TryGetValue(id, out var member):
                            Write(member.Joined, FlagAccess.Clear, FlagRole.MemberRemoved, source, second);
                            WriteField(member.Left, FlagAccess.Set, FlagRole.MemberRemoved, source, second);
                            break;
                    }
                }
            }
        }

        void GiveKeyItem(KeyItemFlags key, AtlasSource source, bool secondEffect = false)
        {
            Write(key.Held, FlagAccess.Set, FlagRole.ItemGiven, source, secondEffect);
            WriteField(key.Region, FlagAccess.Set, FlagRole.ItemGiven, source, secondEffect);
        }

        // Map objects.
        var chunksByBorg9 = MapChunks.Read(rom).ToLookup(c => c.Collision);
        var objects = new List<AtlasObject>();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.MapChunk))
        {
            foreach (var o in Borg9Voxels.Read(archive.Read(listing.Index)))
            {
                objects.Add(new AtlasObject(listing.Index, o, chunksByBorg9[listing.Index].ToList()));
                AddObject(o, new ObjectSource(listing.Index, o.Index));
            }
        }

        void AddObject(VoxelObject o, ObjectSource source)
        {
            var used = o.Flags.HasFlag(VoxelFlags.ClearFlagBOnUse) ? FlagAccess.Clear : FlagAccess.Set;
            switch (o.Data)
            {
                case ContainerData c:
                    // Herb, spice and gem sources and custom-model loot (kinds 4-7) are gated
                    // like triggers. Chests, sacks, crates and barrels show until they're
                    // opened or destroyed.
                    if (c.Kind is >= 4 and <= 7)
                    {
                        Gate(o.Visible, FlagRole.ObjectVisible, source);
                        Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    }
                    Read(c.OpenFlag, FlagRole.ContainerOpened, source);
                    Read(c.ExplodeFlag, FlagRole.ContainerExploded, source);
                    WriteField(c.OpenFlag, FlagAccess.Set, FlagRole.ContainerOpened, source);
                    WriteField(c.ExplodeFlag, FlagAccess.Set, FlagRole.ContainerExploded, source);
                    WriteField(o.FlagB, used, FlagRole.ObjectUsed, source);
                    foreach (var key in KeyItemsIn(c.Item, c.Loot))
                        GiveKeyItem(key, source);
                    break;
                case MonsterPartyData m:
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    var fought = true;
                    if (m.Dialogue != 0)
                    {
                        WriteField(o.FlagB, FlagAccess.Set, FlagRole.PartyTalkedTo, source);
                        if (startedBy.TryGetValue(m.Dialogue, out var by))
                            by.Add(source);
                        // The dialogue decides whether there's a fight. If there is, flag C is its encounter flag.
                        fought = scripts.TryGetValue(m.Dialogue, out var talk)
                            && talk.Nodes.Any(n => n.Ops.Any(op => op.Opcode == Borg13Opcode.Battle));
                        if (fought)
                            WriteField(o.FlagC, FlagAccess.Set, FlagRole.PartyFought, source);
                    }
                    else
                    {
                        Read(o.FlagB, FlagRole.PartyBeaten, source);
                        WriteField(o.FlagB, FlagAccess.Set, FlagRole.PartyBeaten, source);
                    }
                    if (fought)
                    {
                        foreach (var boss in BossesInFirstSlots(m))
                            Write(boss, FlagAccess.Set, FlagRole.BossBeaten, source);
                        foreach (var key in KeyItemsIn(m.Loot))
                            GiveKeyItem(key, source);
                    }
                    break;
                case TeleporterData t:
                    Gate(o.Visible, FlagRole.ObjectVisible, source);
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (t.LockLevel != 0)
                    {
                        Read(t.LockFlag, FlagRole.DoorLocked, source);
                        WriteField(t.UnlockFlag, FlagAccess.Set, FlagRole.DoorUnlocked, source);
                    }
                    Read(t.TrapFlag, FlagRole.TrapArmed, source);
                    WriteField(t.DisarmFlag, FlagAccess.Set, FlagRole.TrapDisarmed, source);
                    if (t.SecretLevel != 0)
                    {
                        Read(t.SecretFlag, FlagRole.SecretFound, source);
                        WriteField(t.SecretFlag, FlagAccess.Set, FlagRole.SecretFound, source);
                    }
                    break;
                case DialogueData d:
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (startedBy.TryGetValue(d.Script, out var started))
                        started.Add(source);
                    break;
                case TriggerData t:
                    Gate(o.Visible, FlagRole.ObjectVisible, source);
                    Gate(o.Enabled, FlagRole.ObjectEnabled, source);
                    if (t.Kind == TriggerKind.SetFlag)
                        Write(t.A, FlagAccess.Set, FlagRole.TriggerSets, source);
                    WriteField(o.FlagB, used, FlagRole.ObjectUsed, source);
                    break;
                default:
                    if (o.Type is not (VoxelType.SavePoint or VoxelType.Dialogue))
                        Gate(o.Visible, FlagRole.ObjectVisible, source);
                    break;
            }
        }

        // Shops. The game stocks a slot only when its third byte is nonzero, and the extra slots
        // whenever they name an item.
        for (var i = 0; i < db.Shops.Count; i++)
        {
            var shop = db.Shops[i];
            var stock = shop.Stock.Where(s => s.Unk2 != 0).Select(s => s.Item).Concat(shop.ExtraStock);
            foreach (var key in stock.Where(keyItems.ContainsKey))
                GiveKeyItem(keyItems[key], new ShopSource(i, shop.Shopkeeper));
        }

        // The flag network.
        for (var flag = funnel.BitFlagCount; flag < funnel.Flags.Count; flag++)
        {
            foreach (var input in Inputs(funnel, flag))
                Read(input, FlagRole.NetworkInput, new FlagSource((ushort)flag));
        }

        // Engine code.
        var labels = new Dictionary<ushort, string>();
        void Engine(ushort flag, FlagAccess access, string description, string? label = null)
        {
            if (access == FlagAccess.Read)
                Read(flag, FlagRole.EngineCheck, new EngineSource(description));
            else
                Write(flag, access, FlagRole.Engine, new EngineSource(description));
            if (label != null && flag >= 2)
                labels.TryAdd(flag, label);
        }

        foreach (var range in EngineFlags.Ranges)
        {
            for (var flag = range.First; flag <= range.Last; flag++)
                Engine(flag, FlagAccess.Set, range.Description);
        }
        var cinematics = Cinematics.Read(rom);
        var titles = db.CinematicTitles.ToDictionary(t => t.Flag, t => t.Text.Trim());
        for (var i = 0; i < cinematics.Count; i++)
        {
            var flag = cinematics[i].DoneFlag;
            var title = titles.TryGetValue(flag, out var t) ? $"the cinematic “{t}”" : $"cinematic {i}";
            Engine(flag, FlagAccess.Set, $"Set when {title} ends. It plays when {EngineFlags.CinematicStarts[i]}.", $"Saw {title}");
        }
        foreach (var (boss, flag) in EngineFlags.BossFlags)
            Engine(flag, FlagAccess.Set, $"Set when the party beats {Name(boss)}", $"Beat {names[boss]}");

        foreach (var m in tables.Party)
        {
            var who = names[m.Member];
            Engine(m.Joined, FlagAccess.Set, $"Set when {Name(m.Member)} joins, cleared when removed alive", $"{who} is in the party");
            if (m.Left != 0)
                Engine(m.Left, FlagAccess.Set, $"Set when {Name(m.Member)} is removed alive, cleared when they join", $"{who} left the party");
            Engine(m.Died, FlagAccess.Set, $"Set while {Name(m.Member)} is dead in the party", $"{who} is dead");
        }
        foreach (var k in tables.KeyItems)
        {
            Engine(k.Held, FlagAccess.Set, $"Set when the party gains {Name(k.Item)}, cleared when it has none",
                $"Has {names.GetValueOrDefault(k.Item, k.Item.ToString())}");
            Engine(k.Held, FlagAccess.Read, $"Restores {Name(k.Item)} to the inventory when a save loads");
            if (k.Region != 0)
                Engine(k.Region, FlagAccess.Set, $"Set when the party gains {Name(k.Item)}");
        }
        var chunkNames = MapChunks.Read(rom).Where(c => c.Set == MapSet.Overworld).ToDictionary(c => (c.Row, c.Column), c => c.Name);
        var firstRegion = tables.Regions.Min(r => r.Flag);
        foreach (var region in tables.Regions.GroupBy(r => r.Flag).OrderBy(g => g.Key))
        {
            var chunks = string.Join(", ", region.Select(r => chunkNames.GetValueOrDefault((r.Row, r.Column), $"{r.Row},{r.Column}")));
            Engine(region.Key, FlagAccess.Set, $"Set on entering Overworld {chunks}", $"World map region {region.Key - firstRegion + 1}");
        }
        foreach (var piece in tables.WorldMap)
            Engine(piece.Flag, FlagAccess.Read, $"Shows its piece of the world map (Borg8 {piece.Image})");
        var minimaps = Minimaps.Read(rom);
        foreach (var (images, place) in new[] { (minimaps.Ehud, "the Ehud underground"), (minimaps.RoogEntrance, "the Roog entrance"), (minimaps.Barrows, "the Barrows") })
        {
            foreach (var m in images)
                Engine(m.Flag, FlagAccess.Read, $"Chooses the minimap image of {place} (Borg8 {m.Image})");
        }
        foreach (var flag in tables.CampDialogueFlags)
            Engine(flag, FlagAccess.Read, "Marks a dialogue object as a camp scene: camping near it starts the dialogue while the flag holds");
        foreach (var title in db.CinematicTitles)
            Engine(title.Flag, FlagAccess.Read, $"Unlocks “{title.Text}” in the Theatre menu");
        foreach (var e in EngineFlags.Writes)
            Engine(e.Flag, FlagAccess.Set, e.Description, e.Description);
        foreach (var e in EngineFlags.Reads)
            Engine(e.Flag, FlagAccess.Read, e.Description, e.Description);

        startedBy[EngineFlags.HelpScript].Add(new EngineSource("The help menu"));

        return new QuestAtlas
        {
            Funnel = funnel,
            Journal = db.Journal,
            Scripts = scripts.ToDictionary(s => s.Key, s => new AtlasScript(s.Key, s.Value, startedBy[s.Key])),
            Objects = objects,
            References = refs,
            Names = names,
            Labels = labels,
        };
    }

    /// <summary>
    /// The bosses a monster party puts in the first four enemy slots, which are all
    /// <c>set_boss_flag</c> looks at. The slots take each entry's minimum count in order, then
    /// random extras, so an entry with no minimum can only land after every minimum.
    /// </summary>
    private static IEnumerable<ushort> BossesInFirstSlots(MonsterPartyData party)
    {
        var minimums = party.Enemies.Sum(e => e.Min);
        var slot = 0;
        foreach (var (enemy, min, _) in party.Enemies)
        {
            var first = min > 0 ? slot : minimums;
            if (first < 4 && EngineFlags.BossFlags.TryGetValue(enemy, out var flag))
                yield return flag;
            slot += min;
        }
    }

    /// <summary>A derived flag's inputs, without the unused ones.</summary>
    public static IEnumerable<ushort> Inputs(GameStateFunnelTable funnel, int flag)
    {
        var definition = funnel.Flags[flag];
        for (var i = 0; i < definition.InputCount; i++)
        {
            var input = funnel.Inputs[definition.InputStart + i];
            if (input != 0xFFFF)
                yield return input;
        }
    }
}
