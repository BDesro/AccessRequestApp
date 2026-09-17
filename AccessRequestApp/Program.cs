using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using AccessRequestApp.Data;
using AccessRequestApp.Security;
using AccessRequestApp.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// By default, ASP.NET Core persists its Data Protection keys to disk, so a sign-in cookie issued
// before the app was closed still validates after it's relaunched. Keeping keys in memory instead
// means every fresh start invalidates old cookies, so a closed-and-reopened app always requires
// signing in again — the right behavior for a locally-run demo tool, not a load-balanced service.
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();

// No email sender is configured, so account confirmation cannot happen via email.
// Demo users are seeded pre-confirmed; this is not a production authentication setup.
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(
        "CanManageAccessRequests",
        policy => policy.RequireRole(ApplicationRoles.Administrator));
});

builder.Services.AddScoped<IAccessRequestService, AccessRequestService>();

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/Requests");
});

// Basic defense-in-depth, not comprehensive DDoS protection — see README.
// Partitioned by authenticated user ID when available, otherwise by remote IP.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, _) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("RateLimiting");
        var partitionKey = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? context.HttpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";
        logger.LogWarning("Rate limit exceeded for {PartitionKey} on {Path}", partitionKey, context.HttpContext.Request.Path);
        return ValueTask.CompletedTask;
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
    {
        var userId = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        var key = userId ?? $"ip:{httpContext.Connection.RemoteIpAddress}";
        var permitLimit = userId is not null ? 100 : 30;

        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("submission", httpContext =>
    {
        var key = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? $"ip:{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });

    options.AddPolicy("decisions", httpContext =>
    {
        var key = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? $"ip:{httpContext.Connection.RemoteIpAddress}";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    await DemoIdentitySeeder.SeedAsync(scope.ServiceProvider, app.Lifetime.ApplicationStopping);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseRouting();

app.UseRateLimiter();

app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
