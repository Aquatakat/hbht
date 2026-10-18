using Aidyn.Plugins;
using Aidyn.Plugins.Records;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;

namespace Aidyn.Rom.Import;

/// <summary>
/// The world's records: the map sets, terrains, weather, the World tab's map, camping and the sky.
/// </summary>
internal static class WorldImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Map sets");
        ctx.WriteRecords("mapsets.json", BuildMapSets(ctx.Chunks, ctx.Audio));

        ctx.Report("Terrains");
        ctx.WriteRecords("terrains.json", BuildTerrains(ctx.Gradients, CampFactors.Read(ctx.Rom), ctx.Weather));
        ctx.WriteRecords("weather.json", new[] { BuildWeather(ctx.Weather, ctx.Gradients, ctx.ParticleSlots) });
        ctx.WriteRecords("worldmap.json", new[] { BuildWorldMap(ctx.Engine, WorldMapTowns.Read(ctx.Rom)) });
        ctx.WriteRecords("camping.json", new[] { BuildCamping(ctx.Engine) });
        ctx.WriteRecords("newgame.json", new[] { BuildNewGame(ctx.Db) });

        ctx.Report("Sky");
        ctx.WriteRecords("sky.json", BuildSky(ctx.Rom, ctx.Archive));
    }

    /// <summary>The map sets, with the music each plays as it's entered (<see cref="AudioFacts.MapMusic"/>).</summary>
    private static List<MapSetRecord> BuildMapSets(IReadOnlyList<MapChunk> chunks, AudioTables audio) =>
    [
        .. Enum.GetValues<MapSet>().Select(set => new MapSetRecord(
            RecordId.Decimal(RecordTypes.MapSet, (int)set), set.ToString(), Maps.MapSets.CellSize(set),
            [
                .. chunks.Where(c => c.Set == set).Select(c => new ChunkEntry(c.Row, c.Column,
                    c.Scene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.Scene),
                    c.AltScene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.AltScene),
                    RecordId.Hex(RecordTypes.Map, c.Collision), c.Terrain,
                    AudioFacts.ChunkMusic(c, audio) is { } music ? RecordId.Hex(RecordTypes.Sequence, music) : null)),
            ],
            AudioFacts.MapMusic(set, audio) is { Count: > 0 } setMusic ? [.. setMusic.Select(m => RecordId.Hex(RecordTypes.Sequence, m))] : null)),
    ];

    /// <summary>
    /// The terrain types: each one's sky and whether it's drawn (a fact, <see cref="Skies.ForTerrain"/>), its camp
    /// ambush factor, and its weather from the code's tables, with snow on type 5 alone, a literal in
    /// <c>Weather.Roll</c> (<c>0x8007bdb0</c> in 1.0).
    /// </summary>
    internal static List<TerrainRecord> BuildTerrains(IReadOnlyList<int> gradients, float[] campFactors, WeatherTables weather) =>
        [.. Enumerable.Range(0, Skies.TerrainCount).Select(t =>
        {
            var (sky, drawn) = Skies.ForTerrain(t);
            return new TerrainRecord(RecordId.Decimal(RecordTypes.Terrain, t), RecordId.Hex(RecordTypes.Image, gradients[sky]), drawn, campFactors[t],
                weather.Terrains[t], weather.Scales[t], Snow: t == SnowTerrain);
        })];

    /// <summary>The terrain type it snows on rather than rains (<c>Weather.Roll</c>).</summary>
    public const int SnowTerrain = 5;

    /// <summary>
    /// The weather's rules, and how rain and snow are drawn (<c>ProcessWeather</c>, <c>0x8001e7c0</c>): their textures are
    /// particle texture slots, literals in the code (rain's drops slot 4 and splashes 16, snow's 0 for both), looked up in
    /// the engine's particle list (<paramref name="particleTextures"/>); their colours are floats at <c>0x800ce0d0</c> and
    /// <c>0x800ce0e0</c>, and the rest literals, which are checked in 1.0's machine code.
    /// </summary>
    internal static WeatherRecord BuildWeather(WeatherTables tables, IReadOnlyList<int> gradients, IReadOnlyList<int?> particleTextures)
    {
        RecordId? Texture(int slot) =>
            slot < particleTextures.Count && particleTextures[slot] is { } borg1 ? RecordId.Hex(RecordTypes.Texture, borg1) : null;
        return BuildWeather(tables, gradients) with
        {
            Rain = Texture(4) is { } drop && Texture(16) is { } ripple
                ? new PrecipitationLook(drop, ripple, new WeatherColor(0.546875f, 0.7421875f, 0.8203125f, 0.8f), Splashes: true,
                    Fall: 0.075, FallPerIntensity: 0, Interval: 6, IntervalPerIntensity: -4, Life: 240, LifePerIntensity: 0, SplashesSooner: 2)
                : null,
            Snow = Texture(0) is { } flake
                ? new PrecipitationLook(flake, flake, new WeatherColor(1, 1, 1, 1), Splashes: false,
                    Fall: 0.03, FallPerIntensity: 0.02, Interval: 6, IntervalPerIntensity: -7, Life: 360, LifePerIntensity: -60, SplashesSooner: 0)
                : null,
        };
    }

    /// <summary>
    /// The weather's rules: the seasons' chances from the code's table, and the rest literals in <c>Weather.Roll</c>
    /// (<c>0x8007bdb0</c> in 1.0), <c>Weather.DriftWind</c> (<c>0x8007baa0</c>, given 0.05 by <c>World.Update</c>),
    /// <c>ProcessWeather</c> (<c>0x8001e7c0</c>) and <c>RenderSky</c> (<c>0x8001d840</c>), carried as facts, and the
    /// rain sky, sky 2 of <paramref name="gradients"/>, which <c>ProcessWeather</c> names.
    /// </summary>
    internal static WeatherRecord BuildWeather(WeatherTables tables, IReadOnlyList<int> gradients) => new(
        RecordId.Decimal(RecordTypes.Weather, 0),
        tables.Seasons,
        KeepAtMost: 75,
        FallBelow: 70,
        IntensitySpread: 0.15f,
        FallFog: new WeatherRange(0.1f, 0.35f),
        Fog: new WeatherRange(0.1f, 0.75f),
        WindChance: 0.05f,
        WindTurn: 0.1f,
        // −0.004 to 0.001: 0xBB83126F and 0x3A83126F.
        WindRise: new WeatherRange(BitConverter.Int32BitsToSingle(unchecked((int)0xBB83126F)), BitConverter.Int32BitsToSingle(0x3A83126F)),
        WindGust: BitConverter.Int32BitsToSingle(0x3B17B426),
        WindMaxSpeed: BitConverter.Int32BitsToSingle(0x3E6D097B),
        // 1/300 and 1/900 a tick, doubles in the code.
        FallRate: (float)(1.0 / 300),
        FogRate: (float)(1.0 / 900),
        // RenderSky (0x8001d840): every 20th frame, a share under the storm × 0.25 (a double).
        LightningEvery: 20,
        LightningShare: 0.25f,
        RainSky: RecordId.Hex(RecordTypes.Image, gradients[RainSky]));

    /// <summary>The sky rain and snow bring (<c>ProcessWeather</c>, <c>SetBackgroundType</c> with 2).</summary>
    public const int RainSky = 2;

    /// <summary>
    /// The World tab's map: the pieces (<c>worldMapPieces</c>) and the towns' names from the code's tables, and the
    /// party's ring and where it goes, literals in <c>WidgetMenuWorldMap</c> (<c>0x80049234</c> in 1.0), carried as
    /// facts. The original places the ring in any outdoor map set without a minimap; the port only in the Overworld.
    /// </summary>
    internal static WorldMapRecord BuildWorldMap(EngineTables tables, IReadOnlyList<WorldMapTown> towns) => new(
        RecordId.Decimal(RecordTypes.WorldMap, 0),
        [.. tables.WorldMap.Select(p => new MapPiece(DbRecordIds.Flag(p.Flag), RecordId.Hex(RecordTypes.Image, p.Image), p.X, p.Y))],
        [.. towns.Select(t => new MapTown(RecordId.Hex(RecordTypes.Image, t.Image), t.X, t.Y))],
        RecordId.Hex(RecordTypes.Image, 0x00F6),
        RecordId.Decimal(RecordTypes.MapSet, 0),
        new System.Numerics.Vector2(35, 29),
        4,
        new System.Numerics.Vector2(6.15f, 6.68f));

    /// <summary>
    /// Camping's settings: the camp scenes' flags from the code's table, and the rest literals in the code
    /// (<c>get_ranger_camping_bool</c>, <c>Party::CampAmbushCheck</c>, <c>campfire_func</c>), carried as facts.
    /// </summary>
    internal static CampingRecord BuildCamping(EngineTables tables) => new(
        RecordId.Decimal(RecordTypes.Camping, 0),
        "The party cannot camp here.",
        DbRecordIds.Flag(EngineFlags.CampAmbushes),
        AmbushBase: 15,
        RangerCap: 10,
        AmbushScale: 0.0666667f,
        Heal: 0.5f,
        // HOURS(8) ÷ daySpeed: 8 hours of the clock at trek's speed.
        EffectTicks: 8 * 60 * 60 * 60 / 72,
        DbRecordIds.Flag(EngineFlags.CampCheck),
        [.. tables.CampDialogueFlags.Select(f => DbRecordIds.Flag(f))],
        RecordId.Hex(RecordTypes.Map, 0x185C));

    /// <summary>
    /// How a new game starts, carried as facts: literals in 1.0's code. The title screen hands the game the Overworld's portal 0
    /// (<c>Title.Leave</c>, <c>0x80055718</c>), which <c>Zones.SetMapSet</c> (<c>0x80026264</c>) turns into chunk I08, row 9 and
    /// column 8 (<c>0x80026374</c>, <c>0x80026378</c>), at (16, 62) (the floats at <c>0x800ce7d8</c> and <c>0x800ce7dc</c>);
    /// <c>InitGameTrek</c> makes the player's body 10 up (<c>0x80021018</c>) and sets it down from on high. The party is Alaron,
    /// the entity at DB position 153, with 1 gold (<c>init_DBs</c>).
    /// </summary>
    internal static NewGameRecord BuildNewGame(Db.GameDb db) => new(
        RecordId.Decimal(RecordTypes.NewGame, 0),
        new ChunkRef(RecordId.Decimal(RecordTypes.MapSet, (int)MapSet.Overworld), 9, 8),
        new System.Numerics.Vector2(16, 62),
        Height: 10,
        DbRecordIds.For(db.Entities[CharacterImporter.AlaronDbPosition].Id)
            ?? throw new InvalidDataException("The entity DB has no Alaron at position 153."),
        Gold: 1);

    /// <summary>
    /// The sun, moon, stars, clouds and lens flare, with the Borg8 images engine code draws them with
    /// (<see cref="Skies"/> carries those as facts). Every image must be a UI image in the archive.
    /// </summary>
    private static SkyRecord BuildSky(RomImage rom, BorgArchive archive)
    {
        var uiImages = archive.Listings.Where(l => l.Type == BorgType.UiImage).Select(l => l.Index).ToHashSet();
        RecordId Image(int index) => uiImages.Contains(index)
            ? RecordId.Hex(RecordTypes.Image, index)
            : throw new InvalidDataException($"The sky's image 0x{index:X} isn't a Borg8 in {rom.Version.Name}.");

        var objects = Skies.ReadSkyObjects(rom);
        SkyBody Body(SkyObjectEntry o, int image, float? scale = null) =>
            new(Image(image), scale ?? o.Scale, o.Height, o.Azimuth, o.Color);
        var moon = Skies.MoonImages;
        return new SkyRecord(
            RecordId.Decimal(RecordTypes.Sky, 0),
            Body(objects.Single(o => o.Kind == SkyObjectKind.Sun), Skies.SunImage),
            Body(objects.Single(o => o.Kind == SkyObjectKind.Moon), moon.Full),
            new MoonImages(Image(moon.New), Image(moon.WaxingCrescent), Image(moon.WaningCrescent),
                Image(moon.FirstQuarter), Image(moon.ThirdQuarter), Image(moon.Full)),
            [.. objects.Where(o => o.Kind == SkyObjectKind.Star).Select(o => Body(o, Skies.StarImage, Skies.StarScale))],
            [.. Skies.CloudImages.Select(Image)],
            [.. Skies.ReadClouds(rom).Select(c => new Cloud(c.Image, c.X, c.Z, c.Alpha, c.Cover))],
            [.. Skies.FlareImages.Select(Image)],
            [.. Skies.ReadLensFlare(rom).Select(f => new FlarePiece(f.Image, f.Distance, f.Scale, f.Color))]);
    }
}
