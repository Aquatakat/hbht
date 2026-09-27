using System.Globalization;
using System.Text;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;

/// <summary>A flag's entry in the quest atlas, as text.</summary>
internal static class FlagReport
{
    public static string Describe(QuestAtlas atlas, int flag)
    {
        var s = new StringBuilder();
        var definition = atlas.Funnel.Flags[flag];
        if (atlas.IsBitFlag(flag))
            s.AppendLine(CultureInfo.InvariantCulture, $"Flag {flag}: plain, starts {(definition.InitialValue != 0 ? "set" : "clear")}");
        else
            s.AppendLine(CultureInfo.InvariantCulture, $"Flag {flag}: derived, {Derivation(atlas, flag)}");

        foreach (var entry in atlas.JournalByFlag[(ushort)flag])
            s.AppendLine(CultureInfo.InvariantCulture, $"Journal {entry}: {atlas.Journal[entry].Text.Trim()}");
        if (flag >= EngineFlags.JournalTriggers && flag - EngineFlags.JournalTriggers < atlas.Journal.Count)
            s.AppendLine(CultureInfo.InvariantCulture, $"Shows the new-entry notice for journal {flag - EngineFlags.JournalTriggers}.");

        var refs = atlas.References.Where(r => r.Flag == flag).ToList();
        var writes = refs.Where(r => r.Access != FlagAccess.Read).ToList();
        if (atlas.IsBitFlag(flag) || writes.Count > 0)
            Section("Set or cleared by", writes);
        Section("Read by", refs.Where(r => r.Access == FlagAccess.Read));
        return s.ToString();

        void Section(string title, IEnumerable<FlagReference> list)
        {
            var items = list.ToList();
            s.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"{title}: {(items.Count == 0 ? "nothing known" : "")}");
            foreach (var r in items)
            {
                s.Append("  ").Append(r.Role);
                if (r.Access == FlagAccess.Clear)
                    s.Append(" (clears)");
                if (r.Ignored)
                    s.Append(" (ignored by the game)");
                s.Append(": ").AppendLine(Where(atlas, r.Source));
            }
        }
    }

    public static string Derivation(QuestAtlas atlas, int flag)
    {
        var d = atlas.Funnel.Flags[flag];
        var inputs = string.Join(", ", QuestAtlas.Inputs(atlas.Funnel, flag));
        return d.Kind switch
        {
            FlagKind.Logic when d.Op == FlagOp.Mask => $"inputs as bits == {d.Operand} [{inputs}]",
            FlagKind.Logic => $"{d.Op.ToString().ToUpperInvariant()} [{inputs}]",
            FlagKind.Value => $"inputs as bits {Compare(d.Op)} {d.Operand} [{inputs}]",
            FlagKind.Count => $"count set {Compare(d.Op)} {d.Operand} [{inputs}]",
            _ => d.Kind.ToString(),
        };

        static string Compare(FlagOp op) => op switch
        {
            FlagOp.Equal => "==", FlagOp.Greater => ">", FlagOp.Less => "<", FlagOp.NotEqual => "!=", _ => op.ToString(),
        };
    }

    public static string Where(QuestAtlas atlas, AtlasSource source)
    {
        switch (source)
        {
            case ScriptSource ss:
                var entry = atlas.Scripts[ss.Script];
                var where = $"Borg13 {ss.Script} ({entry.Script.StartMode}){(ss.Node is { } n ? $" node {n}" : "")}";
                if (ss.Node is { } node)
                {
                    var text = LineNear(entry.Script, node);
                    if (text.Length > 0)
                        where += $" near \"{Shorten(text)}\"";
                }
                return where + StartedBy(atlas, entry);
            case ObjectSource os:
                return Object(atlas, os);
            case FlagSource fs:
                return $"derived flag {fs.Flag}";
            case EngineSource es:
                return $"engine: {es.Description}";
            default:
                return source.ToString();
        }
    }

    private static string StartedBy(QuestAtlas atlas, AtlasScript entry) =>
        entry.StartedBy.Count == 0 ? ", started by code or nothing"
            : ", started from " + string.Join("; ", entry.StartedBy.Take(3).Select(s => s switch
            {
                ObjectSource os => Object(atlas, os),
                ScriptSource ss => $"Borg13 {ss.Script}",
                _ => s.ToString(),
            })) + (entry.StartedBy.Count > 3 ? $" and {entry.StartedBy.Count - 3} more" : "");

    private static string Object(QuestAtlas atlas, ObjectSource os)
    {
        var o = atlas.Objects.First(x => x.Borg9 == os.Borg9 && x.Object.Index == os.Index);
        var chunk = o.Chunks.Count > 0 ? string.Join("/", o.Chunks) : $"Borg9 {o.Borg9}";
        var name = o.Object.Data switch
        {
            DialogueData d => $" \"{d.Name}\" (Borg13 {d.Script})",
            TeleporterData t => $" \"{t.Name}\"",
            MonsterPartyData m => $" {atlas.Names.GetValueOrDefault(m.Entity, m.Entity.ToString())}",
            _ => "",
        };
        return $"{o.Object.Type}{name} in {chunk} (object {o.Object.Index})";
    }

    /// <summary>The node's own text, or the nearest text on the way to it.</summary>
    private static string LineNear(Borg13Script script, int node)
    {
        var seen = new HashSet<int>();
        var queue = new Queue<int>([node]);
        while (queue.TryDequeue(out var i))
        {
            if (!seen.Add(i))
                continue;
            if (script.Nodes[i].Text.Length > 0)
                return script.Nodes[i].Text;
            foreach (var parent in script.Nodes.Where(n => n.Links.Contains(i)))
                queue.Enqueue(parent.Index);
        }
        return "";
    }

    private static string Shorten(string text) => text.Length <= 60 ? text : text[..57] + "...";
}
