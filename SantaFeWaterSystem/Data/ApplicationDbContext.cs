using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Models;
using System.Collections.Generic;

namespace SantaFeWaterSystem.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Consumer> Consumers { get; set; }
        public DbSet<Billing> Billings { get; set; }
        public DbSet<Payment> Payments { get; set; }
        public DbSet<Disconnection> Disconnections { get; set; }
        public DbSet<Support> Supports { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<Setting> Settings { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }
        public DbSet<AuditTrailArchive> AuditTrailArchives { get; set; }
        public DbSet<Rate> Rates { get; set; }
        public DbSet<Permission> Permissions { get; set; }
        public DbSet<StaffPermission> StaffPermissions { get; set; }
        public DbSet<Feedback> Feedbacks { get; set; }
        public DbSet<SmsLog> SmsLogs { get; set; }
        public DbSet<BillNotification> BillNotifications { get; set; }
        public DbSet<UserPushSubscription> UserPushSubscriptions { get; set; }










        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasOne(u => u.Consumer)
                .WithOne(c => c.User)
                .HasForeignKey<Consumer>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade); // ✅ allow deleting user with linked consumer

            modelBuilder.Entity<Billing>()
                .Property(b => b.AmountDue)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Billing>()
                .Property(b => b.CubicMeterUsed)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Payment>()
                .Property(p => p.AmountPaid)
                .HasColumnType("decimal(18,2)");

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Consumer)
                .WithMany()
                .HasForeignKey(p => p.ConsumerId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Payment>()
                .HasOne(p => p.Billing)
                .WithMany()
                .HasForeignKey(p => p.BillingId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Notification>()
                .HasOne(n => n.Consumer)
                .WithMany(c => c.Notifications)
                .HasForeignKey(n => n.ConsumerId)
                .OnDelete(DeleteBehavior.SetNull);


            modelBuilder.Entity<StaffPermission>()
                .HasOne(sp => sp.Staff)
                .WithMany(u => u.StaffPermissions)
                .HasForeignKey(sp => sp.StaffId)
                .OnDelete(DeleteBehavior.Cascade);



            modelBuilder.Entity<StaffPermission>()
                .HasOne(sp => sp.Permission)
                .WithMany()
                .HasForeignKey(sp => sp.PermissionId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Feedback>()
                .HasOne(f => f.User)
                .WithMany(u => u.Feedbacks)
                .HasForeignKey(f => f.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BillNotification>()
    .HasOne(bn => bn.Billing)
    .WithMany() // No navigation collection in Billing
    .HasForeignKey(bn => bn.BillingId)
    .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<BillNotification>()
    .HasOne(bn => bn.Consumer)
    .WithMany()
    .HasForeignKey(bn => bn.ConsumerId)
    .OnDelete(DeleteBehavior.Restrict);

           
            modelBuilder.Entity<Disconnection>()
                .HasOne(d => d.Billing)
                .WithMany(b => b.Disconnections)
                .HasForeignKey(d => d.BillingId)
                .OnDelete(DeleteBehavior.Restrict);
        




        modelBuilder.Entity<Permission>().HasData(
     new Permission { Id = 1, Name = "ManageUsers", Description = "Access to user management" },
     new Permission { Id = 2, Name = "ManageConsumers", Description = "Access to consumer management" },
     new Permission { Id = 3, Name = "ManageBilling", Description = "Access to billing management" },
     new Permission { Id = 4, Name = "ManagePayments", Description = "Access to payment management" },
     new Permission { Id = 5, Name = "ManageDisconnections", Description = "Access to disconnection management" },
     new Permission { Id = 6, Name = "ViewReports", Description = "Access to reports" },
     new Permission { Id = 7, Name = "ManageNotifications", Description = "Access to notifications management" },
     new Permission { Id = 8, Name = "ManageSupport", Description = "Access to support management" },
     new Permission { Id = 9, Name = "ManageFeedback", Description = "Access to feedback management" },
    new Permission { Id = 10, Name = "RegisterAdmin", Description = "Permission to register new admins" },
    new Permission { Id = 11, Name = "RegisterUser", Description = "Permission to register new users" },
    new Permission { Id = 12, Name = "ManageQRCodes", Description = "Permission to manage QR codes" },
    new Permission { Id = 13, Name = "ManageRate", Description = "Permission to manage rates" },
    new Permission { Id = 14, Name = "EditUser", Description = "Permission to edit user details" },
    new Permission { Id = 15, Name = "ResetPassword", Description = "Permission to reset user password" },
    new Permission { Id = 16, Name = "DeleteUser", Description = "Permission to delete a user" },
    new Permission { Id = 17, Name = "Reset2FA", Description = "Permission to reset two-factor authentication" },
    new Permission { Id = 18, Name = "LockUser", Description = "Permission to lock a user account" },
    new Permission { Id = 19, Name = "UnlockUser", Description = "Permission to unlock a user account" },
    new Permission { Id = 20, Name = "ViewConsumer", Description = "Permission to view consumer details" },
    new Permission { Id = 21, Name = "EditConsumer", Description = "Permission to edit consumer" },
    new Permission { Id = 22, Name = "DeleteConsumer", Description = "Permission to delete consumer" },
    new Permission { Id = 23, Name = "ViewBilling", Description = "Permission to view billing records" },
    new Permission { Id = 24, Name = "EditBilling", Description = "Permission to edit billing records" },
    new Permission { Id = 25, Name = "DeleteBilling", Description = "Permission to delete billing records" },
    new Permission { Id = 26, Name = "NotifyBilling", Description = "Permission to send billing notifications" },
    new Permission { Id = 27, Name = "ViewPenaltyLog", Description = "Permission to view penalty history logs" },
    new Permission { Id = 28, Name = "ViewPayment", Description = "Permission to view payment records" },
    new Permission { Id = 29, Name = "EditPayment", Description = "Permission to edit payment records" },
    new Permission { Id = 30, Name = "DeletePayment", Description = "Permission to delete payment records" },
    new Permission { Id = 31, Name = "VerifyPayment", Description = "Permission to verify payment records" }
);
        }

    }
}
