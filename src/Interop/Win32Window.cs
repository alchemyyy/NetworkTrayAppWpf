namespace NetworkTrayAppWPF.Interop;

/// <summary>
/// A minimal Win32 window for receiving shell notification messages.
/// This is used by ShellNotifyIcon to receive tray icon callbacks.
/// </summary>
internal sealed class Win32Window : NativeWindow, IDisposable
{
    private Action<Message>? _windowProcedureCallback;

    public void Initialize(Action<Message> wndProc)
    {
        _windowProcedureCallback = wndProc;
        CreateHandle(new CreateParams());
    }

    protected override void WndProc(ref Message message)
    {
        // Throwing across the Win32 message pump is undefined behavior -
        // the callback gets the message, but never the exception.
        if (_windowProcedureCallback != null)
        {
            try { _windowProcedureCallback(message); }
            catch (Exception ex) { WPFLog.Log($"Win32Window.WndProc: {ex.Message}"); }
        }
        base.WndProc(ref message);
    }

    public void Dispose()
    {
        DestroyHandle();
    }
}
