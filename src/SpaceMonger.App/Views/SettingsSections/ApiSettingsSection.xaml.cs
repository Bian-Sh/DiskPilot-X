using System.Windows;
using System.Windows.Controls;
using SpaceMonger.App.ViewModels;

namespace SpaceMonger.App.Views.SettingsSections;

public partial class ApiSettingsSection : UserControl
{
    public ApiSettingsSection()
    {
        InitializeComponent();
    }

    // PasswordBox 出于安全考虑不支持双向绑定，这里把输入同步到 ViewModel。
    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ProxyPassword = ((PasswordBox)sender).Password;
    }
}
