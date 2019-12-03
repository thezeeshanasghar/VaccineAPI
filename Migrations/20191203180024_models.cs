using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VaccineAPI.Migrations
{
    public partial class models : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FromDate",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "ToDate",
                table: "Schedules");

            migrationBuilder.AlterColumn<int>(
                name: "PreferredDayOfReminder",
                table: "Childs",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FromDate",
                table: "Schedules",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "ToDate",
                table: "Schedules",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AlterColumn<string>(
                name: "PreferredDayOfReminder",
                table: "Childs",
                nullable: true,
                oldClrType: typeof(int));
        }
    }
}
