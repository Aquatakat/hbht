using Godot;

namespace Aidyn.Inspector;

/// <summary>Where a graph's zoom and snapping controls go.</summary>
internal static class GraphMenu
{
    private const float Margin = 12;

    /// <summary>
    /// Moves the controls from the top left, where they cover the graph's first node, to the bottom
    /// right, beside the minimap.
    /// </summary>
    public static void MoveToBottom(GraphEdit graph)
    {
        var hbox = graph.GetMenuHBox();
        var menu = hbox.GetParent() is Control panel && panel != graph ? panel : hbox;
        menu.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        menu.GrowHorizontal = Control.GrowDirection.Begin;
        menu.GrowVertical = Control.GrowDirection.Begin;
        var size = menu.GetCombinedMinimumSize();
        menu.OffsetRight = -(graph.MinimapSize.X + 2 * Margin);
        menu.OffsetBottom = -Margin;
        menu.OffsetLeft = menu.OffsetRight - size.X;
        menu.OffsetTop = menu.OffsetBottom - size.Y;
    }
}
