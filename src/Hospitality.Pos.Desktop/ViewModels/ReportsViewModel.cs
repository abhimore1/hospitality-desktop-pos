using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using Microsoft.Win32;

namespace Hospitality.Pos.Desktop.ViewModels;

public partial class ReportsViewModel : ObservableObject, IRefreshable
{
    private readonly ReportRepository _reports;
    private readonly SettingsRepository _settings;
    private readonly OrderRepository _orders;
    private readonly AppSession _session;
    private readonly ToastService _toasts;
    private ReportSnapshot _snapshot = new();

    private long _dayEndTotalSalesPaise;
    private long _dayEndTaxPaise;
    private long _dayEndDiscountPaise;
    private long _dayEndSystemCashPaise;
    private int _dayEndOpenOrdersCount;
    private int _dayEndUnsyncedCount;

    public ReportsViewModel(
        ReportRepository reports,
        SettingsRepository settings,
        OrderRepository orders,
        AppSession session,
        ToastService toasts)
    {
        _reports = reports;
        _settings = settings;
        _orders = orders;
        _session = session;
        _toasts = toasts;

        Periods = new ObservableCollection<SelectableChip>
        {
            new() { Key = "7", Label = "Last 7 days", IsSelected = true },
            new() { Key = "30", Label = "Last 30 days" }
        };

        ReportTabs = new ObservableCollection<SelectableChip>
        {
            new() { Key = "0", Label = "Performance Overview", IsSelected = true },
            new() { Key = "1", Label = "Leakage & Anti-Fraud" },
            new() { Key = "2", Label = "Day-End Closing (Z-Report)" }
        };

        Refresh();
    }

    public ObservableCollection<SelectableChip> Periods { get; }
    public ObservableCollection<SelectableChip> ReportTabs { get; }
    public ObservableCollection<DailyPoint> Daily { get; } = [];
    public ObservableCollection<MixRow> PaymentMix { get; } = [];
    public ObservableCollection<MixRow> CategoryMix { get; } = [];
    public ObservableCollection<TopItemRow> TopItems { get; } = [];
    public ObservableCollection<LeakageEvent> LeakageEvents { get; } = [];

    // Tabs
    [ObservableProperty] private int _selectedTabIndex = 0;
    public bool IsPerformanceTab => SelectedTabIndex == 0;
    public bool IsLeakageTab => SelectedTabIndex == 1;
    public bool IsDayEndTab => SelectedTabIndex == 2;

    [ObservableProperty] private string _salesText = "₹0";
    [ObservableProperty] private string _ordersText = "0";
    [ObservableProperty] private string _averageText = "₹0";
    [ObservableProperty] private string _taxText = "₹0";
    [ObservableProperty] private string _cgstText = "₹0";
    [ObservableProperty] private string _sgstText = "₹0";
    [ObservableProperty] private string _periodLabel = "Last 7 days";
    [ObservableProperty] private string _peakDayText = "—";
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private string _exportHint = "CSV includes sales, tax, payments and top items.";
    [ObservableProperty] private Brush _taxBarBrush = Ui.Brush("#0F766E");

    // Leakage / Anti-fraud
    [ObservableProperty] private string _totalDiscountsText = "₹0";
    [ObservableProperty] private int _discountCount;
    [ObservableProperty] private int _voidCount;
    [ObservableProperty] private int _reprintCount;
    [ObservableProperty] private bool _hasLeakageEvents;

    // Day-End Closing
    [ObservableProperty] private string _dayEndDateText = DateTime.Today.ToString("dd MMMM yyyy");
    [ObservableProperty] private string _dayEndTotalSalesText = "₹0";
    [ObservableProperty] private string _dayEndTaxText = "₹0";
    [ObservableProperty] private string _dayEndDiscountText = "₹0";
    [ObservableProperty] private string _dayEndSystemCashText = "₹0";
    [ObservableProperty] private bool _hasOpenOrders;
    [ObservableProperty] private string _openOrdersWarning = "";
    [ObservableProperty] private bool _hasUnsynced;
    [ObservableProperty] private string _unsyncedWarning = "";

    // Cash Denominations
    [ObservableProperty] private int _count500;
    [ObservableProperty] private int _count200;
    [ObservableProperty] private int _count100;
    [ObservableProperty] private int _count50;
    [ObservableProperty] private int _count20;
    [ObservableProperty] private int _count10;
    [ObservableProperty] private int _countCoins;
    [ObservableProperty] private long _countedCashPaise;
    [ObservableProperty] private string _countedCashText = "₹0";
    [ObservableProperty] private long _cashVariancePaise;
    [ObservableProperty] private string _cashVarianceText = "₹0";
    [ObservableProperty] private bool _isCashBalanced = true;
    [ObservableProperty] private string _closureNotes = "";

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPerformanceTab));
        OnPropertyChanged(nameof(IsLeakageTab));
        OnPropertyChanged(nameof(IsDayEndTab));
    }

    partial void OnCount500Changed(int value) => RecalculateCash();
    partial void OnCount200Changed(int value) => RecalculateCash();
    partial void OnCount100Changed(int value) => RecalculateCash();
    partial void OnCount50Changed(int value) => RecalculateCash();
    partial void OnCount20Changed(int value) => RecalculateCash();
    partial void OnCount10Changed(int value) => RecalculateCash();
    partial void OnCountCoinsChanged(int value) => RecalculateCash();

    private void RecalculateCash()
    {
        CountedCashPaise = (Count500 * 500L + Count200 * 200L + Count100 * 100L +
                            Count50 * 50L + Count20 * 20L + Count10 * 10L + CountCoins) * 100L;
        CountedCashText = Services.Inr.Format(CountedCashPaise);
        CashVariancePaise = CountedCashPaise - _dayEndSystemCashPaise;
        CashVarianceText = (CashVariancePaise > 0 ? "+" : "") + Services.Inr.Format(CashVariancePaise);
        IsCashBalanced = CashVariancePaise == 0;
    }

    public void Refresh()
    {
        var days = int.TryParse(Periods.FirstOrDefault(period => period.IsSelected)?.Key, out var value) ? value : 7;
        _snapshot = _reports.GetReport(days);

        SalesText = Services.Inr.Format(_snapshot.SalesPaise);
        OrdersText = _snapshot.OrderCount.ToString();
        AverageText = Services.Inr.Format(_snapshot.AverageBillPaise);
        TaxText = Services.Inr.Format(_snapshot.TaxPaise);
        CgstText = Services.Inr.Format(_snapshot.CgstPaise);
        SgstText = Services.Inr.Format(_snapshot.SgstPaise);
        PeriodLabel = days == 7 ? "Last 7 days" : "Last 30 days";
        IsEmpty = _snapshot.OrderCount == 0;

        var peak = _snapshot.Daily.Where(point => point.IsPeak).OrderByDescending(point => point.TotalPaise).FirstOrDefault();
        PeakDayText = peak is null || peak.TotalPaise == 0
            ? "No sales in this period"
            : $"{peak.Label} • {Services.Inr.Format(peak.TotalPaise)}";

        Replace(Daily, _snapshot.Daily);
        Replace(PaymentMix, _snapshot.PaymentMix);
        Replace(CategoryMix, _snapshot.CategoryMix);
        Replace(TopItems, _snapshot.TopItems);

        // Leakage report
        var leakage = _orders.GetLeakageReport(days);
        Replace(LeakageEvents, leakage);
        HasLeakageEvents = leakage.Count > 0;

        var discountTotal = leakage.Where(e => e.EventType == "Discount").Sum(e => e.AmountPaise);
        TotalDiscountsText = Services.Inr.Format(discountTotal);
        DiscountCount = leakage.Count(e => e.EventType == "Discount");
        VoidCount = leakage.Count(e => e.EventType.Contains("Cancel", StringComparison.OrdinalIgnoreCase) || e.EventType.Contains("Void", StringComparison.OrdinalIgnoreCase));
        ReprintCount = leakage.Count(e => e.EventType == "Reprint");

        // Day-End closing summary for today
        var actor = _session.CurrentStaff?.Name ?? "Manager";
        var daySummary = _orders.GetDayEndSummary(DateTime.Today, actor);
        _dayEndTotalSalesPaise = daySummary.TotalSalesPaise;
        _dayEndTaxPaise = daySummary.TotalTaxPaise;
        _dayEndDiscountPaise = daySummary.TotalDiscountPaise;
        _dayEndSystemCashPaise = daySummary.SystemCashPaise;
        _dayEndOpenOrdersCount = daySummary.OpenOrdersCount;
        _dayEndUnsyncedCount = daySummary.UnsyncedCount;

        DayEndDateText = DateTime.Today.ToString("dd MMMM yyyy");
        DayEndTotalSalesText = daySummary.TotalSalesText;
        DayEndTaxText = daySummary.TotalTaxText;
        DayEndDiscountText = daySummary.TotalDiscountText;
        DayEndSystemCashText = daySummary.SystemCashText;

        HasOpenOrders = _dayEndOpenOrdersCount > 0;
        OpenOrdersWarning = HasOpenOrders ? $"{_dayEndOpenOrdersCount} order(s) still open. Settle or cancel before day close." : "";

        HasUnsynced = _dayEndUnsyncedCount > 0;
        UnsyncedWarning = HasUnsynced ? $"{_dayEndUnsyncedCount} offline record(s) pending cloud sync." : "";

        RecalculateCash();
    }

    [RelayCommand]
    private void SelectTab(SelectableChip tab)
    {
        foreach (var t in ReportTabs)
        {
            t.IsSelected = t == tab;
        }

        if (int.TryParse(tab.Key, out var tabIndex))
        {
            SelectedTabIndex = tabIndex;
        }
    }

    [RelayCommand]
    private void SelectPeriod(SelectableChip chip)
    {
        foreach (var period in Periods)
        {
            period.IsSelected = period == chip;
        }

        Refresh();
    }

    [RelayCommand]
    private void CompleteDayEndClosure()
    {
        if (_dayEndOpenOrdersCount > 0)
        {
            var confirm = MessageBox.Show(
                $"There are still {_dayEndOpenOrdersCount} open order(s) on tables. Closing the business day will finalize today's cash drawer.\n\nAre you sure you want to proceed?",
                "Open Orders Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
            {
                return;
            }
        }

        var actor = _session.CurrentStaff?.Name ?? "Manager";
        var denominationsJson = JsonSerializer.Serialize(new
        {
            C500 = Count500,
            C200 = Count200,
            C100 = Count100,
            C50 = Count50,
            C20 = Count20,
            C10 = Count10,
            Coins = CountCoins
        });

        var closure = new DayEndClosure
        {
            ClosureDate = DateTime.Today.ToString("yyyy-MM-dd"),
            ClosedBy = actor,
            TotalSalesPaise = _dayEndTotalSalesPaise,
            TotalTaxPaise = _dayEndTaxPaise,
            TotalDiscountPaise = _dayEndDiscountPaise,
            SystemCashPaise = _dayEndSystemCashPaise,
            CountedCashPaise = CountedCashPaise,
            CashVariancePaise = CashVariancePaise,
            DenominationsJson = denominationsJson,
            OpenOrdersCount = _dayEndOpenOrdersCount,
            UnsyncedCount = _dayEndUnsyncedCount,
            CreatedAt = DateTime.Now,
            Notes = ClosureNotes
        };

        try
        {
            _orders.RecordDayEndClosure(closure);
            _toasts.Success("Business day closed successfully. Z-Report logged.");
            Refresh();
        }
        catch (Exception ex)
        {
            _toasts.Error($"Day-end closure failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportTallyXml()
    {
        var config = _settings.Load();
        var days = int.TryParse(Periods.FirstOrDefault(period => period.IsSelected)?.Key, out var value) ? value : 7;
        var dialog = new SaveFileDialog
        {
            Filter = "Tally XML file (*.xml)|*.xml",
            FileName = $"tally-sales-vouchers-{DateTime.Today:yyyy-MM-dd}.xml",
            Title = "Export to Tally XML"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var xml = _reports.GenerateTallyXml(days, config);
            File.WriteAllText(dialog.FileName, xml, new UTF8Encoding(false));
            ExportHint = $"Tally XML exported to {dialog.FileName}";
            _toasts.Success("Tally XML vouchers exported.");
        }
        catch (Exception ex)
        {
            _toasts.Error($"Tally export failed: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ExportCsv()
    {
        var config = _settings.Load();
        var dialog = new SaveFileDialog
        {
            Filter = "CSV file (*.csv)|*.csv",
            FileName = $"sales-report-{DateTime.Today:yyyy-MM-dd}.csv",
            Title = "Export sales report"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var csv = BuildCsv(config);
            File.WriteAllText(dialog.FileName, csv, new UTF8Encoding(true));
            ExportHint = $"Exported to {dialog.FileName}";
            _toasts.Success("Sales report exported.");
        }
        catch (Exception ex)
        {
            _toasts.Error($"Export failed: {ex.Message}");
        }
    }

    private string BuildCsv(PosSettings config)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Restrunt POS — sales report");
        builder.AppendLine($"Outlet,{Escape(config.OutletName)}");
        builder.AppendLine($"GSTIN,{Escape(config.Gstin)}");
        builder.AppendLine($"Period,{Escape(PeriodLabel)}");
        builder.AppendLine($"Generated,{DateTime.Now:yyyy-MM-dd HH:mm}");
        builder.AppendLine();
        builder.AppendLine("Summary");
        builder.AppendLine("Metric,Amount (INR)");
        builder.AppendLine($"Net sales,{Rupees(_snapshot.SalesPaise)}");
        builder.AppendLine($"Bills,{_snapshot.OrderCount}");
        builder.AppendLine($"Average bill,{Rupees(_snapshot.AverageBillPaise)}");
        builder.AppendLine($"Taxable value,{Rupees(_snapshot.SalesPaise - _snapshot.TaxPaise)}");
        builder.AppendLine($"CGST,{Rupees(_snapshot.CgstPaise)}");
        builder.AppendLine($"SGST,{Rupees(_snapshot.SgstPaise)}");
        builder.AppendLine($"Total tax,{Rupees(_snapshot.TaxPaise)}");
        builder.AppendLine();
        builder.AppendLine("Daily sales");
        builder.AppendLine("Date,Sales (INR)");
        foreach (var day in _snapshot.Daily)
        {
            builder.AppendLine($"{Escape(day.Label)},{Rupees(day.TotalPaise)}");
        }

        builder.AppendLine();
        builder.AppendLine("Payment mix");
        builder.AppendLine("Method,Bills,Amount (INR),Share %");
        foreach (var method in _snapshot.PaymentMix)
        {
            builder.AppendLine($"{Escape(method.Label)},{method.Count},{Rupees(method.TotalPaise)},{method.SharePercent:0.0}");
        }

        builder.AppendLine();
        builder.AppendLine("Top items");
        builder.AppendLine("Item,Category,Quantity,Revenue (INR)");
        foreach (var item in _snapshot.TopItems)
        {
            builder.AppendLine($"{Escape(item.Name)},{Escape(item.CategoryName)},{item.Quantity},{Rupees(item.RevenuePaise)}");
        }

        return builder.ToString();
    }

    private static string Rupees(long paise) =>
        (paise / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> source)
    {
        target.Clear();
        foreach (var item in source)
        {
            target.Add(item);
        }
    }
}
