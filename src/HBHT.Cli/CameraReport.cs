using HBHT.Core.Cameras;
using HBHT.Core.Trek;
using HBHT.Plugins;

/// <summary>
/// <c>hbht camera trace</c>: trek's camera traced headless over the fixed walks (<see cref="CameraTrace"/>), under the original's
/// camera and the enhanced one, as a table (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced).
/// </summary>
internal static class CameraReport
{
    public const string Usage = """
          hbht camera trace [original|enhanced]
                                               trek's camera over fixed walks (into GwerniaCastle M05 and down its
                                               hallway, in at I07's entrance, along C15's forest path into its bend),
                                               under both cameras or the one given: the share of ticks the view ahead
                                               is blocked close by, the camera is past a wall, the player is hidden and
                                               he's seen from the front while moving, and how sharply the view turns
          hbht camera census                   every teleporter arrived through, held forward 3 s, under both cameras:
                                               the shares ever past a wall and showing his front, interiors and outdoors
        """;

    public static int Census(PluginSet plugins)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Console.Write(CameraCensus.Report(CameraCensus.Run(plugins)));
        Console.WriteLine($"{watch.Elapsed.TotalSeconds:F0} s");
        return 0;
    }

    public static int Run(PluginSet plugins, string[] args)
    {
        CameraStyle[] styles = args switch
        {
            [] => [CameraStyle.Original, CameraStyle.Enhanced],
            ["original"] => [CameraStyle.Original],
            ["enhanced"] => [CameraStyle.Enhanced],
            _ => [],
        };
        if (styles.Length == 0)
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }
        var results = new List<CameraTraceResult>();
        foreach (var walk in CameraWalk.Standard)
            foreach (var style in styles)
                results.Add(CameraTrace.Run(plugins, walk, style));
        Console.Write(CameraTrace.Table(results));
        return 0;
    }
}
