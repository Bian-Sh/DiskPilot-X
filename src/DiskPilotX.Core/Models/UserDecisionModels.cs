using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DiskPilotX.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<UserDecisionFieldType>))]
public enum UserDecisionFieldType
{
    [JsonStringEnumMemberName("single_select")]
    SingleSelect,

    [JsonStringEnumMemberName("multi_select")]
    MultiSelect,

    [JsonStringEnumMemberName("number")]
    Number,

    [JsonStringEnumMemberName("text")]
    Text,
}

public sealed record UserDecisionOption
{
    public UserDecisionOption(string id, string label, string? description = null)
    {
        Id = id;
        Label = label;
        Description = description;
    }

    public string Id { get; }

    public string Label { get; }

    public string? Description { get; }
}

public sealed record UserDecisionField
{
    public UserDecisionField(
        string id,
        string label,
        UserDecisionFieldType type,
        bool isRequired = false,
        IReadOnlyList<UserDecisionOption>? options = null,
        string? defaultOptionId = null,
        IReadOnlyList<string>? defaultOptionIds = null,
        int? minSelections = null,
        int? maxSelections = null,
        decimal? defaultNumber = null,
        decimal? minNumber = null,
        decimal? maxNumber = null,
        decimal? step = null,
        string? defaultText = null,
        bool isMultiline = false,
        bool allowCustomInput = false,
        string? customInputPlaceholder = null,
        int? maxLength = null, bool isCredentialName = false)
    {
        Id = id;
        Label = label;
        Type = type;
        IsRequired = isRequired;
        Options = CopyItems(options ?? [], nameof(options));
        DefaultOptionId = defaultOptionId;
        DefaultOptionIds = CopyStrings(defaultOptionIds ?? [], nameof(defaultOptionIds));
        MinSelections = minSelections;
        MaxSelections = maxSelections;
        DefaultNumber = defaultNumber;
        MinNumber = minNumber;
        MaxNumber = maxNumber;
        Step = step;
        DefaultText = defaultText;
        IsMultiline = isMultiline;
        AllowCustomInput = allowCustomInput;
        CustomInputPlaceholder = customInputPlaceholder;
        MaxLength = maxLength;
        IsCredentialName = isCredentialName;
    }

    public string Id { get; }

    public string Label { get; }

    public UserDecisionFieldType Type { get; }

    public bool IsCredentialName { get; }

    public bool IsRequired { get; }

    public IReadOnlyList<UserDecisionOption> Options { get; }

    public string? DefaultOptionId { get; }

    public IReadOnlyList<string> DefaultOptionIds { get; }

    public int? MinSelections { get; }

    public int? MaxSelections { get; }

    public decimal? DefaultNumber { get; }

    public decimal? MinNumber { get; }

    public decimal? MaxNumber { get; }

    public decimal? Step { get; }

    public string? DefaultText { get; }

    public bool IsMultiline { get; }

    public bool AllowCustomInput { get; }

    public string? CustomInputPlaceholder { get; }

    public int? MaxLength { get; }

    private static ReadOnlyCollection<UserDecisionOption> CopyItems(
        IReadOnlyList<UserDecisionOption> items,
        string parameterName)
    {
        var copy = new UserDecisionOption[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            copy[index] = items[index]
                ?? throw new ArgumentException("用户决策选项不能为 null。", parameterName);
        }

        return Array.AsReadOnly(copy);
    }

    private static ReadOnlyCollection<string> CopyStrings(
        IReadOnlyList<string> items,
        string parameterName)
    {
        var copy = new string[items.Count];
        for (var index = 0; index < items.Count; index++)
        {
            copy[index] = items[index]
                ?? throw new ArgumentException("用户决策默认选项不能为 null。", parameterName);
        }

        return Array.AsReadOnly(copy);
    }
}

public sealed record UserDecisionRequest
{
    /// <summary>解析并校验结构化请求；缺少 fields 时兼容旧版 title/question 文本题。</summary>
    public static UserDecisionRequest Parse(JsonNode? args) => UserDecisionRequestParser.Parse(args);

    /// <summary>供工具注册方复用 Loom-X 的结构化输入协议。</summary>
    public static JsonNode CreateSchema() => UserDecisionRequestParser.CreateSchema();

    public UserDecisionRequest(
        string title,
        string question,
        IReadOnlyList<UserDecisionField> fields,
        string? description = null,
        string? impactSummary = null,
        bool allowCancel = true)
    {
        ArgumentNullException.ThrowIfNull(fields);
        var copy = new UserDecisionField[fields.Count];
        for (var index = 0; index < fields.Count; index++)
        {
            copy[index] = fields[index]
                ?? throw new ArgumentException("用户决策字段不能为 null。", nameof(fields));
        }

        Title = title;
        Question = question;
        Description = description;
        ImpactSummary = impactSummary;
        AllowCancel = allowCancel;
        Fields = Array.AsReadOnly(copy);
    }

    public string Title { get; }

    public string Question { get; }

    public string? Description { get; }

    public string? ImpactSummary { get; }

    public bool AllowCancel { get; }

    public IReadOnlyList<UserDecisionField> Fields { get; }
}

public sealed record UserDecisionValidationError(string FieldId, string Message)
{
    public const string RequestFieldId = "$request";
}

public sealed class UserDecisionValidationException : ArgumentException
{
    public UserDecisionValidationException(IReadOnlyList<UserDecisionValidationError> errors)
        : base("用户决策请求无效。")
    {
        ArgumentNullException.ThrowIfNull(errors);
        Errors = Array.AsReadOnly(errors.ToArray());
    }

    public IReadOnlyList<UserDecisionValidationError> Errors { get; }
}

public static class UserDecisionValidator
{
    private const int MaxTitleLength = 120;
    private const int MaxQuestionLength = 2000;
    private const int MaxDescriptionLength = 2000;
    private const int MaxImpactSummaryLength = 1000;
    private const int MaxFieldIdLength = 64;
    private const int MaxFieldLabelLength = 200;
    private const int MaxOptionIdLength = 64;
    private const int MaxOptionLabelLength = 200;
    private const int MaxOptionDescriptionLength = 500;
    private const int MaxCustomInputPlaceholderLength = 200;
    private const int DefaultTextMaxLength = 1000;
    private const int MaxTextLength = 4000;

    public static IReadOnlyList<UserDecisionValidationError> ValidateRequest(UserDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = new List<UserDecisionValidationError>();

        var namesOnly = request.Fields.Count == 1 && request.Fields[0].IsCredentialName;
        ValidateDisplayText(errors, UserDecisionValidationError.RequestFieldId, "标题", request.Title, true, MaxTitleLength, namesOnly);
        ValidateDisplayText(errors, UserDecisionValidationError.RequestFieldId, "问题", request.Question, true, MaxQuestionLength, namesOnly);
        ValidateDisplayText(errors, UserDecisionValidationError.RequestFieldId, "说明", request.Description, false, MaxDescriptionLength, namesOnly);
        ValidateDisplayText(
            errors,
            UserDecisionValidationError.RequestFieldId,
            "影响摘要",
            request.ImpactSummary,
            false,
            MaxImpactSummaryLength, namesOnly);

        if (request.Fields.Count == 0)
        {
            errors.Add(new UserDecisionValidationError(
                UserDecisionValidationError.RequestFieldId,
                "用户决策至少需要一个字段。"));
            return errors.AsReadOnly();
        }

        var fieldIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in request.Fields)
        {
            var fieldId = GetErrorFieldId(field.Id);
            if (!fieldIds.Add(field.Id ?? string.Empty))
            {
                errors.Add(new UserDecisionValidationError(fieldId, "字段 id 不能重复。"));
            }

            ValidateField(errors, field, fieldId);
        }

        return errors.AsReadOnly();
    }

    public static IReadOnlyList<UserDecisionValidationError> ValidateSubmission(
        UserDecisionRequest request,
        IReadOnlyDictionary<string, object?> values) =>
        ValidateSubmission(request, values, new Dictionary<string, string>());

    public static IReadOnlyList<UserDecisionValidationError> ValidateSubmission(
        UserDecisionRequest request,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, string> customInputs)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(customInputs);
        var errors = new List<UserDecisionValidationError>();
        var fields = request.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);

        foreach (var key in values.Keys.Concat(customInputs.Keys))
        {
            if (!fields.ContainsKey(key))
            {
                errors.Add(new UserDecisionValidationError(
                    UserDecisionValidationError.RequestFieldId,
                    "提交结果包含未知字段。"));
            }
        }

        foreach (var field in request.Fields)
        {
            values.TryGetValue(field.Id, out var value);
            customInputs.TryGetValue(field.Id, out var customInput);
            ValidateSubmittedValue(errors, field, value, customInput, customInputs.ContainsKey(field.Id));
        }

        return errors.AsReadOnly();
    }

    private static void ValidateField(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        ValidateIdentifier(errors, fieldId, "字段 id", field.Id, MaxFieldIdLength);
        ValidateDisplayText(errors, fieldId, "字段标题", field.Label, true, MaxFieldLabelLength, field.IsCredentialName);

        if (field.IsCredentialName && (field.Type is not (UserDecisionFieldType.Text or UserDecisionFieldType.SingleSelect)
            || field.IsMultiline || field.MaxLength is > 200))
            errors.Add(new UserDecisionValidationError(fieldId, "凭据名称字段仅支持最多200字的单行文本或单选。"));
        switch (field.Type)
        {
            case UserDecisionFieldType.SingleSelect:
                ValidateSingleSelectField(errors, field, fieldId);
                break;
            case UserDecisionFieldType.MultiSelect:
                ValidateMultiSelectField(errors, field, fieldId);
                break;
            case UserDecisionFieldType.Number:
                ValidateNumberField(errors, field, fieldId);
                break;
            case UserDecisionFieldType.Text:
                ValidateTextField(errors, field, fieldId);
                break;
            default:
                errors.Add(new UserDecisionValidationError(fieldId, "字段类型无效。"));
                break;
        }
    }

    private static void ValidateSingleSelectField(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        ValidateSingleSelectProperties(errors, field, fieldId);
        var optionIds = ValidateOptions(errors, field, fieldId);
        if (field.DefaultOptionId is not null && !optionIds.Contains(field.DefaultOptionId))
        {
            errors.Add(new UserDecisionValidationError(fieldId, "单选字段的默认选项不存在。"));
        }
    }

    private static void ValidateMultiSelectField(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        ValidateMultiSelectProperties(errors, field, fieldId);
        var optionIds = ValidateOptions(errors, field, fieldId);
        var minimum = field.MinSelections ?? 0;
        var maximum = field.MaxSelections ?? field.Options.Count;

        if (minimum < 0)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "最少选择数不能小于零。"));
        }
        else if (maximum < minimum)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "最少选择数不能大于最多选择数。"));
        }
        else if (maximum > field.Options.Count)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "最多选择数不能超过选项数量。"));
        }

        var defaultIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var defaultId in field.DefaultOptionIds)
        {
            if (!defaultIds.Add(defaultId))
            {
                errors.Add(new UserDecisionValidationError(fieldId, "多选字段的默认选项不能重复。"));
                continue;
            }

            if (!optionIds.Contains(defaultId))
            {
                errors.Add(new UserDecisionValidationError(fieldId, "多选字段的默认选项不存在。"));
            }
        }

        if (field.DefaultOptionIds.Count > 0
            && (field.DefaultOptionIds.Count < minimum || field.DefaultOptionIds.Count > maximum))
        {
            errors.Add(new UserDecisionValidationError(fieldId, "多选字段的默认值不符合选择数量限制。"));
        }
    }

    private static void ValidateSingleSelectProperties(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        if (field.DefaultOptionIds.Count > 0
            || field.MinSelections is not null
            || field.MaxSelections is not null
            || HasNumberProperties(field)
            || HasTextOnlyProperties(field))
        {
            AddInapplicablePropertyError(errors, fieldId);
        }

        ValidateCustomInputProperties(errors, field, fieldId);
    }

    private static void ValidateMultiSelectProperties(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        if (field.DefaultOptionId is not null
            || HasNumberProperties(field)
            || HasTextOnlyProperties(field))
        {
            AddInapplicablePropertyError(errors, fieldId);
        }

        ValidateCustomInputProperties(errors, field, fieldId);
    }

    private static void ValidateNumberProperties(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        if (HasSelectionProperties(field) || HasTextProperties(field) || HasCustomInputProperties(field))
        {
            AddInapplicablePropertyError(errors, fieldId);
        }
    }

    private static void ValidateTextProperties(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        if (HasSelectionProperties(field) || HasNumberProperties(field) || HasCustomInputProperties(field))
        {
            AddInapplicablePropertyError(errors, fieldId);
        }
    }

    private static bool HasSelectionProperties(UserDecisionField field) =>
        field.Options.Count > 0
        || field.DefaultOptionId is not null
        || field.DefaultOptionIds.Count > 0
        || field.MinSelections is not null
        || field.MaxSelections is not null;

    private static bool HasNumberProperties(UserDecisionField field) =>
        field.DefaultNumber is not null
        || field.MinNumber is not null
        || field.MaxNumber is not null
        || field.Step is not null;

    private static bool HasTextOnlyProperties(UserDecisionField field) =>
        field.DefaultText is not null
        || field.IsMultiline;

    private static bool HasTextProperties(UserDecisionField field) =>
        HasTextOnlyProperties(field)
        || field.MaxLength is not null;

    private static bool HasCustomInputProperties(UserDecisionField field) =>
        field.AllowCustomInput
        || field.CustomInputPlaceholder is not null;

    private static void ValidateCustomInputProperties(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        if (!field.AllowCustomInput)
        {
            if (field.CustomInputPlaceholder is not null || field.MaxLength is not null)
            {
                AddInapplicablePropertyError(errors, fieldId);
            }

            return;
        }

        ValidateDisplayText(
            errors,
            fieldId,
            "自由输入提示",
            field.CustomInputPlaceholder,
            field.CustomInputPlaceholder is not null,
            MaxCustomInputPlaceholderLength, field.IsCredentialName);

        var maxLength = field.MaxLength ?? (field.IsCredentialName ? 200 : DefaultTextMaxLength);
        if (maxLength <= 0 || maxLength > MaxTextLength)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "自由输入最大长度必须处于允许范围内。"));
        }
    }

    private static void AddInapplicablePropertyError(
        ICollection<UserDecisionValidationError> errors,
        string fieldId) =>
        errors.Add(new UserDecisionValidationError(fieldId, "字段包含当前类型不适用的属性。"));

    private static HashSet<string> ValidateOptions(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        var optionIds = new HashSet<string>(StringComparer.Ordinal);
        if (field.Options.Count == 0)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "选择字段至少需要一个选项。"));
            return optionIds;
        }

        foreach (var option in field.Options)
        {
            ValidateIdentifier(errors, fieldId, "选项 id", option.Id, MaxOptionIdLength);
            ValidateDisplayText(errors, fieldId, "选项标签", option.Label, true, MaxOptionLabelLength, field.IsCredentialName);
            ValidateDisplayText(
                errors,
                fieldId,
                "选项说明",
                option.Description,
                false,
                MaxOptionDescriptionLength, field.IsCredentialName);

            if (!optionIds.Add(option.Id ?? string.Empty))
            {
                errors.Add(new UserDecisionValidationError(fieldId, "选项 id 不能重复。"));
            }
        }

        return optionIds;
    }

    private static void ValidateNumberField(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        ValidateNumberProperties(errors, field, fieldId);
        if (field.MinNumber is not null
            && field.MaxNumber is not null
            && field.MinNumber > field.MaxNumber)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "数值字段的最小值不能大于最大值。"));
            return;
        }

        if (field.Step is not null && field.Step <= 0)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "数值字段的步长必须大于零。"));
            return;
        }

        if (field.DefaultNumber is not null)
        {
            ValidateNumber(errors, field, field.DefaultNumber.Value, "默认值");
        }
    }

    private static void ValidateTextField(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string fieldId)
    {
        ValidateTextProperties(errors, field, fieldId);
        var maxLength = field.MaxLength ?? (field.IsCredentialName ? 200 : DefaultTextMaxLength);
        if (maxLength <= 0 || maxLength > MaxTextLength)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "文本最大长度必须处于允许范围内。"));
            return;
        }

        if (field.DefaultText is null)
        {
            return;
        }

        if (field.IsRequired && string.IsNullOrWhiteSpace(field.DefaultText))
        {
            errors.Add(new UserDecisionValidationError(fieldId, "必填文本的默认值不能为空。"));
        }
        else if (field.DefaultText.Length > maxLength)
        {
            errors.Add(new UserDecisionValidationError(fieldId, "文本默认值超过最大长度。"));
        }
        else if (ContainsBlockedFieldContent(field, field.DefaultText))
        {
            errors.Add(new UserDecisionValidationError(fieldId, "文本默认值包含敏感字段模式。"));
        }
    }

    private static void ValidateSubmittedValue(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        object? value,
        string? customInput,
        bool hasCustomInputEntry)
    {
        if (hasCustomInputEntry)
        {
            ValidateSubmittedCustomInput(errors, field, customInput);
            ValidateSubmittedSelectionWithCustomInput(errors, field, value);
            return;
        }

        if (value is null)
        {
            if (field.IsRequired)
            {
                errors.Add(new UserDecisionValidationError(field.Id, "必填字段不能为空。"));
            }

            return;
        }

        switch (field.Type)
        {
            case UserDecisionFieldType.SingleSelect:
                ValidateSubmittedSingle(errors, field, value);
                break;
            case UserDecisionFieldType.MultiSelect:
                ValidateSubmittedMulti(errors, field, value);
                break;
            case UserDecisionFieldType.Number:
                ValidateSubmittedNumber(errors, field, value);
                break;
            case UserDecisionFieldType.Text:
                ValidateSubmittedText(errors, field, value);
                break;
            default:
                errors.Add(new UserDecisionValidationError(field.Id, "字段类型无效。"));
                break;
        }
    }

    private static void ValidateSubmittedCustomInput(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        string? customInput)
    {
        if (!field.AllowCustomInput
            || field.Type is not (UserDecisionFieldType.SingleSelect or UserDecisionFieldType.MultiSelect))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "该字段不允许自由输入。"));
            return;
        }

        if (string.IsNullOrWhiteSpace(customInput))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "自由输入不能为空白。"));
            return;
        }

        var maxLength = field.MaxLength ?? (field.IsCredentialName ? 200 : DefaultTextMaxLength);
        if (customInput.Length > maxLength)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "自由输入长度超过最大限制。"));
            return;
        }

        if (ContainsBlockedFieldContent(field, customInput))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "自由输入包含敏感信息。"));
            return;
        }
    }

    private static void ValidateSubmittedSelectionWithCustomInput(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        object? value)
    {
        switch (field.Type)
        {
            case UserDecisionFieldType.SingleSelect when value is not null:
                ValidateSubmittedSingle(errors, field, value);
                break;
            case UserDecisionFieldType.MultiSelect when value is not null:
                ValidateSubmittedMulti(errors, field, value, enforceMinimum: false);
                break;
        }
    }

    private static void ValidateSubmittedSingle(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        object value)
    {
        if (value is not string selectedId)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "单选字段值类型无效。"));
            return;
        }

        if (!field.Options.Any(option => string.Equals(option.Id, selectedId, StringComparison.Ordinal)))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "单选字段选择了未知选项。"));
        }
    }

    private static void ValidateSubmittedMulti(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        object value,
        bool enforceMinimum = true)
    {
        if (value is not IEnumerable<string> selectedValues || value is string)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "多选字段值类型无效。"));
            return;
        }

        var selected = selectedValues.ToArray();
        if (selected.Any(item => item is null))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "多选字段不能包含空选项。"));
            return;
        }

        if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Length)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "多选字段不能重复选择同一选项。"));
            return;
        }

        var optionIds = field.Options.Select(option => option.Id).ToHashSet(StringComparer.Ordinal);
        if (selected.Any(item => !optionIds.Contains(item)))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "多选字段选择了未知选项。"));
            return;
        }

        var minimum = Math.Max(field.MinSelections ?? 0, field.IsRequired ? 1 : 0);
        var maximum = field.MaxSelections ?? field.Options.Count;
        if ((enforceMinimum && selected.Length < minimum) || selected.Length > maximum)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "多选字段的选择数量无效。"));
        }
    }

    private static void ValidateSubmittedNumber(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        object value)
    {
        if (!TryConvertNumber(value, out var number))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "数值字段值类型无效。"));
            return;
        }

        ValidateNumber(errors, field, number, "提交值");
    }

    private static void ValidateSubmittedText(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        object value)
    {
        if (value is not string text)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "文本字段值类型无效。"));
            return;
        }

        if (field.IsRequired && string.IsNullOrWhiteSpace(text))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "必填文本不能为空。"));
            return;
        }

        var maxLength = field.MaxLength ?? (field.IsCredentialName ? 200 : DefaultTextMaxLength);
        if (text.Length > maxLength)
        {
            errors.Add(new UserDecisionValidationError(field.Id, "文本长度超过最大限制。"));
        }
        else if (ContainsBlockedFieldContent(field, text))
        {
            errors.Add(new UserDecisionValidationError(field.Id, "文本内容包含敏感信息。"));
        }
    }

    private static void ValidateNumber(
        ICollection<UserDecisionValidationError> errors,
        UserDecisionField field,
        decimal value,
        string valueName)
    {
        if (field.MinNumber is not null && value < field.MinNumber)
        {
            errors.Add(new UserDecisionValidationError(field.Id, $"数值{valueName}小于最小值。"));
            return;
        }

        if (field.MaxNumber is not null && value > field.MaxNumber)
        {
            errors.Add(new UserDecisionValidationError(field.Id, $"数值{valueName}大于最大值。"));
            return;
        }

        if (field.Step is not null)
        {
            var origin = field.MinNumber ?? 0;
            if ((value - origin) % field.Step.Value != 0)
            {
                errors.Add(new UserDecisionValidationError(field.Id, $"数值{valueName}不符合步长限制。"));
            }
        }
    }

    private static void ValidateIdentifier(
        ICollection<UserDecisionValidationError> errors,
        string fieldId,
        string name,
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add(new UserDecisionValidationError(fieldId, $"{name}不能为空。"));
        }
        else if (value.Length > maxLength)
        {
            errors.Add(new UserDecisionValidationError(fieldId, $"{name}长度超过限制。"));
        }
        else if (UserDecisionContentPolicy.IsSensitivePath([value]) || UserDecisionContentPolicy.ContainsCredentialValue(value))
        {
            errors.Add(new UserDecisionValidationError(fieldId, $"{name}包含敏感字段模式。"));
        }
    }

    private static void ValidateDisplayText(
        ICollection<UserDecisionValidationError> errors,
        string fieldId,
        string name,
        string? value,
        bool required,
        int maxLength, bool credentialName = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required)
            {
                errors.Add(new UserDecisionValidationError(fieldId, $"{name}不能为空。"));
            }

            return;
        }

        if (value.Length > maxLength)
        {
            errors.Add(new UserDecisionValidationError(fieldId, $"{name}长度超过限制。"));
        }
        else if (credentialName ? UserDecisionContentPolicy.ContainsCredentialValue(value) : UserDecisionContentPolicy.ContainsSensitiveContent(value))
        {
            errors.Add(new UserDecisionValidationError(fieldId, $"{name}包含敏感字段模式。"));
        }
    }

    private static bool ContainsBlockedFieldContent(UserDecisionField field, string value) => field.IsCredentialName
        ? value.Contains('\r') || value.Contains('\n') || UserDecisionContentPolicy.ContainsCredentialValue(value)
        : UserDecisionContentPolicy.ContainsSensitiveContent(value);
    private static bool TryConvertNumber(object value, out decimal number)
    {
        try
        {
            switch (value)
            {
                case decimal decimalValue:
                    number = decimalValue;
                    return true;
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    number = Convert.ToDecimal(value);
                    return true;
                case float floatValue when float.IsFinite(floatValue):
                    number = Convert.ToDecimal(floatValue);
                    return true;
                case double doubleValue when double.IsFinite(doubleValue):
                    number = Convert.ToDecimal(doubleValue);
                    return true;
                default:
                    number = default;
                    return false;
            }
        }
        catch (OverflowException)
        {
            number = default;
            return false;
        }
    }

    private static string GetErrorFieldId(string? fieldId) =>
        string.IsNullOrWhiteSpace(fieldId) ? "$field" : fieldId;
}

public sealed record UserDecisionResult
{
    private readonly ReadOnlyDictionary<string, object?> values;
    private readonly ReadOnlyDictionary<string, string> customInputs;

    private UserDecisionResult(
        bool cancelled,
        string? cancellationReason,
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, string> customInputs)
    {
        Cancelled = cancelled;
        CancellationReason = cancellationReason;
        this.values = CopyValues(values);
        this.customInputs = CopyCustomInputs(customInputs);
    }

    /// <summary>与 Loom-X 一致的返回结构；取消不携带未提交答案。</summary>
    public JsonNode ToJson() => new JsonObject
    {
        ["cancelled"] = Cancelled,
        ["values"] = JsonSerializer.SerializeToNode(Values),
        ["custom_inputs"] = JsonSerializer.SerializeToNode(CustomInputs),
    };

    public bool Cancelled { get; }

    public string? CancellationReason { get; }

    public IReadOnlyDictionary<string, object?> Values => values;

    public IReadOnlyDictionary<string, string> CustomInputs => customInputs;

    public static UserDecisionResult Submit(
        IReadOnlyDictionary<string, object?> values,
        IReadOnlyDictionary<string, string>? customInputs = null)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new UserDecisionResult(false, null, values, customInputs ?? new Dictionary<string, string>());
    }

    public static UserDecisionResult Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("取消原因不能为空。", nameof(reason));
        }

        return new UserDecisionResult(
            true,
            reason,
            new Dictionary<string, object?>(),
            new Dictionary<string, string>());
    }

    private static ReadOnlyDictionary<string, object?> CopyValues(
        IReadOnlyDictionary<string, object?> values)
    {
        var copy = new Dictionary<string, object?>(values.Count, StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("用户决策结果字段 id 不能为空。", nameof(values));
            }

            copy.Add(pair.Key, CopyValue(pair.Value));
        }

        return new ReadOnlyDictionary<string, object?>(copy);
    }

    private static ReadOnlyDictionary<string, string> CopyCustomInputs(
        IReadOnlyDictionary<string, string> customInputs)
    {
        var copy = new Dictionary<string, string>(customInputs.Count, StringComparer.Ordinal);
        foreach (var pair in customInputs)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                throw new ArgumentException("用户决策自由输入字段 id 不能为空。", nameof(customInputs));
            }

            copy.Add(pair.Key, pair.Value
                ?? throw new ArgumentException("用户决策自由输入不能为 null。", nameof(customInputs)));
        }

        return new ReadOnlyDictionary<string, string>(copy);
    }

    private static object? CopyValue(object? value) => value switch
    {
        IReadOnlyList<string> items => Array.AsReadOnly(items.ToArray()),
        IEnumerable<string> items when value is not string => Array.AsReadOnly(items.ToArray()),
        _ => value,
    };
}
public sealed record PendingUserDecision(string RequestId, string OwnerId, UserDecisionRequest Request);
