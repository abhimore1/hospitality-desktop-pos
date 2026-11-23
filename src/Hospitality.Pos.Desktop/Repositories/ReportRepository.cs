using Dapper;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Models;

namespace Hospitality.Pos.Desktop.Repositories;

public sealed class ReportRepository
{
    private static readonly string[] MixColors =
    [
        "#0F766E", "#F59E0B", "#0284C7", "#7C3AED", "#DB2777", "#16A34A", "#DC2626"
    ];

    private readonly PosDatabase _database;

    public ReportRepository(PosDatabase database) => _database = database;

    public DashboardSnapshot GetDashboard()
    {
        using var connection = _database.Open();

        var agg = connection.QuerySingle<DashboardAgg>(
            """
            SELECT COALESCE(SUM(total_paise), 0) AS sales_paise,
                   COUNT(*) AS order_count,
                   COALESCE(SUM(tax_paise), 0) AS tax_paise
            FROM orders
            WHERE status = 'paid' AND date(paid_at) = date('now', 'localtime');
            """);

        var openOrders = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM orders WHERE status = 'open';");
        var tablesTotal = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM dining_tables;");
        var tablesOccupied = connection.ExecuteScalar<int>(
            "SELECT COUNT(DISTINCT table_id) FROM orders WHERE status = 'open' AND table_id IS NOT NULL;");

        var hourlyRows = connection.Query<HourAgg>(
            """
            SELECT strftime('%H', paid_at) AS hour, SUM(total_paise) AS total_paise
            FROM orders
            WHERE status = 'paid' AND date(paid_at) = date('now', 'localtime')
            GROUP BY hour;
            """).ToDictionary(row => row.Hour ?? "", row => row.TotalPaise);

        var hourly = new List<HourPoint>();
        for (var hour = 8; hour <= 22; hour++)
        {
            var total = hourlyRows.GetValueOrDefault(hour.ToString("00"));
            hourly.Add(new HourPoint
            {
                HourLabel = hour switch
                {
                    12 => "12p",
                    > 12 => $"{hour - 12}p",
                    _ => $"{hour}a"
                },
                TotalPaise = total
            });
        }

        var maxHour = hourly.Max(point => point.TotalPaise);
        foreach (var point in hourly)
        {
            point.BarHeight = maxHour == 0 ? 6 : Math.Max(6, 132d * point.TotalPaise / maxHour);
        }

        var topItems = connection.Query<TopItemRaw>(
            """
            SELECT oi.name AS name,
                   SUM(oi.qty) AS quantity,
                   SUM(oi.line_total_paise) AS revenue_paise,
                   COALESCE(MAX(c.name), '') AS category_name
            FROM order_items oi
            JOIN orders o ON o.id = oi.order_id
            LEFT JOIN menu_items m ON m.id = oi.menu_item_id
            LEFT JOIN categories c ON c.id = m.category_id
            WHERE o.status = 'paid' AND date(o.paid_at) = date('now', 'localtime')
            GROUP BY oi.name
            ORDER BY quantity DESC, revenue_paise DESC
            LIMIT 5;
            """).ToList();

        var maxQty = topItems.Count == 0 ? 1 : topItems.Max(item => item.Quantity);
        var top = topItems.Select(item => new TopItemRow
        {
            Name = item.Name,
            Quantity = item.Quantity,
            RevenuePaise = item.RevenuePaise,
            CategoryName = item.CategoryName,
            Share = (double)item.Quantity / maxQty
        }).ToList();

        var recent = connection.Query<RecentBillRaw>(
            """
            SELECT o.bill_no AS bill_no,
                   o.total_paise AS total_paise,
                   o.payment_method AS payment_method,
                   COALESCE(t.name, CASE WHEN o.order_type = 'Takeaway' THEN 'Takeaway' ELSE 'Delivery' END) AS table_label,
                   o.paid_at AS paid_at
            FROM orders o
            LEFT JOIN dining_tables t ON t.id = o.table_id
            WHERE o.status = 'paid' AND date(o.paid_at) = date('now', 'localtime')
            ORDER BY o.paid_at DESC
            LIMIT 6;
            """).Select(row => new RecentBillRow
            {
                BillNo = row.BillNo,
                TotalPaise = row.TotalPaise,
                PaymentMethod = row.PaymentMethod,
                TableLabel = row.TableLabel,
                TimeText = row.PaidAt.ToString("h:mm tt")
            }).ToList();

        var lowStock = connection.Query<InventoryItem>(
            """
            SELECT * FROM inventory_items
            WHERE stock <= low_threshold
            ORDER BY (stock / NULLIF(low_threshold, 0)), name
            LIMIT 5;
            """).ToList();

        return new DashboardSnapshot
        {
            SalesPaise = agg.SalesPaise,
            OrderCount = agg.OrderCount,
            Covers = agg.OrderCount,
            AverageBillPaise = agg.OrderCount == 0 ? 0 : agg.SalesPaise / agg.OrderCount,
            TaxPaise = agg.TaxPaise,
            OpenOrderCount = openOrders,
            TablesOccupied = tablesOccupied,
            TablesTotal = tablesTotal,
            Hourly = hourly,
            TopItems = top,
            RecentBills = recent,
            LowStock = lowStock
        };
    }

    public ReportSnapshot GetReport(int days)
    {
        using var connection = _database.Open();
        var startDate = DateTime.Today.AddDays(-(days - 1)).ToString("yyyy-MM-dd");

        var agg = connection.QuerySingle<ReportAgg>(
            """
            SELECT COALESCE(SUM(total_paise), 0) AS sales_paise,
                   COUNT(*) AS order_count,
                   COALESCE(SUM(tax_paise), 0) AS tax_paise,
                   COALESCE(SUM(cgst_paise), 0) AS cgst_paise,
                   COALESCE(SUM(sgst_paise), 0) AS sgst_paise
            FROM orders
            WHERE status = 'paid' AND date(paid_at) >= @startDate;
            """, new { startDate });

        var dailyRows = connection.Query<DailyAgg>(
            """
            SELECT date(paid_at) AS day, SUM(total_paise) AS total_paise
            FROM orders
            WHERE status = 'paid' AND date(paid_at) >= @startDate
            GROUP BY day
            ORDER BY day;
            """, new { startDate }).ToDictionary(row => row.Day, row => row.TotalPaise);

        var daily = new List<DailyPoint>();
        for (var offset = days - 1; offset >= 0; offset--)
        {
            var date = DateTime.Today.AddDays(-offset);
            var total = dailyRows.GetValueOrDefault(date.ToString("yyyy-MM-dd"));
            daily.Add(new DailyPoint
            {
                Label = date.ToString("ddd d"),
                TotalPaise = total
            });
        }

        var maxDaily = daily.Count == 0 ? 0 : daily.Max(point => point.TotalPaise);
        foreach (var point in daily)
        {
            point.BarHeight = maxDaily == 0 ? 6 : Math.Max(6, 150d * point.TotalPaise / maxDaily);
            point.IsPeak = point.TotalPaise == maxDaily && maxDaily > 0;
        }

        var paymentRows = connection.Query<MixRaw>(
            """
            SELECT COALESCE(NULLIF(payment_method, ''), 'Other') AS label,
                   COUNT(*) AS count,
                   SUM(total_paise) AS total_paise
            FROM orders
            WHERE status = 'paid' AND date(paid_at) >= @startDate
            GROUP BY label
            ORDER BY total_paise DESC;
            """, new { startDate }).ToList();

        var paymentTotal = paymentRows.Sum(row => row.TotalPaise);
        var payments = paymentRows.Select((row, index) => new MixRow
        {
            Label = row.Label,
            Count = row.Count,
            TotalPaise = row.TotalPaise,
            Share = paymentTotal == 0 ? 0 : (double)row.TotalPaise / paymentTotal,
            Color = MixColors[index % MixColors.Length]
        }).ToList();

        var categoryRows = connection.Query<MixRaw>(
            """
            SELECT COALESCE(c.name, 'Unmapped') AS label,
                   COUNT(DISTINCT o.id) AS count,
                   SUM(oi.line_total_paise) AS total_paise
            FROM order_items oi
            JOIN orders o ON o.id = oi.order_id
            LEFT JOIN menu_items m ON m.id = oi.menu_item_id
            LEFT JOIN categories c ON c.id = m.category_id
            WHERE o.status = 'paid' AND date(o.paid_at) >= @startDate
            GROUP BY label
            ORDER BY total_paise DESC;
            """, new { startDate }).ToList();

        var categoryTotal = categoryRows.Sum(row => row.TotalPaise);
        var categories = categoryRows.Select((row, index) => new MixRow
        {
            Label = row.Label,
            Count = row.Count,
            TotalPaise = row.TotalPaise,
            Share = categoryTotal == 0 ? 0 : (double)row.TotalPaise / categoryTotal,
            Color = MixColors[index % MixColors.Length]
        }).ToList();

        var topRows = connection.Query<TopItemRaw>(
            """
            SELECT oi.name AS name,
                   SUM(oi.qty) AS quantity,
                   SUM(oi.line_total_paise) AS revenue_paise,
                   COALESCE(MAX(c.name), '') AS category_name
            FROM order_items oi
            JOIN orders o ON o.id = oi.order_id
            LEFT JOIN menu_items m ON m.id = oi.menu_item_id
            LEFT JOIN categories c ON c.id = m.category_id
            WHERE o.status = 'paid' AND date(o.paid_at) >= @startDate
            GROUP BY oi.name
            ORDER BY quantity DESC, revenue_paise DESC
            LIMIT 8;
            """, new { startDate }).ToList();

        var maxQty = topRows.Count == 0 ? 1 : topRows.Max(item => item.Quantity);
        var top = topRows.Select(item => new TopItemRow
        {
            Name = item.Name,
            Quantity = item.Quantity,
            RevenuePaise = item.RevenuePaise,
            CategoryName = item.CategoryName,
            Share = (double)item.Quantity / maxQty
        }).ToList();

        return new ReportSnapshot
        {
            Days = days,
            SalesPaise = agg.SalesPaise,
            OrderCount = agg.OrderCount,
            PaidOrders = agg.OrderCount,
            AverageBillPaise = agg.OrderCount == 0 ? 0 : agg.SalesPaise / agg.OrderCount,
            TaxPaise = agg.TaxPaise,
            CgstPaise = agg.CgstPaise,
            SgstPaise = agg.SgstPaise,
            Daily = daily,
            PaymentMix = payments,
            CategoryMix = categories,
            TopItems = top
        };
    }

    private sealed class DashboardAgg
    {
        public long SalesPaise { get; set; }
        public int OrderCount { get; set; }
        public long TaxPaise { get; set; }
    }

    private sealed class HourAgg
    {
        public string? Hour { get; set; }
        public long TotalPaise { get; set; }
    }

    private sealed class TopItemRaw
    {
        public string Name { get; set; } = "";
        public int Quantity { get; set; }
        public long RevenuePaise { get; set; }
        public string CategoryName { get; set; } = "";
    }

    private sealed class RecentBillRaw
    {
        public string BillNo { get; set; } = "";
        public long TotalPaise { get; set; }
        public string PaymentMethod { get; set; } = "";
        public string TableLabel { get; set; } = "";
        public DateTime PaidAt { get; set; }
    }

    private sealed class ReportAgg
    {
        public long SalesPaise { get; set; }
        public int OrderCount { get; set; }
        public long TaxPaise { get; set; }
        public long CgstPaise { get; set; }
        public long SgstPaise { get; set; }
    }

    private sealed class DailyAgg
    {
        public string Day { get; set; } = "";
        public long TotalPaise { get; set; }
    }

    private sealed class MixRaw
    {
        public string Label { get; set; } = "";
        public int Count { get; set; }
        public long TotalPaise { get; set; }
    }
}
