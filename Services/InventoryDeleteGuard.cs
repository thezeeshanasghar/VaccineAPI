using System.Linq;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // Deleting a clinic / brand / doctor / user / dose can cascade (or orphan) batches, counters and
    // dose records that the inventory ledger still refers to. None of those deletes may run while
    // real stock or pending doses exist: stock must first be moved out or written off through the
    // normal inventory operations. Returns an explanation, or null when the delete is safe.
    public static class InventoryDeleteGuard
    {
        public static string? ForClinic(Context db, long clinicId)
        {
            bool hasStock = db.Stocks.Include(s => s.Bill).Any(s => s.Quantity != 0
                && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)));
            if (hasStock)
                return "This clinic still holds stock. Move it out (transfer) or write it off (Adjust Stock) before deleting the clinic.";
            bool hasHistory = db.Stocks.Include(s => s.Bill).Any(s =>
                    s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId))
                || db.InventoryTransactions.Any(t => t.ClinicId == clinicId);
            if (hasHistory)
                return "This clinic has stock history (batches or inventory records). It cannot be deleted without erasing that trail; deactivate it instead.";
            if (db.UnbatchedUses.Any(u => u.ClinicId == clinicId && u.Status == UnbatchedUseStatus.Pending))
                return "This clinic has doses waiting for batch details. Resolve them before deleting the clinic.";
            return null;
        }

        public static string? ForBrand(Context db, long brandId)
        {
            if (db.Stocks.Any(s => s.BrandId == brandId && s.Quantity != 0))
                return "This brand still has stock. Write it off or transfer it before deleting the brand.";
            if (db.Stocks.Any(s => s.BrandId == brandId) || db.InventoryTransactions.Any(t => t.BrandId == brandId))
                return "This brand has stock history (batches or inventory records) and cannot be deleted without erasing that trail.";
            if (db.UnbatchedUses.Any(u => u.BrandId == brandId && u.Status == UnbatchedUseStatus.Pending))
                return "This brand has doses waiting for batch details. Resolve them before deleting it.";
            return null;
        }

        public static string? ForDoctor(Context db, long doctorId)
        {
            foreach (var clinicId in db.Clinics.Where(c => c.DoctorId == doctorId).Select(c => c.Id).ToList())
            {
                var err = ForClinic(db, clinicId);
                if (err != null) return err;
            }
            return null;
        }

        public static string? ForUser(Context db, long userId)
        {
            var doctorIds = db.Doctors.Where(d => d.UserId == userId).Select(d => d.Id).ToList();
            foreach (var id in doctorIds)
            {
                var err = ForDoctor(db, id);
                if (err != null) return err;
            }
            return null;
        }

        public static string? ForDose(Context db, long doseId)
        {
            if (db.Schedules.Any(s => s.DoseId == doseId && s.IsDone && s.BrandId != null))
                return "Doses of this schedule have already been given; deleting it would erase those records and their stock trail.";
            return null;
        }
    }
}
