using AccessRequestApp.Data;
using AccessRequestApp.Domain;
using AccessRequestApp.Security;
using AccessRequestApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestApp.Pages.Requests;

// "decisions" also covers this page's GET (viewing the list) since Razor Pages rate
// limiting attributes apply per-page, not per-handler — an accepted, documented
// simplification for the exercise's time box rather than splitting into extra pages.
[EnableRateLimiting("decisions")]
public sealed class IndexModel(
    ApplicationDbContext db,
    IAccessRequestService requestService,
    UserManager<IdentityUser> userManager,
    IAuthorizationService authorizationService,
    ILogger<IndexModel> logger) : PageModel
{
    public bool IsAdministrator { get; private set; }

    public IReadOnlyList<AccessRequestRow> Requests { get; private set; } = [];

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = userManager.GetUserId(User);
        IsAdministrator = User.IsInRole(ApplicationRoles.Administrator);

        IQueryable<AccessRequest> query = db.AccessRequests.AsNoTracking();
        if (!IsAdministrator)
        {
            query = query.Where(r => r.RequestedByUserId == userId);
        }

        // SQLite's EF Core provider can't translate ORDER BY on DateTimeOffset; Id ordering
        // is equivalent here since it increases monotonically with insertion order.
        var requests = await query
            .OrderByDescending(r => r.Id)
            .ToListAsync(cancellationToken);

        var userIds = requests
            .Select(r => r.RequestedByUserId)
            .Concat(requests.Where(r => r.DecidedByUserId != null).Select(r => r.DecidedByUserId!))
            .Distinct()
            .ToList();

        var displayNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id, cancellationToken);

        Requests = requests
            .Select(r => new AccessRequestRow(
                r.Id,
                r.SystemName,
                r.BusinessJustification,
                displayNames.GetValueOrDefault(r.RequestedByUserId, r.RequestedByUserId),
                r.RequestedAtUtc,
                r.Status,
                r.DecidedByUserId is null ? null : displayNames.GetValueOrDefault(r.DecidedByUserId, r.DecidedByUserId),
                r.DecidedAtUtc,
                r.DecisionReason))
            .ToList();
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, string? reason, CancellationToken cancellationToken)
    {
        var authResult = await authorizationService.AuthorizeAsync(User, "CanManageAccessRequests");
        if (!authResult.Succeeded)
        {
            logger.LogWarning("Blocked approval attempt on request {RequestId} by unauthorized user.", id);
            return Forbid();
        }

        var adminId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(adminId))
        {
            return Challenge();
        }

        try
        {
            await requestService.ApproveAsync(id, adminId, reason, cancellationToken);
            StatusMessage = $"Request #{id} approved.";
        }
        catch (InvalidAccessRequestTransitionException ex)
        {
            logger.LogInformation("Invalid approve transition on request {RequestId}: {Reason}", id, ex.Message);
            StatusMessage = "That request could not be approved — it may have already been decided.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDenyAsync(int id, string reason, CancellationToken cancellationToken)
    {
        var authResult = await authorizationService.AuthorizeAsync(User, "CanManageAccessRequests");
        if (!authResult.Succeeded)
        {
            logger.LogWarning("Blocked denial attempt on request {RequestId} by unauthorized user.", id);
            return Forbid();
        }

        var adminId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(adminId))
        {
            return Challenge();
        }

        try
        {
            await requestService.DenyAsync(id, adminId, reason, cancellationToken);
            StatusMessage = $"Request #{id} denied.";
        }
        catch (ArgumentException)
        {
            StatusMessage = "A reason is required to deny a request.";
        }
        catch (InvalidAccessRequestTransitionException ex)
        {
            logger.LogInformation("Invalid deny transition on request {RequestId}: {Reason}", id, ex.Message);
            StatusMessage = "That request could not be denied — it may have already been decided.";
        }

        return RedirectToPage();
    }
}

public sealed record AccessRequestRow(
    int Id,
    string SystemName,
    string BusinessJustification,
    string RequestedByDisplayName,
    DateTimeOffset RequestedAtUtc,
    AccessRequestStatus Status,
    string? DecidedByDisplayName,
    DateTimeOffset? DecidedAtUtc,
    string? DecisionReason);
