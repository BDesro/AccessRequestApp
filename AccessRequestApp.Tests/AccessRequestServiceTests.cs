using AccessRequestApp.Data;
using AccessRequestApp.Domain;
using AccessRequestApp.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AccessRequestApp.Tests;

/// <summary>
/// Backed by a SQLite in-memory database (kept alive via an open connection) so tests exercise
/// real EF Core/SQLite translation instead of mocking DbSet behavior. Each operation gets its
/// own fresh ApplicationDbContext, mirroring the per-HTTP-request scoped lifetime used in
/// production — reusing one context across calls would mask ExecuteUpdateAsync's effects behind
/// EF's change-tracker identity map.
/// </summary>
public sealed class AccessRequestServiceTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public AccessRequestServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var db = CreateContext();
        db.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new ApplicationDbContext(options);
    }

    [Fact]
    public async Task CreateAsync_SetsPendingStatusAndAttribution()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using var verify = CreateContext();
        var request = await verify.AccessRequests.SingleAsync(r => r.Id == id);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.Equal("user-1", request.RequestedByUserId);
        Assert.True((DateTimeOffset.UtcNow - request.RequestedAtUtc) < TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task CreateAsync_WritesCreatedAuditEvent()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using var verify = CreateContext();
        var auditEvent = await verify.AccessRequestAuditEvents.SingleAsync(e => e.AccessRequestId == id);
        Assert.Equal(AuditAction.Created, auditEvent.Action);
        Assert.Equal("user-1", auditEvent.PerformedByUserId);
    }

    [Fact]
    public async Task ApproveAsync_OnPendingRequest_SucceedsAndWritesAuditEvent()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using (var db = CreateContext())
        {
            await new AccessRequestService(db).ApproveAsync(id, "admin-1", "Looks fine", CancellationToken.None);
        }

        using var verify = CreateContext();
        var request = await verify.AccessRequests.SingleAsync(r => r.Id == id);
        Assert.Equal(AccessRequestStatus.Approved, request.Status);
        Assert.Equal("admin-1", request.DecidedByUserId);
        Assert.NotNull(request.DecidedAtUtc);

        var auditEvent = await verify.AccessRequestAuditEvents.SingleAsync(e => e.AccessRequestId == id && e.Action == AuditAction.Approved);
        Assert.Equal("admin-1", auditEvent.PerformedByUserId);
    }

    [Fact]
    public async Task DenyAsync_WithoutReason_ThrowsAndDoesNotChangeState()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using (var db = CreateContext())
        {
            await Assert.ThrowsAsync<ArgumentException>(
                () => new AccessRequestService(db).DenyAsync(id, "admin-1", "  ", CancellationToken.None));
        }

        using var verify = CreateContext();
        var request = await verify.AccessRequests.SingleAsync(r => r.Id == id);
        Assert.Equal(AccessRequestStatus.Pending, request.Status);
        Assert.False(await verify.AccessRequestAuditEvents.AnyAsync(e => e.AccessRequestId == id && e.Action == AuditAction.Denied));
    }

    [Fact]
    public async Task DenyAsync_OnPendingRequest_SucceedsAndWritesAuditEvent()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using (var db = CreateContext())
        {
            await new AccessRequestService(db).DenyAsync(id, "admin-1", "Not needed for this role", CancellationToken.None);
        }

        using var verify = CreateContext();
        var request = await verify.AccessRequests.SingleAsync(r => r.Id == id);
        Assert.Equal(AccessRequestStatus.Denied, request.Status);
        Assert.Equal("Not needed for this role", request.DecisionReason);

        var auditEvent = await verify.AccessRequestAuditEvents.SingleAsync(e => e.AccessRequestId == id && e.Action == AuditAction.Denied);
        Assert.Equal("Not needed for this role", auditEvent.Details);
    }

    [Fact]
    public async Task ApproveAsync_OnAlreadyDecidedRequest_ThrowsAndDoesNotOverwriteDecision()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using (var db = CreateContext())
        {
            await new AccessRequestService(db).DenyAsync(id, "admin-1", "First decision", CancellationToken.None);
        }

        using (var db = CreateContext())
        {
            await Assert.ThrowsAsync<InvalidAccessRequestTransitionException>(
                () => new AccessRequestService(db).ApproveAsync(id, "admin-2", "Second decision attempt", CancellationToken.None));
        }

        using var verify = CreateContext();
        var request = await verify.AccessRequests.SingleAsync(r => r.Id == id);
        Assert.Equal(AccessRequestStatus.Denied, request.Status);
        Assert.Equal("admin-1", request.DecidedByUserId);
        Assert.Equal("First decision", request.DecisionReason);

        var auditEventCount = await verify.AccessRequestAuditEvents.CountAsync(e => e.AccessRequestId == id);
        Assert.Equal(2, auditEventCount); // Created + the one successful Denied — no event for the rejected Approve attempt
    }

    [Fact]
    public async Task DenyAsync_OnAlreadyDecidedRequest_Throws()
    {
        int id;
        using (var db = CreateContext())
        {
            id = await new AccessRequestService(db).CreateAsync("Salesforce", "Need it for sales.", "user-1", CancellationToken.None);
        }

        using (var db = CreateContext())
        {
            await new AccessRequestService(db).ApproveAsync(id, "admin-1", null, CancellationToken.None);
        }

        using var db2 = CreateContext();
        await Assert.ThrowsAsync<InvalidAccessRequestTransitionException>(
            () => new AccessRequestService(db2).DenyAsync(id, "admin-2", "Too late", CancellationToken.None));
    }
}
