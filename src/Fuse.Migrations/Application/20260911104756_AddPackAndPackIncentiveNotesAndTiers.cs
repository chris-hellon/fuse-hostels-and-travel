using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddPackAndPackIncentiveNotesAndTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "Catalog",
                table: "PackIncentives",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                schema: "Catalog",
                table: "Packages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackIncentiveTiers",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackIncentiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LastModifiedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackIncentiveTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackIncentiveTiers_PackIncentives_PackIncentiveId",
                        column: x => x.PackIncentiveId,
                        principalSchema: "Catalog",
                        principalTable: "PackIncentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackIncentiveTiers_PackIncentiveId",
                schema: "Catalog",
                table: "PackIncentiveTiers",
                column: "PackIncentiveId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackIncentiveTiers",
                schema: "Catalog");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "Catalog",
                table: "PackIncentives");

            migrationBuilder.DropColumn(
                name: "Notes",
                schema: "Catalog",
                table: "Packages");
        }
    }
}
