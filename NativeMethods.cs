/**
 *
 * There are currently no Win32 controls in this tray app. Just keeping this here as reference for now.
 *
 */
// using System.Runtime.InteropServices;
//
// namespace NetworkTrayAppWpf;
//
// internal static partial class NativeMethods
// {
//     [LibraryImport("user32.dll")]
//     public static partial uint GetDpiForWindow(IntPtr hwnd);
//
//     public enum PreferredAppMode
//     {
//         Default = 0,
//         AllowDark = 1,
//         ForceDark = 2,
//         ForceLight = 3,
//         Max = 4
//     }
//
//     [DllImport("uxtheme.dll", EntryPoint = "#135", SetLastError = true)]
//     public static extern PreferredAppMode SetPreferredAppMode(PreferredAppMode mode);
//
//     [DllImport("uxtheme.dll", EntryPoint = "#136", SetLastError = true)]
//     public static extern void FlushMenuThemes();
//
//     public static void EnableDarkModeForApp()
//     {
//         try
//         {
//             SetPreferredAppMode(PreferredAppMode.AllowDark);
//             FlushMenuThemes();
//         }
//         catch
//         {
//             // Ignore failures on older Windows versions
//         }
//     }
// }
