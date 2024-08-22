using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace VaccineAPI.Models
{
    public class Context : DbContext
    {
        public  Context(DbContextOptions<Context> options) : base(options)
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
        public DbSet<NormalRange> NormalRanges { get; set; }
        public DbSet<City> Cities { get; set; }
        public DbSet<Invoice> Invoices { get; set; }
         protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
                    base.OnModelCreating(modelBuilder);
                    modelBuilder.Entity<User>().HasData(new User() {Id = 1, MobileNumber= "3331231231" , Password = "1234" , UserType  = "SUPERADMIN" , CountryCode = "92"});
                    modelBuilder.Entity< Doctor >(b=>{ b.Property(r => r.IsApproved)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                            b.Property(r => r.ShowPhone)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             
                            b.Property(r => r.ShowMobile)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             
                            b.Property(r => r.AllowInvoice)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              
                            b.Property(r => r.AllowChart)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              
                            b.Property(r => r.AllowFollowUp)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              
                            b.Property(r => r.AllowInventory)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                            }); 
                           

                              modelBuilder.Entity< Clinic>()
                            .Property(r => r.IsOnline)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());


                            modelBuilder.Entity< Dose>()
                            .Property(r => r.IsSpecial)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             modelBuilder.Entity< ClinicTiming>()
                            .Property(r => r.IsOpen)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());


                              modelBuilder.Entity< Schedule >(b=>{
                                b.Property(r => r.IsDone)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                            
                        
                            b.Property(r => r.Due2EPI)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             b.Property(r => r.IsSkip)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             b.Property(r => r.IsDisease)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                              });

                             modelBuilder.Entity< Child >(b=>{
                            //     b.Property(r => r.IsBrand)
                            // .HasConversion(new BoolToZeroOneConverter<Int16>());
                            
                        
                            // b.Property(r => r.IsConsultationFee)
                            // .HasConversion(new BoolToZeroOneConverter<Int16>());

                             
                            b.Property(r => r.IsEPIDone)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                             
                            b.Property(r => r.IsVerified)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                              b.Property(r => r.IsInactive)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());

                            //  b.Property(r => r.IsDivertAlert)
                            // .HasConversion(new BoolToZeroOneConverter<Int16>());
                             });
                             

                             modelBuilder.Entity< DoctorSchedule >(b=>{  
                            b.Property(r => r.IsActive)
                            .HasConversion(new BoolToZeroOneConverter<Int16>());
                             });
                           

                            //  modelBuilder.Entity< User >(b=> {
                            //    b.Property(r => r.AllowInventory)
                            // .HasConversion(new BoolToZeroOneConverter<Int16>());
                             
                            //   b.Property(r => r.AllowInvoice)
                            // .HasConversion(new BoolToZeroOneConverter<Int16>());
                            //  });
                            
                            
                            
            
        }
    }
}