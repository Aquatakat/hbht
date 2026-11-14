using System.Collections.Concurrent;
using Godot;

namespace HBHT.Game;

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

    /// <summary>Whether it's stopping, which a long job checks between its steps to stop early.</summary>
    public bool Stopping => _stopping;

    private volatile bool _stopping;

    /// <summary>
    /// Stops once the job under way is done, leaving the rest, and waits for it however long it takes: Godot frees everything
    /// once the tree has gone, and a job still running then used what was freed, which crashed the game as it quit
    /// (runtime.md#loading-chunks). The job waits on the main thread for each call into the renderer that returns something,
    /// which the main thread serves between frames, so here it serves them as fast as they come, without sleeping: a sleep
    /// of Windows' 15.6 ms tick a call made a scenery build's thousand or so calls take 16 s. The thread works below the
    /// main thread's priority, and is raised above it to finish, as a busy machine can leave it waiting for its turn.
    /// </summary>
    public void Dispose()
    {
        _stopping = true;
        _builds.CompleteAdding();
        _reads.CompleteAdding();
        while (_builds.TryTake(out _))
        {
        }
        DropReads();
        try
        {
            _thread.Priority = ThreadPriority.AboveNormal;
        }
        catch (ThreadStateException)
        {
            // It has already stopped.
        }
        while (!_thread.Join(0))
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
