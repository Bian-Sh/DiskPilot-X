using System.Xml.Linq;

namespace DiskPilotX.App.Tests;

public sealed class ThemeSettingsSurfaceTests
{
    [Fact]
    public void 正式主题设置不暴露磨砂调试入口且保留常用控制()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md")))
            root = root.Parent;
        Assert.NotNull(root);

        var document = XDocument.Load(Path.Combine(root!.FullName,
            "src/DiskPilotX.App/Views/SettingsSections/ThemeSettingsSection.xaml"));
        var buttons = document.Descendants().Where(element => element.Name.LocalName == "Button");
        Assert.DoesNotContain(buttons, element =>
            (string?)element.Attribute("Click") == "CopyBackdropDiagnostics_Click" ||
            (string?)element.Attribute("Content") == "{loc:Loc ThemeBackdropCopyDiagnostics}");

        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "KeepBackgroundGlassToggle", "GlassTransparencySlider" })
            Assert.Contains(document.Descendants(), element => (string?)element.Attribute(xaml + "Name") == name);
        Assert.Contains(document.Descendants(), element => element.Name.LocalName == "CheckBox" &&
            (string?)element.Attribute("IsChecked") == "{Binding GlassEnabled}");
    }
}
