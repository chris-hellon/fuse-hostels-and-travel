using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddPackageTiers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Price",
                schema: "Catalog",
                table: "PackageItems");

            migrationBuilder.AddColumn<string>(
                name: "TierTitle",
                schema: "Catalog",
                table: "BookingPackages",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackageTiers",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
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
                    table.PrimaryKey("PK_PackageTiers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageTiers_Packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "Catalog",
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackageItemPrices",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageTierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_PackageItemPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageItemPrices_PackageItems_PackageItemId",
                        column: x => x.PackageItemId,
                        principalSchema: "Catalog",
                        principalTable: "PackageItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackageItemPrices_PackageTiers_PackageTierId",
                        column: x => x.PackageTierId,
                        principalSchema: "Catalog",
                        principalTable: "PackageTiers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageItemPrices_PackageItemId",
                schema: "Catalog",
                table: "PackageItemPrices",
                column: "PackageItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageItemPrices_PackageTierId",
                schema: "Catalog",
                table: "PackageItemPrices",
                column: "PackageTierId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageTiers_PackageId",
                schema: "Catalog",
                table: "PackageTiers",
                column: "PackageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageItemPrices",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "PackageTiers",
                schema: "Catalog");

            migrationBuilder.DropColumn(
                name: "TierTitle",
                schema: "Catalog",
                table: "BookingPackages");

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                schema: "Catalog",
                table: "PackageItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
