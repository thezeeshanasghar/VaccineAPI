using Microsoft.EntityFrameworkCore.Migrations;

namespace VaccineAPI.Migrations
{
    public partial class user : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CountryCode", "MobileNumber", "Password", "UserType" },
                values: new object[] { 1L, "92", "3331231231", "1234", "SUPERADMIN" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1L);
        }
    }
}
