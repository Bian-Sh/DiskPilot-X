using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using DiskPilotX.App.Localization;

namespace DiskPilotX.App.Controls;

public partial class AppModalHost : UserControl
{
    private readonly Stack<ModalRequest> _requests = new();
    private ModalRequest? _currentRequest;
    private bool _isShaking;
    private bool _isAnimating;

    public AppModalHost()
    {
        InitializeComponent();
    }

    public Task<int> ShowAsync(string title, string content, ModalMessageType messageType, ModalButtonFlags buttonFlags)
    {
        var view = CreateMessageView(title, content, messageType, buttonFlags);
        return ShowAsync<int>(view, ModalResult.Negative, 460, 180);
    }

    public async Task<MessageBoxResult> ShowMessageAsync(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var result = await ShowAsync(title, message, ToModalMessageType(image), ToModalButtonFlags(buttons));
        return ToMessageBoxResult(result, buttons);
    }

    public Task<bool?> ShowContentAsync(FrameworkElement content, double maxWidth = 620, double maxHeight = 700)
    {
        return ShowAsync<bool?>(content, null, maxWidth, maxHeight);
    }

    public void CloseCurrent(object? result)
    {
        if (_currentRequest is null)
            return;

        var request = _currentRequest;
        _currentRequest = null;
        request.Complete(result);
        ShowNextOrHide();
    }

    private Task<TResult> ShowAsync<TResult>(FrameworkElement content, TResult defaultResult, double maxWidth = 620, double maxHeight = 700)
    {
        var request = new ModalRequest(content, typeof(TResult), defaultResult, maxWidth, maxHeight);
        _requests.Push(request);
        if (_currentRequest is null)
        {
            ShowNextOrHide();
        }

        return request.Task.ContinueWith(t => (TResult)t.Result!, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ShowNextOrHide()
    {
        if (_requests.Count == 0)
        {
            AnimateOut(() =>
            {
                ModalContentPresenter.Content = null;
                Visibility = Visibility.Collapsed;
                Keyboard.ClearFocus();
            });
            return;
        }

        _currentRequest = _requests.Pop();
        ModalCard.MaxWidth = _currentRequest.MaxWidth;
        ModalCard.Height = _currentRequest.MaxHeight;
        ModalContentPresenter.Content = _currentRequest.Content;
        Visibility = Visibility.Visible;
        AnimateIn();
        Focus();
    }

    private void AnimateIn()
    {
        _isAnimating = true;
        var duration = TimeSpan.FromMilliseconds(350);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        RootGrid.Opacity = 0;
        var maskAnim = new DoubleAnimation(0, 1, duration);
        RootGrid.BeginAnimation(OpacityProperty, maskAnim);

        ModalCard.RenderTransform = new ScaleTransform(0.9, 0.9);
        ModalCard.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleXAnim = new DoubleAnimation(0.9, 1, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(0.9, 1, duration) { EasingFunction = easing };

        scaleXAnim.Completed += (_, _) => _isAnimating = false;
        ModalCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        ModalCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
    }

    private void AnimateOut(Action onComplete)
    {
        _isAnimating = true;
        var duration = TimeSpan.FromMilliseconds(250);
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };

        var maskAnim = new DoubleAnimation(1, 0, duration);

        ModalCard.RenderTransform = new ScaleTransform(1, 1);
        ModalCard.RenderTransformOrigin = new Point(0.5, 0.5);

        var scaleXAnim = new DoubleAnimation(1, 0.9, duration) { EasingFunction = easing };
        var scaleYAnim = new DoubleAnimation(1, 0.9, duration) { EasingFunction = easing };

        ModalCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
        ModalCard.RenderTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);

        maskAnim.Completed += (_, _) =>
        {
            _isAnimating = false;
            onComplete();
        };
        RootGrid.BeginAnimation(OpacityProperty, maskAnim);
    }

    private void RootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isAnimating)
            return;

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

        Storyboard.SetTarget(timeline, ModalCard);
        Storyboard.SetTargetProperty(timeline, new PropertyPath(MarginProperty));

        var storyboard = new Storyboard();
        storyboard.Children.Add(timeline);
        storyboard.Completed += (_, _) => _isShaking = false;
        storyboard.Begin();
    }

    private FrameworkElement CreateMessageView(string title, string content, ModalMessageType messageType, ModalButtonFlags buttonFlags)
    {
        var root = new Grid { MinWidth = 360, MaxWidth = 480 };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });             // top edge
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                // title
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                // content + icon
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // spacer
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                // buttons
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });             // bottom edge

        // Title background bar ──────────────────────────
        var titleBg = new Border
        {
            Background = (Brush)FindResource("VP.SurfaceBrush"),
            CornerRadius = new CornerRadius(22, 22, 0, 0),
            Margin = new Thickness(0, 0, 0, 0)
        };
        Grid.SetRow(titleBg, 0);
        Grid.SetRowSpan(titleBg, 2); // top edge + title
        root.Children.Add(titleBg);

        // Title ──────────────────────────────────────────
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("VP.TextPrimaryBrush"),
            Margin = new Thickness(28, 0, 28, 10),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(titleBlock, 1);
        root.Children.Add(titleBlock);

        // Content row (icon + text) ─────────────────────
        var contentPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(40, 0, 40, 0)
        };
        Grid.SetRow(contentPanel, 3);
        root.Children.Add(contentPanel);

        var iconText = GetIconText(messageType);
        if (!string.IsNullOrEmpty(iconText))
        {
            var iconBrush = GetIconBrush(messageType);
            var iconRing = new Grid
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconRing.Children.Add(new Ellipse
            {
                Stroke = iconBrush,
                StrokeThickness = 1.6,
                Fill = Brushes.Transparent
            });
            iconRing.Children.Add(new TextBlock
            {
                Text = iconText,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = iconBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            });
            contentPanel.Children.Add(iconRing);
        }

        contentPanel.Children.Add(new TextBlock
        {
            Text = content,
            MaxWidth = 330,
            FontSize = 13,
            Foreground = (Brush)FindResource("VP.TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center
        });

        // Button row ────────────────────────────────────
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Grid.SetRow(buttonPanel, 5);
        root.Children.Add(buttonPanel);

        var isFirstButton = true;
        foreach (var (label, result, isPrimary) in GetButtons(buttonFlags))
        {
            var button = new Button
            {
                Content = label,
                MinWidth = 88,
                Height = 34,
                Margin = isFirstButton ? new Thickness(0) : new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsDefault = isPrimary,
                IsCancel = result == ModalResult.Negative,
                Style = (Style)FindResource(isPrimary ? "VP.AccentButton" : "VP.Button")
            };
            isFirstButton = false;
            button.Click += (_, _) => CloseCurrent(result);
            buttonPanel.Children.Add(button);
        }

        return root;
    }
    private IEnumerable<(string Label, int Result, bool IsPrimary)> GetButtons(ModalButtonFlags buttonFlags)
    {
        if (buttonFlags.HasFlag(ModalButtonFlags.Negative))
        {
            return new[]
            {
                (L.Text("CancelButton"), ModalResult.Negative, false),
                (L.Text("ConfirmButton"), ModalResult.Positive, true)
            };
        }

        return new[] { (L.Text("OkButton"), ModalResult.Positive, true) };
    }

    private static string GetIconText(ModalMessageType messageType)
    {
        return messageType switch
        {
            ModalMessageType.Error => "✕",
            ModalMessageType.Warning => "!",
            ModalMessageType.Info => "i",
            _ => string.Empty
        };
    }

    private Brush GetIconBrush(ModalMessageType messageType)
    {
        return messageType switch
        {
            ModalMessageType.Error => (Brush)FindResource("VP.DangerBrush"),
            ModalMessageType.Warning => (Brush)FindResource("VP.WarningBrush"),
            ModalMessageType.Info => (Brush)FindResource("VP.AccentBrush"),
            _ => (Brush)FindResource("VP.TextSecondaryBrush")
        };
    }

    private static ModalMessageType ToModalMessageType(MessageBoxImage image)
    {
        return image switch
        {
            MessageBoxImage.Error => ModalMessageType.Error,
            MessageBoxImage.Warning => ModalMessageType.Warning,
            _ => ModalMessageType.Info
        };
    }

    private static ModalButtonFlags ToModalButtonFlags(MessageBoxButton buttons)
    {
        return buttons == MessageBoxButton.OK ? ModalButtonFlags.Positive : ModalButtonFlags.Positive | ModalButtonFlags.Negative;
    }

    private static MessageBoxResult ToMessageBoxResult(int result, MessageBoxButton buttons)
    {
        if (result == ModalResult.Positive)
        {
            return buttons switch
            {
                MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.Yes,
                _ => MessageBoxResult.OK
            };
        }

        return buttons switch
        {
            MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel => MessageBoxResult.No,
            _ => MessageBoxResult.None
        };
    }

    private sealed class ModalRequest
    {
        private readonly Type _resultType;
        private readonly object? _defaultResult;
        private readonly TaskCompletionSource<object?> _source = new();

        public ModalRequest(FrameworkElement content, Type resultType, object? defaultResult, double maxWidth, double maxHeight)
        {
            Content = content;
            _resultType = resultType;
            _defaultResult = defaultResult;
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
        }

        public FrameworkElement Content { get; }

        public double MaxWidth { get; }

        public double MaxHeight { get; }

        public Task<object?> Task => _source.Task;

        public void Complete(object? result)
        {
            if (result is null && _resultType.IsValueType && Nullable.GetUnderlyingType(_resultType) is null)
            {
                result = _defaultResult;
            }

            _source.TrySetResult(result ?? _defaultResult);
        }
    }
}

public enum ModalMessageType
{
    Info,
    Warning,
    Error
}

[Flags]
public enum ModalButtonFlags
{
    Positive = 1,
    Negative = 2
}

public static class ModalResult
{
    public const int Positive = 0;
    public const int Negative = 1;
}























