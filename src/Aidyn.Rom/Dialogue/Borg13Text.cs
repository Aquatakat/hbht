using System.Globalization;
using System.Text;
using Aidyn.Rom.Db;

namespace Aidyn.Rom.Dialogue;

/// <summary>Writes a Borg13 script as readable text, one node per paragraph.</summary>
public static class Borg13Text
{
    /// <param name="entityName">Names an entity or dialogue entity, or returns null.</param>
    public static string Format(Borg13Script script, int borgIndex, Func<ItemId, string?> entityName)
    {
        var s = new StringBuilder();
        s.Append(CultureInfo.InvariantCulture,
            $"Borg13 {borgIndex}: {script.StartMode}, id {script.Id}, done flag {script.DoneFlag}");
        if (script.StartMode == Borg13StartMode.Backdrop)
            s.Append(CultureInfo.InvariantCulture, $", backdrop {script.Backdrop}");
        s.AppendLine();
        if (script.Actors.Count > 0)
            s.AppendLine("Actors: " + string.Join(", ", script.Actors.Select(Entity)));

        foreach (var node in script.Nodes)
        {
            s.AppendLine();
            s.Append(CultureInfo.InvariantCulture, $"[{node.Index}] {node.Kind}");
            foreach (var op in node.Ops)
                s.Append("  ").Append(Op(op));
            s.AppendLine();
            if (node.Text.Length > 0)
                s.AppendLine("    \"" + node.Text + "\"");
            if (node.Links.Count > 0)
                s.AppendLine("    -> " + Links(script, node));
        }
        return s.ToString();

        string Entity(ItemId id) => entityName(id) is { } name ? $"{name} ({id})" : id.ToString();

        string Op(Borg13Op op) => op.Opcode switch
        {
            Borg13Opcode.SetSpeaker or Borg13Opcode.CameraCutToActor or Borg13Opcode.CameraOnActor
                or Borg13Opcode.CameraAngleToActor or Borg13Opcode.CameraPanToActor or Borg13Opcode.CameraSineToActor
                or Borg13Opcode.AddEnemy or Borg13Opcode.AddMember or Borg13Opcode.RemoveMember or Borg13Opcode.CheckMember
                => $"{op.Opcode}({Entity(new ItemId(op.Value))})",
            Borg13Opcode.AddItem or Borg13Opcode.TakeItem or Borg13Opcode.CheckItem or Borg13Opcode.ShowItem
                => $"{op.Opcode}({new ItemId(op.Value)})",
            _ => op.ToString(),
        };
    }

    private static string Links(Borg13Script script, Borg13Node node) => node.Kind switch
    {
        Borg13NodeKind.If => $"{node.Links[0]} if nonzero, else {node.Links[1]}",
        Borg13NodeKind.Case => string.Join(", ", node.Links.Select(l => $"{l} [{Range(script.Nodes[l])}]")),
        _ => string.Join(", ", node.Links),
    };

    private static string Range(Borg13Node node) =>
        node.Max == Borg13Node.NoUpperBound ? $"{node.Min}+" : node.Min == node.Max ? $"{node.Min}" : $"{node.Min}-{node.Max}";
}
