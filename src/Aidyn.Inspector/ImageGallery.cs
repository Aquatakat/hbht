using Godot;

namespace Aidyn.Inspector;

/// <summary>Images side by side, each with a label, blown up in whole steps and drawn with nearest filtering.</summary>
public static class ImageGallery
{
    /// <param name="Opened">Called when the image is clicked, if it links somewhere.</param>
    public sealed record Item(string Label, Image Image, Action? Opened = null, string? Tooltip = null);

    /// <summary>
    /// A scrolling gallery. All images get the same zoom, which blows the largest up to about 256
    /// pixels, so mip levels keep their scale.
    /// </summary>
    public static ScrollContainer Create(IEnumerable<Item> items)
    {
        var list = items.ToList();
        var flow = new HFlowContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 16);
        flow.AddThemeConstantOverride("v_separation", 12);
        var largest = list.Count == 0 ? 1 : list.Max(i => Math.Max(i.Image.GetWidth(), i.Image.GetHeight()));
        var zoom = Math.Max(1, 256 / Math.Max(1, largest));
        foreach (var item in list)
        {
            var size = new Vector2(item.Image.GetWidth(), item.Image.GetHeight()) * zoom;
            var box = new VBoxContainer { TooltipText = item.Tooltip ?? "" };
            box.AddChild(new Label { Text = item.Label });
            var backdrop = new Panel { CustomMinimumSize = size, SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin, TooltipText = item.Tooltip ?? "" };
            backdrop.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.3f, 0.3f, 0.3f) });
            backdrop.AddChild(new TextureRect
            {
                Texture = ImageTexture.CreateFromImage(item.Image),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Size = size,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            });
            if (item.Opened is { } opened)
            {
                backdrop.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
                backdrop.GuiInput += e =>
                {
                    if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                        opened();
                };
            }
            box.AddChild(backdrop);
            flow.AddChild(box);
        }
        var scroll = new ScrollContainer();
        scroll.AddChild(flow);
        return scroll;
    }
}
