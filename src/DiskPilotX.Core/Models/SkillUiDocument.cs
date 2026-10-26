using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace DiskPilotX.Core.Models;

/// <summary>受约束 XAML 的数据描述；不实例化 CLR 类型或执行标记扩展。</summary>
public sealed record SkillUiDocument(string Type, IReadOnlyDictionary<string,string> Properties, IReadOnlyList<SkillUiDocument> Children)
{
    private static readonly Dictionary<string,string[]> Allowed = new()
    {
        ["StackPanel"] = ["Orientation"], ["Grid"] = ["Columns"], ["Border"] = [],
        ["TextBlock"] = ["Text", "FontSize", "FontWeight"], ["TextBox"] = ["Text", "Name", "MaxLength"],
        ["CheckBox"] = ["Content", "Name", "IsChecked"], ["ComboBox"] = ["Name", "SelectedValue"],
        ["ComboBoxItem"] = ["Content"], ["Button"] = ["Content", "Command"], ["DataGrid"] = ["ItemsSource"]
    };
    private static readonly Regex Identifier = new("^[a-z][a-z0-9_]{0,63}$");
    private static readonly Regex Binding = new(@"^\{Binding ([a-z][a-z0-9_]{0,63})\}$");
    public static SkillUiDocument Parse(string xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml) || xaml.Length > 64000) throw new ArgumentException("面板描述为空或超过 64 KB。");
        using var input = new StringReader(xaml);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 64000 });
        var document = XDocument.Load(reader);
        var count = 0; var names = new HashSet<string>(StringComparer.Ordinal);
        return Read(document.Root ?? throw new ArgumentException("面板缺少根节点。"), 1);
        SkillUiDocument Read(XElement element, int depth)
        {
            if (++count > 256 || depth > 12) throw new ArgumentException("面板节点过多或嵌套过深。");
            var type = element.Name.LocalName;
            if (element.Name.NamespaceName is not ("" or "http://schemas.microsoft.com/winfx/2006/xaml/presentation") || !Allowed.TryGetValue(type, out var attributes))
                throw new ArgumentException("面板包含不支持的控件：" + type);
            var properties = new Dictionary<string,string>();
            foreach (var attribute in element.Attributes())
            {
                if (attribute.IsNamespaceDeclaration) continue;
                var key = attribute.Name.LocalName;
                if (attribute.Name.NamespaceName.Length > 0 && !(key == "Name" && attribute.Name.NamespaceName == "http://schemas.microsoft.com/winfx/2006/xaml"))
                    throw new ArgumentException("面板不允许扩展属性。");
                if (!attributes.Contains(key) || !properties.TryAdd(key, attribute.Value)) throw new ArgumentException("不允许的面板属性：" + key);
                if (attribute.Value.Contains('{') && !Binding.IsMatch(attribute.Value)) throw new ArgumentException("面板仅允许绑定简单数据字段。");
                if (key == "Name" && (!Identifier.IsMatch(attribute.Value) || !names.Add(attribute.Value))) throw new ArgumentException("表单字段名称无效或重复。");
                if (key == "Command" && attribute.Value is not ("preview" or "continue" or "close")) throw new ArgumentException("面板命令不受支持。");
            }
            if (element.Nodes().OfType<XText>().Any(t => !string.IsNullOrWhiteSpace(t.Value))) throw new ArgumentException("请使用 Text 或 Content 描述文字。");
            var children = element.Elements().Select(child => Read(child, depth + 1)).ToArray();
            if ((type == "Border" && children.Length > 1) || (type is not ("StackPanel" or "Grid" or "Border" or "ComboBox") && children.Length > 0)
                || (type == "ComboBox" && children.Any(c => c.Type != "ComboBoxItem"))) throw new ArgumentException("控件的子节点无效。");
            return new(type, properties, children);
        }
    }
    public static string? BindingField(string value) => Binding.Match(value) is { Success: true } match ? match.Groups[1].Value : null;
}
