using Dapper;
using Hospitality.Pos.Desktop.Data;
using Hospitality.Pos.Desktop.Models;
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

    /// <summary>Best-effort pending count for error/status reporting paths.</summary>
    public int SafeCountPending()
    {
        try
        {
            return CountPending();
        }
        catch
        {
            return 0;
        }
    }

    public int CountFailedPending()
    {
        using var connection = _database.Open();
        return connection.ExecuteScalar<int>(
            """
            SELECT COUNT(*)
            FROM sync_outbox
            WHERE synced = 0 AND last_error IS NOT NULL AND TRIM(last_error) <> '';
            """);
    }

    public int SafeCountFailedPending()
    {
        try
        {
            return CountFailedPending();
        }
        catch
        {
            return 0;
        }
    }

    public DateTime? GetOldestPendingCreatedAt()
    {
        using var connection = _database.Open();
        var value = connection.ExecuteScalar<string?>(
            """
            SELECT MIN(created_at)
            FROM sync_outbox
            WHERE synced = 0;
            """);
        return DateTime.TryParse(value, out var createdAt) ? createdAt : null;
    }

    public DateTime? SafeGetOldestPendingCreatedAt()
    {
        try
        {
            return GetOldestPendingCreatedAt();
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<OutboxEntry> GetPending(int limit = 50)
    {
        using var connection = _database.Open();
        return connection.Query<OutboxEntry>(
            """
            SELECT id,
                   entity,
                   entity_id AS EntityId,
                   operation,
                   payload,
                   created_at AS CreatedAt,
                   attempts,
                   synced,
                   last_attempt_at AS LastAttemptAt,
                   last_error AS LastError
            FROM sync_outbox
            WHERE synced = 0
            ORDER BY id
            LIMIT @limit;
            """, new { limit }).ToList();
    }

    public void MarkSynced(IEnumerable<long> ids)
    {
        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0)
        {
            return;
        }

        using var connection = _database.Open();
        connection.Execute(
            """
            UPDATE sync_outbox
            SET synced = 1,
                last_error = NULL,
                last_attempt_at = @attemptedAt
            WHERE id IN @ids;
            """,
            new { ids = idList, attemptedAt = DateTime.Now });
    }

    public void MarkAttemptFailed(IEnumerable<long> ids, string error)
    {
        var idList = ids.Distinct().ToArray();
        if (idList.Length == 0)
        {
            return;
        }

        using var connection = _database.Open();
        connection.Execute(
            """
            UPDATE sync_outbox
            SET attempts = attempts + 1,
                last_attempt_at = @attemptedAt,
                last_error = @error
            WHERE id IN @ids AND synced = 0;
            """,
            new { ids = idList, attemptedAt = DateTime.Now, error = TrimError(error) });
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

    private static string TrimError(string error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Sync failed.";
        }

        error = error.Trim();
        return error.Length <= 500 ? error : error[..500];
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
