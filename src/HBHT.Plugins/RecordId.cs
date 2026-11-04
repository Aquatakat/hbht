using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace HBHT.Plugins;

/// <summary>
/// A record's ID, <c>&lt;namespace&gt;:&lt;type&gt;/&lt;key&gt;</c> (ADR 0004), such as
/// <c>aidyn:flag/47</c> or <c>mymod:flag/met_the_hermit</c>. Only the canonical spelling parses,
/// so a record has exactly one ID string.
/// </summary>
public readonly record struct RecordId
{
    /// <summary>The base game's namespace, which only the importer writes.</summary>
    public const string BaseNamespace = "aidyn";

    private RecordId(string ns, string type, string key)
    {
        Namespace = ns;
        Type = type;
        Key = key;
    }

    public string Namespace { get; }
    public string Type { get; }

    /// <summary>One or more <c>/</c>-separated segments.</summary>
    public string Key { get; }

    public override string ToString() => $"{Namespace}:{Type}/{Key}";

    /// <summary>
    /// A base-game record keyed by an archive index or a u16 <c>ItemID</c>: <c>0x</c> and four uppercase hex digits
    /// (<see cref="KeyKind.Db"/> and <see cref="KeyKind.Borg"/>). A type keyed otherwise is an <see cref="ArgumentException"/>.
    /// </summary>
    public static RecordId Hex(string type, int value)
    {
        if (value is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Base-game hex keys are u16.");
        var kind = RecordTypes.Info(type).Key;
        if (kind is not (KeyKind.Db or KeyKind.Borg))
            throw new ArgumentException($"A {type} record is keyed by {Spelling(kind)}.", nameof(type));
        return new RecordId(BaseNamespace, type, "0x" + value.ToString("X4", CultureInfo.InvariantCulture));
    }

    /// <summary>A base-game record keyed by a decimal index (<see cref="KeyKind.Decimal"/>), such as a flag. A type keyed otherwise is an <see cref="ArgumentException"/>.</summary>
    public static RecordId Decimal(string type, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        var kind = RecordTypes.Info(type).Key;
        if (kind != KeyKind.Decimal)
            throw new ArgumentException($"A {type} record is keyed by {Spelling(kind)}.", nameof(type));
        return new RecordId(BaseNamespace, type, value.ToString(CultureInfo.InvariantCulture));
    }

    private static string Spelling(KeyKind kind) => kind switch
    {
        KeyKind.Db => "its u16 ItemID, 0xHHHH",
        KeyKind.Borg => "its archive index, 0xHHHH",
        KeyKind.Decimal => "a decimal index",
        KeyKind.Chunk => "its map set's number then its cell's name",
        _ => "its chunk's key then its decimal index",
    };

    public static RecordId Parse(string text) =>
        TryParse(text, out var id, out var error) ? id : throw new FormatException($"\"{text}\" isn't a record ID: {error}.");

    public static bool TryParse(string? text, out RecordId id) => TryParse(text, out id, out _);

    public static bool TryParse(string? text, out RecordId id, [NotNullWhen(false)] out string? error)
    {
        id = default;
        if (text is null)
        {
            error = "it's null";
            return false;
        }
        var colon = text.IndexOf(':', StringComparison.Ordinal);
        var slash = colon < 0 ? -1 : text.IndexOf('/', colon);
        if (colon < 0 || slash < 0)
        {
            error = "it needs the form namespace:type/key";
            return false;
        }

        var ns = text[..colon];
        var type = text[(colon + 1)..slash];
        var key = text[(slash + 1)..];
        if (!IsName(ns))
            error = "the namespace must be [a-z0-9_]+";
        else if (RecordTypes.TryInfo(type) is not { } info)
            error = $"\"{type}\" isn't a record type";
        else
            error = CheckKey(ns, info, key);
        if (error is not null)
            return false;

        id = new RecordId(ns, type, key);
        return true;
    }

    /// <summary>
    /// The key's shape, by the type's <see cref="KeyKind"/> (ADR 0004): in <c>aidyn</c>, a u16 as <c>0xHHHH</c> for a DB record or a
    /// Borg, a decimal without leading zeros for most, a chunk's map set then its cell's name (ADR 0035), and a map object's chunk key
    /// then its decimal index; in a mod, a name, or for a map object its chunk's name then its decimal index.
    /// </summary>
    private static string? CheckKey(string ns, RecordTypeInfo info, string key)
    {
        var segments = key.Split('/');
        var mod = ns != BaseNamespace;
        // A chunk's segments, before a map object's index.
        var chunkSegments = mod ? 1 : 2;
        var wanted = info.Key switch
        {
            KeyKind.Chunk => chunkSegments,
            KeyKind.MapObject => chunkSegments + 1,
            _ => 1,
        };
        if (segments.Length != wanted)
        {
            return info.Key switch
            {
                KeyKind.Chunk => mod ? $"a mod's {info.Name} key is one name" : $"a {info.Name} key is its map set's number then its cell's name, such as 8/C05",
                KeyKind.MapObject => mod ? $"a mod's {info.Name} key is its chunk's name then its index" : $"a {info.Name} key is its chunk's key then its index, such as 8/C05/17",
                _ => $"a {info.Name} key is one segment",
            };
        }
        if (info.Key is KeyKind.Chunk or KeyKind.MapObject)
        {
            if (mod ? !IsName(segments[0]) : !IsCanonicalDecimal(segments[0]) || !IsCellName(segments[1]))
                return mod ? "a mod's chunk key is [a-z0-9_]+" : "a chunk key is its map set's number then its cell's name, such as 8/C05 or 0/0_13";
            if (info.Key == KeyKind.MapObject && !IsCanonicalDecimal(segments[^1]))
                return "a map object's index is a decimal without leading zeros";
            return null;
        }
        var first = segments[0];
        if (mod)
        {
            if (!IsName(first))
                return "a mod's key is [a-z0-9_]+";
        }
        else if (info.Key == KeyKind.Decimal)
        {
            if (!IsCanonicalDecimal(first))
                return $"a {info.Name} key is a decimal without leading zeros";
        }
        else if (!IsHex(first))
        {
            return $"a {info.Name} key is 0x and four uppercase hex digits";
        }
        return null;
    }

    /// <summary>
    /// A cell's name as a base chunk key spells it (<c>ChunkRecord.NameOf</c>): its row's letter and its column in at least two digits,
    /// such as <c>C05</c>, or for a cell those can't name its row and column, <c>0_13</c> or <c>-1_13</c>.
    /// </summary>
    private static bool IsCellName(string s)
    {
        if (s.Length >= 3 && s[0] is >= 'A' and <= 'Z')
            return s.AsSpan(1).ContainsOnly("0123456789") && (s.Length == 3 || s[1] != '0');
        var underscore = s.IndexOf('_', StringComparison.Ordinal);
        if (underscore < 0)
            return false;
        return IsSignedDecimal(s[..underscore]) && IsSignedDecimal(s[(underscore + 1)..]);
    }

    private static bool IsSignedDecimal(string s) => s.StartsWith('-') ? s != "-0" && IsCanonicalDecimal(s[1..]) : IsCanonicalDecimal(s);

    private static bool IsHex(string s) => s.Length == 6 && s.StartsWith("0x", StringComparison.Ordinal) && s.AsSpan(2).ContainsOnly("0123456789ABCDEF");

    private static bool IsName(string s) => s.Length > 0 && s.AsSpan().ContainsOnly("abcdefghijklmnopqrstuvwxyz0123456789_");

    private static bool IsCanonicalDecimal(string s) =>
        s.Length > 0 && s.AsSpan().ContainsOnly("0123456789") && (s.Length == 1 || s[0] != '0');
}

file static class SpanExtensions
{
    public static bool ContainsOnly(this ReadOnlySpan<char> span, string allowed) => span.IndexOfAnyExcept(allowed) < 0;
}
