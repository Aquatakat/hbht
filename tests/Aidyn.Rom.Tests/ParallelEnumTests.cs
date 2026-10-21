using Aidyn.Plugins.Records;
using Aidyn.Rom.Db;
using Aidyn.Rom.Maps;

namespace Aidyn.Rom.Tests;

/// <summary>
/// The ROM's enums that the importer casts into a record's enum, which keeps the ROM's numbers: each record member
/// must be the ROM member of the same name and value, and the ROM may have only the members named here besides.
/// </summary>
public class ParallelEnumTests
{
    private static void AssertMatches<TRom, TRecord>(params TRom[] romOnly)
        where TRom : struct, Enum where TRecord : struct, Enum
    {
        var rom = Enum.GetValues<TRom>().Except(romOnly).ToDictionary(e => e.ToString(), e => Convert.ToInt64(e));
        var record = Enum.GetValues<TRecord>().ToDictionary(e => e.ToString(), e => Convert.ToInt64(e));
        Assert.Equal(rom.OrderBy(p => p.Key), record.OrderBy(p => p.Key));
    }

    /// <summary>The record leaves out <see cref="RomSceneObjectFlags.Actor"/>, which the importer folds into the model's record type.</summary>
    [Fact]
    public void SceneObjectFlagsAreTheRomsWithoutActor() =>
        AssertMatches<RomSceneObjectFlags, SceneObjectFlags>(RomSceneObjectFlags.Actor);

    /// <summary>The importer leaves a flag with an invalid operation plain.</summary>
    [Fact]
    public void FlagOperationsAreTheRomsWithoutInvalid() =>
        AssertMatches<FlagOp, FlagOperation>(FlagOp.Invalid);

    /// <summary>An item's kind is its record ID's type, which names some kinds differently (Glove, KeyItem).</summary>
    [Fact]
    public void ItemKindsAreDbTypes() =>
        Assert.All(Enum.GetValues<ItemKind>(), k => Assert.True(Enum.IsDefined((DbType)(int)k), $"{k} isn't a DbType."));
}
