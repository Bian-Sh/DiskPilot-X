using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using DiskPilotX.Core.Models;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.App.Localization;

/// <summary>将文字、技能自有文案和语言快照组合绑定；只更新显示属性，不重建表单或结果条目。</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class SkillTextExtension : MarkupExtension
{
    public SkillTextExtension() { }
    public SkillTextExtension(string path) => Path = path;
    public string Path { get; set; } = ".";
    public string? CatalogPath { get; set; }

    /// <summary>让内联图表等共享模板继承所属技能词典，不依赖聊天主窗口的数据上下文。</summary>
    public static readonly DependencyProperty CatalogProperty = DependencyProperty.RegisterAttached(
        "Catalog", typeof(IReadOnlyDictionary<string, Dictionary<string,string>>), typeof(SkillTextExtension),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));
    public static void SetCatalog(DependencyObject target, IReadOnlyDictionary<string, Dictionary<string,string>>? value) => target.SetValue(CatalogProperty,value);
    public static IReadOnlyDictionary<string, Dictionary<string,string>>? GetCatalog(DependencyObject target) =>
        (IReadOnlyDictionary<string, Dictionary<string,string>>?)target.GetValue(CatalogProperty);

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new MultiBinding { Converter = Converter.Instance, Mode = BindingMode.OneWay };
        binding.Bindings.Add(new Binding(Path));
        binding.Bindings.Add(CatalogPath is not null ? new Binding(CatalogPath) : new Binding { Path=new PropertyPath(CatalogProperty), RelativeSource=RelativeSource.Self });
        binding.Bindings.Add(new Binding("DataContext.Spec.Localizations")
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Window), 1)
        });
        binding.Bindings.Add(new Binding(nameof(LanguageSource.State)) { Source = LanguageSource.Instance });
        return binding.ProvideValue(serviceProvider);
    }

    public static string Resolve(string value, IReadOnlyDictionary<string, Dictionary<string, string>>? tables)
        => new SkillTextCatalog(tables).Resolve(value, L.GetState().CultureName);

    private sealed class Converter : IMultiValueConverter
    {
        public static readonly Converter Instance = new();
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
            Resolve(values[0] as string ?? "", values[1] as IReadOnlyDictionary<string, Dictionary<string, string>> ?? values[2] as IReadOnlyDictionary<string, Dictionary<string, string>>);
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class LanguageSource : INotifyPropertyChanged
    {
        public static readonly LanguageSource Instance = new();
        private LanguageSource() => L.LanguageChanged += () => PropertyChanged?.Invoke(this, new(nameof(State)));
        public SkillLocalizationState State => L.GetState();
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
