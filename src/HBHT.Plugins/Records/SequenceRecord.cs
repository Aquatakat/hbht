namespace HBHT.Plugins.Records;

/// <summary>
/// A piece of music or a sound effect (<c>aidyn:sequence/0xHHHH</c>, a Borg12): rows of events over
/// its tracks, which play its instruments. The events are the asset <c>sequence/0xHHHH.dcm</c>, in
/// the original's encoding (ADR 0016); docs/formats/audio.md has it.
/// </summary>
/// <param name="Tracks">How many tracks each row has an event for.</param>
/// <param name="LoopPosition">Where in the events a looping play starts again, in bytes. Whether it loops is up to whatever plays it.</param>
public sealed record SequenceRecord(
    RecordId Id,
    int Tracks,
    IReadOnlyList<SequenceInstrument> Instruments,
    int LoopPosition = 0);

/// <summary>A sample a sequence plays, and its loop.</summary>
/// <param name="Sample">An <c>aidyn:sample/…</c>, a mono WAV asset.</param>
/// <param name="Loop">Null if it plays once.</param>
public sealed record SequenceInstrument(RecordId Sample, SampleLoop? Loop = null);

/// <summary>
/// Where a sample loops, in samples: on reaching <paramref name="End"/> it goes back to
/// <paramref name="Start"/>, for ever, until the note stops.
/// </summary>
public sealed record SampleLoop(int Start, int End);
