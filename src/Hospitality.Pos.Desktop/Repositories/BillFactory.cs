using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.Repositories;

public static class BillFactory
{
    public static BillDocument Create(PosOrder order, PosSettings settings) => new()
    {
        OutletName = settings.OutletName,
        Address = settings.Address,
        City = settings.City,
        Phone = settings.Phone,
        Gstin = settings.Gstin,
        Fssai = settings.Fssai,
        InvoiceNo = order.BillNo,
        OrderType = order.OrderType,
        TableLabel = order.TableName,
        Cashier = order.CashierName,
        PaymentMethod = order.PaymentMethod,
        PaidAt = order.PaidAt ?? DateTime.Now,
        GstRateBps = order.GstRateBps,
        TaxablePaise = order.SubtotalPaise,
        DiscountPaise = order.DiscountPaise,
        DiscountReason = order.DiscountReason,
        DiscountApprover = order.DiscountApprover,
        ReprintCount = order.ReprintCount,
        CustomerName = order.CustomerName,
        CustomerPhone = order.CustomerPhone,
        CustomerGstin = order.CustomerGstin,
        RoomNumber = order.RoomNumber,
        CgstPaise = order.CgstPaise,
        SgstPaise = order.SgstPaise,
        RoundOffPaise = order.RoundOffPaise,
        TotalPaise = order.TotalPaise,
        Payments = order.Payments ?? [],
        Lines = order.Items.Select(item => new BillLine
        {
            Name = item.Name,
            Qty = item.Qty,
            UnitPricePaise = item.UnitPricePaise,
            LineTotalPaise = item.LineTotalPaise,
            IsVeg = item.IsVeg,
            TaxBps = item.TaxBps
        }).ToList()
    };
}
