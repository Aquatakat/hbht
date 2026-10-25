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

    /// <summary>A profile's keys, their ids resolved through the voice sounds. Kind 0 makes no sound, so it's none.</summary>
    public static List<EntityRecord> WithSounds(GameDb db, BodySoundTables tables, List<EntityRecord> entities)
    {
        var positions = new Dictionary<RecordId, int>();
        for (var i = 0; i < db.Entities.Count; i++)
        {
            if (DbRecordIds.For(db.Entities[i].Id) is { } id)
                positions.TryAdd(id, i);
        }
        return
        [
            .. entities.Select(e => positions.TryGetValue(e.Id, out var p) && p < tables.Profiles.Count && tables.Profiles[p] is { Kind: not 0 } profile
                ? e with { Sounds = new BodySounds(profile.Kind, [.. profile.Keys.Select(k => Key(k, tables.VoiceSounds))]) }
                : e),
        ];
    }

    public static List<WeaponRecord> WithSounds(GameDb db, BodySoundTables tables, List<WeaponRecord> weapons)
    {
        var sounds = new Dictionary<RecordId, IReadOnlyList<RecordId>>();
        for (var i = 0; i < db.Weapons.Count; i++)
        {
            if (DbRecordIds.For(db.Weapons[i].Id) is { } id && tables.WeaponSounds.TryGetValue(i, out var pair))
                sounds.TryAdd(id, [Sequence(tables.VoiceSounds[pair.Item1]), Sequence(tables.VoiceSounds[pair.Item2])]);
        }
        return [.. weapons.Select(w => sounds.TryGetValue(w.Id, out var s) ? w with { Sounds = s } : w)];
    }

    public static List<SpellRecord> WithSounds(GameDb db, BodySoundTables tables, List<SpellRecord> spells)
    {
        var casts = new Dictionary<RecordId, RecordId>();
        for (var i = 0; i < db.Spells.Count && i < tables.SpellSounds.Count; i++)
        {
            if (DbRecordIds.For(db.Spells[i].Id) is { } id)
                casts.TryAdd(id, Sequence(tables.SpellSounds[i]));
        }
        return [.. spells.Select(s => casts.TryGetValue(s.Id, out var cast) ? s with { Cast = cast } : s)];
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
        new(key.Frac, key.Request, [.. key.Ids.Select(id => Sequence(sounds[id]))]);
}
