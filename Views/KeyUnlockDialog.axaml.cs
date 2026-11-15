using Avalonia.Controls;
using Avalonia.Interactivity;
using OloopApp.Core.Server;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>
/// Opens the key bundle on a device: passphrase, or recovery code. The result is the unlocked
/// user, or null when the user cancelled.
/// </summary>
public partial class KeyUnlockDialog : Window
{
    public KeyUnlockDialog(KeyUnlockViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public KeyUnlockDialog()
    {
        InitializeComponent();
    }

    public static async Task<UnlockedUser?> ShowAsync(Window owner, KeyUnlockViewModel viewModel)
        => await new KeyUnlockDialog(viewModel).ShowDialog<UnlockedUser?>(owner);

    private async void OnUnlockClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is KeyUnlockViewModel vm && await vm.UnlockAsync())
            Close(vm.Result);
    }

    private void OnSwitchClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not KeyUnlockViewModel vm)
            return;

        vm.SwitchKind();
        InputLabel.Text = vm.UsesRecoveryCode ? "Recovery code" : "Passphrase";
        SwitchButton.Content = vm.UsesRecoveryCode ? "Use the passphrase" : "Use the recovery code";
        InputBox.PasswordChar = vm.UsesRecoveryCode ? '\0' : '●';
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(null);
}
