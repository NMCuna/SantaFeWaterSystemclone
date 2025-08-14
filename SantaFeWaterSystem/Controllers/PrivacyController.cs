using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Linq;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "User")] // Only consumers
    public class PrivacyController : Controller
    {
        private readonly ApplicationDbContext _context;

        public PrivacyController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> Agree()
        {
            var latestPolicy = await _context.PrivacyPolicies
                .Include(p => p.Sections) // Include related sections
                .OrderByDescending(p => p.Version)
                .FirstOrDefaultAsync();

            if (latestPolicy == null)
                return NotFound("Privacy policy not found.");

            return View(latestPolicy);
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AgreePolicy(int policyVersion)
        {
            // Get current logged-in username (could be account number or admin username)
            var usernameOrAccountNumber = User.Identity?.Name;

            // Get the User record
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == usernameOrAccountNumber || u.AccountNumber == usernameOrAccountNumber);

            if (user == null)
                return Unauthorized("User not found.");

            // Find the linked Consumer
            var consumer = await _context.Consumers
                .FirstOrDefaultAsync(c => c.UserId == user.Id);

            if (consumer == null)
                return Unauthorized("Linked consumer not found.");

            // Save agreement
            var agreement = new UserPrivacyAgreement
            {
                ConsumerId = consumer.Id,
                PolicyVersion = policyVersion,
                AgreedAt = DateTime.UtcNow
            };

            _context.UserPrivacyAgreements.Add(agreement);
            await _context.SaveChangesAsync();

            return RedirectToAction("Dashboard", "User");
        }
    }
}
