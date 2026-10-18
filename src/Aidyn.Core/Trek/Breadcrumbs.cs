using System.Numerics;
using Aidyn.Plugins;

namespace Aidyn.Core.Trek;

/// <summary>Where the player stood in trek, kept as the original keeps it (<c>SaveZoneEngine</c>): the chunk, and where in it, and the way he faced.</summary>
public readonly record struct Breadcrumb(RecordId MapSet, int Row, int Column, Vector3 Local, Vector2 Heading);

/// <summary>
/// The breadcrumbs (<c>Zones.Breadcrumbs</c>, <c>0x800D90E0</c>): the last 15 places the player stood in trek, kept every 120
/// ticks but where one already kept in the map set is nearer than 10, in a ring. Fleeing a battle puts the party at the
/// one in the map set farthest from where it began (<c>Zones.FindBreadcrumb</c>, <c>0x80009A48</c>). Every time trek is built
/// again, all 15 are where the player stands (<c>Zones.ResetBreadcrumbs</c>, <c>0x8000DF00</c>). They aren't saved.
/// </summary>
/// <remarks>
/// As the original, a chunk's distance from another is a literal 100 a row or column, whatever the map set's cell size,
/// and the test for one nearer than 10 compares the places within their chunks, as though both were in the same one. Checked
/// in 1.0's code and the combat checkpoints 01 and 02.
/// </remarks>
public sealed class Breadcrumbs
{
    /// <summary>How many are kept.</summary>
    public const int Count = 15;

    /// <summary>How often one is kept, in ticks (<c>Zones.BreadcrumbTimer</c>'s 0x78).</summary>
    public const int Ticks = 120;

    /// <summary>A place nearer than this to one kept isn't kept.</summary>
    public const float Apart = 10;

    /// <summary>A chunk's span in the distances between them (the original's literal).</summary>
    public const float ChunkSpan = 100;

    private readonly Breadcrumb[] _crumbs = new Breadcrumb[Count];
    private int _next;
    private int _timer;

    public IReadOnlyList<Breadcrumb> All => _crumbs;

    /// <summary>All of them where the player stands, as trek is built again (<c>Zones.ResetBreadcrumbs</c>).</summary>
    public void Reset(Breadcrumb here)
    {
        Array.Fill(_crumbs, here);
        _next = 0;
        _timer = 0;
    }

    /// <summary>
    /// A tick of trek's zone engine (<c>HandleZoneEngineFrame</c>, <c>0x8000E784</c>): every 120 ticks, where the player
    /// stands is kept, unless the nearest kept in the map set is nearer than 10 within its chunk.
    /// </summary>
    public void Tick(Breadcrumb here)
    {
        if (--_timer > 0)
            return;
        _timer = Ticks;
        if (Find(here, farthest: false) is { } near && Vector3.Distance(near.Local, here.Local) < Apart)
            return;
        _crumbs[_next] = here;
        _next = (_next + 1) % Count;
    }

    /// <summary>
    /// The nearest kept in <paramref name="from"/>'s map set, the last of equals, or the farthest, the last of equals
    /// (<c>Zones.FindBreadcrumb</c>), or null with none there.
    /// </summary>
    public Breadcrumb? Find(Breadcrumb from, bool farthest)
    {
        Breadcrumb? found = null;
        var best = farthest ? 0f : 100000f;
        foreach (var crumb in _crumbs)
        {
            if (crumb.MapSet != from.MapSet)
                continue;
            var d = Vector3.Distance(Global(from), Global(crumb));
            if (farthest ? d >= best : d <= best)
            {
                best = d;
                found = crumb;
            }
        }
        return found;
    }

    private static Vector3 Global(Breadcrumb crumb) => crumb.Local + new Vector3(crumb.Row, 0, crumb.Column) * ChunkSpan;
}
