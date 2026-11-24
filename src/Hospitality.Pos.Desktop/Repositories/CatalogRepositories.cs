using Dapper;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Services;

namespace Hospitality.Pos.Desktop.Repositories;

public sealed class StaffRepository
{
    private readonly PosDatabase _database;

    public StaffRepository(PosDatabase database) => _database = database;

    public List<Staff> GetAll()
    {
        using var connection = _database.Open();
        return connection.Query<Staff>(
            "SELECT * FROM staff WHERE is_active = 1 ORDER BY id;").ToList();
    }

    public Staff? ValidatePin(int staffId, string pin)
    {
        using var connection = _database.Open();
        var staff = connection.QuerySingleOrDefault<Staff>(
            "SELECT * FROM staff WHERE id = @staffId AND is_active = 1;", new { staffId });

        if (staff is null || !Security.VerifyPin(pin, staff.PinHash))
        {
            return null;
        }

        return staff;
    }
}

public sealed class MenuRepository
{
    private readonly PosDatabase _database;
    private readonly DataChangeNotifier _notifier;
    private readonly AppSession _session;

    public MenuRepository(PosDatabase database, DataChangeNotifier notifier, AppSession session)
    {
        _database = database;
        _notifier = notifier;
        _session = session;
    }

    public List<MenuCategory> GetCategories()
    {
        using var connection = _database.Open();
        return connection.Query<MenuCategory>(
            "SELECT * FROM categories ORDER BY sort_order, id;").ToList();
    }

    public List<MenuItem> GetItems(string? search = null, int? categoryId = null)
    {
        using var connection = _database.Open();
        var sql =
            """
            SELECT m.*, c.name AS category_name, c.color AS category_color
            FROM menu_items m
            JOIN categories c ON c.id = m.category_id
            WHERE 1 = 1
            """;

        if (categoryId is > 0)
        {
            sql += " AND m.category_id = @categoryId";
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            sql += " AND (m.name LIKE @search OR m.description LIKE @search)";
        }

        sql += " ORDER BY c.sort_order, m.sort_order, m.id;";

        return connection.Query<MenuItem>(sql, new
        {
            categoryId,
            search = $"%{search?.Trim()}%"
        }).ToList();
    }

    public MenuItem? GetItem(int id)
    {
        using var connection = _database.Open();
        return connection.QuerySingleOrDefault<MenuItem>(
            """
            SELECT m.*, c.name AS category_name, c.color AS category_color
            FROM menu_items m JOIN categories c ON c.id = m.category_id
            WHERE m.id = @id;
            """, new { id });
    }

    public void SetAvailability(int itemId, bool isAvailable)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        connection.Execute(
            "UPDATE menu_items SET is_available = @isAvailable WHERE id = @itemId;",
            new { itemId, isAvailable }, transaction);

        OutboxRepository.Enqueue(
            connection, transaction, "menu_item", itemId.ToString(), "update",
            $$"""{"id":{{itemId}},"isAvailable":{{isAvailable.ToString().ToLowerInvariant()}}}""");

        AuditRepository.Write(
            connection, transaction, _session.CurrentStaff?.Name ?? "System",
            "menu.availability", $"Item {itemId} set to {(isAvailable ? "available" : "sold out")}.");

        transaction.Commit();
        _notifier.Notify();
    }

    public List<InventoryItem> GetLowStock()
    {
        using var connection = _database.Open();
        return connection.Query<InventoryItem>(
            """
            SELECT * FROM inventory_items
            WHERE stock <= low_threshold
            ORDER BY (stock / NULLIF(low_threshold, 0)), name;
            """).ToList();
    }

    public string ExportMenuCsv()
    {
        var items = GetItems();
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Category,Name,Description,Price (INR),Tax %,IsVeg,HSN/SAC,Station,Available");
        foreach (var item in items)
        {
            var priceRupees = (item.PricePaise / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            var taxPercent = (item.TaxBps / 100m).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            sb.AppendLine($"\"{EscapeCsv(item.CategoryName)}\",\"{EscapeCsv(item.Name)}\",\"{EscapeCsv(item.Description)}\",{priceRupees},{taxPercent},{(item.IsVeg ? "Veg" : "NonVeg")},\"{EscapeCsv(item.HsnSac)}\",\"{EscapeCsv(item.Station)}\",{(item.IsAvailable ? "1" : "0")}");
        }
        return sb.ToString();
    }

    public bool ImportMenuCsv(string csvContent, out int added, out int updated, out string error)
    {
        added = 0;
        updated = 0;
        error = "";

        if (string.IsNullOrWhiteSpace(csvContent))
        {
            error = "CSV file is empty.";
            return false;
        }

        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        try
        {
            var lines = csvContent.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length <= 1)
            {
                error = "CSV contains only a header line.";
                return false;
            }

            var categories = GetCategories().ToDictionary(c => c.Name.Trim(), c => c.Id, StringComparer.OrdinalIgnoreCase);

            for (var i = 1; i < lines.Length; i++)
            {
                var row = ParseCsvRow(lines[i]);
                if (row.Count < 4) continue;

                var categoryName = row[0].Trim();
                var name = row[1].Trim();
                var desc = row.Count > 2 ? row[2].Trim() : "";
                var priceStr = row.Count > 3 ? row[3].Trim() : "0";
                var taxStr = row.Count > 4 ? row[4].Trim() : "5";
                var vegStr = row.Count > 5 ? row[5].Trim() : "Veg";
                var hsnStr = row.Count > 6 ? row[6].Trim() : "996331";
                var stationStr = row.Count > 7 ? row[7].Trim() : "Kitchen";

                if (string.IsNullOrWhiteSpace(name)) continue;

                if (!categories.TryGetValue(categoryName, out var categoryId))
                {
                    categoryId = connection.ExecuteScalar<int>(
                        "INSERT INTO categories (name, color, sort_order) VALUES (@categoryName, '#0F766E', 10) RETURNING id;",
                        new { categoryName }, transaction);
                    categories[categoryName] = categoryId;
                }

                var pricePaise = decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var pr)
                    ? (long)Math.Round(pr * 100m, MidpointRounding.AwayFromZero) : 0;
                var taxBps = decimal.TryParse(taxStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tx)
                    ? (int)Math.Round(tx * 100m, MidpointRounding.AwayFromZero) : 500;
                var isVeg = !vegStr.Contains("Non", StringComparison.OrdinalIgnoreCase);

                var existingId = connection.ExecuteScalar<int?>(
                    "SELECT id FROM menu_items WHERE name = @name;", new { name }, transaction);

                if (existingId is not null)
                {
                    connection.Execute(
                        """
                        UPDATE menu_items
                        SET category_id = @categoryId, description = @desc, price_paise = @pricePaise,
                            tax_bps = @taxBps, is_veg = @isVeg, hsn_sac = @hsnStr, station = @stationStr
                        WHERE id = @existingId;
                        """,
                        new { categoryId, desc, pricePaise, taxBps, isVeg = isVeg ? 1 : 0, hsnStr, stationStr, existingId }, transaction);
                    updated++;
                }
                else
                {
                    connection.Execute(
                        """
                        INSERT INTO menu_items
                            (category_id, name, description, price_paise, tax_bps, is_veg, is_available, hsn_sac, station, sort_order)
                        VALUES
                            (@categoryId, @name, @desc, @pricePaise, @taxBps, @isVeg, 1, @hsnStr, @stationStr, 99);
                        """,
                        new { categoryId, name, desc, pricePaise, taxBps, isVeg = isVeg ? 1 : 0, hsnStr, stationStr }, transaction);
                    added++;
                }
            }

            AuditRepository.Write(connection, transaction, _session.CurrentStaff?.Name ?? "System", "menu.imported",
                $"Menu CSV import: {added} added, {updated} updated.");

            transaction.Commit();
            _notifier.Notify();
            return true;
        }
        catch (Exception ex)
        {
            transaction.Rollback();
            error = ex.Message;
            return false;
        }
    }

    private static string EscapeCsv(string text) => text?.Replace("\"", "\"\"") ?? "";

    private static List<string> ParseCsvRow(string line)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        result.Add(current.ToString());
        return result;
    }
}
