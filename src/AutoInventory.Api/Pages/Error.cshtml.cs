using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutoInventory.Api.Pages;

[IgnoreAntiforgeryToken]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class ErrorModel : PageModel
{
    public string Title { get; private set; } = "Er ging iets mis";
    public string Text { get; private set; } = "Probeer het later opnieuw.";
    public string? TraceId { get; private set; }

    public void OnGet(int? code) => Set(code);
    public void OnPost(int? code) => Set(code);

    private void Set(int? code)
    {
        if (code == StatusCodes.Status404NotFound)
        {
            Title = "Niet gevonden";
            Text = "Deze pagina of dit voertuig bestaat niet (meer).";
            return;
        }
        TraceId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
    }
}
