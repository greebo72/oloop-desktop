using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using OloopApp.ViewModels;

namespace OloopApp.Views;

/// <summary>The sign-in of this device. The result is true when the device is signed in, false when the user cancelled.</summary>
public partial class LoginDialog : Window
{
    public LoginDialog(LoginViewModel viewModel)
    {
        InitializeComponent();
        UiScale.Attach(this);
        DataContext = viewModel;
        Opened += (_, _) => PasswordBox.Focus();
    }

    /// <summary>The constructor the XAML runtime loader needs; the dialog is always made with a view model.</summary>
    public LoginDialog()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(Window owner, LoginViewModel viewModel)
        => await new LoginDialog(viewModel).ShowDialog<bool>(owner);

    protected override async void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter && !e.Handled)
        {
            e.Handled = true;
            await SignInAsync();
        }
    }

    private async void OnSignInClick(object? sender, RoutedEventArgs e) => await SignInAsync();

    private async Task SignInAsync()
    {
        if (DataContext is LoginViewModel vm && await vm.SignInAsync())
            Close(true);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);
}
