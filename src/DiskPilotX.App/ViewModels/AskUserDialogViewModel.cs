using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using DiskPilotX.Core.Models;

namespace DiskPilotX.App.ViewModels;

/// <summary>把挂起的用户决策请求投影为逐题表单，并在前进或提交时执行安全校验。</summary>
public sealed class AskUserDialogViewModel : ObservableObject
{
    private static readonly IReadOnlyDictionary<string, object?> EmptyValues =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
    private static readonly IReadOnlyDictionary<string, string> EmptyCustomInputs =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

    private readonly UserDecisionRequest request;
    private readonly TaskCompletionSource<JsonNode> completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int currentFieldIndex;
    private string errorSummary = string.Empty;

    public AskUserDialogViewModel(UserDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = UserDecisionValidator.ValidateRequest(request);
        if (errors.Count > 0) throw new UserDecisionValidationException(errors);
        this.request = request;
        Fields = request.Fields.Select(CreateField).ToArray();
        if (Fields.Count == 0)
        {
            throw new ArgumentException("用户决策请求至少需要一个字段。", nameof(request));
        }
    }

    public bool IsCompleted => completion.Task.IsCompleted;

    public bool IsActive => !IsCompleted;

    public string Title => request.Title;

    public string Question => request.Question;

    public string? Description => request.Description;

    public string? ImpactSummary => request.ImpactSummary;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool HasImpactSummary => !string.IsNullOrWhiteSpace(ImpactSummary);

    public bool AllowCancel => request.AllowCancel;

    public Task<JsonNode> Completion => completion.Task;

    public IReadOnlyList<AskUserFieldViewModel> Fields { get; }

    public int CurrentFieldIndex => currentFieldIndex;

    public AskUserFieldViewModel CurrentField => Fields[CurrentFieldIndex];

    public string StepText => $"{CurrentFieldIndex + 1} / {Fields.Count}";

    public bool HasPreviousField => CurrentFieldIndex > 0;

    public bool HasNextField => CurrentFieldIndex < Fields.Count - 1;

    public bool IsLastField => !HasNextField;

    public bool CanSkipCurrentField => !CurrentField.IsRequired;

    public bool CanContinueCurrentField => FindCurrentError() is null;

    public string PrimaryActionText => IsLastField ? "提交" : "继续";

    public string ErrorSummary
    {
        get => errorSummary;
        private set
        {
            if (SetProperty(ref errorSummary, value))
            {
                OnPropertyChanged(nameof(HasErrors));
            }
        }
    }

    public bool HasErrors => ErrorSummary.Length > 0;

    public void MovePrevious()
    {
        if (IsCompleted || !HasPreviousField)
        {
            return;
        }

        currentFieldIndex--;
        NotifyCurrentFieldChanged();
    }

    public void MoveNextWithoutValidation()
    {
        if (IsCompleted || !HasNextField)
        {
            return;
        }

        currentFieldIndex++;
        NotifyCurrentFieldChanged();
    }

    public bool TryAdvanceCurrentField()
    {
        if (IsCompleted || !ValidateCurrentField(showError: true))
        {
            return false;
        }

        if (HasNextField)
        {
            MoveNextWithoutValidation();
            return true;
        }

        return ValidateFields(BuildValues(), BuildCustomInputs());
    }

    public bool TrySkipCurrentField(out bool shouldSubmit)
    {
        shouldSubmit = false;
        if (IsCompleted || !CanSkipCurrentField)
        {
            return false;
        }

        CurrentField.ClearValue();
        if (HasNextField)
        {
            MoveNextWithoutValidation();
            return true;
        }

        shouldSubmit = ValidateFields(BuildValues(), BuildCustomInputs());
        return shouldSubmit;
    }

    public bool TryCompleteSubmission()
    {
        if (IsCompleted || !TryBuildResult(out var values, out var customInputs)) return false;
        return Complete(UserDecisionResult.Submit(values, customInputs));
    }

    /// <summary>用户取消操作，遵守请求的 allow_cancel。</summary>
    public bool TryCancel() => AllowCancel && Complete(UserDecisionResult.Cancel("用户取消"));

    /// <summary>任务停止或窗口关闭时强制释放等待，即使请求不允许用户主动取消。</summary>
    public void Cancel() => Complete(UserDecisionResult.Cancel("任务已停止"));

    private bool Complete(UserDecisionResult result)
    {
        if (!completion.TrySetResult(result.ToJson())) return false;
        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsActive));
        return true;
    }

    public bool TryBuildResult(out IReadOnlyDictionary<string, object?> values) =>
        TryBuildResult(out values, out _);

    public bool TryBuildResult(
        out IReadOnlyDictionary<string, object?> values,
        out IReadOnlyDictionary<string, string> customInputs)
    {
        var projectedValues = BuildValues();
        var projectedCustomInputs = BuildCustomInputs();
        if (!ValidateFields(projectedValues, projectedCustomInputs))
        {
            values = EmptyValues;
            customInputs = EmptyCustomInputs;
            return false;
        }

        values = new ReadOnlyDictionary<string, object?>(projectedValues);
        customInputs = new ReadOnlyDictionary<string, string>(projectedCustomInputs);
        return true;
    }

    private AskUserFieldViewModel CreateField(UserDecisionField field) => field.Type switch
    {
        UserDecisionFieldType.SingleSelect => new AskUserSingleSelectFieldViewModel(field, OnFieldChanged),
        UserDecisionFieldType.MultiSelect => new AskUserMultiSelectFieldViewModel(field, OnFieldChanged),
        UserDecisionFieldType.Number => new AskUserNumberFieldViewModel(field, OnFieldChanged),
        UserDecisionFieldType.Text => new AskUserTextFieldViewModel(field, OnFieldChanged),
        _ => throw new ArgumentOutOfRangeException(nameof(field), "不支持的用户决策字段类型。"),
    };

    private Dictionary<string, object?> BuildValues()
    {
        var values = new Dictionary<string, object?>(Fields.Count, StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            values.Add(field.Id, field.GetValue());
        }

        return values;
    }

    private Dictionary<string, string> BuildCustomInputs()
    {
        var customInputs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var field in Fields)
        {
            if (field.GetCustomInput() is { } customInput)
            {
                customInputs.Add(field.Id, customInput);
            }
        }

        return customInputs;
    }

    private void OnFieldChanged()
    {
        ValidateCurrentField(showError: true);
        OnPropertyChanged(nameof(CanContinueCurrentField));
    }

    private UserDecisionValidationError? FindCurrentError() =>
        UserDecisionValidator.ValidateSubmission(request, BuildValues(), BuildCustomInputs())
            .FirstOrDefault(error => string.Equals(error.FieldId, CurrentField.Id, StringComparison.Ordinal));

    private bool ValidateCurrentField(bool showError)
    {
        var error = FindCurrentError();
        if (showError)
        {
            CurrentField.ErrorMessage = error?.Message ?? string.Empty;
            ErrorSummary = error is null
                ? string.Empty
                : "请检查当前问题的答案。";
        }

        return error is null;
    }

    private bool ValidateFields(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, string> customInputs)
    {
        var errors = UserDecisionValidator.ValidateSubmission(request, values, customInputs);
        foreach (var field in Fields)
        {
            field.ErrorMessage = errors.FirstOrDefault(error =>
                string.Equals(error.FieldId, field.Id, StringComparison.Ordinal))?.Message ?? string.Empty;
        }

        if (errors.FirstOrDefault() is { } firstError)
        {
            var index = Fields.ToList().FindIndex(field => field.Id == firstError.FieldId);
            if (index >= 0 && index != currentFieldIndex)
            {
                currentFieldIndex = index;
                NotifyCurrentFieldChanged();
            }
        }

        ErrorSummary = errors.Count == 0
            ? string.Empty
            : "请检查当前问题的答案。";
        OnPropertyChanged(nameof(CanContinueCurrentField));
        return errors.Count == 0;
    }

    private void NotifyCurrentFieldChanged()
    {
        ErrorSummary = CurrentField.HasError
            ? "请检查当前问题的答案。"
            : string.Empty;
        OnPropertyChanged(nameof(CurrentFieldIndex));
        OnPropertyChanged(nameof(CurrentField));
        OnPropertyChanged(nameof(StepText));
        OnPropertyChanged(nameof(HasPreviousField));
        OnPropertyChanged(nameof(HasNextField));
        OnPropertyChanged(nameof(IsLastField));
        OnPropertyChanged(nameof(CanSkipCurrentField));
        OnPropertyChanged(nameof(CanContinueCurrentField));
        OnPropertyChanged(nameof(PrimaryActionText));
    }
}

public abstract class AskUserFieldViewModel : ObservableObject
{
    private string errorMessage = string.Empty;
    private bool clearingValue;
    private bool isSkipped;

    protected AskUserFieldViewModel(UserDecisionField field, Action changed)
    {
        Field = field;
        Changed = changed;
    }

    protected UserDecisionField Field { get; }

    protected Action Changed { get; }

    public string Id => Field.Id;

    public string Label => Field.Label;

    public bool IsRequired => Field.IsRequired;

    public string RequiredSuffix => IsRequired ? " *" : string.Empty;

    public string ErrorMessage
    {
        get => errorMessage;
        internal set
        {
            if (SetProperty(ref errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public bool HasError => ErrorMessage.Length > 0;

    internal object? GetValue() => isSkipped ? null : GetValueCore();

    internal string? GetCustomInput() => isSkipped ? null : GetCustomInputCore();

    internal void ClearValue()
    {
        clearingValue = true;
        try
        {
            ClearValueCore();
        }
        finally
        {
            clearingValue = false;
        }

        isSkipped = true;
        ErrorMessage = string.Empty;
        Changed();
    }

    protected void NotifyValueChanged()
    {
        if (clearingValue)
        {
            return;
        }

        isSkipped = false;
        Changed();
    }

    protected abstract object? GetValueCore();

    protected virtual string? GetCustomInputCore() => null;

    protected abstract void ClearValueCore();
}

public sealed class AskUserSingleSelectFieldViewModel : AskUserFieldViewModel
{
    private bool updatingSelection;
    private string customInput = string.Empty;

    public AskUserSingleSelectFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        Options = field.Options
            .Select(option => new AskUserOptionViewModel(
                option,
                string.Equals(option.Id, field.DefaultOptionId, StringComparison.Ordinal),
                OnOptionChanged))
            .ToArray();
    }

    public IReadOnlyList<AskUserOptionViewModel> Options { get; }

    public bool AllowsCustomInput => Field.AllowCustomInput;

    public string CustomInputPlaceholder => Field.CustomInputPlaceholder ?? "我有其他想法…";

    public int CustomInputMaxLength => Field.MaxLength ?? (Field.IsCredentialName ? 200 : 1000);

    public string CustomInput
    {
        get => customInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref customInput, value))
            {
                return;
            }

            NotifyValueChanged();
        }
    }

    public string? SelectedOptionId
    {
        get => Options.FirstOrDefault(option => option.IsSelected)?.Id;
        set
        {
            updatingSelection = true;
            try
            {
                foreach (var option in Options)
                {
                    option.IsSelected = string.Equals(option.Id, value, StringComparison.Ordinal);
                }
            }
            finally
            {
                updatingSelection = false;
            }

            OnPropertyChanged();
            NotifyValueChanged();
        }
    }

    protected override object? GetValueCore() => SelectedOptionId;

    protected override string? GetCustomInputCore() =>
        string.IsNullOrWhiteSpace(CustomInput) ? null : CustomInput;

    protected override void ClearValueCore()
    {
        ClearSelectionsWithoutNotification();
        SetCustomInputWithoutSelectionReset(string.Empty);
    }

    private void OnOptionChanged(AskUserOptionViewModel selected)
    {
        if (updatingSelection)
        {
            return;
        }

        if (selected.IsSelected)
        {
            updatingSelection = true;
            try
            {
                foreach (var option in Options)
                {
                    if (!ReferenceEquals(option, selected))
                    {
                        option.IsSelected = false;
                    }
                }
            }
            finally
            {
                updatingSelection = false;
            }
        }

        OnPropertyChanged(nameof(SelectedOptionId));
        NotifyValueChanged();
    }

    private void ClearSelectionsWithoutNotification()
    {
        updatingSelection = true;
        try
        {
            foreach (var option in Options)
            {
                option.IsSelected = false;
            }
        }
        finally
        {
            updatingSelection = false;
        }

        OnPropertyChanged(nameof(SelectedOptionId));
    }

    private void SetCustomInputWithoutSelectionReset(string value)
    {
        SetProperty(ref customInput, value, nameof(CustomInput));
    }
}

public sealed class AskUserMultiSelectFieldViewModel : AskUserFieldViewModel
{
    private bool updatingSelection;
    private string customInput = string.Empty;

    public AskUserMultiSelectFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        var defaults = field.DefaultOptionIds.ToHashSet(StringComparer.Ordinal);
        Options = field.Options
            .Select(option => new AskUserOptionViewModel(option, defaults.Contains(option.Id), OnOptionChanged))
            .ToArray();
    }

    public IReadOnlyList<AskUserOptionViewModel> Options { get; }

    public bool AllowsCustomInput => Field.AllowCustomInput;

    public string CustomInputPlaceholder => Field.CustomInputPlaceholder ?? "我有其他想法…";

    public int CustomInputMaxLength => Field.MaxLength ?? (Field.IsCredentialName ? 200 : 1000);

    public string CustomInput
    {
        get => customInput;
        set
        {
            value ??= string.Empty;
            if (!SetProperty(ref customInput, value))
            {
                return;
            }

            NotifyValueChanged();
        }
    }

    protected override object GetValueCore() =>
        Array.AsReadOnly(Options.Where(option => option.IsSelected).Select(option => option.Id).ToArray());

    protected override string? GetCustomInputCore() =>
        string.IsNullOrWhiteSpace(CustomInput) ? null : CustomInput;

    protected override void ClearValueCore()
    {
        ClearSelectionsWithoutNotification();
        SetCustomInputWithoutSelectionReset(string.Empty);
    }

    private void OnOptionChanged(AskUserOptionViewModel option)
    {
        if (updatingSelection)
        {
            return;
        }

        NotifyValueChanged();
    }

    private void ClearSelectionsWithoutNotification()
    {
        updatingSelection = true;
        try
        {
            foreach (var option in Options)
            {
                option.IsSelected = false;
            }
        }
        finally
        {
            updatingSelection = false;
        }
    }

    private void SetCustomInputWithoutSelectionReset(string value)
    {
        SetProperty(ref customInput, value, nameof(CustomInput));
    }
}

/// <summary>保留输入原文，无效数字不能回退到上一次有效值后提交。</summary>
public sealed class AskUserNumberFieldViewModel : AskUserFieldViewModel
{
    private string numberText;

    public AskUserNumberFieldViewModel(UserDecisionField field, Action changed) : base(field, changed)
    {
        numberText = field.DefaultNumber?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public string NumberText
    {
        get => numberText;
        set
        {
            if (SetProperty(ref numberText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(NumberValue));
                NotifyValueChanged();
            }
        }
    }

    public decimal? NumberValue
    {
        get => TryReadNumber(out var number) ? number : null;
        set => NumberText = value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    }

    public decimal Minimum => Field.MinNumber ?? decimal.MinValue;
    public decimal Maximum => Field.MaxNumber ?? decimal.MaxValue;
    public decimal Increment => Field.Step ?? 1;
    public string RangeHint => $"最小值：{(Field.MinNumber?.ToString() ?? "不限")}；最大值：{(Field.MaxNumber?.ToString() ?? "不限")}；步长：{(Field.Step?.ToString() ?? "不限")}";

    private bool TryReadNumber(out decimal number) => decimal.TryParse(NumberText,
        NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite,
        CultureInfo.CurrentCulture, out number);

    protected override object? GetValueCore() => string.IsNullOrWhiteSpace(NumberText)
        ? null : TryReadNumber(out var number) ? number : NumberText;

    protected override void ClearValueCore() => NumberText = string.Empty;
}

public sealed class AskUserTextFieldViewModel : AskUserFieldViewModel
{
    private string textValue;

    public AskUserTextFieldViewModel(UserDecisionField field, Action changed)
        : base(field, changed)
    {
        textValue = field.DefaultText ?? string.Empty;
    }

    public string TextValue
    {
        get => textValue;
        set
        {
            value ??= string.Empty;
            if (SetProperty(ref textValue, value))
            {
                NotifyValueChanged();
            }
        }
    }

    public bool IsMultiline => Field.IsMultiline;

    public int MaxLength => Field.MaxLength ?? (Field.IsCredentialName ? 200 : 1000);

    protected override object GetValueCore() => TextValue;

    protected override void ClearValueCore() => TextValue = string.Empty;
}

public sealed class AskUserOptionViewModel : ObservableObject
{
    private readonly Action<AskUserOptionViewModel> changed;
    private bool isSelected;

    public AskUserOptionViewModel(
        UserDecisionOption option,
        bool isSelected,
        Action<AskUserOptionViewModel> changed)
    {
        Id = option.Id;
        Label = option.Label;
        Description = option.Description;
        this.isSelected = isSelected;
        this.changed = changed;
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }

    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);

    public bool IsSelected
    {
        get => isSelected;
        set
        {
            if (SetProperty(ref isSelected, value))
            {
                changed(this);
            }
        }
    }
}
