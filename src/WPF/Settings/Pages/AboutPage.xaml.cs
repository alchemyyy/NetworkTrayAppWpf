using System.Diagnostics;
using System.Windows.Navigation;
using UserControl = System.Windows.Controls.UserControl;

namespace NetworkTrayAppWPF.WPF.Settings.Pages;

/// <summary>
/// About page. Owns the build/runtime info rows, the Github hyperlink, and the static known-issues notes.
/// Has no AppSettings dependency - every value displayed is derived from <see cref="BuildInfo"/>
/// at the moment the section is shown.
/// The shell calls <see cref="RefreshOnShow"/> when the user navigates to this tab.
/// </summary>
public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();

    public void RefreshOnShow()
    {
        BuildNumberText.Text = BuildInfo.BuildNumber.ToString();
        RuntimeText.Text = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
