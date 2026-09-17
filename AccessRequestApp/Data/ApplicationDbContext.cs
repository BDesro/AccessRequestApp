using AccessRequestApp.Domain;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AccessRequestApp.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<AccessRequest> AccessRequests => Set<AccessRequest>();

    public DbSet<AccessRequestAuditEvent> AccessRequestAuditEvents => Set<AccessRequestAuditEvent>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<AccessRequest>()
            .HasMany(r => r.AuditEvents)
            .WithOne(e => e.AccessRequest)
            .HasForeignKey(e => e.AccessRequestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
