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
        DemoDataSeeder.Seed(connection);
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
    }
}
