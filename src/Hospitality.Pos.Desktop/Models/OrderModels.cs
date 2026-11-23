namespace Hospitality.Pos.Desktop.Models;

public sealed class DiningTable
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Section { get; set; } = "";
    public int Seats { get; set; }
    public string Status { get; set; } = "available";
    public string? OpenOrderId { get; set; }
    public long OpenAmountPaise { get; set; }
    public DateTime? OpenedAt { get; set; }
    public bool IsOccupied => OpenOrderId is not null;
}

public sealed class PosOrder
{
    public string Id { get; set; } = "";
    public string BillNo { get; set; } = "";
    public string OrderType { get; set; } = "Dine-in";
    public int? TableId { get; set; }
    public string TableName { get; set; } = "";
    public string Status { get; set; } = "open";
    public string KotStatus { get; set; } = "new";
    public string CustomerName { get; set; } = "";
    public string CashierName { get; set; } = "";
    public string Terminal { get; set; } = "";
    public long SubtotalPaise { get; set; }
    public long DiscountPaise { get; set; }
    public long TaxPaise { get; set; }
    public long CgstPaise { get; set; }
    public long SgstPaise { get; set; }
    public long RoundOffPaise { get; set; }
    public long TotalPaise { get; set; }
    public int GstRateBps { get; set; } = 500;
    public bool PricesIncludeTax { get; set; } = true;
    public string PaymentMethod { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public bool Synced { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}

public sealed class OrderItem
{
    public string Id { get; set; } = "";
    public string OrderId { get; set; } = "";
    public int MenuItemId { get; set; }
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public long UnitPricePaise { get; set; }
    public int TaxBps { get; set; } = 500;
    public long LineTotalPaise { get; set; }
    public bool IsVeg { get; set; }
}

public sealed class BillLine
{
    public string Name { get; set; } = "";
    public int Qty { get; set; }
    public long UnitPricePaise { get; set; }
    public long LineTotalPaise { get; set; }
    public bool IsVeg { get; set; }
    public int TaxBps { get; set; }
    public string UnitPriceText => Services.Inr.Format(UnitPricePaise);
    public string LineTotalText => Services.Inr.Format(LineTotalPaise);
    public string TaxText => $"{TaxBps / 100m:0.##}% GST";
}

public sealed record OrderLineInput(
    int MenuItemId,
    string Name,
    int Qty,
    long UnitPricePaise,
    int TaxBps,
    bool IsVeg,
    long LineTotalPaise);

public sealed class BillDocument
{
    public string OutletName { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Gstin { get; set; } = "";
    public string Fssai { get; set; } = "";
    public string InvoiceNo { get; set; } = "";
    public string OrderType { get; set; } = "";
    public string TableLabel { get; set; } = "";
    public string Cashier { get; set; } = "";
    public string PaymentMethod { get; set; } = "";
    public DateTime PaidAt { get; set; }
    public int GstRateBps { get; set; }
    public long TaxablePaise { get; set; }
    public long DiscountPaise { get; set; }
    public long CgstPaise { get; set; }
    public long SgstPaise { get; set; }
    public long RoundOffPaise { get; set; }
    public long TotalPaise { get; set; }
    public List<BillLine> Lines { get; set; } = [];

    public string GstRateText => $"{GstRateBps / 100m:0.##}%";
    public string CgstLabel => $"CGST @ {GstRateBps / 200m:0.##}%";
    public string SgstLabel => $"SGST @ {GstRateBps / 200m:0.##}%";
    public string PaidAtText => PaidAt.ToString("dd MMM yyyy, h:mm tt");
    public string TableText => string.IsNullOrWhiteSpace(TableLabel) ? "—" : TableLabel;
    public string PaymentText => string.IsNullOrWhiteSpace(PaymentMethod) ? "Unpaid" : PaymentMethod;
    public bool HasDiscount => DiscountPaise > 0;
    public string ItemCountText => $"{Lines.Sum(line => line.Qty)} items";
}
