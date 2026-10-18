using System.Numerics;
using Aidyn.Core.Characters;
using Aidyn.Core.Flags;
using Aidyn.Core.Items;
using Aidyn.Core.Physics;
using Aidyn.Core.Ui;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.MapObjects;

/// <summary>What using map objects asks of the runtime or the editor showing the world.</summary>
public interface IMapObjectHost
{
    /// <summary>
    /// Goes through a teleporter, in the chunk <paramref name="from"/>: the fade, then the player arriving
    /// (<c>set_teleport_pointer</c>, then <c>TeleportPlayer</c>; see <see cref="TeleportArrival"/>).
    /// </summary>
    void Teleport(LoadedMap from, TeleporterObject teleporter);

    /// <summary>
    /// Opens the loot window on what a container held (<c>build_loot_menu</c>). The gold, which it shows,
    /// is already the party's.
    /// </summary>
    void OpenLoot(ItemPile loot, int gold);

    /// <summary>Starts a dialogue object's script (<c>dialouge_vobject_func</c>).</summary>
    void StartScript(LoadedMap map, DialogueObject dialogue);

    /// <summary>
    /// A pressed on a monster party's body (<c>monsterpary_func</c>): a talker's conversation starts
    /// (<see cref="Trek.Conversation"/>), and a hunter's battle. Does nothing by default.
    /// </summary>
    void Talk(LoadedMap map, MonsterPartyObject party) { }

    /// <summary>Asks an actor a scene object places for an animation (<c>Borg7_SetAnimation</c>).</summary>
    void Animate(LoadedMap map, SceneObject actor, int request);

    /// <summary>A scene object's look or presence changed: a container opened, destroyed or taken.</summary>
    void Changed(LoadedMap map, SceneObject scene);

    /// <summary>A one-shot sound plays.</summary>
    void Sound(Audio.OneShot sound);

    /// <summary>A trapped container goes off where it stands, in the world, with its lights (<see cref="Lights.DynamicLights.Explosion"/>).</summary>
    void Explode(Vector3 at) { }
}

/// <summary>
/// Using map objects in trek (<c>objectJumpTables.cpp</c>, <c>voxObjects2.cpp</c>, <c>voxelProcess.cpp</c>,
/// <c>lockpick.cpp</c>, <c>trapmenu.cpp</c>): what A reaches and does, what walking into a tangible object
/// or onto a trigger triangle does, and the popups each shows. Only the chunk the player is in counts, as
/// the original keeps only its objects at hand.
/// </summary>
/// <remarks>
/// <para>
/// Each step that needs the player's say shows a popup and waits: a secret door found, a trap's menu, a
/// lock picked, a trap's damage. Closing the popup of a secret door, a disarmed trap or a picked lock uses
/// the object again, so the next step follows; so does closing a door's trap's damage, which then opens it.
/// </para>
/// <para>
/// Monster parties, which A also reaches, come with their wanderers (M2's fifth session). Camera
/// objects take the camera (<see cref="Camera"/>).
/// </para>
/// </remarks>
public sealed partial class MapObjectUse(GameState game, MenuText text, IMapObjectHost host)
{
    /// <summary>How many objects A weighs at once (<c>obj_ref_func</c>'s list).</summary>
    public const int MaxCandidates = 16;

    /// <summary>How long a container's opening sound and a trap's explosion play, in ticks.</summary>
    public const int ContainerSoundTicks = 180, ExplosionTicks = 240;

    private Action? _onClose;
    private Action<int>? _onChoose;

    /// <summary>The collision whose toggles triggers change, keyed by <see cref="LoadedMap.Key"/>.</summary>
    public CollisionWorld? World { get; set; }

    /// <summary>The popup over the world, if one is up.</summary>
    public TrekPopup? Popup { get; private set; }

    private FlagNetwork Flags => game.Flags;

    private Party Party => game.Party;

    private bool Holds(FlagGate? gate) => gate is null || Flags.Holds(gate);

    private void Write(FlagWrite? used)
    {
        if (used is not null)
            Flags.TrySet(used.Flag, !used.Clear);
    }

    // A, B and the stick on a popup.

    /// <summary>A: closes a message, or chooses the cursor's option.</summary>
    public void Accept()
    {
        if (Popup is not { } popup)
            return;
        if (popup.Kind == TrekPopupKind.Choice)
        {
            var choose = _onChoose;
            Close();
            choose?.Invoke(popup.Choices.Cursor);
        }
        else
        {
            var then = _onClose;
            Close();
            then?.Invoke();
        }
    }

    /// <summary>B: closes a message as A does, or a choice without choosing.</summary>
    public void Back()
    {
        if (Popup is { Kind: TrekPopupKind.Choice })
            Close();
        else
            Accept();
    }

    /// <summary>Moves a choice's cursor.</summary>
    public void MoveCursor(int by) => Popup?.Choices.Move(by);

    /// <summary>Says something in trek's message box (<c>TrekTextPopup</c>), which A or B closes.</summary>
    public void Tell(string text) => Show(TrekPopup.Message(text));

    private void Close()
    {
        Popup = null;
        _onClose = null;
        _onChoose = null;
    }

    private void Show(TrekPopup popup, Action? then = null)
    {
        Popup = popup;
        _onClose = then;
        _onChoose = null;
    }

    // What A reaches.

    /// <summary>
    /// The object A uses (<c>obj_ref_func</c>): of the objects the grid cell the player is in lists (off the
    /// grid, the first cell's; see <see cref="ObjectGrid.At"/>), those active, not tangible and within
    /// reach by each type's test, after the body A reaches, if any; the one alone, or else the best by
    /// nearness and by facing: (farthest − its distance) / farthest + (1 + the cosine of its bearing off
    /// the heading) / 2, the later on a tie. Null if none.
    /// </summary>
    /// <param name="at">The player's centre, in the chunk's coordinates.</param>
    /// <param name="heading">The way the player faces, in X and Z.</param>
    /// <param name="radius">The player's radius, which a container's reach adds.</param>
    /// <param name="body">
    /// The monster party whose body A reaches (see <see cref="Trek.Wanderers.Reaches"/>), where its body is in
    /// the chunk's coordinates, and how far across the ground, which it's weighed by.
    /// </param>
    public MapObject? Reach(LoadedMap map, Vector3 at, Vector2 heading, float radius, (MonsterPartyObject Party, Vector3 At, float Distance)? body = null)
    {
        // A map without a grid, which only a mod's could be, weighs all its objects.
        var listed = map.Record.Grid is { } grid ? grid.At(at).Select(i => map[i]).OfType<MapObject>() : map.Objects;
        var candidates = new List<(MapObject Object, Vector3 At, float Distance)>();
        if (body is var (party, bodyAt, bodyDistance))
            candidates.Add((party, bodyAt, bodyDistance));
        candidates.AddRange(listed.Where(o => !o.Tangible && map.IsActive(o.Index) && InReach(map, o, at, radius))
            .Take(MaxCandidates - candidates.Count).Select(o => (o, o.Position, Vector3.Distance(o.Position, at))));
        if (candidates.Count <= 1)
            return candidates.Count == 0 ? null : candidates[0].Object;
        var farthest = candidates.Max(c => c.Distance);
        MapObject? best = null;
        var bestScore = 0f;
        var player = new Vector2(at.X, at.Z);
        foreach (var (o, position, distance) in candidates)
        {
            // The original keeps the way the player faces reversed, and the bearing from the object.
            var bearing = new Vector2(position.X, position.Z) - player;
            bearing = bearing.LengthSquared() > 0 ? Vector2.Normalize(bearing) : bearing;
            var score = (farthest - distance) / farthest + (Vector2.Dot(bearing, heading) + 1) * 0.5f;
            if (bestScore <= score)
                (best, bestScore) = (o, score);
        }
        return best;
    }

    // gVoxelProxFuncs.
    private bool InReach(LoadedMap map, MapObject o, Vector3 at, float radius)
    {
        var distance = Vector3.Distance(o.Position, at);
        return o switch
        {
            ContainerObject c => distance <= c.Reach + radius && map.Container(o.Index)!.Usable(Flags),
            TeleporterObject or TriggerObject or DialogueObject => distance <= o.Radius && Holds(o.Enabled),
            SavePointObject => distance <= o.Radius,
            _ => false,
        };
    }

    /// <summary>A pressed in trek: uses what the player reaches, if anything. False if nothing.</summary>
    public bool UseNearest(LoadedMap map, Vector3 at, Vector2 heading, float radius, (MonsterPartyObject Party, Vector3 At, float Distance)? body = null)
    {
        if (Popup is not null || Reach(map, at, heading, radius, body) is not { } o)
            return false;
        Use(map, o);
        return true;
    }

    /// <summary>Uses an object, as A does (<c>gVoxelUseFuncs</c>).</summary>
    public void Use(LoadedMap map, MapObject o)
    {
        switch (o)
        {
            case ContainerObject container:
                Loot(map, container);
                break;
            case TeleporterObject teleporter:
                UseTeleporter(map, teleporter);
                break;
            case DialogueObject dialogue:
                host.StartScript(map, dialogue);
                break;
            case TriggerObject trigger:
                Trigger(map, trigger);
                break;
            case MonsterPartyObject party:
                host.Talk(map, party);
                break;
        }
    }

    // Walking into objects and onto trigger triangles.

    /// <summary>
    /// What walking within a tangible object's radius does, each tick (<c>ProcessVoxelObjects</c>): an
    /// active tangible teleporter whose <see cref="MapObject.Enabled"/> holds takes the player through,
    /// skipping its secret, trap and lock; a trigger goes off; a dialogue starts its script. A popup
    /// doesn't stop the world: triggers still go off under one, but a teleporter waits for it to close
    /// (<c>vobject_pause_check</c>), and so does a dialogue.
    /// </summary>
    public void Touch(LoadedMap map, Vector3 at)
    {
        TouchCameras(map, at);
        foreach (var o in map.Objects)
        {
            if (!o.Tangible || !map.IsActive(o.Index) || Vector3.Distance(o.Position, at) > o.Radius || !Holds(o.Enabled))
                continue;
            if (Popup is not null && o is not TriggerObject)
                continue;
            switch (o)
            {
                case TeleporterObject teleporter:
                    host.Teleport(map, teleporter);
                    return;
                case TriggerObject trigger:
                    Trigger(map, trigger);
                    break;
                case DialogueObject dialogue:
                    host.StartScript(map, dialogue);
                    return;
            }
        }
    }

    /// <summary>
    /// What touching a trigger triangle does (<c>check_trigger</c>), if the object it names is active: a
    /// camera takes the camera for the next second (<see cref="StepCameras"/>); a teleporter takes the
    /// player through at once, whatever its gates; a dialogue starts its script if the player is within
    /// its radius and its <see cref="MapObject.Enabled"/> holds.
    /// </summary>
    public void Tread(LoadedMap map, int index, Vector3 at)
    {
        if (map.IsActive(index) && map[index] is CameraObject camera)
        {
            TreadCamera(map, camera);
            return;
        }
        if (Popup is not null || !map.IsActive(index) || map[index] is not { } o)
            return;
        switch (o)
        {
            case TeleporterObject teleporter:
                host.Teleport(map, teleporter);
                break;
            case DialogueObject dialogue when Vector3.Distance(o.Position, at) <= o.Radius && Holds(o.Enabled):
                host.StartScript(map, dialogue);
                break;
        }
    }

    // Triggers.

    /// <summary>
    /// A trigger going off (<c>trigger_vobject_func</c>): it sets its flag, asks its actor for an
    /// animation, or changes its map's toggles' collision; then it writes its <see cref="MapObject.Used"/>.
    /// </summary>
    public void Trigger(LoadedMap map, TriggerObject trigger)
    {
        switch (trigger.Kind)
        {
            case TriggerKind.SetFlag when trigger.Flag is { } flag:
                Flags.TrySet(flag, true);
                break;
            case TriggerKind.Animate when trigger.Request is { } request && map.Linked<SceneObject>(trigger) is { } actor:
                host.Animate(map, actor, request);
                break;
            case TriggerKind.ChangeCollision when trigger.Toggle is { } toggle:
                World?.ChangeToggles(map.Key, toggle, trigger.KeepFlags ?? 0xFFFF, trigger.AddFlags ?? 0);
                break;
        }
        Write(trigger.Used);
    }

    // Teleporters.

    // teleporter_func: a secret door is found first, then its trap and its lock, and then the player goes through.
    private void UseTeleporter(LoadedMap map, TeleporterObject o)
    {
        var teleporter = map.Teleporter(o.Index)!;
        if (o.SecretFlag is { } secret && !Flags.IsSet(secret))
        {
            // secret_door_func: closing the message tries the door again.
            teleporter.SecretLevel = 0;
            Flags.TrySet(secret, true);
            Show(TrekPopup.Message(text[MenuText.SecretDoor]), () => UseTeleporter(map, o));
        }
        else if (teleporter.TrapLevel != 0 && !(o.TrapFlag is { } trap && Flags.IsSet(trap)))
        {
            TrapMenu(map, o, teleporter.TrapLevel);
        }
        else if (teleporter.LockLevel != 0 && !(o.LockFlag is { } locked && Flags.IsSet(locked)))
        {
            PickLock(map, o, teleporter.LockLevel);
        }
        else
        {
            host.Teleport(map, o);
        }
    }

    // Containers.

    // loot_func.
    private void Loot(LoadedMap map, ContainerObject o)
    {
        var container = map.Container(o.Index)!;
        if (o.Opened is { } opened && Flags.IsSet(opened) || map.Remembered(game.Memory, o) || o.Exploded is { } exploded && Flags.IsSet(exploded))
        {
            map.SetActive(o.Index, false);
            return;
        }
        if (o.Kind is ContainerKind.Herb or ContainerKind.Spice or ContainerKind.Gem)
        {
            Harvest(map, o, container);
            return;
        }
        if (o.Kind == ContainerKind.Custom)
        {
            map.SetActive(o.Index, false);
            Write(o.Used);
        }
        if (container.TrapLevel != 0)
            TrapMenu(map, o, container.TrapLevel);
        else if (container.LockLevel != 0)
            PickLock(map, o, container.LockLevel);
        else
            Open(map, o, container);
    }

    // get_loot_reagent: the source is used up, unless its gates still hold, when it's remembered for a while.
    private void Harvest(LoadedMap map, ContainerObject o, Container container)
    {
        var loot = container.Harvest(map.HarvestShare, Flags, game.Items);
        var scene = map.Linked<SceneObject>(o);
        var stays = MapObjectPresence.There(o, Flags.IsSet) && !map.Remembered(game.Memory, o);
        map.SetActive(o.Index, stays);
        if (scene is not null)
        {
            map.SetActive(scene.Index, stays);
            host.Changed(map, scene);
        }
        if (stays)
            game.Memory.Remember(new Memory(MapRecord.VoxelId(map.Record.Id, o.Index), MemoryKind.Opened), 0);
        host.OpenLoot(loot, 0);
    }

    // loot_func's opening: its flags, its model or its own model hidden, its loot, and the gold or the loot window.
    private void Open(LoadedMap map, ContainerObject o, Container container)
    {
        var (loot, gold) = container.Open(Flags, game.Items, id => game.Plugins.Get<LootRecord>(id), game.Random.Loot);
        if (map.Linked<SceneObject>(o) is { } scene)
        {
            if (o.Kind == ContainerKind.Custom)
                map.SetActive(scene.Index, false);
            else
                map.SetLook(scene.Index, ContainerLook.Opened);
            host.Changed(map, scene);
        }
        // loot_func: by its kind, at the sound volume as its gain.
        if (o.Kind != ContainerKind.Custom && game.Sounds?.Containers.ElementAtOrDefault((int)o.Kind) is { } opens)
            host.Sound(new Audio.OneShot(opens, ContainerSoundTicks, TimesSoundVolume: true));
        map.SetActive(o.Index, false);
        // Without an opened flag, it's remembered as emptied for a while.
        if (o.Opened is null && !(o.Exploded is { } exploded && Flags.IsSet(exploded)))
            game.Memory.Remember(new Memory(MapRecord.VoxelId(map.Record.Id, o.Index), MemoryKind.Opened), 0);
        Party.Gold += (uint)gold;
        if (loot.Stacks.Count == 0)
        {
            if (game.Sounds is { } sounds)
                host.Sound(new Audio.OneShot(sounds.GoldGained, Audio.OneShot.LongTicks));
            Show(TrekPopup.Message(text.Format(MenuText.PickedUpGold, gold)));
        }
        else
        {
            host.OpenLoot(loot, gold);
        }
    }

    // Traps.

    // TrapMenu::InitMenu: how dangerous the trap is, then Disarm or Exit.
    private void TrapMenu(LoadedMap map, MapObject o, int level)
    {
        var danger = text[level < 3 ? MenuText.TrapSlightly : level < 6 ? MenuText.TrapModerately : MenuText.TrapVery];
        Popup = TrekPopup.Choice(MenuText.Fill(text[MenuText.TrapDanger], danger), 180, 10, TrekPopup.PaleGreen,
            [text[MenuText.TrapDisarm], text[MenuText.TrapExit]]);
        _onClose = null;
        _onChoose = choice =>
        {
            if (choice == 0)
                Disarm(map, o, level);
        };
    }

    // TrapMenu::AttemptDisarm, TrapDisarmed and TrapExploded.
    private void Disarm(LoadedMap map, MapObject o, int level)
    {
        IReadOnlyList<(Character Member, int Damage)>? hurt;
        var random = game.Random.World;
        if (o is ContainerObject container)
        {
            hurt = map.Container(o.Index)!.Disarm(Party, Flags, random);
            if (hurt is not null)
            {
                // open_explosive_chest.
                if (map.Linked<SceneObject>(container) is { } scene)
                {
                    map.SetLook(scene.Index, ContainerLook.Destroyed);
                    host.Changed(map, scene);
                }
                map.SetActive(o.Index, false);
                // Containers.Explode (0x8000ffe4): its fires' and burst's lights, then the sound.
                host.Explode(map.Origin + container.Position);
                // open_explosive_chest: one of four at random.
                if (game.Sounds is { Explosions.Count: > 0 } sounds)
                    host.Sound(new Audio.OneShot(sounds.Explosions[(int)Rng.RandomExtensions.Below(game.Random.Cosmetic, (uint)sounds.Explosions.Count)],
                        ExplosionTicks, TimesSoundVolume: true));
            }
        }
        else
        {
            var teleporter = map.Teleporter(o.Index)!;
            teleporter.TrapLevel = 0;
            hurt = Containers.Disarm(Party, level, random) ? null : Containers.TrapDamage(Party, level, random);
        }

        if (hurt is null)
        {
            Show(TrekPopup.Message(text[MenuText.TrapDisarmed], 120), () => AfterTrap(map, o));
            return;
        }
        var lines = new List<string> { text[MenuText.TrapExploded] };
        lines.AddRange(hurt.Select(h => text.Format(MenuText.TrapDamage, h.Member.Name, h.Damage)));
        // A door's damage goes on through the door; a container's is the end of it.
        Show(TrekPopup.Damage(string.Join('\n', lines)), o is TeleporterObject ? () => AfterTrap(map, o) : null);
    }

    // TrapMenu::CloseMenu: a door's disarm flag is set, and the object is used again.
    private void AfterTrap(LoadedMap map, MapObject o)
    {
        if (o is TeleporterObject { DisarmFlag: { } disarmed })
            Flags.TrySet(disarmed, true);
        Use(map, o);
    }

    // Locks.

    // lockpicking_check: picked, it's unlocked, and closing the message uses it again; a door's picked flag is set then.
    private void PickLock(LoadedMap map, MapObject o, int level)
    {
        var thief = Party.MostSkilled(Skill.Thief);
        var result = o is ContainerObject ? map.Container(o.Index)!.PickLock(Party) : Containers.PickLock(Party, level);
        var message = result switch
        {
            LockResult.Picked => text.Format(MenuText.LockPicked, thief!.Name),
            LockResult.NoOne => text[MenuText.LockNoOne],
            LockResult.TooTired => text.Format(MenuText.LockTooTired, thief!.Name),
            LockResult.JustBeyond => text.Format(MenuText.LockBeyond, text[MenuText.LockALittle], thief!.Name),
            LockResult.Beyond => text.Format(MenuText.LockBeyond, text[MenuText.LockDefinitely], thief!.Name),
            _ => text.Format(MenuText.LockBeyond, text[MenuText.LockFar], thief!.Name),
        };
        if (result != LockResult.Picked)
        {
            Show(TrekPopup.Message(message));
            return;
        }
        if (map.Teleporter(o.Index) is { } teleporter)
            teleporter.LockLevel = 0;
        Show(TrekPopup.Message(message), () =>
        {
            if (o is TeleporterObject { PickedFlag: { } picked })
                Flags.TrySet(picked, true);
            Use(map, o);
        });
    }
}
