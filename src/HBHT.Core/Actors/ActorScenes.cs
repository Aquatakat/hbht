using System.Runtime.CompilerServices;
using HBHT.Plugins;
using HBHT.Plugins.Assets;

namespace HBHT.Core.Actors;

/// <summary>
/// The actors' scenes the core plays without a view, read once for a plugin set: each one's clips, by its root transform
/// (<see cref="ActorClips"/>), its still animations and its transforms (<see cref="SceneSkeleton"/>). Safe across threads.
/// </summary>
public sealed class ActorScenes
{
    private static readonly ConditionalWeakTable<PluginSet, ActorScenes> Sets = [];

    private readonly PluginSet _plugins;
    private readonly Dictionary<RecordId, Scene?> _scenes = [];

    private ActorScenes(PluginSet plugins) => _plugins = plugins;

    /// <summary>The scenes of <paramref name="plugins"/>.</summary>
    public static ActorScenes Of(PluginSet plugins) => Sets.GetValue(plugins, p => new ActorScenes(p));

    /// <summary>A scene's asset, or null if no plugin has it.</summary>
    public Scene? Get(RecordId scene)
    {
        lock (_scenes)
        {
            if (_scenes.TryGetValue(scene, out var found))
                return found;
            return _scenes[scene] = _plugins.FindAsset(scene, "glb") is { } path ? Scene.Read(File.ReadAllBytes(path)) : null;
        }
    }

    /// <summary>One scene's asset, as the core reads it.</summary>
    public sealed class Scene
    {
        private readonly GlbReader _file;
        private readonly Dictionary<int, ActorClips> _clips = [];

        private Scene(GlbReader file)
        {
            _file = file;
            Extras = SceneExtras.Read(file);
            Skeleton = SceneSkeleton.Read(file);
        }

        public static Scene Read(byte[] glb) => new(GlbReader.Parse(glb));

        public SceneExtras Extras { get; }

        public SceneSkeleton Skeleton { get; }

        /// <summary>
        /// Whether the views give it an animator: it has a glTF animation, which Godot gives a player, or still animations
        /// (<c>ActorAnimator.Animated</c>).
        /// </summary>
        public bool Plays => Clips(0).Animated || Extras.StillAnimations.Count > 0;

        /// <summary>Its clips for an actor rooted at <paramref name="rootTransform"/>.</summary>
        public ActorClips Clips(int rootTransform)
        {
            lock (_clips)
            {
                if (!_clips.TryGetValue(rootTransform, out var clips))
                    _clips[rootTransform] = clips = ActorClips.Read(_file, Extras, rootTransform);
                return clips;
            }
        }
    }
}
