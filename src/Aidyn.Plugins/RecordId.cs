using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Aidyn.Plugins;

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

    /// <summary>A base-game record keyed by an archive index or a u16 <c>ItemID</c>: <c>0x</c> and four uppercase hex digits.</summary>
    public static RecordId Hex(string type, int value, params int[] nested)
    {
        if (value is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(value), value, "Base-game hex keys are u16.");
        return Base(type, "0x" + value.ToString("X4", CultureInfo.InvariantCulture), nested);
    }

    /// <summary>A base-game record keyed by a decimal index, such as a flag.</summary>
    public static RecordId Decimal(string type, int value, params int[] nested)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        return Base(type, value.ToString(CultureInfo.InvariantCulture), nested);
    }

    private static RecordId Base(string type, string key, int[] nested)
    {
        if (!RecordTypes.IsKnown(type))
            throw new ArgumentException($"\"{type}\" isn't a record type.", nameof(type));
        foreach (var n in nested)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(n);
            key += "/" + n.ToString(CultureInfo.InvariantCulture);
        }
        return new RecordId(BaseNamespace, type, key);
    }

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
        else if (!RecordTypes.IsKnown(type))
            error = $"\"{type}\" isn't a record type";
        else
            error = CheckKey(ns, key);
        if (error is not null)
            return false;

        id = new RecordId(ns, type, key);
        return true;
    }

    private static string? CheckKey(string ns, string key)
    {
        var segments = key.Split('/');
        for (var i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (ns != BaseNamespace)
            {
                if (!IsName(s))
                    return "a mod's key segments must be [a-z0-9_]+";
            }
            else if (i == 0 && s.StartsWith("0x", StringComparison.Ordinal))
            {
                if (s.Length != 6 || !s.AsSpan(2).ContainsOnly("0123456789ABCDEF"))
                    return "a hex key is 0x and four uppercase hex digits";
            }
            else if (!IsCanonicalDecimal(s))
            {
                return "a base-game key is 0xHHHH or a decimal without leading zeros";
            }
        }
        return null;
    }

    private static bool IsName(string s) => s.Length > 0 && s.AsSpan().ContainsOnly("abcdefghijklmnopqrstuvwxyz0123456789_");

    private static bool IsCanonicalDecimal(string s) =>
        s.Length > 0 && s.AsSpan().ContainsOnly("0123456789") && (s.Length == 1 || s[0] != '0');
}

file static class SpanExtensions
{
    public static bool ContainsOnly(this ReadOnlySpan<char> span, string allowed) => span.IndexOfAnyExcept(allowed) < 0;
}
