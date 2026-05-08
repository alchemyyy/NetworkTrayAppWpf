using System.IO;
using System.Windows.Media.Imaging;
using NetworkTrayAppWPF.Interop;

namespace NetworkTrayAppWPF.Visuals;

internal static class IconRenderingHelper
{
    public static uint GetTaskbarDpi()
    {
        IntPtr deviceContext = User32.GetDC(IntPtr.Zero);
        if (deviceContext == IntPtr.Zero) return 96;

        try
        {
            int dpi = User32.GetDeviceCaps(deviceContext, User32.LOGPIXELSX);
            return dpi > 0 ? (uint)dpi : 96u;
        }
        finally
        {
            _ = User32.ReleaseDC(IntPtr.Zero, deviceContext);
        }
    }

    public static int GetIconSizeForDpi(uint dpi)
    {
        int size = User32.GetSystemMetricsForDpi(User32.SM_CXSMICON, dpi);
        return size > 0 ? size : (int)Math.Round(16 * dpi / 96.0);
    }

    public static Icon BitmapToIcon(RenderTargetBitmap bitmap)
    {
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using MemoryStream stream = new();
        encoder.Save(stream);
        stream.Position = 0;

        using Bitmap gdiBitmap = new(stream);
        IntPtr hIcon = gdiBitmap.GetHicon();
        try
        {
            using Icon original = Icon.FromHandle(hIcon);
            return (Icon)original.Clone();
        }
        finally
        {
            User32.DestroyIcon(hIcon);
        }
    }
}
