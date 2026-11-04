using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Core.Scripts;

/// <summary>
/// A cut-scene to start: its script, where it starts from, and the chunk it plays on as its stage. What a trek cues
/// (<see cref="Trek.TrekLoop.Cue(CutSceneLaunch)"/>) and a session starts.
/// </summary>
public readonly record struct CutSceneLaunch(ScriptRecord Script, ScriptStart Start, RecordId Stage)
{
    /// <summary>
    /// What a dialogue object starts when its script is a cut-scene a plugin has and it names a stage, its reference
    /// points numbered from its own; null if not.
    /// </summary>
    /// <param name="voxel">The dialogue object's record ID, which remembers what the script's checks and random lines settle.</param>
    public static CutSceneLaunch? Of(PluginSet plugins, ScriptObject dialogue, RecordId? voxel) =>
        plugins.TryGet<ScriptRecord>(dialogue.Plays) is { Mode: ScriptMode.Cutscene } script ? Of(script, dialogue, voxel) : null;

    /// <summary>As the other, for a <paramref name="script"/> the caller has found: null if the object names no stage.</summary>
    public static CutSceneLaunch? Of(ScriptRecord script, ScriptObject dialogue, RecordId? voxel) =>
        dialogue.Stage is { } stage ? new CutSceneLaunch(script, new ScriptStart(dialogue.Marks, voxel), stage) : null;
}
