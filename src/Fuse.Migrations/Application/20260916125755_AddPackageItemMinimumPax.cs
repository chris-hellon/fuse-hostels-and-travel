using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddPackageItemMinimumPax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinimumPax",
                schema: "Catalog",
                table: "PackageItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinimumPaxScope",
                schema: "Catalog",
                table: "PackageItems",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinimumPax",
                schema: "Catalog",
                table: "PackageItems");

            migrationBuilder.DropColumn(
                name: "MinimumPaxScope",
                schema: "Catalog",
                table: "PackageItems");
        }
    }
}
