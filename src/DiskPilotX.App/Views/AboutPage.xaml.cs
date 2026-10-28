using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views;

public partial class AboutPage : UserControl
{
    public AboutPage() => InitializeComponent();

    private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void DonateLink_Click(object sender, MouseButtonEventArgs e)
    {
        ShowDonateModal();
    }

    private void ShowDonateModal()
    {
        var dialog = new DonateDialog();
        dialog.ShowDialog();
    }
}
