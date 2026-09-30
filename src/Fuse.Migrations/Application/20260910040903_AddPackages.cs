using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BookingPackageId",
                schema: "Catalog",
                table: "BookingItems",
                type: "uniqueidentifier",
                nullable: true);

            // The BookingPackage entity only ever existed in the EF model (via its configuration);
            // no table was ever created for it, so create BookingPackages fresh rather than renaming.
            migrationBuilder.CreateTable(
                name: "BookingPackages",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BookingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageName = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    PackagePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
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
                    table.PrimaryKey("PK_BookingPackages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Packages",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShortDescription = table.Column<string>(type: "nvarchar(3000)", maxLength: 3000, nullable: true),
                    ImagePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    ThumbnailImagePath = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    PriceLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PublishToSite = table.Column<bool>(type: "bit", nullable: true),
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
                    table.PrimaryKey("PK_Packages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PackageItems",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PackageId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TourId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    TourPriceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
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
                    table.PrimaryKey("PK_PackageItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackageItems_Packages_PackageId",
                        column: x => x.PackageId,
                        principalSchema: "Catalog",
                        principalTable: "Packages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackageItems_Tours_TourId",
                        column: x => x.TourId,
                        principalSchema: "Catalog",
                        principalTable: "Tours",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookingItems_BookingPackageId",
                schema: "Catalog",
                table: "BookingItems",
                column: "BookingPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_BookingPackages_BookingId",
                schema: "Catalog",
                table: "BookingPackages",
                column: "BookingId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageItems_PackageId",
                schema: "Catalog",
                table: "PackageItems",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PackageItems_TourId",
                schema: "Catalog",
                table: "PackageItems",
                column: "TourId");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingItems_BookingPackages_BookingPackageId",
                schema: "Catalog",
                table: "BookingItems",
                column: "BookingPackageId",
                principalSchema: "Catalog",
                principalTable: "BookingPackages",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_BookingPackages_Bookings_BookingId",
                schema: "Catalog",
                table: "BookingPackages",
                column: "BookingId",
                principalSchema: "Catalog",
                principalTable: "Bookings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BookingItems_BookingPackages_BookingPackageId",
                schema: "Catalog",
                table: "BookingItems");

            migrationBuilder.DropTable(
                name: "PackageItems",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "Packages",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "BookingPackages",
                schema: "Catalog");

            migrationBuilder.DropIndex(
                name: "IX_BookingItems_BookingPackageId",
                schema: "Catalog",
                table: "BookingItems");

            migrationBuilder.DropColumn(
                name: "BookingPackageId",
                schema: "Catalog",
                table: "BookingItems");
        }
    }
}
