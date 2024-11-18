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
        public DbSet<Invoice> Invoices { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<User>().HasData(new User() { Id = 1, MobileNumber = "3331231231", Password = "1234", UserType = "SUPERADMIN", CountryCode = "92" });
        }
    }
}
