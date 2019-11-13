using Microsoft.EntityFrameworkCore.Migrations;

namespace VaccineAPI.Migrations
{
    public partial class updated : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Doctors");

            migrationBuilder.RenameColumn(
                name: "AdditionInfo",
                table: "Doctors",
                newName: "PhoneNo");

            migrationBuilder.AlterColumn<string>(
                name: "Password",
                table: "Users",
                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AlterColumn<string>(
                name: "ValidUpto",
                table: "Doctors",
                nullable: true,
                oldClrType: typeof(int),
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PMDC",
                table: "Doctors",
                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AlterColumn<string>(
                name: "DisplayName",
                table: "Doctors",
                nullable: true,
                oldClrType: typeof(int));

            migrationBuilder.AddColumn<string>(
                name: "AdditionalInfo",
                table: "Doctors",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndTime",
                table: "Clinics",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OffDays",
                table: "Clinics",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StartTime",
                table: "Clinics",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "Childs",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Brands",
                nullable: true,
                oldClrType: typeof(int),
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdditionalInfo",
                table: "Doctors");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "OffDays",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "Clinics");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "Childs");

            migrationBuilder.RenameColumn(
                name: "PhoneNo",
                table: "Doctors",
                newName: "AdditionInfo");

            migrationBuilder.AlterColumn<int>(
                name: "Password",
                table: "Users",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ValidUpto",
                table: "Doctors",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PMDC",
                table: "Doctors",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "DisplayName",
                table: "Doctors",
                nullable: false,
                oldClrType: typeof(string),
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhoneNumber",
                table: "Doctors",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "Name",
                table: "Brands",
                nullable: true,
                oldClrType: typeof(string),
                oldNullable: true);
        }
    }
}
