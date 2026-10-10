using Aidyn.Plugins;
using Aidyn.Plugins.Assets;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Audio;

/// <summary>A sample as an instrument plays it, with its loop.</summary>
/// <param name="Samples">16-bit mono PCM.</param>
/// <param name="Loop">Where it loops for ever; null if it plays once.</param>
public sealed record Instrument(short[] Samples, SampleLoop? Loop);

/// <summary>
/// A sequence ready to play: a <see cref="SequenceRecord"/> with its events and samples loaded.
/// </summary>
/// <param name="Stream">The event stream (docs/formats/audio.md).</param>
public sealed record Sequence(int Tracks, IReadOnlyList<Instrument> Instruments, byte[] Stream, int LoopPosition = 0)
{
    /// <summary>
    /// Loads <paramref name="id"/>'s record, its <c>.dcm</c> events and its instruments' WAVs.
    /// Samples several sequences share are read once per <paramref name="samples"/> cache.
    /// </summary>
    public static Sequence Load(PluginSet plugins, RecordId id, Dictionary<RecordId, short[]>? samples = null)
    {
        var record = plugins.Get<SequenceRecord>(id);
        var events = plugins.FindAsset(id, "dcm") ?? throw new FileNotFoundException($"{id} has no .dcm asset.");
        samples ??= [];
        var instruments = record.Instruments.Select(i =>
        {
            if (!samples.TryGetValue(i.Sample, out var pcm))
            {
                var wav = plugins.FindAsset(i.Sample, "wav") ?? throw new FileNotFoundException($"{i.Sample} has no .wav asset.");
                samples[i.Sample] = pcm = Wav.Decode(File.ReadAllBytes(wav));
            }
            return new Instrument(pcm, i.Loop);
        }).ToList();
        return new Sequence(record.Tracks, instruments, File.ReadAllBytes(events), record.LoopPosition);
    }
}
