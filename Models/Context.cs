using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VaccineAPI.Models
{
    public class Context : DbContext
    {
        public Context(DbContextOptions<Context> options) : base(options)
        {

        }

        public DbSet<Vaccine> Vaccines { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Schedule> Schedules { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<FollowUp> FollowUps { get; set; }
        public DbSet<Dose> Doses { get; set; }
        public DbSet<DoctorSchedule> DoctorSchedules { get; set; }
        public DbSet<Doctor> Doctors { get; set; }
        public DbSet<ClinicTiming> ClinicTimings { get; set; }
        public DbSet<Clinic> Clinics { get; set; }
        public DbSet<Child> Childs { get; set; }
        // public DbSet<BrandInventory> BrandInventorys { get; set; }
        public DbSet<BrandAmount> BrandAmounts { get; set; }
        public DbSet<Brand> Brands { get; set; }
        public DbSet<NormalRange> NormalRanges { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Agent> Agents { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
        public DbSet<Bill> Bills { get; set; }
        public DbSet<Stock> Stocks { get; set; }
        public DbSet<AdjustStock> AdjustStocks { get; set; }
        public DbSet<PersonalAssistant> PersonalAssistant { get; set; }
        public DbSet<PaAccess> PaAccess { get; set; }
        public DbSet<PaPermission> PaPermissions { get; set; }
        public DbSet<PaActivityLog> PaActivityLogs { get; set; }
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
            modelBuilder.Entity<User>().HasData(new User() { Id = 1, MobileNumber = "3331231231", Password = "1234", UserType = "SUPERADMIN", CountryCode = "92" });
        }
    }
}
