using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddEventTour : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TourId",
                schema: "Catalog",
                table: "Events",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Events_TourId",
                schema: "Catalog",
                table: "Events",
                column: "TourId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_Tours_TourId",
                schema: "Catalog",
                table: "Events",
                column: "TourId",
                principalSchema: "Catalog",
                principalTable: "Tours",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Tours_TourId",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_TourId",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "TourId",
                schema: "Catalog",
                table: "Events");
        }
    }
}
