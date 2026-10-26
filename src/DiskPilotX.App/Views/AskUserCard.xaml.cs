using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiskPilotX.App.ViewModels;

namespace DiskPilotX.App.Views;

/// <summary>设置 AskUserDialogViewModel 为 DataContext 即可显示并交互。</summary>
public partial class AskUserCard : UserControl
{
    public AskUserCard() => InitializeComponent();

    private AskUserDialogViewModel? ViewModel => DataContext as AskUserDialogViewModel;

    private void Previous_OnClick(object sender, RoutedEventArgs e) => ViewModel?.MovePrevious();
    private void Next_OnClick(object sender, RoutedEventArgs e) => ViewModel?.TryAdvanceCurrentField();
    private void Cancel_OnClick(object sender, RoutedEventArgs e) => ViewModel?.TryCancel();

    private async void SingleChoice_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true }
            || ViewModel is not { IsActive: true, HasNextField: true } vm
            || vm.CurrentField is not AskUserSingleSelectFieldViewModel { AllowsCustomInput: false }) return;

        // 给选择反馈留出时间；切题、取消或替换 DataContext 后不再延迟跳转。
        var field = vm.CurrentField;
        await Task.Delay(180);
        if (IsVisible && ReferenceEquals(DataContext, vm) && ReferenceEquals(vm.CurrentField, field))
            vm.TryAdvanceCurrentField();
    }

    private void Input_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox input) return;
        var control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var multiline = input.DataContext is AskUserTextFieldViewModel { IsMultiline: true };
        if (multiline ? !control : control) return;
        AdvanceOrSubmit();
        e.Handled = true;
    }

    private void AdvanceOrSubmit()
    {
        if (ViewModel is not { IsActive: true } vm) return;
        var submit = vm.IsLastField;
        if (vm.TryAdvanceCurrentField() && submit) vm.TryCompleteSubmission();
    }
}
