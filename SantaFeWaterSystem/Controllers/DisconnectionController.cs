using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Services;
using SantaFeWaterSystem.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Controllers
{
    public class DisconnectionController : Controller
    {
        private readonly ApplicationDbContext _context;
        private const int PageSize = 10;
        private readonly AuditLogService _audit;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public DisconnectionController(ApplicationDbContext context, AuditLogService audit, IHttpContextAccessor httpContextAccessor)
        {
            _context = context;
            _audit = audit;
            _httpContextAccessor = httpContextAccessor;
        }

        private string GetCurrentUsername()
        {
            return _httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "Unknown";
        }

        public async Task<IActionResult> Index(string searchTerm, string sortOrder, int page = 1, int pageSize = PageSize)
        {
            var today = DateTime.Today;

            // Step 1: Get overdue billing info grouped by ConsumerId
            var overdueGrouped = await _context.Billings
                .Where(b => !b.IsPaid && b.DueDate < today)
                .GroupBy(b => b.ConsumerId)
                .Where(g => g.Count() >= 2)
                .Select(g => new
                {
                    ConsumerId = g.Key,
                    OverdueBillsCount = g.Count(),
                    TotalUnpaidAmount = g.Sum(b => b.TotalAmount),
                    LatestDueDate = g.Max(b => b.DueDate)
                })
                .ToListAsync();

            var consumerIds = overdueGrouped.Select(g => g.ConsumerId).ToList();

            // Step 2: Fetch consumer data
            var consumers = await _context.Consumers
                .Where(c => consumerIds.Contains(c.Id))
                .ToListAsync();

            // Step 3: Join consumers with their overdue billing data
            var disconnectionData = (from c in consumers
                                     join b in overdueGrouped on c.Id equals b.ConsumerId
                                     select new DisconnectionViewModel
                                     {
                                         ConsumerId = c.Id,
                                         ConsumerName = c.FullName,
                                         OverdueBillsCount = b.OverdueBillsCount,
                                         TotalUnpaidAmount = b.TotalUnpaidAmount,
                                         LatestDueDate = b.LatestDueDate,
                                         IsDisconnected = c.IsDisconnected
                                     }).ToList();

            // Step 4: Search filter
            if (!string.IsNullOrEmpty(searchTerm))
            {
                searchTerm = searchTerm.ToLower();
                disconnectionData = disconnectionData
                    .Where(d => d.ConsumerName.ToLower().Contains(searchTerm) || d.ConsumerId.ToString().Contains(searchTerm))
                    .ToList();
            }

            // Step 5: Sorting
            disconnectionData = sortOrder switch
            {
                "name_desc" => disconnectionData.OrderByDescending(d => d.ConsumerName).ToList(),
                "overdue" => disconnectionData.OrderBy(d => d.OverdueBillsCount).ToList(),
                "overdue_desc" => disconnectionData.OrderByDescending(d => d.OverdueBillsCount).ToList(),
                "amount" => disconnectionData.OrderBy(d => d.TotalUnpaidAmount).ToList(),
                "amount_desc" => disconnectionData.OrderByDescending(d => d.TotalUnpaidAmount).ToList(),
                "date" => disconnectionData.OrderBy(d => d.LatestDueDate).ToList(),
                "date_desc" => disconnectionData.OrderByDescending(d => d.LatestDueDate).ToList(),
                _ => disconnectionData.OrderBy(d => d.ConsumerName).ToList()
            };

            // Step 6: Pagination
            var count = disconnectionData.Count();
            var items = disconnectionData
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            var paginated = new PaginatedList<DisconnectionViewModel>(items, count, page, pageSize);

            return View(paginated);
        }



        
        // GET: Disconnection/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var consumer = await _context.Consumers
                .Include(c => c.User)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (consumer == null)
                return NotFound();

            var overdueBills = await _context.Billings
                .Where(b => b.ConsumerId == id && !b.IsPaid && b.DueDate < DateTime.Today)
                .ToListAsync();

            var latestDueDate = overdueBills
                .OrderByDescending(b => b.DueDate)
                .FirstOrDefault()?.DueDate;

            var totalUnpaidAmount = overdueBills.Sum(b => b.TotalAmount);

            var disconnection = await _context.Disconnections
                .Where(d => d.ConsumerId == id)
                .OrderByDescending(d => d.DateDisconnected)
                .FirstOrDefaultAsync();

            var viewModel = new DisconnectionViewModel
            {
                ConsumerId = consumer.Id,
                ConsumerName = $"{consumer.FirstName} {consumer.LastName}",
                OverdueBillsCount = overdueBills.Count,
                TotalUnpaidAmount = totalUnpaidAmount,
                LatestDueDate = latestDueDate,
                IsDisconnected = disconnection != null
            };

            return View(viewModel); // ✅ now matches your view model structure
        }



        // POST: Disconnection/Disconnect/5
        [HttpPost]
        public async Task<IActionResult> Disconnect(int id)
        {
            var consumer = await _context.Consumers
                .Include(c => c.Billings)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (consumer == null)
                return NotFound();

            consumer.IsDisconnected = true;
            consumer.Status = "Disconnected";


            var disconnection = new Disconnection
            {
                ConsumerId = id,
                DateDisconnected = DateTime.Now,
                Remarks = "2 or more overdue bills",
                IsReconnected = false,
                Action = "Disconnected",
                 PerformedBy = GetCurrentUsername() ?? "Unknown" 
            };


            _context.Disconnections.Add(disconnection);

            // ➕ Create Notification
            var notif = new Notification
            {
                ConsumerId = consumer.Id,
                Title = "Disconnection Notice",
                Message = $"Hello {consumer.FirstName}, you failed to pay any bill from your 2 overdue bills within 3 days. Your water service has been disconnected. " +
                          $"To reconnect, please visit the main office of Santa Fe Water System located at the Santa Fe Municipal Hall. Thank you.",
                CreatedAt = DateTime.Now
            };
            _context.Notifications.Add(notif);

            // 🔍 Audit log
            var audit = new AuditTrail
            {
                Action = "Disconnect",
                PerformedBy = GetCurrentUsername(),
                Details = $"Disconnected Consumer ID {id} due to 2 or more overdue bills.",
                Timestamp = DateTime.Now
            };
            _context.AuditTrails.Add(audit);

            await _context.SaveChangesAsync();

            TempData["Message"] = $"Consumer {consumer.FirstName} has been disconnected and notified.";

            return RedirectToAction(nameof(Index));
        }

        // POST: Disconnection/Reconnect/5
        [HttpPost]
        public async Task<IActionResult> Reconnect(int id)
        {
            var consumer = await _context.Consumers.FindAsync(id);
            if (consumer == null)
                return NotFound();

            consumer.IsDisconnected = false;
            consumer.Status = "Active";


            var disconnection = await _context.Disconnections
                .Where(d => d.ConsumerId == id && !d.IsReconnected)
                .OrderByDescending(d => d.DateDisconnected)
                .FirstOrDefaultAsync();

            if (disconnection != null)
            {
                disconnection.IsReconnected = true;
                disconnection.DateReconnected = DateTime.Now;
            }

            // ➕ Create Notification
            var notif = new Notification
            {
                ConsumerId = consumer.Id,
                Title = "Reconnection Notice",
                Message = $"Hello {consumer.FirstName}, your water service has been successfully reconnected. Thank you for settling your bills. You may now continue using our services.",
                CreatedAt = DateTime.Now
            };
            _context.Notifications.Add(notif);

            // 🔍 Audit log
            var audit = new AuditTrail
            {
                Action = "Reconnect",
                PerformedBy = GetCurrentUsername(),
                Details = $"Reconnected Consumer ID {id}.",
                Timestamp = DateTime.Now
            };
            _context.AuditTrails.Add(audit);

            await _context.SaveChangesAsync();

            TempData["Message"] = $"Consumer {consumer.FirstName} has been reconnected and notified.";

            return RedirectToAction(nameof(Index));
        }




        // POST: Disconnection/Notify/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Notify(int id)
        {
            var consumer = _context.Consumers
                .Include(c => c.Billings)
                .FirstOrDefault(c => c.Id == id);

            if (consumer != null)
            {
                var overdueBills = consumer.Billings
                    .Where(b => !b.IsPaid && b.DueDate < DateTime.Today)
                    .ToList();

                if (overdueBills.Count >= 2)
                {
                    var notif = new Notification
                    {
                        ConsumerId = consumer.Id,
                        Title = "Disconnection Notice",
                        Message = $"Hello {consumer.FirstName}, you have 2 overdue bills that are not yet paid. Please pay at least one bill within 3 days to avoid disconnection.",
                        CreatedAt = DateTime.Now
                    };

                    _context.Notifications.Add(notif);

                    // 🔍 Audit log
                    var audit = new AuditTrail
                    {
                        Action = "Notify",
                        PerformedBy = GetCurrentUsername(),
                        Details = $"Sent disconnection notice to Consumer ID {id}.",
                        Timestamp = DateTime.Now
                    };
                    _context.AuditTrails.Add(audit);

                    _context.SaveChanges();
                    TempData["Message"] = $"Disconnection notice sent to {consumer.FirstName}.";
                }
                else
                {
                    TempData["Error"] = "Consumer does not meet disconnection criteria (must have 2 or more overdue bills).";
                }
            }
            else
            {
                TempData["Error"] = "Consumer not found.";
            }

            return RedirectToAction("Index");
        }

    }
}
