using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using SantaFeWaterSystem.Models.ViewModels;
using SantaFeWaterSystem.Services;

namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class PaymentsController : BaseController
    {
        
        private readonly IWebHostEnvironment _env;
        private readonly BillingService _billingService;
        private readonly PdfService _pdfService;

        public PaymentsController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            BillingService billingService,
            PdfService pdfService,
            PermissionService permissionService,
            AuditLogService audit) 
            : base(permissionService, context, audit)
        {
           
            _env = env;
            _billingService = billingService;
            _pdfService = pdfService;
        }



        // GET: Payments/ManagePayments
        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> ManagePayments(
            string searchTerm,
            string statusFilter,
            string paymentMethodFilter,
            int page = 1,
            int pageSize = 6)
        {
            var query = _context.Payments
                .Include(p => p.Billing)
                    .ThenInclude(b => b.Consumer)
                .AsQueryable();

            // Filter: Search by Consumer First or Last Name
            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(p =>
                    p.Billing.Consumer.FirstName.Contains(searchTerm) ||
                    p.Billing.Consumer.LastName.Contains(searchTerm));
            }

            // Filter: Status (Verified / Pending)
            if (statusFilter == "Verified")
            {
                query = query.Where(p => p.IsVerified);
            }
            else if (statusFilter == "Pending")
            {
                query = query.Where(p => !p.IsVerified);
            }

            // Filter: Payment Method
            if (!string.IsNullOrEmpty(paymentMethodFilter))
            {
                query = query.Where(p => p.Method == paymentMethodFilter);
            }

            var totalPayments = await query.CountAsync();
            var totalPages = (int)Math.Ceiling(totalPayments / (double)pageSize);

            var payments = await query
                .OrderByDescending(p => p.PaymentDate)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(p => new PaymentViewModel
                {
                    PaymentId = p.Id,
                    FirstName = p.Billing.Consumer.FirstName,
                    LastName = p.Billing.Consumer.LastName,
                    BillingDate = p.Billing.BillingDate,
                    PaymentDate = p.PaymentDate,
                    AmountPaid = p.AmountPaid,
                    PaymentMethod = p.Method,
                    TransactionId = p.TransactionId,
                    ReceiptImageUrl = p.ReceiptPath,
                    IsVerified = p.IsVerified
                })
                .ToListAsync();

            var viewModel = new PaginatedPaymentsViewModel
            {
                Payments = payments,
                PageNumber = page,
                TotalPages = totalPages,
                SearchTerm = searchTerm,
                StatusFilter = statusFilter,
                PaymentMethodFilter = paymentMethodFilter
            };

            return View(viewModel);
        }




        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        public async Task<IActionResult> ExportSelectedToPdf([FromForm] List<int> selectedIds)
        {
            if (selectedIds == null || !selectedIds.Any())
                return BadRequest("No payments selected.");

            var selectedPayments = await _context.Payments
                .Include(p => p.Billing)
                    .ThenInclude(b => b.Consumer)
                .Where(p => selectedIds.Contains(p.Id))
                .ToListAsync();

            var pdfBytes = _pdfService.GeneratePaymentsPdf(selectedPayments);

            return File(pdfBytes, "application/pdf", "SelectedPayments.pdf");
        }












        // GET: Create Walk-in Payment
        [Authorize(Roles = "Admin,Staff")]
        [HttpGet]
        public IActionResult CreatePayment()
        {
            // ✅ Only consumers with at least one unpaid billing
            var unpaidConsumers = _context.Consumers
                .Where(c => _context.Billings.Any(b => b.ConsumerId == c.Id && b.Status != "Paid"))
                .OrderBy(c => c.FirstName)
                .Select(c => new
                {
                    c.Id,
                    FullName = c.FirstName + " " + c.LastName
                })
                .ToList();

            ViewBag.Consumers = new SelectList(unpaidConsumers, "Id", "FullName");

            return View();
        }



        // POST: Create Walk-in Payment
        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePayment(Payment payment)
        {
            if (ModelState.IsValid)
            {
                payment.PaymentDate = DateTime.Now;

                // Set who processed the walk-in payment
                var username = User.Identity?.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
                payment.ProcessedBy = user?.FullName ?? username ?? "Unknown";

                // Add payment
                _context.Payments.Add(payment);

                // Mark related billing as Paid
                var billing = await _context.Billings.FindAsync(payment.BillingId);
                if (billing != null)
                {
                    billing.Status = "Paid";
                }

                // Get consumer for audit trail
                var consumer = await _context.Consumers.FindAsync(payment.ConsumerId);

                // Add audit log
                _context.AuditTrails.Add(new AuditTrail
                {
                    Action = $"Created walk-in payment for consumer: {consumer?.FirstName} {consumer?.LastName}, " +
                             $"Amount: ₱{payment.AmountPaid:N2}, Billing Date: {billing?.BillingDate:MMMM dd, yyyy}",
                    PerformedBy = user?.FullName ?? username ?? "Admin",
                    Timestamp = DateTime.Now
                });

                // Save everything together
                await _context.SaveChangesAsync();

                return RedirectToAction("WalkInConfirmation", new { id = payment.Id });
            }

            // Rebuild consumer list with unpaid billings only
            var unpaidConsumers = _context.Consumers
                .Where(c => _context.Billings.Any(b => b.ConsumerId == c.Id && b.Status == "Unpaid"))
                .OrderBy(c => c.FirstName)
                .Select(c => new
                {
                    c.Id,
                    FullName = c.FirstName + " " + c.LastName +
                        (c.User != null && !string.IsNullOrEmpty(c.User.AccountNumber)
                            ? $" ({c.User.AccountNumber})"
                            : "")
                })
                .ToList();

            ViewBag.Consumers = new SelectList(unpaidConsumers, "Id", "FullName", payment.ConsumerId);
            return View(payment);
        }




        // API: Get latest unpaid billing for selected consumer
        [HttpGet]
        public IActionResult GetLatestBilling(int consumerId)
        {
            var billing = _context.Billings
                .Where(b => b.ConsumerId == consumerId && !b.IsPaid)
                .OrderByDescending(b => b.BillingDate)
                .Select(b => new
                {
                    b.Id,
                    b.BillingDate,
                    CubicMeter = b.CubicMeterUsed,
                    b.AmountDue,
                    b.AdditionalFees,
                    TotalAmount = b.TotalAmount
                })
                .FirstOrDefault();

            if (billing == null)
                return NotFound();

            return Json(billing);
        }



        public IActionResult AuditLogs()
        {
            var logs = _context.AuditTrails
                .OrderByDescending(a => a.Timestamp)
                .ToList();

            return View(logs);
        }


        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> WalkInConfirmation(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Consumer)
                    .ThenInclude(c => c.User) // Include User to get AccountNumber
                .Include(p => p.Billing)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
                return NotFound();

            return View(payment);
        }


        [Authorize(Roles = "Admin,Staff")]
        public async Task<IActionResult> DownloadReceipt(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Consumer)
                .Include(p => p.Billing)
                 .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
                return NotFound();

            var pdfBytes = WalkInReceiptPdfService.Generate(payment, payment.Consumer, payment.Billing);
            return File(pdfBytes, "application/pdf", $"WalkInReceipt_{id}.pdf");
        }







        // GET: Payments/Edit/5
        [Authorize(Roles = "Admin,Staff")]
        public IActionResult Edit(int id)
        {
            var payment = _context.Payments
                .Include(p => p.Consumer)
                .Include(p => p.Billing)
                .FirstOrDefault(p => p.Id == id);

            if (payment == null) return NotFound();

            var model = new CreatePaymentViewModel
            {
                PaymentId = payment.Id,
                ConsumerId = payment.ConsumerId,
                BillingId = payment.BillingId,
                PaymentDate = payment.PaymentDate,
                AmountPaid = payment.AmountPaid,
                Method = payment.Method,
                TransactionId = payment.TransactionId,
                ExistingReceiptImageUrl = payment.ReceiptPath, // ✅ Consistent naming
                Consumers = _context.Consumers.Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.FirstName
                }).ToList(),
                Billings = _context.Billings.Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.BillingDate.ToShortDateString()
                }).ToList()
            };

            return View(model);
        }

        // POST: Payments/Edit/5
        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(CreatePaymentViewModel model)
        {
            if (!ModelState.IsValid)
            {
                model.Consumers = _context.Consumers.Select(c => new SelectListItem
                {
                    Value = c.Id.ToString(),
                    Text = c.FirstName
                }).ToList();

                model.Billings = _context.Billings.Select(b => new SelectListItem
                {
                    Value = b.Id.ToString(),
                    Text = b.BillingDate.ToShortDateString()
                }).ToList();

                return View(model);
            }

            var payment = await _context.Payments
                .Include(p => p.Consumer)
                .FirstOrDefaultAsync(p => p.Id == model.PaymentId);

            if (payment == null) return NotFound();

            // Keep old values for audit
            var oldAmount = payment.AmountPaid;
            var oldMethod = payment.Method;
            var oldDate = payment.PaymentDate;
            var oldTransactionId = payment.TransactionId;
            var oldReceiptPath = payment.ReceiptPath;

            // Update values
            payment.PaymentDate = model.PaymentDate;
            payment.AmountPaid = model.AmountPaid;
            payment.Method = model.Method;
            payment.TransactionId = model.TransactionId;

            if (model.ReceiptImageFile != null)
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "receipts");
                Directory.CreateDirectory(uploadsFolder);

                var fileName = Guid.NewGuid() + Path.GetExtension(model.ReceiptImageFile.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await model.ReceiptImageFile.CopyToAsync(stream);
                }

                payment.ReceiptPath = "/uploads/receipts/" + fileName;
            }

            _context.Update(payment);

            // Add audit trail
            _context.AuditTrails.Add(new AuditTrail
            {
                Action = $"Edited payment for consumer: {payment.Consumer?.FirstName}. " +
                         $"Old Amount: ₱{oldAmount:N2}, New Amount: ₱{model.AmountPaid:N2}. " +
                         $"Old Method: {oldMethod ?? "N/A"}, New Method: {model.Method ?? "N/A"}. " +
                         $"Old Date: {oldDate:MMMM dd, yyyy}, New Date: {model.PaymentDate:MMMM dd, yyyy}. " +
                         $"Old Transaction ID: {oldTransactionId ?? "-"}. New: {model.TransactionId ?? "-"}.",
                PerformedBy = User.Identity?.Name ?? "Admin",
                Timestamp = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManagePayments));
        }







        // GET: Payments/Details/5
        [Authorize(Roles = "Admin,Staff")]
        public IActionResult Details(int id)
        {
            var payment = _context.Payments
                .Include(p => p.Consumer)
                .Include(p => p.Billing)
                .FirstOrDefault(p => p.Id == id);

            if (payment == null)
                return NotFound();
            var model = new PaymentViewModel
            {
                PaymentId = payment.Id,
                FirstName = payment.Consumer.FirstName,
                LastName = payment.Consumer.LastName,
                BillingDate = payment.Billing.BillingDate,
                PaymentDate = payment.PaymentDate,
                AmountPaid = payment.AmountPaid,
                PaymentMethod = payment.Method,
                TransactionId = payment.TransactionId,
                ReceiptImageUrl = payment.ImageUrl,
                IsVerified = payment.IsVerified
            };


            return View(model);
        }











        // GET: Payments/Delete/5
        [Authorize(Roles = "Admin,Staff")]
        public IActionResult Delete(int id)
        {
            var payment = _context.Payments
                .Include(p => p.Consumer)
                .Include(p => p.Billing)
                .FirstOrDefault(p => p.Id == id);

            if (payment == null)
                return NotFound();

            var model = new PaymentViewModel
            {
                PaymentId = payment.Id,
                FirstName = payment.Consumer.FirstName,
                LastName = payment.Consumer.LastName,
                BillingDate = payment.Billing.BillingDate,
                PaymentDate = payment.PaymentDate,
                AmountPaid = payment.AmountPaid,
                PaymentMethod = payment.Method,
                TransactionId = payment.TransactionId,
                ReceiptImageUrl = payment.ImageUrl,
                IsVerified = payment.IsVerified
            };

            return View(model);
        }

        // POST: Payments/Delete/5
        [Authorize(Roles = "Admin,Staff")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Consumer)
                .Include(p => p.Billing)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (payment == null)
                return NotFound();

            // ✅ Revert billing status and mark as unpaid
            if (payment.Billing != null)
            {
                payment.Billing.MarkAsUnpaid(); // ✅ Cleaner, uses your model logic
                _context.Billings.Update(payment.Billing);
            }


            // ✅ Add audit trail before deletion
            _context.AuditTrails.Add(new AuditTrail
            {
                Action = $"Deleted payment for {payment.Consumer?.FirstName} {payment.Consumer?.LastName}, " +
                         $"Amount: ₱{payment.AmountPaid:N2}, Payment Date: {payment.PaymentDate:MMMM dd, yyyy}",
                PerformedBy = User.Identity?.Name ?? "System",
                Timestamp = DateTime.Now
            });

            // ✅ Remove the payment record
            _context.Payments.Remove(payment);
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManagePayments));
        }










        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        public async Task<IActionResult> Verify(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Consumer)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            payment.IsVerified = true;

            var billing = await _context.Billings.FindAsync(payment.BillingId);
            if (billing != null)
            {
                billing.Status = "Paid";
                billing.IsPaid = true;
            }

            // Add audit trail with full consumer name and amount
            _context.AuditTrails.Add(new AuditTrail
            {
                Action = $"Verified online payment for consumer: {payment.Consumer?.FirstName} {payment.Consumer?.LastName}, Amount: ₱{payment.AmountPaid:N2}",
                PerformedBy = User.Identity?.Name ?? "Admin",
                Timestamp = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManagePayments));
        }

        [Authorize(Roles = "Admin,Staff")]
        [HttpPost]
        public async Task<IActionResult> Unverify(int id)
        {
            var payment = await _context.Payments
                .Include(p => p.Consumer)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (payment == null) return NotFound();

            payment.IsVerified = false;

            var billing = await _context.Billings.FindAsync(payment.BillingId);
            if (billing != null)
            {
                billing.Status = "Pending";
                billing.IsPaid = false;
            }

            // Add audit trail with full consumer name and amount
            _context.AuditTrails.Add(new AuditTrail
            {
                Action = $"Unverified online payment for consumer: {payment.Consumer?.FirstName} {payment.Consumer?.LastName}, Amount: ₱{payment.AmountPaid:N2}",
                PerformedBy = User.Identity?.Name ?? "Admin",
                Timestamp = DateTime.Now
            });

            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(ManagePayments));
        }


    }
}
