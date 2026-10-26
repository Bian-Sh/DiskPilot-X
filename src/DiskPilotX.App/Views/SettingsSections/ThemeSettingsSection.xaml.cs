using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DiskPilotX.App.ViewModels;
using DiskPilotX.Core.Models.Theme;

namespace DiskPilotX.App.Views.SettingsSections;

public partial class ThemeSettingsSection : UserControl
{
    public ThemeSettingsSection()
    {
        InitializeComponent();
    }

    private void ThemePreset_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border { Tag: ThemeProfile preset } border)
            return;

        if (DataContext is SettingsViewModel vm)
        {
            vm.ApplyThemePresetCommand.Execute(preset);
        }

        var scaleTransform = new ScaleTransform(1, 1);
        border.RenderTransformOrigin = new Point(0.5, 0.5);
        border.RenderTransform = scaleTransform;

        var pulseIn = new DoubleAnimation(1, 1.05, TimeSpan.FromMilliseconds(80))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var pulseOut = new DoubleAnimation(1.05, 1, TimeSpan.FromMilliseconds(120))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };

        pulseIn.Completed += (_, _) =>
        {
            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseOut);
            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseOut);
        };
        scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, pulseIn);
        scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, pulseIn);
    }
}
