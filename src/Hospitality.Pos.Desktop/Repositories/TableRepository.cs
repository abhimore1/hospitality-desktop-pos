using Dapper;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Models;

namespace Hospitality.Pos.Desktop.Repositories;

public sealed class TableRepository
{
    private readonly PosDatabase _database;

    public TableRepository(PosDatabase database) => _database = database;

    public List<DiningTable> GetTables()
    {
        using var connection = _database.Open();
        return connection.Query<DiningTable>(
            """
            SELECT t.*,
                   o.id AS open_order_id,
                   COALESCE(o.total_paise, 0) AS open_amount_paise,
                   o.created_at AS opened_at
            FROM dining_tables t
            LEFT JOIN orders o ON o.table_id = t.id AND o.status = 'open'
            ORDER BY t.id;
            """).ToList();
    }
}
