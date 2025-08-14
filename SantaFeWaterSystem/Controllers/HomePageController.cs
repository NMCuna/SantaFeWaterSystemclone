// Controllers/Admin/HomePageController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Controllers.Admin
{
    [Authorize(Roles = "Admin,Staff")]
    public class HomePageController : Controller
    {
        private readonly ApplicationDbContext _context;

        public HomePageController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var content = await _context.HomePageContents.FirstOrDefaultAsync();
            return View(content);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(HomePageContent model)
        {
            if (ModelState.IsValid)
            {
                _context.HomePageContents.Add(model);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        public async Task<IActionResult> Edit(int id)
        {
            var content = await _context.HomePageContents.FindAsync(id);
            if (content == null) return NotFound();
            return View(content);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(HomePageContent model)
        {
            if (ModelState.IsValid)
            {
                _context.Update(model);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // GET: show confirmation
        public async Task<IActionResult> Delete(int id)
        {
            var content = await _context.HomePageContents.FindAsync(id);
            if (content == null) return NotFound();
            return View(content);
        }

        // POST: actually delete
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var content = await _context.HomePageContents.FindAsync(id);
            if (content == null) return NotFound();

            _context.HomePageContents.Remove(content);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

    }
}
