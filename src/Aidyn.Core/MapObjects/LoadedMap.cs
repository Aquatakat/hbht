using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.MapObjects;

/// <summary>How a container's model shows (<c>replace_container_voxel</c>): as placed, opened, or destroyed.</summary>
public enum ContainerLook
{
    Closed,
    Opened,
    Destroyed,
}

/// <summary>
/// A teleporter as it stands while its chunk is loaded: its lock, trap and secret, which its flags clear
/// as the chunk loads (<c>TP_lock_secret_check</c>), and picking, disarming and finding clear until then.
/// </summary>
public sealed class Teleporter(TeleporterObject record)
{
    public TeleporterObject Record { get; } = record;

    /// <summary>The lock's level, or 0 once it's open.</summary>
    public int LockLevel { get; internal set; } = record.LockLevel;

    /// <summary>The trap's level, or 0 once it's disarmed or gone off.</summary>
    public int TrapLevel { get; internal set; } = record.TrapLevel;

    /// <summary>The skill a secret door needs before it's there, or 0 once found.</summary>
    public int SecretLevel { get; internal set; } = record.SecretLevel;
}

/// <summary>
/// A chunk's map objects as the zone engine holds them while the chunk is loaded (a <c>Borg9Data</c>):
/// which are active, its containers' and teleporters' locks and traps, and how containers' models show.
/// Loading a chunk activates its objects by the flags (<see cref="Activate"/>); what using them changes
/// lasts until the chunk is loaded again.
/// </summary>
public sealed class LoadedMap
{
    /// <summary>How far <see cref="Linked"/> follows links, as <c>GetVoxelFromObjectLink</c> does.</summary>
    public const int MaxLinks = 51;

    private readonly Dictionary<int, MapObject> _objects;
    private readonly HashSet<int> _active = [];
    private readonly Dictionary<int, Container> _containers = [];
    private readonly Dictionary<int, Teleporter> _teleporters = [];
    private readonly Dictionary<int, ContainerLook> _looks = [];

    private LoadedMap(MapRecord record, object key)
    {
        Record = record;
        Key = key;
        _objects = record.Objects.ToDictionary(o => o.Index);
    }

    public MapRecord Record { get; }

    /// <summary>What the runtime keys the chunk by, such as its collision's part in the world.</summary>
    public object Key { get; }

    /// <summary>
    /// The best Ranger's share of what herb, spice and gem sources give, rolled as the chunk's objects
    /// are activated (<c>set_ranger_float</c>).
    /// </summary>
    public float HarvestShare { get; private set; }

    public MapObject? this[int index] => _objects.GetValueOrDefault(index);

    public IEnumerable<MapObject> Objects => Record.Objects;

    /// <summary>Whether an object is active: there, and for one that can be used, not used up (<c>VOXEL_Active</c>).</summary>
    public bool IsActive(int index) => _active.Contains(index);

    internal void SetActive(int index, bool active)
    {
        if (active)
            _active.Add(index);
        else
            _active.Remove(index);
    }

    /// <summary>A container's lock and trap, or null if the object isn't one.</summary>
    public Container? Container(int index) => _containers.GetValueOrDefault(index);

    /// <summary>A teleporter's lock, trap and secret, or null if the object isn't one.</summary>
    public Teleporter? Teleporter(int index) => _teleporters.GetValueOrDefault(index);

    /// <summary>Which of its models a container's scene object shows.</summary>
    public ContainerLook Look(int sceneIndex) => _looks.GetValueOrDefault(sceneIndex);

    internal void SetLook(int sceneIndex, ContainerLook look) => _looks[sceneIndex] = look;

    /// <summary>
    /// The object of type <typeparamref name="T"/> an object works with, following links from object to
    /// object (<c>GetVoxelFromObjectLink</c>, checked in 1.0's code): a container's model, or the actor a
    /// trigger animates. An object with no link is its own. Null where the chain ends elsewhere, which the
    /// original crashes on.
    /// </summary>
    public T? Linked<T>(MapObject from) where T : MapObject
    {
        var o = from;
        for (var i = 0; i < MaxLinks && o.Link is { } link; i++)
        {
            if (this[Index(link)] is not { } next)
                return null;
            o = next;
            if (o is T found)
                return found;
        }
        return o as T;
    }

    /// <summary>The index in its map of the object a voxel ID names.</summary>
    public static int Index(RecordId voxel) => int.Parse(voxel.Key[(voxel.Key.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// A chunk's objects as it loads (<c>VoxelObjects_Activate</c>, the original's <c>gVoxelFuncs2</c>):
    /// each is active if it's there by its flags (<see cref="MapObjectPresence"/>) and not only for a
    /// console without the Expansion Pak, with these rules besides.
    /// <list type="bullet">
    /// <item>A chest, sack, crate or barrel is there until it's opened, or remembered as emptied, when its
    /// model shows it opened; or destroyed, when its model shows that. Its model is there either way.</item>
    /// <item>A herb, spice, gem or custom container needs its <see cref="MapObject.Visible"/> and
    /// <see cref="MapObject.Enabled"/> to hold, and not to be remembered as emptied, and its model is there
    /// only while it is.</item>
    /// <item>A teleporter's lock and trap are gone while their flags are set, and a secret door that
    /// needs a skill is there only once found, or if the party's search reaches its level.</item>
    /// </list>
    /// </summary>
    public static LoadedMap Activate(MapRecord record, object key, FlagNetwork flags, MapObjectMemory memory, Characters.Party party,
        IRandom random)
    {
        var map = new LoadedMap(record, key);
        bool IsSet(RecordId flag) => flags.TryHandle(flag, out var handle) && flags.Get(handle);
        map.HarvestShare = Containers.HarvestShare(party, random);

        foreach (var o in record.Objects)
        {
            if (o.Pak == PakVariant.Without)
                continue;
            var active = o switch
            {
                ContainerObject container => map.ActivateContainer(container, IsSet, memory),
                TeleporterObject teleporter => map.ActivateTeleporter(teleporter, IsSet, party, random),
                _ => MapObjectPresence.There(o, IsSet),
            };
            map.SetActive(o.Index, active);
        }

        // render_container: a chest's model is always there; a herb source's while the source is.
        foreach (var container in record.Objects.OfType<ContainerObject>())
        {
            if (map.Linked<SceneObject>(container) is { } scene && scene.Pak != PakVariant.Without)
                map.SetActive(scene.Index, container.Kind < ContainerKind.Herb || map.IsActive(container.Index));
        }
        return map;
    }

    /// <summary>Whether a container is remembered as emptied, which it is when it has no opened flag.</summary>
    public bool Remembered(MapObjectMemory memory, ContainerObject container) =>
        memory.Find(new Memory(MapRecord.VoxelId(Record.Id, container.Index), MemoryKind.Opened)) is not null;

    // exploding_container_check.
    private bool ActivateContainer(ContainerObject o, Func<RecordId, bool> isSet, MapObjectMemory memory)
    {
        var container = _containers[o.Index] = new Container(o);
        var scene = Linked<SceneObject>(o);
        if (o.Kind >= ContainerKind.Herb)
            return MapObjectPresence.There(o, isSet) && !Remembered(memory, o);
        if (o.Opened is { } opened && isSet(opened) || Remembered(memory, o))
        {
            if (scene is not null)
                SetLook(scene.Index, ContainerLook.Opened);
            container.Empty();
            return false;
        }
        if (o.Exploded is { } exploded && isSet(exploded))
        {
            if (scene is not null)
                SetLook(scene.Index, ContainerLook.Destroyed);
            return false;
        }
        return true;
    }

    // TP_lock_secret_check.
    private bool ActivateTeleporter(TeleporterObject o, Func<RecordId, bool> isSet, Characters.Party party, IRandom random)
    {
        var teleporter = _teleporters[o.Index] = new Teleporter(o);
        if (!MapObjectPresence.There(o, isSet))
            return false;
        if (teleporter.SecretLevel != 0)
        {
            if (o.SecretFlag is not { } secret || !isSet(secret))
            {
                if (party.SecretSearch(random) < teleporter.SecretLevel)
                    return false;
            }
            teleporter.SecretLevel = 0;
        }
        if (o.TrapFlag is { } trap && isSet(trap))
            teleporter.TrapLevel = 0;
        if (o.LockFlag is { } locked && isSet(locked))
            teleporter.LockLevel = 0;
        return true;
    }
}
