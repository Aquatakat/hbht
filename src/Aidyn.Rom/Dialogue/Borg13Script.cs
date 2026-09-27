using System.Text;
using Aidyn.Rom.Db;

namespace Aidyn.Rom.Dialogue;

/// <summary>How a dialogue is presented (the decomp's <c>start_func</c>).</summary>
public enum Borg13StartMode : byte
{
    /// <summary>Talking to someone on the map.</summary>
    Conversation = 0,
    /// <summary>A scripted scene on its own map, with actors and cameras.</summary>
    Cutscene = 1,
    /// <summary>Text over a full-screen picture, such as a book. Nodes can be revisited.</summary>
    Backdrop = 2,
    /// <summary>A signpost.</summary>
    Sign = 3,
    /// <summary>The help menu. Like <see cref="Backdrop"/>, nodes can be revisited.</summary>
    Help = 4,
}

/// <summary>What a node does once reached (from the low bits of its flags byte).</summary>
public enum Borg13NodeKind
{
    /// <summary>Shows its text, then goes to its one link. With no link, it goes back to the last menu or case, if any.</summary>
    Line,
    /// <summary>Evaluates its condition: nonzero goes to the first link, zero to the second.</summary>
    If,
    /// <summary>
    /// Shows its text and evaluates its condition, then considers the links whose
    /// <see cref="Borg13Node.Min"/>–<see cref="Borg13Node.Max"/> range holds the value. One match is
    /// followed; several are offered as a menu.
    /// </summary>
    Case,
    /// <summary>Shows its text and offers its links as choices. Choices already taken are hidden, except in Backdrop and Help scripts.</summary>
    Menu,
}

/// <summary>One op: a command and its operand.</summary>
public readonly record struct Borg13Op(Borg13Opcode Opcode, ushort Value)
{
    public override string ToString() => $"{Opcode}({Value})";
}

/// <summary>A node of a Borg13 script (a <c>borg13command</c>, 0x38 bytes).</summary>
/// <param name="Flags">
/// The raw flags byte. 0x1, 0x2 and 0x4 give the <see cref="Kind"/>. 0x40 is on most menu choices and
/// is never read. The game uses 0x80 at run time to mark nodes visited.
/// </param>
/// <param name="Ops">Up to four, without the trailing <see cref="Borg13Opcode.None"/>s.</param>
/// <param name="Text">
/// As stored: <c>\n</c> and <c>\xHH</c> escapes are left as written, and a run of 20 <c>#</c> stands
/// for the player's name. Many lines start with <c>V</c> and four digits, which the game strips and
/// never uses.
/// </param>
/// <param name="Min">When this node is a Case's link: the lowest value that selects it.</param>
/// <param name="Max">The highest value that selects it. 65000 means no upper bound.</param>
/// <param name="Links">Node indices, in order. What they mean depends on the <see cref="Kind"/>.</param>
public sealed record Borg13Node(
    int Index,
    byte Flags,
    IReadOnlyList<Borg13Op> Ops,
    string Text,
    ushort Min,
    ushort Max,
    IReadOnlyList<int> Links)
{
    public const int Size = 0x38;
    public const ushort NoUpperBound = 65000;

    public Borg13NodeKind Kind => (Flags & 1) != 0 ? Borg13NodeKind.Line
        : (Flags & 2) != 0 ? Borg13NodeKind.If
        : (Flags & 4) != 0 ? Borg13NodeKind.Case
        : Borg13NodeKind.Menu;

    /// <summary>The op an If or Case evaluates: the first condition op.</summary>
    public Borg13Op? Condition => Ops.Cast<Borg13Op?>().FirstOrDefault(op => op!.Value.Opcode.IsCondition());

    /// <summary>The one effect op that runs: the first.</summary>
    public Borg13Op? Effect => Ops.Cast<Borg13Op?>().FirstOrDefault(op => op!.Value.Opcode.IsEffect());

    /// <summary>Whether a Case's value selects this node.</summary>
    public bool Matches(int value) => Min <= value && (value <= Max || Max == NoUpperBound);
}

/// <summary>
/// A Borg13: a dialogue, cut-scene, sign, book or menu script. It's a graph of nodes that starts at
/// node 0. The data is a 0x18-byte header, the nodes, the actors, then the text, padded to 8 bytes.
/// In 513 scripts the text holds strings no node uses, and the padding isn't always zero. 153 scripts
/// end with a blank node that nothing links to, which is dropped.
/// </summary>
/// <param name="Id">The script's number in the original tools. The debug build prints it.</param>
/// <param name="DoneFlag">
/// An event flag the game sets when a Cutscene, Backdrop or Help script finishes. Conversations
/// and signs don't set it.
/// </param>
/// <param name="Backdrop">For Backdrop scripts, which picture to show (the decomp's <c>libraryType</c>). 14 for all others.</param>
/// <param name="Actors">The entities a Cutscene spawns and directs.</param>
public sealed record Borg13Script(
    ushort Id,
    ushort DoneFlag,
    Borg13StartMode StartMode,
    byte Backdrop,
    IReadOnlyList<ItemId> Actors,
    IReadOnlyList<Borg13Node> Nodes)
{
    private const int HeaderSize = 0x18;
    private const int ActorSize = 8;
    private const byte NoLink = 0xFF;

    public static Borg13Script Parse(byte[] data)
    {
        var r = new RecordReader(data);
        var nodesOffset = r.U32Be();
        var actorsOffset = r.U32Be();
        var textOffset = r.U32Be();
        var id = r.U16Be();
        var doneFlag = r.U16Be();
        var textSize = r.U16Be();
        var nodeCount = r.U8();
        var actorCount = r.U8();
        var startMode = r.U8();
        var backdrop = r.U8();
        if (r.U16Be() != 0)
            throw new InvalidDataException("Borg13 header padding isn't zero.");

        if (nodesOffset != HeaderSize || actorsOffset != nodesOffset + nodeCount * Borg13Node.Size
            || textOffset != actorsOffset + actorCount * ActorSize
            || (textOffset + textSize + 7 & ~7) != data.Length)
            throw new InvalidDataException(
                $"Borg13 parts at 0x{nodesOffset:X}/0x{actorsOffset:X}/0x{textOffset:X} (+0x{textSize:X}) don't follow its " +
                $"{nodeCount} nodes and {actorCount} actors in 0x{data.Length:X} bytes.");
        if (nodeCount == 0)
            throw new InvalidDataException("Borg13 has no nodes.");
        if (startMode > (byte)Borg13StartMode.Help)
            throw new InvalidDataException($"Unknown Borg13 start mode {startMode}.");

        var text = data.AsSpan((int)textOffset, textSize);
        var nodes = new List<Borg13Node>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            if (ReadNode(ref r, i, nodeCount, text) is { } node)
                nodes.Add(node);
            else if (i != nodeCount - 1)
                throw new InvalidDataException($"Borg13 node {i} is blank but isn't the last.");
        }
        if (nodes.SelectMany(n => n.Links).Any(l => l >= nodes.Count))
            throw new InvalidDataException("A Borg13 node links to the blank last node.");

        var actors = new ItemId[actorCount];
        for (var i = 0; i < actors.Length; i++)
        {
            // A run-time pointer, the entity, then padding.
            if (r.U32Be() != 0)
                throw new InvalidDataException($"Borg13 actor {i} has a stored pointer.");
            actors[i] = new ItemId(r.U16Be());
            if (r.U16Be() != 0)
                throw new InvalidDataException($"Borg13 actor {i} padding isn't zero.");
        }

        return new Borg13Script(id, doneFlag, (Borg13StartMode)startMode, backdrop, actors, nodes);
    }

    /// <summary>Reads a node, or returns null for a blank one: no ops, text or links, and no index.</summary>
    private static Borg13Node? ReadNode(ref RecordReader r, int index, int nodeCount, ReadOnlySpan<byte> text)
    {
        var ops = new List<Borg13Op>(4);
        for (var i = 0; i < 4; i++)
        {
            var value = r.U16Be();
            var opcode = r.U8();
            if (r.U8() != 0 || r.U32Be() != 0)
                throw new InvalidDataException($"Borg13 node {index} op {i} has nonzero padding.");
            if (opcode == 0)
                continue;
            if (opcode > (byte)Borg13Opcode.ResetVisited)
                throw new InvalidDataException($"Borg13 node {index} has unknown opcode {opcode}.");
            if (ops.Count < i)
                throw new InvalidDataException($"Borg13 node {index} has an op after an empty slot.");
            ops.Add(new Borg13Op((Borg13Opcode)opcode, value));
        }

        var textStart = r.S16Be();
        var textLength = r.U16Be();
        var min = r.U16Be();
        var max = r.U16Be();
        var links = new List<int>(8);
        var ended = false;
        // The first link, then seven more. The eighth byte after the first link is the node's own index.
        for (var i = 0; i < 8; i++)
        {
            var link = r.U8();
            if (link == NoLink)
            {
                ended = true;
                continue;
            }
            if (ended)
                throw new InvalidDataException($"Borg13 node {index} has a link after an empty slot.");
            if (link >= nodeCount)
                throw new InvalidDataException($"Borg13 node {index} links to node {link} of {nodeCount}.");
            links.Add(link);
        }
        var storedIndex = r.U8();
        var flags = r.U8();
        if (r.U16Be() != 0 || r.U32Be() != 0)
            throw new InvalidDataException($"Borg13 node {index} has nonzero padding.");
        if (storedIndex == NoLink && flags == 0 && ops.Count == 0 && textLength == 0 && links.Count == 0 && min == 0 && max == 0)
            return null;
        if (storedIndex != index)
            throw new InvalidDataException($"Borg13 node {index} doesn't store its own index.");

        if (textStart < 0 || textStart + textLength >= text.Length || text[textStart + textLength] != 0
            || text.Slice(textStart, textLength).Contains((byte)0))
            throw new InvalidDataException($"Borg13 node {index} text at 0x{textStart:X} (+{textLength}) isn't a NUL-terminated string.");

        return new Borg13Node(index, flags, ops, Encoding.ASCII.GetString(text.Slice(textStart, textLength)),
            min, max, links);
    }
}
