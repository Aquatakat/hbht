using System.Numerics;
using HBHT.Plugins;

namespace HBHT.Core.Trek;

/// <summary>
/// Where the player stood in trek, kept as the original keeps it (<c>SaveZoneEngine</c>, <c>0x8000E3D4</c>): the chunk, where in
/// it (+0x0), the way he faced (+0xC, the body's <c>+0x20</c>) and the follow camera's eye (+0x14, <c>Camera.Listener</c>), the
/// last within the same chunk as <paramref name="Local"/>; and whether he stood on the ground there, which the original doesn't
/// keep, for the port's rescue from a fall (<see cref="Breadcrumbs.Rescue"/>).
/// </summary>
public readonly record struct Breadcrumb(RecordId MapSet, int Row, int Column, Vector3 Local, Vector2 Heading, Vector3 Eye, bool Grounded = true);

/// <summary>
/// The breadcrumbs (<c>Zones.Breadcrumbs</c>, <c>0x800D90E0</c>): the last 15 places the player stood in trek, kept every 120
/// ticks but where one already kept in the map set is nearer than 10, in a ring. Fleeing a battle puts the party at the
/// one in the map set farthest from where it began (<c>Zones.FindBreadcrumb</c>, <c>0x80009A48</c>), facing as he did there and
/// with the camera's eye where it was then (<see cref="TrekLoop.Resume"/>). Every time trek is built
/// again, all 15 are where the player stands (<c>Zones.ResetBreadcrumbs</c>, <c>0x8000DF00</c>). They aren't saved. The port
/// also puts the player back at one when he falls out of the world (<see cref="Rescue"/>).
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
    private readonly bool[] _tried = new bool[Count];
    private int _next;
    private int _timer;

    public IReadOnlyList<Breadcrumb> All => _crumbs;

    /// <summary>A count that goes up with each one kept, and as all are reset.</summary>
    public int Kept { get; private set; }

    /// <summary>All of them where the player stands, as trek is built again (<c>Zones.ResetBreadcrumbs</c>).</summary>
    public void Reset(Breadcrumb here)
    {
        Array.Fill(_crumbs, here);
        Array.Clear(_tried);
        _next = 0;
        _timer = 0;
        Kept++;
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
        _tried[_next] = false;
        _next = (_next + 1) % Count;
        Kept++;
    }

    /// <summary>
    /// Where the player is put back after falling out of the world, the port's: the newest kept in <paramref name="mapSet"/>
    /// where he stood on the ground and that no rescue has tried since it was kept, which this marks tried, with every other
    /// kept at the same place (all 15 are one after trek is built). Null once all have been tried.
    /// </summary>
    public Breadcrumb? Rescue(RecordId mapSet)
    {
        for (var i = 1; i <= Count; i++)
        {
            var slot = (_next - i + Count) % Count;
            var crumb = _crumbs[slot];
            if (_tried[slot] || !crumb.Grounded || crumb.MapSet != mapSet)
                continue;
            for (var j = 0; j < Count; j++)
                if (_crumbs[j] with { Heading = default, Eye = default } == crumb with { Heading = default, Eye = default })
                    _tried[j] = true;
            return crumb;
        }
        return null;
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
