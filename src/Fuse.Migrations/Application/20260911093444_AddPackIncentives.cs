using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddPackIncentives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PackIncentiveId",
                schema: "Catalog",
                table: "Bookings",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackIncentives",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MinimumProductCount = table.Column<int>(type: "int", nullable: false),
                    CommissionAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IgnoresMinimumPax = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_PackIncentives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackIncentiveTours",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackIncentiveId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TourId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_PackIncentiveTours", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackIncentiveTours_PackIncentives_PackIncentiveId",
                        column: x => x.PackIncentiveId,
                        principalSchema: "Catalog",
                        principalTable: "PackIncentives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackIncentiveTours_Tours_TourId",
                        column: x => x.TourId,
                        principalSchema: "Catalog",
                        principalTable: "Tours",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_PackIncentiveId",
                schema: "Catalog",
                table: "Bookings",
                column: "PackIncentiveId");

            migrationBuilder.CreateIndex(
                name: "IX_PackIncentiveTours_PackIncentiveId",
                schema: "Catalog",
                table: "PackIncentiveTours",
                column: "PackIncentiveId");

            migrationBuilder.CreateIndex(
                name: "IX_PackIncentiveTours_TourId",
                schema: "Catalog",
                table: "PackIncentiveTours",
                column: "TourId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_PackIncentives_PackIncentiveId",
                schema: "Catalog",
                table: "Bookings",
                column: "PackIncentiveId",
                principalSchema: "Catalog",
                principalTable: "PackIncentives",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_PackIncentives_PackIncentiveId",
                schema: "Catalog",
                table: "Bookings");

            migrationBuilder.DropTable(
                name: "PackIncentiveTours",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "PackIncentives",
                schema: "Catalog");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_PackIncentiveId",
                schema: "Catalog",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "PackIncentiveId",
                schema: "Catalog",
                table: "Bookings");
        }
    }
}
