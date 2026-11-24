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
    public string CustomerPhone { get; set; } = "";
    public string CustomerGstin { get; set; } = "";
    public string CashierName { get; set; } = "";
    public string Terminal { get; set; } = "";
    public long SubtotalPaise { get; set; }
    public long DiscountPaise { get; set; }
    public string DiscountReason { get; set; } = "";
    public string DiscountApprover { get; set; } = "";
    public int ReprintCount { get; set; } = 0;
    public string RoomNumber { get; set; } = "";
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
    public List<OrderPayment> Payments { get; set; } = [];
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
    public string Station { get; set; } = "Kitchen";
    public string Status { get; set; } = "active";
    public string? CancelReason { get; set; }
    public string? CancelApprover { get; set; }
    public string Course { get; set; } = "Main";
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
    long LineTotalPaise,
    string Station = "Kitchen",
    string Course = "Main");

public sealed class OrderPayment
{
    public string Id { get; set; } = "";
    public string OrderId { get; set; } = "";
    public string Method { get; set; } = "";
    public long AmountPaise { get; set; }
    public string Reference { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string AmountText => Services.Inr.Format(AmountPaise);
}

public sealed record PaymentSplitInput(
    string Method,
    long AmountPaise,
    string Reference = "");

public sealed class HotelRoom
{
    public int Id { get; set; }
    public string RoomNumber { get; set; } = "";
    public string GuestName { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Status { get; set; } = "occupied";
    public long FolioBalancePaise { get; set; }
    public DateTime CheckIn { get; set; }
    public string FolioBalanceText => Services.Inr.Format(FolioBalancePaise);
    public string DisplayText => $"Room {RoomNumber} • {GuestName}";
    public bool IsOccupied => string.Equals(Status, "occupied", StringComparison.OrdinalIgnoreCase);
}

public sealed class DayEndClosure
{
    public int Id { get; set; }
    public string ClosureDate { get; set; } = "";
    public string ClosedBy { get; set; } = "";
    public long TotalSalesPaise { get; set; }
    public long TotalTaxPaise { get; set; }
    public long TotalDiscountPaise { get; set; }
    public long SystemCashPaise { get; set; }
    public long CountedCashPaise { get; set; }
    public long CashVariancePaise { get; set; }
    public string DenominationsJson { get; set; } = "{}";
    public int UnsyncedCount { get; set; }
    public int OpenOrdersCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public string Notes { get; set; } = "";

    public string TotalSalesText => Services.Inr.Format(TotalSalesPaise);
    public string TotalTaxText => Services.Inr.Format(TotalTaxPaise);
    public string TotalDiscountText => Services.Inr.Format(TotalDiscountPaise);
    public string SystemCashText => Services.Inr.Format(SystemCashPaise);
    public string CountedCashText => Services.Inr.Format(CountedCashPaise);
    public string CashVarianceText => Services.Inr.Format(CashVariancePaise);
    public bool IsBalanced => CashVariancePaise == 0;
}

public sealed class LeakageEvent
{
    public string EventType { get; set; } = ""; // Discount, Void, Reprint
    public string Actor { get; set; } = "";
    public string BillNo { get; set; } = "";
    public string Detail { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Approver { get; set; } = "";
    public long AmountPaise { get; set; }
    public DateTime Timestamp { get; set; }
    public string AmountText => AmountPaise > 0 ? Services.Inr.Format(AmountPaise) : "—";
    public string TimeText => Timestamp.ToString("dd MMM, h:mm tt");
}

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
    public string DiscountReason { get; set; } = "";
    public string DiscountApprover { get; set; } = "";
    public int ReprintCount { get; set; } = 0;
    public string CustomerName { get; set; } = "";
    public string CustomerPhone { get; set; } = "";
    public string CustomerGstin { get; set; } = "";
    public string RoomNumber { get; set; } = "";
    public long CgstPaise { get; set; }
    public long SgstPaise { get; set; }
    public long RoundOffPaise { get; set; }
    public long TotalPaise { get; set; }
    public List<BillLine> Lines { get; set; } = [];
    public List<OrderPayment> Payments { get; set; } = [];

    public string GstRateText => $"{GstRateBps / 100m:0.##}%";
    public string CgstLabel => $"CGST @ {GstRateBps / 200m:0.##}%";
    public string SgstLabel => $"SGST @ {GstRateBps / 200m:0.##}%";
    public string PaidAtText => PaidAt.ToString("dd MMM yyyy, h:mm tt");
    public string TableText => string.IsNullOrWhiteSpace(TableLabel) ? "—" : TableLabel;
    public string PaymentText => string.IsNullOrWhiteSpace(PaymentMethod) ? "Unpaid" : PaymentMethod;
    public bool HasDiscount => DiscountPaise > 0;
    public string ItemCountText => $"{Lines.Sum(line => line.Qty)} items";
    public bool IsDuplicate => ReprintCount > 0;
    public string DuplicateNotice => IsDuplicate ? $"*** DUPLICATE INVOICE (Reprint #{ReprintCount}) ***" : "";
    public bool HasCustomerPhone => !string.IsNullOrWhiteSpace(CustomerPhone);
    public bool HasCustomerGstin => !string.IsNullOrWhiteSpace(CustomerGstin);
    public bool HasCustomerInfo => HasCustomerPhone || HasCustomerGstin;
    public bool HasRoomNumber => !string.IsNullOrWhiteSpace(RoomNumber);
    public bool HasMultiplePayments => Payments.Count > 1;
}
