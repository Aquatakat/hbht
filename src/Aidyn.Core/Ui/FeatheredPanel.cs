namespace Aidyn.Core.Ui;

/// <summary>
/// A translucent black panel with feathered edges, as the original's popups draw it
/// (<c>WidgetChoiceDia::SetImages</c>): a band, a gradient along each side fading outward, and a
/// rounded piece at each corner. The band and the gradients' inner edges have the same alpha, 136.
/// </summary>
/// <param name="Band">The panel's solid part.</param>
/// <param name="Top">How far the feathering reaches above the band. The others are below it and beside it.</param>
public readonly record struct FeatheredPanel(ScreenRect Band, int Top, int Bottom, int Left, int Right)
{
    public FeatheredPanel Offset(int x, int y) =>
        this with { Band = new ScreenRect(Band.Left + x, Band.Top + y, Band.Right + x, Band.Bottom + y) };
}
