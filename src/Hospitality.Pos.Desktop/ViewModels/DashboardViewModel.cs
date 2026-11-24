using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class DashboardViewModel : ObservableObject, IRefreshable
{
    private readonly ReportRepository _reports;
    private readonly AppSession _session;
    private readonly AppNavigation _navigation;
    private readonly OutboxRepository _outbox;

    public DashboardViewModel(
        ReportRepository reports,
        AppSession session,
        AppNavigation navigation,
        OutboxRepository outbox,
        DataChangeNotifier notifier)
    {
        _reports = reports;
        _session = session;
        _navigation = navigation;
        _outbox = outbox;
        notifier.Changed += Refresh;
        Refresh();
    }

    public ObservableCollection<HourPoint> Hours { get; } = [];
    public ObservableCollection<TopItemRow> TopItems { get; } = [];
    public ObservableCollection<RecentBillRow> RecentBills { get; } = [];
    public ObservableCollection<InventoryItem> LowStock { get; } = [];

    [ObservableProperty] private string _greeting = "";
    [ObservableProperty] private string _dateLine = "";
    [ObservableProperty] private string _salesText = "₹0";
    [ObservableProperty] private string _ordersText = "0";
    [ObservableProperty] private string _averageText = "₹0";
    [ObservableProperty] private string _tablesText = "0 / 0";
    [ObservableProperty] private string _taxText = "₹0";
    [ObservableProperty] private string _openOrdersText = "0 open";
    [ObservableProperty] private string _itemsSoldText = "";
    [ObservableProperty] private bool _hasLowStock;
    [ObservableProperty] private bool _hasLongOpen;
    [ObservableProperty] private string _longOpenText = "";
    [ObservableProperty] private bool _hasPendingSync;
    [ObservableProperty] private string _pendingSyncText = "";
    [ObservableProperty] private bool _hasNoAlerts = true;
    [ObservableProperty] private bool _hasSalesData;
    [ObservableProperty] private bool _hasTopItems;

    public void Refresh()
    {
        var snapshot = _reports.GetDashboard();

        var now = DateTime.Now;
        var partOfDay = now.Hour < 12 ? "Good morning" : now.Hour < 17 ? "Good afternoon" : "Good evening";
        var firstName = (_session.CurrentStaff?.Name ?? "there").Split(' ')[0];
        Greeting = $"{partOfDay}, {firstName}";
        DateLine = now.ToString("dddd, d MMMM yyyy");

        SalesText = Services.Inr.Format(snapshot.SalesPaise);
        OrdersText = snapshot.OrderCount.ToString();
        AverageText = Services.Inr.Format(snapshot.AverageBillPaise);
        TablesText = $"{snapshot.TablesOccupied} / {snapshot.TablesTotal}";
        TaxText = Services.Inr.Format(snapshot.TaxPaise);
        OpenOrdersText = $"{snapshot.OpenOrderCount} open";
        ItemsSoldText = snapshot.Covers == 0 ? "No covers yet" : $"{snapshot.Covers} covers today";

        Replace(Hours, snapshot.Hourly);
        Replace(TopItems, snapshot.TopItems);
        Replace(RecentBills, snapshot.RecentBills);
        Replace(LowStock, snapshot.LowStock);
        HasLowStock = LowStock.Count > 0;
        HasSalesData = snapshot.SalesPaise > 0;
        HasTopItems = TopItems.Count > 0;

        HasLongOpen = snapshot.LongOpenCount > 0;
        LongOpenText = snapshot.LongOpenCount == 1
            ? "1 table open over 2h"
            : $"{snapshot.LongOpenCount} tables open over 2h";

        var pending = _outbox.SafeCountPending();
        HasPendingSync = pending > 0;
        PendingSyncText = pending == 1 ? "1 change waiting to sync" : $"{pending} changes waiting to sync";
        HasNoAlerts = !HasLowStock && !HasLongOpen && !HasPendingSync;
    }

    [RelayCommand]
    private void NewOrder() => _navigation.Navigate("Billing");

    [RelayCommand]
    private void ViewReports() => _navigation.Navigate("Reports");

    [RelayCommand]
    private void ViewTables() => _navigation.Navigate("Tables");

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
