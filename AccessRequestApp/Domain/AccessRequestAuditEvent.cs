namespace AccessRequestApp.Domain;

public sealed class AccessRequestAuditEvent
{
    public long Id { get; set; }

    public int AccessRequestId { get; set; }

    public AuditAction Action { get; set; }

    public required string PerformedByUserId { get; set; }

    public DateTimeOffset PerformedAtUtc { get; set; }

    public string? Details { get; set; }

    public AccessRequest? AccessRequest { get; set; }
}
