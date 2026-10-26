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

    private void WhitelistList_RightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ItemsControl itemsControl && itemsControl.ContextMenu is not null)
        {
            itemsControl.ContextMenu.PlacementTarget = itemsControl;
        }
    }
}
