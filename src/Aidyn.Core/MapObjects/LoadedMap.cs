using System.Numerics;
using Aidyn.Core.Cameras;
using Aidyn.Core.Chunks;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Rng;
using Aidyn.Core.Scenes;
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
    /// <summary>How far <see cref="Linked"/> follows links, as <c>GetVoxelObjectFromLink</c> does.</summary>
    public const int MaxLinks = 51;

    private readonly Dictionary<int, MapObject> _objects;
    private readonly HashSet<int> _active = [];
    private readonly Dictionary<int, Container> _containers = [];
    private readonly Dictionary<int, Teleporter> _teleporters = [];
    private readonly Dictionary<int, ContainerLook> _looks = [];
    private readonly Dictionary<int, CameraMove> _cameraMoves = [];

    private LoadedMap(MapRecord record, ChunkKey key)
    {
        Record = record;
        Key = key;
        _objects = record.Objects.ToDictionary(o => o.Index);
    }

    public MapRecord Record { get; }

    /// <summary>The chunk it is, which its collision and drawn surfaces are under in the world.</summary>
    public ChunkKey Key { get; }

    /// <summary>Where the chunk's origin is in the world, which its objects' positions are relative to.</summary>
    public Vector3 Origin { get; private init; }

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

    /// <summary>
    /// A camera object's move, which keeps where it's got to while the chunk is loaded, to its
    /// reference point in the world (<c>FindReferncePoint</c>), or above the player without one.
    /// </summary>
    public CameraMove CameraMove(CameraObject camera)
    {
        if (!_cameraMoves.TryGetValue(camera.Index, out var move))
        {
            var point = camera.RefPoint is { } id && MapRecord.MapOf(id) == Record.Id && this[MapRecord.IndexOf(id)] is ReferencePointObject reference
                ? Origin + reference.Position
                : (Vector3?)null;
            _cameraMoves[camera.Index] = move = new CameraMove(camera.Flags, point, camera.Values);
        }
        return move;
    }

    /// <summary>Which of its models a container's scene object shows.</summary>
    public ContainerLook Look(int sceneIndex) => _looks.GetValueOrDefault(sceneIndex);

    internal void SetLook(int sceneIndex, ContainerLook look) => _looks[sceneIndex] = look;

    /// <summary>
    /// The object of type <typeparamref name="T"/> an object works with, following links from object to
    /// object (<c>GetVoxelObjectFromLink</c>, checked in 1.0's code): a container's model, or the actor a
    /// trigger animates. An object with no link is its own. Null where the chain ends elsewhere, which the
    /// original crashes on.
    /// </summary>
    public T? Linked<T>(MapObject from) where T : MapObject
    {
        var o = from;
        for (var i = 0; i < MaxLinks && o.Link is { } link; i++)
        {
            if (this[MapRecord.IndexOf(link)] is not { } next)
                return null;
            o = next;
            if (o is T found)
                return found;
        }
        return o as T;
    }

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
    /// <item>A scene object remembered as put out (<see cref="PutOut"/>) isn't there (<c>0x80017af8</c> in 1.0
    /// looks every scene object up in the chart).</item>
    /// </list>
    /// </summary>
    public static LoadedMap Activate(MapRecord record, ChunkKey key, FlagNetwork flags, MapObjectMemory memory, Characters.Party party,
        IRandom random, Vector3 origin = default)
    {
        var map = new LoadedMap(record, key) { Origin = origin };
        map.HarvestShare = Containers.HarvestShare(party, random);

        foreach (var o in record.Objects)
        {
            if (o.Pak == PakVariant.Without)
                continue;
            var active = o switch
            {
                ContainerObject container => map.ActivateContainer(container, flags.IsSet, memory),
                TeleporterObject teleporter => map.ActivateTeleporter(teleporter, flags.IsSet, party, random),
                SceneObject scene => MapObjectPresence.There(o, flags.IsSet) && !map.Remembered(memory, scene),
                _ => MapObjectPresence.There(o, flags.IsSet),
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

    /// <summary>
    /// Gates the scene objects no container links to again by the flags, as the runtime does when the
    /// pause menu closes, so what changed there shows, and keeps those remembered as put out out, as
    /// the original's reload does. Returns whether any came or went.
    /// </summary>
    public bool Regate(FlagNetwork flags, MapObjectMemory memory)
    {
        var models = Record.Objects.OfType<ContainerObject>().Select(c => Linked<SceneObject>(c)?.Index).OfType<int>().ToHashSet();
        var changed = false;
        foreach (var scene in Record.Objects.OfType<SceneObject>())
        {
            if (scene.Pak == PakVariant.Without || models.Contains(scene.Index))
                continue;
            var there = MapObjectPresence.There(scene, flags.IsSet) && !Remembered(memory, scene);
            if (there == IsActive(scene.Index))
                continue;
            SetActive(scene.Index, there);
            changed = true;
        }
        return changed;
    }

    /// <summary>Whether a scene object is remembered as put out.</summary>
    public bool Remembered(MapObjectMemory memory, SceneObject scene) =>
        memory.Find(new Memory(MapRecord.VoxelId(Record.Id, scene.Index), MemoryKind.PutOut)) is not null;

    /// <summary>
    /// The distances what goes out once reached is put out by (<see cref="PutOut"/>): the original's, whatever the Draw distance
    /// and Model detail settings, the user's choice. Putting a light out is the game's behaviour, which a save keeps, not how far
    /// the scenery draws, so the settings don't scale it (docs/systems/rendering.md#draw-distance), and it isn't a setting the
    /// session is handed (<see cref="SessionSettings"/>).
    /// </summary>
    public static readonly ObjectDistance PutOutDistances = ObjectDistance.Original;

    /// <summary>
    /// Puts out the active objects that go out once reached (<see cref="SceneObject.GoesOut"/>, Oriana's path
    /// lights) whose first level of detail the original draws from <paramref name="view"/> and whose second it
    /// doesn't: within 2 short of the first level's distance, 3 for the lights, and in view
    /// (<c>RenderVoxelScenes</c>, <c>0x8000cda0</c> in 1.0). Each stops being active, so it's gone from the next
    /// frame, without fading, and is remembered for <see cref="MapObjectMemory.PutOutLoads"/> map loads, which
    /// keeps it out whenever its chunk loads till then. Walking away doesn't bring it back.
    /// </summary>
    /// <remarks>
    /// It goes by the original's distances whatever the draw distance is set to (<see cref="PutOutDistances"/>), since it's
    /// the game's behaviour, not how far it draws. The original keys the memory under the chunk the player is in, with
    /// the object's index in its own map, wherever the object is (the call passes the zone engine's current
    /// map set, row and column with no offset, <c>0x11</c>). So a light in a neighbour, reached from across
    /// the chunk's edge, goes out until its chunk loads again, and the memory names the object at that index
    /// in the player's chunk, which stays out the next time that chunk loads if it's a scene object. Ported
    /// as it is. Of the 23 lights only one is within 3 of its chunk's edge, across the ground: object 48 of J08,
    /// 2.99 from J09, so it can hardly happen.
    /// </remarks>
    /// <param name="view">Where the original draws the scene objects from this frame.</param>
    /// <param name="centre">The map of the chunk the player is in.</param>
    /// <returns>The objects put out.</returns>
    public IReadOnlyList<SceneObject> PutOut(in SceneView view, MapObjectMemory memory, RecordId centre)
    {
        List<SceneObject>? putOut = null;
        Span<byte> alphas = stackalloc byte[2];
        foreach (var o in Record.Objects)
        {
            if (o is not SceneObject { GoesOut: true } scene || !IsActive(scene.Index))
                continue;
            SceneObjectFade.Alphas(scene, Origin, view, alphas, PutOutDistances);
            if (alphas[0] == 0 || alphas[1] != 0)
                continue;
            SetActive(scene.Index, false);
            memory.Remember(new Memory(MapRecord.VoxelId(centre, scene.Index), MemoryKind.PutOut), 0, MapObjectMemory.PutOutLoads);
            (putOut ??= []).Add(scene);
        }
        return putOut ?? [];
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
