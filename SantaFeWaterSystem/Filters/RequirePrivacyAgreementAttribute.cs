using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using System.Linq;

namespace SantaFeWaterSystem.Filters
{
    public class RequirePrivacyAgreementAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var httpContext = context.HttpContext;
            var db = (ApplicationDbContext)httpContext.RequestServices
                .GetService(typeof(ApplicationDbContext));

            // ✅ Step 0: Skip PrivacyController actions (avoid redirect loop)
            var path = httpContext.Request.Path.Value?.ToLower() ?? "";
            if (path.Contains("/privacy/agree") || path.Contains("/privacy/agreepolicy"))
            {
                base.OnActionExecuting(context);
                return;
            }

            // ✅ Step 1: Get logged-in username/account number from session
            var usernameOrAccountNumber = httpContext.Session.GetString("LoggedInUser");

            if (string.IsNullOrEmpty(usernameOrAccountNumber))
            {
                // Not logged in → let normal auth handle it
                base.OnActionExecuting(context);
                return;
            }

            // ✅ Step 2: Get user and linked consumer
            var user = db.Users
                .Include(u => u.Consumer)
                .FirstOrDefault(u =>
                    u.Username == usernameOrAccountNumber ||
                    u.AccountNumber == usernameOrAccountNumber);

            if (user == null || user.Consumer == null)
            {
                // Admin/staff or invalid → skip privacy check
                base.OnActionExecuting(context);
                return;
            }

            // ✅ Step 3: Get latest published policy
            var latestPolicy = db.PrivacyPolicies
                .OrderByDescending(p => p.Version)
                .FirstOrDefault();

            if (latestPolicy == null)
            {
                // No policy exists → allow everything
                base.OnActionExecuting(context);
                return;
            }

            // ✅ Step 4: Get user’s latest agreement
            var agreement = db.UserPrivacyAgreements
                .AsNoTracking()
                .Where(a => a.ConsumerId == user.Consumer.Id)
                .OrderByDescending(a => a.PolicyVersion)
                .FirstOrDefault();

            // ✅ Step 5: Redirect if missing or outdated
            if (agreement == null || agreement.PolicyVersion < latestPolicy.Version)
            {
                context.Result = new RedirectToActionResult(
                    "Agree",
                    "Privacy",
                    new { version = latestPolicy.Version }
                );
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
