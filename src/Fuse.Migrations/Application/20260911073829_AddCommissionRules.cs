using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddCommissionRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExcludeFromStaffSales",
                schema: "Catalog",
                table: "Tours",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommissionMinimumPax",
                schema: "Catalog",
                table: "TourPrices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommissionMinimumPaxScope",
                schema: "Catalog",
                table: "TourPrices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                schema: "Catalog",
                table: "Packages",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommissionIgnoresMinimumPax",
                schema: "Catalog",
                table: "Packages",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CommissionAmount",
                schema: "Catalog",
                table: "BookingPackages",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "CommissionIgnoresMinimumPax",
                schema: "Catalog",
                table: "BookingPackages",
                type: "bit",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcludeFromStaffSales",
                schema: "Catalog",
                table: "Tours");

            migrationBuilder.DropColumn(
                name: "CommissionMinimumPax",
                schema: "Catalog",
                table: "TourPrices");

            migrationBuilder.DropColumn(
                name: "CommissionMinimumPaxScope",
                schema: "Catalog",
                table: "TourPrices");

            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                schema: "Catalog",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "CommissionIgnoresMinimumPax",
                schema: "Catalog",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "CommissionAmount",
                schema: "Catalog",
                table: "BookingPackages");

            migrationBuilder.DropColumn(
                name: "CommissionIgnoresMinimumPax",
                schema: "Catalog",
                table: "BookingPackages");
        }
    }
}
