using Dapper;
using Hospitality.Pos.Desktop.Domain;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;
using Hospitality.Pos.Desktop.Services;
using Microsoft.Data.Sqlite;

namespace Hospitality.Pos.Desktop.Data;
/// <summary>
/// Seeds a realistic demo outlet the first time the local database is created.
/// This is prototype data only; production installs start empty.
/// </summary>
public static class DemoDataSeeder
{
    private static readonly (int Id, string Name, string Description, int CategoryId, long Price, bool Veg)[] Menu =
    [
        (1, "Paneer Tikka", "Char-grilled cottage cheese, mint chutney", 1, 32000, true),
        (2, "Chicken Tikka", "Yogurt-marinated chicken, smoked in the tandoor", 1, 38000, false),
        (3, "Veg Spring Rolls", "Crisp rolls, sweet chilli dip", 1, 26000, true),
        (4, "Crispy Corn", "Sweet corn, curry leaf, chilli", 1, 24000, true),
        (5, "Chilli Paneer", "Indo-Chinese favourite, dry", 1, 29000, true),
        (6, "Butter Chicken", "Slow-cooked tomato gravy, cream, butter", 2, 42000, false),
        (7, "Dal Makhani", "Black lentils simmered overnight", 2, 28000, true),
        (8, "Paneer Butter Masala", "Cottage cheese in a rich tomato gravy", 2, 34000, true),
        (9, "Kadai Chicken", "Chicken, peppers, freshly ground masala", 2, 40000, false),
        (10, "Palak Paneer", "Spinach gravy, soft paneer", 2, 31000, true),
        (11, "Veg Kolhapuri", "Spicy Maharashtrian vegetable curry", 2, 30000, true),
        (12, "Butter Naan", "Tandoor-baked, brushed with butter", 3, 6000, true),
        (13, "Garlic Naan", "Fresh garlic and coriander", 3, 8000, true),
        (14, "Tandoori Roti", "Whole wheat, no oil", 3, 4000, true),
        (15, "Laccha Paratha", "Flaky layered paratha", 3, 7000, true),
        (16, "Jeera Rice", "Basmati rice, cumin", 4, 18000, true),
        (17, "Veg Biryani", "Dum-cooked basmati, vegetables, raita", 4, 32000, true),
        (18, "Chicken Biryani", "Hyderabadi style, leg piece, raita", 4, 38000, false),
        (19, "Steamed Rice", "Basmati rice", 4, 14000, true),
        (20, "Gulab Jamun", "Two pieces, warm", 5, 12000, true),
        (21, "Rasmalai", "Chilled, saffron milk", 5, 14000, true),
        (22, "Brownie with Ice Cream", "Walnut brownie, vanilla scoop", 5, 22000, true),
        (23, "Masala Chai", "Kadak chai, ginger and cardamom", 6, 6000, true),
        (24, "Filter Coffee", "South Indian style", 6, 8000, true),
        (25, "Fresh Lime Soda", "Sweet, salted or mixed", 6, 9000, true),
        (26, "Cold Coffee", "Creamy, served chilled", 6, 15000, true),
        (27, "Mineral Water", "One litre bottle", 6, 3000, true),
        (28, "Veg Thali", "Dal, seasonal sabzi, rice, roti, salad, sweet", 7, 38000, true),
        (29, "Chicken Thali", "Chicken curry, dal, rice, roti, salad", 7, 45000, false),
        (30, "Family Feast", "Two curries, rice, six breads, dessert, drinks", 7, 99900, false)
    ];

    public static void Seed(SqliteConnection connection)
    {
        if (GetSetting(connection, "seed_version") == "1")
        {
            return;
        }

        using var transaction = connection.BeginTransaction();

        SeedSettings(connection, transaction);
        SeedCatalog(connection, transaction);
        SeedStaff(connection, transaction);
        SeedTables(connection, transaction);
        SeedInventory(connection, transaction);
        SeedHotelRooms(connection, transaction);
        SeedDemoOrders(connection, transaction);

        UpsertSetting(connection, transaction, "seed_version", "1");
        transaction.Commit();
    }

    private static string GetSetting(SqliteConnection connection, string key) =>
        connection.ExecuteScalar<string?>(
            "SELECT value FROM app_settings WHERE key = @key;", new { key }) ?? "";

    private static void SeedSettings(SqliteConnection connection, SqliteTransaction transaction)
    {
        var values = new Dictionary<string, string>
        {
            ["outlet_name"] = "Spice Route Kitchen",
            ["outlet_address"] = "12, FC Road, Shivajinagar",
            ["outlet_city"] = "Pune, Maharashtra 411005",
            ["outlet_phone"] = "+91 98220 44556",
            ["outlet_gstin"] = "27AABCS1429P1Z6",
            ["outlet_fssai"] = "11522012000456",
            ["gst_rate_bps"] = "500",
            ["prices_include_tax"] = "1",
            ["bill_prefix"] = "INV",
            ["bill_sequence"] = "0",
            ["kot_sequence"] = "1040",
            ["terminal_name"] = "Counter 01",
            ["license_plan"] = "Growth",
            ["license_until"] = DateTime.Today.AddYears(1).ToString("yyyy-MM-dd"),
            ["demo_mode"] = "1"
        };

        foreach (var (key, value) in values)
        {
            UpsertSetting(connection, transaction, key, value);
        }
    }

    private static void UpsertSetting(SqliteConnection connection, SqliteTransaction transaction, string key, string value)
    {
        connection.Execute(
            """
            INSERT INTO app_settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            new { key, value }, transaction);
    }

    private static void SeedCatalog(SqliteConnection connection, SqliteTransaction transaction)
    {
        var categories = new[]
        {
            (1, "Starters", "#EA580C", 1),
            (2, "Main Course", "#0F766E", 2),
            (3, "Breads", "#CA8A04", 3),
            (4, "Rice & Biryani", "#7C3AED", 4),
            (5, "Desserts", "#DB2777", 5),
            (6, "Beverages", "#0284C7", 6),
            (7, "Combos", "#DC2626", 7)
        };

        foreach (var (id, name, color, sort) in categories)
        {
            connection.Execute(
                "INSERT OR IGNORE INTO categories (id, name, color, sort_order) VALUES (@id, @name, @color, @sort);",
                new { id, name, color, sort }, transaction);
        }

        var order = 0;
        foreach (var (id, name, description, categoryId, price, veg) in Menu)
        {
            connection.Execute(
                """
                INSERT OR IGNORE INTO menu_items
                    (id, category_id, name, description, price_paise, tax_bps, is_veg, is_available, hsn_sac, sort_order)
                VALUES (@id, @categoryId, @name, @description, @price, 500, @veg, 1, '996331', @sort);
                """,
                new { id, categoryId, name, description, price, veg, sort = order++ }, transaction);
        }
    }

    private static void SeedStaff(SqliteConnection connection, SqliteTransaction transaction)
    {
        var staff = new[]
        {
            (1, "Anita Sharma", "owner", "1234", "#0F766E", "AS"),
            (2, "Vikram Rao", "manager", "1234", "#7C3AED", "VR"),
            (3, "Rahul Patil", "cashier", "1234", "#0284C7", "RP"),
            (4, "Suresh Kumar", "waiter", "1234", "#EA580C", "SK")
        };

        foreach (var (id, name, role, pin, color, initials) in staff)
        {
            connection.Execute(
                """
                INSERT OR IGNORE INTO staff (id, name, role, pin_hash, color, initials, is_active)
                VALUES (@id, @name, @role, @pinHash, @color, @initials, 1);
                """,
                new { id, name, role, pinHash = Security.HashPin(pin), color, initials }, transaction);
        }
    }

    private static void SeedTables(SqliteConnection connection, SqliteTransaction transaction)
    {
        var tables = new (int Id, string Name, string Section, int Seats)[]
        {
            (1, "T1", "Ground floor", 4), (2, "T2", "Ground floor", 4),
            (3, "T3", "Ground floor", 4), (4, "T4", "Ground floor", 4),
            (5, "T5", "Ground floor", 2), (6, "T6", "Ground floor", 4),
            (7, "P1", "Terrace", 4), (8, "P2", "Terrace", 4),
            (9, "P3", "Terrace", 2), (10, "P4", "Terrace", 4),
            (11, "P5", "Terrace", 6), (12, "G1", "Garden", 4),
            (13, "G2", "Garden", 4), (14, "G3", "Garden", 6)
        };

        foreach (var (id, name, section, seats) in tables)
        {
            connection.Execute(
                "INSERT OR IGNORE INTO dining_tables (id, name, section, seats, status) VALUES (@id, @name, @section, @seats, 'available');",
                new { id, name, section, seats }, transaction);
        }
    }

    private static void SeedInventory(SqliteConnection connection, SqliteTransaction transaction)
    {
        var items = new (int Id, string Name, string Unit, double Stock, double Low)[]
        {
            (1, "Paneer", "kg", 7.5, 10),
            (2, "Chicken", "kg", 14, 8),
            (3, "Basmati rice", "kg", 26, 15),
            (4, "Milk", "L", 8, 10),
            (5, "Cooking oil", "L", 4, 5),
            (6, "Tomatoes", "kg", 18, 10),
            (7, "Butter", "kg", 3.5, 5)
        };

        foreach (var (id, name, unit, stock, low) in items)
        {
            connection.Execute(
                """
                INSERT OR IGNORE INTO inventory_items (id, name, unit, stock, low_threshold, updated_at)
                VALUES (@id, @name, @unit, @stock, @low, @now);
                """,
                new { id, name, unit, stock, low, now = DateTime.Now }, transaction);
        }
    }

    public static void SeedHotelRooms(SqliteConnection connection, SqliteTransaction? transaction)
    {
        var count = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM hotel_rooms;", transaction: transaction);
        if (count > 0)
        {
            return;
        }

        var rooms = new[]
        {
            (1, "101", "Rohan Mehra", "+91 98111 22334", "occupied", 0L, DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd HH:mm")),
            (2, "102", "Priya Sharma", "+91 98222 33445", "occupied", 0L, DateTime.Today.ToString("yyyy-MM-dd HH:mm")),
            (3, "201", "Amit Verma", "+91 98333 44556", "occupied", 0L, DateTime.Today.AddDays(-2).ToString("yyyy-MM-dd HH:mm")),
            (4, "202", "Kavita Reddy", "+91 98444 55667", "vacant", 0L, DateTime.Today.ToString("yyyy-MM-dd HH:mm")),
            (5, "301", "Dr. K. Saxena", "+91 98555 66778", "occupied", 0L, DateTime.Today.AddDays(-1).ToString("yyyy-MM-dd HH:mm"))
        };

        foreach (var (id, roomNumber, guestName, phone, status, folio, checkIn) in rooms)
        {
            connection.Execute(
                """
                INSERT OR IGNORE INTO hotel_rooms (id, room_number, guest_name, phone, status, folio_balance_paise, check_in)
                VALUES (@id, @roomNumber, @guestName, @phone, @status, @folio, @checkIn);
                """,
                new { id, roomNumber, guestName, phone, status, folio, checkIn }, transaction);
        }
    }

    private static void SeedDemoOrders(SqliteConnection connection, SqliteTransaction transaction)
    {
        var existing = connection.ExecuteScalar<int>("SELECT COUNT(*) FROM orders;", transaction: transaction);
        if (existing > 0)
        {
            return;
        }

        var items = connection.Query<MenuItem>(
            "SELECT id, name, price_paise, tax_bps, is_veg FROM menu_items;", transaction: transaction).ToList();

        var random = new Random(23112026);
        var fy = FinancialYearLabel(DateTime.Today);

        // Historical paid bills across the service day, for the dashboard and reports.
        for (var i = 0; i < 46; i++)
        {
            var created = DateTime.Today.AddHours(8).AddMinutes(i * 16 + random.Next(0, 6));
            var lineCount = random.Next(1, 5);
            var chosen = items.OrderBy(_ => random.Next()).Take(lineCount).ToList();
            var lines = chosen.Select(item => new TaxLine(item.PricePaise, random.Next(1, 3), item.TaxBps)).ToList();
            var totals = TaxCalculator.Calculate(lines, 0, true);

            var orderType = i % 9 == 0 ? "Delivery" : i % 5 == 0 ? "Takeaway" : "Dine-in";
            var tableId = orderType == "Dine-in" ? random.Next(1, 15) : (int?)null;
            var payment = random.Next(0, 10) switch
            {
                <= 4 => "UPI",
                <= 7 => "Cash",
                _ => "Card"
            };

            var orderId = Guid.NewGuid().ToString();
            var billNo = $"{GetSetting(connection, "bill_prefix")}/{fy}/{i + 1:0000}";

            connection.Execute(
                """
                INSERT INTO orders
                    (id, bill_no, order_type, table_id, status, kot_status, cashier_name, terminal,
                     subtotal_paise, discount_paise, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                     total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                     created_at, paid_at, synced)
                VALUES
                    (@id, @billNo, @orderType, @tableId, 'paid', 'served', 'Rahul Patil', 'Counter 01',
                     @subtotal, @discount, @tax, @cgst, @sgst, @roundOff,
                     @total, 500, 1, @payment, '',
                     @created, @paidAt, 1);
                """,
                new
                {
                    id = orderId,
                    billNo,
                    orderType,
                    tableId,
                    subtotal = totals.TaxablePaise,
                    discount = totals.DiscountPaise,
                    tax = totals.TaxPaise,
                    cgst = totals.CgstPaise,
                    sgst = totals.SgstPaise,
                    roundOff = totals.RoundOffPaise,
                    total = totals.TotalPaise,
                    payment,
                    created,
                    paidAt = created.AddMinutes(35)
                }, transaction);

            InsertItems(connection, transaction, orderId, chosen, lines);
        }

        // Live open orders for the kitchen and table screens.
        var openOrders = new (int TableId, int[] ItemIds, int[] Quantities, string KotStatus, int MinutesAgo)[]
        {
            (2, [1, 6, 12, 25], [1, 1, 2, 1], "preparing", 14),
            (5, [18, 23], [1, 1], "new", 6),
            (11, [28, 20], [2, 1], "ready", 31),
            (13, [30, 26], [1, 2], "preparing", 22)
        };

        var kotNo = 1040;
        foreach (var (tableId, itemIds, quantities, kotStatus, minutesAgo) in openOrders)
        {
            kotNo++;
            var chosen = items.Where(item => itemIds.Contains(item.Id)).ToList();
            var taxLines = new List<TaxLine>();
            for (var index = 0; index < chosen.Count; index++)
            {
                taxLines.Add(new TaxLine(chosen[index].PricePaise, quantities[index], chosen[index].TaxBps));
            }

            var totals = TaxCalculator.Calculate(taxLines, 0, true);
            var orderId = Guid.NewGuid().ToString();
            var createdAt = DateTime.Now.AddMinutes(-minutesAgo);

            connection.Execute(
                """
                INSERT INTO orders
                    (id, bill_no, order_type, table_id, status, kot_status, cashier_name, terminal,
                     subtotal_paise, discount_paise, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                     total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                     created_at, paid_at, synced)
                VALUES
                    (@id, @billNo, 'Dine-in', @tableId, 'open', @kotStatus, 'Anita Sharma', 'Counter 01',
                     @subtotal, @discount, @tax, @cgst, @sgst, @roundOff,
                     @total, 500, 1, '', '',
                     @created, NULL, 0);
                """,
                new
                {
                    id = orderId,
                    billNo = $"KOT-{kotNo}",
                    tableId,
                    kotStatus,
                    subtotal = totals.TaxablePaise,
                    discount = totals.DiscountPaise,
                    tax = totals.TaxPaise,
                    cgst = totals.CgstPaise,
                    sgst = totals.SgstPaise,
                    roundOff = totals.RoundOffPaise,
                    total = totals.TotalPaise,
                    created = createdAt
                }, transaction);

            InsertItems(connection, transaction, orderId, chosen, taxLines);

            OutboxRepository.Enqueue(
                connection, transaction, "order", orderId, "upsert",
                $$"""{"id":"{{orderId}}","status":"open","tableId":{{tableId}}}""");
        }

        UpsertSetting(connection, transaction, "bill_sequence", "46");
        UpsertSetting(connection, transaction, "kot_sequence", kotNo.ToString());
    }

    private static void InsertItems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string orderId,
        IReadOnlyList<MenuItem> items,
        IReadOnlyList<TaxLine> lines)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var line = lines[i];
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
                    menuItemId = item.Id,
                    name = item.Name,
                    qty = line.Quantity,
                    unitPrice = line.UnitPricePaise,
                    taxBps = line.TaxRateBps,
                    lineTotal = line.UnitPricePaise * line.Quantity
                }, transaction);
        }
    }

    /// <summary>
    /// Keeps the demo outlet believable when the prototype is opened on a later day.
    /// This only runs when demo_mode=1; production data is never touched.
    /// </summary>
    public static void EnsureDemoDay(SqliteConnection connection)
    {
        SeedHotelRooms(connection, null);

        if (GetSetting(connection, "demo_mode") != "1")
        {
            return;
        }

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (GetSetting(connection, "demo_seed_date") == today)
        {
            return;
        }

        using var transaction = connection.BeginTransaction();

        var items = connection.Query<MenuItem>(
            "SELECT id, name, price_paise, tax_bps, is_veg FROM menu_items;", transaction: transaction).ToList();
        if (items.Count == 0)
        {
            transaction.Rollback();
            return;
        }

        // Re-time yesterday's seeded tickets so the kitchen never opens on day-old orders.
        connection.Execute(
            """
            UPDATE orders
            SET created_at = datetime('now', 'localtime', '-' || (
                SELECT CASE bill_no
                    WHEN 'KOT-1041' THEN 14
                    WHEN 'KOT-1042' THEN 6
                    WHEN 'KOT-1043' THEN 31
                    WHEN 'KOT-1044' THEN 22
                    ELSE 18
                END) || ' minutes')
            WHERE status = 'open'
              AND date(created_at) < date('now', 'localtime');
            """, transaction: transaction);

        var paidToday = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM orders WHERE status = 'paid' AND date(paid_at) = date('now', 'localtime');",
            transaction: transaction);

        if (paidToday == 0)
        {
            var random = new Random(int.Parse(DateTime.Today.ToString("yyyyMMdd")));
            var fy = FinancialYearLabel(DateTime.Today);
            var prefix = GetSetting(connection, "bill_prefix");
            if (string.IsNullOrWhiteSpace(prefix))
            {
                prefix = "INV";
            }

            var sequence = connection.ExecuteScalar<int>(
                "SELECT CAST(COALESCE((SELECT value FROM app_settings WHERE key = 'bill_sequence'), '0') AS INTEGER);",
                transaction: transaction);

            var dayStart = DateTime.Today.AddMinutes(10);
            var latest = DateTime.Now.AddMinutes(-2);
            if (latest <= dayStart)
            {
                latest = dayStart.AddMinutes(30);
            }

            const int billCount = 38;
            var span = latest - dayStart;
            var usedTableIds = new HashSet<int>();

            for (var index = 0; index < billCount; index++)
            {
                sequence++;
                var orderId = Guid.NewGuid().ToString();
                var created = dayStart + span * ((index + random.NextDouble() * 0.7) / billCount);
                var lineCount = random.Next(1, 5);
                var chosen = items.OrderBy(_ => random.Next()).Take(lineCount).ToList();
                var lines = chosen
                    .Select(item => new TaxLine(item.PricePaise, random.Next(1, 3), item.TaxBps))
                    .ToList();
                var totals = TaxCalculator.Calculate(lines, 0, true);

                var orderType = index % 9 == 0 ? "Delivery" : index % 5 == 0 ? "Takeaway" : "Dine-in";
                int? tableId = null;
                if (orderType == "Dine-in")
                {
                    // Spread dine-in checks over the floor instead of always picking table 1-14.
                    for (var attempt = 0; attempt < 14; attempt++)
                    {
                        var candidate = random.Next(1, 15);
                        if (usedTableIds.Add(candidate) || usedTableIds.Count >= 14)
                        {
                            tableId = candidate;
                            break;
                        }
                    }
                }

                var payment = random.Next(0, 10) switch
                {
                    <= 4 => "UPI",
                    <= 7 => "Cash",
                    _ => "Card"
                };

                var paidAt = created.AddMinutes(Math.Min(35, Math.Max(4, (latest - created).TotalMinutes)));
                var billNo = $"{prefix}/{fy}/{sequence:0000}";

                connection.Execute(
                    """
                    INSERT INTO orders
                        (id, bill_no, order_type, table_id, status, kot_status, cashier_name, terminal,
                         subtotal_paise, discount_paise, tax_paise, cgst_paise, sgst_paise, round_off_paise,
                         total_paise, gst_rate_bps, prices_include_tax, payment_method, note,
                         created_at, paid_at, synced)
                    VALUES
                        (@id, @billNo, @orderType, @tableId, 'paid', 'served', 'Rahul Patil', 'Counter 01',
                         @subtotal, @discount, @tax, @cgst, @sgst, @roundOff,
                         @total, 500, 1, @payment, '',
                         @created, @paidAt, 1);
                    """,
                    new
                    {
                        id = orderId,
                        billNo,
                        orderType,
                        tableId,
                        subtotal = totals.TaxablePaise,
                        discount = totals.DiscountPaise,
                        tax = totals.TaxPaise,
                        cgst = totals.CgstPaise,
                        sgst = totals.SgstPaise,
                        roundOff = totals.RoundOffPaise,
                        total = totals.TotalPaise,
                        payment,
                        created,
                        paidAt
                    }, transaction);

                InsertItems(connection, transaction, orderId, chosen, lines);
            }

            UpsertSetting(connection, transaction, "bill_sequence", sequence.ToString());
        }

        UpsertSetting(connection, transaction, "demo_seed_date", today);
        transaction.Commit();
    }

    public static string FinancialYearLabel(DateTime date)
    {
        var startYear = date.Month >= 4 ? date.Year : date.Year - 1;
        return $"{startYear % 100:00}-{(startYear + 1) % 100:00}";
    }
}
