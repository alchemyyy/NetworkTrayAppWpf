using System.Reflection;
using System.Windows.Interop;
using NetworkTrayAppWPF.Interop;
using NetworkTrayAppWPF.Models;

namespace NetworkTrayAppWPF.Services;

public sealed class HotkeyFiredEventArgs(HotkeyAction action, string parameter) : EventArgs
{
    public HotkeyAction Action { get; } = action;
    public string Parameter { get; } = parameter;
}

public sealed class HotkeyApplyResult
{
    public List<HotkeyBinding> Registered { get; } = [];

    /// <summary>Bindings that failed to register (combo already taken by another app, reserved, etc.).</summary>
    public Dictionary<HotkeyBinding, string> Failed { get; } = [];
}

/// <summary>
/// Owns a hidden message-only window, listens for WM_HOTKEY,
/// and translates the fired ID back into the (action, parameter) pair the user bound.
/// Message-only because RegisterHotKey is HWND- and thread-bound; an app window's HWND lifecycle
/// (show/hide cycles, prewarm) isn't a stable anchor.
///
/// Thread affinity:
/// <see cref="Initialize"/>, <see cref="Apply"/> and <see cref="TryRegister"/> must be called from the WPF UI thread
/// (the thread that created the message-only window).
/// WM_HOTKEY is delivered to that thread's queue,
/// so handlers wired to <see cref="Fired"/> already run on the UI thread - no Dispatcher marshaling needed.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private HwndSource? _source;
    private IntPtr _hwnd;
    private int _nextId = 1;
    private readonly Dictionary<int, HotkeyBinding> _byId = [];
    private bool _disposed;

    /// <summary>Fired on the UI thread when a registered hotkey is pressed.</summary>
    public event EventHandler<HotkeyFiredEventArgs>? Fired;

    public void Initialize()
    {
        if (_source != null) return;

        HwndSourceParameters parameters = new(Assembly.GetExecutingAssembly().GetName().Name + ".HotkeySink")
        {
            ParentWindow = User32.HWND_MESSAGE,
            WindowStyle = 0,
            ExtendedWindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _hwnd = _source.Handle;
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// Diff-and-apply: unregister everything currently registered,
    /// then re-register each enabled binding from <paramref name="bindings"/>.
    /// Simpler than a true diff and well within the cost budget - we expect &lt;30 bindings total.
    /// </summary>
    public HotkeyApplyResult Apply(IEnumerable<HotkeyBinding> bindings)
    {
        ObjectDisposedException.ThrowIf(_disposed, nameof(GlobalHotkeyService));

        if (_source == null) throw new InvalidOperationException("Initialize must be called before Apply.");

        UnregisterAll();

        HotkeyApplyResult result = new();
        foreach (HotkeyBinding b in bindings)
        {
            if (b.RemovedByUser) continue;
            if (!b.Enabled || !b.IsBound) continue;

            if (TryRegisterInternal(b, out string? error))
                result.Registered.Add(b);
            else
                result.Failed[b] = error ?? "Registration failed.";
        }
        return result;
    }

    /// <summary>
    /// Validate-and-register a single binding. Used by the capture UI to give immediate feedback.
    /// On success the binding stays registered until the next <see cref="Apply"/> call;
    /// this is fine because the UI calls Apply after every edit.
    /// </summary>
    public bool TryRegister(HotkeyBinding binding, out string? error)
    {
        if (_disposed) { error = "Service disposed."; return false; }
        if (_source == null) { error = "Service not initialized."; return false; }
        if (!binding.IsBound) { error = "Binding is incomplete."; return false; }
        return TryRegisterInternal(binding, out error);
    }

    private bool TryRegisterInternal(HotkeyBinding binding, out string? error)
    {
        if (!Validate(binding, out error)) return false;

        int id = _nextId++;
        uint mods = binding.Modifiers | User32.MOD_NOREPEAT;
        bool ok = User32.RegisterHotKey(_hwnd, id, mods, binding.VirtualKey);
        if (!ok)
        {
            int lastError = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
            error = lastError == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                ? "Already in use by another app."
                : $"Registration failed (Win32 error {lastError}).";
            return false;
        }

        _byId[id] = binding;
        return true;
    }

    /// <summary>
    /// Defence-in-depth validator.
    /// Same rules the capture UI enforces - duplicated here so a hand-edited settings.xml can't slip past them.
    /// </summary>
    public static bool Validate(HotkeyBinding binding, out string? error)
    {
        error = null;
        if (binding.VirtualKey == 0) { error = "No key set."; return false; }
        if ((binding.Modifiers & (User32.MOD_ALT | User32.MOD_CONTROL | User32.MOD_SHIFT | User32.MOD_WIN)) == 0)
        {
            error = "At least one modifier (Ctrl, Alt, Shift, Win) is required.";
            return false;
        }
        if (binding.VirtualKey == 0x7B) // VK_F12
        {
            error = "F12 is reserved by the debugger.";
            return false;
        }
        return true;
    }

    private void UnregisterAll()
    {
        if (_hwnd == IntPtr.Zero) return;

        foreach (int id in _byId.Keys)
        {
            try { User32.UnregisterHotKey(_hwnd, id); }
            catch { /* best effort */ }
        }
        _byId.Clear();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != User32.WM_HOTKEY) return IntPtr.Zero;

        int id = wParam.ToInt32();
        if (!_byId.TryGetValue(id, out HotkeyBinding? binding)) return IntPtr.Zero;

        try { Fired?.Invoke(this, new HotkeyFiredEventArgs(binding.Action, binding.Parameter)); }
        catch (Exception ex) { WPFLog.Log($"GlobalHotkeyService.Fired handler threw: {ex}"); }

        handled = true;
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        UnregisterAll();
        if (_source != null)
        {
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
        _hwnd = IntPtr.Zero;
    }
}
