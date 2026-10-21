using System.Numerics;
using Aidyn.Core.Audio;
using Aidyn.Core.Calendar;
using Aidyn.Core.Cameras;
using Aidyn.Core.Lights;
using Aidyn.Core.MapObjects;
using Aidyn.Core.Particles;
using Aidyn.Core.Physics;
using Aidyn.Core.Sky;
using Aidyn.Plugins.Records;

namespace Aidyn.Core.Chunks;

/// <summary>
/// The zone engine (<c>InitZoneEngine</c>, <c>HandleZoneEngineFrame</c>, <c>FreeZoneEngine</c>): the world a game mode plays
/// in, which trek, a cut-scene's stage and a battle's arena start (<c>InitZoneEngine</c>). It keeps
/// the 3×3 chunks loaded around a point (<see cref="Chunks"/>), with their collision and the surfaces they draw, and as each
/// chunk loads and goes, its sounds (<see cref="Sounds"/>) and its light map objects (<see cref="Lights"/>). It asks for the
/// map set's music, steers the rain and snow about its <see cref="Camera"/> (<see cref="Precipitation"/>), steps the weather
/// as its frame draws it (<see cref="StepWeather"/>), keeps the sky (<see cref="Sky"/>), and hears bodies' footsteps and voices
/// from the camera (<see cref="Keys"/>). The mode that runs it says where the camera is, and steps its own bodies in between.
/// </summary>
/// <remarks>
/// A stage (<see cref="Stage"/>) is the zone engine started again on another map set while this one's world is freed, as
/// the original starts it for a cut-scene: its own chunks, lights and rain, over the same audio, chunk sounds, audio keys,
/// rain loop, thunder and sky, which carry on, among them the music held from one chunk to the next (<c>Audio.PersistMusic</c>).
/// The zone engine it was made from keeps the rest, its dynamic lights among them, for its world to be loaded again
/// (<see cref="Trek.TrekLoop.Resume"/>). A battle's arena is a stage the same way (<see cref="GameSession.BeginBattle"/>).
/// </remarks>
public sealed class ZoneEngine
{
    private readonly Shared _shared;

    /// <param name="chunks">The chunks it keeps loaded, with the collision and drawn surfaces they fill.</param>
    /// <param name="audio">What plays; with none, nothing does.</param>
    /// <param name="particles">The particle pool, which the weather's rain and snow fall in; none draws none.</param>
    public ZoneEngine(GameState game, ChunkStreamer chunks, GameAudio? audio = null, ParticleHead? particles = null)
        : this(game, chunks, particles, new Shared(game, audio ?? GameAudio.Silent(game.Plugins)), part: null)
    {
    }

    /// <param name="part">
    /// The key of the one chunk whose surfaces alone the drops land on (<see cref="WeatherParticles"/>), or null for every
    /// loaded chunk's.
    /// </param>
    private ZoneEngine(GameState game, ChunkStreamer chunks, ParticleHead? particles, Shared shared, Func<ChunkKey?>? part)
    {
        Game = game;
        Chunks = chunks;
        Particles = particles;
        _shared = shared;
        if (particles is not null && game.Weather is { } weather)
            Precipitation = new WeatherParticles(particles, weather.Rules, game.Random.Cosmetic, () => Camera?.Aim ?? default,
                () => Camera?.LookXZ ?? Vector2.UnitX, () => Chunks.Visible, part);
        Lights = new WorldLights(game.Random.Cosmetic) { World = chunks.World };
        // What a mode keeps of its own hears of a chunk first, then the chunk's sounds and lights.
        chunks.Loaded += (chunk, map) =>
        {
            Loaded?.Invoke(chunk, map);
            Sounds.Load(map);
            Lights.Load(map);
        };
        chunks.Unloading += (chunk, map) =>
        {
            Unloading?.Invoke(chunk, map);
            Sounds.Unload(map);
            Lights.Unload(map);
        };
    }

    public GameState Game { get; }

    /// <summary>The chunks around the point it's loaded about, which a teleport to another map set changes.</summary>
    public ChunkStreamer Chunks { get; }

    /// <summary>The loaded chunks' collision.</summary>
    public CollisionWorld World => Chunks.World;

    /// <summary>
    /// Whether its world is loaded: a chunk is. Trek's is freed under a cut-scene's stage and a battle's arena
    /// (<see cref="Free"/>), and what plays after them, a cinematic or the credits, builds it again once it ends.
    /// </summary>
    public bool WorldLoaded => Chunks.All.Any();

    /// <summary>The solid surfaces the loaded chunks draw, if it keeps them, which rain and snow land on.</summary>
    public VisibleWorld? Visible => Chunks.Visible;

    /// <summary>
    /// A chunk has loaded, before its sounds and lights: what a mode keeps of its own, such as trek's wanderers, loads here.
    /// </summary>
    public event Action<ChunkEntry, LoadedMap>? Loaded;

    /// <summary>A chunk is going, before its sounds and lights.</summary>
    public event Action<ChunkEntry, LoadedMap>? Unloading;

    /// <summary>
    /// The camera the zone engine draws from (<c>Camera.Listener</c>): the rain falls about it, bodies are heard from it, and
    /// lights that face the camera shine from its eye. The mode running it sets it: trek's follow camera, a cut-scene's own.
    /// </summary>
    public FollowCamera? Camera { get; set; }

    /// <summary>What plays: the map set's music, the sounds of what's here, and one-shots.</summary>
    public GameAudio Audio => _shared.Audio;

    /// <summary>The loaded chunks' sound map objects.</summary>
    public MapSounds Sounds => _shared.Sounds;

    /// <summary>Bodies' footsteps and voices.</summary>
    public AudioKeys Keys => _shared.Keys;

    /// <summary>The rain loop and the thunder.</summary>
    public WeatherSounds WeatherSounds => _shared.WeatherSounds;

    /// <summary>
    /// The sky (<c>Sky::RenderSky</c>, which <c>HandleZoneEngineFrame</c> draws): one for trek's world and every stage made
    /// from it, which each enters as it's built (<see cref="SkyDirector.ClearSky"/>). The session steps it.
    /// </summary>
    public SkyDirector Sky => _shared.Sky;

    /// <summary>The loaded chunks' light map objects and the dynamic lights, which light bodies and the maps' actors.</summary>
    public WorldLights Lights { get; }

    /// <summary>The particle pool (<c>Particles.Head</c>), which its owner steps, or null.</summary>
    public ParticleHead? Particles { get; }

    /// <summary>The weather's rain and snow, in <see cref="Particles"/>, about the <see cref="Camera"/>; null without them.</summary>
    public WeatherParticles? Precipitation { get; }

    /// <summary>
    /// Asks for the music of the map set and a chunk in it (<c>Zones.SetMapSet</c>), as the original does as the zone engine
    /// starts, on every crossing into another chunk and every teleport. Out of the chunks with music, it stops.
    /// </summary>
    public void RequestMusic(ChunkEntry? chunk) => Audio.Music = MapMusic.For(Chunks.MapSet, chunk, Game.Random.Cosmetic);

    /// <summary>
    /// A tick of the weather as the zone engine draws it (<c>HandleZoneEngineFrame</c>), in trek and in cut-scenes:
    /// <c>RenderSky</c>'s lightning once a frame, with its thunder, then <c>ProcessWeather</c> every tick, with the
    /// rain loop. Its frames count on across stages, as the weather does.
    /// </summary>
    /// <param name="skyDrawn">Whether the sky is drawn (the original's sky type 3).</param>
    /// <param name="emitters">The rain's and snow's particles about the view: <see cref="Precipitation"/>, or none.</param>
    public void StepWeather(bool skyDrawn, IPrecipitationEmitters? emitters)
    {
        if (Game.Weather is not { } weather)
            return;
        if (_shared.SkyFrame.Tick())
        {
            weather.SkyFrame(skyDrawn, Game.Random.Cosmetic);
            if (weather.Thunder is { } thunder)
                WeatherSounds.Thunder(thunder);
        }
        // The rain's particles go with the weather's one decision about them.
        weather.Draw(skyDrawn, emitters: emitters);
        WeatherSounds.Step(weather);
    }

    /// <summary>Bodies are heard from the camera as it stands, before they step (<c>Camera.Listener</c>).</summary>
    public void Listen()
    {
        Keys.Eye = Camera!.Position;
        Keys.Aim = Camera.Aim;
    }

    /// <summary>
    /// The chunks' sounds follow the listener and the camera (<c>ProcessAudioBubbles</c>): trek hears them from the player,
    /// a cut-scene from where its camera looks.
    /// </summary>
    /// <param name="listener">Where, within a sound's radius, it plays.</param>
    public void StepSounds(Vector3 listener) => Sounds.Step(listener, Camera!.Position, Camera.Aim);

    /// <summary>
    /// Starts a frame of the lights as the zone engine draws them: dimmed by the screen's fade, with the sun while the sky is
    /// outdoor, and the moon while it's up there (<see cref="WorldLights.Moon"/>, <see cref="SkyDirector.Moon"/>), and lights
    /// that face the camera shining from its eye (<see cref="WorldLights.BeginFrame"/>).
    /// </summary>
    public void BeginLightFrame(float fade)
    {
        var outdoor = Sky.Drawn;
        Lights.Fade = fade;
        Lights.Eye = Camera!.Position;
        Lights.Sun = outdoor;
        Lights.Moon = outdoor ? Sky.Moon : null;
        Lights.BeginFrame(1);
    }

    /// <summary>
    /// The world goes (<c>FreeZoneEngine</c>): its chunks with their collision, sounds and lights, and the rain's emitters,
    /// which the weather forgets. What the weather draws carries on (docs/systems/weather.md), and so do the dynamic lights.
    /// </summary>
    public void Free()
    {
        Chunks.Clear();
        World.Clear();
        Visible?.Clear();
        Sounds.Clear();
        Game.Weather?.Forget(Precipitation);
        Precipitation?.Clear();
    }

    /// <summary>
    /// A stage ends (<c>FreeZoneEngine</c>, from <c>CutScene.Free</c>, <c>0x8005169c</c>): the rain's emitters are forgotten,
    /// the particle pool is emptied with every emitter in it (<c>Particles.FreeHead</c>), and then the world goes
    /// (<see cref="Free"/>). The weather drawn carries on into the world loaded next.
    /// </summary>
    public void End()
    {
        Game.Weather?.Forget(Precipitation);
        Precipitation?.Clear();
        Particles?.Clear();
        Free();
    }

    /// <summary>
    /// The zone engine started on another map set, for a cut-scene's stage (<c>CutScene.LoadStage</c>, <c>0x80051984</c>)
    /// or a battle's arena (<c>Combat.Init</c>), once this one's world is freed. Its chunks fill the same collision and drawn
    /// surfaces, and it has lights of its own; its rain falls about its own camera and lands on what its centre chunk draws
    /// (<c>Zones.PlayerMap</c>). Its audio, chunk sounds, audio keys and weather's sounds are this one's.
    /// </summary>
    public ZoneEngine Stage(MapSetRecord set)
    {
        var chunks = new ChunkStreamer(Game.Plugins, Game, World, set, Visible);
        return new ZoneEngine(Game, chunks, Particles, _shared, () => chunks.Centre);
    }

    /// <summary>What carries on from one stage to the next.</summary>
    private sealed class Shared(GameState game, GameAudio audio)
    {
        public GameAudio Audio { get; } = audio;

        public MapSounds Sounds { get; } = new(audio, game.Random.Cosmetic, o => MapObjectPresence.There(o, f => game.Flags.IsSet(f)));

        public AudioKeys Keys { get; } = new(audio, game.Sounds, game.Random.Cosmetic);

        public WeatherSounds WeatherSounds { get; } = new(audio, game.Sounds);

        public SkyDirector Sky { get; } = new(game.Plugins, game.Clock, game.Weather, game.Random.Twinkle);

        /// <summary>The tick within a frame of the sky, which flashes lightning once a frame.</summary>
        public FrameGate SkyFrame { get; } = new(Cadence.FrameTicks);
    }
}
