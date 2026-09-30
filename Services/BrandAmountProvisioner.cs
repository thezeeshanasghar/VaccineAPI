using System.Linq;
using Microsoft.EntityFrameworkCore;
using VaccineAPI.Models;

namespace VaccineAPI.Services
{
    // The only place a BrandAmount row is created. One row per (brand, doctor, clinic): an existing
    // row (saved or staged in this request) is returned instead of a duplicate. A new row starts at
    // the sum of the batches that already exist for that brand and clinic, so the projection is
    // correct from the moment the row exists.
    public static class BrandAmountProvisioner
    {
        public static BrandAmount Ensure(Context db, long brandId, long doctorId, long clinicId, decimal salePrice = 0)
        {
            var existing = db.BrandAmounts.Local.FirstOrDefault(b => b.BrandId == brandId && b.DoctorId == doctorId && b.ClinicId == clinicId)
                        ?? db.BrandAmounts.FirstOrDefault(b => b.BrandId == brandId && b.DoctorId == doctorId && b.ClinicId == clinicId);
            if (existing != null) return existing;

            int onHand = db.Stocks.Include(s => s.Bill)
                .Where(s => s.BrandId == brandId
                    && (s.ClinicId == clinicId || (s.ClinicId == null && s.Bill != null && s.Bill.ClinicId == clinicId)))
                .Sum(s => (int?)s.Quantity) ?? 0;
            var ba = new BrandAmount { BrandId = brandId, DoctorId = doctorId, ClinicId = clinicId, SalePrice = salePrice, Quantity = onHand };
            db.BrandAmounts.Add(ba);
            db.MarkInventoryWrite(ba);
            return ba;
        }
    }
}
