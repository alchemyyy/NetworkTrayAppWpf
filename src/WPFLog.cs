using System.Diagnostics;
using System.IO;
using System.Text;

namespace NetworkTrayAppWPF;

/// <summary>
/// Singular file-backed logger for the app. Calls to <see cref="Log"/> append a timestamped line to an in-memory buffer
/// that is flushed to disk every ~2 seconds, so logging stays cheap on hot paths and a process kill loses at most ~2s.
///
/// Two files live alongside settings at <c>%LOCALAPPDATA%\TrayAppWPF\&lt;ApplicationName&gt;\</c>:
///   * <c>active.log</c>: current.
///   * <c>old.log</c>: previous rollover.
/// Either trigger rotates - active file at or above 10 MB, or active file at least 7 days old.
/// On rotation, old is deleted, active is renamed to old, and a fresh active starts.
///
/// The logger is best-effort: it never throws back to callers. I/O failures are swallowed
/// so the app keeps running even if the log directory is read-only or the file is locked.
/// </summary>
internal static class WPFLog
{
    private const long MaxBytes = 10L * 1024 * 1024;
    private const string ActiveName = "active.log";
    private const string OldName = "old.log";
    private const string TimestampFmt = "yyyy-MM-dd HH:mm:ss.fff";
    private const int InitialBufferCapacity = 64;

    private static readonly Lock _gate = new();
    private static List<string> _buffer = new(InitialBufferCapacity);
    private static System.Threading.Timer? _timer;
    private static string? _activePath;
    private static string? _oldPath;
    private static bool _initialized;
    private static bool _shuttingDown;

    /// <summary>
    /// Resolves log file paths and starts the periodic flush timer.
    /// Idempotent - safe to call from both <c>Program.Main</c> and <c>App.OnStartup</c>; subsequent calls are no-ops.
    /// </summary>
    public static void Initialize()
    {
        lock (_gate)
        {
            if (_initialized) return;
            try
            {
                string folder = Program.AppLocalAppDataDirectory;
                Directory.CreateDirectory(folder);
                _activePath = Path.Combine(folder, ActiveName);
                _oldPath = Path.Combine(folder, OldName);
                _timer = new System.Threading.Timer(
                    OnTimerTick, null, TimeConstants.LogFlushIntervalMs, TimeConstants.LogFlushIntervalMs);
                _initialized = true;
            }
            catch
            {
                // Swallow. Logger stays uninitialized; subsequent Log() calls become no-ops.
            }
        }
    }

    /// <summary>
    /// Enqueues a single line for the next flush. Auto-prepends a timestamp. Never throws.
    /// </summary>
    public static void Log(string? message)
    {
        try
        {
            if (!_initialized) Initialize();
            if (!_initialized) return;

            string timestamp = DateTime.Now.ToString(TimestampFmt);
            string formatted = "[" + timestamp + "] " + (message ?? string.Empty) + "\n";

            lock (_gate)
            {
                if (_shuttingDown) return;
                _buffer.Add(formatted);
            }

#if DEBUG
            // Mirror to VS Output window during dev. Stripped at compile time in Release.
            Debug.WriteLine(formatted.TrimEnd('\n'));
#endif
        }
        catch
        {
            // Logger must never throw back to callers.
        }
    }

    /// <summary>
    /// Synchronously drains the in-memory buffer to disk without tearing down the logger.
    /// Use at intermediate exit points (crash handlers, SessionEnding, ExitApplication)
    /// where more code may still want to log before the process actually dies. Non-throwing.
    /// </summary>
    public static void Flush() => FlushBatch();

    /// <summary>
    /// Stops the flush timer and performs a final synchronous drain.
    /// Once called, future <see cref="Log"/> calls become no-ops,
    /// so reserve this for the very last termination point (App's ProcessExit handler).
    /// Everywhere else, prefer <see cref="Flush"/>. Safe to call multiple times.
    /// </summary>
    public static void Shutdown()
    {
        System.Threading.Timer? timerToDispose;
        lock (_gate)
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            timerToDispose = _timer;
            _timer = null;
        }

        try
        {
            if (timerToDispose != null)
            {
                using ManualResetEvent done = new(false);
                if (timerToDispose.Dispose(done)) done.WaitOne(TimeConstants.LogShutdownTimerWaitMs);
            }
        }
        catch
        {
            // ignored
        }

        FlushBatch();
    }

    private static void OnTimerTick(object? state)
    {
        if (_shuttingDown) return;
        FlushBatch();
    }

    private static void FlushBatch()
    {
        List<string> batch;
        lock (_gate)
        {
            if (_buffer.Count == 0) return;
            batch = _buffer;
            _buffer = new List<string>(InitialBufferCapacity);
        }

        try
        {
            EnsureRotated();
            if (_activePath == null) return;

            using FileStream fs = new(_activePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using StreamWriter sw = new(fs, Encoding.UTF8);
            int count = batch.Count;
            for (int i = 0; i < count; i++) sw.Write(batch[i]);
        }
        catch
        {
            // Disk full, file locked, etc. Drop the batch on the floor - logging must not crash the app.
        }
    }

    private static void EnsureRotated()
    {
        if (_activePath == null || _oldPath == null) return;
        if (!File.Exists(_activePath)) return;

        FileInfo fi = new(_activePath);
        bool sizeOver = fi.Length >= MaxBytes;
        bool ageOver = (DateTime.UtcNow - fi.CreationTimeUtc).TotalMilliseconds >= TimeConstants.LogMaxAgeMs;
        if (!sizeOver && !ageOver) return;

        try { if (File.Exists(_oldPath)) File.Delete(_oldPath); }
        catch
        {
            // ignored
        }

        try { File.Move(_activePath, _oldPath); } catch { return; }

        // Recreate active.log immediately and pin its creation time so the age trigger has a stable anchor
        // even on filesystems that inherit creation time from the parent.
        try
        {
            using (File.Create(_activePath)) { }
            File.SetCreationTimeUtc(_activePath, DateTime.UtcNow);
        }
        catch
        {
            // ignored
        }
    }
}
