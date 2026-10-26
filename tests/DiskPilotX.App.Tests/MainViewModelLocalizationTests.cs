using FluentAssertions;
using DiskPilotX.App.Localization;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.App.Tests;

public class MainViewModelLocalizationTests
{
    [Fact]
    public void LocalizedScanTitle_RefreshesWhenLanguageChanges()
    {
        var originalLanguage = L.CurrentLanguageName;
        try
        {
            L.SetLanguage("en");
            var viewModel = new MainViewModel(new StubFileScanner());

            viewModel.SetLocalizedScanTitle("AiScanTitleFormat", "C drive");
            viewModel.ScanTitleText.Should().Be("AI is scanning C drive. Please wait...");

            L.SetLanguage("zh-CN");

            viewModel.ScanTitleText.Should().Contain("AI正在扫描C drive");
        }
        finally
        {
            L.SetLanguage(originalLanguage);
        }
    }


    [Fact]
    public async Task ScanCommand_ExpandsEnvironmentVariablesBeforeScanning()
    {
        var scanner = new StubFileScanner();
        var viewModel = new MainViewModel(scanner)
        {
            SelectedPath = @"%USERPROFILE%"
        };
        var expected = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

        await viewModel.ScanCommand.ExecuteAsync(null);

        scanner.LastPath.Should().Be(expected);
        viewModel.SelectedPath.Should().Be(expected);
        viewModel.CurrentSession!.TargetPath.Should().Be(expected);
    }

    private sealed class StubFileScanner : IFileScanner
    {
        public bool IsReady => true;

        public event Action? IsReadyChanged;

        public string? LastPath { get; private set; }

        public Task<ScanSession> ScanAsync(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken)
        {
            LastPath = path;
            IsReadyChanged?.Invoke();
            return Task.FromResult(new ScanSession { TargetPath = path });
        }
    }
}
