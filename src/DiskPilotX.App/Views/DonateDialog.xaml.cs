using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DiskPilotX.App.Views;

public partial class DonateDialog : Window
{
    private bool _isShaking;

    public DonateDialog()
    {
        InitializeComponent();
        Owner = Application.Current.MainWindow;

        if (Owner is not null)
        {
            Width = Owner.ActualWidth;
            Height = Owner.ActualHeight;
            Left = Owner.Left;
            Top = Owner.Top;
        }

        Loaded += (_, _) => AnimateIn();
    }

    private void AnimateIn()
    {
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        RootGrid.Opacity = 0;
        var maskAnim = new DoubleAnimation(0, 1, duration);
        RootGrid.BeginAnimation(OpacityProperty, maskAnim);

        CardBorder.RenderTransform = new ScaleTransform(0.9, 0.9);
        CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleXAnim = new DoubleAnimation(0.9, 1, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(0.9, 1, duration) { EasingFunction = easing };

        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is Grid grid && grid == RootGrid)
        {
            ShakeCard();
            e.Handled = true;
        }
    }

    private void ShakeCard()
    {
        if (_isShaking)
            return;

        _isShaking = true;

        var timeline = new ThicknessAnimationUsingKeyFrames
        {
            Duration = TimeSpan.FromMilliseconds(350)
        };
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0), TimeSpan.Zero));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-8, 0, 8, 0), TimeSpan.FromMilliseconds(70)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(6, 0, -6, 0), TimeSpan.FromMilliseconds(140)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(-4, 0, 4, 0), TimeSpan.FromMilliseconds(210)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(2, 0, -2, 0), TimeSpan.FromMilliseconds(280)));
        timeline.KeyFrames.Add(new LinearThicknessKeyFrame(new Thickness(0), TimeSpan.FromMilliseconds(350)));

        Storyboard.SetTarget(timeline, CardBorder);
        Storyboard.SetTargetProperty(timeline, new PropertyPath(MarginProperty));

        var storyboard = new Storyboard();
        storyboard.Children.Add(timeline);
        storyboard.Completed += (_, _) => _isShaking = false;
        storyboard.Begin();
    }

    private void AnimateOut()
    {
        var duration = TimeSpan.FromMilliseconds(250);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        var maskAnim = new DoubleAnimation(1, 0, duration);

        CardBorder.RenderTransform = new ScaleTransform(1, 1);
        CardBorder.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleXAnim = new DoubleAnimation(1, 0.9, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(1, 0.9, duration) { EasingFunction = easing };

        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        CardBorder.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

        maskAnim.Completed += (_, _) => Close();
        RootGrid.BeginAnimation(OpacityProperty, maskAnim);
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        AnimateOut();
    }
}

