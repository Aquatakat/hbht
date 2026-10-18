using System.Globalization;
using Aidyn.Plugins;

namespace Aidyn.TestSupport;

/// <summary>Shorthands for the record IDs tests name over and over. The test projects import them as static usings.</summary>
public static class Ids
{
    /// <summary>Flag <paramref name="n"/>, <c>aidyn:flag/n</c>.</summary>
    public static RecordId Flag(int n) => RecordId.Decimal(RecordTypes.Flag, n);

    /// <summary>Entity <paramref name="n"/>, <c>aidyn:entity/0x…</c>.</summary>
    public static RecordId Entity(int n) => RecordId.Hex(RecordTypes.Entity, n);

    /// <summary>The script in Borg13 archive index <paramref name="borg"/>, <c>aidyn:dialogue/0x…</c>.</summary>
    public static RecordId Script(int borg) => RecordId.Hex(RecordTypes.Dialogue, borg);

    /// <summary>A flag's number.</summary>
    public static int Number(RecordId flag) => int.Parse(flag.Key, CultureInfo.InvariantCulture);
}
