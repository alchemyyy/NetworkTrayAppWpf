using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace NetworkTrayAppWpf;

/// <summary>
/// Monitors and cleans up explorer.exe processes spawned by control panel applets.
/// Uses SetWinEventHook for fully event-driven window detection and destruction.
/// </summary>
internal static class AdapterSettingsShellProcessMonitor
{
    // Not going to bother extracting the GUIDs and running a fancy set
    private static readonly string[] ExplorerFactoryCommandLines =
    [
        "/factory,{5BD95610-9434-43C2-886C-57852CC8A120} -Embedding",  // Control Panel (ncpa.cpl)
        "/factory,{75dff2b7-6936-4c06-a8bb-676a7b00b24b} -Embedding"   // Explorer shell
    ];
    private const string TargetWindowClass = "CabinetWClass";

    private static readonly Lock _lock = new();
    private static readonly HashSet<int> _monitoredPids = [];

    public static void OpenAndMonitorControlPanel()
    {
        OpenAndMonitor("ncpa.cpl", null);
    }

    public static void OpenAndMonitorExplorerShell()
    {
        OpenAndMonitor("explorer.exe", "shell:::{7007ACC7-3202-11D1-AAD2-00805FC1270E}");
    }

    private static void OpenAndMonitor(string fileName, string? arguments)
    {
        try
        {
            HashSet<int> existingPids = GetExplorerFactoryPids();

            // Also exclude PIDs we're already monitoring
            lock (_lock)
            {
                existingPids.UnionWith(_monitoredPids);
            }

            // Start event-driven monitoring on a dedicated thread BEFORE launching
            ProcessMonitor monitor = new ProcessMonitor(existingPids);
            monitor.Start();

            // Wait for hook to be set up
            monitor.WaitForReady();

            Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? "",
                UseShellExecute = true
            })?.Dispose();
        }
        catch
        {
            // ignored
        }
    }

    private static void AddMonitoredPid(int pid)
    {
        lock (_lock)
        {
            _monitoredPids.Add(pid);
        }
    }

    private static void RemoveMonitoredPid(int pid)
    {
        lock (_lock)
        {
            _monitoredPids.Remove(pid);
        }
    }

    private static HashSet<int> GetExplorerFactoryPids()
    {
        HashSet<int> pids = [];
        foreach (Process proc in Process.GetProcessesByName("explorer"))
        {
            try
            {
                if (IsFactoryExplorer(proc.Id))
                    pids.Add(proc.Id);
            }
            catch
            {
                // ignored
            }
            finally
            {
                proc.Dispose();
            }
        }
        return pids;
    }

    private static bool IsFactoryExplorer(int pid)
    {
        try
        {
            using ManagementObjectSearcher searcher = new(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (ManagementBaseObject obj in searcher.Get())
            {
                string? cmdLine = obj["CommandLine"]?.ToString();
                if (cmdLine != null)
                {
                    foreach (string factoryCmd in ExplorerFactoryCommandLines)
                    {
                        if (cmdLine.Contains(factoryCmd, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
            }
        }
        catch
        {
            // ignored
        }
        return false;
    }

    #region P/Invoke

    private const uint EVENT_OBJECT_CREATE = 0x8000;
    private const uint EVENT_OBJECT_DESTROY = 0x8001;
    private const uint EVENT_OBJECT_SHOW = 0x8002;
    private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    private const uint WM_QUIT = 0x0012;
    private const int OBJID_WINDOW = 0;

    private delegate void WinEventDelegate(
        IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
        int idChild, uint dwEventThread, uint dwmsEventTime);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
        WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    [DllImport("user32.dll", EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll", EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    #endregion

    /// <summary>
    /// Phase 1: Monitors for new factory explorer process via EVENT_OBJECT_CREATE.
    /// Once found, hands off to MainWindowMonitor.
    /// </summary>
    private sealed class ProcessMonitor
    {
        private readonly HashSet<int> _existingPids;
        private readonly WinEventDelegate _winEventProc;
        private readonly ManualResetEventSlim _ready = new(false);
        private IntPtr _hook;
        private uint _threadId;

        // Prevents GC of the next monitor in the chain while it's running
        private MainWindowMonitor? _nextMonitorRef;

        public ProcessMonitor(HashSet<int> existingPids)
        {
            _existingPids = existingPids;
            _winEventProc = OnWinEvent;
        }

        public void Start()
        {
            new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "ProcessMonitor"
            }.Start();
        }

        public void WaitForReady() => _ready.Wait();

        private void RunMessageLoop()
        {
            _threadId = GetCurrentThreadId();

            try
            {
                _hook = SetWinEventHook(
                    EVENT_OBJECT_CREATE, EVENT_OBJECT_CREATE,
                    IntPtr.Zero, _winEventProc,
                    0, 0, WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    _ready.Set();
                    return;
                }

                _ready.Set();

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // ignored
            }
            finally
            {
                _ready.Set();
                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }
                _ready.Dispose();
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero)
                return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || _existingPids.Contains((int)pid))
                return;

            if (!IsFactoryExplorer((int)pid))
                return;

            // Found new factory explorer process
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            AddMonitoredPid((int)pid);

            // Hand off to main window monitor
            _nextMonitorRef = new MainWindowMonitor((int)pid);
            _nextMonitorRef.Start();

            PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }
    }

    /// <summary>
    /// Phase 2: Monitors for CabinetWClass window in the target process.
    /// Once found, hands off to WindowDestroyMonitor.
    /// </summary>
    private sealed class MainWindowMonitor
    {
        private readonly int _pid;
        private readonly WinEventDelegate _winEventProc;
        private IntPtr _hook;
        private uint _threadId;

        // Prevents GC of the next monitor in the chain while it's running
        private WindowDestroyMonitor? _nextMonitorRef;

        public MainWindowMonitor(int pid)
        {
            _pid = pid;
            _winEventProc = OnWinEvent;
        }

        public void Start()
        {
            new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "MainWindowMonitor"
            }.Start();
        }

        private void RunMessageLoop()
        {
            _threadId = GetCurrentThreadId();

            try
            {
                // Listen for window creation/show in this specific process
                _hook = SetWinEventHook(
                    EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW,
                    IntPtr.Zero, _winEventProc,
                    (uint)_pid, 0, WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    RemoveMonitoredPid(_pid);
                    return;
                }

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // ignored
            }
            finally
            {
                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            // Only handle CREATE and SHOW, not DESTROY (which is in the middle of the range)
            if (eventType != EVENT_OBJECT_CREATE && eventType != EVENT_OBJECT_SHOW)
                return;

            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero)
                return;

            // Check if this is the CabinetWClass window
            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, 256) > 0 && className.ToString() == TargetWindowClass)
            {
                // Found main window
                if (_hook != IntPtr.Zero)
                {
                    UnhookWinEvent(_hook);
                    _hook = IntPtr.Zero;
                }

                // Hand off to destroy monitor
                _nextMonitorRef = new WindowDestroyMonitor(_pid);
                _nextMonitorRef.Start();

                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    /// <summary>
    /// Phase 3: Monitors for window destruction, then kills the process.
    /// </summary>
    private sealed class WindowDestroyMonitor
    {
        private readonly int _pid;
        private readonly WinEventDelegate _winEventProc;
        private IntPtr _hook;
        private uint _threadId;

        public WindowDestroyMonitor(int pid)
        {
            _pid = pid;
            _winEventProc = OnWinEvent;
        }

        public void Start()
        {
            new Thread(RunMessageLoop)
            {
                IsBackground = true,
                Name = "WindowDestroyMonitor"
            }.Start();
        }

        private void RunMessageLoop()
        {
            _threadId = GetCurrentThreadId();

            try
            {
                _hook = SetWinEventHook(
                    EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
                    IntPtr.Zero, _winEventProc,
                    (uint)_pid, 0, WINEVENT_OUTOFCONTEXT);

                if (_hook == IntPtr.Zero)
                {
                    Cleanup();
                    return;
                }

                while (GetMessage(out MSG msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
            }
            catch
            {
                // ignored
            }
            finally
            {
                Cleanup();
            }
        }

        private void OnWinEvent(IntPtr hWinEventHook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (idObject != OBJID_WINDOW || hwnd == IntPtr.Zero)
                return;

            // Check if the destroyed window is a CabinetWClass (Explorer folder window)
            StringBuilder className = new(256);
            if (GetClassName(hwnd, className, 256) > 0 && className.ToString() == TargetWindowClass)
            {
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private void Cleanup()
        {
            if (_hook != IntPtr.Zero)
            {
                UnhookWinEvent(_hook);
                _hook = IntPtr.Zero;
            }

            RemoveMonitoredPid(_pid);

            try
            {
                using Process process = Process.GetProcessById(_pid);
                if (!process.HasExited)
                    process.Kill();
            }
            catch
            {
                // Process already exited
            }
        }
    }
}
