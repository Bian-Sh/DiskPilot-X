using System.Windows;
using System.Windows.Controls;

namespace DiskPilotX.App.Controls;

/// <summary>
/// A control that renders a fenced code block with a language label and Copy button.
/// </summary>
public partial class CodeBlockControl : UserControl
{
    public static readonly DependencyProperty CodeTextProperty =
        DependencyProperty.Register(
            nameof(CodeText),
            typeof(string),
            typeof(CodeBlockControl),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CodeLanguageProperty =
        DependencyProperty.Register(
            nameof(Language),
            typeof(string),
            typeof(CodeBlockControl),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Gets or sets the code content to display.
    /// </summary>
    public string CodeText
    {
        get => (string)GetValue(CodeTextProperty);
        set => SetValue(CodeTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the optional language label (e.g., "powershell", "bash").
    /// </summary>
    public new string Language
    {
        get => (string)GetValue(CodeLanguageProperty);
        set => SetValue(CodeLanguageProperty, value);
    }

    public CodeBlockControl()
    {
        InitializeComponent();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(CodeText))
        {
            Clipboard.SetText(CodeText);
        }
    }
}
