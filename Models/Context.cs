using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VaccineAPI.Models
{
    public class Context : DbContext
    {
        public Context(DbContextOptions<Context> options) : base(options)
        {

        }

        // Stock and BrandAmount carry an app-incremented optimistic concurrency token
        // ([ConcurrencyCheck] RowVersion — MySQL has no native rowversion type). EF only
        // uses the token's CURRENT value in the UPDATE's WHERE clause; it never bumps it.
        // Bumping here, centrally, means every existing and future write site gets
        // concurrency protection for free with no per-controller code.
        private void BumpConcurrencyTokens()
        {
            foreach (var entry in ChangeTracker.Entries<Stock>())
                if (entry.State == EntityState.Modified)
                    entry.Entity.RowVersion++;

            foreach (var entry in ChangeTracker.Entries<BrandAmount>())
                if (entry.State == EntityState.Modified)
                    entry.Entity.RowVersion++;

            foreach (var entry in ChangeTracker.Entries<UnbatchedUse>())
                if (entry.State == EntityState.Modified)
                    entry.Entity.RowVersion++;
        }

        // ---- single-writer enforcement -------------------------------------------------------
        // Inventory state (batch quantity / purchased quantity / closed flag / clinic, the BrandAmount
        // counter, and every ledger row) may only be written by InventoryTransactionService, which
        // marks each entity it writes. Any other write that reaches SaveChanges is reported (and, when
        // EnforceSingleInventoryWriter is true, refused). This is a runtime check, unlike a source scan.
        public static bool EnforceSingleInventoryWriter { get; set; } = false;
        private readonly System.Collections.Generic.HashSet<object> _inventoryWriteMarks =
            new System.Collections.Generic.HashSet<object>(ReferenceEqualityComparer.Instance);
        public void MarkInventoryWrite(object entity) => _inventoryWriteMarks.Add(entity);
        // Test fixtures only: a context that seeds baseline rows directly (never used by API code).
        public bool BypassInventoryWriterCheck { get; set; }

        private void CheckInventoryWriters()
        {
            if (BypassInventoryWriterCheck) return;
            var violations = new System.Collections.Generic.List<string>();
            foreach (var e in ChangeTracker.Entries())
            {
                if (e.State != EntityState.Added && e.State != EntityState.Modified) continue;
                if (_inventoryWriteMarks.Contains(e.Entity)) continue;
                switch (e.Entity)
                {
                    case Stock st when e.State == EntityState.Added || Touched(e, nameof(Stock.Quantity), nameof(Stock.OriginalQuantity), nameof(Stock.IsClosed), nameof(Stock.ClinicId), nameof(Stock.BrandId)):
                        violations.Add($"Stock {st.Id} written outside the inventory service"); break;
                    case BrandAmount ba when e.State == EntityState.Added || Touched(e, nameof(BrandAmount.Quantity)):
                        violations.Add($"BrandAmount {ba.Id} counter written outside the inventory service"); break;
                    case InventoryTransaction tx:
                        violations.Add($"Ledger row {tx.Id} written outside the inventory service"); break;
                }
            }
            if (violations.Count == 0) return;
            var msg = "UNAUTHORISED INVENTORY WRITE: " + string.Join("; ", violations);
            VaccineAPI.Services.InventoryTransactionService.OnInvariantViolation?.Invoke(msg);
            if (EnforceSingleInventoryWriter)
                throw new VaccineAPI.Services.InventoryInvariantException(msg);
        }

        private static bool Touched(Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry e, params string[] props)
        {
            foreach (var p in props)
                if (e.Property(p).IsModified) return true;
            return false;
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            BumpConcurrencyTokens();
            CheckInventoryWriters();
            var n = base.SaveChanges(acceptAllChangesOnSuccess);
            _inventoryWriteMarks.Clear();
            return n;
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            BumpConcurrencyTokens();
            CheckInventoryWriters();
            var n = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            _inventoryWriteMarks.Clear();
            return n;
        }

        public DbSet<Vaccine> Vaccines { get; set; }
        public DbSet<VaccineInfo> VaccineInfos { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Schedule> Schedules { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<HomeServiceCity> HomeServiceCities { get; set; }
        public DbSet<FollowUp> FollowUps { get; set; }
        public DbSet<Dose> Doses { get; set; }
        public DbSet<DoctorSchedule> DoctorSchedules { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<Clinic> Clinics { get; set; }
        public DbSet<Child> Childs { get; set; }
        // public DbSet<BrandInventory> BrandInventorys { get; set; }
        public DbSet<BrandAmount> BrandAmounts { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<NormalRange> NormalRanges { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Agent> Agents { get; set; }
        public DbSet<AgentVaccineFeeOverride> AgentVaccineFeeOverrides { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Bill> Bills { get; set; }
        public DbSet<Stock> Stocks { get; set; }
        public DbSet<AdjustStock> AdjustStocks { get; set; }
        public DbSet<PersonalAssistant> PersonalAssistant { get; set; }
        public DbSet<PaAccess> PaAccess { get; set; }
        public DbSet<PaPermission> PaPermissions { get; set; }
        public DbSet<PaActivityLog> PaActivityLogs { get; set; }
        public DbSet<Manager> Manager { get; set; }
        public DbSet<ManagerAccess> ManagerAccess { get; set; }
        public DbSet<ManagerPermission> ManagerPermissions { get; set; }
        public DbSet<InvoiceSubmission> InvoiceSubmissions { get; set; }
        public DbSet<Fee> Fee { get; set; }
        public DbSet<VaccineBrand> VaccineBrands { get; set; }
        public DbSet<StockTransfer> StockTransfers { get; set; }
        public DbSet<DirectSale> DirectSales { get; set; }
        public DbSet<Supplier> Suppliers { get; set; }
        public DbSet<SupplierPayment> SupplierPayments { get; set; }
        public DbSet<PaCashHandover> PaCashHandovers { get; set; }
        public DbSet<Expense> Expenses { get; set; }
        public DbSet<PAAssignment> PAAssignments { get; set; }
        public DbSet<PaPayableAdjustment> PaPayableAdjustments { get; set; }
        public DbSet<InvoiceAmendment> InvoiceAmendments { get; set; }
        public DbSet<InventoryTransaction> InventoryTransactions { get; set; }
        public DbSet<UnbatchedUse> UnbatchedUses { get; set; }
        public DbSet<IdempotencyKey> IdempotencyKeys { get; set; }
        public DbSet<PAAssignmentSchedule> PAAssignmentSchedules { get; set; }
        public DbSet<Refrigerator> Refrigerators { get; set; }
        public DbSet<TemperatureReading> TemperatureReadings { get; set; }
        public DbSet<ColdChainApprovalLog> ColdChainApprovalLogs { get; set; }
        public DbSet<AppEmailSetting> AppEmailSettings { get; set; }
        public DbSet<SalesReportLog> SalesReportLogs { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (!string.IsNullOrEmpty(tableName))
                {
                    entity.SetTableName(tableName.ToLower());
                }
            }
            modelBuilder.Entity<HomeServiceCity>().ToTable("home_service_cities");
            // One movement can be reversed at most once (NULLs are allowed to repeat).
            modelBuilder.Entity<InventoryTransaction>().HasIndex(t => t.ReversesTransactionId).IsUnique();
            // A ClientRequestId can be accepted at most once per doctor and endpoint.
            modelBuilder.Entity<IdempotencyKey>().HasIndex(k => new { k.DoctorId, k.Endpoint, k.ClientRequestId }).IsUnique();
            // One BrandAmount row per (brand, doctor, clinic).
            modelBuilder.Entity<BrandAmount>().HasIndex(b => new { b.BrandId, b.DoctorId, b.ClinicId }).IsUnique();
            // One live (pending or claimed) unbatched use per schedule.
            modelBuilder.Entity<UnbatchedUse>().HasIndex(u => u.ActiveScheduleKey).IsUnique();
            modelBuilder.Entity<User>().HasData(new User() { Id = 1, MobileNumber = "3331231231", Password = "1234", UserType = "SUPERADMIN", CountryCode = "92" });
        }
    }
}
