using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using MyOnlineCv.Data;
using MyOnlineCv.Models;

namespace MyOnlineCv.Controller
{
    public class ContactController : ControllerBase
    {
       private readonly ApplicationDbContext _context;
        public ContactController(ApplicationDbContext context)
        {
            _context = context;
        }
        [HttpPost("api/contact")]
        public IActionResult SubmitContactRequest([FromBody] ContactRequest contactRequest)
        {
            if (string.IsNullOrEmpty(contactRequest.Name) || string.IsNullOrEmpty(contactRequest.Email) || string.IsNullOrEmpty(contactRequest.Message))
            {
                return BadRequest(new { error = "Email and Message are required." });
            }
            _context.ContactRequests.Add(contactRequest);
            _context.SaveChanges();
            return Ok(new { status = "Success", message = "Your message has been submitted successfully!", data = contactRequest });
        }
        [HttpGet]
        public IActionResult GetMessages()
        {
            var messages = _context.ContactRequests.ToList();
            return Ok(messages);
        }
    }
}
