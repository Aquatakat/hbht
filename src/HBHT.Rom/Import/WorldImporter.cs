using HBHT.Plugins;
using HBHT.Plugins.Records;
using HBHT.Rom.Borg;
using HBHT.Rom.Engine;
using HBHT.Rom.Graphics;
using HBHT.Rom.Maps;
using HBHT.Rom.Quests;

namespace HBHT.Rom.Import;

/// <summary>
/// The world's records: the map sets, terrains, weather, the World tab's map, camping and the sky.
/// </summary>
internal static class WorldImporter
{
    public static void Import(ImportContext ctx)
    {
        ctx.Report("Map sets");
        ctx.WriteRecords("mapsets.json", BuildMapSets(ctx.Chunks, ctx.Audio, MinimapImporter.Build(Minimaps.Read(ctx.Rom))));

        ctx.Report("Terrains");
        ctx.WriteRecords("terrains.json", BuildTerrains(ctx.Gradients, CampFactors.Read(ctx.Rom), ctx.Weather, ctx.Db.CampEncounters, ctx.ArenaChunks));
        ctx.WriteRecords("weather.json", new[] { BuildWeather(ctx.Weather, ctx.Gradients, ctx.ParticleSlots) });
        ctx.WriteRecords("worldmap.json", new[] { BuildWorldMap(ctx.Engine, WorldMapTowns.Read(ctx.Rom)) });
        ctx.WriteRecords("camping.json", new[] { BuildCamping(ctx.Engine, ctx.Chunks, ctx.Archive) });
        ctx.WriteRecords("newgame.json", new[] { BuildNewGame(ctx.Db, ctx.Chunks) });
        ctx.WriteRecords("title.json", new[] { BuildTitle(ctx.Rom, ctx.Archive) });

        ctx.Report("Sky");
        ctx.WriteRecords("sky.json", BuildSky(ctx.Rom, ctx.Archive));
    }

    /// <summary>
    /// The map sets, with the music each plays as it's entered (<see cref="AudioFacts.MapMusic"/>), the places the code puts the
    /// player by number (<see cref="MapSets.Spawns"/>) and its minimap, if it has one (<see cref="MinimapImporter"/>). Their chunks are
    /// records of their own (<see cref="BuildCells"/>).
    /// </summary>
    internal static List<MapSetRecord> BuildMapSets(IReadOnlyList<MapChunk> chunks, AudioTables audio, IReadOnlyDictionary<int, MinimapPlan> minimaps) =>
    [
        .. Enum.GetValues<MapSet>().Select(set => new MapSetRecord(
            RecordId.Decimal(RecordTypes.MapSet, (int)set), set.ToString(), Maps.MapSets.CellSize(set),
            AudioFacts.MapMusic(set, audio) is { Count: > 0 } setMusic ? [.. setMusic.Select(m => RecordId.Hex(RecordTypes.Sequence, m))] : null,
            Maps.MapSets.Spawns(set, chunks),
            minimaps.GetValueOrDefault((int)set))),
    ];

    /// <summary>
    /// Every cell's chunk (ADR 0035) as the map set's chunk table has it, without its map objects, which <see cref="MapImporter"/> adds,
    /// with the Borg9 it takes them and its collision from: its scenes, terrain and music, and the world map region its row and column
    /// name (<see cref="EngineTables.Regions"/>; the code's table doesn't say a map set, so every set's chunk there gets it). A chunk whose
    /// scene the zone engine swaps for a lit one while a flag holds (<see cref="ToggledScenes"/>) gets that as its swap, with its own
    /// see-through scene, which the code leaves.
    /// </summary>
    /// <remarks>
    /// The Overworld's chunks have what <see cref="ChunkCuts"/> took out of their scenes: the chunks each <c>shows</c>, and the
    /// fill of each cell other chunks' scenes drew over. A cell with a fill and no chunk gets one, with the Borg9 of the original's
    /// own chunks of sea (<see cref="SeaMap"/>), and the terrain and the Draw Distance of the chunk nearest it that gave to the fill.
    /// Draw Distance's Extended draws the 5×5 round a chunk by <see cref="MapSets.Extended"/>.
    /// </remarks>
    internal static List<(ChunkRecord Chunk, int Borg)> BuildCells(ImportContext ctx) =>
        BuildCells(ctx.Chunks, ctx.Audio, ctx.Engine.Regions, ToggledScenes.Read(ctx.Rom), ctx.OverworldCuts);

    internal static List<(ChunkRecord Chunk, int Borg)> BuildCells(IReadOnlyList<MapChunk> chunks, AudioTables audio,
        IReadOnlyList<RegionChunk> regions, IReadOnlyList<ToggledScene> toggled, ChunkCuts cuts)
    {
        var region = regions.ToDictionary(r => (r.Row, r.Column), r => DbRecordIds.Flag(r.Flag));
        var lit = toggled.ToDictionary(t => t.Scene);
        var extended = MapSets.Extended(chunks);
        var fills = cuts.Fills.Select((f, i) => (Fill: f, Id: FillId(i))).ToDictionary(f => (f.Fill.Row, f.Fill.Column));
        var overworld = chunks.Where(c => c.Set == MapSet.Overworld).ToDictionary(c => (c.Row, c.Column));
        IReadOnlyList<RecordId>? Shows((int Row, int Column) cell) =>
            cuts.Shows.TryGetValue(cell, out var shows) ? [.. shows.Select(s => MapImporter.ChunkId(MapSet.Overworld, s.Row, s.Column))] : null;
        RecordId Set(MapSet set) => RecordId.Decimal(RecordTypes.MapSet, (int)set);
        var cells = chunks.Select(c => (new ChunkRecord(MapImporter.ChunkId(c), Set(c.Set), c.Row, c.Column, TerrainId(c.Terrain), [],
            Borg: Hex(c.Collision),
            Collision: RecordId.Hex(RecordTypes.Collision, c.Collision),
            Scene: c.Scene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.Scene),
            AltScene: c.AltScene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.AltScene),
            Music: AudioFacts.ChunkMusic(c, audio) is { } music ? RecordId.Hex(RecordTypes.Sequence, music) : null,
            Region: region.TryGetValue((c.Row, c.Column), out var flag) ? flag : null,
            Swap: lit.TryGetValue(c.Scene, out var swap)
                ? new SceneSwap(new FlagGate(DbRecordIds.Flag(swap.Flag)), RecordId.Hex(RecordTypes.Scene, swap.Lit),
                    c.AltScene == 0 ? null : RecordId.Hex(RecordTypes.Scene, c.AltScene))
                : null,
            Fill: c.Set == MapSet.Overworld && fills.TryGetValue((c.Row, c.Column), out var fill) ? fill.Id : null,
            Extended: c.Set == MapSet.Overworld && extended.Contains((c.Row, c.Column)),
            Shows: c.Set == MapSet.Overworld ? Shows((c.Row, c.Column)) : null), c.Collision)).ToList();
        cells.AddRange(fills.Values.Where(f => !overworld.ContainsKey((f.Fill.Row, f.Fill.Column)))
            .OrderBy(f => f.Fill.Row).ThenBy(f => f.Fill.Column)
            .Select(f => (new ChunkRecord(MapImporter.ChunkId(MapSet.Overworld, f.Fill.Row, f.Fill.Column), Set(MapSet.Overworld),
                f.Fill.Row, f.Fill.Column, TerrainId(overworld[f.Fill.Nearest].Terrain), [],
                Borg: Hex(SeaMap), Collision: RecordId.Hex(RecordTypes.Collision, SeaMap), Fill: f.Id,
                Extended: extended.Contains(f.Fill.Nearest)), SeaMap)));
        return cells;
    }

    /// <summary>A Borg's number as a chunk's <see cref="ChunkRecord.Borg"/> spells it, <c>0x1436</c>.</summary>
    private static string Hex(int borg) => "0x" + borg.ToString("X4", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A terrain type's record ID.</summary>
    internal static RecordId TerrainId(int terrain) => RecordId.Decimal(RecordTypes.Terrain, terrain);

    /// <summary>The ID of the <paramref name="index"/>th of <see cref="ChunkCuts.Fills"/>.</summary>
    internal static RecordId FillId(int index) => RecordId.Decimal(RecordTypes.Fill, index);

    /// <summary>
    /// The Borg9 the original's own chunks of sea share (L23, S17, S18 and T17 on the Overworld), with no objects, which the chunks
    /// made for fills take too.
    /// </summary>
    internal const int SeaMap = 0x3559;

    /// <summary>
    /// The terrain types: each one's sky and whether it's drawn (a fact, <see cref="Skies.ForTerrain"/>), its camp
    /// ambush factor, its weather from the code's tables, with snow on type 5 alone, a literal in
    /// <c>Weather.Roll</c> (<c>0x8007bdb0</c> in 1.0), the Battle arenas its battles take (a fact, <see cref="Arenas"/>, by the
    /// arenas' chunks, <paramref name="arenas"/>), and who ambushes a camp there, from the camp encounters (<see cref="CampAmbushes"/>).
    /// </summary>
    /// <param name="arenas">The Battle arenas' chunks by the original's numbers (<see cref="Arenas.Chunks"/>), or none to leave them out.</param>
    internal static List<TerrainRecord> BuildTerrains(IReadOnlyList<int> gradients, float[] campFactors, WeatherTables weather,
        IReadOnlyList<Db.CampEncounter>? camp = null, IReadOnlyList<RecordId>? arenas = null) =>
        [.. Enumerable.Range(0, Skies.TerrainCount).Select(t =>
        {
            var (sky, drawn) = Skies.ForTerrain(t);
            return new TerrainRecord(TerrainId(t), RecordId.Hex(RecordTypes.Image, gradients[sky]), drawn, campFactors[t],
                weather.Terrains[t], weather.Scales[t], Snow: t == SnowTerrain,
                Arenas: arenas is null ? null : [.. Arenas.ForTerrain(t).Select(r => (IReadOnlyList<RecordId>)[.. Enumerable.Range(r.Min, r.Max - r.Min + 1).Select(n => arenas[n])])],
                Ambushes: camp is null ? null : CampAmbushes(camp, t));
        })];

    /// <summary>
    /// A terrain's camp ambushes, its five level tiers of the camp encounters (the code reads only the first five of a
    /// terrain's ten slots), each group an enemy with its least and most. The table has rows for terrains 0–26 alone.
    /// </summary>
    internal static IReadOnlyList<CampAmbush>? CampAmbushes(IReadOnlyList<Db.CampEncounter> camp, int terrain)
    {
        var rows = camp.Where(e => e.Terrain == terrain && e.LevelTier < Db.CampEncounter.LevelTiers).OrderBy(e => e.LevelTier).ToList();
        if (rows.Count == 0)
            return null;
        return [.. rows.Select(r => new CampAmbush([.. r.Groups.Where(g => g.Enemy.Value != 0 && g.Max > 0)
            .Select(g => new PartyEnemy(DbRecordIds.Required(g.Enemy, $"terrain {terrain}'s camp ambush"), g.Min, g.Max))]))];
    }

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
    /// (<c>get_ranger_camping_bool</c>, <c>Party::CampAmbushCheck</c>, <c>campfire_func</c>), carried as facts: the Campsite is the first
    /// script object in Borg9 <see cref="CampsiteMap"/>, in the chunk of it.
    /// </summary>
    internal static CampingRecord BuildCamping(EngineTables tables, IReadOnlyList<MapChunk> chunks, BorgArchive archive) => new(
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
        FirstScriptObject(chunks, archive, CampsiteMap),
        AmbushLevels: [5, 10, 15, 20]);

    /// <summary>The Campsite's Borg9, whose first script object camping starts when no camp scene does (<c>campfire_func</c>).</summary>
    internal const int CampsiteMap = 0x185C;

    /// <summary>
    /// The first script object of a Borg9 that one chunk has (the original's dialogue object), as the chunk's map object's ID; a Borg9 that
    /// isn't one chunk's, or that has none, is an <see cref="InvalidDataException"/>.
    /// </summary>
    internal static RecordId FirstScriptObject(IReadOnlyList<MapChunk> chunks, BorgArchive archive, int borg9, int? script = null)
    {
        var chunk = chunks.Where(c => c.Collision == borg9).ToList() is [var only] ? only
            : throw new InvalidDataException($"Borg9 0x{borg9:X4} isn't one chunk's.");
        var found = Borg9Voxels.Read(archive.Read(borg9)).FirstOrDefault(o => o.Data is DialogueData d && (script is null || d.Script == script))
            ?? throw new InvalidDataException($"Borg9 0x{borg9:X4} has no script object{(script is { } s ? $" playing 0x{s:X4}" : "")}.");
        return ChunkRecord.ObjectId(MapImporter.ChunkId(chunk), found.Index);
    }

    /// <summary>
    /// How a new game starts, carried as facts: literals in 1.0's code. The title screen hands the game the Overworld's portal 0
    /// (<c>Title.Leave</c>, <c>0x80055718</c>), which <c>Zones.SetMapSet</c> (<c>0x80026264</c>) turns into chunk I08, row 9 and
    /// column 8 (<c>0x80026374</c>, <c>0x80026378</c>), at (16, 62) (the floats at <c>0x800ce7d8</c> and <c>0x800ce7dc</c>);
    /// <c>InitGameTrek</c> makes the player's body 10 up (<c>0x80021018</c>) and sets it down from on high. The party is Alaron,
    /// the entity at DB position 153, with 1 gold (<c>init_DBs</c>). The arrival is the Overworld's portal 0 in
    /// <see cref="MapSets.Spawns"/>, which the map set record carries too.
    /// </summary>
    internal static NewGameRecord BuildNewGame(Db.GameDb db, IReadOnlyList<MapChunk> chunks)
    {
        var portal = MapSets.Spawns(MapSet.Overworld, chunks)!.Single(p => p.Portal == 0);
        return new(
            RecordId.Decimal(RecordTypes.NewGame, 0),
            MapImporter.ChunkId(MapSet.Overworld, portal.Row, portal.Column),
            portal.Position,
            Height: 10,
            DbRecordIds.For(db.Entities[CharacterImporter.AlaronDbPosition].Id)
                ?? throw new InvalidDataException("The entity DB has no Alaron at position 153."),
            Gold: 1);
    }

    /// <summary>
    /// The title screen's fly-throughs, from the code's table (<see cref="TitleTables"/>): each its Borg6 and that animation's
    /// scene, the Overworld chunk the scenery is loaded round, and the two u16s nothing reads. 1.0 passes over the one at
    /// place <see cref="TitleTables.PakSequence"/> without the Expansion Pak, which the port always plays as with.
    /// </summary>
    internal static TitleRecord BuildTitle(RomImage rom, BorgArchive archive) => new(
        RecordId.Decimal(RecordTypes.Title, 0),
        [.. TitleTables.ReadFlycams(rom).Select(f =>
        {
            var animation = Graphics.Borg6Animation.Parse(archive.Read(f.Animation));
            return new TitleFlycam(RecordId.Hex(RecordTypes.Scene, animation.Scene), RecordId.Hex(RecordTypes.Animation, f.Animation),
                animation.Length, MapImporter.ChunkId(MapSet.Overworld, f.Row, f.Column), MapImporter.ChunkId(MapSet.Overworld, f.OriginRow, f.OriginColumn));
        })]);

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
