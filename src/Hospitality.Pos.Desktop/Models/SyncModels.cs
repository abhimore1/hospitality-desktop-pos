namespace Hospitality.Pos.Desktop.Models;

public sealed class OutboxEntry
{
    public long Id { get; set; }
    public string Entity { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
    public int Attempts { get; set; }
    public bool Synced { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public string? LastError { get; set; }
}

public sealed record SyncRunResult(
    bool TransportConfigured,
    bool Attempted,
    int PendingBefore,
    int SyncedCount,
    int FailedCount,
    string Message);