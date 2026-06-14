using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using VaccineAPI.Models;
using VaccineAPI.ModelDTO;

namespace VaccineAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly Context _db;

        public DashboardController(Context context)
        {
            _db = context;
        }

        [HttpGet("combined-data/{doctorId}")]
        public async Task<IActionResult> GetCombinedDashboardData(int doctorId)
        {
            try
            {
                var currentMonth = DateTime.Now.Month;
                var currentYear = DateTime.Now.Year;
                var currentDate = DateTime.Now.Date;
                DateTime startOfMonth = new DateTime(currentYear, currentMonth, 1);
                DateTime endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

                // Current Month Child Count
                var currentMonthChildCount = await _db.Childs
                    .CountAsync(c => c.DOB.Month == currentMonth
                                    && c.DOB.Year == currentYear
                                    && c.Clinic.DoctorId == doctorId);

                // Total Child Count
                var totalChildCount = await _db.Childs
                    .CountAsync(c => c.Clinic.DoctorId == doctorId);

                // Alerts Count
                var totalAlertsCount = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Date.Date >= startOfMonth.Date && s.Date.Date <= currentDate)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId)
                    .Where(s => s.IsDone != true && s.IsSkip != true && s.Child.IsInactive != true)
                    .Select(s => s.Child.Id)
                    .Distinct()
                    .CountAsync();

                // Future Alerts Count
                var futureAlertsCount = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Date.Date > currentDate && s.Date.Date <= endOfMonth.Date)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId)
                    .Where(s => s.IsDone != true && s.IsSkip != true && s.Child.IsInactive != true)
                    .Select(s => s.Child.Id)
                    .Distinct()
                    .CountAsync();

                // Current Month Given Doses Count
                var givenDosesCount = await _db.Schedules
                    .Include(s => s.Child.Clinic)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId &&
                                s.IsDone == true &&
                                s.GivenDate.HasValue &&
                                s.GivenDate.Value.Month == currentMonth &&
                                s.GivenDate.Value.Year == currentYear)
                    .CountAsync();

                var totalRevenue =await _db.Schedules.Include(s => s.Child.Clinic)
                        .Where(s =>s.Child.Clinic.DoctorId == doctorId
                            && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Month == currentMonth
                            && s.GivenDate.Value.Year == currentYear)
                        .SumAsync(s => s.Amount ?? 0) 
                    + await _db.AdjustStocks.Where(sa => _db.Clinics.Any(c => c.Id == sa.ClinicId && c.DoctorId == doctorId)
                            && sa.Date.Month == currentMonth
                            && sa.Date.Year == currentYear
                            && sa.Adjustment < 0)
                        .SumAsync(sa => sa.Price); 

                var dashboardData = new DashboardDTO
                {
                    CurrentMonthChildCount = currentMonthChildCount,
                    TotalChildCount = totalChildCount,
                    TotalAlertsCount = totalAlertsCount,
                    FutureAlertsCount = futureAlertsCount,
                    GivenDosesCount = givenDosesCount,
                    TotalRevenue = (totalRevenue),
                    // TotalIncreasedStock = totalIncreasedStock,
                };

                return Ok(dashboardData);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching the combined dashboard data.");
            }
        }

        // GET /api/dashboard/shell-data/{userType}/{id}
        // userType: "DOCTOR" (id = DoctorId) or "PA" (id = PersonalAssistantId)
        // Combines the calls fired by the members shell + dashboard page on every load:
        // clinics, online clinic, hasPA, pending approvals, pending bookings,
        // unread notifications, doctor profile flags / PA permissions + assignment count.
        [HttpGet("shell-data/{userType}/{id}")]
        public async Task<IActionResult> GetShellData(string userType, long id)
        {
            try
            {
                var data = new ShellDataDTO();
                long doctorId;

                if (string.Equals(userType, "PA", StringComparison.OrdinalIgnoreCase))
                {
                    var pa = await _db.PersonalAssistant.FirstOrDefaultAsync(p => p.Id == id);
                    if (pa == null)
                    {
                        return NotFound(new { message = "Personal assistant not found." });
                    }
                    doctorId = pa.DoctorId;
                    data.PaName = pa.Name;

                    data.PaPermissions = await _db.PaPermissions.FirstOrDefaultAsync(p => p.PaId == id);

                    data.PaAssignmentCount = await _db.PAAssignments
                        .CountAsync(a => a.PersonalAssistantId == id && !a.IsCompleted && !a.IsCancelled);

                    data.Clinics = await _db.PaAccess
                        .Include(pa2 => pa2.Clinic)
                        .Where(pa2 => pa2.PersonalAssistantId == id)
                        .Select(pa2 => pa2.Clinic)
                        .ToListAsync();
                }
                else
                {
                    var doctor = await _db.Doctors.FirstOrDefaultAsync(d => d.Id == id);
                    if (doctor == null)
                    {
                        return NotFound(new { message = "Doctor not found." });
                    }
                    doctorId = doctor.Id;

                    data.DisplayName = doctor.DisplayName;
                    data.ProfileImage = doctor.ProfileImage;
                    data.AllowInventory = doctor.AllowInventory;
                    data.AllowFinancial = doctor.AllowFinancial;
                    data.AllowAnalytics = doctor.AllowAnalytics;
                    data.AllowAgent = doctor.AllowAgent;
                    data.AllowAssistant = doctor.AllowAssistant;

                    data.Clinics = await _db.Clinics
                        .Where(c => c.DoctorId == doctorId)
                        .ToListAsync();
                }

                data.OnlineClinic = data.Clinics.FirstOrDefault(c => c.IsOnline);

                data.HasPA = await _db.PersonalAssistant.AnyAsync(p => p.DoctorId == doctorId);

                if (data.HasPA)
                {
                    var clinicIds = data.Clinics.Select(c => c.Id).ToList();
                    if (string.Equals(userType, "PA", StringComparison.OrdinalIgnoreCase))
                    {
                        clinicIds = await _db.Clinics.Where(c => c.DoctorId == doctorId).Select(c => c.Id).ToListAsync();
                    }

                    data.PendingApprovalsCount = await _db.Childs
                        .CountAsync(c => clinicIds.Contains(c.ClinicId)
                            && (c.IsPAApprove == false
                                || c.Schedules.Any(s => s.IsPAApprove == false && s.IsDone == true)));
                }

                if (data.OnlineClinic != null)
                {
                    data.PendingBookingsCount = await _db.Bookings
                        .CountAsync(b => b.ClinicId == data.OnlineClinic.Id && b.Status == "Pending");
                }

                data.UnreadNotificationCount = await _db.Notifications
                    .CountAsync(n => n.RecipientType == "DOCTOR" && n.RecipientId == doctorId && !n.IsRead);

                return Ok(new Response<ShellDataDTO>(true, null, data));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return StatusCode(500, "An error occurred while fetching shell data.");
            }
        }

        // GET /api/dashboard/analytics/{doctorId}
        [HttpGet("analytics/{doctorId}")]
        public async Task<IActionResult> GetAnalytics(int doctorId)
        {
            try
            {
                var now = DateTime.Now;
                var months = Enumerable.Range(0, 6)
                    .Select(i => new DateTime(now.Year, now.Month, 1).AddMonths(-5 + i))
                    .ToList();

                var clinicIds = await _db.Clinics
                    .Where(c => c.DoctorId == doctorId)
                    .Select(c => c.Id)
                    .ToListAsync();

                // Monthly revenue: sum of schedule amounts given per month (last 6 months)
                var revenueRaw = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                             && s.IsDone == true
                             && s.GivenDate.HasValue
                             && s.GivenDate.Value >= months.First()
                             && s.GivenDate.Value < months.Last().AddMonths(1))
                    .GroupBy(s => new { s.GivenDate.Value.Year, s.GivenDate.Value.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Sum(s => s.Amount ?? 0) })
                    .ToListAsync();

                // Monthly doses given (last 6 months)
                var dosesRaw = await _db.Schedules
                    .Include(s => s.Child)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                             && s.IsDone == true
                             && s.GivenDate.HasValue
                             && s.GivenDate.Value >= months.First()
                             && s.GivenDate.Value < months.Last().AddMonths(1))
                    .GroupBy(s => new { s.GivenDate.Value.Year, s.GivenDate.Value.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                    .ToListAsync();

                // Monthly new patients registered (last 6 months) — using Child.CreatedAt or DOB fallback
                var patientsRaw = await _db.Childs
                    .Where(c => c.Clinic.DoctorId == doctorId
                             && c.DOB >= months.First()
                             && c.DOB < months.Last().AddMonths(1))
                    .GroupBy(c => new { c.DOB.Year, c.DOB.Month })
                    .Select(g => new { g.Key.Year, g.Key.Month, Count = g.Count() })
                    .ToListAsync();

                // Top 10 vaccines given all-time
                var topVaccinesRaw = await _db.Schedules
                    .Include(s => s.Brand)
                    .Include(s => s.Child)
                    .Where(s => s.Child.Clinic.DoctorId == doctorId
                             && s.IsDone == true
                             && s.BrandId != null)
                    .GroupBy(s => new { s.BrandId, BrandName = s.Brand.Name })
                    .Select(g => new { g.Key.BrandName, Count = g.Count() })
                    .OrderByDescending(g => g.Count)
                    .Take(10)
                    .ToListAsync();

                // Map to month-labelled series, filling zeros for missing months
                var monthlyRevenue = months.Select(m => new MonthlyStatDTO
                {
                    Month = m.ToString("MMM yy"),
                    Value = revenueRaw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Total ?? 0
                }).ToList();

                var monthlyDoses = months.Select(m => new MonthlyStatDTO
                {
                    Month = m.ToString("MMM yy"),
                    Value = dosesRaw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Count ?? 0
                }).ToList();

                var monthlyPatients = months.Select(m => new MonthlyStatDTO
                {
                    Month = m.ToString("MMM yy"),
                    Value = patientsRaw.FirstOrDefault(r => r.Year == m.Year && r.Month == m.Month)?.Count ?? 0
                }).ToList();

                var topVaccines = topVaccinesRaw.Select(v => new BrandStatDTO
                {
                    BrandName = v.BrandName ?? "Unknown",
                    Count = v.Count
                }).ToList();

                return Ok(new AnalyticsDTO
                {
                    MonthlyRevenue = monthlyRevenue,
                    MonthlyDoses = monthlyDoses,
                    MonthlyNewPatients = monthlyPatients,
                    TopVaccines = topVaccines
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analytics error: {ex.Message}");
                return StatusCode(500, "Error fetching analytics data.");
            }
        }

        // GET /api/dashboard/advanced?doctorId=X&clinicId=Y&from=DATE&to=DATE&compareFrom=DATE&compareTo=DATE
        // clinicId = 0 means all clinics
        [HttpGet("advanced")]
        public async Task<IActionResult> GetAdvanced(
            [FromQuery] long     doctorId,
            [FromQuery] long     clinicId    = 0,
            [FromQuery] DateTime? from       = null,
            [FromQuery] DateTime? to         = null,
            [FromQuery] DateTime? compareFrom = null,
            [FromQuery] DateTime? compareTo   = null)
        {
            try
            {
                var now   = DateTime.Now;
                var curFrom = from        ?? new DateTime(now.Year, now.Month, 1);
                var curTo   = to          ?? now;
                var preFrom = compareFrom ?? curFrom.AddYears(-1);
                var preTo   = compareTo   ?? curTo.AddYears(-1);

                var clinics = await _db.Clinics
                    .Where(c => c.DoctorId == doctorId && (clinicId == 0 || c.Id == clinicId))
                    .ToListAsync();
                var clinicIds = clinics.Select(c => c.Id).ToList();

                // ── Helper lambdas ───────────────────────────────────────────
                async Task<PeriodSummaryDTO> BuildSummary(DateTime f, DateTime t)
                {
                    var ids = clinicId == 0 ? clinicIds : clinicIds.Where(x => x == clinicId).ToList();

                    // Patients registered (use CreatedAt if available, else DOB)
                    int patients = await _db.Childs
                        .CountAsync(c => ids.Contains(c.ClinicId)
                            && (c.CreatedAt.HasValue
                                ? c.CreatedAt.Value.Date >= f.Date && c.CreatedAt.Value.Date <= t.Date
                                : c.DOB.Date >= f.Date && c.DOB.Date <= t.Date));

                    // Doses given
                    int doses = await _db.Schedules
                        .CountAsync(s => ids.Contains(s.Child.ClinicId)
                            && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= f.Date
                            && s.GivenDate.Value.Date <= t.Date);

                    // Revenue: vaccination amounts
                    decimal vaxRev = await _db.Schedules
                        .Where(s => ids.Contains(s.Child.ClinicId)
                            && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= f.Date
                            && s.GivenDate.Value.Date <= t.Date)
                        .SumAsync(s => (decimal?)(s.Amount ?? 0)) ?? 0;

                    // Revenue: direct sales
                    decimal dsRev = await _db.DirectSales
                        .Where(d => ids.Contains(d.ClinicId)
                            && d.SaleDate.Date >= f.Date && d.SaleDate.Date <= t.Date)
                        .SumAsync(d => (decimal?)d.TotalSaleValue) ?? 0;

                    // Revenue: consultation fees
                    decimal consultRev = await _db.InvoiceSubmissions
                        .Where(x => x.DoctorId == doctorId
                            && (clinicId == 0 || x.ClinicId == clinicId)
                            && x.InvoiceDate.Date >= f.Date && x.InvoiceDate.Date <= t.Date)
                        .SumAsync(x => (decimal?)x.ConsultationFee) ?? 0;

                    decimal revenue = vaxRev + dsRev + consultRev;

                    // Expense: purchase bills
                    decimal purchaseExp = await _db.Stocks
                        .Where(s => s.BillId != null
                            && ids.Contains(s.Bill.ClinicId)
                            && !s.Bill.BillNo.StartsWith("XFER-")
                            && s.Bill.BillDate.Date >= f.Date && s.Bill.BillDate.Date <= t.Date)
                        .SumAsync(s => (decimal?)(s.StockAmount * s.OriginalQuantity)) ?? 0;

                    // Expense: operating expenses
                    decimal opExp = await _db.Expenses
                        .Where(e => e.DoctorId == doctorId
                            && (clinicId == 0 || e.ClinicId == clinicId || e.IsShared)
                            && e.ExpenseDate.Date >= f.Date && e.ExpenseDate.Date <= t.Date)
                        .SumAsync(e => (decimal?)e.Amount) ?? 0;

                    decimal expense = purchaseExp + opExp;

                    return new PeriodSummaryDTO
                    {
                        Patients  = patients,
                        Doses     = doses,
                        Revenue   = revenue,
                        Expense   = expense,
                        NetProfit = revenue - expense
                    };
                }

                // ── Section 1: Current vs Previous Summary ───────────────────
                var current  = await BuildSummary(curFrom, curTo);
                var previous = await BuildSummary(preFrom, preTo);

                // ── Section 2: By Clinic ─────────────────────────────────────
                var byClinic = new List<ClinicSummaryDTO>();
                foreach (var c in clinics)
                {
                    decimal cVaxRev = await _db.Schedules
                        .Where(s => s.Child.ClinicId == c.Id && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= curFrom.Date && s.GivenDate.Value.Date <= curTo.Date)
                        .SumAsync(s => (decimal?)(s.Amount ?? 0)) ?? 0;

                    decimal cDsRev = await _db.DirectSales
                        .Where(d => d.ClinicId == c.Id
                            && d.SaleDate.Date >= curFrom.Date && d.SaleDate.Date <= curTo.Date)
                        .SumAsync(d => (decimal?)d.TotalSaleValue) ?? 0;

                    decimal cConsult = await _db.InvoiceSubmissions
                        .Where(x => x.ClinicId == c.Id
                            && x.InvoiceDate.Date >= curFrom.Date && x.InvoiceDate.Date <= curTo.Date)
                        .SumAsync(x => (decimal?)x.ConsultationFee) ?? 0;

                    decimal cRev = cVaxRev + cDsRev + cConsult;

                    decimal cPurchase = await _db.Stocks
                        .Where(s => s.BillId != null && s.Bill.ClinicId == c.Id
                            && !s.Bill.BillNo.StartsWith("XFER-")
                            && s.Bill.BillDate.Date >= curFrom.Date && s.Bill.BillDate.Date <= curTo.Date)
                        .SumAsync(s => (decimal?)(s.StockAmount * s.OriginalQuantity)) ?? 0;

                    decimal cOp = await _db.Expenses
                        .Where(e => e.DoctorId == doctorId && (e.ClinicId == c.Id || e.IsShared)
                            && e.ExpenseDate.Date >= curFrom.Date && e.ExpenseDate.Date <= curTo.Date)
                        .SumAsync(e => (decimal?)e.Amount) ?? 0;

                    decimal cExp = cPurchase + cOp;

                    int cDoses = await _db.Schedules
                        .CountAsync(s => s.Child.ClinicId == c.Id && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= curFrom.Date && s.GivenDate.Value.Date <= curTo.Date);

                    int cPatients = await _db.Childs
                        .CountAsync(ch => ch.ClinicId == c.Id
                            && (ch.CreatedAt.HasValue
                                ? ch.CreatedAt.Value.Date >= curFrom.Date && ch.CreatedAt.Value.Date <= curTo.Date
                                : ch.DOB.Date >= curFrom.Date && ch.DOB.Date <= curTo.Date));

                    byClinic.Add(new ClinicSummaryDTO
                    {
                        ClinicId   = c.Id,
                        ClinicName = c.Name,
                        Revenue    = cRev,
                        Expense    = cExp,
                        Net        = cRev - cExp,
                        Doses      = cDoses,
                        Patients   = cPatients
                    });
                }

                // ── Section 3: Vaccine Performance ───────────────────────────
                var vaxGroups = await _db.Schedules
                    .Include(s => s.Brand)
                    .Where(s => clinicIds.Contains(s.Child.ClinicId)
                        && s.IsDone == true && s.BrandId != null
                        && s.GivenDate.HasValue
                        && s.GivenDate.Value.Date >= curFrom.Date && s.GivenDate.Value.Date <= curTo.Date)
                    .GroupBy(s => new { s.BrandId, BrandName = s.Brand.Name })
                    .Select(g => new VaccinePerfDTO
                    {
                        BrandId  = g.Key.BrandId.Value,
                        Name     = g.Key.BrandName,
                        Doses    = g.Count(),
                        Revenue  = g.Sum(s => s.Amount ?? 0),
                        AvgPrice = g.Count() > 0 ? g.Sum(s => s.Amount ?? 0) / g.Count() : 0
                    })
                    .OrderByDescending(x => x.Doses)
                    .Take(15)
                    .ToListAsync();

                // ── Section 4: Monthly Trend (current period) ────────────────
                var allMonths = Enumerable.Range(0,
                    ((curTo.Year - curFrom.Year) * 12 + curTo.Month - curFrom.Month) + 1)
                    .Select(i => new DateTime(curFrom.Year, curFrom.Month, 1).AddMonths(i))
                    .ToList();

                var monthlyTrend = new List<MonthlyTrendDTO>();
                foreach (var m in allMonths)
                {
                    var mEnd = new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
                    var mFrom = m > curFrom ? m : curFrom;
                    var mTo   = mEnd < curTo ? mEnd : curTo;

                    decimal mVax = await _db.Schedules
                        .Where(s => clinicIds.Contains(s.Child.ClinicId) && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= mFrom.Date && s.GivenDate.Value.Date <= mTo.Date)
                        .SumAsync(s => (decimal?)(s.Amount ?? 0)) ?? 0;

                    decimal mDs = await _db.DirectSales
                        .Where(d => clinicIds.Contains(d.ClinicId)
                            && d.SaleDate.Date >= mFrom.Date && d.SaleDate.Date <= mTo.Date)
                        .SumAsync(d => (decimal?)d.TotalSaleValue) ?? 0;

                    decimal mConsult = await _db.InvoiceSubmissions
                        .Where(x => x.DoctorId == doctorId
                            && (clinicId == 0 || x.ClinicId == clinicId)
                            && x.InvoiceDate.Date >= mFrom.Date && x.InvoiceDate.Date <= mTo.Date)
                        .SumAsync(x => (decimal?)x.ConsultationFee) ?? 0;

                    decimal mPurchase = await _db.Stocks
                        .Where(s => s.BillId != null && clinicIds.Contains(s.Bill.ClinicId)
                            && !s.Bill.BillNo.StartsWith("XFER-")
                            && s.Bill.BillDate.Date >= mFrom.Date && s.Bill.BillDate.Date <= mTo.Date)
                        .SumAsync(s => (decimal?)(s.StockAmount * s.OriginalQuantity)) ?? 0;

                    decimal mOp = await _db.Expenses
                        .Where(e => e.DoctorId == doctorId
                            && (clinicId == 0 || e.ClinicId == clinicId || e.IsShared)
                            && e.ExpenseDate.Date >= mFrom.Date && e.ExpenseDate.Date <= mTo.Date)
                        .SumAsync(e => (decimal?)e.Amount) ?? 0;

                    int mDoses = await _db.Schedules
                        .CountAsync(s => clinicIds.Contains(s.Child.ClinicId) && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= mFrom.Date && s.GivenDate.Value.Date <= mTo.Date);

                    int mPatients = await _db.Childs
                        .CountAsync(c => clinicIds.Contains(c.ClinicId)
                            && (c.CreatedAt.HasValue
                                ? c.CreatedAt.Value.Date >= mFrom.Date && c.CreatedAt.Value.Date <= mTo.Date
                                : c.DOB.Date >= mFrom.Date && c.DOB.Date <= mTo.Date));

                    monthlyTrend.Add(new MonthlyTrendDTO
                    {
                        Month    = m.ToString("MMM yy"),
                        Revenue  = mVax + mDs + mConsult,
                        Expense  = mPurchase + mOp,
                        Doses    = mDoses,
                        Patients = mPatients
                    });
                }

                // ── Section 4: Monthly Trend (previous period) ────────────────
                var prevMonths = Enumerable.Range(0,
                    ((preTo.Year - preFrom.Year) * 12 + preTo.Month - preFrom.Month) + 1)
                    .Select(i => new DateTime(preFrom.Year, preFrom.Month, 1).AddMonths(i))
                    .ToList();

                var monthlyPrev = new List<MonthlyTrendDTO>();
                foreach (var m in prevMonths)
                {
                    var mEnd  = new DateTime(m.Year, m.Month, DateTime.DaysInMonth(m.Year, m.Month));
                    var mFrom = m > preFrom ? m : preFrom;
                    var mTo   = mEnd < preTo ? mEnd : preTo;

                    decimal mVax = await _db.Schedules
                        .Where(s => clinicIds.Contains(s.Child.ClinicId) && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= mFrom.Date && s.GivenDate.Value.Date <= mTo.Date)
                        .SumAsync(s => (decimal?)(s.Amount ?? 0)) ?? 0;

                    decimal mDs = await _db.DirectSales
                        .Where(d => clinicIds.Contains(d.ClinicId)
                            && d.SaleDate.Date >= mFrom.Date && d.SaleDate.Date <= mTo.Date)
                        .SumAsync(d => (decimal?)d.TotalSaleValue) ?? 0;

                    decimal mConsult = await _db.InvoiceSubmissions
                        .Where(x => x.DoctorId == doctorId
                            && (clinicId == 0 || x.ClinicId == clinicId)
                            && x.InvoiceDate.Date >= mFrom.Date && x.InvoiceDate.Date <= mTo.Date)
                        .SumAsync(x => (decimal?)x.ConsultationFee) ?? 0;

                    decimal mPurchase = await _db.Stocks
                        .Where(s => s.BillId != null && clinicIds.Contains(s.Bill.ClinicId)
                            && !s.Bill.BillNo.StartsWith("XFER-")
                            && s.Bill.BillDate.Date >= mFrom.Date && s.Bill.BillDate.Date <= mTo.Date)
                        .SumAsync(s => (decimal?)(s.StockAmount * s.OriginalQuantity)) ?? 0;

                    decimal mOp = await _db.Expenses
                        .Where(e => e.DoctorId == doctorId
                            && (clinicId == 0 || e.ClinicId == clinicId || e.IsShared)
                            && e.ExpenseDate.Date >= mFrom.Date && e.ExpenseDate.Date <= mTo.Date)
                        .SumAsync(e => (decimal?)e.Amount) ?? 0;

                    int mDoses = await _db.Schedules
                        .CountAsync(s => clinicIds.Contains(s.Child.ClinicId) && s.IsDone == true
                            && s.GivenDate.HasValue
                            && s.GivenDate.Value.Date >= mFrom.Date && s.GivenDate.Value.Date <= mTo.Date);

                    int mPatients = await _db.Childs
                        .CountAsync(c => clinicIds.Contains(c.ClinicId)
                            && (c.CreatedAt.HasValue
                                ? c.CreatedAt.Value.Date >= mFrom.Date && c.CreatedAt.Value.Date <= mTo.Date
                                : c.DOB.Date >= mFrom.Date && c.DOB.Date <= mTo.Date));

                    monthlyPrev.Add(new MonthlyTrendDTO
                    {
                        Month    = m.ToString("MMM yy"),
                        Revenue  = mVax + mDs + mConsult,
                        Expense  = mPurchase + mOp,
                        Doses    = mDoses,
                        Patients = mPatients
                    });
                }

                return Ok(new AdvancedAnalyticsDTO
                {
                    Current     = current,
                    Previous    = previous,
                    ByClinic    = byClinic,
                    ByVaccine   = vaxGroups,
                    Monthly     = monthlyTrend,
                    MonthlyPrev = monthlyPrev
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Advanced analytics error: {ex.Message}\n{ex.StackTrace}");
                return StatusCode(500, "Error fetching advanced analytics.");
            }
        }
    }
}