using DiskPilotX.App.Localization;
using FluentAssertions;
using DiskPilotX.Core.Services.Scanning;

namespace DiskPilotX.App.Tests;

public class ScanProgressLocalizationTests
{
    [Fact]
    public void 普通进度不标记为整盘索引且无计数阶段不显示虚假零值()
    {
        var original = L.CurrentLanguageName;
        try
        {
            L.SetLanguage("zh-CN");
            var progress = new ScanProgress("目标目录", 447, 15);
            progress.IsVolumeIndex.Should().BeFalse();
            ScanProgressFormatter.Format(progress).Should().Contain("447 个文件").And.Contain("15 个文件夹").And.NotContain("索引");
            ScanProgressFormatter.Format(new ScanProgress("正在构建目录树", 0, 0)).Should().Be("正在构建目录树");
            ScanProgressFormatter.Format(new ScanProgress(@"D:\", 0, 0) { IsVolumeIndex = true }).Should().Contain("整盘索引").And.Contain("0 条文件记录");
        }
        finally { L.SetLanguage(original); }
    }

    [Theory]
    [InlineData("zh-CN", "整盘索引", "文件记录", "目录记录")]
    [InlineData("zh-TW", "整碟索引", "檔案記錄", "目錄記錄")]
    [InlineData("en", "volume index", "file records", "folder records")]
    public void 整盘索引文案明确标记记录数而非目标文件数(string language, string phase, string files, string folders)
    {
        var original = L.CurrentLanguageName;
        try
        {
            L.SetLanguage(language);
            var text = ScanProgressFormatter.Format(new ScanProgress(@"D:\", 2242454, 257546) { IsVolumeIndex = true });
            text.Should().Contain(phase).And.Contain(files).And.Contain(folders).And.Contain(@"D:\");
            text.Should().NotContain("!ScanVolumeIndexProgressStatus!");
        }
        finally { L.SetLanguage(original); }
    }
}
