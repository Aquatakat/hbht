using System.Globalization;
using System.Text;
using HBHT.Plugins;
using HBHT.Plugins.Records;

/// <summary>
/// A flag's entry in the quest index, as text: what sets, clears and reads it, with each script's
/// nearest line and where it's started from.
/// </summary>
internal sealed class FlagReport
{
    private readonly PluginSet _plugins;
    private readonly PluginCatalog _catalog;
    private readonly QuestIndex _index;

    public FlagReport(PluginSet plugins, QuestIndex index)
    {
        _plugins = plugins;
        _index = index;
        _catalog = PluginCatalog.Build(plugins);
        Flags = [.. plugins.All<FlagRecord>(RecordTypes.Flag).OrderBy(f => Number(f.Id))];
        Journal = [.. plugins.All<JournalRecord>(RecordTypes.Journal).OrderBy(j => Number(j.Id))];
    }

    /// <summary>Every flag, by number.</summary>
    public IReadOnlyList<FlagRecord> Flags { get; }

    /// <summary>Every journal entry, by number.</summary>
    private IReadOnlyList<JournalRecord> Journal { get; }

    private static int Number(RecordId id) => int.Parse(id.Key, CultureInfo.InvariantCulture);

    public string Describe(int number)
    {
        var s = new StringBuilder();
        var id = RecordId.Decimal(RecordTypes.Flag, number);
        var flag = _plugins.Get<FlagRecord>(id);
        if (flag.Formula is { } formula)
            s.AppendLine(CultureInfo.InvariantCulture, $"Flag {number}: derived, {formula.Describe()}; inputs {string.Join(", ", formula.Inputs.Select(i => Number(i)))}");
        else
            s.AppendLine(CultureInfo.InvariantCulture, $"Flag {number}: plain, starts {(flag.Initial ? "set" : "clear")}");
        if (flag.Label is { } label)
            s.AppendLine(label);

        foreach (var entry in Journal.Where(j => j.Flag == id))
            s.AppendLine(CultureInfo.InvariantCulture, $"Journal {Number(entry.Id)}: {entry.Text.Trim()}");
        if (flag.Journal is { } shown)
            s.AppendLine(CultureInfo.InvariantCulture, $"Shows the new-entry notice for journal {Number(shown)}.");

        var uses = _index.UsesOf(id);
        var writes = uses.Where(u => u.Access != FlagAccess.Read).ToList();
        if (flag.Formula is null || writes.Count > 0)
            Section("Set or cleared by", writes);
        Section("Read by", uses.Where(u => u.Access == FlagAccess.Read));
        return s.ToString();

        void Section(string title, IEnumerable<FlagUse> list)
        {
            var items = list.ToList();
            s.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{title}: {(items.Count == 0 ? "nothing known" : "")}");
            foreach (var use in items)
            {
                s.Append("  ").Append(use.Role);
                if (use.Access == FlagAccess.Clear)
                    s.Append(" (clears)");
                if (use.Inverted)
                    s.Append(" (while clear)");
                s.Append(": ").AppendLine(Where(use.By, use.At));
            }
        }
    }

    /// <summary>A record that uses a flag, as text. For a script, <paramref name="node"/> names the node.</summary>
    private string Where(RecordId by, int? node = null)
    {
        switch (by.Type)
        {
            case RecordTypes.Dialogue when _plugins.TryGet<DialogueRecord>(by) is { } script:
                var where = $"{by} ({script.Mode}){(node is { } n ? $" node {n}" : "")}";
                if (node is { } at && LineNear(script, at) is { Length: > 0 } text)
                    where += $" near \"{Shorten(text)}\"";
                return where + StartedFrom(by);
            case RecordTypes.Voxel:
                return Object(by);
            case RecordTypes.Flag:
                return $"derived flag {by.Key}";
            default:
                var summary = _catalog.Find(by)?.Summary;
                return string.IsNullOrEmpty(summary) ? by.ToString() : $"{by} ({summary})";
        }
    }

    private string StartedFrom(RecordId script)
    {
        var starters = _index.StartersOf(script);
        if (starters.Count == 0)
            return ", started by code or nothing";
        return ", started from " + string.Join("; ", starters.Take(3).Select(s => s.By.Type == RecordTypes.Voxel ? Object(s.By) : s.By.ToString()))
            + (starters.Count > 3 ? $" and {starters.Count - 3} more" : "");
    }

    /// <summary>A map object as text: what it is, where its map sits, and its index.</summary>
    private string Object(RecordId voxel) => $"{_catalog.Describe(voxel)} (object {MapRecord.IndexOf(voxel)})";

    /// <summary>The node's own text, or the nearest text on the way to it.</summary>
    private static string LineNear(DialogueRecord script, int node)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<int>([node]);
        while (queue.TryDequeue(out var i))
        {
            if (!seen.Add(i) || i >= script.Nodes.Count)
                continue;
            if (script.Nodes[i].Text.Length > 0)
                return script.Nodes[i].Text;
            foreach (var parent in script.Nodes.Where(n => n.Links?.Contains(i) == true))
                queue.Enqueue(parent.Index);
        }
        return "";
    }

    private static string Shorten(string text)
    {
        text = text.ReplaceLineEndings(" ");
        return text.Length <= 60 ? text : text[..57] + "...";
    }
}
