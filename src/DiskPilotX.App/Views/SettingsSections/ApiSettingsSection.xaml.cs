using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views.SettingsSections;

public partial class ApiSettingsSection : UserControl
{
    public ApiSettingsSection()
    {
        InitializeComponent();
    }

    private async void ModelSettings_Loaded(object sender, RoutedEventArgs e)
    {
        if (IsVisible && DataContext is SettingsViewModel vm) await vm.EnsureModelCatalogAsync();
    }

    private async void ModelSettings_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsLoaded && e.NewValue is true && DataContext is SettingsViewModel vm) await vm.EnsureModelCatalogAsync();
    }

    private async void ModelConnectionEdit_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (IsLoaded && DataContext is SettingsViewModel vm) await vm.CompleteModelConfigurationAsync();
    }

    private async void ModelConnectionEdit_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !IsLoaded || DataContext is not SettingsViewModel vm) return;
        e.Handled = true;
        await vm.CompleteModelConfigurationAsync();
    }

    private async void ModelConnectionChoice_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 忽略初始化及下拉内容内部冒泡的选择事件。
        if (!IsLoaded || !ReferenceEquals(sender, e.OriginalSource)) return;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.DataBind);
        if (IsLoaded && DataContext is SettingsViewModel vm) await vm.CompleteModelConfigurationAsync();
    }

    private Thumb? _modelDragHandle;
    private Border? _modelDragRow;
    private string[]? _modelDragSnapshot;
    private string? _draggedModel;
    private int _modelDropIndex;
    private double _modelDragDistance;
    private System.Windows.Documents.Adorner? _modelDropAdorner;
    private System.Windows.Documents.AdornerLayer? _modelDropLayer;

    private void ModelHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is not Thumb { DataContext: string id } handle || DataContext is not SettingsViewModel vm || vm.IsRefreshingModels) return;
        _modelDragHandle = handle;
        _modelDragRow = FindModelRow(handle);
        _modelDragSnapshot = vm.ModelCandidates.ToArray();
        _draggedModel = id;
        _modelDropIndex = vm.ModelCandidates.IndexOf(id);
        _modelDragDistance = 0;
        if (_modelDragRow is not null) _modelDragRow.Opacity = 0.55;
        e.Handled = true;
    }

    private void ModelHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (_modelDragSnapshot is null || _modelDragRow is null || DataContext is not SettingsViewModel vm) return;
        if (!vm.ModelCandidates.SequenceEqual(_modelDragSnapshot))
        {
            _modelDragHandle?.CancelDrag();
            return;
        }
        _modelDragDistance = Math.Max(_modelDragDistance, Math.Abs(e.VerticalChange));
        if (_modelDragDistance < SystemParameters.MinimumVerticalDragDistance) return;
        // Thumb 的位移相对初始按下点；使用事件坐标，避免依赖全局鼠标位置。
        var pointerY = _modelDragRow.TranslatePoint(new Point(0, _modelDragRow.ActualHeight / 2), ModelOrderList).Y + e.VerticalChange;
        var viewportY = ModelOrderList.TranslatePoint(new Point(0, pointerY), ModelOrderScroll).Y;
        if (viewportY < 18) ModelOrderScroll.ScrollToVerticalOffset(ModelOrderScroll.VerticalOffset - 12);
        else if (viewportY > ModelOrderScroll.ActualHeight - 18) ModelOrderScroll.ScrollToVerticalOffset(ModelOrderScroll.VerticalOffset + 12);
        ModelOrderList.UpdateLayout();

        // 只显示插入标记，不在每个 MouseMove 上写配置或刷新聊天菜单。
        var insertion = vm.ModelCandidates.Count;
        double lineY = 0;
        for (var i = 0; i < vm.ModelCandidates.Count; i++)
        {
            if (ModelOrderList.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row) continue;
            var top = row.TranslatePoint(new Point(0, 0), ModelOrderList).Y;
            lineY = top + row.ActualHeight;
            if (pointerY < top + row.ActualHeight / 2) { insertion = i; lineY = top; break; }
        }
        var source = vm.ModelCandidates.IndexOf(_draggedModel!);
        _modelDropIndex = Math.Clamp(insertion > source ? insertion - 1 : insertion, 0, vm.ModelCandidates.Count - 1);
        ClearModelInsertion();
        _modelDropLayer = System.Windows.Documents.AdornerLayer.GetAdornerLayer(ModelOrderList);
        if (_modelDropLayer is not null && _modelDropIndex != source)
        {
            _modelDropAdorner = new ModelInsertionAdorner(ModelOrderList, lineY, (Brush?)TryFindResource("VP.AccentBrush") ?? Brushes.DodgerBlue) { IsHitTestVisible = false };
            _modelDropLayer.Add(_modelDropAdorner);
        }
        e.Handled = true;
    }

    private void ModelHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var id = _draggedModel;
        var target = _modelDropIndex;
        var snapshot = _modelDragSnapshot;
        var moved = _modelDragDistance >= SystemParameters.MinimumVerticalDragDistance;
        ClearModelDrag();
        if (!e.Canceled && moved && id is not null && DataContext is SettingsViewModel vm && snapshot is not null && vm.ModelCandidates.SequenceEqual(snapshot))
            vm.MoveModel(id, target);
        e.Handled = true;
    }

    private void ModelHandle_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _modelDragHandle is not null)
        {
            _modelDragHandle.CancelDrag(); e.Handled = true; return;
        }
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Keyboard.Modifiers != ModifierKeys.Alt || key is not (Key.Up or Key.Down)
            || sender is not Thumb { DataContext: string id } || DataContext is not SettingsViewModel vm) return;
        vm.MoveModel(id, vm.ModelCandidates.IndexOf(id) + (key == Key.Up ? -1 : 1));
        e.Handled = true;
    }

    private static Border? FindModelRow(DependencyObject current)
    {
        for (var parent = VisualTreeHelper.GetParent(current); parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Border { Name: "ModelRow" } row) return row;
        return null;
    }

    private void ClearModelInsertion()
    {
        if (_modelDropAdorner is not null) _modelDropLayer?.Remove(_modelDropAdorner);
        _modelDropAdorner = null; _modelDropLayer = null;
    }

    private void ClearModelDrag()
    {
        ClearModelInsertion();
        if (_modelDragRow is not null) _modelDragRow.Opacity = 1;
        _modelDragHandle = null; _modelDragRow = null; _modelDragSnapshot = null; _draggedModel = null;
    }

    private sealed class ModelInsertionAdorner(UIElement element, double y, Brush brush) : System.Windows.Documents.Adorner(element)
    {
        protected override void OnRender(DrawingContext context)
        {
            context.DrawLine(new Pen(brush, 2), new Point(1, y), new Point(Math.Max(1, AdornedElement.RenderSize.Width - 1), y));
        }
    }
    // 刷新按钮独立于下拉项选择，点击后保持当前身份和菜单。
    private async void CliIdentityRefresh_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button { DataContext: CliIdentityOption option } || option.RefreshCommand is null) return;
        e.Handled = true;
        await option.RefreshCommand.ExecuteAsync(option);
    }

    // PasswordBox 出于安全考虑不支持双向绑定，这里把输入同步到 ViewModel。
    private void ProxyPasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel)
            viewModel.ProxyPassword = ((PasswordBox)sender).Password;
    }
}
