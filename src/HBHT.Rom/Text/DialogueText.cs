using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace HBHT.Rom.Text;

/// <summary>
/// Cleans a Borg13 node's text. The game does this at run time, and the importer does it once.
/// </summary>
internal static partial class DialogueText
{
    /// <summary>Where the game writes the first party member's name.</summary>
    public const string Player = "{player}";

    /// <summary>
    /// A tools line number, such as <c>V0173</c>, which the game strips before it shows a line.
    /// </summary>
    [GeneratedRegex("^ *[vV][0-9]{4}")]
    private static partial Regex LineNumber();

    /// <summary>A run of 20 <c>#</c>, the room the game reserves for the player's name.</summary>
    [GeneratedRegex("#{20}")]
    private static partial Regex NamePlaceholder();

    /// <summary>
    /// Removes the line number, turns the name placeholder into <see cref="Player"/> and decodes the
    /// <c>\n</c> and <c>\xHH</c> escapes. Nothing else changes, not even spaces. Throws on a stray
    /// <c>#</c> or any other escape, so new data can't slip through.
    /// </summary>
    public static string Clean(string text)
    {
        text = LineNumber().Replace(text, "");
        text = NamePlaceholder().Replace(text, Player);
        if (text.Contains('#', StringComparison.Ordinal))
            throw new InvalidDataException($"Dialogue text \"{text}\" has a # that isn't a run of 20.");
        return Unescape(text);
    }

    /// <summary>
    /// Decodes the <c>\n</c> and <c>\xHH</c> escapes, as the game does when it loads text
    /// (<c>some_string_func</c>). Throws on any other escape.
    /// </summary>
    public static string Unescape(string text)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\')
            {
                result.Append(text[i]);
                continue;
            }
            if (i + 1 < text.Length && text[i + 1] == 'n')
            {
                result.Append('\n');
                i++;
            }
            else if (i + 3 < text.Length && text[i + 1] == 'x'
                && int.TryParse(text.AsSpan(i + 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            {
                result.Append((char)code);
                i += 3;
            }
            else
            {
                throw new InvalidDataException($"Text \"{text}\" has an escape at {i} that isn't \\n or \\xHH.");
            }
        }
        return result.ToString();
    }
}
