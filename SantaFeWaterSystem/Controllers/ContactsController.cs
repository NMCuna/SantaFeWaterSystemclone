using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SantaFeWaterSystem.Data;
using SantaFeWaterSystem.Models;
using System.Linq;
using System.Threading.Tasks;

namespace SantaFeWaterSystem.Controllers.Admin
{
    [Authorize(Roles = "Admin,Staff")]
    public class ContactsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public ContactsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Admin/Contacts
        public async Task<IActionResult> Index()
        {
            var contacts = await _context.ContactInfos.ToListAsync();
            return View(contacts);
        }

        // GET: Admin/Contacts/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/Contacts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ContactInfo model)
        {
            if (ModelState.IsValid)
            {
                _context.ContactInfos.Add(model);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Contact created successfully!";
                return RedirectToAction(nameof(Index));
            }
            return View(model);
        }

        // GET: Admin/Contacts/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var contact = await _context.ContactInfos.FindAsync(id);
            if (contact == null)
                return NotFound();

            return View(contact);
        }

        // POST: Admin/Contacts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ContactInfo model)
        {
            if (id != model.Id)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(model);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Contact updated successfully!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ContactExists(model.Id))
                        return NotFound();
                    else
                        throw;
                }
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // GET: Admin/Contacts/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var contact = await _context.ContactInfos.FirstOrDefaultAsync(m => m.Id == id);
            if (contact == null)
                return NotFound();

            return View(contact);
        }

        // POST: Admin/Contacts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var contact = await _context.ContactInfos.FindAsync(id);
            if (contact != null)
            {
                _context.ContactInfos.Remove(contact);
                await _context.SaveChangesAsync();
                TempData["Success"] = "Contact deleted successfully!";
            }
            return RedirectToAction(nameof(Index));
        }

        private bool ContactExists(int id)
        {
            return _context.ContactInfos.Any(e => e.Id == id);
        }
    }
}
