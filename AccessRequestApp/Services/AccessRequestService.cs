using AccessRequestApp.Data;
using AccessRequestApp.Domain;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestApp.Services;

/// <summary>
/// Centralizes access-request state transitions and audit-event creation so workflow
/// rules live in one place instead of being scattered across Razor Page handlers.
/// </summary>
public sealed class AccessRequestService(ApplicationDbContext db) : IAccessRequestService
{
    public async Task<int> CreateAsync(
        string systemName,
        string justification,
        string userId,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var request = new AccessRequest
        {
            SystemName = systemName.Trim(),
            BusinessJustification = justification.Trim(),
            RequestedByUserId = userId,
            RequestedAtUtc = now,
            Status = AccessRequestStatus.Pending
        };

        request.AuditEvents.Add(new AccessRequestAuditEvent
        {
            Action = AuditAction.Created,
            PerformedByUserId = userId,
            PerformedAtUtc = now
        });

        db.AccessRequests.Add(request);
        await db.SaveChangesAsync(cancellationToken);

        return request.Id;
    }

    public Task ApproveAsync(
        int requestId,
        string administratorUserId,
        string? reason,
        CancellationToken cancellationToken)
        => ChangeStatusAsync(requestId, administratorUserId, reason, AccessRequestStatus.Approved, AuditAction.Approved, cancellationToken);

    public Task DenyAsync(
        int requestId,
        string administratorUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("A denial reason is required.", nameof(reason));
        }

        return ChangeStatusAsync(requestId, administratorUserId, reason, AccessRequestStatus.Denied, AuditAction.Denied, cancellationToken);
    }

    // Reject requests that are no longer Pending: a conditional UPDATE (WHERE Status = Pending)
    // guards against two admins deciding the same request concurrently. Wrapping it with the
    // audit insert in one transaction keeps current-state and history in sync atomically.
    private async Task ChangeStatusAsync(
        int requestId,
        string administratorUserId,
        string? reason,
        AccessRequestStatus newStatus,
        AuditAction auditAction,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        reason = reason?.Trim();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var updated = await db.AccessRequests
            .Where(r => r.Id == requestId && r.Status == AccessRequestStatus.Pending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, newStatus)
                .SetProperty(r => r.DecidedByUserId, administratorUserId)
                .SetProperty(r => r.DecidedAtUtc, now)
                .SetProperty(r => r.DecisionReason, reason),
                cancellationToken);

        if (updated == 0)
        {
            throw new InvalidAccessRequestTransitionException(
                $"Access request {requestId} does not exist or is no longer pending.");
        }

        db.AccessRequestAuditEvents.Add(new AccessRequestAuditEvent
        {
            AccessRequestId = requestId,
            Action = auditAction,
            PerformedByUserId = administratorUserId,
            PerformedAtUtc = now,
            Details = reason
        });

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
