using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Helpers;
using SantaFeWaterSystem.Services;
using SantaFeWaterSystem.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using X.PagedList;


namespace SantaFeWaterSystem.Controllers
{
    [Authorize(Roles = "Admin,Staff")]
    public class ReportsController : BaseController
    {
        
        private readonly IWebHostEnvironment _env;

        public ReportsController(
            ApplicationDbContext context,
            IWebHostEnvironment env,
            PermissionService permissionService,
            AuditLogService audit) 
            : base(permissionService, context, audit)
        {
            
            _env = env;
        }


        public async Task<IActionResult> Index(
     DateTime? startDate,
     DateTime? endDate,
     int? consumerId,
     string billingStatus,
     string selectedMonth,
     int paymentsPage = 1,
     int billingsPage = 1)
        {
            int pageSize = 5;

            // Start with base queries
            var billingsQuery = _context.Billings.Include(b => b.Consumer).AsQueryable();
            var paymentsQuery = _context.Payments.Include(p => p.Consumer).AsQueryable();

            // Handle selected month
            if (!string.IsNullOrWhiteSpace(selectedMonth) &&
                DateTime.TryParse($"{selectedMonth}-01", out var monthDate))
            {
                startDate = new DateTime(monthDate.Year, monthDate.Month, 1);
                endDate = startDate.Value.AddMonths(1).AddDays(-1);
            }

            // Apply date range filters
            if (startDate.HasValue)
            {
                billingsQuery = billingsQuery.Where(b => b.BillingDate >= startDate.Value);
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                billingsQuery = billingsQuery.Where(b => b.BillingDate <= endDate.Value);
                paymentsQuery = paymentsQuery.Where(p => p.PaymentDate <= endDate.Value);
            }

            // Apply consumer filter
            if (consumerId.HasValue)
            {
                billingsQuery = billingsQuery.Where(b => b.ConsumerId == consumerId.Value);
                paymentsQuery = paymentsQuery.Where(p => p.ConsumerId == consumerId.Value);
            }

            // Apply billing status filter
            billingStatus = billingStatus?.Trim();
            if (!string.IsNullOrEmpty(billingStatus) && billingStatus != "All")
            {
                billingsQuery = billingsQuery.Where(b => b.Status == billingStatus);
            }

            // Execute ToListAsync() separately for summary data (non-paged)
            var billingsList = await billingsQuery.ToListAsync();
            var paymentsList = await paymentsQuery.ToListAsync();

            // Dropdown: available billing months
            var availableMonths = _context.Billings
     .AsNoTracking()
     .Select(b => b.BillingDate)
     .AsEnumerable()
     .Select(d => new DateTime(d.Year, d.Month, 1))
     .Distinct()
     .OrderByDescending(d => d)
     .ToList(); // ✅ correct



            // ViewModel with paginated + full lists
            var viewModel = new ReportsViewModel
            {
                PagedBillings = await billingsQuery
                    .OrderByDescending(b => b.BillingDate)
                    .ToPagedListAsync(billingsPage, pageSize),

                PagedPayments = await paymentsQuery
                    .OrderByDescending(p => p.PaymentDate)
                    .ToPagedListAsync(paymentsPage, pageSize),

                Billings = billingsList,
                Payments = paymentsList,
                Consumers = await _context.Consumers.ToListAsync(),
                FilterStartDate = startDate,
                FilterEndDate = endDate,
                SelectedConsumerId = consumerId,
                SelectedBillingStatus = billingStatus ?? "All",
                AvailableMonths = availableMonths,
                SelectedMonth = selectedMonth
            };

            return View(viewModel);
        }


        /// <summary>
        /// Downloads a report for a selected month.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DownloadMonthlyReport(string selectedMonth)
        {
            if (string.IsNullOrEmpty(selectedMonth) ||
                !DateTime.TryParse($"{selectedMonth}-01", out var monthDate))
            {
                return BadRequest("Invalid month format.");
            }

            var start = new DateTime(monthDate.Year, monthDate.Month, 1);
            var end = start.AddMonths(1).AddDays(-1);

            var billings = await _context.Billings
                .Include(b => b.Consumer)
                .Where(b => b.BillingDate >= start && b.BillingDate <= end)
                .ToListAsync();

            var payments = await _context.Payments
                .Include(p => p.Consumer)
                .Where(p => p.PaymentDate >= start && p.PaymentDate <= end)
                .ToListAsync();

            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/logo.png");
            var pdf = ReportPdfService.GenerateReport(billings, payments, logoPath);

            var fileName = $"MonthlyReport_{start:yyyy_MM}.pdf";
            return File(pdf, "application/pdf", fileName);
        }

        /// <summary>
        /// Downloads a filtered PDF report (based on date range, consumer, billing status).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> DownloadPdf(DateTime? startDate, DateTime? endDate, int? consumerId, string billingStatus, string selectedMonth)
        {
            var billings = _context.Billings.Include(b => b.Consumer).AsQueryable();
            var payments = _context.Payments.Include(p => p.Consumer).AsQueryable();

            // Apply month filter if selected
            if (!string.IsNullOrEmpty(selectedMonth) &&
                DateTime.TryParse($"{selectedMonth}-01", out var monthDate))
            {
                startDate = new DateTime(monthDate.Year, monthDate.Month, 1);
                endDate = startDate.Value.AddMonths(1).AddDays(-1);
            }

            if (startDate.HasValue)
            {
                billings = billings.Where(b => b.BillingDate >= startDate.Value);
                payments = payments.Where(p => p.PaymentDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                billings = billings.Where(b => b.BillingDate <= endDate.Value);
                payments = payments.Where(p => p.PaymentDate <= endDate.Value);
            }

            if (consumerId.HasValue)
            {
                billings = billings.Where(b => b.ConsumerId == consumerId.Value);
                payments = payments.Where(p => p.ConsumerId == consumerId.Value);
            }

            if (!string.IsNullOrEmpty(billingStatus) && billingStatus != "All")
            {
                billings = billings.Where(b => b.Status == billingStatus);
            }

            var billingsList = await billings.ToListAsync();
            var paymentsList = await payments.ToListAsync();

            var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/images/logo.png");
            var pdfBytes = ReportPdfService.GenerateReport(billingsList, paymentsList, logoPath);

            var filename = string.IsNullOrEmpty(selectedMonth)
                ? "FilteredReport.pdf"
                : $"Report_{selectedMonth}.pdf";

            return File(pdfBytes, "application/pdf", filename);
        }
    }
}
