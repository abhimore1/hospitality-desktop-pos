using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text;
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
    private readonly ToastService _toasts;
    private ReportSnapshot _snapshot = new();

    public ReportsViewModel(ReportRepository reports, SettingsRepository settings, ToastService toasts)
    {
        _reports = reports;
        _settings = settings;
        _toasts = toasts;

        Periods = new ObservableCollection<SelectableChip>
        {
            new() { Key = "7", Label = "Last 7 days", IsSelected = true },
            new() { Key = "30", Label = "Last 30 days" }
        };

        Refresh();
    }

    public ObservableCollection<SelectableChip> Periods { get; }
    public ObservableCollection<DailyPoint> Daily { get; } = [];
    public ObservableCollection<MixRow> PaymentMix { get; } = [];
    public ObservableCollection<MixRow> CategoryMix { get; } = [];
    public ObservableCollection<TopItemRow> TopItems { get; } = [];

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
