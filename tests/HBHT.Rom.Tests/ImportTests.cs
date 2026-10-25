using HBHT.Plugins;
using HBHT.Plugins.Assets;
using HBHT.Plugins.Records;
using HBHT.Rom.Import;

namespace HBHT.Rom.Tests;

[Collection(ImportedPluginCollection.Name)]
public sealed class ImportTests(Na10Import fixture)
{
    [Fact]
    public void TheBasePluginLoadsAndItsReferencesResolve()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var set = fixture.Data.Set;
        var import = set.Plugins[0].Manifest.Import!;
        Assert.Equal((BasePlugin.ImporterVersion, rom.Sha1), (import.Importer, import.Rom));
        // The manifest records the player's choices, the defaults here.
        Assert.Equal(ImportOptions.Default, import.Options);

        var mapSets = set.All<MapSetRecord>(RecordTypes.MapSet).ToList();
        Assert.Equal(17, mapSets.Count);
        Assert.Equal(543, mapSets.Sum(s => s.Chunks.Count));
        var overworld = set.Get<MapSetRecord>(RecordId.Parse("aidyn:mapset/0"));
        Assert.Equal(("Overworld", 100f), (overworld.Label, overworld.CellSize));
        Assert.Contains(overworld.Chunks, c => c.Name == "I08");

        var sceneObjects = 0;
        foreach (var chunk in mapSets.SelectMany(s => s.Chunks))
        {
            foreach (var scene in new[] { chunk.Scene, chunk.AltScene }.OfType<RecordId>())
                Assert.NotNull(set.FindAsset(scene, "glb"));
            Assert.NotNull(set.FindAsset(chunk.Map, "glb"));
            foreach (var o in set.Get<MapRecord>(chunk.Map).Objects.OfType<SceneObject>())
            {
                sceneObjects++;
                foreach (var model in o.Models)
                {
                    if (model.Model.Type == RecordTypes.Actor)
                        Assert.NotNull(set.FindAsset(set.Get<ActorRecord>(model.Model).Scene, "glb"));
                    else
                        Assert.NotNull(set.FindAsset(model.Model, "glb"));
                }
            }
        }
        Assert.True(sceneObjects > 7000, $"{sceneObjects} scene objects");

        // A chunk's collision reads back as its Borg9's triangles, in the chunk's own coordinates.
        var archive = Borg.BorgArchive.Open(rom);
        var i08 = overworld.Chunks.Single(c => c.Name == "I08");
        var borg9 = Maps.Borg9Map.Parse(archive.Read(Convert.ToInt32(i08.Map.Key, 16)));
        var collision = CollisionMesh.Read(File.ReadAllBytes(set.FindAsset(i08.Map, "glb")!));
        Assert.Equal(borg9.Triangles.Count, collision.Count);
        for (var t = 0; t < collision.Count; t++)
        {
            var (expected, actual) = (borg9.Triangles[t], collision[t]);
            Assert.Equal((borg9.Vertices[expected.A], borg9.Vertices[expected.B], borg9.Vertices[expected.C]), (actual.A, actual.B, actual.C));
            Assert.Equal((int)expected.Surface, actual.Surface);
            Assert.Equal((int)expected.Flags, (int)actual.Flags);
        }
        Assert.Contains(collision, t => t.Flags.HasFlag(CollisionFlags.SeeThrough));

        // Every sequence loads with its samples, every sound map object plays one, and a sample's WAV
        // holds the Borg11's PCM, widened to 16 bits if it was 8.
        var sequences = set.All<SequenceRecord>(RecordTypes.Sequence).ToList();
        Assert.Equal(476, sequences.Count);
        var samples = new Dictionary<RecordId, short[]>();
        foreach (var sequence in sequences)
            Core.Audio.Sequence.Load(set, sequence.Id, samples);
        Assert.Equal(145, samples.Count);
        var sounds = mapSets.SelectMany(m => m.Chunks).Select(c => c.Map).Distinct()
            .SelectMany(m => set.Get<MapRecord>(m).Objects.OfType<SoundObject>()).ToList();
        Assert.Equal(1038, sounds.Count);
        Assert.All(sounds, o => Assert.NotNull(set.TryGet<SequenceRecord>(o.Sound)));
        Assert.Equal(Audio.Borg11Sample.Parse(archive.Read(0x559)).Samples, samples[RecordId.Hex(RecordTypes.Sample, 0x559)]);

        // Every texture is there as PNG, such as the one bodies' shadows use (BORG1_PlayerShadow).
        var textures = archive.Listings.Where(l => l.Type == Borg.BorgType.Texture).ToList();
        Assert.All(textures, l => Assert.NotNull(set.FindAsset(RecordId.Hex(RecordTypes.Texture, l.Index), "png")));
        var shadow = File.ReadAllBytes(set.FindAsset(RecordId.Parse("aidyn:texture/0x0003"), "png")!);
        Assert.Equal(Graphics.Png.Encode(Graphics.Borg1Texture.Parse(archive.Read(3)).Decode()), shadow);

        // Every terrain names its sky gradient, which is there as PNG. I08's is sky 1 (BORG8_SkyGradient0).
        var terrains = set.All<TerrainRecord>(RecordTypes.Terrain).ToList();
        Assert.Equal(28, terrains.Count);
        Assert.All(terrains, t => Assert.NotNull(set.FindAsset(t.Sky, "png")));
        Assert.Equal(RecordId.Hex(RecordTypes.Image, 0x19F), set.Get<TerrainRecord>(RecordId.Decimal(RecordTypes.Terrain, i08.Terrain)).Sky);
        // Every terrain's battles take an arena (BattleSetup.ChooseArena): terrain 20 takes 6-7 or 8-11, terrain 6 arena 18 alone.
        Assert.All(terrains, t => Assert.NotEmpty(t.Arenas!));
        Assert.Equal([new ArenaRange(6, 7), new ArenaRange(8, 11)], set.Get<TerrainRecord>(RecordId.Parse("aidyn:terrain/20")).Arenas!);
        Assert.Equal([new ArenaRange(18, 18)], set.Get<TerrainRecord>(RecordId.Parse("aidyn:terrain/6")).Arenas!);

        // The one sky record, with every image it names there as PNG.
        var sky = set.Get<SkyRecord>(RecordId.Parse("aidyn:sky/0"));
        Assert.Equal((91, 55, 8), (sky.Stars.Count, sky.Clouds.Count, sky.LensFlare.Count));
        Assert.Equal((RecordId.Hex(RecordTypes.Image, 0x1A2), 0f, 90f), (sky.Sun.Image, sky.Sun.Height, sky.Sun.Azimuth));
        Assert.Equal(RecordId.Hex(RecordTypes.Image, 0x198), sky.Moon.Image);
        Assert.All(sky.Stars, s => Assert.Equal(0.19f, s.Scale));
        var phases = sky.MoonPhases;
        RecordId[] skyImages =
        [
            sky.Sun.Image, sky.Moon.Image, .. sky.Stars.Select(s => s.Image), .. sky.CloudImages, .. sky.FlareImages,
            phases.New, phases.WaxingCrescent, phases.WaningCrescent, phases.FirstQuarter, phases.ThirdQuarter, phases.Full,
        ];
        Assert.All(skyImages, id => Assert.NotNull(set.FindAsset(id, "png")));
        Assert.All(sky.Clouds, c => Assert.InRange(c.Image, 0, sky.CloudImages.Count - 1));
        Assert.All(sky.LensFlare, f => Assert.InRange(f.Image, 0, sky.FlareImages.Count - 1));

        // Every flag, and a network that runs on them. 5423 ("5 set, 9 and 47 clear") enables the Shadow
        // after the Kendall cut-scene sets 5.
        var flags = set.All<FlagRecord>(RecordTypes.Flag).ToList();
        Assert.Equal((5627, 626), (flags.Count, flags.Count(f => f.Formula is not null)));
        Assert.Contains(flags, f => f.Label == "Brenna is in the party");
        var network = Core.Flags.FlagNetwork.Load(set);
        Assert.False(network.Get(5423));
        network.Set(5, true);
        Assert.True(network.Get(5423));

        // Journal entry i is added by flag 5556 + i, which turns on with the entry's own flag.
        var journal = set.All<JournalRecord>(RecordTypes.Journal).ToList();
        Assert.Equal(71, journal.Count);
        var first = set.Get<FlagRecord>(RecordId.Parse("aidyn:flag/5556"));
        Assert.Equal((RecordId.Parse("aidyn:journal/0"), journal.Single(j => j.Id.Key == "0").Flag), (first.Journal, first.Formula!.Inputs.Single()));
        network.Set(journal[0].Flag, true);
        Assert.True(network.Get(5556));
        Assert.Equal((RecordId.Parse("aidyn:journal/0"), true), (network.NewestJournalEntry, network.Get(3617)));
        var state = set.Get<StateFlagsRecord>(RecordId.Parse("aidyn:stateflags/0"));
        Assert.Equal(RecordId.Parse("aidyn:flag/2301"), state.TimeOfDay[0]);

        // The World tab's map: eighteen pieces, the barrows' at x 93 and the goblins' at y 165 in the machine code,
        // and six towns. Camping: refused on terrain 19, half the chance of terrain 10's, eight camp scenes.
        var map = set.Get<WorldMapRecord>(RecordId.Parse("aidyn:worldmap/0"));
        Assert.Equal(18, map.Pieces.Count);
        Assert.Equal((93, 165), (map.Pieces.Single(p => p.Flag.Key == "801").X, map.Pieces.Single(p => p.Flag.Key == "803").Y));
        Assert.Equal(6, map.Towns.Count);
        Assert.All(map.Pieces.Select(p => p.Image).Concat(map.Towns.Select(t => t.Image)), i => Assert.NotNull(set.FindAsset(i, "png")));
        Assert.Equal((50f, 0f), (set.Get<TerrainRecord>(RecordId.Parse("aidyn:terrain/10")).Camp, set.Get<TerrainRecord>(RecordId.Parse("aidyn:terrain/19")).Camp));
        var camping = set.Get<CampingRecord>(RecordId.Parse("aidyn:camping/0"));
        Assert.Equal(8, camping.Scenes.Count);
        Assert.Contains(set.Get<MapRecord>(camping.Campsite).Objects, o => o is DialogueObject);

        // The minimaps: eight map sets', every piece's image imported and every rule's piece one of its own.
        var minimaps = set.All<MinimapRecord>(RecordTypes.Minimap).ToList();
        Assert.Equal([4, 6, 7, 9, 10, 11, 12, 14], minimaps.Select(m => int.Parse(m.Id.Key)).Order());
        Assert.All(minimaps.SelectMany(m => m.Sections), s => Assert.NotNull(set.FindAsset(s.Image, "png")));
        Assert.All(minimaps, m => Assert.All(m.Rules, r => Assert.True(r.Section is null || r.Section < m.Sections.Count)));

        // The engine's sounds and the map sets' music: every one a sequence. The Overworld's two lists of chunks with
        // music, thirteen and twenty-two, and five battle pieces.
        var engineSounds = set.Get<SoundsRecord>(RecordId.Parse("aidyn:sounds/0"));
        Assert.Equal((39, 8, 4), (engineSounds.Script.Count, engineSounds.Containers.Count, engineSounds.Explosions.Count));
        Assert.Equal((13, 22), (overworld.Chunks.Count(c => c.Music?.Key == "0x062C"), overworld.Chunks.Count(c => c.Music?.Key == "0x05EA")));
        Assert.Equal("0x062C", overworld.Chunks.Single(c => c.Name == "M23").Music?.Key);
        Assert.Equal(5, set.Get<MapSetRecord>(RecordId.Parse("aidyn:mapset/16")).Music?.Count);
        var music = mapSets.SelectMany(s => (s.Music ?? []).Concat(s.Chunks.Select(c => c.Music).OfType<RecordId>()));
        Assert.All(engineSounds.Script.Concat(engineSounds.Containers).Concat(engineSounds.Explosions).Concat(music)
            .Append(engineSounds.Menu.Cursor).Append(engineSounds.Title), s => Assert.NotNull(set.TryGet<SequenceRecord>(s)));

        // Bodies' sounds: Alaron's eight keys, his hurt voice two sequences, and people's footsteps on grass (surface 4)
        // four, walking at the walk's last frame. The squid makes none.
        var alaron = set.All<EntityRecord>(RecordTypes.Entity).Single(e => e.Name == "Alaron").Sounds!;
        Assert.Equal((2, 8), (alaron.Kind, alaron.Keys.Count));
        Assert.Equal(["0x06C1", "0x06C2"], alaron.Keys.Single(k => k.Request == 7).Sounds.Select(s => s.Key).Distinct().Order());
        var grass = engineSounds.Kinds[2]!.Surfaces[4].Walk!;
        Assert.Equal((65500, 2, "0x0649"), (grass.Frac, grass.Request, grass.Sounds.Select(s => s.Key).Order().First()));
        Assert.Null(engineSounds.Kinds[0]);
        Assert.Contains(set.All<WeaponRecord>(RecordTypes.Weapon), w => w.Sounds is [_, _]);
        Assert.Equal(59, set.All<SpellRecord>(RecordTypes.Spell).Count(s => s.Cast is not null));

        // The scenes' particle emitters, each drawn with a texture of the engine's particle list, and Oriana's orb
        // (scene 11211) started by request 5 and drawn with slot 13.
        var emitterRecords = set.All<EmittersRecord>(RecordTypes.Emitters).ToList();
        Assert.Equal((75, 218), (emitterRecords.Count, emitterRecords.Sum(r => r.Emitters.Count)));
        Assert.All(emitterRecords.SelectMany(r => r.Emitters), e => Assert.NotNull(set.FindAsset(e.Texture!.Value, "png")));
        var orb = set.Get<EmittersRecord>(RecordId.Hex(RecordTypes.Emitters, 11211)).Emitters.Single();
        Assert.Equal((5, 13, 6, 3), (orb.Request, orb.TextureSlot, orb.Burst, orb.Interval));
        Assert.Equal(new Rgba(226, 230, 255, 131), orb.FadeColor);

        // The cinematics, with each shot's planes, which replace its scene camera's.
        var cinematics = set.All<CinematicRecord>(RecordTypes.Cinematic).ToList();
        Assert.Equal([13, 4, 3, 6], cinematics.Select(c => c.Shots.Count));
        Assert.Equal((0.1f, 35f), (cinematics[0].Shots[0].Near, cinematics[0].Shots[0].Far));
        Assert.All(cinematics.SelectMany(c => c.Shots), s => Assert.NotNull(set.TryGet<SequenceRecord>(s.Music)));
        Assert.Equal(["Gabrion's Search", "Shadow Self Merge", "Rooughah Naming", "Coronation of Alaron"], cinematics.Select(c => c.Title));
        Assert.All(cinematics.SelectMany(c => c.Shots), s => Assert.NotNull(set.FindAsset(s.Scene, "glb")));
        // The opening's door hinges are environment-mapped (Borg2 flag 0x1), and keep their texture for it.
        var glb = File.ReadAllBytes(set.FindAsset(RecordId.Hex(RecordTypes.Scene, 0x2218), "glb")!);
        using (var json = System.Text.Json.JsonDocument.Parse(glb.AsMemory(20, BitConverter.ToInt32(glb, 12))))
        {
            var mapped = json.RootElement.GetProperty("materials").EnumerateArray()
                .Where(m => (m.GetProperty("extras").GetProperty("borg2Flags").GetUInt32() & 1) != 0).ToList();
            Assert.NotEmpty(mapped);
            Assert.All(mapped, m => Assert.True(m.GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture", out _)));
        }
        // They're framed for the console's 4:3. The Shadow's merge marks its mirrored Shadow (transform 10, scaled by
        // −1 across), which the runtime culls as the N64 does; the opening's first shot mirrors nothing.
        Assert.All(cinematics, c => Assert.Equal(CinematicRecord.ConsoleAspect, c.Aspect));
        Assert.True(Mirrors(RecordId.Hex(RecordTypes.Scene, 0x24C9)));
        Assert.False(Mirrors(RecordId.Hex(RecordTypes.Scene, 0x2218)));
        // A light's transform's scale tints it, channel by channel: Roog's naming is lit warm and dim.
        var naming = File.ReadAllBytes(set.FindAsset(RecordId.Hex(RecordTypes.Scene, 0x2568), "glb")!);
        using (var json = System.Text.Json.JsonDocument.Parse(naming.AsMemory(20, BitConverter.ToInt32(naming, 12))))
        {
            var light = json.RootElement.GetProperty("nodes").EnumerateArray().Single(n => n.GetProperty("name").GetString() == "light 0");
            Assert.Equal([0.909f, 0.355f, 0.142f], light.GetProperty("scale").EnumerateArray().Select(v => v.GetSingle()));
        }
        bool Mirrors(RecordId scene)
        {
            var bytes = File.ReadAllBytes(set.FindAsset(scene, "glb")!);
            using var doc = System.Text.Json.JsonDocument.Parse(bytes.AsMemory(20, BitConverter.ToInt32(bytes, 12)));
            return doc.RootElement.GetProperty("scenes")[0].GetProperty("extras").TryGetProperty("mirrors", out var m) && m.GetBoolean();
        }
        // The table's times are halved: the opening's first shot's fade to white ends on its animation's last tick.
        var opening = cinematics[0].Shots[0];
        Assert.Equal((1541, RecordId.Hex(RecordTypes.Scene, 0x2218)), (opening.Length, opening.Scene));
        Assert.Equal(new CinematicFade(CinematicFadeColor.White, 1460, 80, 0), opening.Fades.Single());
        Assert.Equal(new CinematicCaption(200, 380, "Yes, your majesty. Right away your majesty!"), opening.Captions[0]);
        Assert.Equal(15, cinematics.SelectMany(c => c.Shots).Count(s => s.Captions.Count > 0));
        // Every caption starts while its shot shows (times 2 to length − 2, the caption clock 1 behind), but the
        // ending's two in its fourth shot, which the original never shows.
        var late = cinematics.SelectMany(c => c.Shots.SelectMany(s => s.Captions.Where(t => t.Start > s.Length - 3))).ToList();
        Assert.Equal(["Friends, here is your King.", "Long live Gwernia!"], late.Select(t => t.Text));

        // The editor's catalog: every record and asset with a summary, and what names what.
        var catalog = PluginCatalog.Build(set);
        Assert.DoesNotContain(catalog.Entries, e => e.Summary.StartsWith('‹'));
        var i08Map = catalog.Find(i08.Map)!;
        Assert.True(i08Map.HasRecord);
        Assert.Equal(Convert.ToInt32(i08.Map.Key, 16), i08Map.Borg);
        Assert.Contains("Overworld I08", i08Map.Summary, StringComparison.Ordinal);
        Assert.NotNull(i08Map.Asset("glb"));
        Assert.Contains(catalog.ReferencesTo(i08.Map), r => r.From == overworld.Id && r.Path.StartsWith("chunks[", StringComparison.Ordinal));
        Assert.Equal("12×37", catalog.Find(RecordId.Hex(RecordTypes.Image, 0x19F))!.Summary);
        Assert.Contains(catalog.ReferencesTo(RecordId.Hex(RecordTypes.Image, 0x19F)), r => r.From.Type == RecordTypes.Terrain);
        Assert.All(textures, l => Assert.NotNull(catalog.Find(RecordId.Hex(RecordTypes.Texture, l.Index))));
        Assert.Equal("derived: how many of 1 set > 0, journal entry 0", catalog.Find(first.Id)!.Summary);
        Assert.Contains(catalog.ReferencesTo(RecordId.Parse("aidyn:flag/5")), r => r.From == RecordId.Parse("aidyn:flag/5423"));

        // Every script, and the entry a conversation makes in the catalog.
        var scripts = set.All<DialogueRecord>(RecordTypes.Dialogue).ToList();
        Assert.Equal(892, scripts.Count);
        var dialogueId = RecordId.Hex(RecordTypes.Dialogue, 475);
        var conversation = set.Get<DialogueRecord>(dialogueId);
        Assert.Equal(DialogueMode.Conversation, conversation.Mode);
        Assert.StartsWith($"Conversation, {conversation.Nodes.Count} nodes: ", catalog.Find(dialogueId)!.Summary, StringComparison.Ordinal);
        Assert.Contains(catalog.ReferencesTo(RecordId.Parse("aidyn:flag/5")), r => r.From.Type == RecordTypes.Dialogue);
    }

    /// <summary>
    /// The mountains round Erromon (the town is the Overworld's B09, C08 and C09, terrain 19) are terrain 5, the one terrain whose
    /// rolls bring snow; the Overworld has no other chunk of it. Rain carried in from elsewhere falls on there until a roll there
    /// (the weather doc).
    /// </summary>
    [Fact]
    public void TheMountainsRoundErromonAreTheOverworldsOnlySnowyTerrain()
    {
        var set = fixture.Data.Set;
        var overworld = set.Get<MapSetRecord>(RecordId.Parse("aidyn:mapset/0"));
        Assert.Equal(["A04", "A05", "A06", "B04", "B05", "B06", "B07", "B08", "C07", "C10"],
            overworld.Chunks.Where(c => c.Terrain == 5).Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.All(new[] { "B09", "C08", "C09" }, name => Assert.Equal(19, overworld.Chunks.Single(c => c.Name == name).Terrain));
        Assert.Equal(["5"], set.All<TerrainRecord>(RecordTypes.Terrain).Where(t => t.Snow).Select(t => t.Id.Key));
    }

    [Fact]
    public void ImportsAreDeterministic()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        // The shared import against a fresh one.
        using var temp = new TempPlugins();
        var first = fixture.Data.Dir;
        var second = Path.Combine(temp.Root, BasePlugin.Folder);
        BaseImporter.Import(rom, second);

        var files = Directory.EnumerateFiles(first, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(first, f)).Order().ToList();
        Assert.Equal(files, Directory.EnumerateFiles(second, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(second, f)).Order());
        foreach (var file in files)
            Assert.True(File.ReadAllBytes(Path.Combine(first, file)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(second, file))), file);
    }

    [Fact]
    public void ResearchRomsAreRefused()
    {
        var rom = TestRoms.Get(RomVersionId.Debug);
        using var temp = new TempPlugins();
        Assert.Throws<UnsupportedRomException>(() => BaseImporter.Import(rom, Path.Combine(temp.Root, "debug")));
    }
}
