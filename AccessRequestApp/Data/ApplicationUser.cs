using Microsoft.AspNetCore.Identity;

namespace AccessRequestApp.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }

    public string? LastName { get; set; }

    /// <summary>First/last name if either is set, otherwise falls back to email.</summary>
    public string GetDisplayName()
    {
        var name = $"{FirstName} {LastName}".Trim();
        return name.Length > 0 ? name : (Email ?? UserName ?? Id);
    }
}
