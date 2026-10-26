using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views;

public partial class AboutPage : UserControl
{
    private UpdateViewModel? _vm;

    public AboutPage()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        SizeChanged += OnSizeChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;

        _vm = DataContext as UpdateViewModel;

        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
            UpdateVisualState();
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateVisualState);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_vm is null) return;
        UpdateProgressBarWidth();
    }

    private void UpdateVisualState()
    {
        if (_vm is null) return;

        UpToDatePanel.Visibility = Visibility.Collapsed;
        UpdateAvailablePanel.Visibility = Visibility.Collapsed;
        DownloadingPanel.Visibility = Visibility.Collapsed;
        DownloadCompletePanel.Visibility = Visibility.Collapsed;

        if (_vm.IsDownloadComplete)
        {
            DownloadCompletePanel.Visibility = Visibility.Visible;
            DownloadCompleteStatus.Text = $"✅ v{_vm.LatestVersion} 已下载完成";
        }
        else if (_vm.IsDownloading)
        {
            DownloadingPanel.Visibility = Visibility.Visible;
            UpdateProgressBarWidth();
        }
        else if (_vm.UpdateAvailable)
        {
            UpdateAvailablePanel.Visibility = Visibility.Visible;
            UpdateTitleDetail.Text = $"v{_vm.LatestVersion}（发布于 {_vm.PublishedAt}）";
            DownloadButtonLabel.Text = $"下载更新（{_vm.FormatFileSize(_vm.MsiFileSize)}）";
        }
        else if (_vm.CheckCompleted)
        {
            UpToDatePanel.Visibility = Visibility.Visible;
        }
    }

    private void UpdateProgressBarWidth()
    {
        if (_vm is null) return;
        var parent = ProgressBarFill.Parent as Border;
        if (parent is null) return;
        var maxWidth = parent.ActualWidth;
        if (maxWidth <= 0) return;

        var targetWidth = maxWidth * _vm.DownloadProgress / 100.0;
        ProgressBarFill.Width = Math.Max(0, targetWidth);

        var downloaded = _vm.MsiFileSize * _vm.DownloadProgress / 100.0;
        ProgressText.Text = $"{_vm.DownloadProgress:F1}%（{_vm.FormatFileSize((long)downloaded)} / {_vm.FormatFileSize(_vm.MsiFileSize)}）";
    }

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
