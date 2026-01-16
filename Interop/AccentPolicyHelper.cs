using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Color = System.Windows.Media.Color;

namespace NetworkTrayAppWpf.Interop;

/// <summary>
/// Helper for enabling acrylic blur effects on WPF windows.
/// </summary>
internal static class AccentPolicyHelper
{
    // RS4 (1803) and later support tint color in acrylic
    private static readonly bool AccentPolicySupportsTintColor =
        Environment.OSVersion.Version.Build >= 17134;

    public static void EnableAcrylic(Window window, Color color, User32.AccentFlags flags = User32.AccentFlags.None)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        User32.AccentPolicy policy = new()
        {
            AccentFlags = flags,
            AccentState = AccentPolicySupportsTintColor
                ? User32.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
                : User32.AccentState.ACCENT_ENABLE_BLURBEHIND,
            GradientColor = ToABGR(color),
        };

        SetAccentPolicy(handle, policy);
    }

    public static void DisableAcrylic(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;

        User32.AccentPolicy policy = new()
        {
            AccentState = User32.AccentState.ACCENT_DISABLED,
        };

        SetAccentPolicy(handle, policy);
    }

    private static void SetAccentPolicy(IntPtr handle, User32.AccentPolicy policy)
    {
        int accentStructSize = Marshal.SizeOf<User32.AccentPolicy>();
        IntPtr accentPtr = Marshal.AllocHGlobal(accentStructSize);

        try
        {
            Marshal.StructureToPtr(policy, accentPtr, false);

            User32.WindowCompositionAttribData data = new()
            {
                Attribute = User32.WindowCompositionAttribute.WCA_ACCENT_POLICY,
                SizeOfData = accentStructSize,
                Data = accentPtr
            };

            User32.SetWindowCompositionAttribute(handle, ref data);
        }
        finally
        {
            Marshal.FreeHGlobal(accentPtr);
        }
    }

    private static uint ToABGR(Color color)
    {
        return (uint)((color.A << 24) | (color.B << 16) | (color.G << 8) | color.R);
    }
}
