using Path = System.IO.Path;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Data;
using DiskPilotX.App.Converters;
using DiskPilotX.Core.Enums;
using DiskPilotX.Core.Models;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DiskPilotX.App.Localization;

namespace DiskPilotX.App.Controls;

public partial class AppModalHost : UserControl
{
    private readonly Stack<ModalRequest> _requests = new();
    private ModalRequest? _currentRequest;
    private bool _isShaking;
    private bool _isAnimating;
    private long _presentationVersion;

    public AppModalHost()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateCardBounds();
    }

    public Task<int> ShowAsync(string title, string content, ModalMessageType messageType, ModalButtonFlags buttonFlags)
    {
        var view = CreateMessageView(title, content, messageType, buttonFlags);
        return ShowAsync<int>(view, ModalResult.Negative, 460, 700, isMessage: true);
    }

    /// <summary>复用关闭确认的模态外观；破坏性操作默认取消，必须显式点击确认。</summary>
    public Task<int> ShowConfirmationAsync(string title, string content, string positiveText)
    {
        var view = CreateMessageView(title, content, ModalMessageType.Warning,
            ModalButtonFlags.Positive | ModalButtonFlags.Negative, positiveText, defaultToNegative: true);
        return ShowAsync<int>(view, ModalResult.Negative, 460, 700, isMessage: true);
    }

    /// <summary>使用字面量 Inline 展示清理摘要，不把用户文件名作为 Markdown 或 XAML 解析。</summary>
    public Task<int> ShowCleanupConfirmationAsync(string title, IReadOnlyList<CleanupRecommendation> items,
        DeletionMode mode, string positiveText)
    {
        if (items.Count == 0) throw new ArgumentException("清理确认至少需要一个项目。", nameof(items));
        var inlines = new List<Inline> { new Run(items.Count == 1 ? "将把 " : "将把所选的 ") };
        if (items.Count == 1)
        {
            var path = Path.TrimEndingDirectorySeparator(items[0].TargetPath);
            inlines.Add(Emphasis(Path.GetFileName(path)));
            inlines.Add(new Run("（"));
        }
        inlines.Add(Emphasis($"{items.Count} 个项目"));
        inlines.Add(new Run(items.Count == 1 ? "，共 " : "（共 "));
        inlines.Add(Emphasis(FileSizeConverter.FormatSize(items.Sum(item => item.Size))));
        inlines.Add(new Run("）"));
        inlines.Add(Emphasis(mode == DeletionMode.PermanentDelete ? "永久删除" : "移入回收站"));
        inlines.Add(new Run(mode == DeletionMode.PermanentDelete
            ? "。此操作无法恢复，请确认这些项目不再需要。"
            : "，之后可从回收站恢复。"));
        var view = CreateMessageView(title, "", mode == DeletionMode.PermanentDelete ? ModalMessageType.Error : ModalMessageType.Warning,
            ModalButtonFlags.Positive | ModalButtonFlags.Negative, positiveText, defaultToNegative: true, inlines);
        return ShowAsync<int>(view, ModalResult.Negative, 460, 700, isMessage: true);

        static Run Emphasis(string text) => new(text) { FontWeight = FontWeights.SemiBold };
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
        // 先完成视觉状态转换，再唤醒调用方，避免同步后续弹窗被旧动画关闭。
        ShowNextOrHide();
        request.Complete(result);
    }

    private Task<TResult> ShowAsync<TResult>(FrameworkElement content, TResult defaultResult, double maxWidth = 620, double maxHeight = 700, bool isMessage = false)
    {
        var request = new ModalRequest(content, typeof(TResult), defaultResult, maxWidth, maxHeight, isMessage);
        _requests.Push(request);
        if (_currentRequest is null)
        {
            ShowNextOrHide();
        }

        return request.Task.ContinueWith(t => (TResult)t.Result!, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ShowNextOrHide()
    {
        var version = ++_presentationVersion;
        if (_requests.Count == 0)
        {
            AnimateOut(() =>
            {
                if (version != _presentationVersion || _currentRequest is not null) return;
                ModalContentPresenter.Content = null;
                Visibility = Visibility.Collapsed;
                Keyboard.ClearFocus();
            });
            return;
        }

        _currentRequest = _requests.Pop();
        ModalCard.MaxWidth = _currentRequest.MaxWidth;
        ModalCard.Height = double.NaN;
        UpdateCardBounds();
        ModalContentPresenter.Content = _currentRequest.Content;
        Visibility = Visibility.Visible;
        AnimateIn();
        Focus();
        var content = _currentRequest.Content;
        var defaultButton = (content as Grid)?.Children.OfType<StackPanel>()
            .SelectMany(panel => panel.Children.OfType<Button>()).FirstOrDefault(button => button.IsDefault);
        if (defaultButton is not null)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
            {
                if (ReferenceEquals(ModalContentPresenter.Content, content)) defaultButton.Focus();
            }));
    }

    private void UpdateCardBounds()
    {
        if (_currentRequest is null) return;
        ModalCard.MinHeight = 0;
        ModalCard.MaxHeight = ActualHeight > 0 ? Math.Max(0, Math.Min(_currentRequest.MaxHeight, ActualHeight - 48)) : _currentRequest.MaxHeight;
        ModalCard.MaxWidth = ActualWidth > 0 ? Math.Max(0, Math.Min(_currentRequest.MaxWidth, ActualWidth - 48)) : _currentRequest.MaxWidth;
        ModalCard.Width = _currentRequest.IsMessage ? ModalCard.MaxWidth : double.NaN;
        if (_currentRequest.IsMessage)
            // 原始关闭弹窗约386×180；扩大外框，不缩放标题和底部锚点。
            ModalCard.MinHeight = Math.Min(ModalCard.MaxHeight, ModalCard.Width * 180 / 386);
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

    private FrameworkElement CreateMessageView(string title, string content, ModalMessageType messageType, ModalButtonFlags buttonFlags,
        string? positiveText = null, bool defaultToNegative = false, IEnumerable<Inline>? contentInlines = null)
    {
        var root = new Grid { MaxWidth = 480 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleBlock = new TextBlock
        {
            Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
            LineHeight = 24, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center
        };
        var titleBg = new Border
        {
            CornerRadius = new CornerRadius(22, 22, 0, 0),
            Padding = new Thickness(28, 12, 28, 12),
            Child = titleBlock
        };
        // 固定素雅底色配纯白标题；颜色与几何尺寸分开控制。
        titleBg.SetResourceReference(Border.BackgroundProperty, messageType switch
        {
            ModalMessageType.Error => "VP.ModalDangerHeaderBrush",
            ModalMessageType.Warning => "VP.ModalWarningHeaderBrush",
            _ => "VP.ModalNormalHeaderBrush"
        });
        root.Children.Add(titleBg);

        var bodyText = new TextBlock
        {
            Text = content, FontSize = 14,
            Foreground = (Brush)FindResource("VP.TextSecondaryBrush"),
            TextWrapping = TextWrapping.Wrap, LineHeight = 24,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Left,
            Margin = new Thickness(28, 16, 28, 16)
        };
        if (contentInlines is not null)
        {
            bodyText.Inlines.Clear();
            bodyText.Inlines.AddRange(contentInlines);
        }
        var bodyPanel = new Grid();
        bodyPanel.Children.Add(bodyText);
        var scroll = new ScrollViewer
        {
            CanContentScroll = false,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = bodyPanel
        };
        // 短文案在视口内居中并保持上下等距；长文案按自身高度从顶部滚动。
        bodyPanel.SetBinding(MinHeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = scroll });
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // Button row ────────────────────────────────────
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(20, 0, 20, 16)
        };
        Grid.SetRow(buttonPanel, 2);
        root.Children.Add(buttonPanel);

        var isFirstButton = true;
        foreach (var (label, result, isPrimary) in GetButtons(buttonFlags))
        {
            var button = new Button
            {
                Content = result == ModalResult.Positive ? positiveText ?? label : label,
                MinWidth = 88,
                Height = 34,
                Margin = isFirstButton ? new Thickness(0) : new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsDefault = defaultToNegative ? result == ModalResult.Negative : isPrimary,
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
        private readonly TaskCompletionSource<object?> _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ModalRequest(FrameworkElement content, Type resultType, object? defaultResult, double maxWidth, double maxHeight, bool isMessage)
        {
            Content = content;
            _resultType = resultType;
            _defaultResult = defaultResult;
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            IsMessage = isMessage;
        }

        public FrameworkElement Content { get; }

        public double MaxWidth { get; }

        public double MaxHeight { get; }

        public bool IsMessage { get; }

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























