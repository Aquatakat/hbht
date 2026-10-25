using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Audio;

/// <summary>
/// The sequences played so far, loaded from the plugins as they're first asked for and kept, with the samples they
/// share. A sequence no plugin has, or whose assets are missing, plays nothing.
/// </summary>
public sealed class SoundBank(PluginSet plugins)
{
    private readonly Dictionary<RecordId, Sequence?> _sequences = [];
    private readonly Dictionary<RecordId, short[]> _samples = [];

    /// <summary>A sequence that couldn't be loaded, and why: for the log.</summary>
    public event Action<RecordId, string>? Missing;

    /// <summary>Puts a sequence made in code in place of the plugins' own, as tests do.</summary>
    public void Add(RecordId id, Sequence sequence) => _sequences[id] = sequence;

    public Sequence? Get(RecordId id)
    {
        if (_sequences.TryGetValue(id, out var sequence))
            return sequence;
        try
        {
            sequence = plugins.TryGet<SequenceRecord>(id) is null ? null : Sequence.Load(plugins, id, _samples);
            if (sequence is null)
                Missing?.Invoke(id, "no plugin has it");
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            Missing?.Invoke(id, e.Message);
        }
        return _sequences[id] = sequence;
    }
}
