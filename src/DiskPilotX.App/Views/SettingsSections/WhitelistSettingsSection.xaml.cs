using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views.SettingsSections;

public partial class WhitelistSettingsSection : UserControl
{
    public WhitelistSettingsSection()
    {
        InitializeComponent();
    }

    private void WhitelistTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.WhitelistEntryChangedCommand.CanExecute(null))
        {
            vm.WhitelistEntryChangedCommand.Execute(null);
        }
    }

    private void WhitelistAdd_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string kind } || kind is not ("scan" or "cleanup"))
            return;

        var composer = kind == "scan" ? ScanWhitelistComposer : CleanupWhitelistComposer;
        var input = kind == "scan" ? ScanWhitelistInput : CleanupWhitelistInput;
        composer.Visibility = Visibility.Visible;
        input.Focus();
    }

    private void WhitelistPath_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox input || DataContext is not SettingsViewModel vm)
            return;

        // 按绑定的字段路由，避免紧凑布局调整后依赖父级层数而失效。
        var bindingPath = input.GetBindingExpression(TextBox.TextProperty)?.ParentBinding.Path?.Path;
        if (bindingPath is not (nameof(SettingsViewModel.NewScanWhitelistPath) or nameof(SettingsViewModel.NewCleanupWhitelistPath)))
            return;

        var isScan = bindingPath == nameof(SettingsViewModel.NewScanWhitelistPath);
        if (e.Key == Key.Escape)
        {
            (isScan ? ScanWhitelistComposer : CleanupWhitelistComposer).Visibility = Visibility.Collapsed;
            (isScan ? ScanWhitelistAddButton : CleanupWhitelistAddButton).Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var command = isScan ? vm.AddScanWhitelistPathCommand : vm.AddCleanupWhitelistPathCommand;
            if (command.CanExecute(null))
            {
                command.Execute(null);
                e.Handled = true;
            }
        }
    }

    private void WhitelistList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ItemsControl itemsControl && itemsControl.ContextMenu is not null)
        {
            itemsControl.ContextMenu.PlacementTarget = itemsControl;
        }
    }
}
