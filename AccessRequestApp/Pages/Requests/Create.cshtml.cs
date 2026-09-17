using System.ComponentModel.DataAnnotations;
using AccessRequestApp.Data;
using AccessRequestApp.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace AccessRequestApp.Pages.Requests;

[EnableRateLimiting("submission")]
public sealed class CreateModel(IAccessRequestService requestService, UserManager<ApplicationUser> userManager) : PageModel
{
    [BindProperty]
    public CreateAccessRequestInput Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Challenge();
        }

        await requestService.CreateAsync(Input.SystemName, Input.BusinessJustification, userId, cancellationToken);

        return RedirectToPage("Index");
    }
}

public sealed class CreateAccessRequestInput
{
    [Required]
    [StringLength(100)]
    [Display(Name = "System")]
    public string SystemName { get; set; } = string.Empty;

    [Required]
    [StringLength(1000)]
    [Display(Name = "Business justification")]
    public string BusinessJustification { get; set; } = string.Empty;
}
