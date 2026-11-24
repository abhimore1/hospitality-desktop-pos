using System.Collections.ObjectModel;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using Hospitality.Pos.Desktop.Services.Sync;
using Hospitality.Pos.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppSession _session;
    private readonly SettingsRepository _settings;
    private readonly OutboxRepository _outbox;
    private readonly ISyncCoordinator _sync;
    private readonly ToastService _toasts;
    private readonly IServiceProvider _services;
    private readonly DashboardViewModel _dashboard;
    private readonly BillingViewModel _billing;
    private readonly TablesViewModel _tables;
    private readonly KitchenViewModel _kitchen;
    private readonly MenuViewModel _menu;
    private readonly ReportsViewModel _reports;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly DispatcherTimer _clockTimer;
    private readonly DispatcherTimer _toastTimer;
    private int _tick;

    public MainViewModel(
        AppSession session,
        SettingsRepository settings,
        OutboxRepository outbox,
        ISyncCoordinator sync,
        ToastService toasts,
        DataChangeNotifier notifier,
        AppNavigation navigation,
        IServiceProvider services,
        DashboardViewModel dashboard,
        BillingViewModel billing,
        TablesViewModel tables,
        KitchenViewModel kitchen,
        MenuViewModel menu,
        ReportsViewModel reports,
        SettingsViewModel settingsViewModel)
    {
        _session = session;
        _settings = settings;
        _outbox = outbox;
        _sync = sync;
        _toasts = toasts;
        _services = services;
        _dashboard = dashboard;
        _billing = billing;
        _tables = tables;
        _kitchen = kitchen;
        _menu = menu;
        _reports = reports;
        _settingsViewModel = settingsViewModel;

        NavItems =
        [
            new NavItem { Key = "Dashboard", Title = "Overview", Icon = FindIcon("IconDashboard") },
            new NavItem { Key = "Billing", Title = "Billing", Icon = FindIcon("IconBilling") },
            new NavItem { Key = "Tables", Title = "Tables", Icon = FindIcon("IconTables") },
            new NavItem { Key = "Kitchen", Title = "Kitchen", Icon = FindIcon("IconKitchen") },
            new NavItem { Key = "Menu", Title = "Menu", Icon = FindIcon("IconMenu") },
            new NavItem { Key = "Reports", Title = "Reports", Icon = FindIcon("IconReports") },
            new NavItem { Key = "Settings", Title = "Settings", Icon = FindIcon("IconSettings") }
        ];

        _toasts.Requested += OnToastRequested;
        _session.StaffChanged += RefreshHeader;
        notifier.Changed += OnDataChanged;
        navigation.Requested += OnNavigationRequested;

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => OnClockTick();
        _clockTimer.Start();

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.8) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastVisible = false;
        };

        RefreshHeader();
        SelectedNav = NavItems[0];
        ToastVisible = false;
    }

    public ObservableCollection<NavItem> NavItems { get; }

    [ObservableProperty] private NavItem? _selectedNav;
    [ObservableProperty] private object? _currentViewModel;
    [ObservableProperty] private string _pageTitle = "Overview";

    [ObservableProperty] private string _outletName = "";
    [ObservableProperty] private string _staffName = "";
    [ObservableProperty] private string _staffRole = "";
    [ObservableProperty] private string _initials = "";
    [ObservableProperty] private string _terminalText = "";
    [ObservableProperty] private string _clockText = "";
    [ObservableProperty] private string _dateText = "";
    [ObservableProperty] private string _planText = "";
    [ObservableProperty] private string _syncText = "";
    [ObservableProperty] private string _syncToolTip = "Check local sync queue";
    [ObservableProperty] private string _connectivityText = "";
    [ObservableProperty] private bool _isOnline;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncNowCommand))]
    private bool _isSyncing;
    [ObservableProperty] private Brush _avatarBrush = Ui.Brush("#0F766E");
    [ObservableProperty] private Brush _connectivityBrush = Ui.Brush("#16A34A");

    [ObservableProperty] private bool _toastVisible;
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private Brush _toastAccent = Ui.Brush("#0F766E");
    [ObservableProperty] private Brush _toastSurface = Ui.Brush("#ECFDF5");
    [ObservableProperty] private Geometry? _toastIcon;

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null)
        {
            return;
        }

        PageTitle = value.Title;
        CurrentViewModel = value.Key switch
        {
            "Dashboard" => _dashboard,
            "Billing" => _billing,
            "Tables" => _tables,
            "Kitchen" => _kitchen,
            "Menu" => _menu,
            "Reports" => _reports,
            "Settings" => _settingsViewModel,
            _ => _dashboard
        };

        if (CurrentViewModel is IRefreshable refreshable)
        {
            refreshable.Refresh();
        }

        if (value.Key == "Kitchen")
        {
            UpdateKitchenBadge();
        }
    }

    [RelayCommand]
    private void Navigate(string key)
    {
        OnNavigationRequested(key, null);
    }

    [RelayCommand]
    private void NewOrder()
    {
        OnNavigationRequested("Billing", null);
    }

    [RelayCommand]
    private void ShowReports()
    {
        OnNavigationRequested("Reports", null);
    }

    [RelayCommand]
    private void Lock()
    {
        var login = _services.GetRequiredService<LoginWindow>();
        login.Owner = Application.Current.MainWindow;
        login.WindowStartupLocation = WindowStartupLocation.CenterOwner;

        if (login.ShowDialog() == true)
        {
            RefreshHeader();
            _toasts.Success($"Signed in as {_session.CurrentStaff?.Name}.");
        }
    }

    private bool CanSyncNow() => !IsSyncing;

    [RelayCommand(CanExecute = nameof(CanSyncNow))]
    private async Task SyncNow()
    {
        if (IsSyncing)
        {
            return;
        }

        try
        {
            IsSyncing = true;
            SyncText = "Syncing…";
            SyncToolTip = "Sync in progress";

            var result = await _sync.SyncPendingAsync();

            if (result.SyncedCount > 0)
            {
                _toasts.Success(result.Message);
            }
            else if (result.Attempted && result.FailedCount > 0)
            {
                _toasts.Warning(result.Message);
            }
            else
            {
                _toasts.Info(result.Message);
            }
        }
        catch (Exception ex)
        {
            _toasts.Error($"Sync could not run. {ex.Message}");
        }
        finally
        {
            IsSyncing = false;
            RefreshSyncStatus();
        }
    }

    private void OnNavigationRequested(string key, object? argument)
    {
        if (key == "Billing")
        {
            if (argument is int tableId)
            {
                _billing.Prepare(tableId);
            }
            else
            {
                _billing.PrepareNew();
            }
        }

        var item = NavItems.FirstOrDefault(nav => nav.Key == key);
        if (item is not null)
        {
            SelectedNav = item;
        }
    }

    private void OnDataChanged()
    {
        RefreshSyncStatus();
        UpdateKitchenBadge();
    }

    private void OnClockTick()
    {
        var now = DateTime.Now;
        ClockText = now.ToString("h:mm tt");
        _tick++;

        if (_tick % 5 == 0)
        {
            RefreshSyncStatus();
        }
    }

    private void RefreshHeader()
    {
        var config = _settings.Load();
        _session.TerminalName = config.TerminalName;

        OutletName = config.OutletName;
        TerminalText = config.TerminalName;
        var staff = _session.CurrentStaff;
        StaffName = staff?.Name ?? "Not signed in";
        StaffRole = staff?.RoleLabel ?? "";
        Initials = staff?.Initials ?? "?";
        AvatarBrush = Ui.Brush("#0F766E");
        PlanText = $"{config.LicensePlan} plan";
        DateText = DateTime.Now.ToString("ddd, d MMM yyyy");
        ClockText = DateTime.Now.ToString("h:mm tt");
        RefreshSyncStatus();
    }

    private void RefreshSyncStatus()
    {
        var pending = _outbox.CountPending();
        SyncText = pending == 0 ? "Saved locally" : $"{pending} pending";
        IsOnline = NetworkInterface.GetIsNetworkAvailable();
        ConnectivityText = IsOnline ? "Online" : "Offline";
        ConnectivityBrush = Ui.Brush(IsOnline ? "#16A34A" : "#D97706");

        var lastStatus = _settings.Get("last_sync_status");
        var lastAttemptRaw = _settings.Get("last_sync_attempt_at");
        var lastAttemptText = "";
        if (DateTimeOffset.TryParse(lastAttemptRaw, out var lastAttempt))
        {
            lastAttemptText = lastAttempt.ToLocalTime().ToString("dd MMM, h:mm tt");
        }

        SyncToolTip = string.IsNullOrWhiteSpace(lastStatus)
            ? "Check local sync queue"
            : string.IsNullOrWhiteSpace(lastAttemptText)
                ? $"Last sync: {lastStatus}"
                : $"Last sync: {lastStatus} ({lastAttemptText})";
    }

    private void UpdateKitchenBadge()
    {
        var kitchen = NavItems.FirstOrDefault(nav => nav.Key == "Kitchen");
        if (kitchen is null)
        {
            return;
        }

        var open = _kitchen.OpenTicketCount;
        kitchen.Badge = open == 0 ? "" : open.ToString();
    }

    private void OnToastRequested(string message, ToastKind kind)
    {
        ToastMessage = message;
        ToastVisible = true;
        ToastAccent = kind switch
        {
            ToastKind.Success => Ui.Brush("#047857"),
            ToastKind.Warning => Ui.Brush("#B45309"),
            ToastKind.Error => Ui.Brush("#B91C1C"),
            _ => Ui.Brush("#0F766E")
        };
        ToastSurface = kind switch
        {
            ToastKind.Success => Ui.Brush("#ECFDF5"),
            ToastKind.Warning => Ui.Brush("#FFFBEB"),
            ToastKind.Error => Ui.Brush("#FEF2F2"),
            _ => Ui.Brush("#F0FDFA")
        };
        ToastIcon = FindIcon(kind is ToastKind.Error or ToastKind.Warning ? "IconAlert" : "IconCheck");

        _toastTimer.Stop();
        _toastTimer.Start();
    }

    private static Geometry? FindIcon(string key) =>
        Application.Current.TryFindResource(key) as Geometry;
}
