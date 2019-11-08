using Microsoft.EntityFrameworkCore.Migrations;

namespace VaccineAPI.Migrations
{
    public partial class first : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    MobileNumber = table.Column<int>(nullable: false),
                    Password = table.Column<int>(nullable: false),
                    UserType = table.Column<string>(nullable: true),
                    CountryCode = table.Column<int>(nullable: true)
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
                        .Annotation("MySQL:AutoIncrement", true),
                    Name = table.Column<string>(nullable: true),
                    MinAge = table.Column<int>(nullable: false),
                    MaxAge = table.Column<int>(nullable: true)
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
                        .Annotation("MySQL:AutoIncrement", true),
                    FirstName = table.Column<string>(nullable: true),
                    LastName = table.Column<string>(nullable: true),
                    Email = table.Column<string>(nullable: true),
                    PMDC = table.Column<int>(nullable: false),
                    IsApproved = table.Column<int>(nullable: false),
                    ShowPhone = table.Column<int>(nullable: false),
                    ShowMobile = table.Column<int>(nullable: false),
                    PhoneNumber = table.Column<int>(nullable: true),
                    ValidUpto = table.Column<int>(nullable: true),
                    InvoiceNumber = table.Column<int>(nullable: true),
                    ProfileImage = table.Column<int>(nullable: true),
                    SignatureImage = table.Column<string>(nullable: true),
                    DisplayName = table.Column<int>(nullable: false),
                    AllowInvoice = table.Column<int>(nullable: false),
                    AllowChart = table.Column<int>(nullable: false),
                    AllowFollowUp = table.Column<int>(nullable: false),
                    AllowInventory = table.Column<int>(nullable: false),
                    SmsLimit = table.Column<int>(nullable: false),
                    DoctorType = table.Column<string>(nullable: true),
                    Qualification = table.Column<string>(nullable: true),
                    AdditionInfo = table.Column<string>(nullable: true),
                    UserId = table.Column<int>(nullable: false),
                    UserId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Doctors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Doctors_Users_UserId1",
                        column: x => x.UserId1,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    MobileNumber = table.Column<int>(nullable: false),
                    Sms = table.Column<string>(nullable: true),
                    ApiResponse = table.Column<string>(nullable: true),
                    Created = table.Column<string>(nullable: true),
                    UserId = table.Column<int>(nullable: true),
                    UserId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_Users_UserId1",
                        column: x => x.UserId1,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Brands",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Name = table.Column<int>(nullable: true),
                    VaccineId = table.Column<int>(nullable: false),
                    VaccineId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Brands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Brands_Vaccines_VaccineId1",
                        column: x => x.VaccineId1,
                        principalTable: "Vaccines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Doses",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Name = table.Column<string>(nullable: true),
                    MinAge = table.Column<int>(nullable: false),
                    MaxAge = table.Column<int>(nullable: true),
                    MinGap = table.Column<int>(nullable: true),
                    DoseOrder = table.Column<int>(nullable: true),
                    VaccineId = table.Column<int>(nullable: false),
                    VaccineId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Doses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Doses_Vaccines_VaccineId1",
                        column: x => x.VaccineId1,
                        principalTable: "Vaccines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Clinics",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Name = table.Column<string>(nullable: true),
                    ConsultationFee = table.Column<int>(nullable: false),
                    Lat = table.Column<float>(nullable: false),
                    Long = table.Column<float>(nullable: false),
                    PhoneNumber = table.Column<int>(nullable: true),
                    IsOnline = table.Column<int>(nullable: false),
                    Address = table.Column<string>(nullable: true),
                    DoctorId = table.Column<int>(nullable: false),
                    DoctorId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clinics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Clinics_Doctors_DoctorId1",
                        column: x => x.DoctorId1,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BrandAmounts",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Amount = table.Column<int>(nullable: false),
                    BrandId = table.Column<int>(nullable: false),
                    BrandId1 = table.Column<long>(nullable: true),
                    DoctorId = table.Column<int>(nullable: false),
                    DoctorId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandAmounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandAmounts_Brands_BrandId1",
                        column: x => x.BrandId1,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandAmounts_Doctors_DoctorId1",
                        column: x => x.DoctorId1,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BrandInventorys",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Count = table.Column<int>(nullable: false),
                    BrandId = table.Column<int>(nullable: false),
                    BrandId1 = table.Column<long>(nullable: true),
                    DoctorId = table.Column<int>(nullable: false),
                    DoctorId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandInventorys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BrandInventorys_Brands_BrandId1",
                        column: x => x.BrandId1,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BrandInventorys_Doctors_DoctorId1",
                        column: x => x.DoctorId1,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DoctorSchedules",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    DoseId = table.Column<int>(nullable: false),
                    DoseId1 = table.Column<long>(nullable: true),
                    DoctorId = table.Column<int>(nullable: false),
                    DoctorId1 = table.Column<long>(nullable: true),
                    GapInDays = table.Column<int>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DoctorSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DoctorSchedules_Doctors_DoctorId1",
                        column: x => x.DoctorId1,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DoctorSchedules_Doses_DoseId1",
                        column: x => x.DoseId1,
                        principalTable: "Doses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Childs",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    FatherName = table.Column<string>(nullable: true),
                    Email = table.Column<string>(nullable: true),
                    DOB = table.Column<string>(nullable: true),
                    Gender = table.Column<string>(nullable: true),
                    City = table.Column<string>(nullable: true),
                    PreferredDayOfReminder = table.Column<string>(nullable: true),
                    PreferredDayOfWeek = table.Column<string>(nullable: true),
                    PreferredSchedule = table.Column<string>(nullable: true),
                    IsEPIDone = table.Column<int>(nullable: true),
                    IsVerified = table.Column<int>(nullable: true),
                    ClinicId = table.Column<int>(nullable: false),
                    ClinicId1 = table.Column<long>(nullable: true),
                    UserId = table.Column<int>(nullable: false),
                    UserId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Childs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Childs_Clinics_ClinicId1",
                        column: x => x.ClinicId1,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Childs_Users_UserId1",
                        column: x => x.UserId1,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClinicTimings",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Day = table.Column<string>(nullable: true),
                    StartTime = table.Column<string>(nullable: true),
                    EndTime = table.Column<string>(nullable: true),
                    Session = table.Column<string>(nullable: true),
                    IsOpen = table.Column<int>(nullable: false),
                    ClinicId = table.Column<int>(nullable: false),
                    ClinicId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicTimings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicTimings_Clinics_ClinicId1",
                        column: x => x.ClinicId1,
                        principalTable: "Clinics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FollowUps",
                columns: table => new
                {
                    Id = table.Column<long>(nullable: false)
                        .Annotation("MySQL:AutoIncrement", true),
                    Disease = table.Column<string>(nullable: true),
                    CurrentVisitDate = table.Column<string>(nullable: true),
                    NextVisitDate = table.Column<string>(nullable: true),
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
                        .Annotation("MySQL:AutoIncrement", true),
                    Date = table.Column<string>(nullable: true),
                    Weight = table.Column<float>(nullable: true),
                    Height = table.Column<float>(nullable: true),
                    Circle = table.Column<float>(nullable: true),
                    IsDone = table.Column<int>(nullable: false),
                    GivenDate = table.Column<string>(nullable: true),
                    BrandId = table.Column<int>(nullable: true),
                    BrandId1 = table.Column<long>(nullable: true),
                    ChildId = table.Column<long>(nullable: false),
                    DoseId = table.Column<int>(nullable: false),
                    DoseId1 = table.Column<long>(nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Schedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Schedules_Brands_BrandId1",
                        column: x => x.BrandId1,
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
                        name: "FK_Schedules_Doses_DoseId1",
                        column: x => x.DoseId1,
                        principalTable: "Doses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BrandAmounts_BrandId1",
                table: "BrandAmounts",
                column: "BrandId1");

            migrationBuilder.CreateIndex(
                name: "IX_BrandAmounts_DoctorId1",
                table: "BrandAmounts",
                column: "DoctorId1");

            migrationBuilder.CreateIndex(
                name: "IX_BrandInventorys_BrandId1",
                table: "BrandInventorys",
                column: "BrandId1");

            migrationBuilder.CreateIndex(
                name: "IX_BrandInventorys_DoctorId1",
                table: "BrandInventorys",
                column: "DoctorId1");

            migrationBuilder.CreateIndex(
                name: "IX_Brands_VaccineId1",
                table: "Brands",
                column: "VaccineId1");

            migrationBuilder.CreateIndex(
                name: "IX_Childs_ClinicId1",
                table: "Childs",
                column: "ClinicId1");

            migrationBuilder.CreateIndex(
                name: "IX_Childs_UserId1",
                table: "Childs",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_Clinics_DoctorId1",
                table: "Clinics",
                column: "DoctorId1");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicTimings_ClinicId1",
                table: "ClinicTimings",
                column: "ClinicId1");

            migrationBuilder.CreateIndex(
                name: "IX_Doctors_UserId1",
                table: "Doctors",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_DoctorId1",
                table: "DoctorSchedules",
                column: "DoctorId1");

            migrationBuilder.CreateIndex(
                name: "IX_DoctorSchedules_DoseId1",
                table: "DoctorSchedules",
                column: "DoseId1");

            migrationBuilder.CreateIndex(
                name: "IX_Doses_VaccineId1",
                table: "Doses",
                column: "VaccineId1");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_ChildId",
                table: "FollowUps",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUps_DoctorId",
                table: "FollowUps",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_UserId1",
                table: "Messages",
                column: "UserId1");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_BrandId1",
                table: "Schedules",
                column: "BrandId1");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_ChildId",
                table: "Schedules",
                column: "ChildId");

            migrationBuilder.CreateIndex(
                name: "IX_Schedules_DoseId1",
                table: "Schedules",
                column: "DoseId1");
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
