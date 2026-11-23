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
}
