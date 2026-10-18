using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Audio;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Engine;

namespace Aidyn.Rom.Import;

/// <summary>
/// The sounds engine code plays, and every sequence and sample in the archive.
/// </summary>
internal static class AudioImporter
{
    /// <summary>
    /// The sounds record, every Borg12 as a sequence record and its events as they are (<c>.dcm</c>, ADR 0016), and every
    /// Borg11 as a WAV. A sample has no rate of its own, so its WAV says the one it's most often
    /// played at, which is only for listening to it elsewhere.
    /// </summary>
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Audio");
        var archive = ctx.Archive;
        ctx.WriteRecords("sounds.json", new[] { BuildSounds(ctx.Audio, ctx.BodySounds) });
        var sequences = archive.Listings.Where(l => l.Type == BorgType.Sequence)
            .Select(l => (l.Index, Sequence: Borg12Sequence.Parse(archive.Read(l.Index)))).ToList();
        ctx.WriteRecords("sequences.json", sequences.Select(s => new SequenceRecord(
            RecordId.Hex(RecordTypes.Sequence, s.Index), s.Sequence.Tracks,
            [
                .. s.Sequence.Instruments.Select(i => new SequenceInstrument(RecordId.Hex(RecordTypes.Sample, i.Sample),
                    i is { LoopStart: { } start, LoopEnd: { } end } ? new SampleLoop(start, end) : null)),
            ],
            s.Sequence.LoopPosition)).ToList());
        foreach (var (index, sequence) in sequences)
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Sequence, index), "dcm", sequence.Stream);

        var rates = sequences
            .SelectMany(s => s.Sequence.Notes().Where(n => n.Pitch > 0).Select(n => (s.Sequence.Instruments[n.Instrument].Sample, n.Pitch)))
            .GroupBy(n => n.Sample)
            .ToDictionary(g => g.Key, g => g.CountBy(n => n.Pitch).OrderByDescending(c => c.Value).ThenBy(c => c.Key).First().Key);
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Sample))
            ctx.WriteAsset(RecordId.Hex(RecordTypes.Sample, listing.Index), "wav",
                Wav.Encode(Borg11Sample.Parse(archive.Read(listing.Index)).Samples, rates.GetValueOrDefault(listing.Index, 22050)));
    }

    /// <summary>The sounds engine code plays: from its tables where it has them, else its literals (<see cref="AudioFacts"/>).</summary>
    internal static SoundsRecord BuildSounds(AudioTables tables, BodySoundTables bodies)
    {
        static RecordId Sequence(int index) => RecordId.Hex(RecordTypes.Sequence, index);
        return new SoundsRecord(
            RecordId.Decimal(RecordTypes.Sounds, 0),
            new MenuSounds(Sequence(AudioFacts.Cursor), Sequence(AudioFacts.PageCursor), Sequence(AudioFacts.Accept), Sequence(AudioFacts.Back),
                Sequence(AudioFacts.TabBack), Sequence(AudioFacts.TabForward), Sequence(AudioFacts.Member), Sequence(AudioFacts.LevelUp),
                Sequence(AudioFacts.CheatAccepted), Sequence(AudioFacts.CheatRejected)),
            [.. tables.Script.Select(Sequence)],
            Sequence(AudioFacts.Experience),
            Sequence(AudioFacts.GoldGained),
            Sequence(AudioFacts.GoldPaid),
            [.. tables.Containers.Select(Sequence)],
            [.. tables.Explosions.Select(Sequence)],
            Sequence(AudioFacts.Rain),
            [.. AudioFacts.Thunder.Select(Sequence)],
            Sequence(AudioFacts.Title),
            Sequence(AudioFacts.Credits),
            BodySoundImporter.Kinds(bodies));
    }
}
