using System.Runtime.InteropServices;

namespace NetworkTrayAppWpf;

/// <summary>
/// Provides methods to invoke Windows shell flyouts via COM interfaces.
/// </summary>
internal static unsafe class ShellFlyout
{
    private static readonly Guid CLSID_ImmersiveShell = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid IID_IServiceProvider = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid SID_ShellExperienceManagerFactory = new("2E8FCB18-A0EE-41AD-8EF8-77FB3A370CA5");
    private static readonly Guid IID_NetworkFlyoutExperienceManager = new("C9DDC674-B44B-4C67-9D79-2B237D9BE05A");
    private static readonly Guid IID_ControlCenterExperienceManager = new("D669A58E-6B18-4D1D-9004-A8862ADB0A20");

    private const int CLSCTX_LOCAL_SERVER = 4;

    [DllImport("ole32.dll")]
    private static extern int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        int dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(
        [MarshalAs(UnmanagedType.LPWStr)] string sourceString,
        int length,
        out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    public static bool ShowNetworkFlyoutWin10()
    {
        return ShowFlyoutCOM(
            "Windows.Internal.ShellExperience.NetworkFlyout",
            IID_NetworkFlyoutExperienceManager);
    }

    public static bool ShowControlCenter()
    {
        return ShowFlyoutCOM(
            "Windows.Internal.ShellExperience.ControlCenter",
            IID_ControlCenterExperienceManager);
    }

    private ref struct ComHandles
    {
        public IntPtr ServiceProvider;
        public IntPtr Factory;
        public IntPtr ExperienceManager;
        public IntPtr Flyout;
        public IntPtr HString;

        public void Dispose()
        {
            if (HString != IntPtr.Zero)
                _ = WindowsDeleteString(HString);
            if (Flyout != IntPtr.Zero)
                Marshal.Release(Flyout);
            if (ExperienceManager != IntPtr.Zero)
                Marshal.Release(ExperienceManager);
            if (Factory != IntPtr.Zero)
                Marshal.Release(Factory);
            if (ServiceProvider != IntPtr.Zero)
                Marshal.Release(ServiceProvider);
        }
    }

    private static bool TryAcquireFlyoutInterfaces(string experienceName, Guid experienceIID, ref ComHandles handles)
    {
        int hr = CoCreateInstance(CLSID_ImmersiveShell, IntPtr.Zero, CLSCTX_LOCAL_SERVER,
            IID_IServiceProvider, out handles.ServiceProvider);
        if (hr < 0 || handles.ServiceProvider == IntPtr.Zero)
            return false;

        IServiceProvider serviceProvider = (IServiceProvider)Marshal.GetObjectForIUnknown(handles.ServiceProvider);
        hr = serviceProvider.QueryService(SID_ShellExperienceManagerFactory,
            SID_ShellExperienceManagerFactory, out handles.Factory);
        if (hr < 0 || handles.Factory == IntPtr.Zero)
            return false;

        hr = WindowsCreateString(experienceName, experienceName.Length, out handles.HString);
        if (hr < 0 || handles.HString == IntPtr.Zero)
            return false;

        IntPtr* vtable = *(IntPtr**)handles.Factory;
        GetExperienceManagerDelegate getExperienceManager = Marshal.GetDelegateForFunctionPointer<GetExperienceManagerDelegate>(
            vtable[VTABLE_GET_EXPERIENCE_MANAGER]);
        hr = getExperienceManager(handles.Factory, handles.HString, out handles.ExperienceManager);
        if (hr < 0 || handles.ExperienceManager == IntPtr.Zero)
            return false;

        hr = Marshal.QueryInterface(handles.ExperienceManager, ref experienceIID, out handles.Flyout);
        return hr >= 0 && handles.Flyout != IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WFRect
    {
        public float X;
        public float Y;
        public float Width;
        public float Height;
    }

    private const int VTABLE_SHOW_FLYOUT = 6;
    private const int VTABLE_GET_EXPERIENCE_MANAGER = 6;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int ShowFlyoutDelegate(IntPtr pThis, ref WFRect rect);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int GetExperienceManagerDelegate(IntPtr pThis, IntPtr experience, out IntPtr ppExperienceManager);

    private static bool ShowFlyoutCOM(string experienceName, Guid experienceIID)
    {
        ComHandles handles = new();
        try
        {
            if (!TryAcquireFlyoutInterfaces(experienceName, experienceIID, ref handles))
                return false;

            IntPtr* flyoutVtable = *(IntPtr**)handles.Flyout;
            ShowFlyoutDelegate showFlyout = Marshal.GetDelegateForFunctionPointer<ShowFlyoutDelegate>(
                flyoutVtable[VTABLE_SHOW_FLYOUT]);
            WFRect rect = default;
            int hr = showFlyout(handles.Flyout, ref rect);

            return hr >= 0;
        }
        catch
        {
            return false;
        }
        finally
        {
            handles.Dispose();
        }
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [PreserveSig]
        int QueryService(in Guid guidService, in Guid riid, out IntPtr ppvObject);
    }
}
