using Dapper;
using Hospitality.Pos.Desktop.Data;

namespace Hospitality.Pos.Desktop.Services;

public sealed class SettingsRepository
{
    private readonly PosDatabase _database;

    public SettingsRepository(PosDatabase database) => _database = database;

    public string Get(string key, string fallback = "")
    {
        using var connection = _database.Open();
        return connection.ExecuteScalar<string?>(
            "SELECT value FROM app_settings WHERE key = @key;",
            new { key }) ?? fallback;
    }

    public int GetInt(string key, int fallback) =>
        int.TryParse(Get(key, fallback.ToString()), out var value) ? value : fallback;

    public bool GetBool(string key, bool fallback) =>
        Get(key, fallback ? "1" : "0") is "1" or "true" or "True";

    public void Set(string key, string value)
    {
        using var connection = _database.Open();
        connection.Execute(
            """
            INSERT INTO app_settings (key, value) VALUES (@key, @value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """,
            new { key, value });
    }

    public PosSettings Load() => new()
    {
        OutletName = Get("outlet_name", "Spice Route Kitchen"),
        Address = Get("outlet_address", "12, FC Road, Shivajinagar"),
        City = Get("outlet_city", "Pune, Maharashtra 411005"),
        Phone = Get("outlet_phone", "+91 98220 44556"),
        Gstin = Get("outlet_gstin", "27AABCS1429P1Z6"),
        Fssai = Get("outlet_fssai", "11522012000456"),
        GstRateBps = GetInt("gst_rate_bps", 500),
        PricesIncludeTax = GetBool("prices_include_tax", true),
        BillPrefix = Get("bill_prefix", "INV"),
        TerminalName = Get("terminal_name", "Counter 01"),
        LicensePlan = Get("license_plan", "Growth"),
        LicenseUntil = Get("license_until", DateTime.Today.AddYears(1).ToString("yyyy-MM-dd")),
        DemoMode = GetBool("demo_mode", true)
    };

    public void Save(PosSettings settings)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        var values = new Dictionary<string, string>
        {
            ["outlet_name"] = settings.OutletName,
            ["outlet_address"] = settings.Address,
            ["outlet_city"] = settings.City,
            ["outlet_phone"] = settings.Phone,
            ["outlet_gstin"] = settings.Gstin,
            ["outlet_fssai"] = settings.Fssai,
            ["gst_rate_bps"] = settings.GstRateBps.ToString(),
            ["prices_include_tax"] = settings.PricesIncludeTax ? "1" : "0",
            ["bill_prefix"] = settings.BillPrefix,
            ["terminal_name"] = settings.TerminalName
        };

        foreach (var (key, value) in values)
        {
            connection.Execute(
                """
                INSERT INTO app_settings (key, value) VALUES (@key, @value)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """,
                new { key, value }, transaction);
        }

        transaction.Commit();
    }
}
