using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aidyn.Rom.Db;
using Aidyn.Rom.Dialogue;
using Aidyn.Rom.Maps;
using Aidyn.Rom.Quests;

/// <summary>
/// The quest atlas as one JSON file for <c>tools/quest-atlas.html</c>. Map objects are included
/// only if they use a flag or start a script, and are referred to by their position in
/// <c>objects</c>.
/// </summary>
internal static class AtlasJson
{
    public static byte[] Serialize(QuestAtlas atlas, string version)
    {
        var objectIndex = new Dictionary<(int, int), int>();
        var objects = new List<AtlasObject>();
        var byKey = atlas.Objects.ToDictionary(o => (o.Borg9, o.Object.Index));
        void Use(AtlasSource source)
        {
            if (source is ObjectSource os && !objectIndex.ContainsKey((os.Borg9, os.Index)))
            {
                objectIndex[(os.Borg9, os.Index)] = objects.Count;
                objects.Add(byKey[(os.Borg9, os.Index)]);
            }
        }
        foreach (var r in atlas.References)
            Use(r.Source);
        foreach (var s in atlas.Scripts.Values.SelectMany(s => s.StartedBy))
            Use(s);

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("version", version);
            w.WriteNumber("bitFlagCount", atlas.Funnel.BitFlagCount);
            w.WriteNumber("flagCount", atlas.Funnel.Flags.Count);

            w.WriteStartObject("names");
            foreach (var (id, name) in atlas.Names.OrderBy(n => n.Key))
                w.WriteString(id.ToString(), name);
            w.WriteEndObject();

            w.WriteStartArray("journal");
            foreach (var entry in atlas.Journal)
            {
                w.WriteStartObject();
                w.WriteNumber("flag", entry.Flag);
                w.WriteString("text", entry.Text);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("engine");
            foreach (var range in EngineFlags.Ranges)
            {
                w.WriteStartObject();
                w.WriteNumber("first", range.First);
                w.WriteNumber("last", range.Last);
                w.WriteString("description", range.Description);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("labels");
            foreach (var (flag, label) in atlas.Labels.OrderBy(l => l.Key))
                w.WriteString(flag.ToString(CultureInfo.InvariantCulture), label);
            w.WriteEndObject();

            // Derived flags' definitions, and plain flags that start set.
            w.WriteStartObject("flags");
            for (var flag = 0; flag < atlas.Funnel.Flags.Count; flag++)
            {
                var d = atlas.Funnel.Flags[flag];
                var derived = !atlas.IsBitFlag(flag);
                if (!derived && d.InitialValue == 0)
                    continue;
                w.WriteStartObject(flag.ToString(CultureInfo.InvariantCulture));
                if (d.InitialValue != 0)
                    w.WriteNumber("initial", d.InitialValue);
                if (derived)
                {
                    w.WriteString("kind", d.Kind.ToString());
                    w.WriteString("op", d.Op.ToString());
                    w.WriteNumber("operand", d.Operand);
                    w.WriteStartArray("inputs");
                    foreach (var input in QuestAtlas.Inputs(atlas.Funnel, flag))
                        w.WriteNumberValue(input);
                    w.WriteEndArray();
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartArray("refs");
            foreach (var r in atlas.References)
            {
                w.WriteStartObject();
                w.WriteNumber("flag", r.Flag);
                w.WriteString("access", r.Access.ToString());
                w.WriteString("role", r.Role.ToString());
                if (r.Ignored)
                    w.WriteBoolean("ignored", true);
                WriteSource(w, "src", r.Source, objectIndex);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("scripts");
            foreach (var (index, entry) in atlas.Scripts.OrderBy(s => s.Key))
            {
                var script = entry.Script;
                w.WriteStartObject(index.ToString(CultureInfo.InvariantCulture));
                w.WriteString("mode", script.StartMode.ToString());
                w.WriteNumber("id", script.Id);
                w.WriteNumber("doneFlag", script.DoneFlag);
                if (script.StartMode == Borg13StartMode.Backdrop)
                    w.WriteNumber("backdrop", script.Backdrop);
                w.WriteStartArray("actors");
                foreach (var actor in script.Actors)
                    w.WriteStringValue(actor.ToString());
                w.WriteEndArray();
                w.WriteStartArray("startedBy");
                foreach (var source in entry.StartedBy)
                {
                    w.WriteStartObject();
                    WriteSource(w, "src", source, objectIndex);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteStartArray("nodes");
                foreach (var node in script.Nodes)
                {
                    w.WriteStartObject();
                    w.WriteString("kind", node.Kind.ToString());
                    if (node.Text.Length > 0)
                        w.WriteString("text", node.Text);
                    w.WriteStartArray("ops");
                    foreach (var op in node.Ops)
                    {
                        w.WriteStartArray();
                        w.WriteStringValue(op.Opcode.ToString());
                        w.WriteNumberValue(op.Value);
                        w.WriteEndArray();
                    }
                    w.WriteEndArray();
                    w.WriteStartArray("links");
                    foreach (var link in node.Links)
                        w.WriteNumberValue(link);
                    w.WriteEndArray();
                    if (node.Min != 0 || node.Max != 0)
                    {
                        w.WriteNumber("min", node.Min);
                        w.WriteNumber("max", node.Max);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartArray("objects");
            foreach (var o in objects)
                WriteObject(w, o);
            w.WriteEndArray();

            w.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteSource(Utf8JsonWriter w, string name, AtlasSource source, Dictionary<(int, int), int> objectIndex)
    {
        w.WriteStartObject(name);
        switch (source)
        {
            case ScriptSource s:
                w.WriteNumber("script", s.Script);
                if (s.Node is { } node)
                    w.WriteNumber("node", node);
                break;
            case ObjectSource o:
                w.WriteNumber("object", objectIndex[(o.Borg9, o.Index)]);
                break;
            case FlagSource f:
                w.WriteNumber("derived", f.Flag);
                break;
            case ShopSource p:
                w.WriteNumber("shop", p.Shop);
                w.WriteString("shopkeeper", p.Shopkeeper.ToString());
                break;
            case EngineSource e:
                w.WriteString("engine", e.Description);
                break;
        }
        w.WriteEndObject();
    }

    private static void WriteObject(Utf8JsonWriter w, AtlasObject entry)
    {
        var o = entry.Object;
        w.WriteStartObject();
        w.WriteNumber("borg9", entry.Borg9);
        w.WriteNumber("index", o.Index);
        w.WriteString("type", o.Type.ToString());
        w.WriteStartArray("chunks");
        foreach (var chunk in entry.Chunks)
            w.WriteStringValue(chunk.ToString());
        w.WriteEndArray();
        w.WriteStartArray("pos");
        w.WriteNumberValue(MathF.Round(o.Position.X, 2));
        w.WriteNumberValue(MathF.Round(o.Position.Y, 2));
        w.WriteNumberValue(MathF.Round(o.Position.Z, 2));
        w.WriteEndArray();
        w.WriteString("bits", "0x" + ((int)o.Flags).ToString("X4", CultureInfo.InvariantCulture));
        w.WriteString("visible", o.Visible.ToString());
        w.WriteString("enabled", o.Enabled.ToString());
        w.WriteNumber("flagB", o.FlagB);
        switch (o.Data)
        {
            case DialogueData d:
                w.WriteNumber("script", d.Script);
                w.WriteString("name", d.Name);
                break;
            case MonsterPartyData m:
                w.WriteString("entity", m.Entity.ToString());
                if (m.Dialogue != 0)
                    w.WriteNumber("script", m.Dialogue);
                w.WriteStartArray("enemies");
                foreach (var e in m.Enemies)
                    w.WriteStringValue(e.Enemy.ToString());
                w.WriteEndArray();
                break;
            case TeleporterData t:
                w.WriteString("name", t.Name);
                w.WriteString("to", new MapChunk(t.Destination.Set, t.Destination.Row, t.Destination.Column, 0, 0, 0, 0).ToString());
                break;
            case ContainerData c:
                if (!c.Loot.IsNone)
                    w.WriteString("loot", c.Loot.ToString());
                if (!c.Item.IsNone)
                    w.WriteString("item", c.Item.ToString());
                if (c.Gold != 0)
                    w.WriteNumber("gold", c.Gold);
                break;
            case TriggerData t:
                w.WriteString("trigger", t.Kind.ToString());
                w.WriteNumber("a", t.A);
                break;
            case ReferencePointData r:
                w.WriteString("name", r.Name);
                break;
        }
        w.WriteEndObject();
    }
}
