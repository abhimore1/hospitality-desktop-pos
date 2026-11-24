using Dapper;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Domain;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Services;
using Microsoft.Data.Sqlite;

namespace Hospitality.Pos.Desktop.Repositories;

public sealed class OrderRepository
{
    private readonly PosDatabase _database;
    private readonly SettingsRepository _settings;
    private readonly AppSession _session;
    private readonly DataChangeNotifier _notifier;

    public OrderRepository(
        PosDatabase database,
        SettingsRepository settings,
        AppSession session,
        DataChangeNotifier notifier)
    {
        _database = database;
        _settings = settings;
        _session = session;
        _notifier = notifier;
    }

    public PosOrder? GetOrder(string orderId)
    {
        using var connection = _database.Open();
        return LoadOrder(connection, null, orderId, openOnly: false);
    }

    public PosOrder? GetOpenOrderForTable(int tableId)
    {
        using var connection = _database.Open();
        var orderId = connection.ExecuteScalar<string?>(
            """
            SELECT id FROM orders
            WHERE table_id = @tableId AND status = 'open'
            ORDER BY created_at DESC LIMIT 1;
            """, new { tableId });

        return orderId is null ? null : LoadOrder(connection, null, orderId, openOnly: true);
    }

    public List<PosOrder> GetKitchenOrders()
    {
        using var connection = _database.Open();
        var orders = connection.Query<PosOrder>(
            """
            SELECT o.*, COALESCE(t.name, '') AS table_name
            FROM orders o
            LEFT JOIN dining_tables t ON t.id = o.table_id
            WHERE o.status = 'open'
            ORDER BY o.created_at;
            """).ToList();

        foreach (var order in orders)
        {
            order.Items = LoadItems(connection, null, order.Id);
        }

        return orders;
    }

    public PosOrder CreateOpenOrder(
        string orderType,
        int? tableId,
        OrderTotals totals,
        IReadOnlyList<OrderLineInput> lines,
        string note = "",
        string customerName = "",
        string customerPhone = "",
        string customerGstin = "")
    {
        var now = DateTime.Now;
        var orderId = Guid.NewGuid().ToString();
        var actor = _session.CurrentStaff?.Name ?? "System";

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var billNo = NextSequence(connection, transaction, "kot_sequence", "KOT-", 4);
        var tableName = GetTableName(connection, transaction, tableId);

        connection.Execute(
            """
            INSERT INTO orders
                (id, bill_no, order_type, table_id, status, kot_status, customer_name, customer_phone, customer_gstin,
                 cashier_name, terminal, subtotal_paise, discount_paise, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                 total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                 created_at, paid_at, synced)
            VALUES
                (@id, @billNo, @orderType, @tableId, 'open', 'new', @customerName, @customerPhone, @customerGstin,
                 @cashier, @terminal, @subtotal, @discount, @tax, @cgst, @sgst, @roundOff,
                 @total, @gstRate, @inclusive, '', @note,
                 @createdAt, NULL, 0);
            """,
            new
            {
                id = orderId,
                billNo,
                orderType,
                tableId,
                customerName,
                customerPhone,
                customerGstin,
                cashier = actor,
                terminal = _session.TerminalName,
                subtotal = totals.TaxablePaise,
                discount = totals.DiscountPaise,
                tax = totals.TaxPaise,
                cgst = totals.CgstPaise,
                sgst = totals.SgstPaise,
                roundOff = totals.RoundOffPaise,
                total = totals.TotalPaise,
                gstRate = _settings.GetInt("gst_rate_bps", 500),
                inclusive = _settings.GetBool("prices_include_tax", true),
                note,
                createdAt = now
            }, transaction);

        InsertItems(connection, transaction, orderId, lines);
        OutboxRepository.Enqueue(
            connection, transaction, "order", orderId, "upsert",
            $$"""{"id":"{{orderId}}","status":"open","tableId":{{(tableId is null ? "null" : tableId.ToString())}}}""");
        AuditRepository.Write(connection, transaction, actor, "order.kot", $"{billNo} sent to kitchen.");

        transaction.Commit();
        _notifier.Notify();

        return new PosOrder
        {
            Id = orderId,
            BillNo = billNo,
            OrderType = orderType,
            TableId = tableId,
            TableName = tableName,
            Status = "open",
            KotStatus = "new",
            CashierName = actor,
            Terminal = _session.TerminalName,
            SubtotalPaise = totals.TaxablePaise,
            DiscountPaise = totals.DiscountPaise,
            TaxPaise = totals.TaxPaise,
            CgstPaise = totals.CgstPaise,
            SgstPaise = totals.SgstPaise,
            RoundOffPaise = totals.RoundOffPaise,
            TotalPaise = totals.TotalPaise,
            CreatedAt = now,
            Items = lines.Select(line => new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                MenuItemId = line.MenuItemId,
                Name = line.Name,
                Qty = line.Qty,
                UnitPricePaise = line.UnitPricePaise,
                TaxBps = line.TaxBps,
                LineTotalPaise = line.LineTotalPaise,
                IsVeg = line.IsVeg
            }).ToList()
        };
    }

    public PosOrder CreatePaidOrder(
        string orderType,
        int? tableId,
        OrderTotals totals,
        IReadOnlyList<OrderLineInput> lines,
        string paymentMethod,
        string note = "",
        IReadOnlyList<PaymentSplitInput>? splitPayments = null,
        string customerName = "",
        string customerPhone = "",
        string customerGstin = "",
        string discountReason = "",
        string discountApprover = "",
        string roomNumber = "")
    {
        var now = DateTime.Now;
        var orderId = Guid.NewGuid().ToString();
        var actor = _session.CurrentStaff?.Name ?? "System";

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var billNo = NextBillNo(connection, transaction);
        var tableName = GetTableName(connection, transaction, tableId);
        var effectivePayment = splitPayments is not null && splitPayments.Count > 1
            ? string.Join(" + ", splitPayments.Select(p => p.Method))
            : paymentMethod;

        connection.Execute(
            """
            INSERT INTO orders
                (id, bill_no, order_type, table_id, status, kot_status, customer_name, customer_phone, customer_gstin,
                 cashier_name, terminal, subtotal_paise, discount_paise, discount_reason, discount_approver,
                 reprint_count, room_number, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                 total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                 created_at, paid_at, synced)
            VALUES
                (@id, @billNo, @orderType, @tableId, 'paid', 'served', @customerName, @customerPhone, @customerGstin,
                 @cashier, @terminal, @subtotal, @discount, @discountReason, @discountApprover,
                 0, @roomNumber, @tax, @cgst, @sgst, @roundOff,
                 @total, @gstRate, @inclusive, @payment, @note,
                 @createdAt, @paidAt, 0);
            """,
            new
            {
                id = orderId,
                billNo,
                orderType,
                tableId,
                customerName,
                customerPhone,
                customerGstin,
                cashier = actor,
                terminal = _session.TerminalName,
                subtotal = totals.TaxablePaise,
                discount = totals.DiscountPaise,
                discountReason,
                discountApprover,
                roomNumber,
                tax = totals.TaxPaise,
                cgst = totals.CgstPaise,
                sgst = totals.SgstPaise,
                roundOff = totals.RoundOffPaise,
                total = totals.TotalPaise,
                gstRate = _settings.GetInt("gst_rate_bps", 500),
                inclusive = _settings.GetBool("prices_include_tax", true),
                payment = effectivePayment,
                note,
                createdAt = now,
                paidAt = now
            }, transaction);

        InsertItems(connection, transaction, orderId, lines);
        var recordedPayments = RecordPayments(connection, transaction, orderId, paymentMethod, totals.TotalPaise, splitPayments, now);

        if (!string.IsNullOrWhiteSpace(roomNumber))
        {
            connection.Execute(
                "UPDATE hotel_rooms SET folio_balance_paise = folio_balance_paise + @total WHERE room_number = @roomNumber;",
                new { total = totals.TotalPaise, roomNumber }, transaction);
        }

        OutboxRepository.Enqueue(
            connection, transaction, "order", orderId, "upsert",
            $$"""{"id":"{{orderId}}","status":"paid","total":{{totals.TotalPaise}}}""");

        if (totals.DiscountPaise > 0)
        {
            AuditRepository.Write(connection, transaction, actor, "bill.discount",
                $"{billNo} discount of {Inr.Format(totals.DiscountPaise)} (Reason: {discountReason}, Approver: {discountApprover}).");
        }

        AuditRepository.Write(connection, transaction, actor, "bill.settled", $"{billNo} paid via {effectivePayment}.");

        transaction.Commit();
        _notifier.Notify();

        return new PosOrder
        {
            Id = orderId,
            BillNo = billNo,
            OrderType = orderType,
            TableId = tableId,
            TableName = tableName,
            Status = "paid",
            KotStatus = "served",
            CustomerName = customerName,
            CustomerPhone = customerPhone,
            CustomerGstin = customerGstin,
            CashierName = actor,
            Terminal = _session.TerminalName,
            SubtotalPaise = totals.TaxablePaise,
            DiscountPaise = totals.DiscountPaise,
            DiscountReason = discountReason,
            DiscountApprover = discountApprover,
            ReprintCount = 0,
            RoomNumber = roomNumber,
            TaxPaise = totals.TaxPaise,
            CgstPaise = totals.CgstPaise,
            SgstPaise = totals.SgstPaise,
            RoundOffPaise = totals.RoundOffPaise,
            TotalPaise = totals.TotalPaise,
            PaymentMethod = effectivePayment,
            CreatedAt = now,
            PaidAt = now,
            Payments = recordedPayments,
            Items = lines.Select(line => new OrderItem
            {
                Id = Guid.NewGuid().ToString(),
                OrderId = orderId,
                MenuItemId = line.MenuItemId,
                Name = line.Name,
                Qty = line.Qty,
                UnitPricePaise = line.UnitPricePaise,
                TaxBps = line.TaxBps,
                LineTotalPaise = line.LineTotalPaise,
                IsVeg = line.IsVeg,
                Station = line.Station,
                Course = line.Course,
                Status = "active"
            }).ToList()
        };
    }

    public PosOrder? PayOpenOrder(
        string orderId,
        string paymentMethod,
        OrderTotals totals,
        IReadOnlyList<OrderLineInput> lines,
        IReadOnlyList<PaymentSplitInput>? splitPayments = null,
        string customerName = "",
        string customerPhone = "",
        string customerGstin = "",
        string discountReason = "",
        string discountApprover = "",
        string roomNumber = "")
    {
        var actor = _session.CurrentStaff?.Name ?? "System";

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var order = LoadOrder(connection, transaction, orderId, openOnly: true);
        if (order is null)
        {
            transaction.Rollback();
            return null;
        }

        var billNo = NextBillNo(connection, transaction);
        var paidAt = DateTime.Now;
        var effectivePayment = splitPayments is not null && splitPayments.Count > 1
            ? string.Join(" + ", splitPayments.Select(p => p.Method))
            : paymentMethod;

        connection.Execute(
            """
            UPDATE orders
            SET bill_no = @billNo, status = 'paid', kot_status = 'served',
                customer_name = @customerName, customer_phone = @customerPhone, customer_gstin = @customerGstin,
                payment_method = @payment, paid_at = @paidAt,
                subtotal_paise = @subtotal, discount_paise = @discount,
                discount_reason = @discountReason, discount_approver = @discountApprover,
                room_number = @roomNumber,
                tax_paise = @tax, cgst_paise = @cgst, sgst_paise = @sgst,
                round_off_paise = @roundOff, total_paise = @total
            WHERE id = @orderId;
            """,
            new
            {
                billNo,
                customerName,
                customerPhone,
                customerGstin,
                payment = effectivePayment,
                paidAt,
                subtotal = totals.TaxablePaise,
                discount = totals.DiscountPaise,
                discountReason,
                discountApprover,
                roomNumber,
                tax = totals.TaxPaise,
                cgst = totals.CgstPaise,
                sgst = totals.SgstPaise,
                roundOff = totals.RoundOffPaise,
                total = totals.TotalPaise,
                orderId
            }, transaction);

        connection.Execute("DELETE FROM order_items WHERE order_id = @orderId;", new { orderId }, transaction);
        InsertItems(connection, transaction, orderId, lines);
        var recordedPayments = RecordPayments(connection, transaction, orderId, paymentMethod, totals.TotalPaise, splitPayments, paidAt);

        if (!string.IsNullOrWhiteSpace(roomNumber))
        {
            connection.Execute(
                "UPDATE hotel_rooms SET folio_balance_paise = folio_balance_paise + @total WHERE room_number = @roomNumber;",
                new { total = totals.TotalPaise, roomNumber }, transaction);
        }

        OutboxRepository.Enqueue(
            connection, transaction, "order", orderId, "upsert",
            $$"""{"id":"{{orderId}}","status":"paid","total":{{totals.TotalPaise}}}""");

        if (totals.DiscountPaise > 0)
        {
            AuditRepository.Write(connection, transaction, actor, "bill.discount",
                $"{billNo} discount of {Inr.Format(totals.DiscountPaise)} (Reason: {discountReason}, Approver: {discountApprover}).");
        }

        AuditRepository.Write(connection, transaction, actor, "bill.settled", $"{billNo} paid via {effectivePayment}.");

        transaction.Commit();
        _notifier.Notify();

        order.BillNo = billNo;
        order.Status = "paid";
        order.KotStatus = "served";
        order.CustomerName = customerName;
        order.CustomerPhone = customerPhone;
        order.CustomerGstin = customerGstin;
        order.PaymentMethod = effectivePayment;
        order.PaidAt = paidAt;
        order.SubtotalPaise = totals.TaxablePaise;
        order.DiscountPaise = totals.DiscountPaise;
        order.DiscountReason = discountReason;
        order.DiscountApprover = discountApprover;
        order.RoomNumber = roomNumber;
        order.TaxPaise = totals.TaxPaise;
        order.CgstPaise = totals.CgstPaise;
        order.SgstPaise = totals.SgstPaise;
        order.RoundOffPaise = totals.RoundOffPaise;
        order.TotalPaise = totals.TotalPaise;
        order.Payments = recordedPayments;
        order.Items = lines.Select(line => new OrderItem
        {
            Id = Guid.NewGuid().ToString(),
            OrderId = orderId,
            MenuItemId = line.MenuItemId,
            Name = line.Name,
            Qty = line.Qty,
            UnitPricePaise = line.UnitPricePaise,
            TaxBps = line.TaxBps,
            LineTotalPaise = line.LineTotalPaise,
            IsVeg = line.IsVeg,
            Station = line.Station,
            Course = line.Course,
            Status = "active"
        }).ToList();
        return order;
    }

    public void AdvanceKitchenStatus(string orderId, string nextStatus)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        connection.Execute(
            "UPDATE orders SET kot_status = @nextStatus WHERE id = @orderId AND status = 'open';",
            new { nextStatus, orderId }, transaction);
        OutboxRepository.Enqueue(
            connection, transaction, "order", orderId, "update",
            $$"""{"id":"{{orderId}}","kotStatus":"{{nextStatus}}"}""");
        transaction.Commit();
        _notifier.Notify();
    }

    private PosOrder? LoadOrder(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string orderId,
        bool openOnly)
    {
        var sql =
            """
            SELECT o.*, COALESCE(t.name, '') AS table_name
            FROM orders o
            LEFT JOIN dining_tables t ON t.id = o.table_id
            WHERE o.id = @orderId
            """;
        if (openOnly)
        {
            sql += " AND o.status = 'open'";
        }

        var order = connection.QuerySingleOrDefault<PosOrder>(sql, new { orderId }, transaction);
        if (order is not null)
        {
            order.Items = LoadItems(connection, transaction, order.Id);
            order.Payments = LoadPayments(connection, transaction, order.Id);
        }

        return order;
    }

    private static List<OrderPayment> LoadPayments(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string orderId) =>
        connection.Query<OrderPayment>(
            """
            SELECT * FROM order_payments
            WHERE order_id = @orderId
            ORDER BY created_at;
            """, new { orderId }, transaction).ToList();

    private static List<OrderPayment> RecordPayments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string orderId,
        string paymentMethod,
        long totalPaise,
        IReadOnlyList<PaymentSplitInput>? splitPayments,
        DateTime now)
    {
        var result = new List<OrderPayment>();
        if (splitPayments is not null && splitPayments.Count > 0)
        {
            foreach (var split in splitPayments)
            {
                var pid = Guid.NewGuid().ToString();
                connection.Execute(
                    """
                    INSERT INTO order_payments (id, order_id, method, amount_paise, reference, created_at)
                    VALUES (@pid, @orderId, @method, @amount, @reference, @createdAt);
                    """,
                    new
                    {
                        pid,
                        orderId,
                        method = split.Method,
                        amount = split.AmountPaise,
                        reference = split.Reference,
                        createdAt = now
                    }, transaction);

                result.Add(new OrderPayment
                {
                    Id = pid,
                    OrderId = orderId,
                    Method = split.Method,
                    AmountPaise = split.AmountPaise,
                    Reference = split.Reference,
                    CreatedAt = now
                });
            }
        }
        else
        {
            var pid = Guid.NewGuid().ToString();
            connection.Execute(
                """
                INSERT INTO order_payments (id, order_id, method, amount_paise, reference, created_at)
                VALUES (@pid, @orderId, @method, @amount, '', @createdAt);
                """,
                new
                {
                    pid,
                    orderId,
                    method = paymentMethod,
                    amount = totalPaise,
                    createdAt = now
                }, transaction);

            result.Add(new OrderPayment
            {
                Id = pid,
                OrderId = orderId,
                Method = paymentMethod,
                AmountPaise = totalPaise,
                CreatedAt = now
            });
        }

        return result;
    }

    private static List<OrderItem> LoadItems(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string orderId) =>
        connection.Query<OrderItem>(
            """
            SELECT oi.*, COALESCE(m.is_veg, 1) AS is_veg
            FROM order_items oi
            LEFT JOIN menu_items m ON m.id = oi.menu_item_id
            WHERE oi.order_id = @orderId
            ORDER BY oi.rowid;
            """, new { orderId }, transaction).ToList();

    private static void InsertItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string orderId,
        IReadOnlyList<OrderLineInput> lines)
    {
        foreach (var line in lines)
        {
            connection.Execute(
                """
                INSERT INTO order_items
                    (id, order_id, menu_item_id, name, qty, unit_price_paise, tax_bps, line_total_paise, station, course, status)
                VALUES (@id, @orderId, @menuItemId, @name, @qty, @unitPrice, @taxBps, @lineTotal, @station, @course, 'active');
                """,
                new
                {
                    id = Guid.NewGuid().ToString(),
                    orderId,
                    menuItemId = line.MenuItemId,
                    name = line.Name,
                    qty = line.Qty,
                    unitPrice = line.UnitPricePaise,
                    taxBps = line.TaxBps,
                    lineTotal = line.LineTotalPaise,
                    station = string.IsNullOrWhiteSpace(line.Station) ? "Kitchen" : line.Station,
                    course = string.IsNullOrWhiteSpace(line.Course) ? "Main" : line.Course
                }, transaction);
        }
    }

    public void LogReprint(string orderId, string actor)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            "UPDATE orders SET reprint_count = reprint_count + 1 WHERE id = @orderId;",
            new { orderId }, transaction);

        var order = connection.QuerySingleOrDefault<PosOrder>(
            "SELECT bill_no, reprint_count FROM orders WHERE id = @orderId;",
            new { orderId }, transaction);

        if (order is not null)
        {
            AuditRepository.Write(connection, transaction, actor, "bill.reprint",
                $"Duplicate bill reprint #{order.ReprintCount} for {order.BillNo}.");
        }

        transaction.Commit();
        _notifier.Notify();
    }

    public bool MergeTables(int sourceTableId, int targetTableId, out string message)
    {
        var actor = _session.CurrentStaff?.Name ?? "System";

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var sourceOrderId = connection.ExecuteScalar<string?>(
            "SELECT id FROM orders WHERE table_id = @id AND status = 'open' ORDER BY created_at DESC LIMIT 1;",
            new { id = sourceTableId }, transaction);

        var targetOrderId = connection.ExecuteScalar<string?>(
            "SELECT id FROM orders WHERE table_id = @id AND status = 'open' ORDER BY created_at DESC LIMIT 1;",
            new { id = targetTableId }, transaction);

        if (string.IsNullOrEmpty(sourceOrderId))
        {
            message = "Source table has no active order to merge.";
            return false;
        }

        var sourceOrder = LoadOrder(connection, transaction, sourceOrderId, openOnly: true);
        if (sourceOrder is null)
        {
            message = "Source order not found.";
            return false;
        }

        var targetOrder = string.IsNullOrEmpty(targetOrderId)
            ? null
            : LoadOrder(connection, transaction, targetOrderId, openOnly: true);

        if (targetOrder is null)
        {
            // Just move the source table order to the target table
            var targetTableName = GetTableName(connection, transaction, targetTableId);
            connection.Execute(
                "UPDATE orders SET table_id = @targetTableId WHERE id = @sourceOrderId;",
                new { targetTableId, sourceOrderId }, transaction);

            AuditRepository.Write(connection, transaction, actor, "table.transferred",
                $"{sourceOrder.BillNo} transferred from Table {sourceOrder.TableName} to Table {targetTableName}.");

            transaction.Commit();
            _notifier.Notify();
            message = $"Transferred to Table {targetTableName}.";
            return true;
        }

        // Both tables have open orders: merge source order items into target order
        connection.Execute(
            "UPDATE order_items SET order_id = @targetOrderId WHERE order_id = @sourceOrderId;",
            new { targetOrderId = targetOrder.Id, sourceOrderId = sourceOrder.Id }, transaction);

        var targetItems = LoadItems(connection, transaction, targetOrder.Id);
        var activeItems = targetItems.Where(i => i.Status == "active").ToList();
        var taxLines = activeItems.Select(ai => new TaxLine(ai.UnitPricePaise, ai.Qty, ai.TaxBps)).ToList();
        var inclusive = _settings.GetBool("prices_include_tax", true);
        var totals = TaxCalculator.Calculate(taxLines, 0, inclusive);

        connection.Execute(
            """
            UPDATE orders
            SET subtotal_paise = @subtotal, discount_paise = @discount,
                tax_paise = @tax, cgst_paise = @cgst, sgst_paise = @sgst,
                round_off_paise = @roundOff, total_paise = @total
            WHERE id = @orderId;
            """,
            new
            {
                orderId = targetOrder.Id,
                subtotal = totals.TaxablePaise,
                discount = totals.DiscountPaise,
                tax = totals.TaxPaise,
                cgst = totals.CgstPaise,
                sgst = totals.SgstPaise,
                roundOff = totals.RoundOffPaise,
                total = totals.TotalPaise
            }, transaction);

        // Mark source order as merged
        connection.Execute(
            "UPDATE orders SET status = 'merged', table_id = NULL, note = @note WHERE id = @sourceOrderId;",
            new { sourceOrderId = sourceOrder.Id, note = $"Merged into {targetOrder.BillNo} (Table {targetOrder.TableName})" },
            transaction);

        AuditRepository.Write(connection, transaction, actor, "table.merged",
            $"Merged Table {sourceOrder.TableName} ({sourceOrder.BillNo}) into Table {targetOrder.TableName} ({targetOrder.BillNo}). Combined total: {Inr.Format(totals.TotalPaise)}.");

        transaction.Commit();
        _notifier.Notify();
        message = $"Merged Table {sourceOrder.TableName} into Table {targetOrder.TableName}.";
        return true;
    }

    public bool CancelOrderItem(string orderId, string itemId, string reason, string approver, out string message)
    {
        var actor = _session.CurrentStaff?.Name ?? "System";

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var item = connection.QuerySingleOrDefault<OrderItem>(
            "SELECT * FROM order_items WHERE id = @itemId AND order_id = @orderId;",
            new { itemId, orderId }, transaction);

        if (item is null)
        {
            message = "Item not found in order.";
            return false;
        }

        connection.Execute(
            """
            UPDATE order_items
            SET status = 'cancelled', cancel_reason = @reason, cancel_approver = @approver
            WHERE id = @itemId;
            """,
            new { itemId, reason, approver }, transaction);

        var activeItems = connection.Query<OrderItem>(
            "SELECT * FROM order_items WHERE order_id = @orderId AND status = 'active';",
            new { orderId }, transaction).ToList();

        var inclusive = _settings.GetBool("prices_include_tax", true);
        var taxLines = activeItems.Select(ai => new TaxLine(ai.UnitPricePaise, ai.Qty, ai.TaxBps)).ToList();
        var totals = TaxCalculator.Calculate(taxLines, 0, inclusive);

        connection.Execute(
            """
            UPDATE orders
            SET subtotal_paise = @subtotal, discount_paise = @discount,
                tax_paise = @tax, cgst_paise = @cgst, sgst_paise = @sgst,
                round_off_paise = @roundOff, total_paise = @total
            WHERE id = @orderId;
            """,
            new
            {
                orderId,
                subtotal = totals.TaxablePaise,
                discount = totals.DiscountPaise,
                tax = totals.TaxPaise,
                cgst = totals.CgstPaise,
                sgst = totals.SgstPaise,
                roundOff = totals.RoundOffPaise,
                total = totals.TotalPaise
            }, transaction);

        var billNo = connection.ExecuteScalar<string>("SELECT bill_no FROM orders WHERE id = @orderId;", new { orderId }, transaction);

        AuditRepository.Write(connection, transaction, actor, "kot.item_cancelled",
            $"{item.Qty}x {item.Name} cancelled on {billNo} by {approver} (Reason: {reason}, Wastage: {Inr.Format(item.LineTotalPaise)}).");

        transaction.Commit();
        _notifier.Notify();
        message = $"{item.Name} cancelled.";
        return true;
    }

    public List<HotelRoom> GetHotelRooms()
    {
        using var connection = _database.Open();
        return connection.Query<HotelRoom>(
            "SELECT * FROM hotel_rooms ORDER BY room_number;").ToList();
    }

    public void RecordDayEndClosure(DayEndClosure closure)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            """
            INSERT INTO day_end_closures
                (closure_date, closed_by, total_sales_paise, total_tax_paise, total_discount_paise,
                 system_cash_paise, counted_cash_paise, cash_variance_paise, denominations_json,
                 unsynced_count, open_orders_count, created_at, notes)
            VALUES
                (@ClosureDate, @ClosedBy, @TotalSalesPaise, @TotalTaxPaise, @TotalDiscountPaise,
                 @SystemCashPaise, @CountedCashPaise, @CashVariancePaise, @DenominationsJson,
                 @UnsyncedCount, @OpenOrdersCount, @CreatedAt, @Notes);
            """, closure, transaction);

        AuditRepository.Write(connection, transaction, closure.ClosedBy, "dayend.closure",
            $"Day-end closed for {closure.ClosureDate}: Sales {closure.TotalSalesText}, Variance {closure.CashVarianceText}.");

        transaction.Commit();
        _notifier.Notify();
    }

    public DayEndClosure GetDayEndSummary(DateTime date, string actor)
    {
        using var connection = _database.Open();
        var dateStr = date.ToString("yyyy-MM-dd");

        var salesAgg = connection.QuerySingle<(long Sales, long Tax, long Discount)>(
            """
            SELECT COALESCE(SUM(total_paise), 0) AS Sales,
                   COALESCE(SUM(tax_paise), 0) AS Tax,
                   COALESCE(SUM(discount_paise), 0) AS Discount
            FROM orders
            WHERE status = 'paid' AND date(paid_at) = @dateStr;
            """, new { dateStr });

        var cashSales = connection.ExecuteScalar<long>(
            """
            SELECT COALESCE(SUM(amount_paise), 0)
            FROM order_payments op
            JOIN orders o ON o.id = op.order_id
            WHERE o.status = 'paid' AND date(o.paid_at) = @dateStr AND op.method = 'Cash';
            """, new { dateStr });

        // Fallback for orders that might have payment_method = 'Cash' but no payments row
        if (cashSales == 0)
        {
            cashSales = connection.ExecuteScalar<long>(
                """
                SELECT COALESCE(SUM(total_paise), 0)
                FROM orders
                WHERE status = 'paid' AND date(paid_at) = @dateStr AND payment_method LIKE '%Cash%';
                """, new { dateStr });
        }

        var openOrdersCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM orders WHERE status = 'open';");

        var unsyncedCount = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sync_outbox WHERE synced = 0;");

        return new DayEndClosure
        {
            ClosureDate = dateStr,
            ClosedBy = actor,
            TotalSalesPaise = salesAgg.Sales,
            TotalTaxPaise = salesAgg.Tax,
            TotalDiscountPaise = salesAgg.Discount,
            SystemCashPaise = cashSales,
            CountedCashPaise = cashSales,
            CashVariancePaise = 0,
            OpenOrdersCount = openOrdersCount,
            UnsyncedCount = unsyncedCount,
            CreatedAt = DateTime.Now
        };
    }

    public List<LeakageEvent> GetLeakageReport(int days)
    {
        using var connection = _database.Open();
        var startDate = DateTime.Today.AddDays(-days).ToString("yyyy-MM-dd");

        var events = new List<LeakageEvent>();

        // 1. Discounts
        var discounts = connection.Query<PosOrder>(
            """
            SELECT bill_no, cashier_name, total_paise, discount_paise, discount_reason, discount_approver, paid_at
            FROM orders
            WHERE status = 'paid' AND discount_paise > 0 AND date(paid_at) >= @startDate
            ORDER BY paid_at DESC;
            """, new { startDate });

        foreach (var d in discounts)
        {
            events.Add(new LeakageEvent
            {
                EventType = "Discount",
                Actor = d.CashierName,
                BillNo = d.BillNo,
                AmountPaise = d.DiscountPaise,
                Reason = string.IsNullOrWhiteSpace(d.DiscountReason) ? "Manager Discretion" : d.DiscountReason,
                Approver = string.IsNullOrWhiteSpace(d.DiscountApprover) ? d.CashierName : d.DiscountApprover,
                Detail = $"Discount on bill {d.BillNo}",
                Timestamp = d.PaidAt ?? DateTime.Now
            });
        }

        // 2. Reprints and Voids from audit log
        var audits = connection.Query<AuditEntryRaw>(
            """
            SELECT actor, action, detail, created_at
            FROM audit_log
            WHERE date(created_at) >= @startDate
              AND (action LIKE '%reprint%' OR action LIKE '%cancel%' OR action LIKE '%void%')
            ORDER BY created_at DESC;
            """, new { startDate });

        foreach (var a in audits)
        {
            var isReprint = a.Action.Contains("reprint", StringComparison.OrdinalIgnoreCase);
            events.Add(new LeakageEvent
            {
                EventType = isReprint ? "Reprint" : "Cancelled / Void",
                Actor = a.Actor,
                BillNo = "",
                AmountPaise = 0,
                Reason = a.Action,
                Approver = a.Actor,
                Detail = a.Detail,
                Timestamp = DateTime.TryParse(a.CreatedAt, out var ts) ? ts : DateTime.Now
            });
        }

        return events.OrderByDescending(e => e.Timestamp).ToList();
    }

    private sealed class AuditEntryRaw
    {
        public string Actor { get; set; } = "";
        public string Action { get; set; } = "";
        public string Detail { get; set; } = "";
        public string CreatedAt { get; set; } = "";
    }

    private string NextBillNo(SqliteConnection connection, SqliteTransaction transaction)
    {
        var prefix = connection.ExecuteScalar<string?>(
            "SELECT value FROM app_settings WHERE key = 'bill_prefix';", transaction: transaction) ?? "INV";
        var fy = DemoDataSeeder.FinancialYearLabel(DateTime.Today);
        var sequence = IncrementSetting(connection, transaction, "bill_sequence");
        return $"{prefix}/{fy}/{sequence:0000}";
    }

    private static string NextSequence(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string key,
        string prefix,
        int digits)
    {
        var sequence = IncrementSetting(connection, transaction, key);
        return prefix + sequence.ToString(new string('0', digits));
    }

    private static int IncrementSetting(SqliteConnection connection, SqliteTransaction transaction, string key)
    {
        var raw = connection.ExecuteScalar<string?>(
            "SELECT value FROM app_settings WHERE key = @key;", new { key }, transaction);
        var current = int.TryParse(raw, out var value) ? value : 0;
        var next = current + 1;

        connection.Execute(
            """
            INSERT INTO app_settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            new { key, value = next.ToString() }, transaction);

        return next;
    }

    private static string GetTableName(
        SqliteConnection connection,
        SqliteTransaction transaction,
        int? tableId)
    {
        if (tableId is null)
        {
            return "";
        }

        return connection.ExecuteScalar<string?>(
            "SELECT name FROM dining_tables WHERE id = @tableId;", new { tableId }, transaction) ?? "";
    }
}
