using Dapper;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Services;
using Microsoft.Data.Sqlite;

namespace Hospitality.Pos.Desktop.Repositories;

public sealed class OutboxRepository
{
    private readonly PosDatabase _database;

    public OutboxRepository(PosDatabase database) => _database = database;

    public int CountPending()
    {
        using var connection = _database.Open();
        return connection.ExecuteScalar<int>("SELECT COUNT(*) FROM sync_outbox WHERE synced = 0;");
    }

    public static void Enqueue(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string entity,
        string entityId,
        string operation,
        string payload = "{}")
    {
        connection.Execute(
            """
            INSERT INTO sync_outbox (entity, entity_id, operation, payload, created_at, attempts, synced)
            VALUES (@entity, @entityId, @operation, @payload, @createdAt, 0, 0);
            """,
            new { entity, entityId, operation, payload, createdAt = DateTime.Now },
            transaction);
    }
}

public sealed class AuditRepository
{
    private readonly PosDatabase _database;

    public AuditRepository(PosDatabase database) => _database = database;

    public void Log(string actor, string action, string detail)
    {
        using var connection = _database.Open();
        Write(connection, null, actor, action, detail);
    }

    public static void Write(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string actor,
        string action,
        string detail)
    {
        connection.Execute(
            "INSERT INTO audit_log (actor, action, detail, created_at) VALUES (@actor, @action, @detail, @createdAt);",
            new { actor, action, detail, createdAt = DateTime.Now },
            transaction);
    }
}
