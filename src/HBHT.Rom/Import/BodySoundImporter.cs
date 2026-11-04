using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Import;

/// <summary>
/// Bodies' sounds as records (docs/systems/audio.md): each entity's audio keys, by its position in the entity DB, which
/// is its place in the entity list; the footsteps by body kind; and each weapon's and spell's sounds.
/// </summary>
internal static class BodySoundImporter
{
    private static RecordId Sequence(int index) => RecordId.Hex(RecordTypes.Sequence, index);

    /// <summary>
    /// A profile's keys, their ids resolved through the voice sounds. Kind 0 makes no sound, so it's none. An entity from the dialogue
    /// entity table has the profile at its place in <c>DialogEntityList</c> in the entities' table, the entity's at that place
    /// (<c>Wanderers.Alloc</c> stores <c>GetIdIndex</c> of its ID, and <c>AudioKeys.ProcessPlayer</c> reads the entity profiles with
    /// it). That list is the DB's order (<see cref="PositionLists"/>), as the entity list is. A twin keeps its entity's own.
    /// </summary>
    public static List<EntityRecord> WithSounds(GameDb db, BodySoundTables tables, List<EntityRecord> entities)
    {
        var sounds = Sounds(db.Entities.Select(e => e.Id), tables);
        foreach (var (id, s) in Sounds(db.DialogEntities.Select(e => e.Id).Where(e => !DbRecordIds.Twins.ContainsKey(e.Value)), tables, keepPlaces: db.DialogEntities.Select(e => e.Id)))
            sounds.TryAdd(id, s);
        return [.. entities.Select(e => sounds.TryGetValue(e.Id, out var s) ? e with { Sounds = s } : e)];
    }

    /// <summary>
    /// The profile at each record's place in its DB table, for those whose isn't kind 0: the places are <paramref name="keepPlaces"/>'s,
    /// the whole table, when <paramref name="records"/> leaves some out.
    /// </summary>
    private static Dictionary<RecordId, BodySounds> Sounds(IEnumerable<ItemId> records, BodySoundTables tables, IEnumerable<ItemId>? keepPlaces = null)
    {
        var wanted = records.ToHashSet();
        var sounds = new Dictionary<RecordId, BodySounds>();
        var place = 0;
        foreach (var record in keepPlaces ?? wanted)
        {
            var p = place++;
            if (wanted.Contains(record) && DbRecordIds.For(record) is { } id && p < tables.Profiles.Count && tables.Profiles[p] is { Kind: not 0 } profile)
                sounds.TryAdd(id, new BodySounds(profile.Kind, [.. profile.Keys.Select(k => Key(k, tables.VoiceSounds))]));
        }
        return sounds;
    }

    /// <summary>Each weapon's strike sounds, by its place in the weapon DB.</summary>
    public static List<ItemRecord> WithSounds(GameDb db, BodySoundTables tables, List<ItemRecord> items)
    {
        var sounds = new Dictionary<RecordId, IReadOnlyList<RecordId>>();
        for (var i = 0; i < db.Weapons.Count; i++)
        {
            if (DbRecordIds.For(db.Weapons[i].Id) is { } id && tables.WeaponSounds.TryGetValue(i, out var pair))
                sounds.TryAdd(id, [Sequence(tables.VoiceSounds[pair.Item1]), Sequence(tables.VoiceSounds[pair.Item2])]);
        }
        return [.. items.Select(w => w.Weapon is { } weapon && sounds.TryGetValue(w.Id, out var s) ? w with { Weapon = weapon with { Sounds = s } } : w)];
    }

    public static List<SpellRecord> WithSounds(GameDb db, BodySoundTables tables, List<SpellRecord> spells)
    {
        var casts = new Dictionary<RecordId, RecordId>();
        for (var i = 0; i < db.Spells.Count && i < tables.SpellSounds.Count; i++)
        {
            if (DbRecordIds.For(db.Spells[i].Id) is { } id)
                casts.TryAdd(id, Sequence(tables.SpellSounds[i]));
        }
        return [.. spells.Select(s => casts.TryGetValue(s.Id, out var cast)
            ? s with { Presentation = (s.Presentation ?? new MagicPresentation()) with { Sound = cast } }
            : s)];
    }

    /// <summary>Each body kind's footsteps: its loudness and its set's keys by surface, through the step sounds.</summary>
    public static List<BodyKind?> Kinds(BodySoundTables tables)
    {
        SoundKey? Step(int entry) => tables.Footsteps.ElementAtOrDefault(entry) is { } key ? Key(key, tables.StepSounds) : null;
        return
        [
            .. BodySoundTables.KindStepSet.Select((set, kind) => set < 0 || set >= tables.StepSets.Count ? null : new BodyKind(
                (float)BodySoundTables.KindLoudness[kind],
                [.. tables.StepSets[set].Select(s => new FootstepSurface(Step(s.Sneak), Step(s.Walk), Step(s.Run)))])),
        ];
    }

    private static SoundKey Key(RomSoundKey key, IReadOnlyList<int> sounds) =>
        new(key.Frac, Requests.Id(key.Request), [.. key.Ids.Select(id => Sequence(sounds[id]))]);
}
