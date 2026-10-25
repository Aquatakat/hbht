using System.Numerics;
using HBHT.Core;
using HBHT.Core.Actors;
using HBHT.Core.Calendar;
using HBHT.Core.Cameras;
using HBHT.Core.Chunks;
using HBHT.Core.Items;
using HBHT.Core.MapObjects;
using HBHT.Core.Movement;
using HBHT.Core.Particles;
using HBHT.Core.Physics;
using HBHT.Core.Rng;
using HBHT.Core.Scripts;
using HBHT.Core.Trek;
using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.Rom.Tests;

/// <summary>
/// Trek's loop, headless, on NA 1.0's data and a new game: the chunks around the player and their
/// wanderers, a door's fade, the clock under a conversation, chunk crossings and the fade into a cut-scene.
/// </summary>
[Collection(ImportedPluginCollection.Name)]
public sealed class TrekLoopTests(Na10Import fixture)
{
    private static readonly RecordId Overworld = RecordId.Parse("aidyn:mapset/0");

    private Na10Import.Loaded Data => fixture.Data;

    private sealed class Host : ITrekHost
    {
        public int Arrivals { get; set; }
        public int ConversationChanges { get; set; }

        void ITrekHost.Arrived() => Arrivals++;

        void ITrekHost.ConversationChanged() => ConversationChanges++;

        public List<Transition> Transitions { get; } = [];

        void ITransitionHost.Transition(Transition next) => Transitions.Add(next);

        void ITrekHost.Warn(string text)
        {
        }

        IRootMotion ITrekHost.Motion(Wanderer wanderer) => IActorMotion.Still;

        void ITrekHost.Animate(LoadedMap map, SceneObject actor, int request)
        {
        }

        void ITrekHost.Changed(LoadedMap map, SceneObject scene)
        {
        }

        /// <summary>What trek put out, in order.</summary>
        public List<(LoadedMap Map, SceneObject Scene)> PutOut { get; } = [];

        void ITrekHost.PutOut(LoadedMap map, SceneObject scene) => PutOut.Add((map, scene));

        void ITrekHost.Log(string text)
        {
        }

        void IScriptHost.Cue(ScriptCue cue)
        {
        }
    }

    /// <summary>Trek as a new game starts it, with the player on the ground at a chunk's point.</summary>
    private (TrekLoop Trek, Host Host) Start(string chunkName = "I08", float x = 16, float z = 62, int mapSet = 0, ParticleHead? particles = null,
        Action<GameState>? prepare = null)
    {
        var game = GameState.NewGame(Data.Set);
        prepare?.Invoke(game);
        var host = new Host();
        var set = Data.Set.Get<MapSetRecord>(RecordId.Decimal(RecordTypes.MapSet, mapSet));
        var streamer = new ChunkStreamer(Data.Set, game, new CollisionWorld(), set, new VisibleWorld());
        var start = GameStart.OfNewGame(Data.Set) with { Set = set, Chunk = chunkName, Local = new Vector3(x, 10, z) };
        var trek = GameSession.Start(game, host, new ZoneEngine(game, streamer, particles: particles), start).Trek;
        return (trek, host);
    }

    private static PlayerInput Still() => new(Vector2.Zero);

    [Fact]
    public void RainOverGwerniaLandsOnTheSurfacesTheChunksDraw()
    {
        var particles = new ParticleHead(new Lcg());
        var (trek, _) = Start("M07", 40, 55, particles: particles);
        trek.Game.Weather!.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.5f);
        for (var i = 0; i < 600; i++)
        {
            trek.Tick(Still);
            particles.Step();
        }
        var drops = trek.Zone.Precipitation!.Drops!;
        var splashes = particles.Sprites.Where(s => s.Emitter == trek.Zone.Precipitation.Splashes!.Slot).ToList();
        Assert.InRange(particles.Sprites.Count(s => s.Emitter == drops.Slot), 10, 40);
        Assert.InRange(splashes.Count, 20, 80);
        // Each splash lies 0.05 over a surface the chunks draw, laid flat on it, which faces within 60° of up.
        foreach (var s in splashes)
        {
            Assert.True(trek.Chunks.Visible!.Raycast(s.Position + 0.3f * Vector3.UnitY, s.Position - 0.3f * Vector3.UnitY, out var ground, out var normal, out var absorbs));
            Assert.False(absorbs);
            Assert.Equal(WeatherParticles.SplashLift, Vector3.Dot(s.Position - ground, normal), 3);
            var up = -Vector3.Transform(Vector3.UnitZ, s.Orientation!.Value);
            Assert.True(Vector3.Dot(up, normal) > 0.99f);
            Assert.True(normal.Y >= WeatherParticles.SplashSlope);
        }

        // Through a door into an interior, whose sky isn't drawn: the pair is forgotten and killed, none is made there,
        // and the weather's one decision says so; the rain loop stops there.
        var weather = trek.Game.Weather;
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        var door = map.Objects.OfType<TeleporterObject>().First(t => t.LockLevel == 0 && t.TrapLevel == 0 && t.SecretFlag is null
            && TeleportArrival.For(Data.Set, t) is { } a && a.Set.Id != Overworld
            && Data.Set.Get<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, a.Chunk.Terrain)) is { SkyDrawn: false });
        ((IMapObjectHost)trek).Teleport(map, door);
        for (var i = 0; i < 120; i++)
        {
            trek.Tick(Still);
            particles.Step();
            Assert.Equal(weather.Emitting, trek.Zone.Precipitation.Drops is not null);
        }
        Assert.False(weather.Emitting || weather.RainLoop);
        Assert.Equal(0, particles.EmittersInUse);
    }

    /// <summary>
    /// I08's path light 90, lit once Oriana's conversation sets flag 50. Walking north at 104 instead treads on a trigger
    /// that cues a cut-scene.
    /// </summary>
    private SceneObject PathLight => Assert.IsType<SceneObject>(Data.Map("aidyn:map/0x0FDB").Objects.Single(o => o.Index == 90));

    /// <summary>Trek in I08 with the path lights lit and the player <paramref name="south"/> south of light 90, facing it.</summary>
    private (TrekLoop Trek, Host Host, LoadedMap Map) StartByPathLight(float south)
    {
        var light = PathLight;
        var (trek, host) = Start("I08", light.Position.X, light.Position.Z + south, prepare: game => game.Flags.Set(Flag(50), true));
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        Assert.Equal(RecordId.Hex(RecordTypes.Map, 0x0FDB), map.Record.Id);
        Assert.True(map.IsActive(light.Index));
        return (trek, host, map);
    }

    [Fact]
    public void APathLightGoesOutInTreksTickAsThePlayerWalksUpToItOnTheSameTickEveryTime()
    {
        var light = PathLight;
        (int Tick, Vector3 At) Walk()
        {
            var (trek, host, map) = StartByPathLight(8);
            trek.PlayerAnimation = new SteadyMotion(0.1f);
            var at = map.Origin + light.Position;
            // The first tick tells of the new journal entry, which holds the player still till it's closed. A frame passes.
            trek.Tick(Still);
            trek.Use.Accept();
            trek.Tick(Still);
            Assert.Null(trek.Use.Popup);
            for (var tick = 0; tick < 600; tick++)
            {
                Assert.Equal(TrekStep.Ran, trek.Tick(() => new PlayerInput(new Vector2(0, 1))));
                if (host.PutOut.Count == 0)
                    continue;
                // Within 3 of it, on a tick that ends a frame, and remembered under the player's chunk for 16 map loads.
                var (outMap, scene) = Assert.Single(host.PutOut);
                Assert.Same(map, outMap);
                Assert.Equal(light.Index, scene.Index);
                Assert.False(map.IsActive(light.Index));
                Assert.InRange(Vector3.Distance(trek.Player.Sphere.Position, at), 2, 3);
                Assert.Equal(0, (tick + 1) % Cadence.FrameTicks);
                var memory = Assert.Single(trek.Game.Memory.Entries, e => e.Key.Kind == MemoryKind.PutOut);
                Assert.Equal((MapRecord.VoxelId(map.Record.Id, light.Index), MapObjectMemory.PutOutLoads), (memory.Key.MapObject, memory.Lifetime));

                // Walking on past it doesn't bring it back.
                var position = trek.Player.Sphere.Position;
                for (var i = 0; i < 60; i++)
                    trek.Tick(() => new PlayerInput(new Vector2(0, 1)));
                Assert.False(map.IsActive(light.Index));
                Assert.Single(host.PutOut);
                return (tick, position);
            }
            Assert.Fail($"The light never went out: the player got to {trek.Player.Sphere.Position}, the light's at {at}.");
            return default;
        }

        // Nothing the runtime draws at goes into it: the same steps put it out on the same tick, at the same place.
        Assert.Equal(Walk(), Walk());
    }

    [Fact]
    public void APathLightStaysLitWhileTheFreeCameraFlies()
    {
        var (trek, host, map) = StartByPathLight(2);
        for (var i = 0; i < 10; i++)
            Assert.Equal(TrekStep.Frozen, trek.Tick(Still, frozen: true));
        Assert.Empty(host.PutOut);
        Assert.True(map.IsActive(PathLight.Index));

        // The frame it lands, within 3 of it and looking at it, it goes out.
        for (var i = 0; i < Cadence.FrameTicks; i++)
            trek.Tick(Still);
        Assert.Equal(PathLight.Index, Assert.Single(host.PutOut).Scene.Index);
    }

    /// <summary>How many chunks the map set has in the 3×3 around one: fewer at its edges.</summary>
    private static int Around(ChunkStreamer chunks, ChunkEntry chunk) =>
        chunks.MapSet.Chunks.Count(c => Math.Abs(c.Row - chunk.Row) <= 1 && Math.Abs(c.Column - chunk.Column) <= 1);

    private static PlayerInput NotRead() => throw new InvalidOperationException("Trek read the player's input.");

    [Fact]
    public void TheChunksAroundThePlayerLoadWithTheirWanderersAndGoWithThem()
    {
        var (trek, _) = Start();
        var chunks = trek.Chunks;

        // I08 is at the overworld's edge.
        Assert.Equal(Around(chunks, chunks.Find("I08")!), chunks.All.Count());
        Assert.InRange(chunks.All.Count(), 4, 8);
        Assert.NotEmpty(trek.Wanderers.All);
        Assert.True(chunks.World.Count > 0);
        Assert.All(trek.Wanderers.All, w => Assert.Contains(chunks.All, c => c.Map == w.Map));

        // Far off, every chunk the 3×3 left is gone, and its wanderers with it.
        var before = chunks.All.Select(c => c.Map).ToList();
        var i08 = chunks.Find("I08")!;
        var far = chunks.MapSet.Chunks.MaxBy(c => Math.Abs(c.Row - i08.Row) + Math.Abs(c.Column - i08.Column))!;
        chunks.Update(chunks.Origin(far) + new Vector3(50, 0, 50));
        Assert.DoesNotContain(chunks.All, c => before.Contains(c.Map));
        Assert.All(trek.Wanderers.All, w => Assert.Contains(chunks.All, c => c.Map == w.Map));
    }

    [Fact]
    public void ADoorFadesOutHoldsBlackThenTakesThePlayerThroughAndFadesIn()
    {
        var (trek, host) = Start("M07", 40, 55);
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        // A door in Gwernia to another map set, with nothing to stop it.
        var door = map.Objects.OfType<TeleporterObject>().First(t => t.LockLevel == 0 && t.TrapLevel == 0 && t.SecretFlag is null
            && TeleportArrival.For(Data.Set, t) is { } a && a.Set.Id != Overworld);
        var arrival = TeleportArrival.For(Data.Set, door)!;
        var memory = trek.Game.Memory.Remember(new Memory(MapRecord.VoxelId(map.Record.Id, door.Index), MemoryKind.Opened), 0);
        var time = trek.Game.Clock.Time;

        ((IMapObjectHost)trek).Teleport(map, door);
        Assert.True(trek.Teleporting);

        // Out at 2/3 a tick, then three frames at black, with the player's input dead throughout.
        var brightness = new List<float>();
        for (var i = 0; i < 8; i++)
        {
            Assert.Equal(TrekStep.Ran, trek.Tick(NotRead));
            brightness.Add(trek.Brightness);
            if (i < 7)
                Assert.Equal(Overworld, trek.Chunks.MapSet.Id);
        }
        Assert.Equal(1 - TrekLoop.TeleportFadeOutPerTick, brightness[0]);
        Assert.All(brightness.Skip(1), b => Assert.Equal(0, b));
        Assert.Equal(arrival.Set.Id, trek.Chunks.MapSet.Id);
        Assert.False(trek.Teleporting);
        Assert.Equal(1, host.Arrivals);
        Assert.Equal(arrival.Chunk, trek.Chunks.ChunkAt(trek.Player.Sphere.Position));
        Assert.Equal(MapObjectMemory.Long - 1, memory.Lifetime);
        // A fade passes no less time than any other tick.
        Assert.NotEqual(time, trek.Game.Clock.Time);

        // In at 1/15 a tick, without input until the tick it's done.
        for (var i = 0; i < 14; i++)
            trek.Tick(NotRead);
        Assert.True(trek.TeleportFade.Fading);
        trek.Tick(Still);
        Assert.False(trek.TeleportFade.Fading);
        Assert.Equal(1, trek.Brightness);
    }

    [Fact]
    public void ASignStopsTheClockAndTheWanderersButNotTheWorld()
    {
        var (trek, host) = Start();
        var (_, record, sign) = Data.Of<DialogueObject>()
            .First(d => Data.Set.TryGet<DialogueRecord>(d.Object.Script) is { Mode: DialogueMode.Sign });
        var map = LoadedMap.Activate(record, Worlds.Chunk(), trek.Game.Flags, trek.Game.Memory, trek.Game.Party, new Lcg());

        ((IMapObjectHost)trek).StartScript(map, sign);
        Assert.NotNull(trek.Conversation);
        Assert.True(host.ConversationChanges > 0);

        var time = trek.Game.Clock.Time;
        var timers = trek.Wanderers.All.Select(w => w.Timer).ToList();
        for (var i = 0; i < 10; i++)
            Assert.Equal(TrekStep.Ran, trek.Tick(NotRead));
        Assert.Equal(time, trek.Game.Clock.Time);
        Assert.Equal(timers, trek.Wanderers.All.Select(w => w.Timer));
    }

    [Fact]
    public void CrossingIntoAnotherChunkAgesWhatMapObjectsRemember()
    {
        var (trek, _) = Start();
        var memory = trek.Game.Memory.Remember(new Memory(MapRecord.VoxelId(RecordId.Hex(RecordTypes.Map, 0x0FB6), 0), MemoryKind.Opened), 0);
        trek.Tick(Still);
        Assert.Equal(MapObjectMemory.Long, memory.Lifetime);

        var next = trek.Chunks.Find("I09")!;
        trek.Player.Sphere.Position = trek.Chunks.Origin(next) + new Vector3(16, trek.Player.Sphere.Position.Y, 2);
        trek.Tick(Still);
        Assert.Equal(MapObjectMemory.Long - 1, memory.Lifetime);
        trek.Tick(Still);
        Assert.Equal(MapObjectMemory.Long - 1, memory.Lifetime);
    }

    [Fact]
    public void ACueFadesTrekOutThenItsWorldGoesForTheCutSceneAndComesBackAfter()
    {
        var (trek, _) = Start();
        var (id, map, dialogue) = Data.Of<DialogueObject>()
            .First(d => Data.Set.TryGet<DialogueRecord>(d.Object.Script) is { Mode: DialogueMode.Cutscene } && d.Object.Stage is not null);
        var script = Data.Set.Get<DialogueRecord>(dialogue.Script);

        trek.Cue(script, new ScriptStart(dialogue.RefPointBase, id), dialogue.Stage!);
        var ticks = 0;
        while (trek.Tick(NotRead) == TrekStep.Ran)
            Assert.InRange(++ticks, 1, 60);
        // 1/30 a tick down to black, then three frames there; the tick the fade ends, the cut-scene takes over.
        Assert.InRange(ticks, 30 + 6 - 1, 31 + 6 - 1);
        Assert.Equal(0, trek.Brightness);

        var cued = trek.BeginCutScene();
        Assert.Equal(script, cued.Script);
        Assert.Null(trek.Cued);
        Assert.Empty(trek.Chunks.All);
        Assert.Equal(0, trek.Chunks.World.Count);
        Assert.Empty(trek.Wanderers.All);

        trek.Resume();
        Assert.Equal(Around(trek.Chunks, trek.Chunks.Find("I08")!), trek.Chunks.All.Count());
        Assert.NotEmpty(trek.Wanderers.All);
        Assert.True(trek.CutSceneFade.Fading);
        Assert.Equal(0, trek.Brightness);
    }

    [Fact]
    public void TheRainFallsOnThroughACutSceneAndTreksWorldBuiltAgain()
    {
        var particles = new ParticleHead(new Lcg());
        var (trek, _) = Start("M07", 40, 55, particles: particles);
        var weather = trek.Game.Weather!;
        weather.Force(WeatherKind.Precipitation, Precipitation.Rain, 0.9f);
        weather.FallIntensity = 0.05f;
        for (var i = 0; i < 60; i++)
        {
            trek.Tick(Still);
            particles.Step();
        }
        Assert.True(trek.Zone.WeatherSounds.RainPlaying && weather.SkyOverridden && weather.Emitting);
        var loops = weather.RainLoopStarts;
        var (id, _, dialogue) = Data.Of<DialogueObject>()
            .First(d => Data.Set.TryGet<DialogueRecord>(d.Object.Script) is { Mode: DialogueMode.Cutscene } && d.Object.Stage is not null);
        trek.Cue(Data.Set.Get<DialogueRecord>(dialogue.Script), new ScriptStart(dialogue.RefPointBase, id), dialogue.Stage!);
        while (trek.Tick(Still) == TrekStep.Ran)
            particles.Step();
        var before = weather.FallIntensity;

        // Trek's world goes for the stage: the pair with it, but what falls, the rain sky and the loop carry on.
        trek.BeginCutScene();
        Assert.Equal(0, particles.EmittersInUse);
        Assert.False(weather.Emitting);
        Assert.True(weather.WantsRainSky && weather.SkyOverridden && trek.Zone.WeatherSounds.RainPlaying);
        Assert.Equal(before, weather.FallIntensity);
        // The stage's ticks, under an outdoor sky.
        for (var i = 0; i < 30; i++)
            trek.Zone.StepWeather(true, null);
        Assert.Equal(before + 30f / 300, weather.FallIntensity, 1e-5f);

        // And back in trek, a new pair about the player, the loop never restarted.
        trek.Resume();
        Assert.True(weather.WantsRainSky && trek.Zone.WeatherSounds.RainPlaying);
        trek.Tick(Still);
        Assert.True(weather.Emitting);
        Assert.NotNull(trek.Zone.Precipitation!.Drops);
        Assert.Equal(loops, weather.RainLoopStarts);
        Assert.True(weather.FallIntensity > before + 30f / 300);
    }

    [Fact]
    public void TheMinimapShowsThePlayersPieceLargeWithThePortraitsUpAndSmallWhenHeMoves()
    {
        // Gwernia castle's M05, a corridor (image 0x97, 41 × 12, at (10, 49) in its plan), where the user's captures stood.
        var (trek, _) = Start("M05", 50, 50, mapSet: 4);
        for (var i = 0; i < 4; i++)
            trek.Tick(Still);
        var minimap = trek.Hud.Minimap;
        Assert.True(minimap.Showing);
        Assert.Equal(RecordId.Hex(RecordTypes.Image, 0x97), minimap.Record!.Sections[minimap.Section!.Value].Image);
        Assert.True(trek.Game.Minimaps.IsSeen(minimap.Record, minimap.Section.Value));
        // Seeing it is saved, and the pieces seen from the start aren't.
        var save = trek.Game.Save();
        Assert.Equal([minimap.Section.Value], save.Minimaps.Single(m => m.Minimap == minimap.Record.Id).Seen.Except(
            minimap.Record.Sections.Select((s, i) => (s, i)).Where(p => p.s.Seen).Select(p => p.i)));
        Assert.True(GameState.Load(Data.Set, HBHT.Plugins.Saves.SaveFile.Parse(HBHT.Plugins.Saves.SaveFile.Serialize(save))).Minimaps
            .IsSeen(minimap.Record, minimap.Section.Value));
        Assert.Equal((187f, 101f), Piece());

        // Moving lowers the portraits and shrinks the minimap over 14 frames, the piece centred in the small box.
        trek.Tick(() => new PlayerInput(new Vector2(0, 0.3f)));
        for (var i = 0; i < 2 * 14 + 2; i++)
            trek.Tick(Still);
        Assert.False(trek.Hud.Portraits.Shown);
        Assert.Equal((247f, 187f), Piece());

        // B raises them again.
        trek.PressB();
        Assert.True(trek.Hud.Portraits.Shown && trek.Hud.Minimap.Large);

        (float, float) Piece() => trek.Hud.Draw(trek.Game, false, Vector2.UnitY, 1)
            .Where(s => s.Image == RecordId.Hex(RecordTypes.Image, 0x97)).Select(s => (s.X, s.Y)).Single();
    }

    [Fact]
    public void TheCompassShowsOutdoorsAndNoMinimapLoadsThere()
    {
        var (trek, _) = Start();
        for (var i = 0; i < 4; i++)
            trek.Tick(Still);
        Assert.Null(trek.Hud.Minimap.Record);
        var sprites = trek.Hud.Draw(trek.Game, true, new Vector2(0, -1), 1).ToList();
        Assert.Contains(sprites, s => s.Image == HBHT.Core.Ui.Compass.North);
        Assert.Contains(sprites, s => s.Image == HBHT.Core.Ui.Sundial.Rays);
        Assert.Contains(sprites, s => s.Image == HBHT.Core.Ui.Sundial.SunGlow);

        // Walking into a cave hides the sun and moon, and out shows them. Trek starting in a cave, as on loading there,
        // leaves them hidden, where the original showed them until the player left and came back.
        trek.Hud.Arrive(false);
        Assert.False(trek.Hud.SunAndMoon);
        trek.Hud.Start();
        Assert.False(trek.Hud.SunAndMoon);
        trek.Hud.Arrive(true);
        Assert.True(trek.Hud.SunAndMoon);

        // On a 16:9 screen of 360 lines, the sundial keeps its place from the bottom left and the compass's ring from
        // the bottom right; the portraits' frames stay centred.
        var wide = trek.Hud.Draw(trek.Game, true, new Vector2(0, -1), 1, 640, 360).ToList();
        Assert.Equal((19f, 289f), wide.Where(s => s.Image == HBHT.Core.Ui.Sundial.Ring).Select(s => (s.X, s.Y)).First());
        Assert.Equal((561f, 289f), wide.Where(s => s.Image == HBHT.Core.Ui.Sundial.Ring).Select(s => (s.X, s.Y)).Last());
        for (var i = 0; i < 2 * 14; i++)
            trek.Tick(Still);
        Assert.Equal(244f, trek.Hud.Draw(trek.Game, true, new Vector2(0, -1), 1, 640, 360).First(s => s.Image == HBHT.Core.Ui.PartyPortraits.Frame).X);
    }

    /// <summary>
    /// The Jundar gate in P24 (object 1, a ChangeCollision trigger on flag 480, which the riddle's cut-scene sets) holds the
    /// player back until the riddle is solved, then lets him walk through to the desert: O24's copy of the gate's wall, where
    /// the chunks overlap, opens with P24's toggles.
    /// </summary>
    [Fact]
    public void TheJundarGateLetsThePlayerThroughOnceItsRiddleIsSolved()
    {
        var (trek, _) = Start("P24", 3.9f, 70f);
        trek.PlayerAnimation = new SteadyMotion(0.1f);
        var map = trek.Chunks.MapAt(trek.Player.Sphere.Position)!;
        Assert.Equal(RecordId.Hex(RecordTypes.Map, 0x1123), map.Record.Id);
        float Walk(int ticks)
        {
            // Down on the stick, away from the camera behind him, is south through the gate.
            for (var i = 0; i < ticks; i++)
            {
                if (trek.Use.Popup is not null)
                    trek.Use.Accept();
                trek.Tick(() => new PlayerInput(new Vector2(0, -1)));
            }
            return (trek.Player.Sphere.Position - map.Origin).Z;
        }

        // The gate's walls are at z 74.1 and 75.4; the passage runs between them to the desert at 76.8.
        Assert.InRange(Walk(300), 73.5f, 74);
        trek.Game.Flags.Set(Flag(480), true);
        Assert.True(Walk(120) > 77);
    }
}
