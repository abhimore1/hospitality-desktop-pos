using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using Hospitality.Pos.Desktop.Services.Sync;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class SettingsViewModel : ObservableObject, IRefreshable
{
    private readonly SettingsRepository _settings;
    private readonly AppSession _session;
    private readonly DataChangeNotifier _notifier;
    private readonly ToastService _toasts;
    private readonly PosDatabase _database;
    private readonly OutboxRepository _outbox;
    private readonly ISyncTransport _syncTransport;
    private PosSettings _config = new();

    public SettingsViewModel(
        SettingsRepository settings,
        AppSession session,
        DataChangeNotifier notifier,
        ToastService toasts,
        PosDatabase database,
        OutboxRepository outbox,
        ISyncTransport syncTransport)
    {
        _settings = settings;
        _session = session;
        _notifier = notifier;
        _toasts = toasts;
        _database = database;
        _outbox = outbox;
        _syncTransport = syncTransport;

        GstRates = new ObservableCollection<SelectableChip>
        {
            new() { Key = "500", Label = "5% — standard restaurant" },
            new() { Key = "1800", Label = "18% — specified premises" }
        };

        _notifier.Changed += RefreshSyncDiagnostics;
        Refresh();
    }

    public ObservableCollection<SelectableChip> GstRates { get; }

    [ObservableProperty] private string _outletName = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _city = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _gstin = "";
    [ObservableProperty] private string _fssai = "";
    [ObservableProperty] private string _billPrefix = "";
    [ObservableProperty] private string _terminalName = "";
    [ObservableProperty] private bool _pricesIncludeTax = true;
    [ObservableProperty] private string _gstRateText = "5%";
    [ObservableProperty] private string _licenseText = "";
    [ObservableProperty] private string _licenseState = "Active";
    [ObservableProperty] private string _databasePath = "";
    [ObservableProperty] private string _databaseSizeText = "";
    [ObservableProperty] private string _saveState = "Saved locally";
    [ObservableProperty] private string _syncPendingText = "0 pending";
    [ObservableProperty] private string _syncIssueText = "No pending issues";
    [ObservableProperty] private string _lastSyncStatusText = "Not attempted yet";
    [ObservableProperty] private string _connectivityStatusText = "Offline";
    [ObservableProperty] private string _syncTransportText = "Not configured";

    public string TaxModeText => PricesIncludeTax
        ? "Prices on the menu include GST. Tax is shown as a breakdown."
        : "Prices on the menu exclude GST. Tax is added at checkout.";

    partial void OnPricesIncludeTaxChanged(bool value) => OnPropertyChanged(nameof(TaxModeText));

    public void Refresh()
    {
        _config = _settings.Load();

        OutletName = _config.OutletName;
        Address = _config.Address;
        City = _config.City;
        Phone = _config.Phone;
        Gstin = _config.Gstin;
        Fssai = _config.Fssai;
        BillPrefix = _config.BillPrefix;
        TerminalName = _config.TerminalName;
        PricesIncludeTax = _config.PricesIncludeTax;
        GstRateText = _config.GstRateText;
        LicenseText = $"{_config.LicensePlan} plan • valid until {_config.LicenseUntil}";
        LicenseState = _config.DemoMode ? "Demo licence" : "Active";
        DatabasePath = _database.DatabasePath;
        DatabaseSizeText = File.Exists(_database.DatabasePath)
            ? $"{new FileInfo(_database.DatabasePath).Length / 1024d:0.#} KB"
            : "Not created";

        foreach (var rate in GstRates)
        {
            rate.IsSelected = rate.Key == _config.GstRateBps.ToString();
        }

        RefreshSyncDiagnostics();
    }

    private void RefreshSyncDiagnostics()
    {
        var pending = _outbox.SafeCountPending();
        var failed = _outbox.SafeCountFailedPending();
        var oldestPending = _outbox.SafeGetOldestPendingCreatedAt();

        SyncPendingText = pending == 0 ? "0 pending (all saved locally)" : $"{pending} pending change(s)";
        if (pending == 0)
        {
            SyncIssueText = "No pending issues";
        }
        else
        {
            var parts = new List<string>();
            if (failed > 0)
            {
                parts.Add($"{failed} with last error");
            }

            if (oldestPending is not null)
            {
                parts.Add($"oldest {oldestPending.Value.ToLocalTime():dd MMM, h:mm tt}");
            }

            SyncIssueText = parts.Count == 0 ? "Queued, awaiting sync" : string.Join(" · ", parts);
        }

        var lastStatus = _settings.Get("last_sync_status");
        var lastAttemptRaw = _settings.Get("last_sync_attempt_at");
        if (DateTimeOffset.TryParse(lastAttemptRaw, out var lastAttempt))
        {
            var when = lastAttempt.ToLocalTime().ToString("dd MMM yyyy, h:mm tt");
            LastSyncStatusText = string.IsNullOrWhiteSpace(lastStatus)
                ? $"Last attempt: {when}"
                : $"{lastStatus} ({when})";
        }
        else
        {
            LastSyncStatusText = string.IsNullOrWhiteSpace(lastStatus) ? "Not attempted yet" : lastStatus;
        }

        var online = NetworkInterface.GetIsNetworkAvailable();
        ConnectivityStatusText = online ? "Online (network available)" : "Offline (network unavailable)";
        SyncTransportText = _syncTransport.IsConfigured
            ? "Configured (ready for backend sync)"
            : "Not configured (local outbox only)";
    }

    [RelayCommand]
    private void SelectGstRate(SelectableChip chip)
    {
        foreach (var rate in GstRates)
        {
            rate.IsSelected = rate == chip;
        }

        _config.GstRateBps = int.TryParse(chip.Key, out var bps) ? bps : 500;
        GstRateText = _config.GstRateText;
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(OutletName))
        {
            _toasts.Warning("Outlet name is required.");
            return;
        }

        var gstin = Gstin.Trim().ToUpperInvariant();
        if (gstin.Length != 15 || !gstin.All(char.IsLetterOrDigit))
        {
            _toasts.Warning("A GSTIN must be 15 letters and digits.");
            return;
        }

        _config.OutletName = OutletName.Trim();
        _config.Address = Address.Trim();
        _config.City = City.Trim();
        _config.Phone = Phone.Trim();
        _config.Gstin = gstin;
        _config.Fssai = Fssai.Trim();
        _config.BillPrefix = string.IsNullOrWhiteSpace(BillPrefix) ? "INV" : BillPrefix.Trim().ToUpperInvariant();
        _config.TerminalName = string.IsNullOrWhiteSpace(TerminalName) ? "Counter 01" : TerminalName.Trim();
        _config.PricesIncludeTax = PricesIncludeTax;
        _config.GstRateBps = int.TryParse(GstRates.FirstOrDefault(rate => rate.IsSelected)?.Key, out var bps) ? bps : 500;

        _settings.Save(_config);
        _session.TerminalName = _config.TerminalName;
        SaveState = $"Saved at {DateTime.Now:h:mm tt}";
        _notifier.Notify();
        _toasts.Success("Outlet settings saved. New bills will use these details.");
        Refresh();
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Directory.CreateDirectory(_database.RootDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{_database.RootDirectory}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _toasts.Error($"Could not open the folder: {ex.Message}");
        }
    }

    [RelayCommand]
    private void RefreshDiagnostics()
    {
        DatabaseSizeText = File.Exists(_database.DatabasePath)
            ? $"{new FileInfo(_database.DatabasePath).Length / 1024d:0.#} KB"
            : "Not created";
        RefreshSyncDiagnostics();
        _toasts.Info("Diagnostics refreshed.");
    }
}
