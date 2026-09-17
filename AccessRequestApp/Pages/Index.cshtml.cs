using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AccessRequestApp.Pages;

public class IndexModel : PageModel
{
    // Home has nothing of its own; /Requests' existing [Authorize] handles sending
    // anonymous visitors to Login first.
    public IActionResult OnGet() => RedirectToPage("/Requests/Index");
}
