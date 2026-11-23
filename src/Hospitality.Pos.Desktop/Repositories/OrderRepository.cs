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
        string note = "")
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
                (id, bill_no, order_type, table_id, status, kot_status, cashier_name, terminal,
                 subtotal_paise, discount_paise, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                 total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                 created_at, paid_at, synced)
            VALUES
                (@id, @billNo, @orderType, @tableId, 'open', 'new', @cashier, @terminal,
                 @subtotal, @discount, @tax, @cgst, @sgst, @roundOff,
                 @total, @gstRate, @inclusive, '', @note,
                 @createdAt, NULL, 0);
            """,
            new
            {
                id = orderId,
                billNo,
                orderType,
                tableId,
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
        string note = "")
    {
        var now = DateTime.Now;
        var orderId = Guid.NewGuid().ToString();
        var actor = _session.CurrentStaff?.Name ?? "System";

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var billNo = NextBillNo(connection, transaction);
        var tableName = GetTableName(connection, transaction, tableId);

        connection.Execute(
            """
            INSERT INTO orders
                (id, bill_no, order_type, table_id, status, kot_status, cashier_name, terminal,
                 subtotal_paise, discount_paise, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                 total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                 created_at, paid_at, synced)
            VALUES
                (@id, @billNo, @orderType, @tableId, 'paid', 'served', @cashier, @terminal,
                 @subtotal, @discount, @tax, @cgst, @sgst, @roundOff,
                 @total, @gstRate, @inclusive, @payment, @note,
                 @createdAt, @paidAt, 0);
            """,
            new
            {
                id = orderId,
                billNo,
                orderType,
                tableId,
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
                payment = paymentMethod,
                note,
                createdAt = now,
                paidAt = now
            }, transaction);

        InsertItems(connection, transaction, orderId, lines);
        OutboxRepository.Enqueue(
            connection, transaction, "order", orderId, "upsert",
            $$"""{"id":"{{orderId}}","status":"paid","total":{{totals.TotalPaise}}}""");
        AuditRepository.Write(connection, transaction, actor, "bill.settled", $"{billNo} paid by {paymentMethod}.");

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
            CashierName = actor,
            Terminal = _session.TerminalName,
            SubtotalPaise = totals.TaxablePaise,
            DiscountPaise = totals.DiscountPaise,
            TaxPaise = totals.TaxPaise,
            CgstPaise = totals.CgstPaise,
            SgstPaise = totals.SgstPaise,
            RoundOffPaise = totals.RoundOffPaise,
            TotalPaise = totals.TotalPaise,
            PaymentMethod = paymentMethod,
            CreatedAt = now,
            PaidAt = now,
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

    public PosOrder? PayOpenOrder(
        string orderId,
        string paymentMethod,
        OrderTotals totals,
        IReadOnlyList<OrderLineInput> lines)
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

        connection.Execute(
            """
            UPDATE orders
            SET bill_no = @billNo, status = 'paid', kot_status = 'served',
                payment_method = @payment, paid_at = @paidAt,
                subtotal_paise = @subtotal, discount_paise = @discount,
                tax_paise = @tax, cgst_paise = @cgst, sgst_paise = @sgst,
                round_off_paise = @roundOff, total_paise = @total
            WHERE id = @orderId;
            """,
            new
            {
                billNo,
                payment = paymentMethod,
                paidAt,
                subtotal = totals.TaxablePaise,
                discount = totals.DiscountPaise,
                tax = totals.TaxPaise,
                cgst = totals.CgstPaise,
                sgst = totals.SgstPaise,
                roundOff = totals.RoundOffPaise,
                total = totals.TotalPaise,
                orderId
            }, transaction);

        connection.Execute("DELETE FROM order_items WHERE order_id = @orderId;", new { orderId }, transaction);
        InsertItems(connection, transaction, orderId, lines);

        OutboxRepository.Enqueue(
            connection, transaction, "order", orderId, "upsert",
            $$"""{"id":"{{orderId}}","status":"paid","total":{{totals.TotalPaise}}}""");
        AuditRepository.Write(connection, transaction, actor, "bill.settled", $"{billNo} paid by {paymentMethod}.");

        transaction.Commit();
        _notifier.Notify();

        order.BillNo = billNo;
        order.Status = "paid";
        order.KotStatus = "served";
        order.PaymentMethod = paymentMethod;
        order.PaidAt = paidAt;
        order.SubtotalPaise = totals.TaxablePaise;
        order.DiscountPaise = totals.DiscountPaise;
        order.TaxPaise = totals.TaxPaise;
        order.CgstPaise = totals.CgstPaise;
        order.SgstPaise = totals.SgstPaise;
        order.RoundOffPaise = totals.RoundOffPaise;
        order.TotalPaise = totals.TotalPaise;
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
            IsVeg = line.IsVeg
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
        }

        return order;
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
                    (id, order_id, menu_item_id, name, qty, unit_price_paise, tax_bps, line_total_paise)
                VALUES (@id, @orderId, @menuItemId, @name, @qty, @unitPrice, @taxBps, @lineTotal);
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
                    lineTotal = line.LineTotalPaise
                }, transaction);
        }
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
