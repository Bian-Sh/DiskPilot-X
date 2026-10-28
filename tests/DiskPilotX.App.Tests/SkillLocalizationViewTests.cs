using System.Text.Json;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.Tests;

[Collection("本地化状态")]
public sealed class SkillLocalizationViewTests
{
    [Fact]
    public void 原生动态视图保留本地化目录()
    {
        using var json = JsonDocument.Parse("""{"title":"{loc title}","sections":[],"localizations":{"en-US":{"title":"Title"},"zh-CN":{"title":"标题"}}}""");
        var spec = SkillUiSpecContract.Parse(json.RootElement);
        var serialized = JsonSerializer.Serialize(spec, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        Assert.Contains("localizations", serialized);
        Assert.Contains("Title", serialized);
    }

    [Fact]
    public void 表单本地化表达式仅能影响显示文字()
    {
        var document = SkillUiDocument.Parse("""<StackPanel><TextBlock Text="{loc title}"/><ComboBox Name="mode"><ComboBoxItem Value="safe" Content="{loc safe}"/></ComboBox></StackPanel>""");
        Assert.Equal("title", SkillUiDocument.LocalizationKey(document.Children[0].Properties["Text"]));
        Assert.Throws<ArgumentException>(() => SkillUiDocument.Parse("""<TextBox Name="path" Text="{loc malicious}"/>"""));
        Assert.Throws<ArgumentException>(() => SkillUiDocument.Parse("""<Button Content="Run" Command="{loc run}"/>"""));
    }

    [Fact]
    public void 表单切换语言仅更新文案且保留输入选择与执行结果() => StaHost.Run(() =>
    {
        var original = DiskPilotX.App.Localization.L.GetState().LanguagePreference;
        var calls = 0;
        var tables = new Dictionary<string, Dictionary<string,string>>
        {
            ["en-US"] = new() { ["title"]="Inventory", ["mode"]="Safe mode", ["check"]="Include" },
            ["zh-CN"] = new() { ["title"]="盘点", ["mode"]="安全模式", ["check"]="包含" }
        };
        DiskPilotX.App.Views.SkillTaskWindow? window = null;
        try
        {
            DiskPilotX.App.Localization.L.SetLanguage("en-US");
            window = new("{loc title}", """<StackPanel><TextBlock Text="{loc title}"/><TextBox Name="roots" Text="initial"/><CheckBox Name="include" Content="{loc check}"/><ComboBox Name="mode"><ComboBoxItem Value="safe" Content="{loc mode}"/></ComboBox></StackPanel>""",
                new System.Text.Json.Nodes.JsonObject(), (_, _, _) => { calls++; return Task.FromResult<System.Text.Json.Nodes.JsonNode>(new System.Text.Json.Nodes.JsonObject()); }, tables);
            var input = Visuals<System.Windows.Controls.TextBox>(window).Single(t => t.Text == "initial");
            var check = Visuals<System.Windows.Controls.CheckBox>(window).Single();
            var combo = Visuals<System.Windows.Controls.ComboBox>(window).Single();
            input.Text = "用户输入 D:/不会丢失"; check.IsChecked = true;
            var selected = combo.SelectedItem;
            DiskPilotX.App.Localization.L.SetLanguage("zh-CN");
            Assert.Equal("盘点", window.Title);
            Assert.Contains(Visuals<System.Windows.Controls.TextBlock>(window), t => t.Text == "盘点");
            Assert.Equal("包含", check.Content);
            Assert.Equal("用户输入 D:/不会丢失", input.Text);
            Assert.True(check.IsChecked);
            Assert.Same(selected, combo.SelectedItem);
            Assert.Equal("safe", combo.SelectedValue);
            Assert.Equal(0, calls);
            window.Close();
            DiskPilotX.App.Localization.L.SetLanguage("en-US");
            Assert.Equal("盘点", window.Title);
            window = null;
        }
        finally { window?.Close(); DiskPilotX.App.Localization.L.SetLanguage(original); }
    });

    [Fact]
    public void 原生视图响应文案且条目实例和勾选不变() => StaHost.Run(() =>
    {
        var original = DiskPilotX.App.Localization.L.GetState().LanguagePreference;
        DiskPilotX.App.Views.SkillWindow? window = null;
        try
        {
            DiskPilotX.App.Localization.L.SetLanguage("en-US");
            var item = new SkillUiItem { Title="{loc title}", Selectable=true, Selected=true, Path=@"D:\测试" };
            var spec = new SkillUiSpec
            {
                Title="{loc title}", Sections=[new SkillUiSection { Kind=SkillUiSectionKind.Text, Items=[item] }],
                Localizations=new() { ["en-US"] = new() { ["title"]="Inventory" }, ["zh-TW"] = new() { ["title"]="盤點" } }
            };
            window = new(spec);
            var vm = (DiskPilotX.App.ViewModels.SkillWindowViewModel)window.DataContext;
            DiskPilotX.App.Localization.L.SetLanguage("zh-TW");
            Assert.Equal("盤點", window.Title);
            Assert.Same(item, vm.Spec.Sections[0].Items[0]);
            Assert.True(item.Selected);
            Assert.Contains("已選", vm.SelectionSummary);
        }
        finally { window?.Close(); DiskPilotX.App.Localization.L.SetLanguage(original); }
    });


    [Fact]
    public void 旧表单绑定选项内容仍按绑定值匹配初始选择() => StaHost.Run(() =>
    {
        var window = new DiskPilotX.App.Views.SkillTaskWindow("兼容性", """<ComboBox Name="mode" SelectedValue="safe"><ComboBoxItem Content="{Binding first}"/><ComboBoxItem Content="{Binding second}"/></ComboBox>""",
            new System.Text.Json.Nodes.JsonObject { ["first"]="fast",["second"]="safe" },
            (_,_,_) => Task.FromResult<System.Text.Json.Nodes.JsonNode>(new System.Text.Json.Nodes.JsonObject()));
        try
        {
            var combo = Visuals<System.Windows.Controls.ComboBox>(window).Single();
            Assert.Equal("safe", combo.SelectedValue);
            Assert.Equal(1, combo.SelectedIndex);
            var fields = (System.Text.Json.Nodes.JsonObject)typeof(DiskPilotX.App.Views.SkillTaskWindow)
                .GetField("fields", System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
            Assert.Equal("safe", fields["mode"]!.GetValue<string>());
        }
        finally { window.Close(); }
    });

    private static IEnumerable<T> Visuals<T>(System.Windows.DependencyObject node) where T : System.Windows.DependencyObject
    {
        if (node is T match) yield return match;
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(node).OfType<System.Windows.DependencyObject>())
            foreach (var nested in Visuals<T>(child)) yield return nested;
    }
}
