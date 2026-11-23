using Hospitality.Pos.Desktop.Models;

namespace Hospitality.Pos.Desktop.Services;

public enum ToastKind
{
    Success,
    Info,
    Warning,
    Error
}

/// <summary>Lightweight in-app notifications rendered by the shell.</summary>
public sealed class ToastService
{
    public event Action<string, ToastKind>? Requested;

    public void Success(string message) => Requested?.Invoke(message, ToastKind.Success);
    public void Info(string message) => Requested?.Invoke(message, ToastKind.Info);
    public void Warning(string message) => Requested?.Invoke(message, ToastKind.Warning);
    public void Error(string message) => Requested?.Invoke(message, ToastKind.Error);
}

/// <summary>Signals that locally stored data changed and screens should refresh.</summary>
public sealed class DataChangeNotifier
{
    public event Action? Changed;

    public void Notify() => Changed?.Invoke();
}

/// <summary>Decoupled navigation requested from view models.</summary>
public sealed class AppNavigation
{
    public event Action<string, object?>? Requested;

    public void Navigate(string destination, object? argument = null) =>
        Requested?.Invoke(destination, argument);
}

public sealed class AppSession
{
    public Staff? CurrentStaff { get; private set; }
    public string TerminalName { get; set; } = "Counter 01";

    public event Action? StaffChanged;

    public void SignIn(Staff staff)
    {
        CurrentStaff = staff;
        StaffChanged?.Invoke();
    }
}
