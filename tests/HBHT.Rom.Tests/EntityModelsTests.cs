using HBHT.Rom.Borg;
using HBHT.Rom.Db;
using HBHT.Rom.Engine;

namespace HBHT.Rom.Tests;

public class EntityModelsTests
{
    [Theory]
    [MemberData(nameof(TestRoms.Versions), MemberType = typeof(TestRoms))]
    public void ModelsMatchAcrossVersions(RomVersionId id)
    {
        var a = EntityModels.Read(TestRoms.Get(RomVersionId.Na10));
        var b = EntityModels.Read(TestRoms.Get(id));

        Assert.Equal(a.Entities, b.Entities);
        Assert.Equal(a.DialogEntities, b.DialogEntities);
    }

    /// <summary>Every entry names a record, an actor and a portrait, and no record has two.</summary>
    [Fact]
    public void ModelsNameRecordsActorsAndPortraits()
    {
        var rom = TestRoms.Get(RomVersionId.Na10);
        var archive = BorgArchive.Open(rom);
        var db = GameDb.Read(rom);
        var models = EntityModels.Read(rom);

        Assert.Equal((221, 32), (models.Entities.Count, models.DialogEntities.Count));
        var entities = db.Entities.Select(e => e.Id).ToHashSet();
        var dialogEntities = db.DialogEntities.Select(e => e.Id).ToHashSet();
        Assert.All(models.Entities, m => Assert.Contains(m.Entity, entities));
        Assert.All(models.DialogEntities, m => Assert.Contains(m.Entity, dialogEntities));
        Assert.Equal(models.Entities.Count, models.Entities.Select(m => m.Entity).Distinct().Count());
        Assert.Equal(models.DialogEntities.Count, models.DialogEntities.Select(m => m.Entity).Distinct().Count());

        var actors = models.Entities.Select(m => m.Actor).Concat(models.DialogEntities.Select(m => m.Actor)).ToList();
        var portraits = models.Entities.Select(m => m.Portrait).Concat(models.DialogEntities.Select(m => m.Portrait)).ToList();
        Assert.All(actors, a => Assert.Equal(BorgType.Actor, archive.Listings[a].Type));
        Assert.All(portraits, p => Assert.Equal(BorgType.UiImage, archive.Listings[p].Type));
        Assert.Equal(104, actors.Distinct().Count());
        Assert.Equal(db.Entities.Count, models.Entities.Count);

        // Each party member's actor animates the scene its combat model is in.
        var brenna = db.Entities.Single(e => e.Name == "Brenna").Id;
        var actor = Graphics.Borg7Actor.Parse(archive.Read(models.Entities.Single(m => m.Entity == brenna).Actor));
        Assert.Equal(10696, Graphics.Borg6Animation.Parse(archive.Read(actor.States[0].Animation)).Scene);
    }
}
