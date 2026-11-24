using System.Windows;
using System.Windows.Input;
using Hospitality.Pos.Desktop.ViewModels;

namespace Hospitality.Pos.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    private void OnLoginSucceeded()
    {
        DialogResult = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogResult = false;
            return;
        }

        if (Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Alt)
        {
            return;
        }

        var digit = e.Key switch
        {
            Key.D0 or Key.NumPad0 => "0",
            Key.D1 or Key.NumPad1 => "1",
            Key.D2 or Key.NumPad2 => "2",
            Key.D3 or Key.NumPad3 => "3",
            Key.D4 or Key.NumPad4 => "4",
            Key.D5 or Key.NumPad5 => "5",
            Key.D6 or Key.NumPad6 => "6",
            Key.D7 or Key.NumPad7 => "7",
            Key.D8 or Key.NumPad8 => "8",
            Key.D9 or Key.NumPad9 => "9",
            _ => null
        };

        if (digit is not null)
        {
            _viewModel.DigitCommand.Execute(digit);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            _viewModel.BackspaceCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
