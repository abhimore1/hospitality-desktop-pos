using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class LoginViewModel : ObservableObject
{
    private const int MaxAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromSeconds(60);

    private readonly StaffRepository _staffRepository;
    private readonly AppSession _session;
    private string _pin = "";
    private int _failedAttempts;
    private DateTime? _lockoutUntil;
    private DispatcherTimer? _lockoutTimer;

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
    [ObservableProperty] private bool _isKeypadEnabled = true;

    public string HintText =>
#if DEBUG
        "Demo build — every staff PIN is 1234";
#else
        "Enter your 4-digit counter PIN";
#endif

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
        if (!IsKeypadEnabled || _pin.Length >= 4)
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
        if (!IsKeypadEnabled)
        {
            return;
        }

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
            _failedAttempts++;

            if (_failedAttempts >= MaxAttempts)
            {
                StartLockout();
                return;
            }

            var remaining = MaxAttempts - _failedAttempts;
            ShowError($"That PIN did not match. {remaining} attempt{(remaining == 1 ? "" : "s")} left.");
            return;
        }

        _failedAttempts = 0;
        _session.SignIn(staff);
        LoginSucceeded?.Invoke();
    }

    private void StartLockout()
    {
        _lockoutUntil = DateTime.UtcNow.Add(LockoutDuration);
        IsKeypadEnabled = false;
        _pin = "";
        UpdatePinDisplay();
        ShowError($"Too many attempts. Try again in {LockoutDuration.TotalSeconds:0}s.");

        _lockoutTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _lockoutTimer.Tick += (_, _) => UpdateLockout();
        _lockoutTimer.Start();
    }

    private void UpdateLockout()
    {
        if (_lockoutUntil is null)
        {
            return;
        }

        var remaining = _lockoutUntil.Value - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            _lockoutTimer?.Stop();
            _lockoutTimer = null;
            _lockoutUntil = null;
            _failedAttempts = 0;
            IsKeypadEnabled = true;
            ClearError();
            return;
        }

        ShowError($"Too many attempts. Try again in {Math.Ceiling(remaining.TotalSeconds):0}s.");
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
