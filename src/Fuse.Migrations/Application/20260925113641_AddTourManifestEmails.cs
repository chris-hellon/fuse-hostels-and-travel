using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddTourManifestEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TourManifestEmails",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TourId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Guests = table.Column<int>(type: "int", nullable: false),
                    Recipients = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SentOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TourManifestEmails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TourManifestEmails_TourId_StartDate",
                schema: "Catalog",
                table: "TourManifestEmails",
                columns: new[] { "TourId", "StartDate" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TourManifestEmails",
                schema: "Catalog");
        }
    }
}
