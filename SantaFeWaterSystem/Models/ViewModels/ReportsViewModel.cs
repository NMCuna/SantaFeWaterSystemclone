using SantaFeWaterSystem.Models;
using System;
using System.Collections.Generic;
using X.PagedList;

namespace SantaFeWaterSystem.ViewModels
{
    public class ReportsViewModel
    {
        // Paginated Lists for Display Tables
        public IPagedList<Billing> PagedBillings { get; set; }
        public IPagedList<Payment> PagedPayments { get; set; }

        // Full Lists for Summary Calculations
        public List<Billing> Billings { get; set; } = new();
        public List<Payment> Payments { get; set; } = new();

        // Dropdown and Filtering
        public List<Consumer> Consumers { get; set; }
        public DateTime? FilterStartDate { get; set; }
        public DateTime? FilterEndDate { get; set; }
        public int? SelectedConsumerId { get; set; }
        public string SelectedBillingStatus { get; set; }

        // Month Dropdown for Export
        public List<DateTime> AvailableMonths { get; set; } = new();
        public string SelectedMonth { get; set; }

        public decimal TotalUnpaid { get; set; }
        public decimal TotalRevenue { get; set; }

    }
}
