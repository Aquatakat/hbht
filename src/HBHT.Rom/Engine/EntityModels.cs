using HBHT.Rom.Db;

namespace HBHT.Rom.Engine;

/// <summary>How an entity looks in the world (<c>gEntityExtras</c>).</summary>
/// <param name="Actor">The Borg7's archive index.</param>
/// <param name="Portrait">The Borg8's archive index.</param>
/// <param name="Strike">
/// The share of an attack's animation at which its blow lands, 0.2 to 0.67 (<c>EntityBody.StrikePoint</c>, <c>0x8006C7FC</c>, which
/// <c>ProcessPlayers</c> reads): see docs/systems/combat.md.
/// </param>
/// <param name="Height">About how tall the model is at a scale of 1, in scene units.</param>
/// <param name="Radius">The collision sphere's radius.</param>
public sealed record EntityModel(ItemId Entity, int Actor, int Portrait, float Strike, float Height, float Radius, float Scale);

/// <summary>How a dialogue entity looks in the world (<c>dailougEnt_info_array</c>).</summary>
public sealed record DialogEntityModel(ItemId Entity, int Actor, int Portrait, float Height, float Scale);

/// <summary>
/// The models and portraits of entities and dialogue entities, read from the code segment
/// (<c>EntityDB::GetBorg7</c> and <c>GetPortrait</c>). Entities without an entry get a bat and a
/// generic portrait; that fallback is code, not data.
/// </summary>
public sealed record EntityModels(IReadOnlyList<EntityModel> Entities, IReadOnlyList<DialogEntityModel> DialogEntities)
{
    private const int EntitySize = 0x1C, DialogEntitySize = 0x14;

    // Each table is keyed by a record's position in the code's list of records + 1, and ends
    // with a key of 0. The lists are in the same order as the DB tables.
    public static EntityModels Read(RomImage rom)
    {
        var entityList = rom.Table(CodeTable.EntityList);
        var dialogEntityList = rom.Table(CodeTable.DialogEntityList);

        var entities = new List<EntityModel>();
        var data = rom.Table(CodeTable.EntityModels);
        for (var at = 0; Be.U16(data, at) is var key and not 0; at += EntitySize)
            entities.Add(new EntityModel(ItemId.Of(DbType.Entity, entityList[key - 1]), (int)Be.U32(data, at + 4),
                (int)Be.U32(data, at + 8), Be.F32(data, at + 12), Be.F32(data, at + 16),
                Be.F32(data, at + 20), Be.F32(data, at + 24)));

        var dialogEntities = new List<DialogEntityModel>();
        data = rom.Table(CodeTable.DialogEntityModels);
        for (var at = 0; Be.U16(data, at) is var key and not 0; at += DialogEntitySize)
            dialogEntities.Add(new DialogEntityModel(ItemId.Of(DbType.DialogEntity, dialogEntityList[key - 1]),
                (int)Be.U32(data, at + 4), (int)Be.U32(data, at + 8), Be.F32(data, at + 12),
                Be.F32(data, at + 16)));

        return new EntityModels(entities, dialogEntities);
    }
}
