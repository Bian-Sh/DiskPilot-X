using System.Xml.Linq;

namespace DiskPilotX.App.Tests;

public sealed class ApiKeyTooltipTests
{
    [Theory]
    [InlineData("Strings.resx", "Enter your API key. It will be encrypted and stored locally.")]
    [InlineData("Strings.zh-CN.resx", "输入 API Key，密钥将加密保存在本机。")]
    [InlineData("Strings.zh-TW.resx", "輸入 API Key，金鑰將加密儲存在本機。")]
    public void APIKey提示使用通用文案且各语言均无厂商名称(string resourceName, string expected)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(directory.FullName, "src/DiskPilotX.App/Localization", resourceName);
            if (File.Exists(file))
            {
                var value = XDocument.Load(file).Descendants("data").Single(e => (string?)e.Attribute("name") == "ApiKeyToolTip").Element("value")!.Value;
                Assert.Equal(expected, value);
                Assert.DoesNotContain("anthropic", value.ToLowerInvariant());
                Assert.DoesNotContain("openai", value.ToLowerInvariant());
                return;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("未找到 API Key 提示的本地化资源。");
    }
}
