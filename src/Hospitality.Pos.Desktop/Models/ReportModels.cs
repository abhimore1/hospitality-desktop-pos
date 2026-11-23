namespace Hospitality.Pos.Desktop.Models;

public sealed class HourPoint
{
    public string HourLabel { get; set; } = "";
    public long TotalPaise { get; set; }
    public double BarHeight { get; set; }
    public string TotalText => Services.Inr.Format(TotalPaise);
}

public sealed class TopItemRow
{
    public string Name { get; set; } = "";
    public int Quantity { get; set; }
    public long RevenuePaise { get; set; }
    public double Share { get; set; }
    public string CategoryName { get; set; } = "";
    public double SharePercent => Math.Round(Share * 100);
    public double ShareWidth => Math.Max(4, Math.Round(Share * 88));
    public string RevenueText => Services.Inr.Format(RevenuePaise);
}

public sealed class RecentBillRow
{
    public string BillNo { get; set; } = "";
    public string TimeText { get; set; } = "";
    public string TableLabel { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public long TotalPaise { get; set; }
    public string TotalText => Services.Inr.Format(TotalPaise);
    public string MethodInitial => string.IsNullOrEmpty(PaymentMethod) ? "?" : PaymentMethod[..1].ToUpperInvariant();
}

public sealed class MixRow
{
    public string Label { get; set; } = "";
    public long TotalPaise { get; set; }
    public int Count { get; set; }
    public double Share { get; set; }
    public string Color { get; set; } = "#0F766E";
    public string TotalText => Services.Inr.Format(TotalPaise);
    public double SharePercent => Math.Round(Share * 100);
    public double ShareWidth => Math.Max(4, Math.Round(Share * 120));
    public string CountText => $"{Count} bill{(Count == 1 ? "" : "s")}";
}

public sealed class DailyPoint
{
    public string Label { get; set; } = "";
    public long TotalPaise { get; set; }
    public double BarHeight { get; set; }
    public string TotalText => Services.Inr.Format(TotalPaise);
    public bool IsPeak { get; set; }
}

public sealed class DashboardSnapshot
{
    public long SalesPaise { get; set; }
    public int OrderCount { get; set; }
    public int Covers { get; set; }
    public long AverageBillPaise { get; set; }
    public int OpenOrderCount { get; set; }
    public int TablesOccupied { get; set; }
    public int TablesTotal { get; set; }
    public long TaxPaise { get; set; }
    public List<HourPoint> Hourly { get; set; } = [];
    public List<TopItemRow> TopItems { get; set; } = [];
    public List<RecentBillRow> RecentBills { get; set; } = [];
    public List<InventoryItem> LowStock { get; set; } = [];
}

public sealed class ReportSnapshot
{
    public int Days { get; set; }
    public long SalesPaise { get; set; }
    public int OrderCount { get; set; }
    public long AverageBillPaise { get; set; }
    public long TaxPaise { get; set; }
    public long CgstPaise { get; set; }
    public long SgstPaise { get; set; }
    public List<DailyPoint> Daily { get; set; } = [];
    public List<MixRow> PaymentMix { get; set; } = [];
    public List<MixRow> CategoryMix { get; set; } = [];
    public List<TopItemRow> TopItems { get; set; } = [];
    public int PaidOrders { get; set; }
}
