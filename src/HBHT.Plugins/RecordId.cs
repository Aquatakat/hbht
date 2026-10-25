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
    /// (<see cref="KeyKind.Db"/> and <see cref="KeyKind.Borg"/>), or with <paramref name="nested"/> a record nested in one by
    /// its index (<see cref="KeyKind.Nested"/>). A type keyed otherwise is an <see cref="ArgumentException"/>.
    /// </summary>
    public static RecordId Hex(string type, int value, params int[] nested)
    {
        if (value is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Base-game hex keys are u16.");
        var kind = RecordTypes.Info(type).Key;
        if (kind == KeyKind.Nested ? nested.Length != 1 : kind == KeyKind.Decimal || nested.Length != 0)
            throw new ArgumentException($"A {type} record is keyed by {Spelling(kind)}.", nameof(type));
        return Base(type, "0x" + value.ToString("X4", CultureInfo.InvariantCulture), nested);
    }

    /// <summary>A base-game record keyed by a decimal index (<see cref="KeyKind.Decimal"/>), such as a flag. A type keyed otherwise is an <see cref="ArgumentException"/>.</summary>
    public static RecordId Decimal(string type, int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        var kind = RecordTypes.Info(type).Key;
        if (kind != KeyKind.Decimal)
            throw new ArgumentException($"A {type} record is keyed by {Spelling(kind)}.", nameof(type));
        return Base(type, value.ToString(CultureInfo.InvariantCulture), []);
    }

    private static RecordId Base(string type, string key, int[] nested)
    {
        foreach (var n in nested)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(n);
            key += "/" + n.ToString(CultureInfo.InvariantCulture);
        }
        return new RecordId(BaseNamespace, type, key);
    }

    private static string Spelling(KeyKind kind) => kind switch
    {
        KeyKind.Db => "its u16 ItemID, 0xHHHH",
        KeyKind.Borg => "its archive index, 0xHHHH",
        KeyKind.Decimal => "a decimal index",
        _ => "its parent's 0xHHHH then its decimal index",
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
    /// Borg, a decimal without leading zeros for the rest, and for a nested record its parent's key then its decimal index; in
    /// a mod, a name, or for a nested record a name then its decimal index.
    /// </summary>
    private static string? CheckKey(string ns, RecordTypeInfo info, string key)
    {
        var segments = key.Split('/');
        var nested = info.Key == KeyKind.Nested;
        if (segments.Length != (nested ? 2 : 1))
            return nested ? $"a {info.Name} key is its parent's key then its index" : $"a {info.Name} key is one segment";
        var first = segments[0];
        if (ns != BaseNamespace)
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
        if (nested && !IsCanonicalDecimal(segments[1]))
            return "a nested record's index is a decimal without leading zeros";
        return null;
    }

    private static bool IsHex(string s) => s.Length == 6 && s.StartsWith("0x", StringComparison.Ordinal) && s.AsSpan(2).ContainsOnly("0123456789ABCDEF");

    private static bool IsName(string s) => s.Length > 0 && s.AsSpan().ContainsOnly("abcdefghijklmnopqrstuvwxyz0123456789_");

    private static bool IsCanonicalDecimal(string s) =>
        s.Length > 0 && s.AsSpan().ContainsOnly("0123456789") && (s.Length == 1 || s[0] != '0');
}

file static class SpanExtensions
{
    public static bool ContainsOnly(this ReadOnlySpan<char> span, string allowed) => span.IndexOfAnyExcept(allowed) < 0;
}
