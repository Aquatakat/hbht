using System.Collections.Concurrent;
using Godot;

namespace Aidyn.Game;

/// <summary>
/// The thread that builds trek's chunks' scenery off the main thread, and reads ahead what the next crossing could load
/// (docs/adr/0025-chunks-load-off-the-main-thread.md). It runs one job at a time, a build before any reading ahead. A job
/// only makes nodes outside the tree and fills caches that give the same whenever they're filled; what it makes reaches the
/// game on the main thread, which adds it. The main thread never waits on it while a frame runs (see <see cref="Models"/>):
/// Godot's renderer serves its calls on the main thread, between frames.
/// </summary>
public sealed class ChunkBuilder : IDisposable
{
    private readonly BlockingCollection<Action> _builds = new();
    private readonly BlockingCollection<Action> _reads = new();
    private readonly Thread _thread;

    public ChunkBuilder()
    {
        _thread = new Thread(Run) { Name = "Chunk builder", IsBackground = true, Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    /// <summary>Builds something the game is waiting for, before anything read ahead.</summary>
    public void Build(Action job) => _builds.Add(job);

    /// <summary>Reads something ahead of its being needed, once nothing waits to be built.</summary>
    public void ReadAhead(Action job) => _reads.Add(job);

    /// <summary>How many reads ahead wait, which a new centre makes stale.</summary>
    public int ReadsWaiting => _reads.Count;

    /// <summary>Drops the reads ahead that haven't started, as a new centre makes them stale.</summary>
    public void DropReads()
    {
        while (_reads.TryTake(out _))
        {
        }
    }

    /// <summary>
    /// Stops once the job under way is done, leaving the rest. The job may be waiting on the renderer, which serves another
    /// thread's calls on the main thread between frames, so the main thread serves them while it waits (a few seconds at most).
    /// </summary>
    public void Dispose()
    {
        _builds.CompleteAdding();
        _reads.CompleteAdding();
        while (_builds.TryTake(out _))
        {
        }
        DropReads();
        for (var waited = 0; waited < 500 && !_thread.Join(10); waited++)
            RenderingServer.ForceSync();
    }

    private void Run()
    {
        BlockingCollection<Action>[] queues = [_builds, _reads];
        while (true)
        {
            Action? job;
            try
            {
                // In the queues' order, so a build goes before a read ahead.
                BlockingCollection<Action>.TakeFromAny(queues, out job);
            }
            catch (ArgumentException)
            {
                // Both are complete and empty.
                return;
            }
            try
            {
                job!();
            }
            catch (Exception e)
            {
                GD.PushError($"Building a chunk off the main thread failed: {e}");
            }
        }
    }
}
