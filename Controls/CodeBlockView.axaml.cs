using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace OloopApp.Controls;

/// <summary>
/// A code block of a Markdown document: the code in a monospace box on a dark
/// background, with a button that copies the code to the clipboard.
/// </summary>
public partial class CodeBlockView : UserControl
{
    /// <summary>Defines the <see cref="Code"/> property.</summary>
    public static readonly StyledProperty<string> CodeProperty =
        AvaloniaProperty.Register<CodeBlockView, string>(nameof(Code), string.Empty);

    /// <summary>Defines the <see cref="Language"/> property.</summary>
    public static readonly StyledProperty<string?> LanguageProperty =
        AvaloniaProperty.Register<CodeBlockView, string?>(nameof(Language));

    private bool _initialized;

    /// <summary>Creates the code block view.</summary>
    public CodeBlockView()
    {
        InitializeComponent();
        _initialized = true;
        Update();
    }

    /// <summary>The code shown in the block.</summary>
    public string Code
    {
        get => GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>The language of the code, taken from the fence of the block.</summary>
    public string? Language
    {
        get => GetValue(LanguageProperty);
        set => SetValue(LanguageProperty, value);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_initialized && (change.Property == CodeProperty || change.Property == LanguageProperty))
            Update();
    }

    private void Update()
    {
        CodeText.Text = Code;

        var language = Language;
        LanguageText.Text = language ?? "";
        LanguageText.IsVisible = !string.IsNullOrWhiteSpace(language);
    }

    private void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        var top = TopLevel.GetTopLevel(this);
        _ = top?.Clipboard?.SetTextAsync(Code);
    }
}
