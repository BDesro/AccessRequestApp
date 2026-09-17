namespace AccessRequestApp.Domain;

public sealed class AccessRequest
{
    public int Id { get; set; }

    public required string SystemName { get; set; }

    public required string BusinessJustification { get; set; }

    public required string RequestedByUserId { get; set; }

    public DateTimeOffset RequestedAtUtc { get; set; }

    public AccessRequestStatus Status { get; set; }

    public string? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAtUtc { get; set; }

    public string? DecisionReason { get; set; }

    public List<AccessRequestAuditEvent> AuditEvents { get; set; } = [];
}
