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
            var db = (ApplicationDbContext)context.HttpContext.RequestServices
                .GetService(typeof(ApplicationDbContext));

            // Step 1: Get logged-in username/account number from session
            var usernameOrAccountNumber = context.HttpContext.Session.GetString("LoggedInUser");

            if (string.IsNullOrEmpty(usernameOrAccountNumber))
            {
                // No logged-in user, skip
                base.OnActionExecuting(context);
                return;
            }

            // Step 2: Get user and linked consumer
            var user = db.Users
                .Include(u => u.Consumer)
                .FirstOrDefault(u =>
                    u.Username == usernameOrAccountNumber ||
                    u.AccountNumber == usernameOrAccountNumber);

            if (user == null || user.Consumer == null)
            {
                // No linked consumer (probably admin/staff)
                base.OnActionExecuting(context);
                return;
            }

            // Step 3: Get latest policy
            var latestPolicy = db.PrivacyPolicies
                .OrderByDescending(p => p.Version)
                .FirstOrDefault();

            if (latestPolicy == null)
            {
                // No policy exists yet — allow access
                base.OnActionExecuting(context);
                return;
            }

            // Step 4: Get user's latest agreement
            var agreement = db.UserPrivacyAgreements
                .AsNoTracking()
                .Where(a => a.ConsumerId == user.Consumer.Id)
                .OrderByDescending(a => a.PolicyVersion)
                .FirstOrDefault();

            // Step 5: Redirect if no agreement or outdated
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
