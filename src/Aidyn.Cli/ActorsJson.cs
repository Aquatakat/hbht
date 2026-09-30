using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aidyn.Rom;
using Aidyn.Rom.Borg;
using Aidyn.Rom.Db;
using Aidyn.Rom.Engine;
using Aidyn.Rom.Graphics;

/// <summary>
/// Every Borg7 actor as one JSON file: its scene, its states
/// and branches, and the entities that use it.
/// </summary>
internal static class ActorsJson
{
    public static byte[] Serialize(RomImage rom, BorgArchive archive)
    {
        var db = GameDb.Read(rom);
        var models = EntityModels.Read(rom);
        var names = db.Entities.ToDictionary(e => e.Id, e => e.Name);
        var dialogNames = db.DialogEntities.ToDictionary(e => e.Id, e => e.Name);
        var users = models.Entities.Select(m => (m.Actor, Id: m.Entity, Name: names[m.Entity], Kind: "entity"))
            .Concat(models.DialogEntities.Select(m => (m.Actor, Id: m.Entity, Name: dialogNames[m.Entity], Kind: "dialogEntity")))
            .ToLookup(u => u.Actor);

        var lengths = new Dictionary<int, Borg6Animation>();
        Borg6Animation Animation(int index) =>
            lengths.TryGetValue(index, out var a) ? a : lengths[index] = Borg6Animation.Parse(archive.Read(index));

        var actors = new JsonArray();
        foreach (var listing in archive.Listings.Where(l => l.Type == BorgType.Actor))
        {
            var actor = Borg7Actor.Parse(archive.Read(listing.Index));
            actors.Add(new JsonObject
            {
                ["borg7"] = listing.Index,
                ["scene"] = Animation(actor.States[0].Animation).Scene,
                ["rootTransform"] = actor.RootTransform,
                ["initialState"] = actor.InitialState,
                ["usedBy"] = new JsonArray(users[listing.Index].Select(u => (JsonNode)new JsonObject
                {
                    ["id"] = u.Id.ToString(),
                    ["name"] = u.Name,
                    ["kind"] = u.Kind,
                }).ToArray()),
                ["states"] = new JsonArray(actor.States.Select(s => (JsonNode)new JsonObject
                {
                    ["borg6"] = s.Animation,
                    ["length"] = Animation(s.Animation).Length,
                    ["initial"] = s.Initial,
                    ["branches"] = new JsonArray(s.Branches.Select(b => (JsonNode)new JsonObject
                    {
                        ["request"] = b.Request == Borg7Branch.AnyRequest ? "any" : b.Request,
                        ["chance"] = b.Chance,
                        ["interrupts"] = b.Interrupts,
                        ["state"] = b.State,
                    }).ToArray()),
                }).ToArray()),
            });
        }

        var root = new JsonObject { ["version"] = rom.Version.Name, ["actors"] = actors };
        return JsonSerializer.SerializeToUtf8Bytes(root, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });
    }
}
