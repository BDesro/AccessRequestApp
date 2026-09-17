using System.ComponentModel.DataAnnotations;
using AccessRequestApp.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessRequestApp.Pages;

[Authorize]
public sealed class ProfileModel(UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public ProfileInput Input { get; set; } = new();

    [TempData]
    public string? StatusMessage { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        Input.FirstName = user.FirstName;
        Input.LastName = user.LastName;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        user.FirstName = string.IsNullOrWhiteSpace(Input.FirstName) ? null : Input.FirstName.Trim();
        user.LastName = string.IsNullOrWhiteSpace(Input.LastName) ? null : Input.LastName.Trim();

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }
            return Page();
        }

        StatusMessage = "Your name has been updated.";
        return RedirectToPage();
    }
}

public sealed class ProfileInput
{
    [StringLength(100)]
    [Display(Name = "First name")]
    public string? FirstName { get; set; }

    [StringLength(100)]
    [Display(Name = "Last name")]
    public string? LastName { get; set; }
}
