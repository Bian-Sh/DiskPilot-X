using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using DiskPilotX.Core.Services.Skills;

namespace DiskPilotX.App.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key)
    {
        Key = key;
    }

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget target &&
            target.TargetObject is DependencyObject dependencyObject &&
            target.TargetProperty is DependencyProperty dependencyProperty)
        {
            var binding = new LocBinding(Key, dependencyObject, dependencyProperty);
            return binding.Value;
        }

        return L.Text(Key);
    }
}

public sealed class LocBinding : INotifyPropertyChanged
{
    private static readonly DependencyProperty LocBindingsProperty = DependencyProperty.RegisterAttached(
        "LocBindings",
        typeof(List<LocBinding>),
        typeof(LocBinding));

    private readonly string _key;
    private readonly DependencyObject _targetObject;
    private readonly DependencyProperty _targetProperty;

    public LocBinding(string key, DependencyObject targetObject, DependencyProperty dependencyProperty)
    {
        _key = key;
        _targetObject = targetObject;
        _targetProperty = dependencyProperty;
        KeepAlive(targetObject, this);
        // 目标控件通过附加属性维持绑定寿命；全局事件不能反向强引用已关闭窗口。
        var weak = new WeakReference<LocBinding>(this);
        Action? handler = null;
        handler = () =>
        {
            if (weak.TryGetTarget(out var binding)) binding.OnLanguageChanged();
            else L.LanguageChanged -= handler;
        };
        L.LanguageChanged += handler;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Value => L.Text(_key);

    private static void KeepAlive(DependencyObject targetObject, LocBinding binding)
    {
        if (targetObject.GetValue(LocBindingsProperty) is not List<LocBinding> bindings)
        {
            bindings = new List<LocBinding>();
            targetObject.SetValue(LocBindingsProperty, bindings);
        }

        bindings.Add(binding);
    }

    private void OnLanguageChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        _targetObject.Dispatcher.InvokeAsync(() =>
        {
            if (_targetProperty == ToolTipService.ToolTipProperty && _targetObject is FrameworkElement element)
            {
                if (ToolTipService.GetToolTip(element) is ToolTip toolTip)
                {
                    toolTip.IsOpen = false;
                }

                ToolTipService.SetToolTip(element, Value);
            }
            else
            {
                _targetObject.SetValue(_targetProperty, Value);
            }
        });
    }
}

public static class L
{
    private const string LanguageEnvironmentVariable = "DISKPILOT_X_LANGUAGE";
    public const string AutoLanguage = "auto";
    private static readonly object Gate = new();
    private static SkillLocalizationState state = new(AutoLanguage, CultureInfo.InstalledUICulture.Name,
        SkillLocalizationState.ResourceCulture(CultureInfo.InstalledUICulture));
    private static readonly ResourceManager ResourceManager = new("DiskPilotX.App.Localization.Strings", typeof(L).Assembly);

    static L() => SetLanguage(DiskPilotX.Core.AppCompatibility.GetEnvironmentVariable(LanguageEnvironmentVariable));

    public static string CurrentLanguageName => GetState().CultureName;
    public static event Action? LanguageChanged;
    public static event Action<SkillLocalizationState>? LocalizationChanged;
    public static SkillLocalizationState GetState() { lock (Gate) return state; }

    public static IDisposable Subscribe(Action<SkillLocalizationState> observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        LocalizationChanged += observer;
        return new SkillLocalizationSubscription(() => LocalizationChanged -= observer);
    }

    public static void SetLanguage(string? language)
    {
        var preference = string.IsNullOrWhiteSpace(language) ? AutoLanguage : language.Trim();
        CultureInfo culture;
        try
        {
            culture = preference.Equals(AutoLanguage, StringComparison.OrdinalIgnoreCase)
                ? CultureInfo.InstalledUICulture : CultureInfo.GetCultureInfo(preference);
        }
        catch (CultureNotFoundException)
        {
            preference = AutoLanguage;
            culture = CultureInfo.InstalledUICulture;
        }
        if (preference.Equals(AutoLanguage, StringComparison.OrdinalIgnoreCase)) preference = AutoLanguage;
        var next = new SkillLocalizationState(preference, culture.Name, SkillLocalizationState.ResourceCulture(culture));
        bool changed;
        lock (Gate)
        {
            changed = state != next;
            state = next;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        if (!changed) return;
        void Notify()
        {
            // 已排队的旧通知不能覆盖较新的语言状态。
            if (GetState() != next) return;
            LanguageChanged?.Invoke();
            LocalizationChanged?.Invoke(next);
        }
        if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess()) dispatcher.BeginInvoke(Notify);
        else Notify();
    }

    public static string Text(string key) => ResourceManager.GetString(key,
        CultureInfo.GetCultureInfo(GetState().ResourceCultureName)) ?? $"!{key}!";

    public static string Format(string key, params object?[] args) =>
        string.Format(CultureInfo.GetCultureInfo(GetState().CultureName), Text(key), args);
}
