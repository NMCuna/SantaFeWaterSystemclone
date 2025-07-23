using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Models.ViewModels;
using X.PagedList;
using SantaFeWaterSystem.Services;
using SantaFeWaterSystem.Settings;
using Microsoft.Extensions.Options;
using QuestPDF.Infrastructure;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class NotificationsController(
        ApplicationDbContext context,
        ISmsQueue smsQueue,
        ISemaphoreSmsService smsService,       
        PermissionService permissionService,
        IWebHostEnvironment env,
        IOptions<SemaphoreSettings> semaphoreOptions,
        AuditLogService audit
    ) : BaseController(permissionService, context, audit)
    {
        private const int PageSize = 5;

        private readonly ISmsQueue _smsQueue = smsQueue;
        private readonly ISemaphoreSmsService _smsService = smsService;        
        private readonly IWebHostEnvironment _env = env;
        private readonly SemaphoreSettings _semaphoreSettings = semaphoreOptions.Value;





        [Authorize(Roles = "Admin,Staff")]
        public IActionResult Index(string search, int pageNumber = 1)
        {
            int pageSize = 7;
            var query = _context.Notifications
                .Include(n => n.Consumer)
                .Where(n => !n.IsArchived);

            if (!string.IsNullOrEmpty(search))
                query = query.Where(n => n.Title.Contains(search) || n.Message.Contains(search));

            var ordered = query.OrderByDescending(n => n.CreatedAt);
            var total = ordered.Count();
            var items = ordered.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

            var paged = new StaticPagedList<Notification>(items, pageNumber, pageSize, total);
            return View(paged);
        }


        [Authorize(Roles = "User,Admin,Staff")]
        [HttpPost]
        public async Task<IActionResult> MarkAsRead([FromBody] int id)
        {
            var notif = await _context.Notifications.FindAsync(id);
            if (notif == null)
                return NotFound();

            if (!notif.IsRead)
            {
                notif.IsRead = true;
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteViaAjax([FromBody] int id)
        {
            var notif = await _context.Notifications.FindAsync(id);
            if (notif == null) return NotFound();

            _context.Notifications.Remove(notif);
            await _context.SaveChangesAsync();
            return Ok();
        }




        [Authorize(Roles = "Admin,Staff")]
        // GET: Notification/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Notification/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Notification notification)
        {
            if (ModelState.IsValid)
            {
                notification.CreatedAt = DateTime.Now;

                if (notification.SendToAll)
                {
                    // Get all consumers
                    var consumers = await _context.Consumers.ToListAsync();

                    foreach (var consumer in consumers)
                    {
                        var userNotification = new Notification
                        {
                            Title = notification.Title,
                            Message = notification.Message,
                            ConsumerId = consumer.Id,
                            CreatedAt = DateTime.Now,
                            IsRead = false,
                            IsArchived = false,
                            SendToAll = true
                        };

                        _context.Notifications.Add(userNotification);
                    }
                }
                else
                {
                    // Individual (optional case)
                    _context.Notifications.Add(notification);
                }

                await _context.SaveChangesAsync();
                TempData["Success"] = "Notification sent successfully.";
                return RedirectToAction("Index", "Notifications");
            }

            return View(notification);
        }




        [Authorize(Roles = "Admin")]
        [HttpGet]
        public async Task<IActionResult> Delete(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null)
                return NotFound();

            _context.Notifications.Remove(notification);
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }



        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        public async Task<IActionResult> Archive(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null)
                return NotFound();

            notification.IsArchived = true;
            await _context.SaveChangesAsync();

            return RedirectToAction("Index");
        }

        public IActionResult Archived()
        {
            var archived = _context.Notifications
                .Include(n => n.Consumer)
                .Where(n => n.IsArchived)
                .OrderByDescending(n => n.CreatedAt)
                .ToList();

            return View("Archived", archived);
        }

        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        public async Task<IActionResult> Unarchive(int id)
        {
            var notification = await _context.Notifications.FindAsync(id);
            if (notification == null)
                return NotFound();

            notification.IsArchived = false;
            await _context.SaveChangesAsync();

            return RedirectToAction("Archived");
        }




        // GET: SendSms
        [Authorize(Roles = "Admin,Staff")]
        [HttpGet]
        public IActionResult SendSms(string searchTerm, int page = 1)
        {
            var consumersQuery = _context.Billings
                .Where(b => !b.IsPaid && b.Consumer != null)
                .Select(b => b.ConsumerId)
                .Distinct()
                .Join(_context.Consumers.Include(c => c.User), id => id, c => c.Id, (id, c) => c);

            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                consumersQuery = consumersQuery.Where(c =>
                    c.FirstName.Contains(searchTerm) ||
                    c.LastName.Contains(searchTerm));
            }

            var totalConsumers = consumersQuery.Count();
            var consumersWithBills = consumersQuery
                .OrderBy(c => c.FirstName)
                .Skip((page - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            foreach (var c in consumersWithBills)
            {
                c.Billings = _context.Billings.Where(b => b.ConsumerId == c.Id && !b.IsPaid).ToList();
            }

            var viewModel = new SmsNotificationViewModel
            {
                SearchTerm = searchTerm,
                PageNumber = page,
                TotalPages = (int)Math.Ceiling(totalConsumers / (double)PageSize),
                ConsumersWithBills = consumersWithBills
            };

            return View(viewModel);
        }


        // POST: SendSms
        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendSms(SmsNotificationViewModel model)
        {
            List<Consumer> recipients;

            if (model.SendToAll)
            {
                recipients = _context.Billings
                    .Where(b => !b.IsPaid && b.Consumer != null)
                    .Select(b => b.ConsumerId)
                    .Distinct()
                    .Join(_context.Consumers.Include(c => c.User), id => id, c => c.Id, (id, c) => c)
                    .ToList();

                foreach (var c in recipients)
                {
                    c.Billings = _context.Billings.Where(b => b.ConsumerId == c.Id && !b.IsPaid).ToList();
                }
            }
            else
            {
                if (model.SelectedConsumerIds == null || !model.SelectedConsumerIds.Any())
                {
                    ModelState.AddModelError("", "Please select at least one consumer.");
                    ReloadModel(model);
                    return View(model);
                }

                recipients = _context.Consumers
                    .Where(c => model.SelectedConsumerIds.Contains(c.Id))
                    .Include(c => c.User)
                    .ToList();

                foreach (var c in recipients)
                {
                    c.Billings = _context.Billings.Where(b => b.ConsumerId == c.Id && !b.IsPaid).ToList();
                }
            }

            if (!recipients.Any())
            {
                ModelState.AddModelError("", "No consumers found to send SMS.");
                ReloadModel(model);
                return View(model);
            }

            foreach (var consumer in recipients)
            {
                if (!string.IsNullOrWhiteSpace(consumer.ContactNumber))
                {
                    var billing = consumer.Billings?.FirstOrDefault();
                    var amount = billing?.AmountDue.ToString("N2") ?? "0.00";
                    var dueDate = billing?.DueDate.ToString("MMMM dd") ?? "N/A";
                    var account = consumer.User?.AccountNumber ?? "N/A";

                    var personalizedMessage = model.Message
                        .Replace("{Name}", consumer.FirstName)
                        .Replace("{Amount}", amount)
                        .Replace("{DueDate}", dueDate)
                        .Replace("{AccountNumber}", account);

                    // Send using mock or real service
                    if (_env.IsDevelopment())
                    {
                        await _smsService.SendSmsAsync(consumer.ContactNumber, personalizedMessage);
                    }
                    else
                    {
                        await _smsService.SendSmsAsync(consumer.ContactNumber, personalizedMessage);
                    }

                    _context.Notifications.Add(new Notification
                    {
                        ConsumerId = consumer.Id,
                        Title = "Water Bill Reminder",
                        Message = personalizedMessage,
                        CreatedAt = DateTime.Now
                    });
                }
            }

            await _context.SaveChangesAsync();

            TempData["Message"] = $"SMS sent to {recipients.Count} consumer(s).";
            return RedirectToAction("SendSms", new { searchTerm = model.SearchTerm });
        }

        private void ReloadModel(SmsNotificationViewModel model)
        {
            var consumersQuery = _context.Billings
                .Where(b => !b.IsPaid && b.Consumer != null)
                .Select(b => b.ConsumerId)
                .Distinct()
                .Join(_context.Consumers.Include(c => c.User), id => id, c => c.Id, (id, c) => c);

            if (!string.IsNullOrWhiteSpace(model.SearchTerm))
            {
                consumersQuery = consumersQuery.Where(c =>
                    c.FirstName.Contains(model.SearchTerm) || c.LastName.Contains(model.SearchTerm));
            }

            var totalConsumers = consumersQuery.Count();
            var consumersWithBills = consumersQuery
                .OrderBy(c => c.FirstName)
                .Skip((model.PageNumber - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            foreach (var c in consumersWithBills)
            {
                c.Billings = _context.Billings.Where(b => b.ConsumerId == c.Id && !b.IsPaid).ToList();
            }

            model.ConsumersWithBills = consumersWithBills;
            model.TotalPages = (int)Math.Ceiling(totalConsumers / (double)PageSize);
        }




        [Authorize(Roles = "Admin")]
        public IActionResult SmsLogs()
        {
            var logs = _context.SmsLogs
                .Include(s => s.Consumer)
                .OrderByDescending(s => s.SentAt)
                .Take(100)
                .Select(s => new SmsLogViewModel
                {
                    ContactNumber = s.ContactNumber,
                    Message = s.Message,
                    SentAt = s.SentAt,
                    IsSuccess = s.IsSuccess,
                    ResponseMessage = s.ResponseMessage,
                    ConsumerName = s.Consumer != null
                        ? $"{s.Consumer.FirstName} {s.Consumer.LastName}"
                        : "N/A"
                })
                .ToList();

            return View(logs);
        }




       
    }


}

