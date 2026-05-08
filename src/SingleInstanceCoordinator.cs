using System.Diagnostics;
using System.IO.MemoryMappedFiles;

namespace NetworkTrayAppWPF;

/// <summary>
/// Owns the single-instance Mutex and PID-bulletin MMF for the watcher's lifetime.
/// On construction, kills any existing watcher/monitored tree keyed by the app GUID
/// and claims ownership.
/// On disposal, releases the mutex so the next launch sees a clean slate.
/// </summary>
internal sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _view;
    private bool _disposed;

    private SingleInstanceCoordinator(Mutex mutex, MemoryMappedFile mmf, MemoryMappedViewAccessor view)
    {
        _mutex = mutex;
        _mmf = mmf;
        _view = view;
    }

    /// <summary>
    /// Claims single-instance ownership.
    /// If another watcher owns it, kills that process tree
    /// (plus any orphan monitored child) before taking over.
    /// </summary>
    public static SingleInstanceCoordinator AcquireOrTakeover()
    {
        Mutex mutex = new(initiallyOwned: true, AppIdentity.SingleInstanceMutexName, out bool createdNew);

        if (!createdNew) TakeoverFromExistingOwner(mutex);

        MemoryMappedFile mmf = MemoryMappedFile.CreateOrOpen(AppIdentity.PIDMmfName, AppIdentity.MmfSize);
        MemoryMappedViewAccessor view = mmf.CreateViewAccessor(0, AppIdentity.MmfSize);

        int generation = view.ReadInt32(AppIdentity.OffsetGeneration);
        view.Write(AppIdentity.OffsetGeneration, generation + 1);
        view.Write(AppIdentity.OffsetWatcherPID, Environment.ProcessId);
        view.Write(AppIdentity.OffsetMonitoredPID, 0);

        return new SingleInstanceCoordinator(mutex, mmf, view);
    }

    /// <summary>Writes the monitored child's PID into the MMF after launch/restart.</summary>
    public void RecordMonitoredPID(int pid)
    {
        if (_disposed) return;

        _view.Write(AppIdentity.OffsetMonitoredPID, pid);
    }

    private static void TakeoverFromExistingOwner(Mutex mutex)
    {
        int oldWatcherPID = 0;
        int oldMonitoredPID = 0;
        int generation = 0;

        try
        {
            using MemoryMappedFile existing = MemoryMappedFile.OpenExisting(AppIdentity.PIDMmfName);
            using MemoryMappedViewAccessor view = existing.CreateViewAccessor(
                0, AppIdentity.MmfSize, MemoryMappedFileAccess.Read);
            generation = view.ReadInt32(AppIdentity.OffsetGeneration);
            oldWatcherPID = view.ReadInt32(AppIdentity.OffsetWatcherPID);
            oldMonitoredPID = view.ReadInt32(AppIdentity.OffsetMonitoredPID);
        }
        catch
        {
            // MMF gone or unreadable - the mutex holder must be dying. Fall through to the wait below.
        }

        if (generation != 0)
        {
            // Kill the watcher tree first - this normally reaps the monitored child too.
            KillByPID(oldWatcherPID);
            // Belt-and-suspenders for the orphan-monitored case (watcher already dead, child still alive).
            // No-op if Kill(entireProcessTree) already got it.
            if (oldMonitoredPID != 0 && oldMonitoredPID != oldWatcherPID) KillByPID(oldMonitoredPID);
        }

        // Old owner is dead; mutex should now be abandoned. Claim it.
        try
        {
            if (!mutex.WaitOne(TimeConstants.SingleInstanceMutexAcquireTimeoutMs))
                throw new InvalidOperationException("Timed out waiting for single-instance mutex.");
        }
        catch (AbandonedMutexException)
        {
            // Expected - previous owner was killed. We now own the mutex.
        }
    }

    private static void KillByPID(int pid)
    {
        if (pid <= 0) return;

        try
        {
            using Process proc = Process.GetProcessById(pid);
            proc.Kill(entireProcessTree: true);
            proc.WaitForExit(5000);
        }
        catch (ArgumentException)
        {
            // PID not running - stale entry, ignore.
        }
        catch (InvalidOperationException)
        {
            // Process already exited between lookup and Kill.
        }
        catch
        {
            // Access denied or other - nothing actionable; takeover will still proceed.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;

        try { _view.Dispose(); }
        catch
        {
            // ignored
        }

        try { _mmf.Dispose(); }
        catch
        {
            // ignored
        }

        try { _mutex.ReleaseMutex(); }
        catch
        {
            // ignored
        }

        try { _mutex.Dispose(); }
        catch
        {
            // ignored
        }
    }
}
