using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Aidyn.Plugins;
using Aidyn.Plugins.Records;

/// <summary>
/// The quest index as one JSON file: every flag with its label, formula, journal entry and uses, every
/// script with what starts it, and the map objects that use a flag or start a script, which the uses
/// refer to by their voxel IDs.
/// </summary>
internal static class AtlasJson
{
    public static byte[] Serialize(FlagReport report, PluginSet plugins)
    {
        var index = report.Index;
        var voxels = new SortedSet<RecordId>(Comparer<RecordId>.Create((a, b) => string.CompareOrdinal(a.ToString(), b.ToString())));
        var scripts = plugins.All<DialogueRecord>(RecordTypes.Dialogue).OrderBy(s => s.Number).ToList();

        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("version", plugins.Plugins.FirstOrDefault(p => p.Manifest.Import is not null)?.Manifest.Import?.RomVersion);
            w.WriteNumber("bitFlagCount", report.Flags.Count(f => f.Formula is null));
            w.WriteNumber("flagCount", report.Flags.Count);

            w.WriteStartArray("journal");
            foreach (var entry in report.Journal)
            {
                w.WriteStartObject();
                w.WriteNumber("flag", FlagReport.Number(entry.Flag));
                w.WriteString("text", entry.Text);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // Flags with something to say: a label, a formula, a journal entry, a start set, or a use.
            w.WriteStartObject("flags");
            foreach (var flag in report.Flags)
            {
                var uses = index.UsesOf(flag.Id);
                if (flag.Label is null && flag.Formula is null && flag.Journal is null && !flag.Initial && uses.Count == 0)
                    continue;
                w.WriteStartObject(FlagReport.Number(flag.Id).ToString(CultureInfo.InvariantCulture));
                if (flag.Label is not null)
                    w.WriteString("label", flag.Label);
                if (flag.Initial)
                    w.WriteBoolean("initial", true);
                if (flag.Journal is { } journal)
                    w.WriteNumber("journal", FlagReport.Number(journal));
                if (flag.Formula is { } formula)
                {
                    w.WriteStartObject("formula");
                    w.WriteString("kind", formula.Kind.ToString());
                    w.WriteString("operation", formula.Op.ToString());
                    w.WriteNumber("operand", formula.Operand);
                    w.WriteStartArray("inputs");
                    foreach (var input in formula.Inputs)
                        w.WriteNumberValue(FlagReport.Number(input));
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteStartArray("uses");
                foreach (var use in uses)
                {
                    if (use.By.Type == RecordTypes.Voxel)
                        voxels.Add(use.By);
                    w.WriteStartObject();
                    w.WriteString("by", use.By.ToString());
                    w.WriteString("access", use.Access.ToString());
                    w.WriteString("role", use.Role.ToString());
                    if (use.At is { } at)
                        w.WriteNumber("node", at);
                    if (use.Inverted)
                        w.WriteBoolean("inverted", true);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("scripts");
            foreach (var script in scripts)
            {
                var starters = index.StartersOf(script.Id);
                foreach (var starter in starters.Where(s => s.By.Type == RecordTypes.Voxel))
                    voxels.Add(starter.By);
                w.WriteStartObject(script.Id.ToString());
                w.WriteNumber("number", script.Number);
                w.WriteString("mode", script.Mode.ToString());
                if (script.DoneFlag is { } done)
                    w.WriteNumber("doneFlag", FlagReport.Number(done));
                if (script.BattleFlag is { } battle)
                    w.WriteNumber("battleFlag", FlagReport.Number(battle));
                w.WriteStartArray("startedBy");
                foreach (var starter in starters)
                {
                    w.WriteStartObject();
                    w.WriteString("by", starter.By.ToString());
                    if (starter.At is { } at)
                        w.WriteNumber("node", at);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("objects");
            foreach (var voxel in voxels)
                w.WriteString(voxel.ToString(), report.Object(voxel));
            w.WriteEndObject();

            w.WriteEndObject();
        }
        return stream.ToArray();
    }
}
