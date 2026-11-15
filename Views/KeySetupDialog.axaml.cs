using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using OloopApp.Core.Server;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// The setup of the keys on the first device: passphrase, then the recovery code. The result is
/// the unlocked user, or null when the user cancelled.
/// </summary>
public partial class KeySetupDialog : Window
{
    public KeySetupDialog(KeySetupViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public KeySetupDialog()
    {
        InitializeComponent();
    }

    public static async Task<UnlockedUser?> ShowAsync(Window owner, KeySetupViewModel viewModel)
        => await new KeySetupDialog(viewModel).ShowDialog<UnlockedUser?>(owner);

    private async void OnContinueClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeySetupViewModel vm)
            await vm.ContinueAsync();
    }

    private async void OnFinishClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeySetupViewModel vm && await vm.FinishAsync())
            Close(vm.Result);
    }

    private void OnBackClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeySetupViewModel vm)
            vm.GoBack();
    }

    private async void OnCopyClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeySetupViewModel vm && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(vm.RecoveryCodeText);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
