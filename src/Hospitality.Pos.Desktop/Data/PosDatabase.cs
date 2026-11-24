using System.IO;
using Dapper;
using Microsoft.Data.Sqlite;

namespace Hospitality.Pos.Desktop.Data;

public sealed class PosDatabase
{
    private readonly string _connectionString;

    public PosDatabase()
    {
        RootDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RestruntPos");
        DatabasePath = Path.Combine(RootDirectory, "pos.db");

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            ForeignKeys = true
        }.ToString();
    }

    public string RootDirectory { get; }
    public string DatabasePath { get; }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA busy_timeout = 5000;");
        return connection;
    }

    public void Initialize()
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        Directory.CreateDirectory(RootDirectory);

        using var connection = Open();
        connection.Execute("PRAGMA journal_mode = WAL;");
        CreateSchema(connection);
        EnsureEnhancedSchema(connection);
        DemoDataSeeder.Seed(connection);
        DemoDataSeeder.EnsureDemoDay(connection);
    }

    private static void CreateSchema(SqliteConnection connection)
    {
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS staff (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                role TEXT NOT NULL,
                pin_hash TEXT NOT NULL,
                color TEXT NOT NULL,
                initials TEXT NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS categories (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                color TEXT NOT NULL,
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS menu_items (
                id INTEGER PRIMARY KEY,
                category_id INTEGER NOT NULL REFERENCES categories(id),
                name TEXT NOT NULL,
                description TEXT NOT NULL DEFAULT '',
                price_paise INTEGER NOT NULL,
                tax_bps INTEGER NOT NULL DEFAULT 500,
                is_veg INTEGER NOT NULL DEFAULT 1,
                is_available INTEGER NOT NULL DEFAULT 1,
                hsn_sac TEXT NOT NULL DEFAULT '996331',
                sort_order INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS dining_tables (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                section TEXT NOT NULL,
                seats INTEGER NOT NULL DEFAULT 4,
                status TEXT NOT NULL DEFAULT 'available'
            );

            CREATE TABLE IF NOT EXISTS orders (
                id TEXT PRIMARY KEY,
                bill_no TEXT NOT NULL,
                order_type TEXT NOT NULL DEFAULT 'Dine-in',
                table_id INTEGER NULL REFERENCES dining_tables(id),
                status TEXT NOT NULL DEFAULT 'open',
                kot_status TEXT NOT NULL DEFAULT 'new',
                customer_name TEXT NOT NULL DEFAULT '',
                cashier_name TEXT NOT NULL DEFAULT '',
                terminal TEXT NOT NULL DEFAULT '',
                subtotal_paise INTEGER NOT NULL DEFAULT 0,
                discount_paise INTEGER NOT NULL DEFAULT 0,
                tax_paise INTEGER NOT NULL DEFAULT 0,
                cgst_paise INTEGER NOT NULL DEFAULT 0,
                sgst_paise INTEGER NOT NULL DEFAULT 0,
                round_off_paise INTEGER NOT NULL DEFAULT 0,
                total_paise INTEGER NOT NULL DEFAULT 0,
                gst_rate_bps INTEGER NOT NULL DEFAULT 500,
                prices_include_tax INTEGER NOT NULL DEFAULT 1,
                payment_method TEXT NOT NULL DEFAULT '',
                note TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                paid_at TEXT NULL,
                synced INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS order_items (
                id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
                menu_item_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                qty INTEGER NOT NULL,
                unit_price_paise INTEGER NOT NULL,
                tax_bps INTEGER NOT NULL DEFAULT 500,
                line_total_paise INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS inventory_items (
                id INTEGER PRIMARY KEY,
                name TEXT NOT NULL,
                unit TEXT NOT NULL,
                stock REAL NOT NULL,
                low_threshold REAL NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_outbox (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                entity TEXT NOT NULL,
                entity_id TEXT NOT NULL,
                operation TEXT NOT NULL,
                payload TEXT NOT NULL,
                created_at TEXT NOT NULL,
                attempts INTEGER NOT NULL DEFAULT 0,
                synced INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS audit_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                actor TEXT NOT NULL,
                action TEXT NOT NULL,
                detail TEXT NOT NULL,
                created_at TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_orders_status ON orders(status);
            CREATE INDEX IF NOT EXISTS ix_orders_paid_at ON orders(paid_at);
            CREATE INDEX IF NOT EXISTS ix_orders_table ON orders(table_id, status);
            CREATE INDEX IF NOT EXISTS ix_order_items_order ON order_items(order_id);
            CREATE INDEX IF NOT EXISTS ix_outbox_pending ON sync_outbox(synced, id);
            """);

        EnsureOutboxSyncColumns(connection);
    }

    private static void EnsureOutboxSyncColumns(SqliteConnection connection)
    {
        var columns = connection.Query<TableColumnInfo>("PRAGMA table_info(sync_outbox);")
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!columns.Contains("last_attempt_at"))
        {
            connection.Execute("ALTER TABLE sync_outbox ADD COLUMN last_attempt_at TEXT NULL;");
        }

        if (!columns.Contains("last_error"))
        {
            connection.Execute("ALTER TABLE sync_outbox ADD COLUMN last_error TEXT NULL;");
        }
    }

    private static void EnsureEnhancedSchema(SqliteConnection connection)
    {
        // 1. orders columns
        var orderColumns = connection.Query<TableColumnInfo>("PRAGMA table_info(orders);")
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!orderColumns.Contains("customer_phone"))
            connection.Execute("ALTER TABLE orders ADD COLUMN customer_phone TEXT NOT NULL DEFAULT '';");
        if (!orderColumns.Contains("customer_gstin"))
            connection.Execute("ALTER TABLE orders ADD COLUMN customer_gstin TEXT NOT NULL DEFAULT '';");
        if (!orderColumns.Contains("discount_reason"))
            connection.Execute("ALTER TABLE orders ADD COLUMN discount_reason TEXT NOT NULL DEFAULT '';");
        if (!orderColumns.Contains("discount_approver"))
            connection.Execute("ALTER TABLE orders ADD COLUMN discount_approver TEXT NOT NULL DEFAULT '';");
        if (!orderColumns.Contains("reprint_count"))
            connection.Execute("ALTER TABLE orders ADD COLUMN reprint_count INTEGER NOT NULL DEFAULT 0;");
        if (!orderColumns.Contains("room_number"))
            connection.Execute("ALTER TABLE orders ADD COLUMN room_number TEXT NOT NULL DEFAULT '';");

        // 2. order_items columns
        var itemColumns = connection.Query<TableColumnInfo>("PRAGMA table_info(order_items);")
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!itemColumns.Contains("station"))
            connection.Execute("ALTER TABLE order_items ADD COLUMN station TEXT NOT NULL DEFAULT 'Kitchen';");
        if (!itemColumns.Contains("status"))
            connection.Execute("ALTER TABLE order_items ADD COLUMN status TEXT NOT NULL DEFAULT 'active';");
        if (!itemColumns.Contains("cancel_reason"))
            connection.Execute("ALTER TABLE order_items ADD COLUMN cancel_reason TEXT NULL;");
        if (!itemColumns.Contains("cancel_approver"))
            connection.Execute("ALTER TABLE order_items ADD COLUMN cancel_approver TEXT NULL;");
        if (!itemColumns.Contains("course"))
            connection.Execute("ALTER TABLE order_items ADD COLUMN course TEXT NOT NULL DEFAULT 'Main';");

        // 3. menu_items columns
        var menuColumns = connection.Query<TableColumnInfo>("PRAGMA table_info(menu_items);")
            .Select(c => c.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!menuColumns.Contains("station"))
            connection.Execute("ALTER TABLE menu_items ADD COLUMN station TEXT NOT NULL DEFAULT 'Kitchen';");
        if (!menuColumns.Contains("is_favorite"))
            connection.Execute("ALTER TABLE menu_items ADD COLUMN is_favorite INTEGER NOT NULL DEFAULT 0;");

        // 4. order_payments table
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS order_payments (
                id TEXT PRIMARY KEY,
                order_id TEXT NOT NULL REFERENCES orders(id) ON DELETE CASCADE,
                method TEXT NOT NULL,
                amount_paise INTEGER NOT NULL,
                reference TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_order_payments_order ON order_payments(order_id);
            """);

        // 5. hotel_rooms table
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS hotel_rooms (
                id INTEGER PRIMARY KEY,
                room_number TEXT NOT NULL UNIQUE,
                guest_name TEXT NOT NULL,
                phone TEXT NOT NULL DEFAULT '',
                status TEXT NOT NULL DEFAULT 'occupied',
                folio_balance_paise INTEGER NOT NULL DEFAULT 0,
                check_in TEXT NOT NULL
            );
            """);

        // 6. day_end_closures table
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS day_end_closures (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                closure_date TEXT NOT NULL,
                closed_by TEXT NOT NULL,
                total_sales_paise INTEGER NOT NULL,
                total_tax_paise INTEGER NOT NULL,
                total_discount_paise INTEGER NOT NULL,
                system_cash_paise INTEGER NOT NULL,
                counted_cash_paise INTEGER NOT NULL,
                cash_variance_paise INTEGER NOT NULL,
                denominations_json TEXT NOT NULL DEFAULT '{}',
                unsynced_count INTEGER NOT NULL DEFAULT 0,
                open_orders_count INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                notes TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_day_end_closures_date ON day_end_closures(closure_date);
            """);
    }

    private sealed class TableColumnInfo
    {
        public string Name { get; set; } = "";
    }
}
