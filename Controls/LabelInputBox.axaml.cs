using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using OloopApp.Core.Filters;

namespace OloopApp.Controls;

/// <summary>
/// The label input of the task dialogs: a text box for comma separated
/// labels that suggests the labels that already exist while the user types.
///
/// The suggestions fit the label at the cursor (the text between two
/// commas). Up and Down choose, Tab or Enter take the suggestion and put a
/// comma behind it, Escape closes the list. Nothing is forced: a label that
/// is not in the list is written as it is typed.
/// </summary>
public partial class LabelInputBox : UserControl
{
    /// <summary>Defines the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<LabelInputBox, string?>(
            nameof(Text), "", defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Defines the <see cref="KnownLabels"/> property.</summary>
    public static readonly StyledProperty<IReadOnlyList<LabelEntry>?> KnownLabelsProperty =
        AvaloniaProperty.Register<LabelInputBox, IReadOnlyList<LabelEntry>?>(nameof(KnownLabels));

    /// <summary>Defines the <see cref="PlaceholderText"/> property.</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<LabelInputBox, string?>(
            nameof(PlaceholderText), "comma separated, e.g. ui, backend");

    private IReadOnlyList<LabelEntry> _suggestions = [];

    /// <summary>Creates the label input.</summary>
    public LabelInputBox()
    {
        InitializeComponent();

        Input.PlaceholderText = PlaceholderText;
        SuggestionPopup.PlacementTarget = Input;
        // The text is followed through the property, not through the
        // TextChanged event: the event comes later, the property changes at
        // once, so the list and the bound text are never a step behind.
        Input.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                if (!string.Equals(Text ?? "", Input.Text ?? "", StringComparison.Ordinal))
                    SetCurrentValue(TextProperty, Input.Text);

                Refresh();
            }
            else if (e.Property == TextBox.CaretIndexProperty)
            {
                Refresh();
            }
        };
        Input.LostFocus += (_, _) => Close();
        Input.AddHandler(KeyDownEvent, OnInputKeyDown, RoutingStrategies.Tunnel);
        SuggestionList.Tapped += OnListTapped;
    }

    /// <summary>The labels as comma separated text.</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>The labels that already exist and are offered while typing.</summary>
    public IReadOnlyList<LabelEntry>? KnownLabels
    {
        get => GetValue(KnownLabelsProperty);
        set => SetValue(KnownLabelsProperty, value);
    }

    /// <summary>The hint of the empty text box.</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>The text box the user types in.</summary>
    public TextBox InputBox => Input;

    /// <summary>The suggestions that are shown, empty when the list is closed.</summary>
    public IReadOnlyList<LabelEntry> Suggestions => SuggestionPopup.IsOpen ? _suggestions : [];

    /// <summary>Whether the suggestion list is open.</summary>
    public bool IsSuggestionListOpen => SuggestionPopup.IsOpen;

    /// <summary>The index of the chosen suggestion, -1 when the list is closed.</summary>
    public int SelectedSuggestionIndex => SuggestionPopup.IsOpen ? SuggestionList.SelectedIndex : -1;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == TextProperty)
        {
            var text = change.GetNewValue<string?>() ?? "";
            if (!string.Equals(Input.Text ?? "", text, StringComparison.Ordinal))
                Input.Text = text;
        }
        else if (change.Property == PlaceholderTextProperty)
        {
            Input.PlaceholderText = change.GetNewValue<string?>();
        }
        else if (change.Property == KnownLabelsProperty)
        {
            Refresh();
        }
    }

    /// <summary>Takes the chosen suggestion over into the text.</summary>
    public bool AcceptSelected()
    {
        if (!SuggestionPopup.IsOpen)
            return false;

        var index = SuggestionList.SelectedIndex;
        if (index < 0 || index >= _suggestions.Count)
            return false;

        var (text, caret) = LabelSuggester.Accept(Input.Text, Input.CaretIndex, _suggestions[index].Name);
        Close();

        Input.Text = text;
        Input.CaretIndex = caret;
        return true;
    }

    private void Refresh()
    {
        _suggestions = Input.IsFocused
            ? LabelSuggester.Suggest(KnownLabels, Input.Text, Input.CaretIndex)
            : [];

        if (_suggestions.Count == 0)
        {
            Close();
            return;
        }

        SuggestionList.ItemsSource = _suggestions;
        SuggestionList.SelectedIndex = 0;
        SuggestionBorder.MinWidth = Math.Max(Input.Bounds.Width, 200);
        SuggestionPopup.IsOpen = true;
    }

    private void Close()
    {
        SuggestionPopup.IsOpen = false;
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (HandleKey(e.Key, e.KeyModifiers))
            e.Handled = true;
    }

    /// <summary>
    /// What the list does with a key. Returns true when the list took it:
    /// Up and Down choose, Tab and Enter take the chosen suggestion, Escape
    /// closes the list. Without an open list nothing is taken, so Enter and
    /// Escape still reach the dialog.
    /// </summary>
    public bool HandleKey(Key key, KeyModifiers modifiers)
    {
        if (!SuggestionPopup.IsOpen || _suggestions.Count == 0)
            return false;

        switch (key)
        {
            case Key.Down:
                SuggestionList.SelectedIndex = (SuggestionList.SelectedIndex + 1) % _suggestions.Count;
                return true;

            case Key.Up:
                SuggestionList.SelectedIndex =
                    (SuggestionList.SelectedIndex - 1 + _suggestions.Count) % _suggestions.Count;
                return true;

            case Key.Tab when modifiers == KeyModifiers.None:
            case Key.Enter:
                return AcceptSelected();

            case Key.Escape:
                // The list owns Escape while it is open: the dialog stays.
                Close();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Takes the given suggestion over into the text — what a click on it
    /// does. Returns false when it is not one of the suggestions shown.
    /// </summary>
    public bool Choose(LabelEntry entry)
    {
        var index = _suggestions.ToList().IndexOf(entry);
        if (!SuggestionPopup.IsOpen || index < 0)
            return false;

        SuggestionList.SelectedIndex = index;
        return AcceptSelected();
    }

    private void OnListTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Control { DataContext: LabelEntry entry } && Choose(entry))
            Input.Focus();
    }
}
