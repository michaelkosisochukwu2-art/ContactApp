using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MyOnlineCv.Data;
using MyOnlineCv.Models;

namespace MyOnlineCv.Pages
{
    public class ContactModel : PageModel
    {
        private readonly ApplicationDbContext _context;

        public ContactModel(ApplicationDbContext context)
        {
            _context = context;
        }

        [BindProperty]
        public ContactRequest ContactRequest { get; set; } = new();

        public List<ContactRequest> Messages { get; set; } = new();

        public void OnGet()
        {
            Messages = _context.ContactRequests.ToList();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                Messages = _context.ContactRequests.ToList();
                return Page();
            }

            _context.ContactRequests.Add(ContactRequest);
            await _context.SaveChangesAsync();

            return RedirectToPage();
        }
    }
}