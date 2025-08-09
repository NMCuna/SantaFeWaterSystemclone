using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Services;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff,User")]
    public class SupportsController : BaseController
    {
       

        public SupportsController(ApplicationDbContext context, PermissionService permissionService, AuditLogService audit)
            : base(permissionService, context, audit)
        {
          
        }

        // GET: Supports
        public async Task<IActionResult> Index(string statusFilter, int page = 1)
        {
            int pageSize = 5;

            var ticketsQuery = _context.Supports
                .Include(s => s.Consumer)
                .Where(s => !s.IsArchived);

            // Badge counts
            ViewBag.OpenCount = await ticketsQuery.CountAsync(s => s.Status == "Open");
            ViewBag.ResolvedCount = await ticketsQuery.CountAsync(s => s.Status == "Resolved");

            // Filtering
            if (!string.IsNullOrEmpty(statusFilter) && statusFilter != "All")
            {
                ticketsQuery = ticketsQuery.Where(s => s.Status == statusFilter);
            }

            ViewBag.CurrentFilter = statusFilter ?? "All";

            // Get total count for pagination
            var totalCount = await ticketsQuery.CountAsync();
            ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            ViewBag.CurrentPage = page;

            // Fetch paginated items
            var tickets = await ticketsQuery
                .OrderByDescending(s => s.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(tickets);
        }


        // GET: Supports/Archived
        public async Task<IActionResult> Archived()
        {
            var archived = await _context.Supports
                                .Include(s => s.Consumer)
                                .Where(s => s.IsArchived)
                                .OrderByDescending(s => s.CreatedAt)
                                .ToListAsync();

            return View("Archived", archived);
        }

        // GET: Supports/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var support = await _context.Supports
                .Include(s => s.Consumer)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (support == null) return NotFound();

            return View(support);
        }

        // GET: Supports/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var support = await _context.Supports.FindAsync(id);
            if (support == null) return NotFound();

            return View(support);
        }

        
        // POST: Supports/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,AdminReply,IsResolved")] Support model)
        {
            var support = await _context.Supports.FindAsync(id);
            if (support == null) return NotFound();

            // Allow editing only if reply already exists
            if (string.IsNullOrWhiteSpace(support.AdminReply))
                return RedirectToAction(nameof(Reply), new { id });

            support.AdminReply = model.AdminReply;
            support.IsResolved = model.IsResolved;
            support.Status = model.IsResolved ? "Resolved" : "Open";
            support.ResolvedAt = model.IsResolved ? DateTime.Now : null;
            support.RepliedAt = DateTime.Now; // Update replied timestamp

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }



        // POST: Supports/Archive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Archive(int id)
        {
            var ticket = await _context.Supports.FindAsync(id);
            if (ticket == null)
                return NotFound();

            ticket.IsArchived = true;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: Supports/Unarchive/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Unarchive(int id)
        {
            var ticket = await _context.Supports.FindAsync(id);
            if (ticket == null)
                return NotFound();

            ticket.IsArchived = false;
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Archived));
        }

        // POST: Supports/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var ticket = await _context.Supports.FindAsync(id);
            if (ticket == null)
                return NotFound();

            _context.Supports.Remove(ticket);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // GET: Supports/Reply/5
        public async Task<IActionResult> Reply(int? id)
        {
            if (id == null) return NotFound();

            var support = await _context.Supports
                .Include(s => s.Consumer)
                .FirstOrDefaultAsync(s => s.Id == id);

            if (support == null) return NotFound();

            // Only allow reply if not yet replied
            if (!string.IsNullOrWhiteSpace(support.AdminReply))
                return RedirectToAction(nameof(Edit), new { id });

            return View(support);
        }

        // POST: Supports/Reply/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reply(int id, [Bind("Id,AdminReply,IsResolved")] Support model)
        {
            var support = await _context.Supports.FindAsync(id);
            if (support == null) return NotFound();

            if (string.IsNullOrWhiteSpace(model.AdminReply))
            {
                ModelState.AddModelError("AdminReply", "Reply message is required.");
                return View(support);
            }

            support.AdminReply = model.AdminReply;
            support.RepliedAt = DateTime.Now;
            support.IsResolved = model.IsResolved;
            support.Status = model.IsResolved ? "Resolved" : "Open";
            support.ResolvedAt = model.IsResolved ? DateTime.Now : null;

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }



        [HttpPost]
        public async Task<IActionResult> MarkReplyAsSeen([FromBody] int id)
        {
            var ticket = await _context.Supports.FindAsync(id);
            if (ticket == null)
                return NotFound();

            if (!ticket.IsReplySeen)
            {
                ticket.IsReplySeen = true;
                await _context.SaveChangesAsync();
            }

            return Ok();
        }


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteViaAjax([FromBody] int id)
        {
            var ticket = await _context.Supports.FindAsync(id);
            if (ticket == null) return NotFound();

            _context.Supports.Remove(ticket);
            await _context.SaveChangesAsync();
            return Ok();
        }


        ////////////////////////User Sopport top bar////////////////////////



        [Authorize(Roles = "User")]
        public async Task<IActionResult> UserSupport()
        {
            var userIdClaim = User.FindFirst("UserId");
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Unauthorized();

            var consumer = await _context.Consumers.FirstOrDefaultAsync(c => c.UserId == userId);
            if (consumer == null) return NotFound();

            var supports = await _context.Supports
                .Where(s => s.ConsumerId == consumer.Id && !s.IsArchived)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            // ✅ Mark all seen replies as seen
            foreach (var support in supports.Where(s => s.AdminReply != null && !s.IsReplySeen))
            {
                support.IsReplySeen = true;
            }

            await _context.SaveChangesAsync();

            return View(supports);
        }


        [Authorize(Roles = "User")]
        [HttpGet]
        public IActionResult GetUnseenRepliesCount()
        {
            var userIdClaim = User.FindFirst("UserId"); // ✅ Use "UserId" claim
            if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
                return Json(new { count = 0 });

            var unseenCount = _context.Supports
                .Where(s =>
                    s.Consumer != null &&
                    s.Consumer.UserId == userId &&
                    !s.IsReplySeen &&
                    !string.IsNullOrEmpty(s.AdminReply))
                .Count();

            return Json(new { count = unseenCount });
        }



    }
}
