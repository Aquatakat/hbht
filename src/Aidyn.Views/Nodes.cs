using Godot;

namespace Aidyn.Views;

/// <summary>Searches of node trees.</summary>
public static class Nodes
{
    /// <summary>Every node of a type under <paramref name="root"/>, and the root itself if it is one.</summary>
    public static List<T> FindAll<T>(Node root) where T : Node
    {
        var found = new List<T>();
        var stack = new Stack<Node>([root]);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (node is T t)
                found.Add(t);
            foreach (var child in node.GetChildren())
                stack.Push(child);
        }
        return found;
    }
}
