using AccessRequestApp.Security;
using Microsoft.AspNetCore.Identity;

namespace AccessRequestApp.Data;

/// <summary>
/// Seeds roles and development-only demo users. Must only be invoked in Development
/// or behind an explicit opt-in — these credentials are not for production use.
/// </summary>
public static class DemoIdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var role in new[] { ApplicationRoles.Employee, ApplicationRoles.Administrator })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        await EnsureDemoUserAsync(userManager, "employee@example.test", "Employee1!", ApplicationRoles.Employee, cancellationToken);
        await EnsureDemoUserAsync(userManager, "admin@example.test", "Administrator1!", ApplicationRoles.Administrator, cancellationToken);
    }

    private static async Task EnsureDemoUserAsync(
        UserManager<ApplicationUser> userManager,
        string email,
        string password,
        string role,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            // No FirstName/LastName seeded here on purpose — demonstrates the fallback to email.
            user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true
            };

            var createResult = await userManager.CreateAsync(user, password);
            if (!createResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed demo user '{email}': {string.Join(", ", createResult.Errors.Select(e => e.Description))}");
            }
        }

        if (!await userManager.IsInRoleAsync(user, role))
        {
            await userManager.AddToRoleAsync(user, role);
        }
    }
}
