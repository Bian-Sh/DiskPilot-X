using System.Xml.Linq;

namespace DiskPilotX.App.Tests;

public sealed class ModelSettingsLayoutTests
{
    [Fact]
    public void URL前移问号承载说明列表无外框和刷新按钮()
    {
        var document = Load(); XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var elements = document.Descendants().ToList();
        var urlRow = elements.SingleOrDefault(element => (string?)element.Attribute(x + "Name") == "ModelsUrlRow");
        var listRow = elements.SingleOrDefault(element => (string?)element.Attribute(x + "Name") == "ModelCatalogRow");
        Assert.NotNull(urlRow); Assert.NotNull(listRow);
        Assert.True(elements.IndexOf(urlRow!) < elements.IndexOf(listRow!));
        var help = elements.Single(element => (string?)element.Attribute(x + "Name") == "ModelsUrlHelp");
        Assert.Equal("?", (string?)help.Attribute("Text"));
        Assert.Equal("Help", (string?)help.Attribute("Cursor"));
        Assert.Equal("{loc:Loc SettingsModelsUrlHint}", (string?)help.Attribute("ToolTip"));
        Assert.DoesNotContain(elements, element => (string?)element.Attribute("Text") == "{loc:Loc SettingsModelsUrlHint}");
        Assert.DoesNotContain(elements, element => (string?)element.Attribute("Text") == "{loc:Loc SettingsModelListEditHint}");
        Assert.DoesNotContain(elements.Where(element => element.Name.LocalName == "Button"), element => ((string?)element.Attribute("Command"))?.Contains("RefreshModelsCommand") == true);
        var scroll = elements.Single(element => (string?)element.Attribute(x + "Name") == "ModelOrderScroll");
        Assert.DoesNotContain(scroll.Ancestors().TakeWhile(element => element != listRow), element => element.Name.LocalName == "Border");
    }

    [Fact]
    public void 所有网络配置文本均使用完成编辑事件而不是逐字符网络请求()
    {
        var document = Load(); XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        foreach (var name in new[] { "BaseUrlInput", "ApiKeyInput", "ModelsUrlInput", "ProxyHostInput", "ProxyPortInput", "ProxyUsernameInput", "ProxyPasswordBox" })
        {
            var element = document.Descendants().SingleOrDefault(element => (string?)element.Attribute(x + "Name") == name);
            Assert.NotNull(element);
            Assert.Equal("ModelConnectionEdit_LostKeyboardFocus", (string?)element!.Attribute("LostKeyboardFocus"));
            Assert.Equal("ModelConnectionEdit_PreviewKeyDown", (string?)element.Attribute("PreviewKeyDown"));
            Assert.Null(element.Attribute("TextChanged"));
        }
        foreach (var binding in new[] { "SelectedApiFormat", "SelectedCliIdentity", "SelectedProxyKind" })
        {
            var combo = document.Descendants().Single(element => element.Name.LocalName == "ComboBox" && (string?)element.Attribute("SelectedValue") == "{Binding " + binding + "}");
            Assert.Equal("ModelConnectionChoice_SelectionChanged", (string?)combo.Attribute("SelectionChanged"));
        }
    }

    [Fact]
    public void APIKey输入框自适应且验证按钮占最右列()
    {
        var document = Load(); XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var input = document.Descendants().Single(element => (string?)element.Attribute(x + "Name") == "ApiKeyInput");
        var grid = input.Parent!;
        Assert.Equal("Grid", grid.Name.LocalName);
        var columns = grid.Elements().Single(element => element.Name.LocalName == "Grid.ColumnDefinitions").Elements().ToArray();
        Assert.Equal(new[] { "*", "Auto" }, columns.Select(column => (string?)column.Attribute("Width")));
        Assert.Null(input.Attribute("Width"));
        var validate = grid.Elements().Single(element => (string?)element.Attribute("Command") == "{Binding ValidateCommand}");
        Assert.Equal("1", (string?)validate.Attribute("Grid.Column"));
        Assert.Equal("Right", (string?)validate.Attribute("HorizontalAlignment"));
    }
    private static XDocument Load()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var file = Path.Combine(directory.FullName, "src/DiskPilotX.App/Views/SettingsSections/ApiSettingsSection.xaml");
            if (File.Exists(file)) return XDocument.Load(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("未找到当前工作区的设置页XAML。");
    }
}