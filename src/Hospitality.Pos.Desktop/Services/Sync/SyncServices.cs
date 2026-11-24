using System.Net.NetworkInformation;
using Hospitality.Pos.Desktop.Models;
using Hospitality.Pos.Desktop.Repositories;

namespace Hospitality.Pos.Desktop.Services.Sync;

/// <summary>
/// Client-side transport for syncing outbox changes with the backend.
/// A real implementation will call POST /sync/push and GET /sync/pull.
/// </summary>
public interface ISyncTransport
{
    bool IsConfigured { get; }
    Task<SyncBatchResult> PushAsync(IReadOnlyList<OutboxEntry> batch, CancellationToken cancellationToken);
}

public sealed record SyncBatchResult(bool Success, IReadOnlyCollection<long> SyncedIds, string? ErrorMessage)
{
    public static SyncBatchResult NotConfigured() => new(false, Array.Empty<long>(), "Sync transport is not configured.");
    public static SyncBatchResult Failed(string message) => new(false, Array.Empty<long>(), message);
    public static SyncBatchResult Succeeded(IReadOnlyCollection<long> ids) => new(true, ids, null);
}

/// <summary>
/// Placeholder transport until hospitality-backend sync endpoints are available.
/// </summary>
public sealed class NoopSyncTransport : ISyncTransport
{
    public bool IsConfigured => false;

    public Task<SyncBatchResult> PushAsync(IReadOnlyList<OutboxEntry> batch, CancellationToken cancellationToken) =>
        Task.FromResult(SyncBatchResult.NotConfigured());
}

public interface ISyncCoordinator
{
    Task<SyncRunResult> SyncPendingAsync(CancellationToken cancellationToken = default);
}

public sealed class SyncCoordinator : ISyncCoordinator
{
    private readonly OutboxRepository _outbox;
    private readonly ISyncTransport _transport;
    private readonly SettingsRepository _settings;

    public SyncCoordinator(OutboxRepository outbox, ISyncTransport transport, SettingsRepository settings)
    {
        _outbox = outbox;
        _transport = transport;
        _settings = settings;
    }

    public async Task<SyncRunResult> SyncPendingAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingBefore = _outbox.CountPending();
            if (pendingBefore == 0)
            {
                RecordSyncAttempt("No pending changes.");
                return new SyncRunResult(
                    _transport.IsConfigured,
                    Attempted: false,
                    PendingBefore: 0,
                    SyncedCount: 0,
                    FailedCount: 0,
                    Message: "Everything is saved locally. No pending changes.");
            }

            if (!_transport.IsConfigured)
            {
                RecordSyncAttempt("Backend sync is not configured yet.");
                return new SyncRunResult(
                    TransportConfigured: false,
                    Attempted: false,
                    PendingBefore: pendingBefore,
                    SyncedCount: 0,
                    FailedCount: 0,
                    Message: $"{pendingBefore} change{(pendingBefore == 1 ? "" : "s")} queued locally. Backend sync is not configured yet.");
            }

            if (!NetworkInterface.GetIsNetworkAvailable())
            {
                RecordSyncAttempt("Offline.");
                return new SyncRunResult(
                    TransportConfigured: true,
                    Attempted: false,
                    PendingBefore: pendingBefore,
                    SyncedCount: 0,
                    FailedCount: 0,
                    Message: $"{pendingBefore} change{(pendingBefore == 1 ? "" : "s")} queued locally. Connect to the internet to sync.");
            }

            return await SyncPendingCoreAsync(pendingBefore, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            RecordSyncAttempt("Sync cancelled.");
            return new SyncRunResult(
                TransportConfigured: _transport.IsConfigured,
                Attempted: false,
                PendingBefore: _outbox.SafeCountPending(),
                SyncedCount: 0,
                FailedCount: 0,
                Message: "Sync cancelled.");
        }
        catch (Exception ex)
        {
            var pending = _outbox.SafeCountPending();
            RecordSyncAttempt("Sync failed.");
            return new SyncRunResult(
                TransportConfigured: _transport.IsConfigured,
                Attempted: true,
                PendingBefore: pending,
                SyncedCount: 0,
                FailedCount: 0,
                Message: $"Sync could not run. {ex.Message}");
        }
    }

    private async Task<SyncRunResult> SyncPendingCoreAsync(int pendingBefore, CancellationToken cancellationToken)
    {
        var batch = _outbox.GetPending(limit: 50);
        if (batch.Count == 0)
        {
            RecordSyncAttempt("No pending changes.");
            return new SyncRunResult(true, false, 0, 0, 0, "Everything is saved locally. No pending changes.");
        }

        try
        {
            var result = await _transport.PushAsync(batch, cancellationToken).ConfigureAwait(false);
            if (result.Success && result.SyncedIds.Count > 0)
            {
                _outbox.MarkSynced(result.SyncedIds);
                RecordSyncAttempt($"Synced {result.SyncedIds.Count} change(s).");
                var remaining = _outbox.CountPending();
                return new SyncRunResult(
                    TransportConfigured: true,
                    Attempted: true,
                    PendingBefore: pendingBefore,
                    SyncedCount: result.SyncedIds.Count,
                    FailedCount: 0,
                    Message: remaining == 0
                        ? $"Synced {result.SyncedIds.Count} change{(result.SyncedIds.Count == 1 ? "" : "s")}."
                        : $"Synced {result.SyncedIds.Count} change{(result.SyncedIds.Count == 1 ? "" : "s")}. {remaining} still pending.");
            }

            var error = string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Sync failed." : result.ErrorMessage!;
            _outbox.MarkAttemptFailed(batch.Select(entry => entry.Id), error);
            RecordSyncAttempt(error);
            return new SyncRunResult(
                TransportConfigured: true,
                Attempted: true,
                PendingBefore: pendingBefore,
                SyncedCount: 0,
                FailedCount: batch.Count,
                Message: error);
        }
        catch (OperationCanceledException)
        {
            return new SyncRunResult(true, false, pendingBefore, 0, 0, "Sync cancelled.");
        }
        catch (Exception ex)
        {
            _outbox.MarkAttemptFailed(batch.Select(entry => entry.Id), ex.Message);
            RecordSyncAttempt(ex.Message);
            return new SyncRunResult(
                TransportConfigured: true,
                Attempted: true,
                PendingBefore: pendingBefore,
                SyncedCount: 0,
                FailedCount: batch.Count,
                Message: "Sync failed. Changes remain queued locally.");
        }
    }

    private void RecordSyncAttempt(string status)
    {
        try
        {
            _settings.Set("last_sync_attempt_at", DateTime.Now.ToString("O"));
            _settings.Set("last_sync_status", status);
        }
        catch
        {
            // Sync status recording is best-effort and must not break local operations.
        }
    }
}