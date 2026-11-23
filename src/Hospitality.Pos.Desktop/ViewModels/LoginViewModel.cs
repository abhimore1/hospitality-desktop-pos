using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private readonly StaffRepository _staffRepository;
    private readonly AppSession _session;
    private string _pin = "";

    public LoginViewModel(StaffRepository staffRepository, AppSession session)
    {
        _staffRepository = staffRepository;
        _session = session;
        Staff = new ObservableCollection<Staff>(_staffRepository.GetAll());
        SelectedStaff = Staff.FirstOrDefault();
        UpdatePinDisplay();
    }

    public ObservableCollection<Staff> Staff { get; }
    public event Action? LoginSucceeded;

    [ObservableProperty] private Staff? _selectedStaff;
    [ObservableProperty] private string _pinDisplay = "";
    [ObservableProperty] private string _errorMessage = "";
    [ObservableProperty] private bool _hasError;

    public string HintText => "Demo build — every staff PIN is 1234";

    partial void OnSelectedStaffChanged(Staff? value)
    {
        _pin = "";
        ClearError();
        UpdatePinDisplay();
    }

    [RelayCommand]
    private void SelectStaff(Staff staff)
    {
        SelectedStaff = staff;
    }

    [RelayCommand]
    private void Digit(string digit)
    {
        if (_pin.Length >= 4)
        {
            return;
        }

        _pin += digit;
        ClearError();
        UpdatePinDisplay();

        if (_pin.Length == 4)
        {
            AttemptLogin();
        }
    }

    [RelayCommand]
    private void Backspace()
    {
        if (_pin.Length > 0)
        {
            _pin = _pin[..^1];
        }

        ClearError();
        UpdatePinDisplay();
    }

    [RelayCommand]
    private void Clear()
    {
        _pin = "";
        ClearError();
        UpdatePinDisplay();
    }

    [RelayCommand]
    private void Login()
    {
        if (_pin.Length < 4)
        {
            ShowError("Enter the 4-digit PIN.");
            return;
        }

        AttemptLogin();
    }

    private void AttemptLogin()
    {
        if (SelectedStaff is null)
        {
            ShowError("Select a staff member first.");
            return;
        }

        var staff = _staffRepository.ValidatePin(SelectedStaff.Id, _pin);
        if (staff is null)
        {
            _pin = "";
            UpdatePinDisplay();
            ShowError("That PIN did not match. Try again.");
            return;
        }

        _session.SignIn(staff);
        LoginSucceeded?.Invoke();
    }

    private void ShowError(string message)
    {
        ErrorMessage = message;
        HasError = true;
    }

    private void ClearError()
    {
        ErrorMessage = "";
        HasError = false;
    }

    private void UpdatePinDisplay()
    {
        var filled = new string('●', _pin.Length);
        var empty = new string('○', 4 - _pin.Length);
        PinDisplay = string.Join("  ", (filled + empty).Select(character => character.ToString()));
    }
}
