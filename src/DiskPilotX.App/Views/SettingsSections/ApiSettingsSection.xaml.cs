using System.Windows;
using System.Windows.Controls;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views.SettingsSections;

public partial class ApiSettingsSection : UserControl
{
    public ApiSettingsSection()
    {
        InitializeComponent();
    }

    // 刷新按钮独立于下拉项选择，点击后保持当前身份和菜单。
    private async void CliIdentityRefresh_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: CliIdentityOption option } || option.RefreshCommand is null) return;
        e.Handled = true;
        await option.RefreshCommand.ExecuteAsync(option);
    }

    // PasswordBox 出于安全考虑不支持双向绑定，这里把输入同步到 ViewModel。
    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ProxyPassword = ((PasswordBox)sender).Password;
    }
}
