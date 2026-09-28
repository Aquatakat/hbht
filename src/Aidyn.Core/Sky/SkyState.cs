namespace Aidyn.Core.Sky;

/// <summary>
/// Which sky is up (<c>gGlobals.sky</c>): a gradient, and the one it's changing to. Entering a
/// chunk chooses its terrain's sky (<c>BorgMaps::GetMapTerrain</c>, then
/// <c>Sky::SetBackgroundType</c> with a change of 600 ticks).
/// </summary>
/// <remarks>
/// Every choice restarts the change, even of the sky that's already coming, and choosing the sky
/// that's up abandons it. A sky that starts or stops being drawn behind the scenery changes at once.
/// </remarks>
public sealed class SkyState
{
    /// <summary>How long a change of sky takes.</summary>
    public const int ChangeTicks = 600;

    private SkyGradient? _next;

    public SkyGradient? Current { get; private set; }
    public bool Drawn { get; private set; }
    /// <summary>The sky being changed to, while it is.</summary>
    public SkyGradient? Next => Changing ? _next : null;
    public bool Changing { get; private set; }
    /// <summary>How far the change has got, 0 to 1.</summary>
    public float Change { get; private set; }

    /// <param name="gradient">The same instance for the same sky, as the original compares sky numbers.</param>
    /// <param name="drawn">Whether the gradient is drawn behind the scenery.</param>
    /// <param name="gradual">False to change at once, as loading a game does.</param>
    public void Choose(SkyGradient gradient, bool drawn, bool gradual = true)
    {
        Change = 0;
        Changing = false;
        if (gradient == Current && drawn == Drawn)
            return;
        if (!gradual || Current is null || drawn != Drawn)
        {
            (Current, Drawn) = (gradient, drawn);
            return;
        }
        _next = gradient;
        Changing = true;
    }

    /// <summary>Advances the change by <paramref name="ticks"/> (<c>Sky::RenderSky</c>).</summary>
    public void Step(int ticks = 1)
    {
        if (!Changing)
            return;
        Change += ticks / (float)ChangeTicks;
        if (Change >= 1)
        {
            Current = _next;
            Change = 0;
            Changing = false;
        }
    }

    /// <summary>The colours at <paramref name="timeOfDay"/>.</summary>
    public SkyColors Colors(float timeOfDay) =>
        Current is null ? throw new InvalidOperationException("No sky has been chosen.") : SkyColors.At(Current, Next, Change, timeOfDay);
}
