using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class ExpandEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AllDay",
                schema: "Catalog",
                table: "Events",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "AllProperties",
                schema: "Catalog",
                table: "Events",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndDate",
                schema: "Catalog",
                table: "Events",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EventCategoryId",
                schema: "Catalog",
                table: "Events",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFeatured",
                schema: "Catalog",
                table: "Events",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PublishToSite",
                schema: "Catalog",
                table: "Events",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceDaysOfWeek",
                schema: "Catalog",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecurrenceEndDate",
                schema: "Catalog",
                table: "Events",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceInterval",
                schema: "Catalog",
                table: "Events",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecurrenceType",
                schema: "Catalog",
                table: "Events",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "StartDate",
                schema: "Catalog",
                table: "Events",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateTable(
                name: "EventCategories",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                    table.PrimaryKey("PK_EventCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventOccurrenceExceptions",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurrenceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventOccurrenceExceptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventOccurrenceExceptions_Events_EventId",
                        column: x => x.EventId,
                        principalSchema: "Catalog",
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventPropertyLookups",
                schema: "Catalog",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventPropertyLookups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventPropertyLookups_Events_EventId",
                        column: x => x.EventId,
                        principalSchema: "Catalog",
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventPropertyLookups_Properties_PropertyId",
                        column: x => x.PropertyId,
                        principalSchema: "Catalog",
                        principalTable: "Properties",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_EventCategoryId",
                schema: "Catalog",
                table: "Events",
                column: "EventCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_EventOccurrenceExceptions_EventId",
                schema: "Catalog",
                table: "EventOccurrenceExceptions",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventPropertyLookups_EventId",
                schema: "Catalog",
                table: "EventPropertyLookups",
                column: "EventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventPropertyLookups_PropertyId",
                schema: "Catalog",
                table: "EventPropertyLookups",
                column: "PropertyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Events_EventCategories_EventCategoryId",
                schema: "Catalog",
                table: "Events",
                column: "EventCategoryId",
                principalSchema: "Catalog",
                principalTable: "EventCategories",
                principalColumn: "Id");

            // Backfill existing events: give them a real StartDate (they had no date), keep them publicly
            // visible (they showed on the site before), and move their single PropertyId into the new lookup.
            migrationBuilder.Sql(
                "UPDATE [Catalog].[Events] SET [StartDate] = [CreatedOn] WHERE [StartDate] <= '1900-01-01';");
            migrationBuilder.Sql(
                "UPDATE [Catalog].[Events] SET [PublishToSite] = 1 WHERE [PublishToSite] IS NULL;");
            migrationBuilder.Sql(
                "INSERT INTO [Catalog].[EventPropertyLookups] ([Id],[EventId],[PropertyId],[TenantId]) " +
                "SELECT NEWID(), [Id], [PropertyId], [TenantId] FROM [Catalog].[Events] WHERE [PropertyId] IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_EventCategories_EventCategoryId",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropTable(
                name: "EventCategories",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "EventOccurrenceExceptions",
                schema: "Catalog");

            migrationBuilder.DropTable(
                name: "EventPropertyLookups",
                schema: "Catalog");

            migrationBuilder.DropIndex(
                name: "IX_Events_EventCategoryId",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AllDay",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "AllProperties",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "EndDate",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "EventCategoryId",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "IsFeatured",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PublishToSite",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceDaysOfWeek",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceEndDate",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceInterval",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "RecurrenceType",
                schema: "Catalog",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "StartDate",
                schema: "Catalog",
                table: "Events");
        }
    }
}
