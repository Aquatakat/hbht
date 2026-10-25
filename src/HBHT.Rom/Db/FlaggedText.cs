using System.Globalization;
using HBHT.Rom.Text;

namespace HBHT.Rom.Db;

/// <summary>
/// A line of text that shows once an event flag is set: a journal entry, or a cinematic's
/// title in the theatre menu. The ROM stores it as the flag number, a space, then the text.
/// </summary>
public sealed record FlaggedText(ushort Flag, string Text)
{
    private static ReadOnlySpan<byte> EndMarker => "****"u8;

    /// <summary>
    /// Reads a list of encrypted strings, each prefixed by its length, up to a <c>****</c> marker.
    /// The journal uses a big-endian u16 length and the theatre menu a single byte.
    /// </summary>
    internal static IReadOnlyList<FlaggedText> ReadList(ReadOnlySpan<byte> data, bool wideLengths)
    {
        var r = new RecordReader(data);
        var entries = new List<FlaggedText>();
        while (!r.StartsWith(EndMarker))
        {
            var length = wideLengths ? r.U16Be() : r.U8();
            entries.Add(Parse(GameText.Decrypt(r.Bytes(length), 0, 0)));
        }
        return entries;
    }

    private static FlaggedText Parse(string line)
    {
        var space = line.IndexOf(' ');
        if (space < 1 || !ushort.TryParse(line.AsSpan(0, space), NumberStyles.None, CultureInfo.InvariantCulture, out var flag))
            throw new InvalidDataException($"Expected a flag number before \"{line}\".");
        return new FlaggedText(flag, line[(space + 1)..]);
    }
}
