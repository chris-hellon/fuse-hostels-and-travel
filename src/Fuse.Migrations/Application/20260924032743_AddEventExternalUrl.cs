using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Fuse.Migrations.Application
{
    /// <inheritdoc />
    public partial class AddEventExternalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalUrl",
                schema: "Catalog",
                table: "Events",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalUrl",
                schema: "Catalog",
                table: "Events");
        }
    }
}
