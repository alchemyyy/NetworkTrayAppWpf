using System.Windows.Forms;

namespace NetworkTrayAppWpf.Interop;

/// <summary>
/// A minimal Win32 window for receiving shell notification messages.
/// This is used by ShellNotifyIcon to receive tray icon callbacks.
/// </summary>
internal sealed class Win32Window : NativeWindow, IDisposable
{
    private Action<Message>? _wndProc;

    public void Initialize(Action<Message> wndProc)
    {
        _wndProc = wndProc;
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message m)
    {
        _wndProc?.Invoke(m);
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
