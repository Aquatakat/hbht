using Aidyn.Rom.Inspection;
using Godot;

namespace Aidyn.Inspector;

/// <summary>A <see cref="FieldNode"/> tree, whose branches are filled in when first expanded.</summary>
public partial class FieldsView : Tree
{
    private readonly Dictionary<TreeItem, FieldNode> _pending = [];

    public FieldsView()
    {
        Columns = 2;
        HideRoot = true;
        ColumnTitlesVisible = true;
        SetColumnTitle(0, "Field");
        SetColumnTitle(1, "Value");
        SetColumnExpandRatio(0, 1);
        SetColumnExpandRatio(1, 2);
        SelectMode = SelectModeEnum.Row;
        ItemCollapsed += OnItemCollapsed;
    }

    /// <summary>Shows the given roots, with the first expanded.</summary>
    public void Show(params FieldNode[] roots)
    {
        Clear();
        _pending.Clear();
        var root = CreateItem();
        foreach (var node in roots)
            Add(root, node);
        if (root.GetFirstChild() is { } first)
            first.Collapsed = false;
    }

    private void Add(TreeItem parent, FieldNode node)
    {
        var item = CreateItem(parent);
        item.SetText(0, node.Name);
        item.SetText(1, node.Value);
        item.SetTooltipText(1, node.Value);
        if (node.HasChildren)
        {
            // A placeholder child makes the item expandable until it's filled in.
            CreateItem(item);
            _pending[item] = node;
            item.Collapsed = true;
        }
    }

    private void OnItemCollapsed(TreeItem item)
    {
        if (item.Collapsed || !_pending.Remove(item, out var node))
            return;
        item.GetFirstChild()?.Free();
        foreach (var child in node.Children)
            Add(item, child);
    }
}
