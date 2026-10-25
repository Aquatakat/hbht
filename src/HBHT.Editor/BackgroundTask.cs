using Godot;

namespace HBHT.Editor;

/// <summary>Tasks the editor starts and doesn't wait for.</summary>
public static class BackgroundTask
{
    /// <summary>Prints what a fire-and-forget task threw, which would otherwise vanish, and hands it to <paramref name="failed"/> too.</summary>
    public static async Task Report(Task task, Action<Exception>? failed = null)
    {
        try
        {
            await task;
        }
        catch (Exception e)
        {
            GD.PrintErr(e.ToString());
            failed?.Invoke(e);
        }
    }
}
