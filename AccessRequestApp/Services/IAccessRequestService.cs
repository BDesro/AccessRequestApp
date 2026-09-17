namespace AccessRequestApp.Services;

public interface IAccessRequestService
{
    Task<int> CreateAsync(
        string systemName,
        string justification,
        string userId,
        CancellationToken cancellationToken);

    Task ApproveAsync(
        int requestId,
        string administratorUserId,
        string? reason,
        CancellationToken cancellationToken);

    Task DenyAsync(
        int requestId,
        string administratorUserId,
        string reason,
        CancellationToken cancellationToken);
}
