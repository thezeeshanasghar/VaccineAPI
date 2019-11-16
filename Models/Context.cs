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
        public DbSet<BrandInventory> BrandInventorys { get; set; }
        public DbSet<BrandAmount> BrandAmounts { get; set; }
        public DbSet<Brand> Brands { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
                    base.OnModelCreating(modelBuilder);
                    modelBuilder.Entity< Doctor >() 
                            .Property(r => r.IsApproved)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                             modelBuilder.Entity< Doctor >()
                            .Property(r => r.ShowPhone)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             modelBuilder.Entity< Doctor >()
                            .Property(r => r.ShowMobile)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                              modelBuilder.Entity< Doctor >()
                            .Property(r => r.AllowInvoice)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              modelBuilder.Entity< Doctor >()
                            .Property(r => r.AllowChart)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              modelBuilder.Entity< Doctor >()
                            .Property(r => r.AllowFollowUp)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              modelBuilder.Entity< Doctor >()
                            .Property(r => r.AllowInventory)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                              modelBuilder.Entity< ClinicTiming >()
                            .Property(r => r.IsOpen)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             modelBuilder.Entity< Child >()
                            .Property(r => r.IsBrand)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                            
                             modelBuilder.Entity< Child >()
                            .Property(r => r.IsConsultationFee)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             modelBuilder.Entity< Child >()
                            .Property(r => r.IsEPIDone)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             modelBuilder.Entity< Child >()
                            .Property(r => r.IsVerified)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                            

                            
                            
                            
                    
                           
                            
            
        }
    }
}