namespace HBHT.Core.Cameras;

/// <summary>
/// The Camera setting, on the options' Enhancements page: the original's cameras, or the port's enhanced ones. For now it's a battle's
/// camera played turn by turn (<see cref="Combat.EnhancedCamera"/>); it may carry trek's camera later.
/// </summary>
public enum CameraStyle
{
    /// <summary>The original's cameras, as 1.0 moves them.</summary>
    Original,

    /// <summary>
    /// A battle's camera directed: the original's shots with slower lead-ins and clear sight lines, the player's character framed with
    /// whom they point at, aiming zoomed out to its whole reach, and its acceleration capped (docs/systems/cameras.md).
    /// </summary>
    Enhanced,
}
