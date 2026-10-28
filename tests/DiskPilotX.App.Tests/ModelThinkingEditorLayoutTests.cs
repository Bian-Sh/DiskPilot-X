using System.Xml.Linq;

namespace DiskPilotX.App.Tests;

public class ModelThinkingEditorLayoutTests
{
    [Fact]
    public void 思考能力菜单从模型行编辑且悬停笔形按钮位于删除左侧()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "src/DiskPilotX.App/Views/SettingsSections/ApiSettingsSection.xaml"))) directory = directory.Parent;
        var document = XDocument.Load(Path.Combine(directory!.FullName, "src/DiskPilotX.App/Views/SettingsSections/ApiSettingsSection.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var elements = document.Descendants().ToArray();
        var edit = Assert.Single(elements.Where(item => (string?)item.Attribute(x + "Name") == "EditModelThinkingButton"));
        var remove = Assert.Single(elements.Where(item => (string?)item.Attribute(x + "Name") == "RemoveModelButton"));
        Assert.True(int.Parse((string)edit.Attribute("Grid.Column")!) < int.Parse((string)remove.Attribute("Grid.Column")!));
        Assert.Equal("ModelThinkingEdit_Click", (string?)edit.Attribute("Click"));
        var hover = elements.Where(item => item.Name.LocalName == "Trigger" && (string?)item.Attribute("SourceName") == "ModelRow" && (string?)item.Attribute("Property") == "IsMouseOver");
        Assert.Contains(hover.SelectMany(item => item.Elements()), item => (string?)item.Attribute("TargetName") == "EditModelThinkingButton" && (string?)item.Attribute("Property") == "Opacity" && (string?)item.Attribute("Value") == "1");
        var popup = Assert.Single(elements.Where(item => (string?)item.Attribute(x + "Name") == "ModelThinkingPopup"));
        Assert.Equal("Bottom", (string?)popup.Attribute("Placement"));
        Assert.Equal("{DynamicResource VP.BackgroundAltBrush}", (string?)popup.Elements().Single().Attribute("Background"));
        Assert.Equal("False", (string?)popup.Attribute("StaysOpen"));
        Assert.DoesNotContain(elements, item => (string?)item.Attribute(x + "Name") is "ThinkingCapabilitiesRow" or "ThinkingConfigurationModelPicker");
    }
}
