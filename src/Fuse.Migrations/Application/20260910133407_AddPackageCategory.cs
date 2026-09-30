using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddPackageCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TourCategoryId",
                schema: "Catalog",
                table: "Packages",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Packages_TourCategoryId",
                schema: "Catalog",
                table: "Packages",
                column: "TourCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Packages_TourCategories_TourCategoryId",
                schema: "Catalog",
                table: "Packages",
                column: "TourCategoryId",
                principalSchema: "Catalog",
                principalTable: "TourCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Packages_TourCategories_TourCategoryId",
                schema: "Catalog",
                table: "Packages");

            migrationBuilder.DropIndex(
                name: "IX_Packages_TourCategoryId",
                schema: "Catalog",
                table: "Packages");

            migrationBuilder.DropColumn(
                name: "TourCategoryId",
                schema: "Catalog",
                table: "Packages");
        }
    }
}
