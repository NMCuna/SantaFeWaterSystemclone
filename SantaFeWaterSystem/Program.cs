using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Globalization;
using QuestPDF.Infrastructure;
using Microsoft.AspNetCore.Identity;
using SantaFeWaterSystem.Settings;
using SantaFeWaterSystem.Services;


QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<PdfService>();

builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();

builder.Services.Configure<SemaphoreSettings>(
    builder.Configuration.GetSection("Semaphore"));



// Register SMS services
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddScoped<ISemaphoreSmsService, MockSmsService>(); // ✅ TESTING ONLY
}
else
{
    builder.Services.AddScoped<ISemaphoreSmsService, SemaphoreSmsService>(); // ✅ FOR DEPLOYMENT  
}
// Always register the queue (required by NotificationsController)
builder.Services.AddSingleton<ISmsQueue, InMemorySmsQueue>();
builder.Services.AddHostedService<InMemorySmsQueue>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditLogService>();



builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddTransient<IEmailSender, SmtpEmailSender>();



// Register database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Configure cookie authentication
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/UserLogin";          // Adjust to your login path
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(20); // Cookie expiration
        options.SlidingExpiration = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // ✅ Always use Secure cookies
        options.Cookie.SameSite = SameSiteMode.Strict; // ✅ Optional but recommended
    });

// Add session support with timeout
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(20);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always; // ✅ Mark session cookie as Secure
    options.Cookie.SameSite = SameSiteMode.Lax; // Optional: use Strict or Lax
});

// Add MVC support
builder.Services.AddControllersWithViews();

// Add IHttpContextAccessor for session & user access
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Seed default admin user on startup (optional)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var cultureInfo = new CultureInfo("en-PH");
    CultureInfo.DefaultThreadCurrentCulture = cultureInfo;
    CultureInfo.DefaultThreadCurrentUICulture = cultureInfo;

    if (!db.Users.Any(u => u.Username == "admin"))
    {
        var adminUser = new User
        {
            Username = "admin",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
            Role = "Admin",  
            IsMfaEnabled = false
        };

        db.Users.Add(adminUser);
        db.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Order is important:
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
