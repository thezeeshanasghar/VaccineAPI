using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VaccineAPI.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "agents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agents", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cities", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InvoiceId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Amount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ChildId = table.Column<long>(type: "bigint", nullable: false),
                    DoctorId = table.Column<long>(type: "bigint", nullable: false),
                    ClinicId = table.Column<long>(type: "bigint", nullable: false),
                    DoseId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoices", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "normalranges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Age = table.Column<int>(type: "int", nullable: false),
                    Gender = table.Column<int>(type: "int", nullable: false),
                    WeightMin = table.Column<float>(type: "float", nullable: false),
                    WeightMax = table.Column<float>(type: "float", nullable: false),
                    HeightMin = table.Column<float>(type: "float", nullable: false),
                    HeightMax = table.Column<float>(type: "float", nullable: false),
                    OfcMin = table.Column<float>(type: "float", nullable: false),
                    OfcMax = table.Column<float>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_normalranges", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MobileNumber = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Password = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryCode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "vaccines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinAge = table.Column<int>(type: "int", nullable: false),
                    MaxAge = table.Column<int>(type: "int", nullable: true),
                    isInfinite = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vaccines", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "doctors",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    FirstName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PMDC = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ShowPhone = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ShowMobile = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PhoneNo = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ValidUpto = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    InvoiceNumber = table.Column<int>(type: "int", nullable: true),
                    ProfileImage = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AllowInvoice = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowChart = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowFollowUp = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AllowInventory = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SMSLimit = table.Column<int>(type: "int", nullable: false),
                    DoctorType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Qualification = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AdditionalInfo = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doctors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_doctors_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    MobileNumber = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SMS = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ApiResponse = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Created = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_messages_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "brands",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VaccineId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brands_vaccines_VaccineId",
                        column: x => x.VaccineId,
                        principalTable: "vaccines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "doses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MinAge = table.Column<int>(type: "int", nullable: false),
                    MaxAge = table.Column<int>(type: "int", nullable: true),
                    MinGap = table.Column<int>(type: "int", nullable: true),
                    DoseOrder = table.Column<int>(type: "int", nullable: true),
                    VaccineId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_doses_vaccines_VaccineId",
                        column: x => x.VaccineId,
                        principalTable: "vaccines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "clinics",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsultationFee = table.Column<int>(type: "int", nullable: false),
                    Lat = table.Column<double>(type: "double", nullable: false),
                    Long = table.Column<double>(type: "double", nullable: false),
                    PhoneNumber = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsOnline = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Address = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MonogramImage = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DoctorId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clinics_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "brandamounts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Amount = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    BrandId = table.Column<long>(type: "bigint", nullable: false),
                    DoctorId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brandamounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_brandamounts_brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_brandamounts_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "doctorschedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DoseId = table.Column<long>(type: "bigint", nullable: false),
                    DoctorId = table.Column<long>(type: "bigint", nullable: false),
                    GapInDays = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_doctorschedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_doctorschedules_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_doctorschedules_doses_DoseId",
                        column: x => x.DoseId,
                        principalTable: "doses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "childs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Guardian = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FatherName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DOB = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Gender = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    City = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Agent = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CNIC = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsEPIDone = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    IsVerified = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    IsInactive = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClinicId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    DoctorId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_childs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_childs_clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_childs_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_childs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "clinictimings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Day = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartTime = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time(6)", nullable: false),
                    Session = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsOpen = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ClinicId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinictimings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clinictimings_clinics_ClinicId",
                        column: x => x.ClinicId,
                        principalTable: "clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "followups",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Disease = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextVisitDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CurrentVisitDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Weight = table.Column<float>(type: "float", nullable: true),
                    Height = table.Column<float>(type: "float", nullable: true),
                    OFC = table.Column<float>(type: "float", nullable: true),
                    BloodPressure = table.Column<float>(type: "float", nullable: true),
                    BloodSugar = table.Column<float>(type: "float", nullable: true),
                    ChildId = table.Column<long>(type: "bigint", nullable: false),
                    DoctorId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_followups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_followups_childs_ChildId",
                        column: x => x.ChildId,
                        principalTable: "childs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_followups_doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "schedules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Weight = table.Column<float>(type: "float", nullable: true),
                    Height = table.Column<float>(type: "float", nullable: true),
                    Circle = table.Column<float>(type: "float", nullable: true),
                    IsDone = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsSkip = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    IsDisease = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    Due2EPI = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DiseaseYear = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GivenDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    BrandId = table.Column<long>(type: "bigint", nullable: true),
                    Amount = table.Column<int>(type: "int", nullable: true),
                    ChildId = table.Column<long>(type: "bigint", nullable: false),
                    DoseId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedules_brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "brands",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_schedules_childs_ChildId",
                        column: x => x.ChildId,
                        principalTable: "childs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_schedules_doses_DoseId",
                        column: x => x.DoseId,
                        principalTable: "doses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.InsertData(
                table: "users",
                columns: new[] { "Id", "CountryCode", "MobileNumber", "Password", "UserType" },
                values: new object[] { 1L, "92", "3331231231", "1234", "SUPERADMIN" });

            migrationBuilder.CreateIndex(
                name: "IX_brandamounts_BrandId",
                table: "brandamounts",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_brandamounts_DoctorId",
                table: "brandamounts",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_brands_VaccineId",
                table: "brands",
                column: "VaccineId");

            migrationBuilder.CreateIndex(
                name: "IX_childs_ClinicId",
                table: "childs",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_childs_DoctorId",
                table: "childs",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_childs_UserId",
                table: "childs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_clinics_DoctorId",
                table: "clinics",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_clinictimings_ClinicId",
                table: "clinictimings",
                column: "ClinicId");

            migrationBuilder.CreateIndex(
                name: "IX_doctors_UserId",
                table: "doctors",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_doctorschedules_DoctorId",
                table: "doctorschedules",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_doctorschedules_DoseId",
                table: "doctorschedules",
                column: "DoseId");

            migrationBuilder.CreateIndex(
                name: "IX_doses_VaccineId",
                table: "doses",
                column: "VaccineId");

            migrationBuilder.CreateIndex(
                name: "IX_followups_ChildId",
                table: "followups",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_followups_DoctorId",
                table: "followups",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_messages_UserId",
                table: "messages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_schedules_BrandId",
                table: "schedules",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_schedules_ChildId",
                table: "schedules",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_schedules_DoseId",
                table: "schedules",
                column: "DoseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agents");

            migrationBuilder.DropTable(
                name: "brandamounts");

            migrationBuilder.DropTable(
                name: "cities");

            migrationBuilder.DropTable(
                name: "clinictimings");

            migrationBuilder.DropTable(
                name: "doctorschedules");

            migrationBuilder.DropTable(
                name: "followups");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "messages");

            migrationBuilder.DropTable(
                name: "normalranges");

            migrationBuilder.DropTable(
                name: "schedules");

            migrationBuilder.DropTable(
                name: "brands");

            migrationBuilder.DropTable(
                name: "childs");

            migrationBuilder.DropTable(
                name: "doses");

            migrationBuilder.DropTable(
                name: "clinics");

            migrationBuilder.DropTable(
                name: "vaccines");

            migrationBuilder.DropTable(
                name: "doctors");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
