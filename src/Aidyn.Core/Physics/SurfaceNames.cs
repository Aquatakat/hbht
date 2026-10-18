namespace Aidyn.Core.Physics;

/// <summary>The collision surfaces' names, for the debug overlay and the editor's collision view.</summary>
public static class SurfaceNames
{
    /// <summary>The 26 surfaces' names, as <c>String/Common</c> gives them, by surface number.</summary>
    public static readonly IReadOnlyList<string> All =
    [
        "Default", "Player and camera", "Player", "Snow", "Grass", "Sand", "Mucky", "Sizzle", "Cave", "Wood",
        "Wood echo", "Stone", "Stone echo", "Stone huge echo", "Water", "Trans snow", "Trans grass", "Trans sand",
        "Trans mucky", "Trans sizzle", "Trans cave", "Trans wood", "Trans wood echo", "Trans stone", "Trans stone echo",
        "Trans stone huge echo",
    ];

    /// <summary>A surface's name, or "Surface n" for a number the table lacks.</summary>
    public static string Of(int surface) => surface >= 0 && surface < All.Count ? All[surface] : $"Surface {surface}";
}
