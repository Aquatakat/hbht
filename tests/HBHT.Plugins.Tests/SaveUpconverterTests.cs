using System.Text.Json.Nodes;
using HBHT.Plugins.Records;
using HBHT.Plugins.Saves;

namespace HBHT.Plugins.Tests;

/// <summary>
/// A format-1 save, the port's before the data format overhaul, is brought up to format 2 as it's read (ADR 0037), rule by rule;
/// what can't be is left out and said, and a format-2 save is read as it is.
/// </summary>
public sealed class SaveUpconverterTests : IDisposable
{
    private readonly TempPlugins _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Chunk(string id, int set, int row, int column, string borg) =>
        $$"""{ "id": "{{id}}", "mapSet": "aidyn:mapset/{{set}}", "row": {{row}}, "column": {{column}}, "terrain": "aidyn:terrain/1", "borg": "{{borg}}", "objects": [] }""";

    // Gwernia C05 alone came from 0x1436; the Overworld's sea cells 0_13, L23 and S17 share 0x3559, as the base's do.
    private PluginSet Plugins() => PluginSet.Load([_temp.Add("aidyn", null,
        ("mapsets.json", """[{ "id": "aidyn:mapset/8", "label": "Gwernia", "cellSize": 100 }, { "id": "aidyn:mapset/0", "label": "Overworld", "cellSize": 100 }]"""),
        ("chunks/8/C05.json", Chunk("aidyn:chunk/8/C05", 8, 3, 5, "0x1436")),
        ("chunks/0/0_13.json", Chunk("aidyn:chunk/0/0_13", 0, 0, 13, "0x3559")),
        ("chunks/0/L23.json", Chunk("aidyn:chunk/0/L23", 0, 12, 23, "0x3559")),
        ("chunks/0/S17.json", Chunk("aidyn:chunk/0/S17", 0, 19, 17, "0x3559")))]);

    private static string Place(string chunk) =>
        $$"""{ "mapSet": "aidyn:mapset/0", "chunk": "{{chunk}}", "position": [1, 2, 3], "heading": [0, 1] }""";

    private static string Memory(string mapObject) => $$"""{ "mapObject": "{{mapObject}}", "kind": "RandomLine", "value": 7, "lifetime": 10 }""";

    private static string Save(string body, int? format = 1) =>
        "{ " + (format is { } f ? $"\"format\": {f}, " : "") + body + " }";

    [Fact]
    public void WeaponsAndArmorAreItemsWhereverTheSaveNamesThem()
    {
        var save = SaveFile.Parse(Save("""
            "party": {
              "members": [{ "entity": "aidyn:entity/0x023F", "trained": { "intelligence": 1, "willpower": 1, "dexterity": 1, "endurance": 1, "strength": 1, "stamina": 1, "level": 1 },
                "current": { "intelligence": 1, "willpower": 1, "dexterity": 1, "endurance": 1, "strength": 1, "stamina": 1, "level": 1 },
                "weapon": { "item": "aidyn:weapon/0x0734", "charges": 3 }, "armor": { "item": "aidyn:armor/0x0507" }, "shield": { "item": "aidyn:armor/0x0612" },
                "gear": [{ "item": "aidyn:item/0x0E01" }] }],
              "inventory": [{ "item": "aidyn:weapon/0x0711", "quantity": 1 }, { "item": "mymod:armor/sallet", "quantity": 2 }, { "item": "aidyn:item/0x0401", "quantity": 3 }]
            }
            """));

        var member = Assert.Single(save.Party.Members);
        Assert.Equal("aidyn:item/0x0734", member.Weapon!.Item.ToString());
        Assert.Equal(3, member.Weapon.Charges);
        Assert.Equal("aidyn:item/0x0507", member.Armor!.Item.ToString());
        Assert.Equal("aidyn:item/0x0612", member.Shield!.Item.ToString());
        Assert.Equal("aidyn:item/0x0E01", Assert.Single(member.Gear).Item.ToString());
        Assert.Equal(["aidyn:item/0x0711", "mymod:item/sallet", "aidyn:item/0x0401"], save.Party.Inventory.Select(s => s.Item.ToString()));
        Assert.Equal(2, save.Format);
        Assert.Equal(1, save.Upconverted!.From);
        Assert.Empty(save.Upconverted!.Dropped);
    }

    [Theory]
    [InlineData("aidyn:dialogentity/0x1404", "aidyn:entity/0x02DD")] // Cadme
    [InlineData("aidyn:dialogentity/0x1406", "aidyn:entity/0x0224")] // Pochanargat
    [InlineData("aidyn:dialogentity/0x1418", "aidyn:entity/0x0214")] // Tamberlain
    [InlineData("aidyn:dialogentity/0x1419", "aidyn:entity/0x02DB")] // Xibid
    [InlineData("aidyn:dialogentity/0x1413", "aidyn:entity/0x1413")] // Mago, who has no twin
    [InlineData("mymod:dialogentity/0x1404", "mymod:entity/0x1404")] // a mod's keeps its key
    [InlineData("aidyn:dialogue/0x0362", "aidyn:script/0x0362")]
    [InlineData("aidyn:minimap/4", "aidyn:mapset/4")]
    [InlineData("aidyn:entity/0x023F", "aidyn:entity/0x023F")]
    [InlineData("aidyn:flag/590", "aidyn:flag/590")]
    [InlineData("not an id", "not an id")]
    public void AnIdIsRenamedByItsType(string old, string renamed) => Assert.Equal(renamed, SaveUpconverter.Rename(old));

    [Fact]
    public void ATwinDialogueEntityIsItsEntityInTheSave()
    {
        var sheet = """{ "entity": "aidyn:dialogentity/0x1419", "trained": { "intelligence": 1, "willpower": 1, "dexterity": 1, "endurance": 1, "strength": 1, "stamina": 1, "level": 1 }, "current": { "intelligence": 1, "willpower": 1, "dexterity": 1, "endurance": 1, "strength": 1, "stamina": 1, "level": 1 } }""";
        var save = SaveFile.Parse(Save($$""" "party": { "kept": [{{sheet}}] } """));
        Assert.Equal("aidyn:entity/0x02DB", Assert.Single(save.Party.Kept).Entity.ToString());
    }

    [Fact]
    public void AMinimapIsItsMapSets()
    {
        var save = SaveFile.Parse(Save(""" "minimaps": [{ "minimap": "aidyn:minimap/10", "seen": [0, 1, 2] }, { "minimap": "aidyn:minimap/4", "seen": [6] }] """));
        Assert.Equal(["aidyn:mapset/10", "aidyn:mapset/4"], save.Minimaps.Select(m => m.MapSet.ToString()));
        Assert.Equal([0, 1, 2], save.Minimaps[0].Seen);
    }

    [Theory]
    [InlineData("I08", "I08")]
    [InlineData("0,13", "0_13")]
    [InlineData("-1,13", "-1_13")]
    public void APlaceOutsideRowsAToZIsSpelledWithAnUnderscore(string old, string name) =>
        Assert.Equal(name, SaveFile.Parse(Save($$""" "place": {{Place(old)}} """)).Place!.Chunk);

    [Fact]
    public void AMapObjectIsInTheOneChunkItsBorg9Became()
    {
        var save = SaveFile.Parse(Save($$""" "place": {{Place("I08")}}, "memories": [{{Memory("aidyn:voxel/0x1436/17")}}] """), Plugins());
        Assert.Equal("aidyn:mapobject/8/C05/17", Assert.Single(save.Memories).MapObject.ToString());
        Assert.Empty(save.Upconverted!.Dropped);
    }

    [Theory]
    [InlineData("0,13", "aidyn:mapobject/0/0_13/4")]
    [InlineData("L23", "aidyn:mapobject/0/L23/4")]
    public void AMapObjectOfASharedBorg9IsInTheChunkTheSaveStandsIn(string place, string mapObject)
    {
        var save = SaveFile.Parse(Save($$""" "place": {{Place(place)}}, "memories": [{{Memory("aidyn:voxel/0x3559/4")}}] """), Plugins());
        Assert.Equal(mapObject, Assert.Single(save.Memories).MapObject.ToString());
    }

    [Theory]
    [InlineData("aidyn:voxel/0x3559/4", "Borg9 0x3559 is 3 chunks', and the save doesn't stand in one of them")]
    [InlineData("aidyn:voxel/0x0E87/0", "no chunk any plugin has came from Borg9 0x0E87")]
    [InlineData("mymod:voxel/isle/2", "it's a mod's, which format 1 named by a map no chunk records")]
    public void AMapObjectThatCantBePlacedIsLeftOutAndSaid(string voxel, string why)
    {
        var save = SaveFile.Parse(Save($$""" "place": {{Place("I08")}}, "memories": [{{Memory(voxel)}}, {{Memory("aidyn:voxel/0x1436/1")}}] """), Plugins());
        Assert.Equal("aidyn:mapobject/8/C05/1", Assert.Single(save.Memories).MapObject.ToString());
        Assert.Equal([$"Its memory of {voxel} (RandomLine) was left out: {why}."], save.Upconverted!.Dropped);
    }

    [Fact]
    public void WithoutPluginsAFormat1SavesMemoriesAreLeftOut()
    {
        var save = SaveFile.Parse(Save($$""" "memories": [{{Memory("aidyn:voxel/0x1436/1")}}] """));
        Assert.Empty(save.Memories);
        Assert.Equal(["Its memory of aidyn:voxel/0x1436/1 (RandomLine) was left out: no plugins were given to find its chunk by."], save.Upconverted!.Dropped);
    }

    [Fact]
    public void ASaveWithNoFormatIsFormat1()
    {
        var save = SaveFile.Parse(Save(""" "party": { "inventory": [{ "item": "aidyn:weapon/0x0711", "quantity": 1 }] } """, format: null));
        Assert.Equal(1, save.Upconverted!.From);
        Assert.Equal("aidyn:item/0x0711", Assert.Single(save.Party.Inventory).Item.ToString());
    }

    [Fact]
    public void AFormat2SaveIsReadAsItIs()
    {
        var save = SaveFile.Parse(Save($$""" "place": {{Place("0_13")}}, "memories": [{{Memory("aidyn:mapobject/8/C05/17")}}] """, format: 2));
        Assert.Null(save.Upconverted);
        Assert.Equal("0_13", save.Place!.Chunk);
        Assert.Equal("aidyn:mapobject/8/C05/17", Assert.Single(save.Memories).MapObject.ToString());
    }

    [Fact]
    public void ANewerFormatIsRefused() =>
        Assert.Throws<InvalidDataException>(() => SaveFile.Parse(Save("", format: SaveGame.CurrentFormat + 1)));

    [Fact]
    public void ASaveIsWrittenAsFormat2AndTheUpconversionIsntSaved()
    {
        var save = SaveFile.Parse(Save(""" "label": "old" """));
        var json = JsonNode.Parse(SaveFile.Serialize(save))!.AsObject();
        Assert.Equal(2, (int)json["format"]!);
        Assert.Null(json["upconverted"]);
    }

    [Fact]
    public void PeekingAChunksBorgReadsNoRecord()
    {
        var plugins = Plugins();
        var chunk = RecordId.Parse("aidyn:chunk/8/C05");
        Assert.Equal("0x1436", plugins.PeekString(chunk, "borg"));
        Assert.False(plugins.IsRead(chunk));
        Assert.Null(plugins.PeekString(chunk, "objects"));
        Assert.Null(plugins.PeekString(RecordId.Parse("aidyn:chunk/8/Z99"), "borg"));
    }
}
