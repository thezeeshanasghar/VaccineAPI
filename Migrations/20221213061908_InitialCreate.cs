using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.Data.EntityFrameworkCore.Metadata;

namespace VaccineAPI.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NormalRanges",
                columns: table => new
                {
                    Id = table.Column<int>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Age = table.Column<int>(nullable: false),
                    Gender = table.Column<int>(nullable: false),
                    WeightMin = table.Column<float>(nullable: false),
                    WeightMax = table.Column<float>(nullable: false),
                    HeightMin = table.Column<float>(nullable: false),
                    HeightMax = table.Column<float>(nullable: false),
                    OfcMin = table.Column<float>(nullable: false),
                    OfcMax = table.Column<float>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NormalRanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    MobileNumber = table.Column<string>(nullable: true),
                    Password = table.Column<string>(nullable: true),
                    UserType = table.Column<string>(nullable: true),
                    CountryCode = table.Column<string>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Vaccines",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    MinAge = table.Column<int>(nullable: false),
                    MaxAge = table.Column<int>(nullable: true),
                    isInfinite = table.Column<bool>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vaccines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Doctors",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    FirstName = table.Column<string>(nullable: true),
                    LastName = table.Column<string>(nullable: true),
                    Email = table.Column<string>(nullable: true),
                    PMDC = table.Column<string>(nullable: true),
                    IsApproved = table.Column<short>(nullable: false),
                    ShowPhone = table.Column<short>(nullable: false),
                    ShowMobile = table.Column<short>(nullable: false),
                    PhoneNo = table.Column<string>(nullable: true),
                    ValidUpto = table.Column<DateTime>(nullable: true),
                    InvoiceNumber = table.Column<int>(nullable: true),
                    ProfileImage = table.Column<string>(nullable: true),
                    SignatureImage = table.Column<string>(nullable: true),
                    DisplayName = table.Column<string>(nullable: true),
                    AllowInvoice = table.Column<short>(nullable: false),
                    AllowChart = table.Column<short>(nullable: false),
                    AllowFollowUp = table.Column<short>(nullable: false),
                    AllowInventory = table.Column<short>(nullable: false),
                    SMSLimit = table.Column<int>(nullable: false),
                    DoctorType = table.Column<string>(nullable: true),
                    Qualification = table.Column<string>(nullable: true),
                    AdditionalInfo = table.Column<string>(nullable: true),
                    UserId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Doctors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Doctors_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    MobileNumber = table.Column<string>(nullable: true),
                    SMS = table.Column<string>(nullable: true),
                    ApiResponse = table.Column<string>(nullable: true),
                    Created = table.Column<DateTime>(nullable: false),
                    UserId = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    VaccineId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brands_Vaccines_VaccineId",
                        column: x => x.VaccineId,
                        principalTable: "Vaccines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Doses",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    MinAge = table.Column<int>(nullable: false),
                    MaxAge = table.Column<int>(nullable: true),
                    MinGap = table.Column<int>(nullable: true),
                    DoseOrder = table.Column<int>(nullable: true),
                    IsSpecial = table.Column<short>(nullable: true),
                    VaccineId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Doses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Doses_Vaccines_VaccineId",
                        column: x => x.VaccineId,
                        principalTable: "Vaccines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    ConsultationFee = table.Column<int>(nullable: false),
                    OffDays = table.Column<string>(nullable: true),
                    Lat = table.Column<double>(nullable: false),
                    Long = table.Column<double>(nullable: false),
                    PhoneNumber = table.Column<string>(nullable: true),
                    IsOnline = table.Column<short>(nullable: false),
                    Address = table.Column<string>(nullable: true),
                    MonogramImage = table.Column<string>(nullable: true),
                    DoctorId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clinics_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrandAmounts",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Amount = table.Column<int>(nullable: false),
                    BrandId = table.Column<long>(nullable: false),
                    DoctorId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandAmounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandAmounts_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrandAmounts_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BrandInventorys",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Count = table.Column<int>(nullable: false),
                    BrandId = table.Column<long>(nullable: false),
                    DoctorId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandInventorys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandInventorys_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BrandInventorys_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DoctorSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    DoseId = table.Column<long>(nullable: false),
                    DoctorId = table.Column<long>(nullable: false),
                    GapInDays = table.Column<int>(nullable: false),
                    IsActive = table.Column<short>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorSchedules_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DoctorSchedules_Doses_DoseId",
                        column: x => x.DoseId,
                        principalTable: "Doses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Childs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(nullable: true),
                    Guardian = table.Column<string>(nullable: true),
                    FatherName = table.Column<string>(nullable: true),
                    Email = table.Column<string>(nullable: true),
                    DOB = table.Column<DateTime>(nullable: false),
                    Gender = table.Column<string>(nullable: true),
                    Type = table.Column<string>(nullable: true),
                    City = table.Column<string>(nullable: true),
                    CNIC = table.Column<string>(nullable: true),
                    PreferredDayOfReminder = table.Column<int>(nullable: false),
                    PreferredDayOfWeek = table.Column<string>(nullable: true),
                    PreferredSchedule = table.Column<string>(nullable: true),
                    IsEPIDone = table.Column<short>(nullable: true),
                    IsVerified = table.Column<short>(nullable: true),
                    IsInactive = table.Column<short>(nullable: true),
                    ClinicId = table.Column<long>(nullable: false),
                    UserId = table.Column<long>(nullable: false),
                    DoctorId = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Childs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Childs_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Childs_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Childs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClinicTimings",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Day = table.Column<string>(nullable: true),
                    StartTime = table.Column<TimeSpan>(nullable: false),
                    EndTime = table.Column<TimeSpan>(nullable: false),
                    Session = table.Column<string>(nullable: true),
                    IsOpen = table.Column<short>(nullable: false),
                    ClinicId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicTimings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicTimings_Clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FollowUps",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Disease = table.Column<string>(nullable: true),
                    NextVisitDate = table.Column<DateTime>(nullable: true),
                    CurrentVisitDate = table.Column<DateTime>(nullable: true),
                    Weight = table.Column<float>(nullable: true),
                    Height = table.Column<float>(nullable: true),
                    OFC = table.Column<float>(nullable: true),
                    BloodPressure = table.Column<float>(nullable: true),
                    BloodSugar = table.Column<float>(nullable: true),
                    ChildId = table.Column<long>(nullable: false),
                    DoctorId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUps_Childs_ChildId",
                        column: x => x.ChildId,
                        principalTable: "Childs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FollowUps_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Schedules",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(nullable: false),
                    Weight = table.Column<float>(nullable: true),
                    Height = table.Column<float>(nullable: true),
                    Circle = table.Column<float>(nullable: true),
                    IsDone = table.Column<short>(nullable: false),
                    IsSkip = table.Column<short>(nullable: true),
                    IsDisease = table.Column<short>(nullable: true),
                    Due2EPI = table.Column<short>(nullable: false),
                    DiseaseYear = table.Column<string>(nullable: true),
                    GivenDate = table.Column<DateTime>(nullable: true),
                    BrandId = table.Column<long>(nullable: true),
                    Amount = table.Column<int>(nullable: true),
                    ChildId = table.Column<long>(nullable: false),
                    DoseId = table.Column<long>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Schedules_Childs_ChildId",
                        column: x => x.ChildId,
                        principalTable: "Childs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Schedules_Doses_DoseId",
                        column: x => x.DoseId,
                        principalTable: "Doses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CountryCode", "MobileNumber", "Password", "UserType" },
                values: new object[] { 1L, "92", "3331231231", "1234", "SUPERADMIN" });

            migrationBuilder.CreateIndex(
                name: "IX_BrandAmounts_BrandId",
                table: "BrandAmounts",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandAmounts_DoctorId",
                table: "BrandAmounts",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandInventorys_BrandId",
                table: "BrandInventorys",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_BrandInventorys_DoctorId",
                table: "BrandInventorys",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_VaccineId",
                table: "Brands",
                column: "VaccineId");

            migrationBuilder.CreateIndex(
                name: "IX_Childs_ClinicId",
                table: "Childs",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Childs_DoctorId",
                table: "Childs",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Childs_UserId",
                table: "Childs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_DoctorId",
                table: "Clinics",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTimings_ClinicId",
                table: "ClinicTimings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_UserId",
                table: "Doctors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_DoctorId",
                table: "DoctorSchedules",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_DoseId",
                table: "DoctorSchedules",
                column: "DoseId");

            migrationBuilder.CreateIndex(
                name: "IX_Doses_VaccineId",
                table: "Doses",
                column: "VaccineId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_ChildId",
                table: "FollowUps",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_DoctorId",
                table: "FollowUps",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_UserId",
                table: "Messages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_BrandId",
                table: "Schedules",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_ChildId",
                table: "Schedules",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_DoseId",
                table: "Schedules",
                column: "DoseId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BrandAmounts");

            migrationBuilder.DropTable(
                name: "BrandInventorys");

            migrationBuilder.DropTable(
                name: "ClinicTimings");

            migrationBuilder.DropTable(
                name: "DoctorSchedules");

            migrationBuilder.DropTable(
                name: "FollowUps");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "NormalRanges");

            migrationBuilder.DropTable(
                name: "Schedules");

            migrationBuilder.DropTable(
                name: "Brands");

            migrationBuilder.DropTable(
                name: "Childs");

            migrationBuilder.DropTable(
                name: "Doses");

            migrationBuilder.DropTable(
                name: "Clinics");

            migrationBuilder.DropTable(
                name: "Vaccines");

            migrationBuilder.DropTable(
                name: "Doctors");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
