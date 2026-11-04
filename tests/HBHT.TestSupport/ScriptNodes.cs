using HBHT.Plugins;
using HBHT.Plugins.Records;

namespace HBHT.TestSupport;

/// <summary>
/// Script nodes for tests, from a flat list of typed ops as the original wrote them (ADR 0034): the setup ops go in
/// <see cref="ScriptNode.Setup"/> in order, the first check in <see cref="ScriptNode.Condition"/> and the first effect in
/// <see cref="ScriptNode.Effect"/>. Ops a node can't run (a trigger's own) go in the setup, where the runner passes over them.
/// </summary>
public static class ScriptNodes
{
    public static ScriptNode Node(int index, ScriptNodeKind kind, string text, IReadOnlyList<int>? links, RecordId? speaker, params ScriptOp[] ops)
    {
        var setup = ops.Where(o => (o.Use & OpUse.Setup) != 0 || (o.Use & (OpUse.Condition | OpUse.Effect)) == 0).ToList();
        return new ScriptNode(index, kind, text, speaker,
            setup.Count == 0 ? null : setup,
            ops.FirstOrDefault(o => o is ScriptOp.Check),
            ops.FirstOrDefault(o => (o.Use & OpUse.Effect) != 0 && o is not ScriptOp.Check && (o.Use & OpUse.Setup) == 0),
            links is { Count: > 0 } ? links : null);
    }

    public static ScriptNode Line(int index, string text, int? link = null, RecordId? speaker = null, params ScriptOp[] ops) =>
        Node(index, ScriptNodeKind.Line, text, link is { } l ? [l] : null, speaker, ops);
}
