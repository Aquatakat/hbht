using HBHT.Core.Cameras;
using HBHT.Core.Trek;
using HBHT.Plugins;

/// <summary>
/// <c>hbht camera measure</c>: the enhanced trek camera's path's bend, slopes, nudges and sneaking measured headless on real
/// walks (<see cref="CameraMeasures"/>), under both cameras (docs/systems/cameras.md#the-enhanced-trek-camera-camera-enhanced).
/// </summary>
internal static class CameraMeasureReport
{
    public const string Usage = """
          hbht camera measure [<walk prefix>]
                                               the enhanced trek camera's bend, slope, nudges and sneaking over fixed
                                               walks, under both cameras: how far forward alone gets and how much of it
                                               is pressed against the trees, the leans, the pitch against the slope, and
                                               the share of a sneak the hunter is in the frame
        """;

    public static int Run(PluginSet plugins, string[] args)
    {
        var prefix = args.FirstOrDefault() ?? "";
        var results = new List<MeasureResult>();
        foreach (var walk in MeasureWalk.Standard.Where(w => w.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            foreach (var style in new[] { CameraStyle.Original, CameraStyle.Enhanced })
                results.Add(CameraMeasures.Run(plugins, walk, style));
        Console.Write(CameraMeasures.Table(results));
        return 0;
    }
}
